"""Run in the open Blender review scene. Rebuild paint and bake a padded UV atlas.

Only UVs and their tangent basis are patched in the Unity ASCII FBX, preserving
geometry IDs, node names, topology, normals, transforms and prefab references.
"""
import bpy, json, re, math
from pathlib import Path
from mathutils import Vector

work=Path(__file__).resolve().parent
asset=work.parents[1]/'FactorioProject/Assets/MapObject/Fluid/Water pump'
obj=bpy.data.objects['Water_pump'];mesh=obj.data;scene=bpy.context.scene
if obj.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
components=json.loads((work/'components.json').read_text())
source=(asset/'Water pump.fbx').read_text()
mesh.materials.clear()
for mat in list(bpy.data.materials):
    if mat.name.startswith(('WP_', 'WaterPump_Clean')):
        bpy.data.materials.remove(mat)
for img in list(bpy.data.images):
    if img.name.startswith('WaterPump_Clean_Atlas'):
        bpy.data.images.remove(img)
palette={
    'Paint':(0.010,0.10,0.38,1),
    'Steel':(0.39,0.44,0.48,1),
    'Base':(0.028,0.039,0.05,1),
    'Brass':(0.52,0.30,0.065,1),
    'Dark':(0.008,0.015,0.024,1),
}
materials={}
def math_node(nodes,links,op,a,b=0):
    n=nodes.new('ShaderNodeMath');n.operation=op
    for i,value in enumerate([a,b]):
        if isinstance(value,(float,int)):n.inputs[i].default_value=value
        else:links.new(value,n.inputs[i])
    return n.outputs[0]
def material(name,color,grille=False):
    mat=bpy.data.materials.new('WP_'+name);mat.use_nodes=True
    nodes=mat.node_tree.nodes;links=mat.node_tree.links;nodes.clear()
    out=nodes.new('ShaderNodeOutputMaterial');emit=nodes.new('ShaderNodeEmission')
    rgb=nodes.new('ShaderNodeRGB');rgb.outputs[0].default_value=color
    color_out=rgb.outputs[0]
    if grille:
        geo=nodes.new('ShaderNodeNewGeometry');sep=nodes.new('ShaderNodeSeparateXYZ');links.new(geo.outputs['Position'],sep.inputs[0])
        x=sep.outputs['X'];z=math_node(nodes,links,'SUBTRACT',sep.outputs['Z'],0.3711)
        radius=math_node(nodes,links,'SQRT',math_node(nodes,links,'ADD',math_node(nodes,links,'MULTIPLY',x,x),math_node(nodes,links,'MULTIPLY',z,z)))
        # Straight staggered rows on the physical rear cap, independent of atlas rotation.
        row=math_node(nodes,links,'FLOOR',math_node(nodes,links,'DIVIDE',z,0.016))
        stagger=math_node(nodes,links,'MULTIPLY',math_node(nodes,links,'PINGPONG',row,1),0.009)
        gx=math_node(nodes,links,'SUBTRACT',math_node(nodes,links,'FLOORED_MODULO',math_node(nodes,links,'ADD',x,stagger),0.018),0.009)
        gz=math_node(nodes,links,'SUBTRACT',math_node(nodes,links,'FLOORED_MODULO',z,0.016),0.008)
        dist=math_node(nodes,links,'SQRT',math_node(nodes,links,'ADD',math_node(nodes,links,'MULTIPLY',gx,gx),math_node(nodes,links,'MULTIPLY',gz,gz)))
        holes=math_node(nodes,links,'LESS_THAN',dist,0.0051)
        ring=math_node(nodes,links,'MULTIPLY',math_node(nodes,links,'LESS_THAN',radius,0.148),math_node(nodes,links,'GREATER_THAN',radius,0.047))
        mask=math_node(nodes,links,'MULTIPLY',holes,ring)
        mix=nodes.new('ShaderNodeMixRGB');links.new(mask,mix.inputs[0]);links.new(color_out,mix.inputs[1]);mix.inputs[2].default_value=palette['Dark'];color_out=mix.outputs[0]
        center=nodes.new('ShaderNodeMixRGB');links.new(math_node(nodes,links,'LESS_THAN',radius,0.031),center.inputs[0]);links.new(color_out,center.inputs[1]);center.inputs[2].default_value=palette['Steel'];color_out=center.outputs[0]
    ao=nodes.new('ShaderNodeAmbientOcclusion');ao.inputs['Distance'].default_value=0.065;ao.samples=16
    mult=nodes.new('ShaderNodeMixRGB');mult.blend_type='MULTIPLY';mult.inputs[0].default_value=0.25
    links.new(color_out,mult.inputs[1]);links.new(ao.outputs['AO'],mult.inputs[2]);links.new(mult.outputs[0],emit.inputs['Color']);links.new(emit.outputs[0],out.inputs['Surface'])
    materials[name]=mat
    return mat
