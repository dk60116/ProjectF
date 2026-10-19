"""Hollow six-sided Carbon nanotube_P, using the shared portable mesh exporter.

Texture source: built-in imagegen, flat coarse graphite/silver honeycomb albedo,
faceted struts, opaque, no perspective, no text. The source is packed unchanged
apart from resizing into the atlas; the other islands are inner-wall/rim colors.
"""
import argparse
import math

from PIL import Image, ImageDraw
from portable_mesh import PortableMesh, ASSETS, ROOT, cross, sub, dot


MESH_GUID = 'c5619b68030141db8f2da95cb1849b72'
MATERIAL_GUID = '96efbcdf955e4a518be01bdaf7cfc060'
TEXTURE_GUID = '5e11075af67147edb1674cfe807593b3'
SOURCE = ROOT / 'Tools/Modeling/Textures/Carbon nanotube_honeycomb.png'
asset = PortableMesh('Carbon nanotube_P', 'Items/Materials/Carbon nanotube',
    MESH_GUID, eye=(1, .8, 1.6), origin=(0, .061, 0), scale=1900,
    vertex_limit=72)
OUTER, INNER, HALF_LENGTH = .070, .051, .180
CENTER_Y = OUTER * math.sin(math.pi / 3)


def point(radius, side, z):
    angle = math.tau * (side % 6) / 6
    return (radius * math.cos(angle), CENTER_Y + radius * math.sin(angle), z)


def triangle(a, b, c):
    # Concave inner faces must use their own normals, not the tube's center.
    n = cross(sub(asset.vertices[b], asset.vertices[a]),
        sub(asset.vertices[c], asset.vertices[a]))
    if dot(n, asset.normals[a]) < 0:
        b, c = c, b
    asset.triangles.append((a, b, c))


def build():
    asset.vertices.clear()
    asset.normals.clear()
    asset.uvs.clear()
    asset.triangles.clear()
    for radius, sign in ((OUTER, 1), (INNER, -1)):
        for side in range(6):
            angle = math.tau * (side + .5) / 6
            normal = (sign * math.cos(angle), sign * math.sin(angle), 0)
            ids = []
            for edge, end in ((0, 0), (1, 0), (1, 1), (0, 1)):
                if sign == 1:
                    uv = ((4 + 760 * (side + edge) / 6) / 1024,
                        1 - (4 + 760 * end) / 1024)
                else:
                    uv = ((820 + 180 * edge) / 1024,
                        1 - (280 + 200 * end) / 1024)
                ids.append(asset.vertex(point(radius, side + edge,
                    HALF_LENGTH if end else -HALF_LENGTH), normal, uv))
            triangle(ids[0], ids[1], ids[2])
            triangle(ids[0], ids[2], ids[3])
    for z in (-HALF_LENGTH, HALF_LENGTH):
        rings = []
        for radius in (OUTER, INNER):
            ring = []
            for side in range(6):
                p = point(radius, side, z)
                uv = ((906 + p[0] / OUTER * 92) / 1024,
                    1 - (128 + (p[1] - CENTER_Y) / OUTER * 92) / 1024)
                ring.append(asset.vertex(p, (0, 0, 1 if z > 0 else -1), uv))
            rings.append(ring)
        for side in range(6):
            other = (side + 1) % 6
            a, b = rings[0][side], rings[0][other]
            c, d = rings[1][other], rings[1][side]
            triangle(a, b, c)
            triangle(a, c, d)
    assert len(asset.vertices) == 72 and len(asset.triangles) == 48
    asset.validate()


def write_assets():
    with Image.open(SOURCE) as source:
        texture = source.convert('RGB').resize((768, 768), Image.Resampling.LANCZOS)
    atlas = Image.new('RGB', (1024, 1024), (46, 48, 51))
    atlas.paste(texture, (0, 0))
    draw = ImageDraw.Draw(atlas)
    draw.rectangle((792, 8, 1023, 248), fill=(163, 168, 175))
    draw.rectangle((792, 256, 1023, 512), fill=(47, 50, 55))
    atlas.save(asset.atlas)
    meta = f'''fileFormatVersion: 2
guid: {TEXTURE_GUID}
TextureImporter:
  serializedVersion: 13
  mipmaps:
    enableMipMap: 1
    sRGBTexture: 1
  isReadable: 0
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 2
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  maxTextureSize: 1024
  textureType: 0
  textureShape: 1
  alphaIsTransparency: 0
  platformSettings: []
  userData:
  assetBundleName:
  assetBundleVariant:
'''
    asset.atlas.with_suffix('.png.meta').write_text(meta, encoding='utf-8')
    material = (ASSETS / 'Items/Equip/Crossbow/M_Crossbow.mat').read_text(encoding='utf-8')
    material = material.replace('m_Name: M_Crossbow', 'm_Name: M_Carbon nanotube_P')
    material = material.replace('6f4838f896553a54580633cde8fc3437', TEXTURE_GUID)
    (asset.output.parent / 'M_Carbon nanotube_P.mat').write_text(material, encoding='utf-8')
    (asset.output.parent / 'M_Carbon nanotube_P.mat.meta').write_text(
        f'fileFormatVersion: 2\nguid: {MATERIAL_GUID}\nNativeFormatImporter:\n'
        '  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData:\n'
        '  assetBundleName:\n  assetBundleVariant:\n', encoding='utf-8')
    asset.write()


def check():
    asset.check()
    assert abs(min(p[1] for p in asset.vertices)) < 1e-8
    assert abs(max(p[2] for p in asset.vertices) - HALF_LENGTH) < 1e-8
    # End triangles are annuli: no hidden cap may close either opening.
    for a, b, c in asset.triangles:
        points = [asset.vertices[i] for i in (a, b, c)]
        if all(abs(p[2] - points[0][2]) < 1e-8 for p in points):
            assert all(math.hypot(p[0], p[1] - CENTER_Y) >= INNER - 1e-8 for p in points)
            # The central axial ray must miss every triangle of both end rings.
            signs = []
            for p, q in zip(points, points[1:] + points[:1]):
                signs.append((q[0] - p[0]) * (CENTER_Y - p[1])
                    - (q[1] - p[1]) * -p[0])
            assert min(signs) < -1e-8 and max(signs) > 1e-8
    material = (asset.output.parent / 'M_Carbon nanotube_P.mat').read_text(encoding='utf-8')
    assert material.count(f'guid: {TEXTURE_GUID}') == 2
    assert 'm_Name: M_Carbon nanotube_P' in material
    assert f'guid: {MESH_GUID}' in asset.output.with_suffix('.mesh.meta').read_text()
    with Image.open(asset.atlas) as atlas:
        assert atlas.size == (1024, 1024) and atlas.mode == 'RGB'
    print('PASS hexagonal hollow tube, ground-aligned bounds, dedicated texture/material references.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    build()
    if not args.check:
        write_assets()
        asset.preview()
    check()
