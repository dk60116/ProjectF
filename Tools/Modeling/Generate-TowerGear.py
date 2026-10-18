"""Generate the standalone tower cog mesh and an offline geometry preview; no editor control."""
import argparse
import math
from pathlib import Path

from PIL import Image, ImageDraw
from portable_mesh import PortableMesh, ASSETS, ROOT, add, sub, mul, cross, dot, unit


FOLDER = 'MapObject/Battle Object/Tower'
MESH_GUID = 'ad7d4a0d850244a0ad8f43ed1ed6bb43'
MATERIAL_GUID = '3062a9a79fbe4884bf138ce3cb3db41d'
asset = PortableMesh('TowerGear', FOLDER, MESH_GUID, vertex_limit=2000)
OUTPUT = ASSETS / FOLDER
TEETH = 10
QUAD_COUNT = TEETH * 4 * 12
UV_GRID = math.ceil(math.sqrt(QUAD_COUNT))


def quad(points, outward):
    normal = cross(sub(points[1], points[0]), sub(points[2], points[0]))
    if dot(normal, outward) < 0:
        points = [points[0], points[3], points[2], points[1]]
        normal = mul(normal, -1)
    index = len(asset.triangles) // 2
    x, y = index % UV_GRID, index // UV_GRID
    ids = [asset.vertex(p, normal, ((x + u) / UV_GRID, (y + v) / UV_GRID))
        for p, (u, v) in zip(points, ((.06,.06),(.94,.06),(.94,.94),(.06,.94)))]
    asset.triangles.extend(((ids[0],ids[1],ids[2]), (ids[0],ids[2],ids[3])))


def build():
    asset.vertices.clear(); asset.normals.clear(); asset.uvs.clear(); asset.triangles.clear()
    outline = []
    half_width = .072
    tip = math.sqrt(.5**2 - half_width**2)
    for tooth in range(TEETH):
        angle = tooth * math.tau / TEETH
        radial = (math.cos(angle), 0, math.sin(angle))
        tangent = (-radial[2], 0, radial[0])
        for radius, width in ((.37,-half_width),(tip,-half_width),(tip,half_width),(.37,half_width)):
            outline.append(add(mul(radial, radius), mul(tangent, width)))
    angles = [math.atan2(p[2],p[0]) for p in outline]
    def outer(scale, y): return [(p[0]*scale,y,p[2]*scale) for p in outline]
    def circle(radius, y): return [(radius*math.cos(a),y,radius*math.sin(a)) for a in angles]
    rings = [outer(.965,0), outer(1,.012), outer(1,.108), outer(.965,.12),
        circle(.20,.12), circle(.185,.135), circle(.185,.19), circle(.17,.205),
        circle(.105,.205), circle(.09,.19), circle(.09,.012), circle(.105,0)]
    for band, ring in enumerate(rings):
        next_ring = rings[(band+1) % len(rings)]
        for i in range(len(outline)):
            j = (i+1) % len(outline)
            edge = sub(ring[j],ring[i])
            radial = unit((edge[2],0,-edge[0]))
            if band in (3,7): normal = (0,1,0)
            elif band == 11: normal = (0,-1,0)
            elif band in (8,9,10): normal = mul(radial,-1)
            else: normal = radial
            quad([ring[i],ring[j],next_ring[j],next_ring[i]],normal)
    assert len(asset.vertices) == 1920 and len(asset.triangles) == 960
    asset.validate()


def vertical_hits(x,z):
    hits = []
    for tri in asset.triangles:
        a,b,c = [asset.vertices[i] for i in tri]
        denominator = (b[2]-c[2])*(a[0]-c[0])+(c[0]-b[0])*(a[2]-c[2])
        if abs(denominator) < 1e-10: continue
        u = ((b[2]-c[2])*(x-c[0])+(c[0]-b[0])*(z-c[2]))/denominator
        v = ((c[2]-a[2])*(x-c[0])+(a[0]-c[0])*(z-c[2]))/denominator
        if min(u,v,1-u-v) >= -1e-7:
            hits.append(u*a[1]+v*b[1]+(1-u-v)*c[1])
    return hits


def validate_profile():
    assert not vertical_hits(0,0), 'Center must be a real through-hole, not a black surface.'
    assert abs(max(vertical_hits(.14,0))-.205) < 1e-6, 'Raised center collar is missing.'
    assert abs(max(vertical_hits(.28,0))-.12) < 1e-6, 'Gear top is missing.'
    for i in range(TEETH):
        angle = i*math.tau/TEETH
        assert vertical_hits(.46*math.cos(angle),.46*math.sin(angle)), 'Tooth tip is missing.'
        gap = angle+math.pi/TEETH
        assert not vertical_hits(.46*math.cos(gap),.46*math.sin(gap)), 'Tooth gap is filled.'
    assert min(p[1] for p in asset.vertices) == 0
    assert max(math.hypot(p[0],p[2]) for p in asset.vertices) <= .500001


