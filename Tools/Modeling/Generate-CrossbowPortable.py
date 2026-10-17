"""Cross bow_P: 120 native Unity vertices, existing texture, no application launch."""
import argparse
import math
from pathlib import Path

from PIL import Image, ImageDraw
from portable_mesh import PortableMesh, add, sub, mul, cross, dot, unit


FOLDER = 'Items/Equip/Crossbow'
GUID = 'b3dc761f5fc048bc9fc8af11111f4468'
asset = PortableMesh('Cross bow_P', FOLDER, GUID)
asset.atlas = asset.output.parent / 'Crossbow_TB.png'
parts = []

# These patches are sampled from the existing Crossbow_TB, not a replacement atlas.
WOOD = (54, 282, 352, 412)
STEEL = (111, 137, 278, 192)
CORD = (25, 72, 153, 88)
DARK = (474, 120, 489, 180)


def tex(rect, u, v):
    x0, y0, x1, y1 = rect
    return ((x0 + (x1-x0)*u)/512, 1-(y0 + (y1-y0)*v)/512)


def quad(ids, center):
    asset.face(ids[0], ids[1], ids[2], center)
    asset.face(ids[0], ids[2], ids[3], center)


def sweep(name, stations, side, up, rect, cap_rect):
    """Four shared side vertices per station, four hard-normal vertices per cap."""
    start = len(asset.vertices)
    center = mul(add(stations[0][0], stations[-1][0]), .5)
    axis = unit(sub(stations[-1][0], stations[0][0]))
    rings = []
    corners = ((-1, -1), (1, -1), (1, 1), (-1, 1))
    for index, (position, width, height) in enumerate(stations):
        ring = []
        for a, b in corners:
            p = add(position, add(mul(side, a*width/2), mul(up, b*height/2)))
            n = add(mul(side, a), mul(up, b))
            ring.append(asset.vertex(p, n, tex(rect, .5 + .28*a + .12*b,
                .1 + .8*index/(len(stations)-1) + .025*b)))
        rings.append(ring)
    for segment, (first, second) in enumerate(zip(rings, rings[1:])):
        segment_center = mul(add(stations[segment][0], stations[segment+1][0]), .5)
        for j in range(4):
            k = (j+1) % 4
            quad((first[j], first[k], second[k], second[j]), segment_center)
    for end in (0, -1):
        cap = []
        for index, (a, b) in enumerate(corners):
            cap.append(asset.vertex(asset.vertices[rings[end][index]],
                mul(axis, -1 if end == 0 else 1), tex(cap_rect, .5+.4*a, .5+.4*b)))
        quad(cap, center)
    parts.append((name, start, len(asset.vertices)))


def beam(name, p0, p1, width, height, rect):
    """Eight shared vertices with bevel-like corner normals and nondegenerate UVs."""
    start = len(asset.vertices)
    center = mul(add(p0, p1), .5)
    axis = unit(sub(p1, p0))
    reference = (0, 1, 0) if abs(axis[1]) < .9 else (1, 0, 0)
    side = unit(cross(reference, axis))
    up = unit(cross(axis, side))
    ids = []
    for end, position in enumerate((p0, p1)):
        for a, b in ((-1,-1), (1,-1), (1,1), (-1,1)):
            p = add(position, add(mul(side, a*width/2), mul(up, b*height/2)))
            n = add(add(mul(side, a), mul(up, b)), mul(axis, (end*2-1)*.35))
            ids.append(asset.vertex(p, n, tex(rect, .5+.28*a+.12*b,
                .15+.7*end+.055*b)))
    for q in ((0,1,2,3), (4,5,6,7), (0,1,5,4), (1,2,6,5), (2,3,7,6), (3,0,4,7)):
        quad(tuple(ids[i] for i in q), center)
    parts.append((name, start, len(asset.vertices)))


def build():
    asset.vertices.clear(); asset.normals.clear(); asset.uvs.clear(); asset.triangles.clear()
    parts.clear()
    # The stock is wider at the butt; the bow sweeps back toward its drawn string.
    sweep('Stock', (
        ((0,.049,-.190), .080, .062),
        ((0,.049,-.145), .057, .045),
        ((0,.049,-.048), .036, .037),
        ((0,.058, .155), .042, .042),
    ), (1,0,0), (0,1,0), WOOD, STEEL)
    for sign in (-1, 1):
        sweep('LimbLeft' if sign < 0 else 'LimbRight', (
            ((sign*.017,.060,.132), .044, .030),
            ((sign*.110,.059,.123), .038, .027),
            ((sign*.188,.058,.068), .020, .018),
        ), (0,0,1), (0,1,0), STEEL, DARK)
        beam('StringLeft' if sign < 0 else 'StringRight',
            (sign*.188,.073,.068), (0,.073,-.014), .004, .004, CORD)
    # Three overlapping guard bars leave a real opening under the trigger.
    beam('GuardFront', (0,.035,-.012), (0,.002,-.018), .008, .009, DARK)
    beam('GuardBottom', (0,.004,-.016), (0,.004,-.065), .008, .009, DARK)
    beam('GuardRear', (0,.002,-.063), (0,.035,-.067), .008, .009, DARK)
    beam('Trigger', (0,.035,-.039), (0,.012,-.044), .006, .007, STEEL)
    beam('BoltRail', (0,.081,-.019), (0,.081,.151), .013, .007, STEEL)
    assert len(asset.vertices) == 120
    assert len(asset.triangles) == 152
    asset.validate()


