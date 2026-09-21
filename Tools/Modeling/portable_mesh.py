"""Native Unity portable-mesh export, atlas preview and geometry validation."""
from pathlib import Path
import math
import re
import struct
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'FactorioProject/Assets'
def add(a,b): return tuple(x+y for x,y in zip(a,b))
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def mul(a,k): return tuple(x*k for x in a)
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def unit(a): return mul(a,1/math.sqrt(dot(a,a)))
def uv(x,y): return (x/1024,1-y/1024)

class PortableMesh:
    def __init__(self,name,folder,guid,eye=(1,0.9,1.4),origin=(0,0.073,0),scale=1850):
        self.name,self.guid=name,guid
        self.output=ASSETS/folder/(name+'.mesh')
        self.atlas=ASSETS/folder/(name+'_TB.png')
        self.eye,self.origin,self.scale=eye,origin,scale
        self.vertices,self.normals,self.uvs,self.triangles=[],[],[],[]

    def vertex(self,p,n,t):
        self.vertices.append(p); self.normals.append(unit(n)); self.uvs.append(t)
        return len(self.vertices)-1

    def face(self,a,b,c,center):
        v=self.vertices
        n=cross(sub(v[b],v[a]),sub(v[c],v[a]))
        if dot(n,sub(mul(add(add(v[a],v[b]),v[c]),1/3),center))<0: b,c=c,b
        self.triangles.append((a,b,c))

    def validate(self):
        v,n,t=self.vertices,self.normals,self.uvs
        assert 0<len(v)<=120
        ids,edges={},{}
        welded=[ids.setdefault(tuple(round(x,7) for x in p),len(ids)) for p in v]
        for a,b,c in self.triangles:
            normal=cross(sub(v[b],v[a]),sub(v[c],v[a]))
            assert dot(normal,normal)>1e-14
            assert dot(normal,add(add(n[a],n[b]),n[c]))>0
            u,w,q=t[a],t[b],t[c]
            assert abs((w[0]-u[0])*(q[1]-u[1])-(w[1]-u[1])*(q[0]-u[0]))>1e-9
            for i,j in ((a,b),(b,c),(c,a)):
                i,j=welded[i],welded[j]
                edge=tuple(sorted((i,j)))
                count,balance=edges.get(edge,(0,0))
                edges[edge]=(count+1,balance+(1 if i<j else -1))
        assert all(c==2 and b==0 for c,b in edges.values())

    def buffers(self):
        indices=b''.join(struct.pack('<3H',*tri) for tri in self.triangles)
        data=bytearray()
        for p,n,t in zip(self.vertices,self.normals,self.uvs):
            tangent=unit(cross((0,1,0) if abs(n[1])<0.99 else (1,0,0),n))
            data.extend(struct.pack('<12f',*p,*n,*tangent,-1,*t))
        return indices,data

    def write(self):
        self.validate()
        text=(ASSETS/'Items/Plate/Steel plate/Steel Plate_P.mesh').read_text(encoding='utf-8')
        lower=[min(p[i] for p in self.vertices) for i in range(3)]
        upper=[max(p[i] for p in self.vertices) for i in range(3)]
        vector=lambda v: '{'+', '.join(f'{k}: {x:.9g}' for k,x in zip('xyz',v))+'}'
        text=text.replace('m_Name: Steel Plate_P','m_Name: '+self.name)
        for key,val in [('indexCount',len(self.triangles)*3),('vertexCount',len(self.vertices)),('m_VertexCount',len(self.vertices))]:
            text=re.sub(key+r': \d+',f'{key}: {val}',text)
        text=re.sub(r'm_Center: \{[^}]+\}','m_Center: '+vector([(a+b)/2 for a,b in zip(lower,upper)]),text)
        text=re.sub(r'm_Extent: \{[^}]+\}','m_Extent: '+vector([(b-a)/2 for a,b in zip(lower,upper)]),text)
        indices,data=self.buffers()
        text=re.sub(r'm_IndexBuffer: [0-9a-f]+','m_IndexBuffer: '+indices.hex(),text)
        text=re.sub(r'm_DataSize: \d+',f'm_DataSize: {len(data)}',text)
        text=re.sub(r'_typelessdata: [0-9a-f]+','_typelessdata: '+data.hex(),text)
        self.output.write_text(text,encoding='utf-8')
        meta=self.output.with_suffix('.mesh.meta')
        if not meta.exists():
            meta.write_text('fileFormatVersion: 2\nguid: '+self.guid+'\nNativeFormatImporter:\n'
                            '  externalObjects: {}\n  mainObjectFileID: 4300000\n'
                            '  userData:\n  assetBundleName:\n  assetBundleVariant:\n',encoding='utf-8')
        source=['# '+self.name+': Y up, meters','o '+self.name]
        for prefix,values in [('v',self.vertices),('vt',self.uvs),('vn',self.normals)]:
            source += [prefix+' '+' '.join(f'{x:.9g}' for x in value) for value in values]
        source += ['f '+' '.join(f'{i+1}/{i+1}/{i+1}' for i in tri) for tri in self.triangles]
        (ROOT/'Tools/Modeling'/ (self.name+'.obj')).write_text('\n'.join(source)+'\n',encoding='utf-8')

    def check(self):
        self.validate()
        text=self.output.read_text(encoding='utf-8')
        assert int(re.search(r'm_VertexCount: (\d+)',text)[1])==len(self.vertices)
        expected_indices,expected_data=self.buffers()
        assert bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)',text)[1])==expected_data
        assert bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)',text)[1])==expected_indices
        assert all(0<=x<=1 for t in self.uvs for x in t)
        print(f'PASS {self.name}: {len(self.vertices)} vertices, {len(self.triangles)} triangles; closed components, outward normals, valid UVs and serialized buffers.')

    def preview(self):
        # Rasterization of the actual mesh and atlas; not an editor screenshot.
        size=800
        texture=Image.open(self.atlas).convert('RGB')
        result=Image.new('RGB',(size,size),(35,39,43))
        pixels,tex=result.load(),texture.load()
        depth=[-1e9]*(size*size)
        eye=unit(self.eye); right=unit(cross((0,1,0),eye)); up=cross(eye,right)
        projected=[(400+dot(sub(p,self.origin),right)*self.scale,424-dot(sub(p,self.origin),up)*self.scale,dot(p,eye)) for p in self.vertices]
        light=unit((-0.5,1,0.7))
        for tri in self.triangles:
            a,b,c=[projected[i] for i in tri]
            denom=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(denom)<1e-9: continue
            for y in range(max(0,int(min(a[1],b[1],c[1]))),min(size,int(max(a[1],b[1],c[1]))+1)):
                for x in range(max(0,int(min(a[0],b[0],c[0]))),min(size,int(max(a[0],b[0],c[0]))+1)):
                    u=((b[1]-c[1])*(x+0.5-c[0])+(c[0]-b[0])*(y+0.5-c[1]))/denom
                    v=((c[1]-a[1])*(x+0.5-c[0])+(a[0]-c[0])*(y+0.5-c[1]))/denom
                    w=1-u-v
                    if min(u,v,w)<0: continue
                    z=u*a[2]+v*b[2]+w*c[2]
                    if z<=depth[y*size+x]: continue
                    depth[y*size+x]=z
                    t=[sum(self.uvs[i][k]*q for i,q in zip(tri,(u,v,w))) for k in range(2)]
                    n=unit(tuple(sum(self.normals[i][k]*q for i,q in zip(tri,(u,v,w))) for k in range(3)))
                    shade=0.55+0.65*max(0,dot(n,light))
                    color=tex[min(texture.width-1,max(0,int(t[0]*texture.width))),min(texture.height-1,max(0,int((1-t[1])*texture.height)))]
                    pixels[x,y]=tuple(min(255,int(ch*shade)) for ch in color)
        draw=ImageDraw.Draw(result)
        draw.text((32,28),f'{self.name} | {len(self.vertices)} vertices | {len(self.triangles)} triangles',fill=(226,227,230))
        draw.text((32,750),'M_'+self.name+' / dedicated texture atlas / offline mesh preview',fill=(170,180,188))
        path=ROOT/'Tools/Modeling/Previews'/(self.name+'.png')
        path.parent.mkdir(exist_ok=True)
        result.save(path)
