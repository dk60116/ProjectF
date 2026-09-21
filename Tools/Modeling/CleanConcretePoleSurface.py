"""Live Blender surface repaint/bake using measured original material colors.

Removes baked vertical streaks and broken markings. Does not touch wood/P assets or geometry.
"""
from pathlib import Path
import bpy

ROOT=Path('C:/Git/ProjectF')
OUT=ROOT/'Tools/Modeling/UVRepair'
NAME='Concrete Utility pole'

def linear(rgb):
    return tuple(c/12.92 if c<=0.04045 else ((c+0.055)/1.055)**2.4 for c in rgb)+(1,)

def components(mesh):
    adjacent=[set() for _ in mesh.vertices]
    for edge in mesh.edges:
        a,b=edge.vertices; adjacent[a].add(b); adjacent[b].add(a)
    unseen=set(range(len(adjacent))); groups=[]
    while unseen:
        seed=min(unseen); unseen.remove(seed); group={seed}; stack=[seed]
        while stack:
            for v in adjacent[stack.pop()] & unseen:
                unseen.remove(v); group.add(v); stack.append(v)
        groups.append(group)
    return groups

def material(label,rgb,variation=0.012,warning=False):
    mat=bpy.data.materials.new('ConcreteCleanup_'+label); mat.use_nodes=True
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    nodes.remove(next(n for n in nodes if n.type=='BSDF_PRINCIPLED'))
    output=next(n for n in nodes if n.type=='OUTPUT_MATERIAL')
    coord=nodes.new('ShaderNodeTexCoord')
    noise=nodes.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value=3.5; noise.inputs['Detail'].default_value=0
    links.new(coord.outputs['Object'],noise.inputs['Vector'])
    paint=nodes.new('ShaderNodeMixRGB')
    paint.inputs[1].default_value=linear(tuple(max(0,c-variation) for c in rgb))
    paint.inputs[2].default_value=linear(tuple(min(1,c+variation) for c in rgb))
    links.new(noise.outputs['Fac'],paint.inputs[0])
    color=paint.outputs[0]
    if warning:
        xyz=nodes.new('ShaderNodeSeparateXYZ'); links.new(coord.outputs['Object'],xyz.inputs[0])
        def math(op,a,b=None):
            node=nodes.new('ShaderNodeMath'); node.operation=op
            for i,value in enumerate((a,b)):
                if value is None: continue
                if isinstance(value,(int,float)): node.inputs[i].default_value=value
                else: links.new(value,node.inputs[i])
            return node.outputs[0]
        # Local cylinder center is offset in Y in the original mesh. Full revolutions
        # keep the marking continuous around the back seam, independent of UV islands.
        angle=math('ARCTAN2',math('ADD',xyz.outputs['Y'],0.05715),xyz.outputs['X'])
        stripe=math('GREATER_THAN',math('SINE',math('ADD',math('MULTIPLY',angle,3),math('MULTIPLY',xyz.outputs['Z'],34))),0)
        marking=nodes.new('ShaderNodeMixRGB')
        marking.inputs[1].default_value=linear((0.17,0.17,0.15))
        marking.inputs[2].default_value=linear((0.627,0.490,0.118))
        links.new(stripe,marking.inputs[0])
        mask=math('MULTIPLY',math('GREATER_THAN',xyz.outputs['Z'],0.18),math('LESS_THAN',xyz.outputs['Z'],0.63))
        combined=nodes.new('ShaderNodeMixRGB')
        links.new(mask,combined.inputs[0]); links.new(color,combined.inputs[1]); links.new(marking.outputs[0],combined.inputs[2])
        color=combined.outputs[0]
    emission=nodes.new('ShaderNodeEmission'); links.new(color,emission.inputs['Color'])
    links.new(emission.outputs[0],output.inputs['Surface'])
    return mat

