"""Precise mesh cleanup helpers for the currently open Pump Blender session.

Run through Blender's Python Console after inspecting the imported FBX.
All coordinates below are in Blender world metres; object transforms are kept.
"""
import bpy
import bmesh
import math
from collections import defaultdict


def cleanup_pump():
    obj = bpy.context.active_object
    assert obj and obj.type == 'MESH'
    if obj.get('pump_shape_cleanup'):
        raise RuntimeError('Already cleaned; reopen the original to repeat.')
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    # Split collinear T-junctions instead of filling zero-area faces.
    candidates = {v for e in bm.edges if e.is_boundary for v in e.verts}
    splits = 0
    for edge in list(bm.edges):
        if not edge.is_valid or not edge.is_boundary:
            continue
        start, end = edge.verts
        delta = end.co - start.co
        if delta.length_squared < 1e-14:
            continue
        interior = []
        for v in candidates:
            if v in edge.verts:
                continue
            t = (v.co - start.co).dot(delta) / delta.length_squared
            if 1e-5 < t < 1 - 1e-5 and (v.co - start.co - t * delta).length < 1e-6:
                interior.append((t, v.co.copy()))
        current, anchor = edge, start
        for _, point in sorted(interior, key=lambda item: item[0]):
            other = current.other_vert(anchor)
            factor = (point - anchor.co).length / (other.co - anchor.co).length
            _, new_v = bmesh.utils.edge_split(current, anchor, factor)
            new_v.co = point
            current = next(e for e in new_v.link_edges if other in e.verts)
            anchor = new_v
            splits += 1
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-6)
    cy, cz = .0009765625, .1962890625
    # Match flange outer diameters, retaining the bore and outer mouth sizes.
    for v in bm.verts:
        if v.co.x > .33:
            y, z = v.co.y - cy, v.co.z - cz
            radius = math.hypot(y, z)
            axial_weight = min(1., max(0., (v.co.x - .33) / .03))
            radial_weight = min(1., max(0., (radius - .105) / .045))
            flange_weight = min(1., max(0., (.44 - v.co.x) / .012))
            scale = 1 + .12 * axial_weight * radial_weight * flange_weight
            v.co.y, v.co.z = cy + y * scale, cz + z * scale
    # Circularize complete 12-sided rings, excluding mixed bolt/ring groups.
    groups = defaultdict(list)
    for v in bm.verts:
        if abs(v.co.x) > .34:
            groups[round(v.co.x, 5)].append(v)
    rings = 0
    for verts in groups.values():
        if len(verts) not in (12, 24):
            continue
        ordered = sorted(verts, key=lambda v: math.hypot(v.co.y-cy, v.co.z-cz))
        for offset in range(0, len(ordered), 12):
            ring = ordered[offset:offset+12]
            radii = [math.hypot(v.co.y-cy, v.co.z-cz) for v in ring]
            if max(radii)-min(radii) > .012:
                continue
            angles = [math.atan2(v.co.z-cz, v.co.y-cy) for v in ring]
            if len({round(a/(math.pi/6)) % 12 for a in angles}) != 12:
                continue
            radius = sum(radii)/12
            for v, angle in zip(ring, angles):
                angle = round(angle/(math.pi/6))*(math.pi/6)
                v.co.y, v.co.z = cy+radius*math.cos(angle), cz+radius*math.sin(angle)
            rings += 1
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    boundary = sum(e.is_boundary for e in bm.edges)
    nonmanifold = sum(not e.is_manifold for e in bm.edges)
    degenerate = sum(f.calc_area() < 1e-12 for f in bm.faces)
    print('CLEANUP', 'T-junction splits', splits, 'rings', rings,
          'boundary', boundary, 'nonmanifold', nonmanifold, 'degenerate', degenerate)
    assert boundary == 0 and nonmanifold == 0 and degenerate == 0, 'Mesh check failed; original mesh retained'
    bm.transform(obj.matrix_world.inverted())
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    obj['pump_shape_cleanup'] = True
    obj.data.calc_loop_triangles()
    print('RESULT', len(obj.data.vertices), 'vertices', len(obj.data.polygons),
          'faces', len(obj.data.loop_triangles), 'triangles')


cleanup_pump()
