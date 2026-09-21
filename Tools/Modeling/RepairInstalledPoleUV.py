"""Live Blender: preserve original painted albedo while repairing atlas layout/padding."""
from pathlib import Path
import math
import re
import bpy

ROOT=Path('C:/Git/ProjectF')
BASE=ROOT/'FactorioProject/Assets/MapObject/Electro'
OUT=ROOT/'Tools/Modeling/UVRepair'
NAMES=('Utility pole','Concrete Utility pole')

def bake(name):
    source=bpy.data.objects[name+'_Installed']
    assert bpy.data.objects.get(name+'_UVRepair') is None
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    obj=source.copy(); obj.data=source.data.copy(); obj.name=name+'_UVRepair'
    bpy.context.scene.collection.objects.link(obj)
    obj.location.y+=2.4
    obj.data.materials.clear()
    mat=source.data.materials[0].copy(); mat.name=name+'_UVRepairPreview'
    obj.data.materials.append(mat)
    original=obj.data.uv_layers.active
    original.name='SourceUV'
    destination=obj.data.uv_layers.new(name='RepairedUV')
    obj.data.uv_layers.active=destination
    destination.active_render=True
    for o in bpy.context.selected_objects: o.select_set(False)
    obj.select_set(True); bpy.context.view_layer.objects.active=obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    # Keep the source islands' internal mapping and area ratios; repack with a safe gutter.
    bpy.ops.uv.pack_islands(rotate=True,rotate_method='CARDINAL',scale=True,
                           margin_method='FRACTION',margin=0.025,merge_overlap=False,
                           shape_method='CONCAVE')
    bpy.ops.object.mode_set(mode='OBJECT')
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    tex=next(n for n in nodes if n.type=='TEX_IMAGE')
    assert tex.image and not tex.image.is_dirty
    source_uv=nodes.new('ShaderNodeUVMap'); source_uv.uv_map='SourceUV'
    links.new(source_uv.outputs['UV'],tex.inputs['Vector'])
    output=next(n for n in nodes if n.type=='OUTPUT_MATERIAL')
    shader=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
    emission=nodes.new('ShaderNodeEmission')
    links.new(tex.outputs['Color'],emission.inputs['Color'])
    links.new(emission.outputs[0],output.inputs['Surface'])
    target=bpy.data.images.new(name+'_RepairedAlbedo',width=2048,height=2048,alpha=False)
    target.colorspace_settings.name=tex.image.colorspace_settings.name
    target_node=nodes.new('ShaderNodeTexImage'); target_node.image=target
    for n in nodes: n.select=False
    target_node.select=True; nodes.active=target_node
    scene=bpy.context.scene
    engine=scene.render.engine
    margin=scene.render.bake.margin; margin_type=scene.render.bake.margin_type
    selected=scene.render.bake.use_selected_to_active
    try:
        scene.render.engine='CYCLES'
        scene.render.bake.margin=24
        scene.render.bake.margin_type='EXTEND'
        scene.render.bake.use_selected_to_active=False
        bpy.ops.object.bake(type='EMIT',use_clear=True)
    finally:
        scene.render.engine=engine
        scene.render.bake.margin=margin; scene.render.bake.margin_type=margin_type
        scene.render.bake.use_selected_to_active=selected
        links.new(shader.outputs[0],output.inputs['Surface'])
    links.new(target_node.outputs['Color'],shader.inputs['Base Color'])
    nodes.remove(emission)
    OUT.mkdir(parents=True,exist_ok=True)
    target.file_format='PNG'; target.filepath_raw=str(OUT/(name+'_TB.png'))
    target.save(); target.pack()
    print('BAKED',name,str(target.filepath_raw))
    return obj

def sample_colors(name):
    """Compare original and rebaked appearance at the same surface points (no render lighting)."""
    import numpy as np
    obj=bpy.data.objects[name+'_UVRepair']; mesh=obj.data
    mat=obj.data.materials[0]
    source=next(n.image for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image.name!=name+'_RepairedAlbedo')
    target=bpy.data.images[name+'_RepairedAlbedo']
    def pixels(image):
        data=np.empty(len(image.pixels),dtype=np.float32); image.pixels.foreach_get(data)
        return data.reshape(image.size[1],image.size[0],4)[:,:,:3]
    src,dst=pixels(source),pixels(target)
    def sample(data,u,v):
        h,w=data.shape[:2]; x=max(0,min(w-1,u*w-0.5)); y=max(0,min(h-1,v*h-0.5))
        ix,iy=int(x),int(y); jx,jy=min(ix+1,w-1),min(iy+1,h-1); tx,ty=x-ix,y-iy
        return (data[iy,ix]*(1-tx)+data[iy,jx]*tx)*(1-ty)+(data[jy,ix]*(1-tx)+data[jy,jx]*tx)*ty
    mesh.calc_loop_triangles(); errors=[]; weighted=[]; weights=[]
    for tri in mesh.loop_triangles:
        for bary in [(1/3,1/3,1/3),(0.6,0.2,0.2),(0.2,0.6,0.2),(0.2,0.2,0.6)]:
            colors=[]
            for layer,data in [('SourceUV',src),('RepairedUV',dst)]:
                uv=sum((mesh.uv_layers[layer].data[li].uv*b for li,b in zip(tri.loops,bary)),start=__import__('mathutils').Vector((0,0)))
                colors.append(sample(data,*uv))
            err=float(np.mean(np.abs(colors[0]-colors[1])))
            errors.append(err); weighted.append(err*tri.area); weights.append(tri.area)
    result={'surface_weighted_mean_error':sum(weighted)/sum(weights),'sample_mean_error':float(np.mean(errors)),
            'sample_p95_error':float(np.percentile(errors,95)),'max_error':max(errors),'samples':len(errors)}
    print(name,result)
    return result