def render(eye, size=600, textured=True):
    """Offline rasterization of the exported geometry, not an editor capture."""
    image = Image.new('RGB', (size,size), (35,39,43))
    pixels = image.load()
    texture = Image.open(asset.atlas).convert('RGB')
    source = texture.load()
    forward = unit(eye)
    right = unit(cross((0,1,0), forward))
    up = cross(forward, right)
    center = (0,.043,-.015)
    scale = size*1.95
    projected = [(size*.5 + dot(sub(p,center),right)*scale,
        size*.5 - dot(sub(p,center),up)*scale, dot(p,forward)) for p in asset.vertices]
    depth = [-math.inf]*(size*size)
    light = unit((-.45,1,.55))
    for tri in asset.triangles:
        a,b,c = [projected[i] for i in tri]
        area = (b[1]-c[1])*(a[0]-c[0]) + (c[0]-b[0])*(a[1]-c[1])
        if abs(area) < 1e-9: continue
        for y in range(max(0,math.floor(min(a[1],b[1],c[1]))),min(size,math.ceil(max(a[1],b[1],c[1])))):
            for x in range(max(0,math.floor(min(a[0],b[0],c[0]))),min(size,math.ceil(max(a[0],b[0],c[0])))):
                u = ((b[1]-c[1])*(x+.5-c[0])+(c[0]-b[0])*(y+.5-c[1]))/area
                v = ((c[1]-a[1])*(x+.5-c[0])+(a[0]-c[0])*(y+.5-c[1]))/area
                w = 1-u-v
                if min(u,v,w) < 0: continue
                z = sum(q*p[2] for q,p in zip((u,v,w),(a,b,c)))
                if z <= depth[y*size+x]: continue
                depth[y*size+x] = z
                n = unit(tuple(sum(asset.normals[i][k]*q for i,q in zip(tri,(u,v,w))) for k in range(3)))
                shade = .58+.60*max(0,dot(n,light))
                if textured:
                    t = [sum(asset.uvs[i][k]*q for i,q in zip(tri,(u,v,w))) for k in range(2)]
                    color = source[min(texture.width-1,max(0,int(t[0]*texture.width))),
                        min(texture.height-1,max(0,int((1-t[1])*texture.height)))]
                else: color = (185,194,202)
                pixels[x,y] = tuple(min(255,int(value*shade)) for value in color)
    assert sum(z != -math.inf for z in depth) > size*size*.025
    return image


def preview():
    result = Image.new('RGB', (1800,680), (35,39,43))
    for index, (label,eye,textured) in enumerate((
        ('TEXTURED', (.95,1.5,1.5), True),
        ('TOP', (.01,3,.001), True),
        ('SIDE / GEOMETRY', (2,.45,.2), False),
    )):
        result.paste(render(eye, textured=textured), (600*index,45))
        ImageDraw.Draw(result).text((600*index+24,24), label, fill=(225,230,235))
    ImageDraw.Draw(result).text((24,650),
        'Cross bow_P | 120 actual vertices | 152 triangles | existing Crossbow_TB | offline geometry preview',
        fill=(225,230,235))
    output = Path(__file__).parent / 'Previews/Cross bow_P.png'
    output.parent.mkdir(exist_ok=True)
    result.save(output)
    return output


def check_references():
    from portable_mesh import ASSETS
    item = (ASSETS / 'Data/Items/Item_132_Crossbow.asset').read_text(encoding='utf-8')
    assert f'portableMesh: {{fileID: 4300000, guid: {GUID}, type: 2}}' in item
    assert 'portableMat: {fileID: 2100000, guid: c80be484389611044afbe86ecea73f03, type: 2}' in item


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Verify geometry, saved buffers and item references without writing.')
    args = parser.parse_args()
    build()
    if not args.check:
        asset.write()
        print(preview())
    asset.check()
    check_references()
    print('PASS portable item references; original FBX and texture are unchanged.')
