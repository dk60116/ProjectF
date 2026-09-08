import bpy
from pathlib import Path
from mathutils import Vector
work=Path(__file__).resolve().parent
asset=work.parents[1]/'FactorioProject/Assets/MapObject/Fluid/Water pump'
for filename in ['WaterPump_material_source.blend','WaterPump_clean.blend']:
    bpy.ops.wm.open_mainfile(filepath=str(work/filename))
    obj=bpy.data.objects['Water_pump']
    if obj.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
    for other in bpy.context.scene.objects:
        other.select_set(other==obj)
        if other.type in {'LIGHT','CAMERA'}:other.hide_set(True)
    bpy.context.view_layer.objects.active=obj
    used_images=set()
    for mat in obj.data.materials:
        for node in mat.node_tree.nodes:
            if node.type=='TEX_IMAGE' and node.image:
                node.image.filepath=bpy.path.relpath(str(asset/'Water pump_TB.png'))
                node.image.pack()
                used_images.add(node.image)
    for img in list(bpy.data.images):
        if img.users==0 and img.type!='RENDER_RESULT':bpy.data.images.remove(img)
    for mat in list(bpy.data.materials):
        if mat.users==0:bpy.data.materials.remove(mat)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if screen.name=='Layout' and area.type=='CONSOLE':area.type='VIEW_3D'
            if area.type=='VIEW_3D':
                space=area.spaces.active;space.shading.type='MATERIAL';space.overlay.show_overlays=False
                space.region_3d.view_location=Vector((0,0,.36));space.region_3d.view_distance=1.55
                space.region_3d.view_rotation=(Vector((1.2,-1.8,1.1))-Vector((0,0,.36))).to_track_quat('Z','Y')
                space.region_3d.view_perspective='ORTHO'
            elif area.type=='IMAGE_EDITOR' and used_images:
                area.spaces.active.image=next(iter(used_images))
    if bpy.context.window:bpy.context.window.workspace=bpy.data.workspaces['Layout']
    bpy.ops.wm.save_as_mainfile(filepath=str(work/filename))
