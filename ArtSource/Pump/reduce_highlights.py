"""Reduce baked-in Pump highlights through Blender's material bake.

Run preview() then inspect the live viewport before bake_and_save().
Luminance compression preserves hue and leaves dark pixels unchanged.
"""
import bpy
import hashlib
import json
import shutil
from pathlib import Path

WORK = Path(__file__).resolve().parent
ASSET = WORK.parents[1] / 'FactorioProject/Assets/MapObject/Fluid/Pump'
BACKUP = WORK / 'original/before_highlight_reduction'


def preview():
    obj = bpy.data.objects['Pump']
    if obj.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    BACKUP.mkdir(parents=True, exist_ok=True)
    for name in ('Pump_TB.png', 'Pump_clean.blend'):
        if not (BACKUP / name).exists():
            shutil.copy2(WORK / name, BACKUP / name)
    bpy.ops.wm.save_as_mainfile(filepath=str(BACKUP / 'session.blend'), copy=True)
    mat = obj.data.materials[0]
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    assert len(nodes) == 3, 'Expected inspected simple Pump material'
    bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
    tex = next(n for n in nodes if n.type == 'TEX_IMAGE')
    # Read the backed-up atlas so the adjustment cannot compound on reruns.
    tex.image = bpy.data.images.load(str(BACKUP / 'Pump_TB.png'), check_existing=True)
    tex.image.pack()
    lum = nodes.new('ShaderNodeRGBToBW')
    links.new(tex.outputs['Color'], lum.inputs['Color'])
    def math_node(operation, a, b):
        n = nodes.new('ShaderNodeMath')
        n.operation = operation
        for index, value in enumerate((a, b)):
            if isinstance(value, (int, float)):
                n.inputs[index].default_value = value
            else:
                links.new(value, n.inputs[index])
        return n.outputs[0]
    excess = math_node('MAXIMUM', math_node('SUBTRACT', lum.outputs['Val'], .08), 0.)
    scale = math_node('DIVIDE', 1., math_node('ADD', 1., math_node('MULTIPLY', excess, 2.)))
    result = nodes.new('ShaderNodeMixRGB')
    result.label = 'Pump highlight compression'
    result.blend_type = 'MULTIPLY'
    result.inputs[0].default_value = 1.
    links.new(tex.outputs['Color'], result.inputs[1])
    links.new(scale, result.inputs[2])
    links.new(result.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = .85
    bsdf.inputs['Specular IOR Level'].default_value = .2
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                area.spaces.active.overlay.show_overlays = False
    print('Highlight compression preview ready; dark linear luminance <= 0.08 stays unchanged')


def bake_and_save():
    obj = bpy.data.objects['Pump']
    if obj.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    mat = obj.data.materials[0]
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
    output = next(n for n in nodes if n.type == 'OUTPUT_MATERIAL')
    adjusted = bsdf.inputs['Base Color'].links[0].from_socket
    emit = nodes.new('ShaderNodeEmission')
    links.new(adjusted, emit.inputs['Color'])
    links.new(emit.outputs['Emission'], output.inputs['Surface'])
    atlas = bpy.data.images['Pump_Atlas']
    target = nodes.new('ShaderNodeTexImage')
    target.image = atlas
    nodes.active = target
    scene = bpy.context.scene
    scene.cycles.samples = 1
    scene.render.bake.margin = 8
    scene.render.bake.margin_type = 'EXTEND'
    bpy.ops.object.bake(type='EMIT', use_clear=True)
    atlas.filepath_raw = str(WORK / 'Pump_TB.png')
    atlas.file_format = 'PNG'
    atlas.save()
    atlas.pack()
    links.new(target.outputs['Color'], bsdf.inputs['Base Color'])
    links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
    for node in list(nodes):
        if node not in (target, bsdf, output):
            nodes.remove(node)
    target.location = (-320, 80)
    bsdf.location = (0, 80)
    output.location = (320, 80)
    atlas.filepath = '//Pump_TB.png'
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'IMAGE_EDITOR':
                area.spaces.active.image = atlas
    assert (ASSET / 'Pump_TB.png').read_bytes() == (BACKUP / 'Pump_TB.png').read_bytes(), 'Project texture changed during edit'
    shutil.copy2(WORK / 'Pump_TB.png', ASSET / 'Pump_TB.png')
    bpy.ops.wm.save_as_mainfile(filepath=str(WORK / 'Pump_clean.blend'))
    report = dict(operation='Hue-preserving baked highlight compression',
                  linear_luminance_threshold=.08, compression_strength=2.,
                  blender_roughness=.85, blender_specular_ior_level=.2,
                  fbx_sha256=hashlib.sha256((ASSET/'Pump.fbx').read_bytes()).hexdigest(),
                  texture_sha256=hashlib.sha256((ASSET/'Pump_TB.png').read_bytes()).hexdigest(),
                  source_texture_sha256=hashlib.sha256((BACKUP/'Pump_TB.png').read_bytes()).hexdigest())
    uv_report = json.loads((WORK/'uv_cleanup.json').read_text())
    assert report['fbx_sha256'] == uv_report['fbx_sha256'], 'Mesh changed'
    uv_report['installed_matches_validated'] = False
    uv_report['texture_superseded_by'] = 'highlight_reduction.json'
    (WORK/'uv_cleanup.json').write_text(json.dumps(uv_report, indent=2))
    (WORK/'highlight_reduction.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))
