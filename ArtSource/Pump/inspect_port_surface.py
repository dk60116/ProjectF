"""Inspect disconnected port shells, mouth coverage, and UV discontinuities."""

import bpy
import bmesh
import math
from collections import Counter

obj = next(item for item in bpy.context.scene.objects if item.type == 'MESH')
bm = bmesh.new()
bm.from_mesh(obj.data)
bm.transform(obj.matrix_world)
bm.verts.ensure_lookup_table()
bm.faces.ensure_lookup_table()
uv = bm.loops.layers.uv.active
pending = set(bm.verts)
components = []
while pending:
    seed = pending.pop()
    stack = [seed]
    verts = {seed}
    while stack:
        vertex = stack.pop()
        for edge in vertex.link_edges:
            neighbor = edge.other_vert(vertex)
            if neighbor in pending:
                pending.remove(neighbor)
                verts.add(neighbor)
                stack.append(neighbor)
    faces = {face for vertex in verts for face in vertex.link_faces}
    components.append((verts, faces))

print('COMPONENT_SIZES', Counter(len(verts) for verts, _ in components))
for verts, faces in components:
    if len(verts) != 456:
        continue
    cx = sum(vertex.co.x for vertex in verts) / len(verts)
    side = 1 if cx > 0 else -1
    mouth = [face for face in faces
             if all(abs(abs(vertex.co.x) - .499) < 1e-5 for vertex in face.verts)]
    mouth_normals = [face.normal.x * side for face in mouth]
    smooth = sum(face.smooth for face in faces)
    patches = Counter()
    for face in faces:
        lu = face.loops[0][uv].uv
        px, py = lu.x * 512, (1 - lu.y) * 512
        for name, x, y in (
            ('steel_light', 160, 385), ('steel_dark', 285, 300),
            ('steel_medium', 450, 270), ('brass', 262, 467),
            ('bore', 343, 312),
        ):
            if abs(px - x) < 8 and abs(py - y) < 8:
                patches[name] += 1
                break
    seam_edges = 0
    for edge in {edge for vertex in verts for edge in vertex.link_edges}:
        if len(edge.link_faces) != 2:
            continue
        faces_here = edge.link_faces
        uv_faces = []
        for face in faces_here:
            uv_faces.append({loop.vert: loop[uv].uv.copy() for loop in face.loops if loop.vert in edge.verts})
        if any((uv_faces[0][vertex] - uv_faces[1][vertex]).length > 1e-5 for vertex in edge.verts):
            seam_edges += 1
    print('PORT_SHELL', side, 'verts', len(verts), 'faces', len(faces),
          'mouth_faces', len(mouth), 'mouth_normal_x',
          (round(min(mouth_normals), 3), round(max(mouth_normals), 3)) if mouth else None,
          'smooth_faces', smooth, 'patches', dict(patches), 'uv_seam_edges', seam_edges)

bm.free()
