"""Give the center rock of Ore_Model.fbx its own material slot.

The source FBX is left untouched. Run this script after replacing Ore_Model.fbx
to regenerate the Gold ore variant without changing any rock geometry or UVs.
"""

from copy import deepcopy
from pathlib import Path
import struct
import zlib


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "FactorioProject/Assets/MapObject/Ore/Ore_Model.fbx"
OUTPUT = ROOT / "FactorioProject/Assets/MapObject/Ore/Gold ore/GoldOre_Model.fbx"
NULL_RECORD = bytes(13)


class Node:
    def __init__(self, name, properties, children, terminated):
        self.name = name
        self.properties = properties
        self.children = children
        self.terminated = terminated


def property_end(data, pos):
    kind = chr(data[pos])
    if kind in {"Y", "C", "I", "F", "D", "L"}:
        return pos + 1 + {"Y": 2, "C": 1, "I": 4, "F": 4, "D": 8, "L": 8}[kind]
    if kind in {"S", "R"}:
        return pos + 5 + struct.unpack_from("<I", data, pos + 1)[0]
    if kind in {"f", "d", "i", "l", "b", "c"}:
        return pos + 13 + struct.unpack_from("<I", data, pos + 9)[0]
    raise ValueError(f"Unsupported FBX property type: {kind}")


def parse_node(data, pos):
    end, count, length, name_length = struct.unpack_from("<IIIB", data, pos)
    name = data[pos + 13 : pos + 13 + name_length]
    prop_start = pos + 13 + name_length
    prop_end = prop_start + length
    properties = []
    cursor = prop_start
    for _ in range(count):
        next_cursor = property_end(data, cursor)
        properties.append(data[cursor:next_cursor])
        cursor = next_cursor
    assert cursor == prop_end

    children = []
    cursor = prop_end
    terminated = False
    while cursor < end:
        if data[cursor : cursor + 13] == NULL_RECORD:
            cursor += 13
            terminated = True
            break
        child, cursor = parse_node(data, cursor)
        children.append(child)
    assert cursor == end
    return Node(name, properties, children, terminated), end


def encode_node(node, start):
    name = node.name
    properties = b"".join(node.properties)
    result = bytearray(13 + len(name) + len(properties))
    result[13 : 13 + len(name)] = name
    result[13 + len(name) :] = properties
    for child in node.children:
        result.extend(encode_node(child, start + len(result)))
    if node.terminated:
        result.extend(NULL_RECORD)
    struct.pack_into("<IIIB", result, 0, start + len(result), len(node.properties), len(properties), len(name))
    return result


def string(raw):
    assert raw[0] == ord("S")
    return raw[5:].decode("utf-8")


def integer(raw):
    assert raw[0] in {ord("I"), ord("L")}
    return struct.unpack_from("<i" if raw[0] == ord("I") else "<q", raw, 1)[0]


def int_array(raw):
    assert raw[0] == ord("i")
    count, encoding, size = struct.unpack_from("<III", raw, 1)
    values = raw[13 : 13 + size]
    if encoding:
        values = zlib.decompress(values)
    return struct.unpack("<" + str(count) + "i", values)


def fbx_string(value):
    data = value.encode("utf-8")
    return b"S" + struct.pack("<I", len(data)) + data


def fbx_int_array(values):
    data = struct.pack("<" + str(len(values)) + "i", *values)
    return b"i" + struct.pack("<III", len(values), 0, len(data)) + data


def one_child(node, name):
    matches = [child for child in node.children if child.name == name]
    assert len(matches) == 1, (name, len(matches))
    return matches[0]


def main():
    data = SOURCE.read_bytes()
    assert data[:23] == b"Kaydara FBX Binary  \x00\x1a\x00"
    assert struct.unpack_from("<I", data, 23)[0] == 7400
    nodes = []
    cursor = 27
    while data[cursor : cursor + 13] != NULL_RECORD:
        node, cursor = parse_node(data, cursor)
        nodes.append(node)
    footer = data[cursor:]

    objects = next(node for node in nodes if node.name == b"Objects")
    geometry = one_child(objects, b"Geometry")
    model = one_child(objects, b"Model")
    material = one_child(objects, b"Material")
    vertices = one_child(geometry, b"Vertices").properties[0]
    assert vertices[0] == ord("d")
    count, encoding, size = struct.unpack_from("<III", vertices, 1)
    vertex_data = vertices[13 : 13 + size]
    if encoding:
        vertex_data = zlib.decompress(vertex_data)
    coordinates = struct.unpack("<" + str(count) + "d", vertex_data)
    faces_raw = int_array(one_child(geometry, b"PolygonVertexIndex").properties[0])
    vertex_count = count // 3
    parent = list(range(vertex_count))

    def find(index):
        while parent[index] != index:
            index = parent[index]
        return index

    faces = []
    face = []
    for raw_index in faces_raw:
        face.append(-raw_index - 1 if raw_index < 0 else raw_index)
        if raw_index < 0:
            faces.append(face)
            for index in face[1:]:
                parent[find(index)] = find(face[0])
            face = []
    assert not face

    components = {}
    for index in range(vertex_count):
        components.setdefault(find(index), []).append(index)
    assert len(components) == 5, "Expected five separate rocks in Ore_Model.fbx"

    def center_distance(indices):
        center_x = sum(coordinates[index * 3] for index in indices) / len(indices)
        center_y = sum(coordinates[index * 3 + 1] for index in indices) / len(indices)
        return center_x * center_x + center_y * center_y

    gold_component = min(components, key=lambda component: center_distance(components[component]))
    material_indices = [int(find(face[0]) == gold_component) for face in faces]
    assert 0 < sum(material_indices) < len(material_indices)

    layer = one_child(geometry, b"LayerElementMaterial")
    one_child(layer, b"MappingInformationType").properties = [fbx_string("ByPolygon")]
    one_child(layer, b"Materials").properties = [fbx_int_array(material_indices)]

    second_material = deepcopy(material)
    second_id = 9000000001
    second_material.properties[0] = b"L" + struct.pack("<q", second_id)
    second_material.properties[1] = fbx_string("Gold ore\x00\x01Material")
    objects.children.insert(objects.children.index(material) + 1, second_material)

    connections = next(node for node in nodes if node.name == b"Connections")
    connection = Node(
        b"C",
        [fbx_string("OO"), b"L" + struct.pack("<q", second_id), model.properties[0]],
        [],
        False,
    )
    connections.children.append(connection)

    definitions = next(node for node in nodes if node.name == b"Definitions")
    for object_type in definitions.children:
        if object_type.name == b"ObjectType" and string(object_type.properties[0]) == "Material":
            material_count = one_child(object_type, b"Count")
            material_count.properties[0] = b"I" + struct.pack("<i", integer(material_count.properties[0]) + 1)
            break
    else:
        raise ValueError("FBX Material definition not found")

    output = bytearray(data[:27])
    for node in nodes:
        output.extend(encode_node(node, len(output)))
    output.extend(footer)
    OUTPUT.write_bytes(output)
    print(f"Created {OUTPUT.relative_to(ROOT)}: {len(faces)} faces, {sum(material_indices)} gold faces, 5 rocks")


if __name__ == "__main__":
    main()
