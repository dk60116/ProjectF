"""Map both pump ports to the original Pump_TB atlas in the live Blender scene.

Preserves body UVs and geometry. Former steel, brass and bore material faces are
assigned small matching regions of the existing texture, then merged to slot 0.
"""
import bpy
from mathutils import Vector

obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
mesh=obj.data
assert len(mesh.vertices)==4146
assert len(mesh.materials)==4
assert not obj.get('pump_single_atlas'), 'Already textured'
assert len(mesh.uv_layers)==1

material=mesh.materials[0]
assert material and material.use_nodes
images=[n.image for n in material.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
assert any(image.name.startswith('Pump_TB') for image in images), 'Original Pump texture missing'

uv=mesh.uv_layers.active.data
world=obj.matrix_world
normal_matrix=world.to_3x3()
counts=[0,0,0,0]

def atlas_point(pixel_x,pixel_y,point,center):
    # Small planar projection adds baked texture variation to each face while
    # keeping UVs inside a consistent, color-matched patch of the old atlas.
    dx=point-center
    u=pixel_x+max(-5.,min(5.,dx.x*45.+dx.y*30.))
    v=pixel_y+max(-5.,min(5.,dx.z*48.-dx.y*24.))
    return (u/512.,1.-v/512.)

for polygon in mesh.polygons:
    slot=polygon.material_index
    counts[slot]+=1
    if slot==0:
        continue
    n=normal_matrix @ polygon.normal
    n.normalize()
    if slot==1:
        if n.z>.35:
            patch=(160.,385.)  # light painted steel on the original top cover
        elif n.z<-.35:
            patch=(285.,300.)  # dark steel below the main housing
        else:
            patch=(450.,270.)  # medium steel on the existing pipework
    elif slot==2:
        patch=(262.,467.)      # bright original brass bolt/accent
    else:
        patch=(343.,312.)      # original recessed dark metal
    center=world @ polygon.center
    for loop_index in polygon.loop_indices:
        point=world @ mesh.vertices[mesh.loops[loop_index].vertex_index].co
        uv[loop_index].uv=atlas_point(*patch,point,center)
    polygon.material_index=0

while len(mesh.materials)>1:
    mesh.materials.pop(index=len(mesh.materials)-1)
mesh.update()
assert all(p.material_index==0 for p in mesh.polygons)
assert all(0.<uv[li].uv.x<1. and 0.<uv[li].uv.y<1.
           for p in mesh.polygons for li in p.loop_indices)
obj['pump_single_atlas']=True
print('ATLAS',counts,'faces by previous slot; one material',mesh.materials[0].name,
      'polygons',len(mesh.polygons),'UV loops',len(uv))
