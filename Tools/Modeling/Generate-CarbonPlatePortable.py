"""Pack the carbon weave into the existing shared Plate_P UV layout.

Never export or modify a mesh: other plate items share this exact asset.
Keep the current material, texture GUID and user-selected texture resolution.
"""
import argparse
import hashlib
import math
import re
import struct

from PIL import Image, ImageDraw
from portable_mesh import PortableMesh, ASSETS, ROOT


MESH = ASSETS / 'Items/Plate/Plate_P.mesh'
MESH_GUID = 'ae2adc0685fa9a64994b8105637502b4'
MATERIAL_GUID = '34f4072e245e49d9bdcff52bec92ab64'
TEXTURE_GUID = 'b4fcff4148ba4a80b2fa2c5fc7d60257'
SOURCE = ROOT / 'Tools/Modeling/Textures/Carbon plate_weave.png'
asset = PortableMesh('Carbon plate_P', 'Items/Plate/Carbon plate', MESH_GUID,
    eye=(1.2, 1.25, 1), origin=(0, .02011495, 0), scale=2500, vertex_limit=24)
asset.output = MESH


def digest(path):
    return hashlib.sha256(path.read_bytes()).digest()


def build():
    text = MESH.read_text(encoding='utf-8')
    # Plate_P's serialized stream is position3/normal3/tangent4/uv2, float32.
    assert re.search(r'm_VertexCount: (\d+)', text)[1] == '24'
    assert 'm_IndexFormat: 0' in text
    data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', text)[1])
    assert len(data) == 24 * 48
    values = list(struct.iter_unpack('<12f', data))
    asset.vertices = [p[:3] for p in values]
    asset.normals = [p[3:6] for p in values]
    asset.uvs = [p[10:12] for p in values]
    indices = bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)', text)[1])
    asset.triangles = list(struct.iter_unpack('<3H', indices))
    assert len(asset.triangles) == 12
    asset.validate()


def face_box(start, size):
    coords = asset.uvs[start:start + 4]
    return (max(0, math.floor(min(u for u, v in coords) * size)),
        max(0, math.floor((1 - max(v for u, v in coords)) * size)),
        min(size, math.ceil(max(u for u, v in coords) * size)),
        min(size, math.ceil((1 - min(v for u, v in coords)) * size)))


def write_atlas():
    with Image.open(asset.atlas) as current:
        size, height = current.size
    assert size == height and size >= 128
    atlas = Image.new('RGB', (size, size), (44, 46, 50))
    with Image.open(SOURCE) as source:
        weave = source.convert('RGB')
    for start in range(0, 24, 4):
        left, top, right, bottom = face_box(start, size)
        width, height = right - left, bottom - top
        normal = asset.normals[start]
        if normal[1] > .9:
            patch = weave.resize((width, height), Image.Resampling.LANCZOS)
            # The shared box has no bevel geometry; retain a thin edge highlight.
            ImageDraw.Draw(patch).rectangle((0, 0, width - 1, height - 1),
                outline=(165, 169, 177), width=max(1, round(width * .012)))
        else:
            patch = Image.new('RGB', (width, height),
                (44, 46, 50) if normal[1] < -.9 else (63, 66, 71))
            if abs(normal[1]) < .1:
                draw = ImageDraw.Draw(patch)
                draw.line((0, 0, width - 1, 0), fill=(113, 117, 124))
                draw.line((0, height - 1, width - 1, height - 1), fill=(40, 42, 46))
        atlas.paste(patch, (left, top))
    atlas.save(asset.atlas)


def check():
    asset.validate()
    assert len(asset.vertices) == 24 and len(asset.triangles) == 12
    assert all(0 <= x <= 1 for t in asset.uvs for x in t)
    folder = asset.atlas.parent
    material = (folder / 'M_Carbon plate_P.mat').read_text(encoding='utf-8')
    assert material.count(f'guid: {TEXTURE_GUID}') == 2
    assert f'guid: {TEXTURE_GUID}' in asset.atlas.with_suffix('.png.meta').read_text()
    assert f'guid: {MATERIAL_GUID}' in (folder / 'M_Carbon plate_P.mat.meta').read_text()
    assert f'guid: {MESH_GUID}' in MESH.with_suffix('.mesh.meta').read_text()
    item = (ASSETS / 'Data/Items/Item_138_Carbon plate.asset').read_text(encoding='utf-8')
    assert f'portableMesh: {{fileID: 4300000, guid: {MESH_GUID}, type: 2}}' in item
    assert f'portableMat: {{fileID: 2100000, guid: {MATERIAL_GUID}, type: 2}}' in item
    with Image.open(asset.atlas) as texture:
        size, height = texture.size
        assert size == height and texture.mode == 'RGB'
        for start in range(0, 24, 4):
            normal = asset.normals[start]
            left, top, right, bottom = face_box(start, size)
            if normal[1] > .9:
                patch = texture.crop((left + 2, top + 2, right - 2, bottom - 2))
                assert patch.convert('L').getextrema()[1] - patch.convert('L').getextrema()[0] > 30
                assert left >= size * .49 and top >= size * .47
            else:
                assert texture.getpixel(((left + right) // 2, (top + bottom) // 2)) == (
                    (44, 46, 50) if normal[1] < -.9 else (63, 66, 71))
        print(f'PASS {size}x{height} atlas: top weave and five dark faces match actual Plate_P UVs.')
    assert not (folder / 'Carbon plate_P.mesh').exists()
    assert not (ROOT / 'Tools/Modeling/Carbon plate_P.obj').exists()
    print('PASS shared Plate_P (24 vertices/12 triangles), item/material references, no duplicate mesh.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    immutable = (MESH, MESH.with_suffix('.mesh.meta'),
        asset.atlas.with_suffix('.png.meta'), asset.atlas.parent / 'M_Carbon plate_P.mat',
        asset.atlas.parent / 'M_Carbon plate_P.mat.meta',
        ASSETS / 'Data/Items/Item_138_Carbon plate.asset')
    before = {path: digest(path) for path in immutable}
    build()
    if not args.check:
        write_atlas()
        asset.preview()
    check()
    assert all(digest(path) == value for path, value in before.items())
    print('PASS shared mesh, ItemDefinition, material and import settings unchanged.')
