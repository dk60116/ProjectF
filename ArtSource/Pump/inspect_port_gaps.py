"""Read-only geometric checks on the live Pump mesh."""
import bpy
import bmesh
import math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
bm=bmesh.new()
bm.from_mesh(obj.data)
bm.transform(obj.matrix_world)
bm.verts.ensure_lookup_table()
bm.faces.ensure_lookup_table()
tree=BVHTree.FromBMesh(bm)
cy,cz=.0009765625,.1962890625

for side in (-1,1):
    print('PORT',side)
    for radius in (0.,.03,.06,.08,.095,.11,.14,.17,.185):
        origin=Vector((side*.55,cy+radius,cz))
        hit=tree.ray_cast(origin,Vector((-side,0,0)),1.)
        print('RAY',radius,'x=',round(hit[0].x,5) if hit[0] else None,
              'material=',hit[2] is not None and bm.faces[hit[2]].material_index)

    cap=[f for f in bm.faces if all(abs(v.co.x-side*.32)<.0001 for v in f.verts)]
    print('CUT_CAP',len(cap),'areas',[round(f.calc_area(),5) for f in cap[:10]])
    missing=[]
    for radius in (.0,.02,.04,.06,.08,.09):
        for a in range(48):
            angle=2*math.pi*a/48
            origin=Vector((side*.55,cy+radius*math.cos(angle),cz+radius*math.sin(angle)))
            hit=tree.ray_cast(origin,Vector((-side,0,0)),1.)
            if hit[0] is None or abs(hit[0].x-side*.32)>.001:
                missing.append((radius,a,None if hit[0] is None else round(hit[0].x,4)))
    print('INNER_BORE_GAPS',len(missing),'examples',missing[:10])

print('BOUNDARY',sum(e.is_boundary for e in bm.edges),
      'NONMANIFOLD',sum(not e.is_manifold for e in bm.edges),
      'ZERO_AREA',sum(f.calc_area()<1e-12 for f in bm.faces))
bm.free()
