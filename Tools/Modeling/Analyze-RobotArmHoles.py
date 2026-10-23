"""Inspect RobotArm mesh boundaries without changing the source assets.

Run with Blender: blender --background Robot\ arm_TopologyClean.blend
    --python Analyze-RobotArmHoles.py -- output-directory [--render]
"""

import json
import sys
from collections import defaultdict
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


output = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
render = "--render" in sys.argv[sys.argv.index("--") + 2:]
output.mkdir(parents=True, exist_ok=True)


def connected_boundaries(mesh):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    edge_indices = [edge.index for edge in bm.edges if len(edge.link_faces) == 1]
    vertex_edges = defaultdict(list)
    for index in edge_indices:
        edge = bm.edges[index]
        for vertex in edge.verts:
            vertex_edges[vertex.index].append(index)
    components = []
    seen = set()
    for index in edge_indices:
        if index in seen:
            continue
        pending = [index]
        seen.add(index)
        component = []
        while pending:
            current = pending.pop()
            component.append(current)
            for vertex in bm.edges[current].verts:
                for neighbor in vertex_edges[vertex.index]:
                    if neighbor not in seen:
                        seen.add(neighbor)
                        pending.append(neighbor)
        components.append(component)
    summaries = []
    for component in components:
        vertices = {vertex.index for index in component for vertex in bm.edges[index].verts}
        degrees = {vertex: sum(index in component for index in vertex_edges[vertex]) for vertex in vertices}
        coords = [bm.verts[index].co.copy() for index in vertices]
        centroid = sum(coords, Vector()) / len(coords)
        perimeter = sum((edge.verts[0].co - edge.verts[1].co).length
                        for edge in (bm.edges[index] for index in component))
        minimum = tuple(min(coord[axis] for coord in coords) for axis in range(3))
        maximum = tuple(max(coord[axis] for coord in coords) for axis in range(3))
        summaries.append({
            "edges": len(component),
            "vertices": len(vertices),
            "closed_loop": len(component) == len(vertices) and all(degree == 2 for degree in degrees.values()),
            "perimeter_local": round(perimeter, 6),
            "center_local": tuple(round(value, 6) for value in centroid),
            "minimum_local": tuple(round(value, 6) for value in minimum),
            "maximum_local": tuple(round(value, 6) for value in maximum),
            "edge_vertex_ids": [(bm.edges[index].verts[0].index, bm.edges[index].verts[1].index)
                                for index in component],
        })
    nonmanifold = sum(len(edge.link_faces) > 2 for edge in bm.edges)
    wire = sum(len(edge.link_faces) == 0 for edge in bm.edges)
    bm.free()
    return summaries, len(edge_indices), nonmanifold, wire


def boundary_overlay(objects, summaries):
    material = bpy.data.materials.new("Boundary: open mesh edge")
    material.diffuse_color = (1.0, 0.02, 0.01, 1)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (1.0, 0.015, 0.005, 1)
    emission.inputs["Strength"].default_value = 3.0
    output_node = nodes.new("ShaderNodeOutputMaterial")
    material.node_tree.links.new(emission.outputs[0], output_node.inputs["Surface"])
    for obj, components in zip(objects, summaries):
        curve = bpy.data.curves.new(f"Open edges: {obj.name}", "CURVE")
        curve.dimensions = "3D"
        curve.bevel_depth = 0.003
        curve.resolution_u = 1
        curve.bevel_resolution = 2
        for component in components:
            for start, end in component["edge_vertex_ids"]:
                spline = curve.splines.new("POLY")
                spline.points.add(1)
                for point, index in zip(spline.points, (start, end)):
                    point.co = (*obj.matrix_world @ obj.data.vertices[index].co, 1)
        curve.materials.append(material)
        overlay = bpy.data.objects.new(curve.name, curve)
        bpy.context.scene.collection.objects.link(overlay)


def preview(label, camera_position):
    scene = bpy.context.scene
    target = Vector((0, 0.17, 0.59))
    camera_data = bpy.data.cameras.new(f"Hole check {label}")
    camera = bpy.data.objects.new(camera_data.name, camera_data)
    scene.collection.objects.link(camera)
    camera.location = Vector(camera_position)
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 1.6
    scene.camera = camera

    lamp_data = bpy.data.lights.new(f"Hole check light {label}", "AREA")
    lamp = bpy.data.objects.new(lamp_data.name, lamp_data)
    scene.collection.objects.link(lamp)
    lamp.location = Vector((camera_position[0] * 0.8, camera_position[1] * 0.8, 4))
    lamp.rotation_euler = (target - lamp.location).to_track_quat("-Z", "Y").to_euler()
    lamp_data.energy = 250
    lamp_data.shape = "DISK"
    lamp_data.size = 4

    world = bpy.data.worlds.new(f"Hole check world {label}")
    world.use_nodes = True
    world.node_tree.nodes.get("Background").inputs["Color"].default_value = (0.4, 0.4, 0.4, 1)
    world.node_tree.nodes.get("Background").inputs["Strength"].default_value = 0.8
    scene.world = world
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 16
    scene.render.resolution_x = 512
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(output / f"{label}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(camera, do_unlink=True)
    bpy.data.objects.remove(lamp, do_unlink=True)


mesh_objects = [obj for obj in bpy.data.objects if obj.type == "MESH" and not obj.hide_render]
unique = {}
for obj in mesh_objects:
    unique.setdefault(obj.data.name, obj)
surface_trees = {}
for obj in mesh_objects:
    points = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    polygons = [tuple(polygon.vertices) for polygon in obj.data.polygons]
    surface_trees[obj.name] = BVHTree.FromPolygons(points, polygons)
report = []
boundary_by_mesh = {}
for obj in unique.values():
    components, edge_count, nonmanifold, wire = connected_boundaries(obj.data)
    for component in components:
        center = obj.matrix_world @ Vector(component["center_local"])
        component["center_world"] = tuple(round(value, 6) for value in center)
        distances = []
        for start, end in component["edge_vertex_ids"]:
            midpoint = obj.matrix_world @ ((obj.data.vertices[start].co + obj.data.vertices[end].co) / 2)
            nearest = min(tree.find_nearest(midpoint)[3] for name, tree in surface_trees.items()
                          if name != obj.name)
            distances.append(nearest)
        distances.sort()
        component["nearest_other_surface_median"] = round(distances[len(distances) // 2], 6)
        component["nearest_other_surface_max"] = round(distances[-1], 6)
    report.append({
        "object": obj.name,
        "mesh": obj.data.name,
        "instances": [instance.name for instance in mesh_objects if instance.data == obj.data],
        "vertices": len(obj.data.vertices),
        "faces": len(obj.data.polygons),
        "boundary_edges": edge_count,
        "boundary_components": len(components),
        "closed_boundary_loops": sum(item["closed_loop"] for item in components),
        "nonmanifold_edges": nonmanifold,
        "wire_edges": wire,
        "components": [{key: value for key, value in component.items() if key != "edge_vertex_ids"}
                       for component in sorted(components, key=lambda item: -item["perimeter_local"])],
    })
    boundary_by_mesh[obj.data.name] = components
    print(f"HOLE_REPORT {obj.data.name}: {edge_count} boundary edges, "
          f"{len(components)} components, {nonmanifold} nonmanifold edges")

(output / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
if render:
    boundary_overlay(mesh_objects, [boundary_by_mesh[obj.data.name] for obj in mesh_objects])
    preview("front", (2.3, -3.1, 2.5))
    preview("back", (-2.3, 3.1, 2.5))
    preview("top", (0.1, 0.17, 4.0))
