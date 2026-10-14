"""Validate the concrete portable asset links and render its actual mesh offline."""
import argparse
import hashlib
import re
import struct

from PIL import Image
from portable_mesh import ASSETS, PortableMesh


def guid(path):
    return re.search(r"^guid: ([0-9a-f]{32})$", path.read_text(), re.M)[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--preview', action='store_true')
    args = parser.parse_args()
    folder = ASSETS / 'Items/Plate/Concrete block'
    mesh_path = folder / 'Concrete block_P.mesh'
    atlas_path = folder / 'Concrete block_P_TB.png'
    material_path = folder / 'M_Concrete block_P.mat'
    item_path = ASSETS / 'Data/Items/Item_131_Concrete block.asset'
    mesh_text = mesh_path.read_text()
    material_text = material_path.read_text()
    item_text = item_path.read_text()
    vertex_data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', mesh_text)[1])
    channels = [tuple(map(int, match)) for match in re.findall(
        r'- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)', mesh_text)]
    assert channels[0] == (0, 0, 0, 3)
    assert channels[1] == (0, 12, 0, 3)
    assert channels[2] == (0, 24, 0, 4)
    assert channels[4] == (0, 40, 0, 2)
    assert all(channel[3] == 0 for i, channel in enumerate(channels) if i not in (0, 1, 2, 4))
    count = int(re.search(r'm_VertexCount: (\d+)', mesh_text)[1])
    assert count == 24 and len(vertex_data) == count * 48
    assert int(re.search(r'm_DataSize: (\d+)', mesh_text)[1]) == len(vertex_data)
    assert 'm_IndexFormat: 0' in mesh_text
    index_data = bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)', mesh_text)[1])
    assert len(index_data) == int(re.search(r'indexCount: (\d+)', mesh_text)[1]) * 2
    mesh = PortableMesh('Concrete block_P', 'Items/Plate/Concrete block',
                        guid(mesh_path.with_suffix('.mesh.meta')),
                        eye=(1, 0.9, 1), origin=(0, 0.025, 0), scale=2400)
    for vertex in struct.iter_unpack('<12f', vertex_data):
        mesh.vertices.append(vertex[:3])
        mesh.normals.append(vertex[3:6])
        mesh.uvs.append(vertex[10:12])
    mesh.triangles = list(struct.iter_unpack('<3H', index_data))
    assert all(0 <= index < count for tri in mesh.triangles for index in tri)
    assert all(0 <= value <= 1 for uv in mesh.uvs for value in uv)
    mesh.validate()
    for field, file_id, path in (
        ('portableMesh', 4300000, mesh_path.with_suffix('.mesh.meta')),
        ('portableMat', 2100000, material_path.with_suffix('.mat.meta')),
    ):
        assert f'{field}: {{fileID: {file_id}, guid: {guid(path)}, type: 2}}' in item_text
    texture_guid = guid(atlas_path.with_suffix('.png.meta'))
    assert f'm_Texture: {{fileID: 2800000, guid: {texture_guid}, type: 3}}' in material_text
    assert guid(ASSETS / 'Shaders/ToonCharacter.shader.meta') in material_text
    assert guid(ASSETS / 'Items/M_Object.mat.meta') in material_text
    assert '_BaseColor: {r: 1, g: 1, b: 1, a: 1}' in material_text
    meta = atlas_path.with_suffix('.png.meta').read_text()
    assert 'enableMipMap: 1' in meta and 'sRGBTexture: 1' in meta
    assert 'maxTextureSize: 512' in meta and 'textureType: 0' in meta
    with Image.open(atlas_path) as image:
        assert image.width == image.height and image.width >= 512
        assert image.convert('RGBA').getchannel('A').getextrema() == (255, 255)
    print(f'PASS: {count} vertices, {len(mesh.triangles)} triangles, closed mesh, valid UVs, opaque texture, material and item links.')
    print('Mesh SHA256: ' + hashlib.sha256(mesh_path.read_bytes()).hexdigest())
    if args.preview:
        mesh.preview()
        print('Preview: Tools/Modeling/Previews/Concrete block_P.png (offline, approximate lighting)')


if __name__ == '__main__':
    main()
