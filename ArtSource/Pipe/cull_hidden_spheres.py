"""Conservative hidden-surface removal for the inspected straight Pipe.

Run analyze() in the live Blender session, review its report, then apply().
Only sphere triangles proven inside the collar or the solid tube wall are removed.
The hollow bore and central inspection window are not treated as solid occluders.
"""
import bpy
import bmesh
import hashlib
import json
import math
import runpy
import shutil
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

WORK = Path(__file__).resolve().parent
EPS = 1e-6


def key(points):
    return tuple(sorted(tuple(round(x, 6) for x in p) for p in points))


def radial_distance(a, b, c):
    # Distance from the bore axis to the projected triangle, including its edges.
    p = [Vector((v.x, v.z)) for v in (a,b,c)]
    cross = lambda u,v: u.x*v.y-u.y*v.x
    signs = [cross(p[i],p[(i+1)%3]) for i in range(3)]
    if min(signs) >= 0 or max(signs) <= 0:
        return 0.0
    result = min(v.length for v in p)
    for i in range(3):
        x,y = p[i],p[(i+1)%3]
        d = y-x
        if d.length_squared:
            t = max(0,min(1,-x.dot(d)/d.length_squared))
            result = min(result,(x+t*d).length)
    return result


def analyze():
    global source, api, removed, report, source_hash, blend_signature
    assert bpy.context.mode == 'OBJECT', 'Leave edit mode before analysis'
    api = runpy.run_path(str(WORK/'build_pipe_material.py'))
    source = api['read_mesh'](api['SOURCE'])
    source_hash = hashlib.sha256(api['SOURCE'].read_bytes()).hexdigest()
    mesh = bpy.data.objects['Pipe'].data
    coords = [v.co.copy() for v in mesh.vertices]
    adj = {v.index:set() for v in mesh.vertices}
    for edge in mesh.edges:
        a,b = edge.vertices
        adj[a].add(b); adj[b].add(a)
    remain, groups = set(adj), []
    while remain:
        todo, group = [next(iter(remain))], set()
        while todo:
            i=todo.pop()
            if i in group: continue
            group.add(i); todo.extend(adj[i]-group)
        remain -= group
        groups.append(group)
    assert sorted(map(len,groups)) == [42,42,336,386,386], 'Mesh structure changed; inspect again'
    body = next(g for g in groups if len(g)==336)
    spheres = [g for g in groups if len(g)==386]
    collars = [g for g in groups if len(g)==42]
    polygons = list(mesh.polygons)
    blend_signature = [key([coords[i] for i in p.vertices]) for p in polygons]
    # Actual collar facet half-spaces, verified convex rather than a guessed sphere.
    collar_planes=[]
    for group in collars:
        planes=[(p.center.copy(),p.normal.copy()) for p in polygons if p.vertices[0] in group]
        assert max((coords[i]-p).dot(n) for i in group for p,n in planes)<EPS
        collar_planes.append(planes)
    # Inspect this tube's end cross sections: outer radius .16, hollow bore .095.
    ys=sorted(set(round(coords[i].y,5) for i in body))
    assert ys == [-.48,-.11,.11,.48]
    radii=set(round(math.hypot(coords[i].x,coords[i].z),5) for i in body)
    assert radii == {.095,.16}
    outer_planes=[(p.center.copy(),p.normal.copy()) for p in polygons
                  if p.vertices[0] in body and abs(p.center.y)>.11
                  and abs(p.normal.y)<EPS and p.normal.dot(Vector((p.center.x,0,p.center.z)))>.1]
    assert outer_planes

    def covered(points, depth=0):
        if any(all((v-p).dot(n)<-EPS for v in points for p,n in planes)
               for planes in collar_planes):
            return True
        a,b,c=points
        in_end = (min(v.y for v in points)>.11+EPS and max(v.y for v in points)<.48-EPS
                  or min(v.y for v in points)>-.48+EPS and max(v.y for v in points)<-.11-EPS)
        if (in_end and radial_distance(a,b,c)>.095+EPS
                and all((v-p).dot(n)<-EPS for v in points for p,n in outer_planes)):
            return True
        if depth==3:
            return False
        ab,bc,ca=(a+b)/2,(b+c)/2,(c+a)/2
        return all(covered(t,depth+1) for t in ((a,ab,ca),(ab,b,bc),(ca,bc,c),(ab,bc,ca)))

    removed=set()
    sphere_stats=[]
    for group in spheres:
        faces=[p for p in polygons if p.vertices[0] in group]
        cut=[p for p in faces if covered(tuple(coords[i] for i in p.vertices))]
        removed.update(key([coords[i] for i in p.vertices]) for p in cut)
        sphere_stats.append({'before':len(faces),'removed':len(cut),'remaining':len(faces)-len(cut)})
    assert removed
    # Check visibility from a dense set of exterior views, including the bore window.
    old_faces=[tuple(p.vertices) for p in polygons]
    new_faces=[f for f in old_faces if key([coords[i] for i in f]) not in removed]
    old_tree=BVHTree.FromPolygons(coords,old_faces,all_triangles=True)
    new_tree=BVHTree.FromPolygons(coords,new_faces,all_triangles=True)
    rays=0
    for k in range(48):
        z=1-2*(k+.5)/48
        theta=k*math.pi*(3-math.sqrt(5))
        d=Vector((math.sqrt(1-z*z)*math.cos(theta),math.sqrt(1-z*z)*math.sin(theta),z))
        u=d.orthogonal().normalized(); v=d.cross(u).normalized()
        for ix in range(65):
            for iy in range(65):
                origin=d*2+u*((ix-32)*.021)+v*((iy-32)*.021)
                before=old_tree.ray_cast(origin,-d,4)[0]
                after=new_tree.ray_cast(origin,-d,4)[0]
                assert (before is None)==(after is None), 'Silhouette changed'
                if before is not None:
                    assert (before-after).length<2e-5, 'Visible surface changed'
                rays+=1
    report={'sphere_faces':sphere_stats,'removed_triangles':len(removed),
            'visibility_test_rays':rays,'visibility_changes':0,'source_sha256':source_hash}
    print(json.dumps(report))


