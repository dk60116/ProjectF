"""Build, clean, unwrap and bake the Corner/T/Cross Pipe variants in Blender.

The actual Unity serialized meshes are imported. Hidden surfaces are removed only
when dense sampling proves a triangle lies strictly inside another closed component.
Exterior first-hit ray tests guard the result before authoring or Unity data changes.
"""
import bpy
import bmesh
import hashlib
import json
import math
import runpy
import shutil
import struct
from pathlib import Path

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

WORK = Path(__file__).resolve().parent
PIPE_DIR = WORK.parents[1] / 'FactorioProject/Assets/MapObject/Fluid/Pipe'
COMMON = runpy.run_path(str(WORK.parent/'Pipe/build_pipe_material.py'))
VARIANTS = {
    'Corner': ('Pipe_Corner.asset',),
    'T': ('Pipe_T.asset', 'Cylinder.asset'),
    'Cross': ('Pipe_Cross.asset',),
}
EPS = 2e-6


def face_key(points):
    return tuple(sorted(tuple(round(x, 6) for x in point) for point in points))


def import_asset(asset_name, collection):
    source = COMMON['read_mesh'](PIPE_DIR/asset_name)
    xyz = [(x,z,y) for x,y,z in source['xyz']]
    triangles = [tuple(reversed(source['indices'][i:i+3]))
                 for i in range(0,len(source['indices']),3)]
    mesh = bpy.data.meshes.new(Path(asset_name).stem+'_AuthoringMesh')
    mesh.from_pydata(xyz,[],triangles)
    mesh.update()
    obj = bpy.data.objects.new(Path(asset_name).stem,mesh)
    collection.objects.link(obj)
    bm=bmesh.new(); bm.from_mesh(mesh)
    source_layer=bm.loops.layers.int.new('UnitySourceVertex')
    for face in bm.faces:
        for loop in face.loops:
            loop[source_layer]=loop.vert.index
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-7)
    bm.to_mesh(mesh); bm.free(); mesh.update()
    source_ids=[value.value for value in mesh.attributes['UnitySourceVertex'].data]
    mesh.normals_split_custom_set([(source['normals'][i][0],source['normals'][i][2],source['normals'][i][1])
                                   for i in source_ids])
    for polygon in mesh.polygons:
        polygon.use_smooth=True
    uv=mesh.uv_layers.new(name='Pipe_BakeUV')
    for loop in mesh.loops:
        uv.data[loop.index].uv=source['uv'][source_ids[loop.index]]
    obj['unity_asset']=asset_name
    return {'object':obj,'source':source,'source_hash':hashlib.sha256((PIPE_DIR/asset_name).read_bytes()).hexdigest(),
            'vertices_before':len(source['xyz']),'triangles_before':len(source['indices'])//3}


def components(mesh):
    adjacent={vertex.index:set() for vertex in mesh.vertices}
    for edge in mesh.edges:
        a,b=edge.vertices; adjacent[a].add(b); adjacent[b].add(a)
    remain=set(adjacent); groups=[]
    while remain:
        todo=[next(iter(remain))]; group=set()
        while todo:
            index=todo.pop()
            if index in group: continue
            group.add(index); todo.extend(adjacent[index]-group)
        remain-=group; groups.append(group)
    result=[]
    for group in groups:
        polygons=[p for p in mesh.polygons if p.vertices[0] in group]
        edge_use={}
        for polygon in polygons:
            ids=polygon.vertices
            for i in range(len(ids)):
                edge=tuple(sorted((ids[i],ids[(i+1)%len(ids)])))
                edge_use[edge]=edge_use.get(edge,0)+1
        points=[mesh.vertices[i].co.copy() for i in group]
        volume=0.0
        for polygon in polygons:
            a,b,c=(mesh.vertices[i].co for i in polygon.vertices)
            volume+=a.dot(b.cross(c))/6
        result.append({'vertices':group,'polygons':polygons,'closed':all(v==2 for v in edge_use.values()),
                       'orientation':1 if volume>=0 else -1,'volume':abs(volume),
                       'min':Vector([min(p[i] for p in points) for i in range(3)]),
                       'max':Vector([max(p[i] for p in points) for i in range(3)])})
    return result


def make_bvh(mesh, component):
    ids=sorted(component['vertices']); remap={old:new for new,old in enumerate(ids)}
    verts=[mesh.vertices[i].co.copy() for i in ids]
    faces=[tuple(remap[i] for i in polygon.vertices) for polygon in component['polygons']]
    return BVHTree.FromPolygons(verts,faces,all_triangles=True)


def hidden_faces(obj):
    mesh=obj.data; groups=components(mesh)
    closed=[]
    for group in groups:
        if group['closed'] and group['volume']>1e-10:
            group['bvh']=make_bvh(mesh,group); closed.append(group)
    removed=set()
    for component in groups:
        for polygon in component['polygons']:
            points=[mesh.vertices[i].co.copy() for i in polygon.vertices]
            tri_min=Vector([min(p[i] for p in points) for i in range(3)])
            tri_max=Vector([max(p[i] for p in points) for i in range(3)])
            targets=[target for target in closed if target is not component
                     and all(tri_min[i]>target['min'][i]+EPS and tri_max[i]<target['max'][i]-EPS for i in range(3))]
            if not targets: continue
            samples=[]
            steps=6
            for i in range(steps+1):
                for j in range(steps+1-i):
                    a=i/steps; b=j/steps; c=1-a-b
                    samples.append(points[0]*a+points[1]*b+points[2]*c)
            def inside_union(point):
                for target in targets:
                    nearest=target['bvh'].find_nearest(point)
                    if nearest[0] is None or nearest[3]<=EPS: continue
                    if (point-nearest[0]).dot(nearest[1])*target['orientation'] < -EPS:
                        return True
                return False
            if all(inside_union(point) for point in samples):
                removed.add(face_key(points))
    return removed,groups


def visibility_check(obj, removed):
    mesh=obj.data; verts=[v.co.copy() for v in mesh.vertices]
    old_faces=[tuple(p.vertices) for p in mesh.polygons]
    new_faces=[face for face in old_faces if face_key([verts[i] for i in face]) not in removed]
    old=BVHTree.FromPolygons(verts,old_faces,all_triangles=True)
    new=BVHTree.FromPolygons(verts,new_faces,all_triangles=True)
    lo=Vector([min(p[i] for p in verts) for i in range(3)])
    hi=Vector([max(p[i] for p in verts) for i in range(3)])
    extent=max(hi-lo); center=(hi+lo)/2; rays=0
    for k in range(48):
        z=1-2*(k+.5)/48; theta=k*math.pi*(3-math.sqrt(5))
        direction=Vector((math.sqrt(1-z*z)*math.cos(theta),math.sqrt(1-z*z)*math.sin(theta),z))
        u=direction.orthogonal().normalized(); v=direction.cross(u).normalized()
        for ix in range(41):
            for iy in range(41):
                origin=center+direction*(extent*2)+u*((ix-20)*extent/34)+v*((iy-20)*extent/34)
                before=old.ray_cast(origin,-direction,extent*4)[0]
                after=new.ray_cast(origin,-direction,extent*4)[0]
                assert (before is None)==(after is None), 'Silhouette changed'
                if before is not None:
                    assert (before-after).length<1e-4, 'Visible first surface changed'
                rays+=1
    return rays


def remove_faces(obj, removed):
    mesh=obj.data
    normals={}
    for polygon in mesh.polygons:
        key=face_key([mesh.vertices[i].co for i in polygon.vertices])
        for li in polygon.loop_indices:
            co=tuple(mesh.vertices[mesh.loops[li].vertex_index].co)
            normals[(key,co)]=mesh.corner_normals[li].vector.copy()
    bm=bmesh.new(); bm.from_mesh(mesh)
    cut=[face for face in bm.faces if face_key([vertex.co for vertex in face.verts]) in removed]
    for face in cut: bm.faces.remove(face)
    for edge in list(bm.edges):
        if not edge.link_faces: bm.edges.remove(edge)
    for vertex in list(bm.verts):
        if not vertex.link_edges: bm.verts.remove(vertex)
    bm.to_mesh(mesh); bm.free(); mesh.update()
    loop_normals=[]
    for polygon in mesh.polygons:
        key=face_key([mesh.vertices[i].co for i in polygon.vertices])
        for li in polygon.loop_indices:
            co=tuple(mesh.vertices[mesh.loops[li].vertex_index].co)
            loop_normals.append(normals[(key,co)])
    mesh.normals_split_custom_set(loop_normals)
    return len(cut)


def unwrap(objects):
    bpy.ops.object.mode_set(mode='OBJECT') if bpy.context.mode!='OBJECT' else None
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.012,
                             area_weight=0.0,correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')


def uv_overlap(objects,size=1024):
    owner=np.full((size,size),-1,dtype=np.int32); overlap=0; tri_id=0
    for obj in objects:
        mesh=obj.data; mesh.calc_loop_triangles(); uv=mesh.uv_layers.active.data
        for triangle in mesh.loop_triangles:
            if triangle.area<=1e-12: continue
            points=np.array([uv[i].uv for i in triangle.loops])*size
            low=np.maximum(np.floor(points.min(0)).astype(int),0)
            high=np.minimum(np.ceil(points.max(0)).astype(int),size)
            xx,yy=np.meshgrid(np.arange(low[0],high[0])+.5,np.arange(low[1],high[1])+.5)
            a,b,c=points; den=(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
            if abs(den)<1e-14: continue
            u=((xx-a[0])*(c[1]-a[1])-(yy-a[1])*(c[0]-a[0]))/den
            v=((b[0]-a[0])*(yy-a[1])-(b[1]-a[1])*(xx-a[0]))/den
            inside=(u>1e-6)&(v>1e-6)&(u+v<1-1e-6)
            region=owner[low[1]:high[1],low[0]:high[0]]
            overlap+=int(np.count_nonzero(inside&(region>=0)))
            region[inside]=tri_id; tri_id+=1
    return overlap


def export_asset(entry):
    obj,source=entry['object'],entry['source']; mesh=obj.data; uv=mesh.uv_layers.active
    faces={}
    for polygon in mesh.polygons:
        mapping={tuple(round(x,6) for x in source['xyz'][mesh.attributes['UnitySourceVertex'].data[li].value]):
                 tuple(uv.data[li].uv) for li in polygon.loop_indices}
        faces.setdefault(tuple(sorted(mapping)),mapping)
    vertices=[]; indices=[]; lookup={}; retained=[]; dropped=0
    tiny_source_triangles=0
    for fi in range(0,len(source['indices']),3):
        sources=source['indices'][fi:fi+3]
        a,b,c=(Vector(source['xyz'][index]) for index in sources)
        tiny_source_triangles+=((b-a).cross(c-a).length/2 < 1e-9)
        points=[tuple(round(x,6) for x in source['xyz'][index]) for index in sources]
        mapping=faces.get(tuple(sorted(points)))
        if mapping is None:
            dropped+=1; continue
        retained.extend(sources)
        for src,point in zip(sources,points):
            coord=mapping[point]; key=(src,coord)
            if key not in lookup:
                raw=bytearray(source['raw'][src*source['stride']:(src+1)*source['stride']])
                struct.pack_into('<2f',raw,source['channels'][4][1],*coord)
                lookup[key]=len(vertices); vertices.append(raw)
            indices.append(lookup[key])
    COMMON['recalculate_tangents'](vertices,indices)
    vertices,indices=COMMON['compact_vertices'](vertices,indices)
    stem='Pipe_T_Cylinder' if obj['unity_asset']=='Cylinder.asset' else Path(obj['unity_asset']).stem
    output=WORK/(stem+'_Baked.asset')
    COMMON['write_mesh'](source,vertices,indices,output,retained_indices=retained)
    entry.update(vertices_after=len(vertices),triangles_after=len(indices)//3,
                 removed_source_triangles=dropped,tiny_source_triangles=tiny_source_triangles,
                 output=str(output))


def make_source_material(name):
    material=COMMON['create_cast_iron'](); material.name=name+'_CastIron_Source'
    material.use_fake_user=True
    return material


def bake_variant(name,objects,material):
    for obj in objects:
        obj.data.materials.clear(); obj.data.materials.append(material)
    image=bpy.data.images.new('Pipe_'+name+'_CastIron_TB',width=2048,height=2048,alpha=False)
    nodes,links=material.node_tree.nodes,material.node_tree.links
    image_node=nodes.new('ShaderNodeTexImage'); image_node.image=image; nodes.active=image_node
    for node in nodes: node.select=node==image_node
    output=next(node for node in nodes if node.type=='OUTPUT_MATERIAL')
    emission=nodes.new('ShaderNodeEmission')
    links.new(nodes[material['bake_color_node']].outputs[0],emission.inputs['Color'])
    links.new(emission.outputs[0],output.inputs['Surface'])
    scene=bpy.context.scene
    try: scene.render.engine='CYCLES'
    except TypeError as exc: raise RuntimeError(str(exc))
    scene.cycles.samples=32; scene.render.bake.margin=16
    for index,obj in enumerate(objects):
        bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active=obj
        scene.render.bake.use_clear=index==0
        bpy.ops.object.bake(type='EMIT')
    image.filepath_raw=str(WORK/('Pipe_'+name+'_CastIron_TB.png')); image.file_format='PNG'; image.save(); image.pack()
    bsdf=next(node for node in nodes if node.type=='BSDF_PRINCIPLED')
    links.new(bsdf.outputs[0],output.inputs['Surface']); nodes.remove(emission)
    preview=bpy.data.materials.new('Pipe_'+name+'_CastIron_Baked'); preview.use_nodes=True
    pbsdf=next(node for node in preview.node_tree.nodes if node.type=='BSDF_PRINCIPLED')
    pbsdf.inputs['Metallic'].default_value=.15; pbsdf.inputs['Roughness'].default_value=.8
    texture=preview.node_tree.nodes.new('ShaderNodeTexImage'); texture.image=image
    preview.node_tree.links.new(texture.outputs['Color'],pbsdf.inputs['Base Color'])
    for obj in objects: obj.data.materials[0]=preview


def prepare():
    global state
    assert bpy.context.mode=='OBJECT'
    backup=WORK/'original'; backup.mkdir(parents=True,exist_ok=True)
    for variant,assets in VARIANTS.items():
        for asset in assets:
            target=backup/asset
            assert not target.exists(), 'Do not overwrite an earlier variant backup'
            shutil.copy2(PIPE_DIR/asset,target)
        shutil.copy2(PIPE_DIR/('Pipe_'+variant+'.prefab' if variant!='T' else 'Pipe_T.prefab'),
                     backup/('Pipe_'+variant+'.prefab' if variant!='T' else 'Pipe_T.prefab'))
    bpy.ops.wm.save_as_mainfile(filepath=str(backup/'session_before_variants.blend'),copy=True)
    for obj in list(bpy.context.scene.objects): bpy.data.objects.remove(obj,do_unlink=True)
    state={}
    for variant,assets in VARIANTS.items():
        collection=bpy.data.collections.new('Pipe_'+variant); bpy.context.scene.collection.children.link(collection)
        entries=[import_asset(asset,collection) for asset in assets]
        state[variant]={'entries':entries,'objects':[entry['object'] for entry in entries]}
    bpy.app.driver_namespace['pipe_variants']=state
    print(json.dumps({name:[{'asset':e['object']['unity_asset'],'blender_vertices':len(e['object'].data.vertices),
                              'blender_triangles':len(e['object'].data.polygons)} for e in data['entries']]
                      for name,data in state.items()}))


def clean_unwrap():
    for name,data in state.items():
        total_removed=total_rays=0; component_report=[]
        for entry in data['entries']:
            removed,groups=hidden_faces(entry['object'])
            rays=visibility_check(entry['object'],removed)
            cut=remove_faces(entry['object'],removed)
            assert cut==len(removed)
            entry['removed_authoring_faces']=cut; total_removed+=cut; total_rays+=rays
            component_report.append({'asset':entry['object']['unity_asset'],'components':len(groups),
                                     'closed_components':sum(g['closed'] for g in groups),'removed_faces':cut})
        unwrap(data['objects'])
        overlap=uv_overlap(data['objects'])
        assert overlap==0, name+' UV overlap'
        data['report']={'authoring_removed_faces':total_removed,'visibility_test_rays':total_rays,
                        'visibility_changes':0,'uv_overlap_pixels_1024':overlap,'components':component_report}
        print(name,json.dumps(data['report']))


def bake_export():
    for name,data in state.items():
        material=make_source_material('Pipe_'+name)
        bake_variant(name,data['objects'],material)
        for entry in data['entries']: export_asset(entry)
        data['report']['assets']=[{key:entry[key] for key in ('source_hash','vertices_before','vertices_after','triangles_before','triangles_after','removed_source_triangles','tiny_source_triangles','output')}
                                  for entry in data['entries']]
        (WORK/(name+'_validation.json')).write_text(json.dumps(data['report'],indent=2))
    # Separate the variants only after baking (object-space procedural scale stays stable).
    for name,offset in (('Corner',-1.5),('T',0),('Cross',1.5)):
        for obj in state[name]['objects']: obj.location.x=offset
    bpy.ops.wm.save_as_mainfile(filepath=str(WORK/'Pipe_Variants_CastIron.blend'))
    print('Baked and exported Corner, T and Cross variants')
