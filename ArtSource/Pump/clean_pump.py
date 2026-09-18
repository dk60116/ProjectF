"""Pump UV/texture cleanup, executed in the existing Blender instance.

The Unity ASCII FBX is read without changing mesh/loop order. Only UV arrays
and tangent basis are written back, preserving Unity node IDs and prefab links.
Use prepare(), repack(), bake(), and finalize() in that order; review between steps.
"""
import bpy
import hashlib
import json
import math
import re
import shutil
from pathlib import Path

import numpy as np
from mathutils import Vector

WORK = Path(__file__).resolve().parent
ASSET = WORK.parents[1] / 'FactorioProject/Assets/MapObject/Fluid/Pump'
ORIGINAL = WORK / 'original'


def array(text, key, cast=float):
    match = re.search(r'\b' + key + r': \*\d+\s*\{\s*a:\s*([^}]+)', text)
    return np.array([cast(v) for v in match.group(1).split(',') if v.strip()])


def uv_overlap(mesh, uv, size=2048):
    mesh.calc_loop_triangles()
    owner = np.full((size, size), -1, dtype=np.int32)
    overlap, bad_faces = 0, set()
    for i, triangle in enumerate(mesh.loop_triangles):
        if triangle.area <= 1e-12:
            continue
        tri = uv[list(triangle.loops)] * size
        lo = np.maximum(np.floor(tri.min(0)).astype(int), 0)
        hi = np.minimum(np.ceil(tri.max(0)).astype(int), size)
        xx, yy = np.meshgrid(np.arange(lo[0], hi[0]) + .5, np.arange(lo[1], hi[1]) + .5)
        a, b, c = tri
        den = (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
        if abs(den) < 1e-14:
            continue
        u = ((xx-a[0])*(c[1]-a[1])-(yy-a[1])*(c[0]-a[0])) / den
        v = ((b[0]-a[0])*(yy-a[1])-(b[1]-a[1])*(xx-a[0])) / den
        inside = (u > 1e-6) & (v > 1e-6) & (u+v < 1-1e-6)
        region = owner[lo[1]:hi[1], lo[0]:hi[0]]
        collisions = inside & (region >= 0)
        overlap += int(np.count_nonzero(collisions))
        if collisions.any():
            bad_faces.add(triangle.polygon_index)
            bad_faces.update(mesh.loop_triangles[int(j)].polygon_index for j in np.unique(region[collisions]))
        region[inside] = i
    return overlap, bad_faces


def set_view(obj, reverse=False):
    target = sum((obj.matrix_world @ Vector(c) for c in obj.bound_box), Vector()) / 8
    extent = max(obj.dimensions)
    direction = Vector((-1.3, 1.8, 1.0) if reverse else (1.3, -1.8, 1.0))
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                space = area.spaces.active
                space.shading.type = 'MATERIAL'
                space.overlay.show_overlays = False
                space.region_3d.view_location = target
                space.region_3d.view_distance = extent * 1.7
                space.region_3d.view_rotation = direction.to_track_quat('Z', 'Y')
                space.region_3d.view_perspective = 'ORTHO'


def prepare():
    assert not bpy.context.scene.objects, 'Expected the inspected empty scene'
    ORIGINAL.mkdir(parents=True, exist_ok=True)
    for name in ('Pump.fbx', 'Pump_TB.png'):
        backup = ORIGINAL / name
        if not backup.exists():
            shutil.copy2(ASSET / name, backup)
    bpy.ops.wm.save_as_mainfile(filepath=str(ORIGINAL / 'session_before_cleanup.blend'), copy=True)
    source = (ORIGINAL / 'Pump.fbx').read_text()
    assert len(re.findall(r'\n\s*Geometry:', source)) == 1
    xyz = array(source, 'Vertices').reshape(-1, 3)
    indices = array(source, 'PolygonVertexIndex', int)
    faces, face = [], []
    for index in indices:
        face.append(int(index if index >= 0 else -index - 1))
        if index < 0:
            faces.append(face)
            face = []
    assert not face
    mesh = bpy.data.meshes.new('Scene')
    mesh.from_pydata([(x / 100, -z / 100, y / 100) for x, y, z in xyz], [], faces)
    mesh.update()
    obj = bpy.data.objects.new('Pump', mesh)
    bpy.context.collection.objects.link(obj)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    uv = array(source, 'UV').reshape(-1, 2)
    uv_indices = array(source, 'UVIndex', int)
    assert len(mesh.loops) == len(uv_indices)
    layer = mesh.uv_layers.new(name='OriginalUV')
    layer.data.foreach_set('uv', uv[uv_indices].reshape(-1))
    normals = array(source, 'Normals').reshape(-1, 3)
    mesh.normals_split_custom_set([(x, -z, y) for x, y, z in normals])
    for poly in mesh.polygons:
        poly.use_smooth = True
    image = bpy.data.images.load(str(ORIGINAL / 'Pump_TB.png'))
    image.pack()
    mat = bpy.data.materials.new('Pump_Surface')
    mat.use_nodes = True
    mesh.materials.append(mat)
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Roughness'].default_value = .65
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = image
    uv_node = nodes.new('ShaderNodeUVMap')
    uv_node.uv_map = 'OriginalUV'
    links.new(uv_node.outputs['UV'], tex.inputs['Vector'])
    links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    set_view(obj)
    bpy.ops.wm.save_as_mainfile(filepath=str(ORIGINAL / 'Pump_imported.blend'), copy=True)
    print(json.dumps({'vertices': len(mesh.vertices), 'faces': len(mesh.polygons),
                      'dimensions': list(obj.dimensions), 'texture_size': list(image.size),
                      'uv_range': [float(uv.min()), float(uv.max())]}))


def repack():
    obj = bpy.data.objects['Pump']
    mesh = obj.data
    assert len(mesh.uv_layers) == 1
    packed = mesh.uv_layers.new(name='UVSet0', do_init=True)
    mesh.uv_layers.active = packed
    packed.active_render = True
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0,
                             area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, rotate_method='CARDINAL', shape_method='CONCAVE',
                            margin_method='FRACTION', margin=.008, merge_overlap=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    updated = np.array([v.uv[:] for v in mesh.uv_layers['UVSet0'].data])
    # Concave surfaces may fold inside a projected island; isolate only those faces.
    split_faces = set()
    for attempt in range(4):
        overlap, bad_faces = uv_overlap(mesh, updated)
        if not overlap:
            break
        split_faces.update(bad_faces)
        layer = mesh.uv_layers['UVSet0']
        for offset, face_id in enumerate(sorted(bad_faces)):
            for li in mesh.polygons[face_id].loop_indices:
                layer.data[li].uv.x += 10 + offset * 2
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.select_all(action='SELECT')
        bpy.ops.uv.pack_islands(rotate=True, rotate_method='CARDINAL', shape_method='CONCAVE',
                                margin_method='FRACTION', margin=.008, merge_overlap=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        updated = np.array([v.uv[:] for v in mesh.uv_layers['UVSet0'].data])
    assert uv_overlap(mesh, updated)[0] == 0, 'UV islands still overlap after repair'
    # Count the final islands. The old UV stays exclusively as the texture-bake source.
    parent = list(range(len(mesh.polygons)))
    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    edges = {}
    for p in mesh.polygons:
        ids = list(p.loop_indices)
        for a, b in zip(ids, ids[1:] + ids[:1]):
            key = tuple(sorted([(mesh.loops[a].vertex_index, tuple(updated[a])),
                                (mesh.loops[b].vertex_index, tuple(updated[b]))]))
            if key in edges:
                parent[find(p.index)] = find(edges[key])
            else:
                edges[key] = p.index
    islands = {}
    for p in mesh.polygons:
        islands.setdefault(find(p.index), []).extend(p.loop_indices)
    report = {'islands': len(islands), 'unwrap_angle_degrees': 66, 'isolated_folded_faces':len(split_faces),
              'original_texture_preserved': True, 'padding_fraction': .008}
    (WORK / 'uv_cleanup.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report))


def bake():
    obj = bpy.data.objects['Pump']
    mat = obj.data.materials[0]
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    source = next(n for n in nodes if n.type == 'TEX_IMAGE')
    output = next(n for n in nodes if n.type == 'OUTPUT_MATERIAL')
    atlas = bpy.data.images.get('Pump_Atlas')
    if atlas is None:
        atlas = bpy.data.images.new('Pump_Atlas', width=2048, height=2048, alpha=False)
    atlas.colorspace_settings.name = source.image.colorspace_settings.name
    target = nodes.new('ShaderNodeTexImage')
    target.image = atlas
    nodes.active = target
    emit = nodes.new('ShaderNodeEmission')
    links.new(source.outputs['Color'], emit.inputs['Color'])
    links.new(emit.outputs['Emission'], output.inputs['Surface'])
    scene = bpy.context.scene
    try:
        scene.render.engine = 'CYCLES'
    except TypeError as error:
        raise RuntimeError('Cycles required for original-color texture transfer') from error
    scene.cycles.samples = 1
    scene.render.bake.margin = 8
    scene.render.bake.margin_type = 'EXTEND'
    bpy.ops.object.bake(type='EMIT', use_clear=True)
    atlas.filepath_raw = str(WORK / 'Pump_TB.png')
    atlas.file_format = 'PNG'
    atlas.save()
    atlas.pack()
    bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
    links.new(target.outputs['Color'], bsdf.inputs['Base Color'])
    links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
    for node in list(nodes):
        if node not in (target, bsdf, output):
            nodes.remove(node)
    target.location = (-320, 80)
    bsdf.location = (0, 80)
    output.location = (320, 80)
    obj.data.uv_layers.remove(obj.data.uv_layers['OriginalUV'])
    set_view(obj)
    bpy.ops.wm.save_as_mainfile(filepath=str(WORK / 'Pump_clean.blend'))
    print('Baked original color to 2048 atlas; one material and one UV map remain')


def finalize():
    mesh = bpy.data.objects['Pump'].data
    source = (ORIGINAL / 'Pump.fbx').read_text()
    def replace(text, key, values):
        values = list(values)
        rows = [','.join(format(float(v), '.12g') for v in values[i:i+48])
                for i in range(0, len(values), 48)]
        body = key + ': *' + str(len(values)) + ' {\n\t\t\t\ta: ' + ',\n'.join(rows) + '\n\t\t\t}'
        result, count = re.subn(r'\b' + key + r': \*\d+\s*\{\s*a:\s*[^}]+}', lambda m: body, text, count=1)
        assert count == 1, key
        return result
    candidate = replace(source, 'UV', (v for loop in mesh.uv_layers.active.data for v in loop.uv))
    candidate = replace(candidate, 'UVIndex', range(len(mesh.loops)))
    mesh.calc_tangents(uvmap='UVSet0')
    candidate = replace(candidate, 'Tangents', (v for loop in mesh.loops for v in (loop.tangent.x, loop.tangent.z, -loop.tangent.y)))
    candidate = replace(candidate, 'Binormals', (v for loop in mesh.loops for v in (loop.bitangent.x, loop.bitangent.z, -loop.bitangent.y)))
    def immutable(text):
        for key in ('UV', 'UVIndex', 'Tangents', 'Binormals'):
            text = re.sub(r'\b' + key + r': \*\d+\s*\{\s*a:\s*[^}]+}', key + ': <updated>', text)
        return text
    assert immutable(source) == immutable(candidate), 'Unexpected change outside UV/tangents'
    assert np.isfinite(array(candidate, 'Tangents')).all()
    assert np.isfinite(array(candidate, 'Binormals')).all()
    (WORK / 'Pump.fbx').write_text(candidate)
    print('Staged FBX: IDs, geometry, normals, node transforms, material bindings preserved')


def validate():
    mesh = bpy.data.objects['Pump'].data
    source = (ORIGINAL / 'Pump.fbx').read_text()
    candidate = (WORK / 'Pump.fbx').read_text()
    old_uv = array(source, 'UV').reshape(-1, 2)[array(source, 'UVIndex', int)]
    new_uv = array(candidate, 'UV').reshape(-1, 2)[array(candidate, 'UVIndex', int)]
    assert np.isfinite(new_uv).all() and new_uv.min() >= 0 and new_uv.max() <= 1
    mesh.calc_loop_triangles()
    triangles = np.array([t.loops[:] for t in mesh.loop_triangles])
    world_area = np.array([t.area for t in mesh.loop_triangles])
    tex_tri = new_uv[triangles]
    ab, ac = tex_tri[:, 1] - tex_tri[:, 0], tex_tri[:, 2] - tex_tri[:, 0]
    area = np.abs(ab[:, 0] * ac[:, 1] - ab[:, 1] * ac[:, 0]) * .5
    visible = world_area > 1e-12
    assert np.all(area[visible] > 1e-12), 'Collapsed visible UV triangles'
    overlap, _ = uv_overlap(mesh, new_uv)
    assert overlap == 0, f'{overlap} overlapping interior texels'
    def sample(image, uv):
        w, h = image.size
        pixels = np.empty(w*h*4, np.float32)
        image.pixels.foreach_get(pixels)
        pixels = pixels.reshape(h, w, 4)
        xy = uv * (w, h) - .5
        lo = np.floor(xy).astype(int)
        f = xy - lo
        x, y = lo[:, 0] % w, lo[:, 1] % h
        x1, y1 = (x+1) % w, (y+1) % h
        fx, fy = f[:, 0, None], f[:, 1, None]
        return ((pixels[y,x]*(1-fx)+pixels[y,x1]*fx)*(1-fy)
                +(pixels[y1,x]*(1-fx)+pixels[y1,x1]*fx)*fy)[:, :3]
    # Sample triangle interiors at matching barycentric positions, weighted by surface area.
    weights = np.array([[1/3,1/3,1/3],[.6,.2,.2],[.2,.6,.2],[.2,.2,.6]])
    source_image = next(i for i in bpy.data.images
                        if i.filepath.replace('\\', '/').endswith('/original/Pump_TB.png'))
    atlas = bpy.data.images['Pump_Atlas']
    a = np.einsum('wv,tvc->twc', weights, old_uv[triangles]).reshape(-1,2)
    b = np.einsum('wv,tvc->twc', weights, tex_tri).reshape(-1,2)
    difference = np.abs(sample(source_image,a)-sample(atlas,b)).reshape(-1,4,3).mean(axis=(1,2))
    mean_error = float(np.average(difference[visible],weights=world_area[visible]))
    assert mean_error < .03, f'Texture transfer differs too much: {mean_error}'
    report = json.loads((WORK / 'uv_cleanup.json').read_text())
    report.update(vertices=len(mesh.vertices), triangles=len(triangles),
                  geometry_ids_transforms_unchanged=True, uv_range=[float(new_uv.min()),float(new_uv.max())],
                  collapsed_visible_uv_triangles=0, overlapping_interior_texels=overlap,
                  atlas_coverage=float(area.sum()), texture_size=list(atlas.size),
                  texture_transfer_mean_rgb_error=mean_error,
                  fbx_sha256=hashlib.sha256((WORK/'Pump.fbx').read_bytes()).hexdigest(),
                  texture_sha256=hashlib.sha256((WORK/'Pump_TB.png').read_bytes()).hexdigest())
    (WORK / 'uv_cleanup.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))
