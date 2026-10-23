"""Conservative hard-surface retopology, UV repacking and albedo reprojection.

Rebuilds coplanar/near-coplanar triangle patches as quads while retaining open
mechanical boundaries. This is not an all-quad sculpt remesh. Writes staging
assets, preserving the original FBX model IDs, hierarchy and pivot transforms.
"""
import sys
import importlib.util
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
import bmesh
import math
import json
import shutil
import numpy as np
from mathutils import Vector
from uv_atlas import overlapping_faces

spec = importlib.util.spec_from_file_location('robot_uv', Path(__file__).with_name('Process-RobotArmUV.py'))
base = importlib.util.module_from_spec(spec); spec.loader.exec_module(base)
OUT = base.OUT / 'Retopology'
BACKUP = OUT / 'Backups'
BACKUP.mkdir(parents=True, exist_ok=True)
FIELDS = ('Vertices','PolygonVertexIndex','Normals','UV','UVIndex','Tangents','TangentsW','Binormals','BinormalsW')

def backup(name):
    destination = BACKUP / name
    if not destination.exists(): shutil.copy2(base.ASSET / name, destination)
    return destination

def encode_mesh(mesh, block):
    inv=base.rotation.inverted()
    mesh.calc_tangents(uvmap='UV_Unwrapped')
    values={'Vertices':[float(v) for vertex in mesh.vertices for v in inv@vertex.co*100], 'PolygonVertexIndex':[], 'UV':[float(v) for loop in mesh.uv_layers[0].data for v in loop.uv], 'UVIndex':list(range(len(mesh.loops))), 'Normals':[], 'Tangents':[], 'Binormals':[], 'TangentsW':[0]*len(mesh.loops), 'BinormalsW':[0]*len(mesh.loops)}
    for polygon in mesh.polygons:
        ids=list(polygon.vertices);ids[-1]=-ids[-1]-1;values['PolygonVertexIndex'].extend(ids)
    for i,loop in enumerate(mesh.loops):
        normal=mesh.corner_normals[i].vector;tangent=loop.tangent;bitangent=normal.cross(tangent)*loop.bitangent_sign
        for field,vec in [('Normals',normal),('Tangents',tangent),('Binormals',bitangent)]:values[field].extend(float(v) for v in inv@vec)
    changed=block
    for field in FIELDS: changed=base.replace_array(changed,field,values[field])
    return changed

