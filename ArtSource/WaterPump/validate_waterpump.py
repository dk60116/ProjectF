"""Asset-only validation. Does not launch Unity or modify assets."""
import json, re, struct, hashlib
from pathlib import Path
import numpy as np

work=Path(__file__).parent
asset=work.parents[1]/'FactorioProject/Assets/MapObject/Fluid/Water pump'
original=(work/'original/Water pump.fbx') if (work/'original/Water pump.fbx').exists() else asset/'Water pump.fbx'
source=original.read_text(); candidate=(work/'Water pump.fbx').read_text()
keys=('UV','UVIndex','Tangents','Binormals')
base_report_path=work/'rectangular_base.json'
base_report=json.loads(base_report_path.read_text()) if base_report_path.exists() else None
if base_report:keys+=('Vertices','Normals')
def pattern(key):return r'\b'+key+r': \*\d+\s*\{\s*a:\s*([^}]+)}'
def array(text,key,dtype=float):
    return np.array([dtype(v) for v in re.search(pattern(key),text).group(1).split(',')])
def immutable(text):
    for key in keys:text=re.sub(pattern(key),key+': <updated>',text)
    return text
assert immutable(source)==immutable(candidate),'Unexpected changes outside permitted mesh arrays'
if base_report:
    reference=(work/'original/before_rectangular_base/Water pump.fbx').read_text()
    allowed=np.array(base_report['base_vertex_ids'],dtype=int)
    original_xyz=array(reference,'Vertices').reshape(-1,3)
    updated_xyz=array(candidate,'Vertices').reshape(-1,3)
    changed=np.any(original_xyz!=updated_xyz,axis=1)
    assert set(np.where(changed)[0])<=set(allowed),'Changed geometry outside the base'
    assert original_xyz[allowed,1].max()<16,'Base selection includes upper geometry'
    for key in ('UV','UVIndex','PolygonVertexIndex'):
        assert np.array_equal(array(reference,key),array(candidate,key)),key+' changed'
    p=updated_xyz[base_report['plate_vertex_ids']]
    assert all(len(np.unique(p[:,axis]))==2 for axis in range(3)),'Plate is not rectangular'
    assert len(set(map(tuple,p)))==8,'Plate corners are not distinct'
    vi0=array(candidate,'PolygonVertexIndex',int)
    vi0=np.where(vi0<0,-vi0-1,vi0)
    unaffected=~np.isin(vi0,allowed)
    for key in ('Normals','Tangents','Binormals'):
        assert np.array_equal(array(reference,key).reshape(-1,3)[unaffected],array(candidate,key).reshape(-1,3)[unaffected]),key+' changed outside base'
    assert hashlib.sha256((work/'Water pump_TB.png').read_bytes()).hexdigest()==base_report['texture_sha256']
indices=array(candidate,'PolygonVertexIndex',int).reshape(-1,3)
uv=array(candidate,'UV').reshape(-1,2)
uvi=array(candidate,'UVIndex',int)
assert len(uvi)==indices.size and uvi.min()>=0 and uvi.max()<len(uv)
assert np.isfinite(uv).all() and uv.min()>=0 and uv.max()<=1
assert np.isfinite(array(candidate,'Tangents')).all()
assert np.isfinite(array(candidate,'Binormals')).all()
triangles=uv[uvi].reshape(-1,3,2)
area=np.abs(np.cross(triangles[:,1]-triangles[:,0],triangles[:,2]-triangles[:,0]))*.5
xyz=array(candidate,'Vertices').reshape(-1,3)
vi=np.where(indices<0,-indices-1,indices)
world=xyz[vi]
geometry_area=np.linalg.norm(np.cross(world[:,1]-world[:,0],world[:,2]-world[:,0]),axis=1)*.5
degenerate_geometry=geometry_area<1e-12
assert (area[~degenerate_geometry]>1e-12).all(),'Degenerate UV on a visible triangle'
size=2048
owner=np.full((size,size),-1,np.int32)
overlaps=0
for i,tri in enumerate(triangles*size):
    if degenerate_geometry[i]:continue
    low=np.maximum(np.floor(tri.min(0)).astype(int),0);high=np.minimum(np.ceil(tri.max(0)).astype(int),size)
    xx,yy=np.meshgrid(np.arange(low[0],high[0])+.5,np.arange(low[1],high[1])+.5)
    a,b,c=tri;den=np.cross(b-a,c-a)
    u=((xx-a[0])*(c[1]-a[1])-(yy-a[1])*(c[0]-a[0]))/den
    v=((b[0]-a[0])*(yy-a[1])-(b[1]-a[1])*(xx-a[0]))/den
    inside=(u>1e-6)&(v>1e-6)&(u+v<1-1e-6)
    region=owner[low[1]:high[1],low[0]:high[0]]
    overlaps+=int(np.count_nonzero(inside&(region>=0)))
    region[inside]=i
assert overlaps==0,f'{overlaps} overlapping interior texels'
png=(work/'Water pump_TB.png').read_bytes()
assert png[:8]==b'\x89PNG\r\n\x1a\n'
dimensions=struct.unpack('>II',png[16:24]);assert dimensions==(2048,2048)
report=dict(geometry_and_ids_unchanged=base_report is None,ids_and_topology_unchanged=True,upper_mesh_unchanged=True,rectangular_base_verified=base_report is not None,vertices=len(array(candidate,'Vertices'))//3,triangles=len(indices),uv_range=[float(uv.min()),float(uv.max())],degenerate_uv_on_visible_triangles=0,preexisting_zero_area_geometry=int(degenerate_geometry.sum()),overlapping_interior_texels=overlaps,uv_area_fraction=float(area.sum()),texture_dimensions=dimensions,fbx_sha256=hashlib.sha256((work/'Water pump.fbx').read_bytes()).hexdigest(),texture_sha256=hashlib.sha256(png).hexdigest(),installed_matches_generated=all((work/name).read_bytes()==(asset/name).read_bytes() for name in ['Water pump.fbx','Water pump_TB.png']))
(work/'validation.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
