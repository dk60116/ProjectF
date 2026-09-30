"""Fill the visually empty inlet/outlet centers with recessed metal inserts.

The rim remains intact and the cap sits 25 mm behind its front edge. Each cap
is a closed, single-material shell inside the existing bore; no port/profile
vertices are moved. Both sides are exact mirrors in world space.
"""

import bpy
import bmesh
import math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

obj = next(item for item in bpy.context.scene.objects if item.type == 'MESH')
mesh = obj.data
assert not obj.get('pump_bores_capped'), 'Already capped'
assert len(mesh.vertices) == 4146 and len(mesh.polygons) == 4799
assert len(mesh.materials) == 1 and len(mesh.uv_layers) == 1

old_positions = [vertex.co.copy() for vertex in mesh.vertices]
old_uv = [loop.uv.copy() for loop in mesh.uv_layers.active.data]
bm = bmesh.new()
bm.from_mesh(mesh)
texcoords = bm.loops.layers.uv.verify()
inverse = obj.matrix_world.inverted()
center_y, center_z = .0009765625, .1962890625

def add_shell_face(vertices, side, surface):
    if side < 0:
        vertices = list(reversed(vertices))
    face = bm.faces.new(vertices)
    face.material_index = 0
    face.smooth = surface == 'wall'
    for loop in face.loops:
        world = obj.matrix_world @ loop.vert.co
        if surface == 'front':
            # Medium steel from the same Pump_TB atlas; one continuous disk.
            pixel_x = 450 + (world.y - center_y) * 35
            pixel_y = 270 + (world.z - center_z) * 35
        else:
            pixel_x = 343 + (world.y - center_y) * 10
            pixel_y = 312 + (world.z - center_z) * 10
        loop[texcoords].uv = (pixel_x / 512, 1 - pixel_y / 512)

for side in (-1, 1):
    rings = []
    for depth in (.458, .474):
        ring = []
        for index in range(24):
            angle = math.pi / 24 + 2 * math.pi * index / 24
            world = Vector((side * depth,
                            center_y + .097 * math.cos(angle),
                            center_z + .097 * math.sin(angle)))
            ring.append(bm.verts.new(inverse @ world))
        rings.append(ring)
    back, front = rings
    for index in range(24):
        next_index = (index + 1) % 24
        add_shell_face((back[index], back[next_index],
                        front[next_index], front[index]), side, 'wall')
    add_shell_face(list(reversed(back)), side, 'back')
    add_shell_face(front, side, 'front')

bm.normal_update()
assert all(edge.is_manifold for edge in bm.edges), 'Open or nonmanifold cap'
assert all(face.calc_area() > 1e-12 for face in bm.faces), 'Zero-area cap face'
bm.to_mesh(mesh)
bm.free()
mesh.update()

assert len(mesh.vertices) == 4242 and len(mesh.polygons) == 4851
assert all((mesh.vertices[index].co - point).length < 1e-7
           for index, point in enumerate(old_positions)), 'Existing positions changed'
assert all((mesh.uv_layers.active.data[index].uv - point).length < 1e-7
           for index, point in enumerate(old_uv)), 'Existing UVs changed'
assert all(face.material_index == 0 for face in mesh.polygons)

check = bmesh.new()
check.from_mesh(mesh)
check.transform(obj.matrix_world)
tree = BVHTree.FromBMesh(check)
for side in (-1, 1):
    for radius in (0, .03, .06, .09):
        for index in range(48):
            angle = 2 * math.pi * (index + .35) / 48
            origin = Vector((side * .55,
                             center_y + radius * math.cos(angle),
                             center_z + radius * math.sin(angle)))
            hit, _, _, _ = tree.ray_cast(origin, Vector((-side, 0, 0)), .5)
            assert hit is not None and abs(abs(hit.x) - .474) < 1e-4, \
                (side, radius, index, hit)
assert all(edge.is_manifold for edge in check.edges)
check.free()

obj['pump_bores_capped'] = True
print('PORT_RECESSES_CAPPED', len(mesh.vertices), len(mesh.polygons),
      'original geometry and UVs preserved; 384 cap ray samples passed')
