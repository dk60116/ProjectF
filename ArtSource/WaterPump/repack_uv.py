"""Repack existing UV islands and transfer the ORIGINAL texture, without repainting.

Run with Blender --background --python repack_uv.py. Output is staged here;
project assets are only replaced after visual and numeric verification.
"""
import bpy, re, math, json
from pathlib import Path
from mathutils import Vector
import numpy as np

work = Path(__file__).resolve().parent
original = work / 'original'
source = (original / 'Water pump.fbx').read_text()
def array(key, cast=float):
    return [cast(x.strip()) for x in re.search(r'\b'+key+r': \*\d+\s*\{\s*a:\s*([^}]+)', source).group(1).split(',') if x.strip()]

# Read the original Unity ASCII FBX, preserving vertex, loop and normal ordering.
bpy.ops.wm.read_factory_settings(use_empty=True)
xyz = array('Vertices')
indices = array('PolygonVertexIndex', int)
faces, face = [], []
for index in indices:
    face.append(index if index >= 0 else -index-1)
    if index < 0:
        faces.append(face); face = []
mesh = bpy.data.meshes.new('Scene')
mesh.from_pydata([(xyz[i]/100, -xyz[i+2]/100, xyz[i+1]/100) for i in range(0,len(xyz),3)], [], faces)
mesh.update()
obj = bpy.data.objects.new('Water_pump', mesh)
bpy.context.collection.objects.link(obj)
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
uvs, uvi = array('UV'), array('UVIndex', int)
source_uv = mesh.uv_layers.new(name='OriginalUV')
for loop, index in zip(source_uv.data, uvi):
    loop.uv = (uvs[index*2], uvs[index*2+1])
normals = array('Normals')
mesh.normals_split_custom_set([(normals[i], -normals[i+2], normals[i+1]) for i in range(0,len(normals),3)])
for poly in mesh.polygons: poly.use_smooth = True
old_uv = np.array([u.uv[:] for u in mesh.uv_layers['OriginalUV'].data])

