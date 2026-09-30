"""Rectify the existing Pump upper actuator in the open Blender session.

Vertex IDs refer to the inspected 2641-vertex Pump_shape_cleanup mesh.
No topology or UVs are replaced. The cap has three symmetric bevel rings.
"""
import bpy
import bmesh
import math
from mathutils import Vector

obj = bpy.context.active_object
mesh = obj.data
assert len(mesh.vertices) == 2641
assert not obj.get('pump_box_aligned'), 'Box was already aligned'
world = obj.matrix_world.copy()
inverse = world.inverted()
points = [world @ v.co for v in mesh.vertices]
original = [p.copy() for p in points]
cx, cy = -.125, .0234375
sx, sy = -.12524413887877017, .023437528870999813

# Remove the measured XY shear/rotation through the tall actuator only.
for p in points:
    if p.x < .015 and -.115 < p.y < .18 and p.z > .36:
        weight = min(1., (p.z - .36) / .06)
        x, y = p.x-sx, p.y-sy
        p.x += weight * (cx + 1.0228270456087485*x + .045269377493006344*y - p.x)
        p.y += weight * (cy - .07543511647495355*x + 1.0206370688281368*y - p.y)

# Straighten long upright edges without shifting the body attachment below.
for edge in mesh.edges:
    i, j = edge.vertices
    a, b = points[i], points[j]
    if min(a.z, b.z) > .42 and max(a.x, b.x) < -.015 and abs(a.z-b.z) > .065:
        if math.hypot(a.x-b.x, a.y-b.y) < .007:
            a.x = b.x = (a.x+b.x)/2
            a.y = b.y = (a.y+b.y)/2

# Plane-align the core side walls. Keep recesses, bolt heads and ribs distinct.
for p, old in zip(points, original):
    if old.x < -.015 and .42 < old.z < .563 and -.11 < old.y < .15:
        for value in (cx-.105, cx+.105):
            if abs(p.x-value) < .0025:
                p.x = value
                break
        for value in (cy-.103, cy+.103):
            if abs(p.y-value) < .0025:
                p.y = value
                break
        if old.z > .55:
            p.z = .558

# Explicit correspondence avoids merely flattening an already twisted cap.
bottom = [1937, 1935, 1936, 1955, 1956, 1957, 1958, 1938]
shoulder = [1939, 1940, 1942, 1954, 1951, 1948, 1947, 1945]
top = [1943, 1941, 1949, 1952, 1953, 1950, 1944, 1946]

def ring(ids, hx, hy, bevel, z):
    xy = [(-hx,hy-bevel),(-hx+bevel,hy),(hx-bevel,hy),(hx,hy-bevel),
          (hx,-hy+bevel),(hx-bevel,-hy),(-hx+bevel,-hy),(-hx,-hy+bevel)]
    for index, (x,y) in zip(ids, xy):
        points[index] = Vector((cx+x,cy+y,z))

ring(bottom, .115, .125, .014, .567)
ring(shoulder, .115, .125, .014, .611)
ring(top, .107, .117, .010, .6201170683)

for vertex, point in zip(mesh.vertices, points):
    vertex.co = inverse @ point
mesh.update()
bm = bmesh.new()
bm.from_mesh(mesh)
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
assert all(e.is_manifold for e in bm.edges)
assert all(f.calc_area() > 1e-8 for f in bm.faces)
bm.to_mesh(mesh)
bm.free()

# Original imported custom normals no longer describe the rectified box.
mesh.normals_split_custom_set([(0.,0.,0.)] * len(mesh.loops))
mesh.update()
obj['pump_box_aligned'] = True
for label, ids in [('bottom',bottom),('shoulder',shoulder),('top',top)]:
    ring_points = [world @ mesh.vertices[i].co for i in ids]
    z_error = max(p.z for p in ring_points)-min(p.z for p in ring_points)
    symmetry_error = max((ring_points[i]+ring_points[i+4]-Vector((2*cx,2*cy,2*ring_points[i].z))).length for i in range(4))
    print(label, 'height error', z_error, 'central symmetry error', symmetry_error)
print('Upper box aligned; vertices',len(mesh.vertices),'UV layers',len(mesh.uv_layers))