def apply():
    assert hashlib.sha256(api['SOURCE'].read_bytes()).hexdigest()==source_hash
    obj=bpy.data.objects['Pipe']; mesh=obj.data
    assert bpy.context.mode=='OBJECT'
    assert [key([mesh.vertices[i].co for i in p.vertices]) for p in mesh.polygons]==blend_signature
    backup=WORK/'before_hidden_surface_cleanup'
    backup.mkdir(exist_ok=True)
    assert not (backup/'Pipe.asset').exists(), 'Do not overwrite backup'
    shutil.copy2(api['SOURCE'],backup/'Pipe.asset')
    bpy.ops.wm.save_as_mainfile(filepath=str(backup/'Pipe.blend'),copy=True)
    xyz=[Vector((x,z,y)) for x,y,z in source['xyz']]
    retained=[]; cut_count=0
    for i in range(0,len(source['indices']),3):
        tri=source['indices'][i:i+3]
        if key([xyz[j] for j in tri]) in removed:
            cut_count+=1
        else:
            retained.extend(tri)
    assert cut_count==len(removed)
    stride=source['stride']
    records=[source['raw'][i:i+stride] for i in range(0,len(source['raw']),stride)]
    vertices,indices=api['compact_vertices'](records,retained)
    for old,new in zip(retained,indices):
        assert records[old][:24]==vertices[new][:24] and records[old][40:]==vertices[new][40:]
    api['write_mesh'](source,vertices,indices,WORK/'Pipe_Baked.asset',retained_indices=retained)
    # Delete only those same faces in the authoring mesh, preserving corner UVs/normals.
    normals={}
    for poly in mesh.polygons:
        fk=key([mesh.vertices[i].co for i in poly.vertices])
        for li in poly.loop_indices:
            co=tuple(mesh.vertices[mesh.loops[li].vertex_index].co)
            normals[(fk,co)]=mesh.corner_normals[li].vector.copy()
    bm=bmesh.new(); bm.from_mesh(mesh)
    cut=[f for f in bm.faces if key([v.co for v in f.verts]) in removed]
    assert len(cut)==len(removed)
    for face in cut:
        bm.faces.remove(face)
    for edge in list(bm.edges):
        if not edge.link_faces:
            bm.edges.remove(edge)
    for vertex in list(bm.verts):
        if not vertex.link_edges:
            bm.verts.remove(vertex)
    bm.to_mesh(mesh); bm.free(); mesh.update()
    loop_normals=[]
    for p in mesh.polygons:
        fk=key([mesh.vertices[i].co for i in p.vertices])
        for li in p.loop_indices:
            co=tuple(mesh.vertices[mesh.loops[li].vertex_index].co)
            loop_normals.append(normals[(fk,co)])
    mesh.normals_split_custom_set(loop_normals)
    obj['pipe_removed_source_triangles']=len(removed)
    report.update(unity_vertices_before=len(records),unity_vertices_after=len(vertices),
                  unity_triangles_before=len(source['indices'])//3,unity_triangles_after=len(indices)//3,
                  blender_vertices_after=len(mesh.vertices),blender_faces_after=len(mesh.polygons),
                  surviving_positions_normals_uvs_preserved=True)
    (WORK/'hidden_surface_validation.json').write_text(json.dumps(report,indent=2))
    bpy.ops.wm.save_as_mainfile(filepath=str(WORK/'Pipe_CastIron.blend'))
    print(json.dumps(report))
