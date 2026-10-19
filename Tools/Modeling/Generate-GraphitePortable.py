"""Factory-machined graphite billet, with geometric edge and end chamfers."""
import argparse
import math

from PIL import Image, ImageDraw
from portable_mesh import PortableMesh, ASSETS, ROOT, sub, mul, cross, dot


MESH_GUID = 'fd9d379617664176a55cfbdb23b3c27b'
MATERIAL_GUID = 'f767f83f743a4776af7a2e0ec675711d'
TEXTURE_GUID = '58cc1c91e97446fe9df7e72d832d75bb'
SOURCE = ROOT / 'Tools/Modeling/Textures/Graphite_albedo.png'
RADIUS = .075
CENTER_Y = RADIUS * math.sin(math.pi / 3)
CENTER = (0, CENTER_Y, 0)
asset = PortableMesh('Graphite_P', 'Items/Materials/Graphite', MESH_GUID,
    eye=(1, .8, 1.6), origin=CENTER, scale=1900, vertex_limit=168)


def point(index, scale, z):
    side, edge = divmod(index % 12, 2)
    angle = math.tau * side / 6
    neighbor = angle + (-1 if edge == 0 else 1) * math.tau / 6
    return (RADIUS * scale * (.90 * math.cos(angle) + .10 * math.cos(neighbor)),
        CENTER_Y + RADIUS * scale * (.90 * math.sin(angle) + .10 * math.sin(neighbor)), z)


def atlas_uv(x, y):
    return (x / 1024, 1 - y / 1024)


def quad(points, bevel, side):
    normal = cross(sub(points[1], points[0]), sub(points[2], points[0]))
    if dot(normal, sub(points[0], CENTER)) < 0:
        normal = mul(normal, -1)
    if bevel:
        coords = ((812, 24), (1000, 24), (1000, 220), (812, 220))
    else:
        left, right = 4 + 760 * side / 6, 4 + 760 * (side + 1) / 6
        coords = ((left, 4), (right, 4), (right, 764), (left, 764))
    ids = [asset.vertex(p, normal, atlas_uv(*uv)) for p, uv in zip(points, coords)]
    asset.face(ids[0], ids[1], ids[2], CENTER)
    asset.face(ids[0], ids[2], ids[3], CENTER)


def build():
    asset.vertices.clear()
    asset.normals.clear()
    asset.uvs.clear()
    asset.triangles.clear()
    stations = ((.88, -.180), (1, -.169), (1, .169), (.88, .180))
    for segment in range(3):
        for side in range(12):
            points = [point(i, *stations[j]) for i, j in
                ((side, segment), (side + 1, segment),
                 (side + 1, segment + 1), (side, segment + 1))]
            quad(points, segment != 1 or side % 2 == 0, side // 2)
    for scale, z in (stations[0], stations[-1]):
        ids = []
        for side in range(12):
            p = point(side, scale, z)
            ids.append(asset.vertex(p, (0, 0, -1 if z < 0 else 1),
                atlas_uv(384 + p[0] / RADIUS * 340,
                    384 + (p[1] - CENTER_Y) / RADIUS * 340)))
        for side in range(1, 11):
            asset.face(ids[0], ids[side], ids[side + 1], CENTER)
    assert len(asset.vertices) == 168 and len(asset.triangles) == 92
    asset.validate()


def write_assets():
    with Image.open(SOURCE) as source:
        surface = source.convert('RGB').resize((768, 768), Image.Resampling.LANCZOS)
    atlas = Image.new('RGB', (1024, 1024), (53, 55, 59))
    atlas.paste(surface, (0, 0))
    ImageDraw.Draw(atlas).rectangle((792, 0, 1023, 256), fill=(162, 166, 174))
    atlas.save(asset.atlas)
    template = ASSETS / 'Items/Materials/Carbon nanotube'
    texture_meta = (template / 'Carbon nanotube_P_TB.png.meta').read_text(encoding='utf-8')
    texture_meta = texture_meta.replace('5e11075af67147edb1674cfe807593b3', TEXTURE_GUID)
    asset.atlas.with_suffix('.png.meta').write_text(texture_meta, encoding='utf-8')
    material = (template / 'M_Carbon nanotube_P.mat').read_text(encoding='utf-8')
    material = material.replace('M_Carbon nanotube_P', 'M_Graphite_P')
    material = material.replace('5e11075af67147edb1674cfe807593b3', TEXTURE_GUID)
    (asset.output.parent / 'M_Graphite_P.mat').write_text(material, encoding='utf-8')
    (asset.output.parent / 'M_Graphite_P.mat.meta').write_text(
        f'fileFormatVersion: 2\nguid: {MATERIAL_GUID}\nNativeFormatImporter:\n'
        '  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData:\n'
        '  assetBundleName:\n  assetBundleVariant:\n', encoding='utf-8')
    asset.write()


def check():
    asset.check()
    assert abs(min(p[1] for p in asset.vertices)) < 1e-8
    assert max(p[2] for p in asset.vertices) == .180
    assert min(p[2] for p in asset.vertices) == -.180
    # Every end is closed by ten triangles; the center must be inside the solid.
    for z in (-.180, .180):
        caps = [tri for tri in asset.triangles
            if all(abs(asset.vertices[i][2] - z) < 1e-8 for i in tri)]
        assert len(caps) == 10
    volume = sum(dot(asset.vertices[a], cross(asset.vertices[b], asset.vertices[c]))
        / 6 for a, b, c in asset.triangles)
    assert .004 < volume < .006
    material = (asset.output.parent / 'M_Graphite_P.mat').read_text(encoding='utf-8')
    assert material.count(f'guid: {TEXTURE_GUID}') == 2
    assert 'm_Name: M_Graphite_P' in material
    assert f'guid: {MESH_GUID}' in asset.output.with_suffix('.mesh.meta').read_text()
    assert f'guid: {MATERIAL_GUID}' in (asset.output.parent / 'M_Graphite_P.mat.meta').read_text()
    assert f'guid: {TEXTURE_GUID}' in asset.atlas.with_suffix('.png.meta').read_text()
    with Image.open(asset.atlas) as texture:
        assert texture.size == (1024, 1024) and texture.mode == 'RGB'
    print('PASS solid hexagonal billet, geometric chamfers, ground alignment, material/texture GUIDs.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    build()
    if not args.check:
        write_assets()
        asset.preview()
    check()
