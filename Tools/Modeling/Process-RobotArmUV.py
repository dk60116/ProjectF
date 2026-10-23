"""Blender UV unwrap/albedo rebake of the six meshes used by Robot arm.prefab.

Staging only: patch UV arrays inside the original ASCII FBX, retaining all FBX
IDs, model transforms, hierarchy, geometry, normals and material connections.
Texture assignments were resolved from prefab mesh/material references and
Unity's imported subasset names. Unused legacy meshes retain their source UVs.
"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from uv_atlas import overlapping_faces
import bpy
import re
import json
import math
import shutil
import numpy as np
from mathutils import Vector
from bpy_extras.io_utils import axis_conversion

ROOT = Path(__file__).resolve().parents[2]
ASSET = ROOT / 'FactorioProject/Assets/MapObject/InputOutputModule/Robot arm'
OUT = ROOT / 'ArtSource/RobotArm'
BACKUP = OUT / 'Backups'
BACKUP.mkdir(parents=True, exist_ok=True)
SPECS = {
    'Robot arm_Body.fbx': {
        'tripo_part_15': 'Robot arm_TB_0.png',
        'tripo_part_17': 'Robot arm_TB_1.png',
        'tripo_part_new_0': 'Robot arm_TB_2.png',
    },
    'Robot arm_Hand.fbx': {
        'tripo_part_1': 'Robot arm Hand_TB_0.png',
        'tripo_part_7': 'Robot arm Hand_TB_1.png',
    },
    'Robot arm_Bottom.fbx': {'Robot_Arm_Bottom': 'Robot arm_Bottom_TB.png'},
}
rotation = axis_conversion(from_forward='Z', from_up='Y', to_forward='-Y', to_up='Z')

def backup(name):
    path = BACKUP / name
    if not path.exists():
        shutil.copy2(ASSET / name, path)
    return path

def sections(text, kind):
    for match in re.finditer(r'^\t' + kind + r': (\d+), "[^"\n]*", "[^"\n]*" \{', text, re.M):
        start = match.start()
        depth = 1
        end = match.end()
        while depth:
            depth += (text[end] == '{') - (text[end] == '}')
            end += 1
        yield int(match[1]), start, end, text[start:end]

def array_pattern(name):
    return r'\b' + name + r': \*(\d+)\s*\{\s*a:\s*([^}]+)\}'

def array(block, name, cast=float):
    match = re.search(array_pattern(name), block)
    assert match, name
    data = [cast(v.strip()) for v in match[2].split(',') if v.strip()]
    assert len(data) == int(match[1])
    return data

def replace_array(block, name, values):
    data = ','.join(format(v, '.9g') if isinstance(v, float) else str(v) for v in values)
    return re.sub(array_pattern(name), f'{name}: *{len(values)} {{\n\t\t\t\ta: {data}\n\t\t\t}}', block, count=1)

def geometry_unchanged(before, after):
    for name in ('UV', 'UVIndex'):
        before = re.sub(array_pattern(name), name, before)
        after = re.sub(array_pattern(name), name, after)
    assert before == after, 'Non-UV source data changed'

def process(block, model_name, texture_name, label):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    vertices = array(block, 'Vertices')
    indices = array(block, 'PolygonVertexIndex', int)
    normals = array(block, 'Normals')
    old_uv = array(block, 'UV')
    old_indices = array(block, 'UVIndex', int)
    faces, face = [], []
    for i in indices:
        face.append(-i-1 if i < 0 else i)
        if i < 0:
            faces.append(face)
            face = []
    assert not face and all(len(f) == 3 for f in faces)
    mesh = bpy.data.meshes.new(model_name)
    mesh.from_pydata([rotation @ Vector(vertices[i:i+3])*0.01 for i in range(0, len(vertices), 3)], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(model_name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    mesh.uv_layers.new(name='UV_Unwrapped')
    original_uv = mesh.uv_layers.new(name='UV_Original')
    assert len(mesh.loops) == len(indices) == len(old_indices) == len(normals)//3
    for loop, idx in zip(original_uv.data, old_indices):
        loop.uv = old_uv[2*idx:2*idx+2]
    mesh.normals_split_custom_set([rotation @ Vector(normals[i:i+3]) for i in range(0, len(normals), 3)])
    mesh.uv_layers.active_index = 0
    mesh.uv_layers[0].active_render = True
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.012, correct_aspect=True)
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(udim_source='ACTIVE_UDIM', rotate=True, margin_method='FRACTION', margin=0.008)
    bpy.ops.object.mode_set(mode='OBJECT')
    uv = mesh.uv_layers[0]
    bad = overlapping_faces(mesh, uv)
    for offset, face_index in enumerate(sorted(bad)):
        for loop_index in mesh.polygons[face_index].loop_indices:
            uv.data[loop_index].uv.x += 2*(offset+1)
    if bad:
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.select_all(action='SELECT')
        bpy.ops.uv.pack_islands(udim_source='ACTIVE_UDIM', rotate=True, margin_method='FRACTION', margin=0.008)
        bpy.ops.object.mode_set(mode='OBJECT')
        uv = mesh.uv_layers[0]
    assert not overlapping_faces(mesh, uv)
    coords = [tuple(loop.uv) for loop in uv.data]
    assert all(math.isfinite(v) and -1e-5 <= v <= 1.00001 for pair in coords for v in pair)
    for face in mesh.polygons:
        a, b, c = [Vector(coords[i]) for i in face.loop_indices]
        area = abs((b.x-a.x)*(c.y-a.y)-(c.x-a.x)*(b.y-a.y))
        assert face.area < 1e-12 or area > 1e-12, 'Collapsed UV face'
    image = bpy.data.images.load(str(backup(texture_name)))
    image.colorspace_settings.name = 'sRGB'
    image.pack()
    target_image = bpy.data.images.new(texture_name, width=1024, height=1024, alpha=True)
    target_image.generated_color = (0, 0, 0, 1)
    target_image.colorspace_settings.name = 'sRGB'
    mat = bpy.data.materials.new(label + '_Baked')
    mat.use_nodes = True
    mesh.materials.append(mat)
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    output = nodes.new('ShaderNodeOutputMaterial')
    emit = nodes.new('ShaderNodeEmission')
    source = nodes.new('ShaderNodeTexImage'); source.image = image
    source_uv = nodes.new('ShaderNodeUVMap'); source_uv.uv_map = 'UV_Original'
    links.new(source_uv.outputs['UV'], source.inputs['Vector'])
    links.new(source.outputs['Color'], emit.inputs['Color'])
    links.new(emit.outputs[0], output.inputs['Surface'])
    target = nodes.new('ShaderNodeTexImage'); target.image = target_image
    target_uv = nodes.new('ShaderNodeUVMap'); target_uv.uv_map = 'UV_Unwrapped'
    links.new(target_uv.outputs['UV'], target.inputs['Vector'])
    nodes.active = target
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 1
    scene.render.bake.margin = 4; scene.render.bake.margin_type = 'EXTEND'
    scene.render.bake.use_selected_to_active = False
    bpy.ops.object.bake(type='EMIT')
    target_image.filepath_raw = str(OUT / texture_name)
    target_image.file_format = 'PNG'
    target_image.save()

    # Unlit before/after validation isolates albedo transfer from lighting.
    camera_data = bpy.data.cameras.new('Validation')
    camera = bpy.data.objects.new('Validation', camera_data)
    scene.collection.objects.link(camera)
    center = sum((Vector(c) for c in obj.bound_box), Vector())/8
    extent = max(obj.dimensions)
    camera.location = center + Vector((3, -4, 3)).normalized()*extent*3
    camera.rotation_euler = (center-camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera_data.type = 'ORTHO'; camera_data.ortho_scale = extent*1.65; camera_data.clip_start = 0.0001
    scene.camera = camera
    scene.render.resolution_x = 512; scene.render.resolution_y = 512; scene.render.resolution_percentage = 100
    scene.render.film_transparent = True; scene.view_settings.view_transform = 'Standard'
    scene.render.image_settings.file_format = 'PNG'; scene.render.image_settings.color_mode = 'RGBA'
    preview = OUT / 'Previews'; preview.mkdir(exist_ok=True)
    arrays = []
    for state, tex in [('Original', source), ('Baked', target)]:
        links.new(tex.outputs['Color'], emit.inputs['Color'])
        path = preview / (label + '-' + state + '.png')
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        rendered = bpy.data.images.load(str(path))
        arrays.append(np.array(rendered.pixels[:], dtype=np.float32).reshape(-1, 4))
    mask = (arrays[0][:, 3] > 0.99) & (arrays[1][:, 3] > 0.99)
    assert mask.sum() > 500
    error = np.abs(arrays[0][mask, :3]-arrays[1][mask, :3])
    mean = float(error.mean())
    assert mean < 0.04, (label, mean)
    principled = nodes.new('ShaderNodeBsdfPrincipled')
    links.new(target.outputs['Color'], principled.inputs['Base Color'])
    links.new(principled.outputs[0], output.inputs['Surface'])
    nodes.remove(emit)
    target_image.pack()
    bpy.data.objects.remove(camera, do_unlink=True)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'IMAGE_EDITOR': area.spaces.active.image = target_image
            elif area.type == 'VIEW_3D':
                area.spaces.active.shading.type = 'MATERIAL'
                area.spaces.active.region_3d.view_location = center
                area.spaces.active.region_3d.view_distance = extent*2.2
    workspace = bpy.data.workspaces.get('UV Editing')
    if workspace and bpy.context.window: bpy.context.window.workspace = workspace
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / (label + '.blend')))
    patched = replace_array(block, 'UV', [v for pair in coords for v in pair])
    patched = replace_array(patched, 'UVIndex', list(range(len(coords))))
    geometry_unchanged(block, patched)
    report = {'mesh': model_name, 'texture': texture_name, 'vertices': len(vertices)//3, 'faces': len(faces), 'overlap_pairs': 0, 'collapsed_faces': 0, 'render_mean_error': mean, 'render_p95_error': float(np.percentile(error, 95)), 'source_geometry_preserved': True}
    print('PART_RESULT ' + json.dumps(report), flush=True)
    return patched, report

if __name__ == '__main__':
    reports = []
    for filename, mapping in SPECS.items():
        backup(filename + '.meta')
        for texture in mapping.values(): backup(texture + '.meta')
        original = backup(filename).read_text(encoding='utf-8-sig')
        models = {i: re.search(r'"Model::([^"]+)"', b)[1] for i, _, _, b in sections(original, 'Model')}
        connections = {int(a): int(b) for a, b in re.findall(r'C: "OO",(\d+),(\d+)', original)}
        replacements = []
        found = set()
        for geometry_id, start, end, block in sections(original, 'Geometry'):
            model = models[connections[geometry_id]]
            if model not in mapping: continue
            found.add(model)
            updated, report = process(block, model, mapping[model], Path(filename).stem + '-' + model)
            reports.append(report)
            replacements.append((start, end, updated))
        assert found == set(mapping)
        updated = original
        for start, end, value in reversed(replacements): updated = updated[:start] + value + updated[end:]
        geometry_unchanged(original, updated)
        (OUT / filename).write_text(updated, encoding='utf-8')
    (OUT / 'uv-bake-report.json').write_text(json.dumps(reports, indent=2), encoding='utf-8')
    print('PASS all six RobotArm mesh UVs and albedo bakes; original FBX structure preserved', flush=True)
    