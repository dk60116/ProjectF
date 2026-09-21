"""Read-only regression check for installed-pole UV/tangent edits."""
from pathlib import Path
import re
import subprocess
import math

ROOT=Path(__file__).resolve().parents[2]
LABELS=('Binormals','Tangents','UV','UVIndex')
def block(text,label):
    return re.search(r'(?m)^[\t ]*'+label+r': \*\d+\s*\{\s*a:\s*([^}]+)\}[\t ]*',text)
def data(text,label):
    return [float(v) for v in block(text,label).group(1).split(',') if v.strip()]
def fixed_content(text):
    text=text.replace('\r\n','\n')
    for label in LABELS:
        m=block(text,label); text=text[:m.start()]+'ATTRIBUTE:'+label+text[m.end():]
    return text

for name,count in [('Utility pole',237),('Concrete Utility pole',377)]:
    rel=f'FactorioProject/Assets/MapObject/Electro/{name}/{name}.fbx'
    old=subprocess.check_output(['git','show','HEAD:'+rel],cwd=ROOT).decode('utf-8')
    new=(ROOT/rel).read_text(encoding='utf-8')
    assert fixed_content(old)==fixed_content(new), 'Unexpected geometry/ID/normal/transform change'
    assert len(data(new,'Vertices'))//3==count
    indices=[int(x) for x in data(new,'PolygonVertexIndex')]
    uv=data(new,'UV'); uv_indices=data(new,'UVIndex')
    assert uv_indices==list(range(len(indices))) and len(uv)==len(indices)*2
    assert all(math.isfinite(c) and 0.02<c<0.98 for c in uv)
    face=[]; triangles=0
    for i,index in enumerate(indices):
        face.append(uv[2*i:2*i+2])
        if index<0:
            assert len(face)==3
            a,b,c=face
            assert abs((b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]))>1e-12
            face=[]; triangles+=1
    assert not face
    for label in ('Tangents','Binormals'):
        values=data(new,label)
        assert len(values)==len(indices)*3 and all(math.isfinite(v) for v in values)
        assert all(abs(sum(v*v for v in values[i:i+3])-1)<0.001 for i in range(0,len(values),3))
    print(f'PASS {name}: {count} vertices, {triangles} triangles; geometry/IDs/normals unchanged; UV and tangents valid.')