def validate(name, object_name=None):
    import numpy as np
    source=bpy.data.objects[name+'_Installed']; obj=bpy.data.objects[object_name or name+'_UVRepair']
    mesh=obj.data; mesh.calc_loop_triangles()
    assert len(mesh.vertices)==len(source.data.vertices)
    assert all((a.co-b.co).length<1e-9 for a,b in zip(mesh.vertices,source.data.vertices))
    assert [tuple(p.vertices) for p in mesh.polygons]==[tuple(p.vertices) for p in source.data.polygons]
    uv=mesh.uv_layers['RepairedUV'].data
    assert all(math.isfinite(c) and 0<=c<=1 for p in uv for c in p.uv)
    areas=[]; coverage=np.zeros((512,512),dtype=np.uint16)
    for tri in mesh.loop_triangles:
        a,b,c=[np.array(uv[i].uv,dtype=float) for i in tri.loops]
        det=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        assert abs(det)>1e-12
        areas.append(abs(det)/2)
        low=np.maximum(0,np.floor(np.minimum(np.minimum(a,b),c)*512).astype(int))
        high=np.minimum(511,np.ceil(np.maximum(np.maximum(a,b),c)*512).astype(int))
        yy,xx=np.mgrid[low[1]:high[1]+1,low[0]:high[0]+1]
        x,y=(xx+0.5)/512,(yy+0.5)/512
        u=((b[1]-c[1])*(x-c[0])+(c[0]-b[0])*(y-c[1]))/det
        v=((c[1]-a[1])*(x-c[0])+(a[0]-c[0])*(y-c[1]))/det
        coverage[low[1]:high[1]+1,low[0]:high[0]+1]+=((u>1e-5)&(v>1e-5)&(u+v<1-1e-5))
    overlap=int(np.count_nonzero(coverage>1))
    assert overlap==0, (name,overlap)
    result={'vertices':len(mesh.vertices),'triangles':len(mesh.loop_triangles),'overlap_pixels_at_512':overlap,
            'small_triangles_at_2048':int(sum(a*2048*2048<2 for a in areas)),'min_uv_area':float(min(areas)),
            'min_border_pixels_at_2048':min(min(min(p.uv),1-max(p.uv)) for p in uv)*2048}
    print(name,result)
    return result

def export_patch(objects=None):
    """Preserve ASCII FBX IDs/topology/transforms; emit only UV/tangent attribute patches."""
    patch='*** Begin Patch\n'
    objects=objects or {name:name+'_UVRepair' for name in NAMES}
    for name,object_name in objects.items():
        validate(name,object_name)
        mesh=bpy.data.objects[object_name].data
        mesh.calc_tangents(uvmap='RepairedUV')
        path=BASE/name/(name+'.fbx'); text=path.read_text(encoding='utf-8')
        data=[float(c) for p in mesh.uv_layers['RepairedUV'].data for c in p.uv]
        tangents=[]; binormals=[]
        for loop in mesh.loops:
            t,b=loop.tangent,loop.bitangent
            tangents.extend((t.x,t.z,-t.y)); binormals.extend((b.x,b.z,-b.y))
        patch+='*** Update File: '+path.as_posix()+'\n'
        for label,values in [('Binormals',binormals),('Tangents',tangents),('UV',data),('UVIndex',list(range(len(mesh.loops))))]:
            match=re.search(r'(?m)^([\t ]*)'+label+r': \*\d+\s*\{\s*a:\s*[^}]+\}',text)
            assert match
            old=match.group(0); indent=match.group(1)
            rows=[','.join(format(v,'.9g') for v in values[i:i+48]) for i in range(0,len(values),48)]
            new=indent+label+': *'+str(len(values))+' {\n'+indent+'\ta: '+(',\n'+indent+'\t').join(rows)+'\n'+indent+'}'
            patch+='@@\n'+''.join('-'+line+'\n' for line in old.splitlines())+''.join('+'+line+'\n' for line in new.splitlines())
    patch+='*** End Patch\n'
    (OUT/'ApplyUV.patch').write_text(patch,encoding='utf-8')
    print('PATCH READY')
