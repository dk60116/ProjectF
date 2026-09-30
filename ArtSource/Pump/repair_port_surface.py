"""Remove the per-polygon texture grid on both Pump inlet/outlet shells.

The previous atlas pass projected every face around its own center. All 912
interior edges of each 456-face port consequently became UV seams, making a
complete rim look like a broken checkerboard. Keep the existing single image,
geometry, fasteners and body UVs; project each continuous surface band from
world coordinates so neighboring faces share the same UV at shared vertices.
"""

import bpy
import math
from collections import Counter, defaultdict
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import bmesh

obj = next(item for item in bpy.context.scene.objects if item.type == 'MESH')
mesh = obj.data
assert not obj.get('pump_port_surface_repaired'), 'Already repaired'
assert len(mesh.vertices) == 4146 and len(mesh.materials) == 1
assert len(mesh.uv_layers) == 1

uv = mesh.uv_layers.active.data
neighbors = [[] for _ in mesh.vertices]
for edge in mesh.edges:
    a, b = edge.vertices
    neighbors[a].append(b)
    neighbors[b].append(a)

unseen = set(range(len(mesh.vertices)))
port_components = []
while unseen:
    seed = unseen.pop()
    component = {seed}
    stack = [seed]
    while stack:
        for neighbor in neighbors[stack.pop()]:
            if neighbor in unseen:
                unseen.remove(neighbor)
                component.add(neighbor)
                stack.append(neighbor)
    if len(component) == 456:
        port_components.append(component)
assert len(port_components) == 2

def seam_count(faces):
    edge_uvs = defaultdict(list)
    for face in faces:
        loops = list(face.loop_indices)
        for index, loop_index in enumerate(loops):
            next_index = loops[(index + 1) % len(loops)]
            a = mesh.loops[loop_index].vertex_index
            b = mesh.loops[next_index].vertex_index
            key = tuple(sorted((a, b)))
            edge_uvs[key].append({a: uv[loop_index].uv.copy(),
                                  b: uv[next_index].uv.copy()})
    return sum(
        len(sides) == 2 and any((sides[0][vertex] - sides[1][vertex]).length > 1e-5
                                for vertex in key)
        for key, sides in edge_uvs.items()
    )

def palette(face):
    point = uv[face.loop_start].uv
    px, py = point.x * 512, (1 - point.y) * 512
    for name, center_x, center_y in (
        ('steel', 160, 385), ('steel', 285, 300),
        ('steel', 450, 270), ('brass', 262, 467), ('bore', 343, 312),
    ):
        if abs(px - center_x) < 8 and abs(py - center_y) < 8:
            return name
    raise AssertionError(('Unknown port UV patch', face.index, px, py))

def new_uv(world_point, region):
    x = abs(world_point.x)
    y = world_point.y - .0009765625
    z = world_point.z - .1962890625
    if region == 'steel':
        # Shared projection for all 14 steel profile bands: no per-face reset.
        px = 450 + (x - .4) * 35 + y * 12
        py = 270 + z * 12 + y * 8
    elif region == 'brass':
        px = 262 + y * 18 + z * 10
        py = 467 + (x - .4) * 20 + z * 10
    else:
        px = 343 + y * 12
        py = 312 + z * 12
    return (px / 512, 1 - py / 512)

results = []
port_faces = set()
for component in port_components:
    faces = [face for face in mesh.polygons
             if face.vertices[0] in component]
    assert len(faces) == 456 and all(set(face.vertices) <= component for face in faces)
    side = 1 if sum((obj.matrix_world @ mesh.vertices[v].co).x for v in component) > 0 else -1
    regions = Counter(palette(face) for face in faces)
    assert regions == {'steel': 336, 'brass': 72, 'bore': 48}, regions
    mouth = [face for face in faces if all(
        abs(abs((obj.matrix_world @ mesh.vertices[v].co).x) - .499) < 1e-5
        for v in face.vertices)]
    assert len(mouth) == 24, (side, len(mouth))
    before = seam_count(faces)
    assert before >= 900, (side, before)
    for face in faces:
        region = palette(face)
        for loop_index in face.loop_indices:
            point = obj.matrix_world @ mesh.vertices[mesh.loops[loop_index].vertex_index].co
            uv[loop_index].uv = new_uv(point, region)
    after = seam_count(faces)
    assert after <= 96, (side, before, after)
    port_faces.update(face.index for face in faces)
    results.append((side, before, after, len(mouth)))

assert len(port_faces) == 912
assert all(0 < corner.uv.x < 1 and 0 < corner.uv.y < 1 for corner in uv)
mesh.update()

# A surface ray must hit the complete front annulus at every angle. The dark
# center remains a recessed, capped connector bore rather than a missing face.
bm = bmesh.new()
bm.from_mesh(mesh)
bm.transform(obj.matrix_world)
tree = BVHTree.FromBMesh(bm)
for side in (-1, 1):
    for radius in (.105, .115, .125):
        for index in range(240):
            angle = 2 * math.pi * (index + .35) / 240
            origin = Vector((side * .55,
                             .0009765625 + radius * math.cos(angle),
                             .1962890625 + radius * math.sin(angle)))
            hit, _, _, _ = tree.ray_cast(origin, Vector((-side, 0, 0)), .5)
            assert hit is not None and abs(abs(hit.x) - .499) < 1e-4, \
                (side, radius, index, hit)
assert all(edge.is_manifold for edge in bm.edges)
bm.free()

obj['pump_port_surface_repaired'] = True
print('PORT_SURFACE_REPAIRED', sorted(results), 'front ring ray samples', 1440)