# One existing image, no color adjustments, masks, generated detail, AO or lighting bake.
original_image = bpy.data.images.load(str(original / 'Water pump_TB.png'))
original_image.colorspace_settings.name = 'sRGB'
original_image.pack()
mat = bpy.data.materials.new('WaterPump_OriginalSurface')
mat.use_nodes = True
mesh.materials.append(mat)
nodes, links = mat.node_tree.nodes, mat.node_tree.links
bsdf = nodes.get('Principled BSDF')
bsdf.inputs['Roughness'].default_value = .65
tex = nodes.new('ShaderNodeTexImage')
tex.image = original_image
tex.interpolation = 'Linear'
uv_node = nodes.new('ShaderNodeUVMap'); uv_node.uv_map = 'OriginalUV'
links.new(uv_node.outputs[0], tex.inputs['Vector'])
links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
output = nodes.get('Material Output')

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.cycles.seed = 0
scene.cycles.use_denoising = True
scene.render.resolution_x = 1000
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.world = bpy.data.worlds.new('ReviewWorld')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.16,.19,.24,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value = .6
scene.view_settings.view_transform = 'AgX'
target = Vector((0,0,.36))
camdata = bpy.data.cameras.new('ReviewCamera')
cam = bpy.data.objects.new('ReviewCamera',camdata)
scene.collection.objects.link(cam); scene.camera = cam
camdata.type = 'ORTHO'; camdata.ortho_scale = 1.42
for name,position,power,size in [('Key',(1,-1,2),170,2),('Fill',(-1,-.4,1),100,2),('Rim',(0,1.5,1.6),140,1.5)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
    light=bpy.data.objects.new(name,data);scene.collection.objects.link(light)
    light.location=position;light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
def render(name,position):
    cam.location=position;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(work/(name+'.png'))
    bpy.ops.render.render(write_still=True)
views = [('front',(1.2,-1.8,1.1)),('back',(-1.2,1.8,1.05))]
for name,position in views:render('before_'+name,position)

# Copy and arrange EXISTING islands. No unwrap, smart projection, seam changes,
# average-island scaling or per-face material classification.
packed = mesh.uv_layers.new(name='UVSet0',do_init=True)
mesh.uv_layers.active = packed
packed.active_render = True
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.select_all(action='SELECT')
bpy.ops.uv.pack_islands(rotate=True,rotate_method='AXIS_ALIGNED',shape_method='CONVEX',margin_method='FRACTION',margin=.006)
bpy.ops.object.mode_set(mode='OBJECT')
packed = mesh.uv_layers['UVSet0']
packed_uv = np.array([u.uv[:] for u in packed.data])

# Verify the mapping is an island-wise similarity transform, never a new unwrap.
parent = list(range(len(mesh.polygons)))
def find(x):
    while parent[x]!=x:
        parent[x]=parent[parent[x]];x=parent[x]
    return x
edge_owner = {}
for poly in mesh.polygons:
    loops = list(poly.loop_indices)
    for j,li in enumerate(loops):
        lj = loops[(j+1)%len(loops)]
        endpoints = sorted([(mesh.loops[li].vertex_index,tuple(old_uv[li])),(mesh.loops[lj].vertex_index,tuple(old_uv[lj]))])
        key = tuple(endpoints)
        if key in edge_owner:parent[find(poly.index)]=find(edge_owner[key])
        else:edge_owner[key]=poly.index
islands = {}
for poly in mesh.polygons:islands.setdefault(find(poly.index),[]).extend(poly.loop_indices)
max_error = 0.
scales = []
for ids in islands.values():
    a,b=old_uv[ids],packed_uv[ids]
    ac,bc=a-a.mean(0),b-b.mean(0)
    u,s,vt=np.linalg.svd(ac.T@bc)
    rotation=u@vt
    scale=s.sum()/max(float((ac*ac).sum()),1e-25)
    error=float(np.abs(ac@rotation*scale-bc).max())
    max_error=max(max_error,error);scales.append(float(scale))
assert max_error<2e-5, max_error

atlas = bpy.data.images.new('WaterPump_OriginalSurface_Repacked',width=2048,height=2048,alpha=False)
atlas.colorspace_settings.name='sRGB'
bake_node=nodes.new('ShaderNodeTexImage');bake_node.image=atlas
nodes.active=bake_node
emit=nodes.new('ShaderNodeEmission')
links.new(tex.outputs['Color'],emit.inputs['Color'])
links.new(emit.outputs[0],output.inputs['Surface'])
scene.cycles.samples=1
scene.render.bake.margin=8
scene.render.bake.margin_type='EXTEND'
scene.render.bake.use_clear=True
bpy.ops.object.bake(type='EMIT')
atlas.filepath_raw=str(work/'Water pump_TB.png');atlas.file_format='PNG';atlas.save();atlas.pack()

# Restore the same physical material; the only changed input is the rearranged atlas.
links.new(bsdf.outputs[0],output.inputs['Surface'])
links.new(bake_node.outputs['Color'],bsdf.inputs['Base Color'])
nodes.remove(emit);nodes.remove(tex);nodes.remove(uv_node)
mesh.uv_layers.remove(mesh.uv_layers['OriginalUV'])
scene.cycles.samples=48
for name,position in views:render('after_'+name,position)
comparison = {}
for name,_ in views:
    a_image=bpy.data.images.load(str(work/('before_'+name+'.png')))
    b_image=bpy.data.images.load(str(work/('after_'+name+'.png')))
    a=np.array(a_image.pixels[:]).reshape(1000,1000,4)[:,:,:3]
    b=np.array(b_image.pixels[:]).reshape(1000,1000,4)[:,:,:3]
    foreground=np.max(np.abs(a-a[0,0]),axis=2)>.015
    difference=np.abs(a-b)[foreground]
    comparison[name]={'mean_abs_rgb':float(difference.mean()),'p95_abs_rgb':float(np.percentile(difference,95))}
    bpy.data.images.remove(a_image);bpy.data.images.remove(b_image)

def replace_array(text,key,values):
    values=list(values)
    # Keep readable line lengths in the ASCII FBX.
    rows=[','.join(format(float(v),'.12g') for v in values[i:i+48]) for i in range(0,len(values),48)]
    replacement=key+': *'+str(len(values))+' {\n\t\t\t\ta: '+',\n'.join(rows)+'\n\t\t\t}'
    result,count=re.subn(r'\b'+key+r': \*\d+\s*\{\s*a:\s*[^}]+}',lambda m:replacement,text,count=1)
    assert count==1,key
    return result
candidate=replace_array(source,'UV',(v for loop in mesh.uv_layers.active.data for v in loop.uv))
candidate=replace_array(candidate,'UVIndex',range(len(mesh.loops)))
mesh.calc_tangents(uvmap='UVSet0')
candidate=replace_array(candidate,'Tangents',(v for loop in mesh.loops for v in (loop.tangent.x,loop.tangent.z,-loop.tangent.y)))
candidate=replace_array(candidate,'Binormals',(v for loop in mesh.loops for v in (loop.bitangent.x,loop.bitangent.z,-loop.bitangent.y)))
(work/'Water pump.fbx').write_text(candidate)

for other in scene.objects:
    other.select_set(other==obj)
    if other.type in {'LIGHT','CAMERA'}:other.hide_set(True)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active;space.shading.type='MATERIAL';space.overlay.show_overlays=False
            space.region_3d.view_location=target;space.region_3d.view_distance=1.55
            space.region_3d.view_rotation=(Vector((1.2,-1.8,1.1))-target).to_track_quat('Z','Y')
            space.region_3d.view_perspective='ORTHO'
        elif area.type=='IMAGE_EDITOR':area.spaces.active.image=atlas
if bpy.context.window:bpy.context.window.workspace=bpy.data.workspaces['Layout']
bpy.ops.wm.save_as_mainfile(filepath=str(work/'WaterPump_clean.blend'))
(work/'uv_repack.json').write_text(json.dumps({'source_texture_unchanged':True,'islands':len(islands),'max_similarity_error':max_error,'island_scale_range':[min(scales),max(scales)],'texture_transfer':'Original image through OriginalUV to packed UV, emission only; no repainting or lighting bake','render_comparison':comparison},indent=2))