mesh.materials.clear()
for name,color in palette.items():mesh.materials.append(material(name,color))
mesh.materials.append(material('Grille',palette['Paint'],True))
slots={name:i for i,name in enumerate(materials)}
steel={12,13,14,15,16,17,19,20,21,26,27}
brass={3,9,10,29}
base={4,5,36}
for component in components:
    ci=component['id']
    name='Steel' if ci in steel else 'Brass' if ci in brass else 'Base' if ci in base else 'Paint'
    for pi in component['faces']:
        p=mesh.polygons[pi];chosen=name
        if ci==0:
            depth=[-mesh.vertices[v].co.y for v in p.vertices]
            # Whole inlet lip surfaces receive one material, including the rear annulus.
            # Sampling AI colors per triangle creates alternating silver/blue teeth here.
            if max(depth)>.474 or min(depth)>.426:
                chosen='Steel'
            # Four blind mounting bores end at these recessed, nearly planar faces.
            if max(depth)-min(depth)<.004 and .450<sum(depth)/len(depth)<.471:
                chosen='Dark'
            if min(depth)>.390 and max(depth)<.400:
                chosen='Dark'
        elif ci==2 and p.center.y>0.469:
            chosen='Grille'
        p.material_index=slots[chosen]
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=0.012,area_weight=0.0,correct_aspect=True,scale_to_bounds=False)
bpy.ops.uv.pack_islands(rotate=True,margin=0.008)
bpy.ops.object.mode_set(mode='OBJECT')
mesh.uv_layers.active.name='UVSet0'
image=bpy.data.images.new('WaterPump_Clean_Atlas',width=2048,height=2048,alpha=False)
image.generated_color=(0.015,0.145,0.32,1)
image.colorspace_settings.name='sRGB'
for mat in materials.values():
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;mat.node_tree.nodes.active=node;node.select=True
scene.render.engine='CYCLES';scene.cycles.samples=16
scene.render.bake.margin=8;scene.render.bake.use_clear=True
bpy.ops.object.bake(type='EMIT')
image.filepath_raw=str(work/'Water pump_TB.png');image.file_format='PNG';image.save()
# Keep a source scene with editable material zones before collapsing the game material.
bpy.ops.wm.save_as_mainfile(filepath=str(work/'WaterPump_material_source.blend'))
final=bpy.data.materials.new('WaterPump_Clean');final.use_nodes=True
bsdf=final.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=.65
tex=final.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image;final.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
mesh.materials.clear();mesh.materials.append(final)
for p in mesh.polygons:p.material_index=0
def replace_array(text,key,values):
    data=list(values)
    body=','.join(format(float(v),'.9g') for v in data)
    replacement=key+': *'+str(len(data))+' {\n\t\t\t\ta: '+body+'\n\t\t\t}'
    text,count=re.subn(r'\b'+key+r': \*\d+\s*\{\s*a:\s*[^}]+}',lambda m:replacement,text,count=1)
    assert count==1,key
    return text
uv=mesh.uv_layers.active.data
candidate=replace_array(source,'UV',(v for loop in uv for v in loop.uv))
candidate=replace_array(candidate,'UVIndex',range(len(uv)))
mesh.calc_tangents(uvmap='UVSet0')
candidate=replace_array(candidate,'Tangents',(v for loop in mesh.loops for v in (loop.tangent.x,loop.tangent.z,-loop.tangent.y)))
candidate=replace_array(candidate,'Binormals',(v for loop in mesh.loops for v in (loop.bitangent.x,loop.bitangent.z,-loop.bitangent.y)))
(work/'Water pump.fbx').write_text(candidate)
def render(name,position):
    cam=scene.camera;cam.location=position;cam.rotation_euler=(Vector((0,0,.36))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(work/(name+'.png'));bpy.ops.render.render(write_still=True)
scene.cycles.samples=24
render('after_front',(1.2,-1.8,1.1));render('after_back',(-1.2,1.8,1.05))
bpy.ops.wm.save_as_mainfile(filepath=str(work/'WaterPump_clean.blend'))