def build_and_bake():
    source=bpy.data.objects[NAME+'_UVRepair']
    assert bpy.data.objects.get(NAME+'_SurfaceClean') is None
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    obj=source.copy(); obj.data=source.data.copy(); obj.name=NAME+'_SurfaceClean'
    bpy.context.scene.collection.objects.link(obj)
    obj.hide_set(False); obj.hide_render=False
    groups=components(obj.data)
    assert len(groups)==18 and len(obj.data.vertices)==377
    group_for={v:i for i,g in enumerate(groups) for v in g}
    mats=[material('Shaft',(0.575,0.575,0.568),warning=True),
          material('Concrete',(0.575,0.575,0.568)),
          material('Steel',(0.423,0.425,0.427),variation=0.008),
          material('Porcelain',(0.710,0.710,0.702),variation=0.005),
          material('DarkFittings',(0.295,0.295,0.285),variation=0.008)]
    obj.data.materials.clear()
    for mat in mats: obj.data.materials.append(mat)
    for p in obj.data.polygons:
        group=group_for[p.vertices[0]]
        p.material_index=0 if group==0 else 1 if group in (1,13,17) else 3 if group in (5,16) else 4 if group in (4,14) else 2
    target=bpy.data.images.new(NAME+'_CleanAlbedo',width=2048,height=2048,alpha=False)
    for mat in mats:
        node=mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image=target
        mat.node_tree.nodes.active=node
    obj.data.uv_layers.active=obj.data.uv_layers['RepairedUV']; obj.data.uv_layers.active.active_render=True
    for o in bpy.context.selected_objects: o.select_set(False)
    obj.select_set(True); bpy.context.view_layer.objects.active=obj
    OUT.mkdir(parents=True,exist_ok=True)
    target.file_format='PNG'; target.filepath_raw=str(OUT/'Concrete Utility pole_Clean_TB.png')
    # Preview exactly the single baked texture used by Unity, not the procedural shaders.
    preview=bpy.data.materials.new('ConcretePole_CleanPreview'); preview.use_nodes=True
    bsdf=next(n for n in preview.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bsdf.inputs['Roughness'].default_value=0.85
    tex=preview.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=target
    preview.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    obj.data.materials.clear(); obj.data.materials.append(preview)
    for p in obj.data.polygons: p.material_index=0
    source.hide_set(True); source.hide_render=True
    reunwrap_and_bake()
    print('BAKED',target.filepath_raw)
    print('VERTICES',len(obj.data.vertices),'POLYGONS',len(obj.data.polygons))

def reunwrap_and_bake():
    import math
    obj=bpy.data.objects[NAME+'_SurfaceClean']; mesh=obj.data
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    for o in bpy.context.selected_objects: o.select_set(False)
    obj.select_set(True); bpy.context.view_layer.objects.active=obj
    groups=components(mesh); group_for={v:i for i,g in enumerate(groups) for v in g}
    shaft_faces={p.index for p in mesh.polygons if group_for[p.vertices[0]]==0 and abs(p.normal.z)<0.8}
    for v in mesh.vertices: v.select=False
    for e in mesh.edges: e.select=False
    for p in mesh.polygons: p.select=p.index not in shaft_faces
    bpy.context.tool_settings.mesh_select_mode=(False,False,True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=0.03,margin_method='FRACTION',scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    uv=mesh.uv_layers['RepairedUV'].data
    for p in mesh.polygons:
        if p.index not in shaft_faces:
            for li in p.loop_indices:
                old=uv[li].uv.copy(); uv[li].uv=(0.66+0.31*old.x,0.03+0.94*old.y)
        else:
            coords=[mesh.vertices[mesh.loops[li].vertex_index].co for li in p.loop_indices]
            angles=[(math.atan2(v.y+0.05715,v.x)+math.pi)/(2*math.pi) for v in coords]
            if max(angles)-min(angles)>0.5: angles=[a+1 if a<0.5 else a for a in angles]
            for li,v,a in zip(p.loop_indices,coords,angles):
                uv[li].uv=(0.03+0.59*a/1.25,0.03+0.94*v.z/1.9990234375)
    mesh.update()
    preview=mesh.materials[0]
    labels=('Shaft','Concrete','Steel','Porcelain','DarkFittings')
    mesh.materials.clear()
    for label in labels: mesh.materials.append(bpy.data.materials['ConcreteCleanup_'+label])
    for p in mesh.polygons:
        g=group_for[p.vertices[0]]
        p.material_index=0 if g==0 else 1 if g in (1,13,17) else 3 if g in (5,16) else 4 if g in (4,14) else 2
    scene=bpy.context.scene
    state=(scene.render.engine,scene.render.bake.margin,scene.render.bake.margin_type,scene.render.bake.use_selected_to_active)
    try:
        scene.render.engine='CYCLES'; scene.render.bake.margin=12
        scene.render.bake.margin_type='EXTEND'; scene.render.bake.use_selected_to_active=False
        bpy.ops.object.bake(type='EMIT',use_clear=True)
    finally:
        scene.render.engine,scene.render.bake.margin,scene.render.bake.margin_type,scene.render.bake.use_selected_to_active=state
        mesh.materials.clear(); mesh.materials.append(preview)
        for p in mesh.polygons: p.material_index=0
    target=bpy.data.images[NAME+'_CleanAlbedo']; target.save(); target.pack()
    print('CYLINDRICAL SHAFT UV',len(shaft_faces),'faces; fittings packed separately')
