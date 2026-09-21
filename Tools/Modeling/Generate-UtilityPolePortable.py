"""UtilityPole_P: 120 actual vertices, dedicated atlas, no application launch."""
import math
import sys
from portable_mesh import PortableMesh, add, sub, mul, cross, unit, uv

asset=PortableMesh('UtilityPole_P','Items/Electro/Utility pole',
                   '497ed4be74c34a118c2d524541d031a8',eye=(0.8,0.55,2),origin=(0,0.166,0),scale=1930)
v,n,t,tris=asset.vertices,asset.normals,asset.uvs,asset.triangles
vertex,face=asset.vertex,asset.face

def pole():
    center=(0,0.15,0)
    base_radius,top_radius=0.0238,0.0175
    rings=[]
    for end in range(2):
        ring=[]
        for j in range(5):
            angle=math.tau*j/5+0.3
            normal=(math.cos(angle),0,math.sin(angle))
            p=add((0,end*0.30,0),mul(normal,base_radius if end==0 else top_radius))
            side_normal=(normal[0],(base_radius-top_radius)/0.30,normal[2])
            ring.append(vertex(p,side_normal,uv(256+210*math.cos(angle),35+end*950)))
        rings.append(ring)
    for j in range(5):
        k=(j+1)%5
        face(rings[0][j],rings[0][k],rings[1][k],center)
        face(rings[0][j],rings[1][k],rings[1][j],center)
    for end in range(2):
        cap=[]
        for j in range(5):
            a=math.tau*j/5
            cap.append(vertex(v[rings[end][j]],(0,-1 if end==0 else 1,0),uv(256+80*math.cos(a),512+80*math.sin(a))))
        for j in range(1,4): face(cap[0],cap[j],cap[j+1],center)

def beam(p0,p1,width,depth,flat):
    axis=unit(sub(p1,p0)); side=unit(cross(axis,(0,0,1))); front=(0,0,1)
    center=mul(add(p0,p1),0.5)
    positions=[]; local=[]
    for end in range(2):
        for a,b in [(-1,-1),(1,-1),(1,1),(-1,1)]:
            positions.append(add(p0 if end==0 else p1,add(mul(side,a*width/2),mul(front,b*depth/2))))
            local.append((end,a,b))
    quads=[(0,1,2,3),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    if flat:
        for q in quads:
            normal=unit(cross(sub(positions[q[1]],positions[q[0]]),sub(positions[q[2]],positions[q[0]])))
            if sum(x*y for x,y in zip(normal,sub(positions[q[0]],center)))<0: normal=mul(normal,-1)
            ids=[]
            for k in q:
                end,a,b=local[k]
                ids.append(vertex(positions[k],normal,uv(255+65*a+24*b,60+end*900+20*b)))
            face(ids[0],ids[1],ids[2],center); face(ids[0],ids[2],ids[3],center)
    else:
        ids=[]
        for p,(end,a,b) in zip(positions,local):
            normal=add(add(mul(side,a),mul(front,b)),mul(axis,(end*2-1)*0.25))
            ids.append(vertex(p,normal,uv(255+65*a+24*b,90+end*780+20*b)))
        for q in quads:
            face(ids[q[0]],ids[q[1]],ids[q[2]],center)
            face(ids[q[0]],ids[q[2]],ids[q[3]],center)

def insulator(x,y):
    start=len(v); first_tri=len(tris)
    center=(x,y+0.014,0)
    for height,radius in [(0,0.013),(0.010,0.007),(0.014,0.0105),(0.024,0.005)]:
        for j in range(4):
            a=math.tau*j/4+math.pi/4
            vertex((x+radius*math.cos(a),y+height,radius*math.sin(a)),(0,1,0),
                   uv(768+180*math.cos(a+0.3),35+height/0.03*430))
    bottom=vertex((x,y-0.002,0),(0,-1,0),uv(768,22))
    top=vertex((x,y+0.030,0),(0,1,0),uv(768,476))
    for ring in range(3):
        for j in range(4):
            a=start+ring*4+j; b=start+ring*4+(j+1)%4
            face(a,b,b+4,center); face(a,b+4,a+4,center)
    for j in range(4):
        face(bottom,start+j,start+(j+1)%4,center)
        face(top,start+12+j,start+12+(j+1)%4,center)
    # Area-weighted normals retain the ribs with no extra split vertices.
    for i in range(start,len(v)): n[i]=(0,0,0)
    for a,b,c in tris[first_tri:]:
        normal=cross(sub(v[b],v[a]),sub(v[c],v[a]))
        for i in (a,b,c): n[i]=add(n[i],normal)
    for i in range(start,len(v)): n[i]=unit(n[i])

def bolt():
    start=len(v); center=(0,0.255,0.018)
    for axis,radius in [((1,0,0),0.0055),((0,1,0),0.0055),((0,0,1),0.003)]:
        for sign in (1,-1):
            normal=mul(axis,sign); p=add(center,mul(normal,radius))
            vertex(p,normal,uv(768+normal[0]*130,768+normal[1]*130))
    for a in (0,1):
        for b in (2,3):
            for c in (4,5): face(start+a,start+b,start+c,center)

def build():
    pole()
    beam((-0.111,0.255,0),(0.111,0.255,0),0.025,0.027,True)
    for sign in (-1,1):
        beam((sign*0.006,0.194,0.012),(sign*0.067,0.244,0.012),0.010,0.012,False)
    insulator(-0.086,0.2675); insulator(0.086,0.2675); insulator(0,0.30)
    bolt()
    assert len(v)==120 and len(tris)==156

if __name__=='__main__':
    build()
    if '--check' not in sys.argv:
        asset.write()
        asset.preview()
    asset.check()
