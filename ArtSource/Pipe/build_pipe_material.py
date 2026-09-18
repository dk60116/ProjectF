"""Live Blender workflow for the current Unity straight Pipe mesh.

Import the actual serialized mesh, unwrap and bake cast iron in Blender, then
generate a reviewable Unity mesh asset. Original geometry and normals survive.
"""
import bpy
import bmesh
import hashlib
import json
import math
import re
import shutil
import struct
from pathlib import Path
from mathutils import Vector

WORK = Path(__file__).resolve().parent
ASSET = WORK.parents[1] / 'FactorioProject/Assets/MapObject/Fluid/Pipe'
SOURCE = ASSET / 'Pipe.asset'


def read_mesh(path):
    text = path.read_text()
    count = int(re.search(r'm_VertexCount: (\d+)', text)[1])
    channels = [tuple(map(int, m)) for m in re.findall(
        r'- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)', text)]
    assert all(s == 0 and f == 0 for s, o, f, d in channels if d)
    stride = max(o + d * 4 for s, o, f, d in channels if d)
    raw = bytes.fromhex(re.search(r'_typelessdata: (\w+)', text)[1])
    assert len(raw) == count * stride
    indices_raw = bytes.fromhex(re.search(r'm_IndexBuffer: (\w+)', text)[1])
    index_format = int(re.search(r'm_IndexFormat: (\d+)', text)[1])
    indices = struct.unpack('<' + ('I' if index_format else 'H') *
                            (len(indices_raw) // (4 if index_format else 2)), indices_raw)
    assert len(re.findall('firstByte:', text)) == 1
    xyz = [struct.unpack_from('<3f', raw, i * stride) for i in range(count)]
    normals = [struct.unpack_from('<3f', raw, i * stride + channels[1][1]) for i in range(count)]
    uv = [struct.unpack_from('<2f', raw, i * stride + channels[4][1]) for i in range(count)]
    return dict(text=text, raw=raw, stride=stride, channels=channels,
                xyz=xyz, normals=normals, uv=uv, indices=indices)


def prepare():
    global original, obj, material
    assert len(bpy.context.scene.objects) == 0, 'Only initialize the inspected empty scene'
    backup = WORK / 'original'
    backup.mkdir(parents=True, exist_ok=True)
    for name in ('Pipe.asset', 'Pipe.asset.meta', 'Pipe.prefab'):
        assert not (backup / name).exists(), 'Do not overwrite an earlier backup'
        shutil.copy2(ASSET / name, backup / name)
    bpy.ops.wm.save_as_mainfile(filepath=str(backup / 'session_before_pipe.blend'), copy=True)
    original = read_mesh(SOURCE)
    # Unity Y-up -> Blender Z-up, reflection compensated by reversed triangles.
    xyz = [(x, z, y) for x, y, z in original['xyz']]
    triangles = [tuple(reversed(original['indices'][i:i+3]))
                 for i in range(0, len(original['indices']), 3)]
    mesh = bpy.data.meshes.new('Pipe_UnityMesh')
    mesh.from_pydata(xyz, [], triangles)
    mesh.update()
    obj = bpy.data.objects.new('Pipe', mesh)
    bpy.context.collection.objects.link(obj)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    # Keep source corner IDs through welding so exporting can preserve all channels.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    source_layer = bm.loops.layers.int.new('UnitySourceVertex')
    for face in bm.faces:
        for loop in face.loops:
            loop[source_layer] = loop.vert.index
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.0000001)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    source_ids = [v.value for v in mesh.attributes['UnitySourceVertex'].data]
    mesh.normals_split_custom_set([(original['normals'][i][0], original['normals'][i][2],
                                   original['normals'][i][1]) for i in source_ids])
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    uv = mesh.uv_layers.new(name='Pipe_BakeUV')
    for loop in mesh.loops:
        uv.data[loop.index].uv = original['uv'][source_ids[loop.index]]
    material = create_cast_iron()
    mesh.materials.append(material)
    set_view()
    print(json.dumps({'vertices':len(mesh.vertices), 'triangles':len(mesh.polygons),
                      'source_vertices':len(original['xyz']), 'source_hash':hashlib.sha256(original['raw']).hexdigest()}))


def create_cast_iron():
    mat = bpy.data.materials.new('Pipe_CastIron_Source')
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Metallic'].default_value = 0.55
    bsdf.inputs['Roughness'].default_value = 0.72
    tex = nodes.new('ShaderNodeTexCoord')
    noise = nodes.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = 180
    noise.inputs['Detail'].default_value = 2
    noise.inputs['Roughness'].default_value = 0.65
    links.new(tex.outputs['Object'], noise.inputs['Vector'])
    ramp = nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = 0.22
    ramp.color_ramp.elements[0].color = (0.040, 0.046, 0.054, 1)
    ramp.color_ramp.elements[1].position = 0.78
    ramp.color_ramp.elements[1].color = (0.10, 0.112, 0.13, 1)
    links.new(noise.outputs['Fac'], ramp.inputs[0])
    # Object-space grain is baked into each UV island at the same physical scale.
    vor = nodes.new('ShaderNodeTexVoronoi')
    vor.inputs['Scale'].default_value = 65
    links.new(tex.outputs['Object'], vor.inputs['Vector'])
    grain = nodes.new('ShaderNodeMixRGB')
    grain.blend_type = 'MULTIPLY'
    grain.inputs[0].default_value = 0.16
    links.new(ramp.outputs['Color'], grain.inputs[1])
    links.new(vor.outputs['Distance'], grain.inputs[2])
    geom = nodes.new('ShaderNodeNewGeometry')
    bevel = nodes.new('ShaderNodeBevel')
    bevel.inputs['Radius'].default_value = 0.006
    bevel.samples = 4
    distance = nodes.new('ShaderNodeVectorMath')
    distance.operation = 'DISTANCE'
    links.new(geom.outputs['Normal'], distance.inputs[0])
    links.new(bevel.outputs['Normal'], distance.inputs[1])
    edge = nodes.new('ShaderNodeMath')
    edge.operation = 'MULTIPLY'
    edge.use_clamp = True
    edge.inputs[1].default_value = 3
    links.new(distance.outputs['Value'], edge.inputs[0])
    mix = nodes.new('ShaderNodeMixRGB')
    mix.label = 'Subtle worn cast-iron edges'
    links.new(edge.outputs[0], mix.inputs[0])
    links.new(grain.outputs[0], mix.inputs[1])
    mix.inputs[2].default_value = (0.29, 0.275, 0.245, 1)
    ao = nodes.new('ShaderNodeAmbientOcclusion')
    ao.inputs['Distance'].default_value = 0.08
    occlusion = nodes.new('ShaderNodeMixRGB')
    occlusion.blend_type = 'MULTIPLY'
    occlusion.inputs[0].default_value = 0.45
    links.new(mix.outputs[0], occlusion.inputs[1])
    links.new(ao.outputs['AO'], occlusion.inputs[2])
    links.new(occlusion.outputs[0], bsdf.inputs['Base Color'])
    bump = nodes.new('ShaderNodeBump')
    bump.inputs['Strength'].default_value = 0.12
    bump.inputs['Distance'].default_value = 0.003
    links.new(noise.outputs['Fac'], bump.inputs['Height'])
    links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
    mat['bake_color_node'] = occlusion.name
    return mat


def set_view():
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                space = area.spaces.active
                space.shading.type = 'MATERIAL'
                space.overlay.show_overlays = False
                space.region_3d.view_location = (0, 0, 0)
                space.region_3d.view_distance = 2.3
                space.region_3d.view_rotation = Vector((1.3, -1.8, 1.2)).to_track_quat('Z','Y')
                space.region_3d.view_perspective = 'ORTHO'


def unwrap():
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.015,
                             area_weight=0.0, correct_aspect=True, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    print('UV unwrap complete:', len(obj.data.uv_layers.active.data), 'corners')


def bake():
    global baked_image
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    obj.data.materials[0] = material
    scene = bpy.context.scene
    try:
        scene.render.engine = 'CYCLES'
    except TypeError as exc:
        raise RuntimeError(str(exc))
    scene.cycles.samples = 32
    baked_image = bpy.data.images.new('Pipe_CastIron_TB', width=2048, height=2048, alpha=False)
    nodes, links = material.node_tree.nodes, material.node_tree.links
    image_node = nodes.new('ShaderNodeTexImage')
    image_node.image = baked_image
    nodes.active = image_node
    for node in nodes:
        node.select = node == image_node
    output = next(n for n in nodes if n.type == 'OUTPUT_MATERIAL')
    emission = nodes.new('ShaderNodeEmission')
    links.new(nodes[material['bake_color_node']].outputs[0], emission.inputs['Color'])
    links.new(emission.outputs[0], output.inputs['Surface'])
    scene.render.bake.margin = 16
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.bake(type='EMIT')
    baked_image.filepath_raw = str(WORK / 'Pipe_CastIron_TB.png')
    baked_image.file_format = 'PNG'
    baked_image.save()
    bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
    links.new(bsdf.outputs[0], output.inputs['Surface'])
    nodes.remove(emission)
    preview = bpy.data.materials.new('Pipe_CastIron_Baked')
    preview.use_nodes = True
    pn = next(n for n in preview.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    pn.inputs['Metallic'].default_value = 0.15
    pn.inputs['Roughness'].default_value = 0.8
    tex = preview.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = baked_image
    preview.node_tree.links.new(tex.outputs['Color'], pn.inputs['Base Color'])
    obj.data.materials[0] = preview
    baked_image.pack()
    bpy.ops.wm.save_as_mainfile(filepath=str(WORK / 'Pipe_CastIron.blend'))
    print('Baked and saved Pipe_CastIron_TB.png and Pipe_CastIron.blend')


def export_mesh():
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    mesh = obj.data
    source_ids = [v.value for v in mesh.attributes['UnitySourceVertex'].data]
    uv = mesh.uv_layers.active
    # Keep original order and coincident backfaces. Explicitly culled hidden
    # sphere surfaces are omitted rather than resurrected by a later export.
    def point_key(p):
        return tuple(round(x, 6) for x in p)
    faces = {}
    for polygon in mesh.polygons:
        face = {point_key(original['xyz'][source_ids[li]]): tuple(uv.data[li].uv)
                for li in polygon.loop_indices}
        faces.setdefault(tuple(sorted(face)), face)
    vertices, indices, lookup, retained = [], [], {}, []
    dropped = 0
    for fi in range(0, len(original['indices']), 3):
        sources = original['indices'][fi:fi+3]
        points = [point_key(original['xyz'][src]) for src in sources]
        face = faces.get(tuple(sorted(points)))
        if face is None:
            dropped += 1
            continue
        retained.extend(sources)
        for src, point in zip(sources, points):
            coord = face[point]
            key = (src, coord)
            if key not in lookup:
                raw = bytearray(original['raw'][src*original['stride']:(src+1)*original['stride']])
                struct.pack_into('<2f', raw, original['channels'][4][1], *coord)
                lookup[key] = len(vertices)
                vertices.append(raw)
            indices.append(lookup[key])
    assert dropped == int(obj.get('pipe_removed_source_triangles', 0)), 'Unexpected missing faces'
    recalculate_tangents(vertices, indices)
    vertices, indices = compact_vertices(vertices, indices)
    write_mesh(original, vertices, indices, WORK/'Pipe_Baked.asset', retained_indices=retained)
    report={'triangles':len(indices)//3,'old_vertices':len(original['xyz']),
            'new_vertices':len(vertices),'positions_normals_secondary_uv_preserved':True,
            'texture_size':[2048,2048]}
    (WORK/'validation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report))


def recalculate_tangents(vertices, indices):
    """Rebuild the tangent basis after UV seams or vertex sharing change."""
    tangents = [Vector() for v in vertices]
    bitangents = [Vector() for v in vertices]
    for fi in range(0, len(indices), 3):
        ids = indices[fi:fi+3]
        a, b, c = [Vector(struct.unpack_from('<3f', vertices[i], 0)) for i in ids]
        ua, ub, uc = [Vector(struct.unpack_from('<2f', vertices[i], 40)) for i in ids]
        e1, e2, d1, d2 = b-a, c-a, ub-ua, uc-ua
        den = d1.x*d2.y-d1.y*d2.x
        if abs(den) > 1e-12:
            tangent = (e1*d2.y-e2*d1.y)/den
            bitangent = (e2*d1.x-e1*d2.x)/den
            for i in ids:
                tangents[i] += tangent
                bitangents[i] += bitangent
    for i, raw_vertex in enumerate(vertices):
        n = Vector(struct.unpack_from('<3f', raw_vertex, 12)).normalized()
        t = (tangents[i]-n*n.dot(tangents[i])).normalized()
        if t.length < 0.1:
            t = n.orthogonal().normalized()
        sign = -1 if n.cross(t).dot(bitangents[i]) < 0 else 1
        struct.pack_into('<4f', raw_vertex, 24, *t, sign)
def compact_vertices(vertices, indices):
    """Weld equal attributes, but preserve UV/normal and mirrored-tangent seams.

    Source IDs and tangent direction are not vertex identity. Recompute tangent
    direction across welded faces, retaining handedness as a mandatory seam.
    Only referenced vertices are emitted. Triangle order/winding is unchanged.
    """
    packed, remapped, lookup = [], [], {}
    for index in indices:
        vertex = vertices[index]
        assert len(vertex) == 56, 'Expected the inspected Pipe vertex layout'
        key = bytes(vertex[:24]) + bytes(vertex[36:])
        if key not in lookup:
            lookup[key] = len(packed)
            packed.append(bytearray(vertex))
        remapped.append(lookup[key])
    recalculate_tangents(packed, remapped)
    return packed, remapped


def write_mesh(original, vertices, indices, path, retained_indices=None):
    assert original['stride'] == 56
    assert original['channels'][0:3] == [(0,0,0,3),(0,12,0,3),(0,24,0,4)]
    assert original['channels'][4:6] == [(0,40,0,2),(0,48,0,2)]
    assert len(vertices) < 65536
    raw = b''.join(vertices)
    text = original['text']
    text = re.sub(r'm_VertexCount: \d+', 'm_VertexCount: '+str(len(vertices)), text)
    text = re.sub(r'    vertexCount: \d+', '    vertexCount: '+str(len(vertices)), text)
    text = re.sub(r'    indexCount: \d+', '    indexCount: '+str(len(indices)), text)
    text = re.sub(r'm_DataSize: \d+', 'm_DataSize: '+str(len(raw)), text)
    text = re.sub(r'_typelessdata: \w+', '_typelessdata: '+raw.hex(), text)
    text = re.sub(r'm_IndexBuffer: \w+', 'm_IndexBuffer: '+struct.pack('<'+'H'*len(indices),*indices).hex(), text)
    # Verify exact triangle positions, normals and secondary UVs survive.
    def signature(buff, stride, ids):
        tris=[]
        for i in range(0,len(ids),3):
            corners=[bytes(buff[j*stride:j*stride+24])+bytes(buff[j*stride+48:j*stride+56]) for j in ids[i:i+3]]
            tris.append(min(tuple(corners[k:]+corners[:k]) for k in range(3)))
        return sorted(tris)
    expected = original['indices'] if retained_indices is None else retained_indices
    assert signature(raw,original['stride'],indices)==signature(original['raw'],original['stride'],expected)
    path.write_text(text)


def cleanup_unity_mesh():
    """Generate a compact asset with exact per-corner visual attribute checks."""
    source = read_mesh(SOURCE)
    records = [source['raw'][i:i+source['stride']]
               for i in range(0,len(source['raw']),source['stride'])]
    vertices, indices = compact_vertices(records, source['indices'])
    # Unlike a fresh unwrap, cleanup must preserve BOTH UV channels exactly.
    for old, new in zip(source['indices'], indices):
        assert records[old][:24] == vertices[new][:24]
        assert records[old][40:] == vertices[new][40:]
        assert records[old][36:40] == vertices[new][36:40]
    assert len(indices) == len(source['indices'])
    assert len(set(indices)) == len(vertices)
    repeated_vertices, repeated_indices = compact_vertices(vertices, indices)
    assert repeated_vertices == vertices and repeated_indices == indices
    for vertex in vertices:
        values = struct.unpack('<14f', vertex)
        assert all(math.isfinite(v) for v in values)
        normal, tangent = Vector(values[3:6]), Vector(values[6:9])
        assert abs(tangent.length-1) < 1e-5
        assert abs(normal.dot(tangent)) < 1e-5
    backup = WORK/'before_vertex_cleanup'
    backup.mkdir(exist_ok=True)
    assert not (backup/'Pipe.asset').exists(), 'Do not overwrite cleanup backup'
    shutil.copy2(SOURCE, backup/'Pipe.asset')
    write_mesh(source, vertices, indices, WORK/'Pipe_Baked.asset')
    report = dict(vertices_before=len(records), vertices_after=len(vertices),
                  removed_vertices=len(records)-len(vertices),
                  triangles_before=len(indices)//3, triangles_after=len(indices)//3,
                  positions_normals_uv0_uv1_winding_preserved=True,
                  tangent_handedness_preserved=True, tangent_basis_valid=True,
                  idempotent=True,
                  source_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest())
    (WORK/'vertex_cleanup_validation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report))