def run(label, model, texture, block):
    bpy.ops.wm.open_mainfile(filepath=str(base.OUT / (label+'.blend')))
    if bpy.context.object.mode != 'OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    source = bpy.context.object
    source.name = model + '_BeforeRetopology'
    original_faces = len(source.data.polygons)
    extent = max(source.dimensions)
    target = source.copy(); target.data = source.data.copy(); target.name = model
    bpy.context.collection.objects.link(target)
    bpy.ops.object.select_all(action='DESELECT'); target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bm = bmesh.new(); bm.from_mesh(target.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=extent*1e-7)
    branching = [e for e in bm.edges if len(e.link_faces)>2]
    if branching: bmesh.ops.split_edges(bm, edges=branching)
    planar = [e for e in bm.edges if e.is_manifold and e.calc_face_angle(0)<math.radians(.15)]
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(.15), use_dissolve_boundaries=False, verts=[], edges=planar)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts)>4])
    bmesh.ops.join_triangles(bm, faces=[f for f in bm.faces if len(f.verts)==3], angle_face_threshold=math.radians(12), angle_shape_threshold=math.radians(70), cmp_uvs=False, cmp_vcols=False, cmp_seam=False, cmp_sharp=False, cmp_materials=True)
    branching = [e for e in bm.edges if len(e.link_faces)>2]
    if branching: bmesh.ops.split_edges(bm, edges=branching)
    assert not any(len(e.link_faces)>2 for e in bm.edges)
    bm.to_mesh(target.data); bm.free(); target.data.update()
    mesh = target.data
    assert sum(len(p.vertices)==4 for p in mesh.polygons)>0
    while mesh.uv_layers: mesh.uv_layers.remove(mesh.uv_layers[0])
    mesh.uv_layers.new(name='UV_Unwrapped')
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.012)
    bpy.ops.uv.average_islands_scale(); bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(udim_source='ACTIVE_UDIM', margin_method='FRACTION', margin=.008)
    bpy.ops.object.mode_set(mode='OBJECT')
    uv = mesh.uv_layers[0]
    bad = overlapping_faces(mesh, uv)
    for offset, face_id in enumerate(sorted(bad)):
        for i in mesh.polygons[face_id].loop_indices: uv.data[i].uv.x += 2*(offset+1)
    if bad:
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.select_all(action='SELECT')
        bpy.ops.uv.pack_islands(udim_source='ACTIVE_UDIM', margin_method='FRACTION', margin=.008)
        bpy.ops.object.mode_set(mode='OBJECT'); uv = mesh.uv_layers[0]
    assert not overlapping_faces(mesh, uv)
    assert all(math.isfinite(v) and -.00001<=v<=1.00001 for loop in uv.data for v in loop.uv)
    # Preserve source shading across rebuilt corners.
    normal_transfer = target.modifiers.new('Preserve source normals','DATA_TRANSFER')
    normal_transfer.object = source
    normal_transfer.use_loop_data = True
    normal_transfer.data_types_loops = {'CUSTOM_NORMAL'}
    normal_transfer.loop_mapping = 'POLYINTERP_NEAREST'
    bpy.ops.object.modifier_apply(modifier=normal_transfer.name)
    mesh = target.data
    for polygon in mesh.polygons: polygon.use_smooth = True

    source_mat = source.data.materials[0].copy(); source.data.materials.clear(); source.data.materials.append(source_mat)
    source_nodes = source_mat.node_tree.nodes; source_links = source_mat.node_tree.links
    old_image = bpy.data.images.load(str(backup(texture)), check_existing=False); old_image.pack()
    old_tex = source_nodes.new('ShaderNodeTexImage'); old_tex.image = old_image
    old_uv = source_nodes.new('ShaderNodeUVMap'); old_uv.uv_map = 'UV_Unwrapped'
    source_links.new(old_uv.outputs['UV'], old_tex.inputs['Vector'])
    emit = source_nodes.new('ShaderNodeEmission'); source_links.new(old_tex.outputs['Color'], emit.inputs['Color'])
    source_links.new(emit.outputs[0], next(n for n in source_nodes if n.type=='OUTPUT_MATERIAL').inputs['Surface'])
    image = bpy.data.images.new(texture,width=1024,height=1024,alpha=True); image.colorspace_settings.name='sRGB'
    material = bpy.data.materials.new(model+'_Retopology'); material.use_nodes=True
    mesh.materials.clear(); mesh.materials.append(material)
    nodes,links=material.node_tree.nodes,material.node_tree.links
    tex=nodes.new('ShaderNodeTexImage');tex.image=image; nodes.active=tex
    uvnode=nodes.new('ShaderNodeUVMap');uvnode.uv_map='UV_Unwrapped';links.new(uvnode.outputs['UV'],tex.inputs['Vector'])
    emission=nodes.new('ShaderNodeEmission');links.new(emission.outputs[0],nodes.get('Material Output').inputs['Surface'])
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=1
    scene.render.bake.use_selected_to_active=True;scene.render.bake.cage_extrusion=extent*.006;scene.render.bake.max_ray_distance=extent*.02
    scene.render.bake.margin=4;scene.render.bake.margin_type='EXTEND'
    source.select_set(True);target.select_set(True);bpy.context.view_layer.objects.active=target
    bpy.ops.object.bake(type='EMIT')
    links.new(tex.outputs['Color'],emission.inputs['Color'])
    image.filepath_raw=str(OUT/texture);image.file_format='PNG';image.save();image.pack()

    # Identical camera compares silhouette and transferred appearance.
    camera_data=bpy.data.cameras.new('Validation');camera=bpy.data.objects.new('Validation',camera_data);scene.collection.objects.link(camera)
    center=sum((Vector(c) for c in source.bound_box),Vector())/8
    camera.location=center+Vector((3,-4,3)).normalized()*extent*3
    camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.type='ORTHO';camera_data.ortho_scale=extent*1.65;camera_data.clip_start=.0001
    scene.camera=camera;scene.render.resolution_x=512;scene.render.resolution_y=512;scene.render.resolution_percentage=100;scene.render.film_transparent=True;scene.view_settings.view_transform='Standard'
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
    previews=OUT/'Previews';previews.mkdir(exist_ok=True)
    arrays=[]
    for before,name in [(True,'Before'),(False,'After')]:
        source.hide_render=not before;target.hide_render=before
        path=previews/(label+'-'+name+'.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
        rendered=bpy.data.images.load(str(path));arrays.append(np.array(rendered.pixels[:],dtype=np.float32).reshape(-1,4))
    masks=[a[:,3]>.5 for a in arrays];iou=float((masks[0]&masks[1]).sum()/float((masks[0]|masks[1]).sum()))
    common=(arrays[0][:,3]>.99)&(arrays[1][:,3]>.99)
    error=float(np.abs(arrays[0][common,:3]-arrays[1][common,:3]).mean())
    assert iou>.97 and error<.04,(label,iou,error)
    source.hide_render=True;source.hide_set(True);source.select_set(False);target.hide_render=False
    links.new(tex.outputs['Color'],nodes.get('Principled BSDF').inputs['Base Color']);links.new(nodes.get('Principled BSDF').outputs[0],nodes.get('Material Output').inputs['Surface']);nodes.remove(emission)
    bpy.data.objects.remove(camera,do_unlink=True)
    bpy.context.view_layer.objects.active=target
    # Write only mesh arrays into the original FBX geometry block.
    changed = encode_mesh(mesh, block)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='IMAGE_EDITOR':area.spaces.active.image=image
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/(label+'.blend')))
    result={'mesh':model,'texture':texture,'before_triangles':original_faces,'after_faces':len(mesh.polygons),'quads':sum(len(p.vertices)==4 for p in mesh.polygons),'after_triangles':sum(len(p.vertices)-2 for p in mesh.polygons),'silhouette_iou':iou,'render_mean_error':error,'uv_overlaps':0,'branching_edges':0}
    print('RETOPO_RESULT '+json.dumps(result),flush=True)
    return changed,result

