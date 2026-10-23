"""Retopologize and close the Long Robot arm's visible-size meshes.

Keeps the original ASCII FBX hierarchy and IDs. The ten FBX geometries share
one material, so their new UV islands are packed and baked into one atlas.
The isolated one-triangle Group_07 marker is deliberately left untouched.
"""

import importlib.util
import json
import math
import re
import shutil
import sys
from pathlib import Path
from xml.sax.saxutils import escape

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from RobotArmNormals import rebuild_normals
from uv_atlas import overlapping_faces
from LongRobotArmConfig import ATLAS_PARTS


ROOT = Path(__file__).resolve().parents[2]
ASSET = ROOT / "FactorioProject/Assets/MapObject/InputOutputModule/Long Robot arm"
OUTPUT = ROOT / "ArtSource/LongRobotArm"
BACKUP = OUTPUT / "Backups"
FBX_NAME = "Long Robot arm_Split_Split.fbx"
TEXTURE_NAME = "Long Robot arm_TB.png"
SKIP = {"Group_07_FFFFFF"}


def import_script(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


fbx = import_script("robot_arm_fbx", "Cleanup-RobotArmTopology.py")
holes = import_script("robot_arm_holes", "Fill-RobotArmHoles.py")


def geometry_blocks(source):
    models = {identifier: re.search(r'"Model::([^"\n]+)"', block)[1]
              for identifier, _, _, block in fbx.sections(source, "Model")}
    links = {int(a): int(b) for a, b in re.findall(r'C: "OO",(\d+),(\d+)', source)}
    for identifier, start, end, block in fbx.sections(source, "Geometry"):
        yield models[links[identifier]], start, end, block


def load_source_mesh(name, block, image):
    vertices = [float(value) for value in fbx.array(block, "Vertices")]
    indices = fbx.array(block, "PolygonVertexIndex")
    faces = fbx.faces_from_indices(indices, len(vertices) // 3)
    mesh = bpy.data.meshes.new(name + "_Source")
    mesh.from_pydata([Vector(vertices[i:i + 3]) * 0.01
                      for i in range(0, len(vertices), 3)], [], [face for face, _ in faces])
    mesh.update()
    uv = [float(value) for value in fbx.array(block, "UV")]
    uv_indices = [int(value) for value in fbx.array(block, "UVIndex")]
    assert len(mesh.loops) == len(uv_indices)
    layer = mesh.uv_layers.new(name="UV_Original")
    for loop, index in zip(layer.data, uv_indices):
        loop.uv = uv[2 * index:2 * index + 2]

    material = bpy.data.materials.new(name + "_Source")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    emit = nodes.new("ShaderNodeEmission")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = image
    uv_node = nodes.new("ShaderNodeUVMap")
    uv_node.uv_map = "UV_Original"
    links.new(uv_node.outputs["UV"], tex.inputs["Vector"])
    links.new(tex.outputs["Color"], emit.inputs["Color"])
    links.new(emit.outputs[0], out.inputs["Surface"])
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name + "_Source", mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def retopologize(source):
    target = source.copy()
    target.data = source.data.copy()
    target.name = source.name.removesuffix("_Source")
    target.data.name = target.name
    bpy.context.collection.objects.link(target)
    bm = bmesh.new()
    bm.from_mesh(target.data)
    extent = max(source.dimensions)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=extent * 1e-7)
    branching = [edge for edge in bm.edges if len(edge.link_faces) > 2]
    if branching:
        bmesh.ops.split_edges(bm, edges=branching)
    planar = [edge for edge in bm.edges
              if edge.is_manifold and edge.calc_face_angle(0) < math.radians(0.15)]
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(0.15),
                              use_dissolve_boundaries=False, verts=[], edges=planar)
    bmesh.ops.triangulate(bm, faces=[face for face in bm.faces if len(face.verts) > 4])
    bmesh.ops.join_triangles(bm, faces=[face for face in bm.faces if len(face.verts) == 3],
                             angle_face_threshold=math.radians(12),
                             angle_shape_threshold=math.radians(70), cmp_uvs=False,
                             cmp_vcols=False, cmp_seam=False, cmp_sharp=False,
                             cmp_materials=True)
    branching = [edge for edge in bm.edges if len(edge.link_faces) > 2]
    if branching:
        bmesh.ops.split_edges(bm, edges=branching)
    scraps = []
    seen = set()
    for vertex in bm.verts:
        if vertex in seen:
            continue
        part = [vertex]
        seen.add(vertex)
        for current in part:
            for edge in current.link_edges:
                neighbor = edge.other_vert(current)
                if neighbor not in seen:
                    seen.add(neighbor)
                    part.append(neighbor)
        if len(part) <= 4:
            scraps.extend(part)
    if scraps:
        bmesh.ops.delete(bm, geom=scraps, context="VERTS")
    bm.to_mesh(target.data)
    bm.free()
    target.data.update()
    target.data.uv_layers.remove(target.data.uv_layers["UV_Original"])
    target.data.uv_layers.new(name="UV")
    probe = bmesh.new()
    probe.from_mesh(target.data)
    boundary = [edge for edge in probe.edges if edge.is_boundary]
    if boundary:
        boundary_before = len(boundary)
        cap_count = 0
        for _ in range(4):
            if not boundary:
                break
            filled = bmesh.ops.holes_fill(probe, edges=boundary, sides=0)["faces"]
            cap_count += len(filled)
            remaining = [edge for edge in probe.edges if edge.is_boundary]
            if len(remaining) >= len(boundary):
                break
            boundary = remaining
        for edges, adjacency in holes.boundary_components(probe):
            for path in holes.boundary_cycles(edges, adjacency):
                start_edge = probe.edges.get((path[0], path[1]))
                adjacent = start_edge.link_faces[0]
                center = sum((vertex.co for vertex in path), Vector()) / len(path)
                pole = probe.verts.new(center + adjacent.normal * 0.0001)
                for first, second in zip(path, path[1:] + path[:1]):
                    probe.faces.new((first, second, pole))
                    cap_count += 1
        large_caps = [face for face in probe.faces if len(face.verts) > 4]
        if large_caps:
            bmesh.ops.poke(probe, faces=large_caps)
        probe.normal_update()
        hole_report = {
            "mesh": target.name,
            "boundary_edges_before": boundary_before,
            "boundary_edges_after": sum(edge.is_boundary for edge in probe.edges),
            "cap_faces": cap_count,
            "nonmanifold_edges_after": sum(len(edge.link_faces) > 2 for edge in probe.edges),
            "degenerate_faces_after": sum(face.calc_area() < 1e-10 for face in probe.faces),
        }
        print("HOLE_FILL", json.dumps(hole_report), flush=True)
        if hole_report["boundary_edges_after"] or hole_report["nonmanifold_edges_after"] or hole_report["degenerate_faces_after"]:
            raise ValueError(f"Incomplete hole repair: {hole_report}")
        probe.to_mesh(target.data)
        target.data.update()
    else:
        hole_report = {"mesh": target.name, "boundary_edges_before": 0,
                       "boundary_edges_after": 0, "cap_faces": 0,
                       "nonmanifold_edges_after": 0, "degenerate_faces_after": 0}
    probe.free()
    rebuild_normals(target)
    return target, {"scrap_vertices_removed": len(scraps), **hole_report}


