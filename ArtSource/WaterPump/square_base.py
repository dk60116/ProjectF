"""Square the existing base plate; preserve topology, UVs and texture bytes."""
import bpy,re,json,hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector
w=Path(__file__).resolve().parent
backup=w/'original/before_rectangular_base'
bpy.ops.wm.open_mainfile(filepath=str(backup/'WaterPump_clean.blend'))
obj=bpy.data.objects['Water_pump'];m=obj.data
old=np.array([v.co[:] for v in m.vertices]); ns=[n.vector.copy() for n in m.corner_normals]
parent=list(range(len(m.vertices)))
def find(i):
 while parent[i]!=i: parent[i]=parent[parent[i]];i=parent[i]
 return i
for e in m.edges:parent[find(e.vertices[0])]=find(e.vertices[1])
groups={}
for v in m.vertices:groups.setdefault(find(v.index),[]).append(v.index)
base=[ids for ids in groups.values() if old[ids,2].max()<.16]
plate=next(ids for ids in base if len(ids)==8 and np.ptp(old[ids,1])>.6)
feet=[ids for ids in base if len(ids)>50]
assert len(base)==3 and len(feet)==2
width=max(abs(old[plate,0]))
top=max(old[plate,2]);bottom=min(old[plate,2])
for i in plate:
 m.vertices[i].co.x=width if old[i,0]>0 else -width
 m.vertices[i].co.y=max(old[plate,1]) if old[i,1]>0 else min(old[plate,1])
 m.vertices[i].co.z=top if old[i,2]>.1 else bottom
# Equal front/back foot widths, retaining their existing small bevels and texture mapping.
foot_width=max(abs(old[i,0]) for ids in feet for i in ids)
scales={}
for ids in feet:
 sx=foot_width/max(abs(old[ids,0]));scales.update({i:sx for i in ids})
 for i in ids:m.vertices[i].co.x=old[i,0]*sx
m.update()
plate_set=set(plate)
for p in m.polygons:
 for li in p.loop_indices:
  vi=m.loops[li].vertex_index
  if vi in plate_set:ns[li]=p.normal.copy()
  elif vi in scales:ns[li]=Vector((ns[li].x/scales[vi],ns[li].y,ns[li].z)).normalized()
m.normals_split_custom_set(ns)
new=np.array([v.co[:] for v in m.vertices]);changed=np.where(np.max(abs(new-old),axis=1)>1e-7)[0]
assert set(changed)<=set(sum(base,[]))
assert np.array_equal(new[[i for i in range(len(old)) if i not in set(sum(base,[]))]],old[[i for i in range(len(old)) if i not in set(sum(base,[]))]])
source=(backup/'Water pump.fbx').read_text()
def patch(text,key,values):
 values=list(values);rows=[','.join(format(float(v),'.17g') for v in values[i:i+48]) for i in range(0,len(values),48)]
 replacement=key+': *'+str(len(values))+' {\n\t\t\t\ta: '+',\n'.join(rows)+'\n\t\t\t}'
 text,n=re.subn(r'\b'+key+r': \*\d+\s*\{\s*a:\s*[^}]+}',lambda _:replacement,text,count=1);assert n==1
 return text
# Retain exact original coordinate text values for untouched vertices.
def arr(key):return np.array([float(v) for v in re.search(r'\b'+key+r': \*\d+\s*\{\s*a:\s*([^}]+)',source).group(1).split(',')])
xyz=arr('Vertices').reshape(-1,3)
for i in set(changed)|plate_set:xyz[i]=(new[i,0]*100,new[i,2]*100,-new[i,1]*100)
out=patch(source,'Vertices',xyz.flat)
affected=[l.index for l in m.loops if l.vertex_index in set(sum(base,[]))]
normals=arr('Normals').reshape(-1,3)
for li in affected:
 n=m.corner_normals[li].vector;normals[li]=(n.x,n.z,-n.y)
out=patch(out,'Normals',normals.flat)
m.calc_tangents(uvmap='UVSet0')
for key,attr in [('Tangents','tangent'),('Binormals','bitangent')]:
 values=arr(key).reshape(-1,3)
 for li in affected:
  v=getattr(m.loops[li],attr);values[li]=(v.x,v.z,-v.y)
 out=patch(out,key,values.flat)
(w/'Water pump.fbx').write_text(out)
scene=bpy.context.scene
scene.cycles.samples=32
cam=scene.camera;target=Vector((0,0,.36))
for name,pos in [('front',(1.2,-1.8,1.1)),('back',(-1.2,1.8,1.05)),('top',(0,0,2))]:
 cam.location=pos;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
 scene.render.filepath=str(w/('rectangular_base_'+name+'.png'));bpy.ops.render.render(write_still=True)
scene.camera.location=(1.2,-1.8,1.1);scene.camera.rotation_euler=(target-scene.camera.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.wm.save_as_mainfile(filepath=str(w/'WaterPump_clean.blend'))
report={'changed_vertex_ids':changed.tolist(),'base_vertex_ids':sum(base,[]),'plate_vertex_ids':plate,'plate_width':2*width,'plate_depth':float(np.ptp(new[plate,1])),'plate_bottom':bottom,'plate_top':top,'front_back_foot_width':2*foot_width,'texture_sha256':hashlib.sha256((w/'Water pump_TB.png').read_bytes()).hexdigest(),'uv_and_topology_unchanged':True,'upper_mesh_unchanged':True}
(w/'rectangular_base.json').write_text(json.dumps(report,indent=2))
print(json.dumps({k:v for k,v in report.items() if not k.endswith('_ids')},indent=2))
