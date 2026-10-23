"""Blender normal rebuild shared by RobotArm retopology and shading repair."""

import math
from collections import defaultdict

import bpy


def rebuild_normals(obj, sharp_angle_degrees=35.0):
    """Smooth connected surfaces and retain the edges of mechanical shells."""
    mesh = obj.data
    for polygon in mesh.polygons:
        polygon.use_smooth = True

    edge_faces = defaultdict(list)
    for polygon in mesh.polygons:
        for key in polygon.edge_keys:
            edge_faces[tuple(sorted(key))].append(polygon.index)

    sharp = mesh.attributes.get("sharp_edge")
    if sharp is None:
        sharp = mesh.attributes.new(name="sharp_edge", type="BOOLEAN", domain="EDGE")
    cosine_limit = math.cos(math.radians(sharp_angle_degrees))
    for edge in mesh.edges:
        faces = edge_faces[tuple(sorted(edge.vertices))]
        sharp.data[edge.index].value = (
            len(faces) != 2
            or mesh.polygons[faces[0]].normal.dot(mesh.polygons[faces[1]].normal) < cosine_limit
        )

    modifier = obj.modifiers.new("Mechanical shell normals", "WEIGHTED_NORMAL")
    modifier.keep_sharp = True
    modifier.mode = "FACE_AREA"
    modifier.weight = 50
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    mesh.update()