def unwrap_atlas(targets, repairs=None, start_index=0):
    if repairs is None:
        repairs = {}
    bpy.ops.object.select_all(action="DESELECT")
    for index in range(start_index, len(targets)):
        target = targets[index]
        bpy.ops.object.select_all(action="DESELECT")
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.012)
        bpy.ops.uv.average_islands_scale()
        bpy.ops.uv.select_all(action="SELECT")
        bpy.ops.uv.pack_islands(udim_source="ACTIVE_UDIM", rotate=True,
                                margin_method="FRACTION", margin=0.015)
        bpy.ops.object.mode_set(mode="OBJECT")
        for _ in range(3):
            layer = target.data.uv_layers.active
            own_overlaps = overlapping_faces(target.data, layer)
            if not own_overlaps:
                break
            print("UV_REPACK", target.name, len(own_overlaps), flush=True)
            for offset, face_id in enumerate(sorted(own_overlaps)):
                for loop_id in target.data.polygons[face_id].loop_indices:
                    layer.data[loop_id].uv.x += 2 * (offset + 1)
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.select_all(action="SELECT")
            bpy.ops.uv.pack_islands(udim_source="ACTIVE_UDIM", rotate=True,
                                    margin_method="FRACTION", margin=0.015)
            bpy.ops.object.mode_set(mode="OBJECT")
        remaining = sorted(overlapping_faces(target.data, target.data.uv_layers.active))
        if remaining:
            layer = target.data.uv_layers.active
            for loop in layer.data:
                loop.uv.x *= 0.78
            columns = max(1, math.ceil(math.sqrt(len(remaining) * 0.2)))
            rows = math.ceil(len(remaining) / columns)
            for order, face_id in enumerate(remaining):
                face = target.data.polygons[face_id]
                axes = sorted(range(3), key=lambda axis: abs(face.normal[axis]))[:2]
                points = [target.data.vertices[target.data.loops[i].vertex_index].co
                          for i in face.loop_indices]
                lo = [min(point[axis] for point in points) for axis in axes]
                hi = [max(point[axis] for point in points) for axis in axes]
                for loop_id, point in zip(face.loop_indices, points):
                    u = (point[axes[0]] - lo[0]) / max(hi[0] - lo[0], 1e-9)
                    v = (point[axes[1]] - lo[1]) / max(hi[1] - lo[1], 1e-9)
                    layer.data[loop_id].uv = (0.8 + 0.2 * ((order % columns) + 0.05 + 0.9 * u) / columns,
                                              ((order // columns) + 0.05 + 0.9 * v) / rows)
        bad_final = overlapping_faces(target.data, target.data.uv_layers.active)
        if bad_final:
            quads = [i for i in bad_final if len(target.data.polygons[i].vertices) == 4]
            if quads and repairs.get(target.name, 0) < 2:
                print("UV_TRIANGULATE", target.name, quads, flush=True)
                bm = bmesh.new()
                bm.from_mesh(target.data)
                bm.faces.ensure_lookup_table()
                bmesh.ops.triangulate(bm, faces=[bm.faces[i] for i in quads])
                bm.to_mesh(target.data)
                bm.free()
                target.data.update()
                print("UV_TRIANGULATED", target.name, len(target.data.polygons),
                      len(target.data.polygons[quads[0]].vertices), flush=True)
                rebuild_normals(target)
                repairs[target.name] = repairs.get(target.name, 0) + 1
                return unwrap_atlas(targets, repairs, index)
            print("UV_BAD_FINAL", target.name,
                  [(i, len(target.data.polygons[i].vertices)) for i in sorted(bad_final)], flush=True)
            raise ValueError(f"Overlapping UV faces remain in {target.name}")
        cell_x, cell_y = index % 3, 2 - index // 3
        for loop in target.data.uv_layers.active.data:
            if any(not math.isfinite(value) or value < -1e-5 or value > 1.00001 for value in loop.uv):
                raise ValueError(f"UV outside atlas: {target.name}")
            loop.uv = ((loop.uv.x + cell_x) / 3, (loop.uv.y + cell_y) / 3)
    copies = [target.copy() for target in targets]
    for copy in copies:
        copy.data = copy.data.copy()
        bpy.context.collection.objects.link(copy)
    bpy.ops.object.select_all(action="DESELECT")
    for copy in copies:
        copy.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    bpy.ops.object.join()
    combined = copies[0]
    overlapping = overlapping_faces(combined.data, combined.data.uv_layers.active)
    bpy.data.objects.remove(combined, do_unlink=True)
    if overlapping:
        raise ValueError(f"Overlapping UV faces across Long arm meshes: {len(overlapping)}")


def bake_texture(pairs):
    atlas = bpy.data.images.new(TEXTURE_NAME + "_Rebaked", width=2048, height=2048, alpha=True)
    atlas.generated_color = (0, 0, 0, 1)
    atlas.colorspace_settings.name = "sRGB"
    material = bpy.data.materials.new("Long Robot arm_Rebaked")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = atlas
    nodes.active = tex
    links.new(emission.outputs[0], output.inputs["Surface"])
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    scene.render.bake.use_selected_to_active = True
    scene.render.bake.margin = 4
    scene.render.bake.margin_type = "EXTEND"
    scene.render.bake.use_clear = True
    for source, target in pairs:
        target.data.materials.clear()
        target.data.materials.append(material)
        bpy.ops.object.select_all(action="DESELECT")
        source.select_set(True)
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        extent = max(source.dimensions)
        scene.render.bake.cage_extrusion = extent * 0.006
        scene.render.bake.max_ray_distance = extent * 0.02
        bpy.ops.object.bake(type="EMIT")
        scene.render.bake.use_clear = False
    path = OUTPUT / TEXTURE_NAME
    atlas.filepath_raw = str(path)
    atlas.file_format = "PNG"
    atlas.save()
    atlas.pack()
    nodes.remove(emission)
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    principled.inputs["Roughness"].default_value = 1.0
    links.new(tex.outputs["Color"], principled.inputs["Base Color"])
    links.new(principled.outputs[0], output.inputs["Surface"])
    for source, _ in pairs:
        bpy.data.objects.remove(source, do_unlink=True)
    return path


def write_atlas_guide():
    size = 2048
    cell = size / 3
    rows = [
        '<svg xmlns="http://www.w3.org/2000/svg" width="2048" height="2048" viewBox="0 0 2048 2048">',
        f'<image href="{escape(TEXTURE_NAME)}" width="{size}" height="{size}"/>',
    ]
    layout = []
    for index, (label, model, file_id) in enumerate(ATLAS_PARTS):
        column, row = index % 3, index // 3
        x, y = column * cell, row * cell
        rows.append(f'<rect x="{x:.3f}" y="{y:.3f}" width="{cell:.3f}" height="{cell:.3f}" '
                    'fill="none" stroke="#d9e0e8" stroke-width="3"/>')
        rows.append(f'<rect x="{x + 2:.3f}" y="{y + 2:.3f}" width="{cell - 4:.3f}" '
                    'height="52" fill="#131820" fill-opacity="0.82"/>')
        rows.append(f'<text x="{x + 16:.3f}" y="{y + 38:.3f}" fill="white" '
                    f'font-family="sans-serif" font-size="32">{index + 1:02d} {escape(label)}</text>')
        layout.append({"part": label, "mesh": model, "prefab_mesh_file_id": file_id,
                       "column": column, "row_from_top": row, "uv_row_from_bottom": 2 - row})
    rows.append('</svg>')
    (OUTPUT / "atlas-guide.svg").write_text("\n".join(rows) + "\n", encoding="utf-8")
    (OUTPUT / "atlas-layout.json").write_text(
        json.dumps({"size": size, "columns": 3, "rows": 3, "cells": layout}, indent=2) + "\n",
        encoding="utf-8")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    source_fbx = BACKUP / FBX_NAME if (BACKUP / FBX_NAME).exists() else ASSET / FBX_NAME
    source_texture = BACKUP / TEXTURE_NAME if (BACKUP / TEXTURE_NAME).exists() else ASSET / TEXTURE_NAME
    source = source_fbx.read_text(encoding="utf-8-sig")
    image = bpy.data.images.load(str(source_texture))
    image.pack()
    pairs, blocks, reports = [], [], []
    for model, start, end, block in geometry_blocks(source):
        if model in SKIP:
            continue
        original = load_source_mesh(model, block, image)
        target, report = retopologize(original)
        pairs.append((original, target))
        blocks.append((model, start, end, block))
        reports.append({"mesh": model, "faces_before": len(original.data.polygons),
                        "faces_after": len(target.data.polygons),
                        "quads": sum(len(face.vertices) == 4 for face in target.data.polygons),
                        **report})
        print("PART_TOPOLOGY " + json.dumps(reports[-1]), flush=True)
    if len(pairs) != 9:
        raise ValueError(f"Expected nine substantial FBX geometries, got {len(pairs)}")
    target_by_name = {target.name: target for _, target in pairs}
    atlas_targets = [target_by_name[model] for _, model, _ in ATLAS_PARTS]
    unwrap_atlas(atlas_targets)
    baked = bake_texture(pairs)
    write_atlas_guide()
    edits = []
    for (model, start, end, block), (_, target) in zip(blocks, pairs):
        edits.append((start, end, holes.encode_mesh(target.data, block)))
    updated = source
    for start, end, changed in reversed(edits):
        updated = updated[:start] + changed + updated[end:]
    updated = "\n".join(line.rstrip() for line in updated.split("\n"))
    if len(list(geometry_blocks(updated))) != 10:
        raise ValueError("FBX geometry IDs changed")
    for target in [target for _, target in pairs]:
        target.select_set(False)
    pairs[0][1].select_set(True)
    bpy.context.view_layer.objects.active = pairs[0][1]
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                area.spaces.active.shading.type = "MATERIAL"
                area.spaces.active.overlay.show_extras = False
    bpy.context.preferences.filepaths.save_version = 0
    blend = OUTPUT / "Long Robot arm_TopologyClean.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    BACKUP.mkdir(parents=True, exist_ok=True)
    for name in (FBX_NAME, TEXTURE_NAME, TEXTURE_NAME + ".meta"):
        backup = BACKUP / name
        if not backup.exists():
            shutil.copy2(ASSET / name, backup)
    (ASSET / FBX_NAME).write_text(updated, encoding="utf-8")
    shutil.copy2(baked, ASSET / TEXTURE_NAME)
    meta_path = ASSET / (TEXTURE_NAME + ".meta")
    metadata = meta_path.read_text(encoding="utf-8")
    metadata, count = re.subn(r"(buildTarget: DefaultTexturePlatform\s+maxTextureSize: )\d+",
                              r"\g<1>2048", metadata, count=1)
    if count != 1:
        raise ValueError("Missing default texture import size")
    meta_path.write_text(metadata, encoding="utf-8")
    (OUTPUT / FBX_NAME).write_text(updated, encoding="utf-8")
    (OUTPUT / "process-report.json").write_text(json.dumps(reports, indent=2) + "\n", encoding="utf-8")
    print("LONG_ROBOT_ARM_DONE " + json.dumps({"parts": len(pairs), "blend": str(blend),
                                                "texture": str(baked)}), flush=True)


if __name__ == "__main__":
    main()
