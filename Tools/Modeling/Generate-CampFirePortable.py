"""Generate CampFire_P using the shared portable-mesh exporter."""
import math
import sys
from portable_mesh import PortableMesh, add, sub, mul, dot, cross, unit, uv

asset = PortableMesh('CampFire_P', 'Items/InputOutputModule/Camp fire',
                     '905e98c597794daaa18f0a5eccdd42bc')
vertices, normals, uvs, triangles = asset.vertices, asset.normals, asset.uvs, asset.triangles
vertex, face = asset.vertex, asset.face
parts = []

def stone(index):
    # Two pentagonal rims, with a small irregular twist for blunt low-poly rocks.
    angle = index*math.tau/6 + 0.17
    radial = (math.cos(angle),0,math.sin(angle))
    tangent = (-math.sin(angle),0,math.cos(angle))
    h = 0.023 + 0.002*math.sin(index*2.4)
    center = add(mul(radial,0.136), (0,h,0))
    start = len(vertices)
    for level in range(2):
        for j in range(5):
            t=math.tau*j/5+0.21*index+level*0.10
            scale=(1.0 if level==0 else 0.78)*(1+0.08*math.sin(j*4+index))
            lateral=add(mul(radial,math.cos(t)*0.036*scale),mul(tangent,math.sin(t)*0.080*scale))
            y=-h if level==0 else h*(0.8+0.14*math.sin(j*2+index))
            p=add(center,add(lateral,(0,y,0)))
            n=add(add(mul(radial,math.cos(t)*0.7),mul(tangent,math.sin(t)*0.45)),(0,-0.5 if level==0 else 0.85,0))
            # Allocate a broad outer UV band to the sides to prevent vertical
            # streaks caused by projecting both rims onto almost the same texels.
            texture_radius = 215 if level == 0 else 95
            vertex(p,n,uv(768+texture_radius*math.cos(t),
                          256+texture_radius*math.sin(t)))
    for j in range(5):
        k=(j+1)%5
        face(start+j,start+k,start+5+k,center)
        face(start+j,start+5+k,start+5+j,center)
    for j in range(1,4):
        face(start,start+j,start+j+1,center)
        face(start+5,start+5+j,start+5+j+1,center)
    parts.append(('Stone_'+str(index+1), start, len(vertices)))

def log(index):
    # Five-sided log with separate cap normals: 10 side + 5 + 5 = 20.
    # Planar bark projection has no UV seam, keeping the actual vertex budget.
    angle = index*math.tau/3 + 0.28
    radial = (math.cos(angle),0,math.sin(angle))
    p0 = add(mul(radial,0.101),(0,0.033,0))
    p1 = add(mul(radial,-0.034),(0,0.144+index*0.014,0))
    axis = unit(sub(p1,p0))
    side = unit(cross(axis,(0,1,0)))
    up = unit(cross(axis,side))
    center = mul(add(p0,p1),0.5)
    radius = 0.0245
    start = len(vertices)
    rings = []
    for end,p in enumerate((p0,p1)):
        ring = []
        for j in range(5):
            t = math.tau*j/5
            n = add(mul(side,math.cos(t)),mul(up,math.sin(t)))
            pos = add(p,mul(n,radius*(1 if end==0 else 0.88)))
            ring.append(vertex(pos,n,uv(256+208*math.cos(t+0.29+index*0.17),48+928*end)))
        rings.append(ring)
    for j in range(5):
        k=(j+1)%5
        face(rings[0][j],rings[0][k],rings[1][k],center)
        face(rings[0][j],rings[1][k],rings[1][j],center)
    for end in range(2):
        rim = []
        for j in range(5):
            t = math.tau*j/5
            rim.append(vertex(vertices[rings[end][j]],mul(axis,-1 if end==0 else 1),
                              uv(768+200*math.cos(t),774+200*math.sin(t))))
        for j in range(1,4): face(rim[0],rim[j],rim[j+1],center)
    parts.append(('Log_'+str(index+1),start,len(vertices)))

def build():
    for i in range(6): stone(i)
    for i in range(3): log(i)
    assert len(vertices) == 120 and len(triangles) == 144

if __name__ == '__main__':
    build()
    if '--check' not in sys.argv:
        asset.write()
        asset.preview()
    asset.check()