def render(eye, size=560):
    background = (31,36,42)
    image = Image.new('RGB',(size,size),background)
    pixels = image.load()
    forward = unit(eye)
    reference = (0,0,1) if abs(forward[1]) > .99 else (0,1,0)
    right = unit(cross(reference,forward)); up = cross(forward,right)
    center = (0,.1025,0); scale = size*.82
    projected = [(size*.5+dot(sub(p,center),right)*scale,
        size*.5-dot(sub(p,center),up)*scale,dot(p,forward)) for p in asset.vertices]
    depth = [-math.inf]*(size*size)
    light = unit((-.45,1,.6))
    for tri in asset.triangles:
        normal = asset.normals[tri[0]]
        if dot(normal,forward) <= 0: continue
        a,b,c = [projected[i] for i in tri]
        area = (b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(area) < 1e-9: continue
        shade = .40+.68*max(0,dot(normal,light))
        color = tuple(min(255,round(value*shade)) for value in (202,209,222))
        for y in range(max(0,math.floor(min(a[1],b[1],c[1]))), min(size,math.ceil(max(a[1],b[1],c[1])))):
            for x in range(max(0,math.floor(min(a[0],b[0],c[0]))), min(size,math.ceil(max(a[0],b[0],c[0])))):
                u = ((b[1]-c[1])*(x+.5-c[0])+(c[0]-b[0])*(y+.5-c[1]))/area
                v = ((c[1]-a[1])*(x+.5-c[0])+(a[0]-c[0])*(y+.5-c[1]))/area
                w = 1-u-v
                if min(u,v,w) < 0: continue
                z = u*a[2]+v*b[2]+w*c[2]
                if z > depth[y*size+x]:
                    depth[y*size+x] = z; pixels[x,y] = color
    assert sum(v != -math.inf for v in depth) > size*size*.05, 'Preview mesh is blank.'
    if abs(forward[1]) > .99:
        assert pixels[size//2,size//2] == background, 'Top view must show the open center.'
    return image


def preview():
    image = Image.new('RGB',(1680,640),(31,36,42))
    draw = ImageDraw.Draw(image)
    for i,(label,eye) in enumerate((('ISOMETRIC',(.8,1.35,1.5)),('TOP / OPEN CENTER',(0,1,0)),
        ('SIDE / COLLAR',(.8,.3,1.5)))):
        image.paste(render(eye),(560*i,36))
        draw.text((560*i+20,16),label,fill=(230,233,239))
    draw.text((20,608),'TowerGear | 10 teeth | 1920 vertices / 960 triangles | 1.0 diameter / 0.205 height | offline geometry preview',
        fill=(210,216,225))
    path = ROOT/'Tools/Modeling/Previews/TowerGear.png'
    image.save(path)
    return path


def check():
    asset.check(); validate_profile()
    prefab = (OUTPUT/'TowerGear.prefab').read_text(encoding='utf-8')
    assert f'm_Mesh: {{fileID: 4300000, guid: {MESH_GUID}, type: 2}}' in prefab
    assert f'{{fileID: 2100000, guid: {MATERIAL_GUID}, type: 2}}' in prefab
    material = (OUTPUT/'M_TowerGear.mat').read_text(encoding='utf-8')
    assert 'guid: 2e827c8c74aa43a084b5b71af6db2df4' in material
    source = (OUTPUT/'TowerGear.obj').read_text(encoding='utf-8').splitlines()
    assert sum(line.startswith('v ') for line in source) == len(asset.vertices)
    assert sum(line.startswith('f ') for line in source) == len(asset.triangles)
    print('PASS: 10 teeth, open through-hole, raised collar, Y-up bottom pivot, prefab/material references and OBJ source.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check',action='store_true',help='Validate saved assets without rewriting them.')
    args = parser.parse_args()
    build(); validate_profile()
    if not args.check:
        for name,guid in (('TowerGear.mesh',MESH_GUID),('M_TowerGear.mat',MATERIAL_GUID)):
            target = OUTPUT/name
            if target.exists() and f'guid: {guid}' not in target.with_suffix(target.suffix+'.meta').read_text(encoding='utf-8'):
                raise RuntimeError(f'Refusing to replace an unrelated asset: {target}')
        asset.write(source_path=OUTPUT/'TowerGear.obj')
        print(preview())
    check()