reports=[]
for filename,mapping in base.SPECS.items():
    original=backup(filename).read_text(encoding='utf-8-sig');backup(filename+'.meta')
    models={i:base.re.search(r'"Model::([^"]+)"',b)[1] for i,_,_,b in base.sections(original,'Model')}
    connections={int(a):int(b) for a,b in base.re.findall(r'C: "OO",(\d+),(\d+)',original)}
    replacements=[]
    for geometry_id,start,end,block in base.sections(original,'Geometry'):
        model=models[connections[geometry_id]]
        if model not in mapping:continue
        changed,report=run(Path(filename).stem+'-'+model,model,mapping[model],block)
        replacements.append((start,end,changed));reports.append(report)
    changed=original
    for start,end,value in reversed(replacements):changed=changed[:start]+value+changed[end:]
    # Everything outside geometry arrays, including pivot transforms and IDs,
    # must remain byte-for-byte equivalent after newline normalization.
    a,b=original,changed
    for field in FIELDS:
        a=base.re.sub(base.array_pattern(field),field,a);b=base.re.sub(base.array_pattern(field),field,b)
    assert a==b,'FBX structure changed outside mesh arrays'
    changed='\n'.join(line.rstrip() for line in changed.split('\n'))
    (OUT/filename).write_text(changed,encoding='utf-8')
(OUT/'retopology-report.json').write_text(json.dumps(reports,indent=2),encoding='utf-8')
print('PASS six-part retopology, UV, texture and silhouette validation',flush=True)
