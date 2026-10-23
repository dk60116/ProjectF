"""Reproject the backed-up splitter albedo from UV_Original to UV_Unwrapped.

Run with Blender --background --factory-startup --python this_file.
Produces staging assets and before/after renders; deployment is a separate copy.
"""
import bpy
import json
import math
import shutil
from pathlib import Path
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'ArtSource/SpliterBelt'
ASSET = ROOT / 'FactorioProject/Assets/MapObject/Belt/Spliter belt'
BACKUP = OUT / 'Backups/Spliter belt-original.png'
if not BACKUP.exists():
    shutil.copy2(ASSET / 'Spliter belt.png', BACKUP)
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'Spliter belt_UV.blend'))
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
obj = bpy.data.objects['Spliter_belt']
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
mesh = obj.data
assert [u.name for u in mesh.uv_layers] == ['UV_Unwrapped', 'UV_Original']
mesh.uv_layers.active_index = 0
mesh.uv_layers[0].active_render = True
original = bpy.data.images.load(str(BACKUP), check_existing=False)
original.colorspace_settings.name = 'sRGB'
original.pack()
baked = bpy.data.images.new('Spliter belt_Baked', width=1024, height=1024, alpha=True)
baked.generated_color = (0, 0, 0, 1)
baked.colorspace_settings.name = 'sRGB'
mat = bpy.data.materials.new('M_Spliter belt')
mat.use_nodes = True
mesh.materials.clear()
mesh.materials.append(mat)
nodes, links = mat.node_tree.nodes, mat.node_tree.links
nodes.clear()
output = nodes.new('ShaderNodeOutputMaterial')
emission = nodes.new('ShaderNodeEmission')
source = nodes.new('ShaderNodeTexImage')
source.image = original
source.label = 'Original albedo (UV_Original)'
source_uv = nodes.new('ShaderNodeUVMap')
source_uv.uv_map = 'UV_Original'
links.new(source_uv.outputs['UV'], source.inputs['Vector'])
links.new(source.outputs['Color'], emission.inputs['Color'])
links.new(emission.outputs[0], output.inputs['Surface'])
target = nodes.new('ShaderNodeTexImage')
target.image = baked
target.label = 'Reprojected albedo (UV_Unwrapped)'
target_uv = nodes.new('ShaderNodeUVMap')
target_uv.uv_map = 'UV_Unwrapped'
links.new(target_uv.outputs['UV'], target.inputs['Vector'])
nodes.active = target
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 1
scene.render.bake.use_selected_to_active = False
scene.render.bake.use_clear = True
scene.render.bake.margin = 4
scene.render.bake.margin_type = 'EXTEND'
bpy.ops.object.bake(type='EMIT')
baked.filepath_raw = str(OUT / 'Spliter belt_Baked.png')
baked.file_format = 'PNG'
baked.save()
pixels = np.array(baked.pixels[:], dtype=np.float32).reshape(-1, 4)
assert np.isfinite(pixels).all() and float(pixels[:, :3].max()) > 0.25

# Same geometry and camera, with unlit albedo, isolate UV reprojection error.
camera_data = bpy.data.cameras.new('BakeValidationCamera')
camera = bpy.data.objects.new('BakeValidationCamera', camera_data)
scene.collection.objects.link(camera)
center = sum((obj.matrix_world @ Vector(corner) for corner in obj.bound_box), Vector()) / 8
extent = max(obj.dimensions)
camera.location = center + Vector((3, -4, 3)).normalized() * extent * 3
camera.rotation_euler = (center-camera.location).to_track_quat('-Z', 'Y').to_euler()
camera_data.type = 'ORTHO'
camera_data.ortho_scale = extent * 1.65
camera_data.clip_start = 0.001
scene.camera = camera
scene.render.resolution_x = 512
scene.render.resolution_y = 512
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.view_settings.view_transform = 'Standard'
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
preview = OUT / 'BakePreview'
preview.mkdir(exist_ok=True)
scene.render.filepath = str(preview / 'Original.png')
bpy.ops.render.render(write_still=True)
links.new(target.outputs['Color'], emission.inputs['Color'])
scene.render.filepath = str(preview / 'Baked.png')
bpy.ops.render.render(write_still=True)
images = [bpy.data.images.load(str(preview / name), check_existing=False) for name in ('Original.png', 'Baked.png')]
before, after = [np.array(image.pixels[:], dtype=np.float32).reshape(-1, 4) for image in images]
mask = (before[:, 3] > 0.99) & (after[:, 3] > 0.99)
assert mask.sum() > 1000
error = np.abs(before[mask, :3] - after[mask, :3])
report = {'resolution': [1024, 1024], 'source_uv': 'UV_Original', 'target_uv': 'UV_Unwrapped', 'bake_type': 'EMIT', 'margin_pixels': 4, 'compared_pixels': int(mask.sum()), 'mean_linear_rgb_error': float(error.mean()), 'p95_linear_rgb_error': float(np.percentile(error, 95))}
(OUT / 'bake-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
assert report['mean_linear_rgb_error'] < 0.04, report

# Restore the final ordinary surface material and replace the checker display.
principled = nodes.new('ShaderNodeBsdfPrincipled')
links.new(target.outputs['Color'], principled.inputs['Base Color'])
principled.inputs['Roughness'].default_value = 0.65
links.new(principled.outputs[0], output.inputs['Surface'])
nodes.remove(emission)
nodes.active = target
bpy.data.objects.remove(camera, do_unlink=True)
bpy.data.cameras.remove(camera_data)
baked.pack()
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'IMAGE_EDITOR':
            area.spaces.active.image = baked
obj['UV_notes'] = 'UV_Unwrapped uses baked albedo. UV_Original and packed original texture are retained for rebaking.'
bpy.ops.export_scene.fbx(filepath=str(OUT / 'Spliter belt_UV.fbx'), use_selection=True, object_types={'MESH'}, use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False, axis_forward='-Z', axis_up='Y', path_mode='AUTO')
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'Spliter belt_UV.blend'))
print('BAKE_RESULT ' + json.dumps(report))
