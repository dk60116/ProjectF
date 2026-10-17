"""Arrow_P: 48 actual Unity vertices, one material, no application launch."""
import argparse
import math
import re

from PIL import Image, ImageDraw
from portable_mesh import PortableMesh, ASSETS, add, sub, mul, cross, dot, unit


MESH_GUID = '60e4e9f38af34bc7bb0f0aaf09bd61c4'
MATERIAL_GUID = 'c55dd426d9df407c9c1e0d335499a923'
TEXTURE_GUID = 'd5992489fcf14b88ad084fc1ea4f6054'
asset = PortableMesh('Arrow_P', 'Items/Equip/Arrow', MESH_GUID,
    eye=(.9,1.4,1.8), origin=(0,.028,.015), scale=2100)
WOOD, STEEL, FEATHER = (.05,.55,.45,.95), (.55,.55,.95,.95), (.05,.05,.45,.45)
CENTER_Y = .029


def tex(rect, u, v):
    return (rect[0]+(rect[2]-rect[0])*u, rect[1]+(rect[3]-rect[1])*v)


def shaft():
    rings = []
    center = (0,CENTER_Y,-.020)
    for end,z in enumerate((-.170,.130)):
        ring = []
        for j in range(3):
            angle = math.tau*j/3 + math.pi/6
            normal = (math.cos(angle),math.sin(angle),0)
            p = add((0,CENTER_Y,z),mul(normal,.0045))
            ring.append(asset.vertex(p,normal,tex(WOOD,.5+.4*math.cos(angle),.1+.8*end)))
        rings.append(ring)
    for j in range(3):
        k = (j+1)%3
        a,b,c,d = rings[0][j],rings[0][k],rings[1][k],rings[1][j]
        asset.face(a,b,c,center); asset.face(a,c,d,center)
    for end in (0,1):
        cap = []
        for j in range(3):
            angle = math.tau*j/3
            cap.append(asset.vertex(asset.vertices[rings[end][j]],(0,0,-1 if end==0 else 1),
                tex(WOOD,.5+.4*math.cos(angle),.5+.4*math.sin(angle))))
        asset.face(*cap,center)


def arrowhead():
    # A sharp diamond section with independent normals for all eight metal facets.
    center = (0,CENTER_Y,.155)
    rim = ((-.023,CENTER_Y,.149), (0,CENTER_Y+.008,.149),
        (.023,CENTER_Y,.149), (0,CENTER_Y-.008,.149))
    for tip in ((0,CENTER_Y,.205),(0,CENTER_Y,.117)):
        for j in range(4):
            points = [tip,rim[j],rim[(j+1)%4]]
            normal = cross(sub(points[1],points[0]),sub(points[2],points[0]))
            if dot(normal,sub(points[0],center)) < 0:
                points[1],points[2] = points[2],points[1]
                normal = mul(normal,-1)
            ids = [asset.vertex(p,normal,tex(STEEL,*t))
                for p,t in zip(points,((.5,.95),(.05,.05),(.95,.05)))]
            asset.face(*ids,center)


def feather(angle):
    radial = (math.cos(angle),math.sin(angle),0)
    tangent = (-math.sin(angle),math.cos(angle),0)
    points = [
        add((0,CENTER_Y,-.163),mul(radial,.003)),
        add((0,CENTER_Y,-.145),mul(radial,.028)),
        add((0,CENTER_Y,-.104),mul(radial,.003)),
        add(add((0,CENTER_Y,-.143),mul(radial,.024)),mul(tangent,.0018)),
    ]
    center = mul(add(add(points[0],points[1]),add(points[2],points[3])),.25)
    # Closed thin tetrahedral vanes retain thickness without double-sided materials.
    start = len(asset.vertices)
    uv_points = ((.1,.1),(.9,.35),(.1,.9),(.75,.43))
    for p,t in zip(points,uv_points):
        asset.vertex(p,sub(p,center),tex(FEATHER,*t))
    for a,b,c in ((0,1,2),(0,1,3),(1,2,3),(2,0,3)):
        asset.face(start+a,start+b,start+c,center)


def build():
    asset.vertices.clear(); asset.normals.clear(); asset.uvs.clear(); asset.triangles.clear()
    shaft()
    arrowhead()
    for j in range(3): feather(math.tau*j/3+math.pi/6)
    assert len(asset.vertices) == 48 and len(asset.vertices) < 50
    assert len(asset.triangles) == 28
    asset.validate()


def write_material():
    # A procedural three-color palette keeps this geometry-only asset self-contained.
    palette = Image.new('RGB',(64,64),(45,38,31))
    draw = ImageDraw.Draw(palette)
    draw.rectangle((0,0,31,31),fill=(205,128,47))
    draw.rectangle((32,0,63,31),fill=(182,190,202))
    draw.rectangle((0,32,31,63),fill=(238,225,201))
    palette.save(asset.atlas)
    meta = '''fileFormatVersion: 2
guid: TEXTURE_GUID
TextureImporter:
  serializedVersion: 13
  mipmaps:
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 1
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  maxTextureSize: 64
  textureType: 0
  textureShape: 1
  alphaIsTransparency: 0
  platformSettings: []
  userData:
  assetBundleName:
  assetBundleVariant:
'''
    asset.atlas.with_suffix('.png.meta').write_text(meta.replace('TEXTURE_GUID',TEXTURE_GUID),encoding='utf-8')
    material = (ASSETS/'Items/Equip/Crossbow/M_Crossbow.mat').read_text(encoding='utf-8')
    material = material.replace('m_Name: M_Crossbow','m_Name: M_Arrow_P')
    material = material.replace('guid: 6f4838f896553a54580633cde8fc3437',f'guid: {TEXTURE_GUID}')
    (asset.output.parent/'M_Arrow_P.mat').write_text(material,encoding='utf-8')
    (asset.output.parent/'M_Arrow_P.mat.meta').write_text(
        f'fileFormatVersion: 2\nguid: {MATERIAL_GUID}\nNativeFormatImporter:\n'
        '  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData:\n'
        '  assetBundleName:\n  assetBundleVariant:\n',encoding='utf-8')


def check():
    asset.check()
    item = (ASSETS/'Data/Items/Item_35_Arrow.asset').read_text(encoding='utf-8')
    assert f'portableMesh: {{fileID: 4300000, guid: {MESH_GUID}, type: 2}}' in item
    assert f'portableMat: {{fileID: 2100000, guid: {MATERIAL_GUID}, type: 2}}' in item
    material = (asset.output.parent/'M_Arrow_P.mat').read_text(encoding='utf-8')
    assert material.count(f'guid: {TEXTURE_GUID}') == 2
    data = asset.output.read_text(encoding='utf-8')
    assert int(re.search(r'm_DataSize: (\d+)',data)[1]) == 48*48
    assert len(asset.vertices) < 50
    print('PASS strict <50 vertex budget, single-submesh material and Arrow item references.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check',action='store_true',help='Validate saved geometry and references without writing.')
    args = parser.parse_args()
    build()
    if not args.check:
        asset.write()
        write_material()
        asset.preview()
    check()
