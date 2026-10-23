"""Blender 4.5: import the static Unity ASCII FBX and create a UV editing source.

Run with blender --background --factory-startup --python this_file.
Only the verified single-mesh, identity-transform ASCII layout is supported.
The Unity source FBX and its existing texture are never overwritten.
"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from uv_atlas import overlapping_faces
import bpy
import json
import math
import re
from bpy_extras.io_utils import axis_conversion
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'FactorioProject/Assets/MapObject/Belt/Spliter belt/Spliter belt.fbx'
OUT = ROOT / 'ArtSource/SpliterBelt'
OUT.mkdir(parents=True, exist_ok=True)
if SOURCE.read_bytes().startswith(b'Kaydara FBX Binary'):
    backups = sorted((OUT / 'Backups').glob('Spliter belt-before-UV-*.fbx'))
    assert backups, 'The original ASCII FBX backup is required for rebuilding UVs.'
    SOURCE = backups[0]
text = SOURCE.read_text(encoding='utf-8-sig')
assert len(re.findall(r'\bGeometry: \d+', text)) == 1
assert len(re.findall(r'\bModel: \d+', text)) == 1
model_block = text[text.index('\n\tModel:'):text.index('\n\tMaterial:')]
assert not re.search(r'P: "Lcl (Translation|Rotation|Scaling)"', model_block)
assert '"UnitScaleFactor", "double", "Number", "",1' in text

def array(name, cast=float):
    match = re.search(r'\b' + name + r': \*(\d+)\s*\{\s*a:\s*([^}]+)\}', text)
    assert match, name
    values = [cast(v.strip()) for v in match[2].split(',') if v.strip()]
    assert len(values) == int(match[1]), name
    return values

positions = array('Vertices')
indices = array('PolygonVertexIndex', int)
source_uvs = array('UV')
uv_indices = array('UVIndex', int)
normals = array('Normals')
assert len(uv_indices) == len(indices) == len(normals) // 3
rotation = axis_conversion(from_forward='Z', from_up='Y', to_forward='-Y', to_up='Z')
vertices = [rotation @ Vector(positions[i:i+3]) * 0.01 for i in range(0, len(positions), 3)]
faces, face = [], []
for index in indices:
    face.append(-index - 1 if index < 0 else index)
    if index < 0:
        faces.append(face)
        face = []
assert not face
bpy.ops.wm.read_factory_settings(use_empty=True)
mesh = bpy.data.meshes.new('Scene')
mesh.from_pydata(vertices, [], faces)
mesh.update()
assert len(mesh.vertices) == len(vertices) and len(mesh.loops) == len(indices)
obj = bpy.data.objects.new('Spliter_belt', mesh)
bpy.context.collection.objects.link(obj)
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
new_uv = mesh.uv_layers.new(name='UV_Unwrapped')
original_uv = mesh.uv_layers.new(name='UV_Original')
for loop, index in zip(original_uv.data, uv_indices):
    loop.uv = source_uvs[2*index:2*index+2]
loop_normals = [rotation @ Vector(normals[i:i+3]) for i in range(0, len(normals), 3)]
mesh.normals_split_custom_set(loop_normals)
mesh.uv_layers.active_index = 0
new_uv.active_render = True
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.012, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
bpy.ops.uv.average_islands_scale()
bpy.ops.uv.pack_islands(udim_source='ACTIVE_UDIM', rotate=True, margin_method='FRACTION', margin=0.008)
bpy.ops.object.mode_set(mode='OBJECT')
new_uv = mesh.uv_layers['UV_Unwrapped']
original_uv = mesh.uv_layers['UV_Original']

# Coincident/back-facing source faces can project onto each other. Give those
# faces independent UV islands without deleting or changing source geometry.
bad_faces = overlapping_faces(mesh, new_uv)
for offset, face_index in enumerate(sorted(bad_faces)):
    for loop_index in mesh.polygons[face_index].loop_indices:
        new_uv.data[loop_index].uv.x += 2 * (offset + 1)
if bad_faces:
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(udim_source='ACTIVE_UDIM', rotate=True, margin_method='FRACTION', margin=0.008)
    bpy.ops.object.mode_set(mode='OBJECT')
    new_uv = mesh.uv_layers['UV_Unwrapped']
    original_uv = mesh.uv_layers['UV_Original']
assert not overlapping_faces(mesh, new_uv), 'UV islands still overlap'
coords = [tuple(loop.uv) for loop in new_uv.data]
assert all(math.isfinite(v) and -1e-5 <= v <= 1.00001 for uv in coords for v in uv)
degenerate = 0
for polygon in mesh.polygons:
    uv = [Vector(coords[i]) for i in polygon.loop_indices]
    area = abs(sum(uv[i].x*uv[(i+1)%len(uv)].y-uv[(i+1)%len(uv)].x*uv[i].y for i in range(len(uv)))) / 2
    if polygon.area > 1e-12 and area < 1e-12:
        degenerate += 1
assert degenerate == 0, f'{degenerate} collapsed UV faces'
assert all((loop.uv - Vector(source_uvs[2*index:2*index+2])).length < 1e-6 for loop, index in zip(original_uv.data, uv_indices))

checker = bpy.data.images.new('UV_Checker', width=1024, height=1024)
checker.generated_type = 'COLOR_GRID'
checker.filepath_raw = str(OUT / 'UV_Checker.png')
checker.file_format = 'PNG'
checker.save()
checker.pack()
mat = bpy.data.materials.new('Spliter_UV_Check')
mat.use_nodes = True
nodes = mat.node_tree.nodes
tex = nodes.new('ShaderNodeTexImage')
tex.image = checker
uvnode = nodes.new('ShaderNodeUVMap')
uvnode.uv_map = 'UV_Unwrapped'
mat.node_tree.links.new(uvnode.outputs['UV'], tex.inputs['Vector'])
mat.node_tree.links.new(tex.outputs['Color'], nodes.get('Principled BSDF').inputs['Base Color'])
mesh.materials.append(mat)
old_tex = nodes.new('ShaderNodeTexImage')
old_tex.label = 'Original texture - use UV_Original'
texture_path = OUT / 'Backups/Spliter belt-original.png'
if not texture_path.exists():
    texture_path = ROOT / 'FactorioProject/Assets/MapObject/Belt/Spliter belt/Spliter belt.png'
if texture_path.exists():
    old_tex.image = bpy.data.images.load(str(texture_path))
    old_tex.image.pack()
old_map = nodes.new('ShaderNodeUVMap')
old_map.uv_map = 'UV_Original'
mat.node_tree.links.new(old_map.outputs['UV'], old_tex.inputs['Vector'])
nodes.active = tex
obj['UV_notes'] = 'UV_Unwrapped: packed non-overlapping atlas. UV_Original preserves the Unity texture layout.'

# A portable vector layout for paint/inspection, without requiring Blender UI.
size = 1024
svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}">', '<rect width="1024" height="1024" fill="#20252d"/>', '<g fill="#536f8b" stroke="#bad5ef" stroke-width="0.6">']
for polygon in mesh.polygons:
    points = ' '.join(f'{coords[i][0]*size:.3f},{(1-coords[i][1])*size:.3f}' for i in polygon.loop_indices)
    svg.append(f'<polygon points="{points}"/>')
svg.extend(['</g>', '</svg>'])
(OUT / 'Spliter belt_UV.svg').write_text('\n'.join(svg), encoding='utf-8')

# Export the editing model with the new UV as channel 0 and the original as backup.
bpy.ops.export_scene.fbx(filepath=str(OUT / 'Spliter belt_UV.fbx'), use_selection=True, object_types={'MESH'}, use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False, axis_forward='-Z', axis_up='Y', path_mode='AUTO')
report = {'source': str(SOURCE), 'vertices': len(mesh.vertices), 'faces': len(mesh.polygons), 'uv_channels': [u.name for u in mesh.uv_layers], 'collapsed_uv_faces': degenerate, 'uv_overlap_pairs': 0, 'uv_bounds': [[min(u[i] for u in coords), max(u[i] for u in coords)] for i in range(2)], 'original_uv_preserved': True}
(OUT / 'uv-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')

# Save an immediately editable UV workspace.
workspace = bpy.data.workspaces.get('UV Editing')
if workspace and bpy.context.window:
    bpy.context.window.workspace = workspace
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'IMAGE_EDITOR':
            area.spaces.active.image = checker
        elif area.type == 'VIEW_3D':
            space = area.spaces.active
            space.shading.type = 'MATERIAL'
            if space.region_3d:
                space.region_3d.view_location = sum((Vector(v) for v in vertices), Vector()) / len(vertices)
                space.region_3d.view_distance = max(obj.dimensions) * 2.2
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'Spliter belt_UV.blend'))
print('UV_RESULT ' + json.dumps(report))
