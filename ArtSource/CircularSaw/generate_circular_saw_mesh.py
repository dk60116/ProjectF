import math
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "FactorioProject/Assets/Items/IronTool/Circular Saw/Circular Saw_P.mesh"

vertices = []
triangles = []


def normalize(value):
    length = math.sqrt(sum(component * component for component in value))
    return tuple(component / length for component in value)


def cross(left, right):
    return (
        left[1] * right[2] - left[2] * right[1],
        left[2] * right[0] - left[0] * right[2],
        left[0] * right[1] - left[1] * right[0],
    )


def subtract(left, right):
    return tuple(left[index] - right[index] for index in range(3))


def add_triangle(first, second, third, outward):
    edge_a = subtract(vertices[second][0], vertices[first][0])
    edge_b = subtract(vertices[third][0], vertices[first][0])
    face_normal = cross(edge_a, edge_b)
    if sum(face_normal[index] * outward[index] for index in range(3)) < 0.0:
        second, third = third, second
    triangles.extend((first, second, third))


def add_vertex(position, normal):
    normal = normalize(normal)
    reference = (0.0, 1.0, 0.0) if abs(normal[1]) < 0.9 else (0.0, 0.0, 1.0)
    tangent = normalize(cross(reference, normal))
    uv = ((position[0] + 0.16) / 0.31, (position[2] + 0.11) / 0.22)
    vertices.append((position, normal, (*tangent, 1.0), uv))
    return len(vertices) - 1


def add_box(minimum, maximum):
    center = tuple((minimum[index] + maximum[index]) * 0.5 for index in range(3))
    corners = (
        (minimum[0], minimum[1], minimum[2]),
        (maximum[0], minimum[1], minimum[2]),
        (maximum[0], minimum[1], maximum[2]),
        (minimum[0], minimum[1], maximum[2]),
        (minimum[0], maximum[1], minimum[2]),
        (maximum[0], maximum[1], minimum[2]),
        (maximum[0], maximum[1], maximum[2]),
        (minimum[0], maximum[1], maximum[2]),
    )
    base = len(vertices)
    for corner in corners:
        add_vertex(corner, subtract(corner, center))

    faces = (
        ((0, 3, 2, 1), (0.0, -1.0, 0.0)),
        ((4, 5, 6, 7), (0.0, 1.0, 0.0)),
        ((0, 4, 7, 3), (-1.0, 0.0, 0.0)),
        ((1, 2, 6, 5), (1.0, 0.0, 0.0)),
        ((0, 1, 5, 4), (0.0, 0.0, -1.0)),
        ((3, 7, 6, 2), (0.0, 0.0, 1.0)),
    )
    for corners_on_face, outward in faces:
        a, b, c, d = (base + corner for corner in corners_on_face)
        add_triangle(a, b, c, outward)
        add_triangle(a, c, d, outward)


def add_cylinder(center_x, center_z, bottom_y, top_y, radii, normal_y=0.32):
    segment_count = len(radii)
    bottom = []
    top = []
    for index, radius in enumerate(radii):
        angle = math.tau * index / segment_count
        radial = (math.cos(angle), 0.0, math.sin(angle))
        bottom.append(add_vertex(
            (center_x + radial[0] * radius, bottom_y, center_z + radial[2] * radius),
            (radial[0], -normal_y, radial[2])))
        top.append(add_vertex(
            (center_x + radial[0] * radius, top_y, center_z + radial[2] * radius),
            (radial[0], normal_y, radial[2])))

    for index in range(segment_count):
        next_index = (index + 1) % segment_count
        outward = normalize((
            math.cos(math.tau * (index + 0.5) / segment_count),
            0.0,
            math.sin(math.tau * (index + 0.5) / segment_count)))
        add_triangle(bottom[index], top[index], top[next_index], outward)
        add_triangle(bottom[index], top[next_index], bottom[next_index], outward)

    for index in range(1, segment_count - 1):
        add_triangle(top[0], top[index], top[index + 1], (0.0, 1.0, 0.0))
        add_triangle(bottom[0], bottom[index + 1], bottom[index], (0.0, -1.0, 0.0))


def add_x_cylinder(minimum_x, maximum_x, center_y, center_z, radius, segment_count):
    left = []
    right = []
    for index in range(segment_count):
        angle = math.tau * index / segment_count
        radial_y = math.cos(angle)
        radial_z = math.sin(angle)
        left.append(add_vertex(
            (minimum_x, center_y + radial_y * radius, center_z + radial_z * radius),
            (-0.28, radial_y, radial_z)))
        right.append(add_vertex(
            (maximum_x, center_y + radial_y * radius, center_z + radial_z * radius),
            (0.28, radial_y, radial_z)))

    for index in range(segment_count):
        next_index = (index + 1) % segment_count
        outward = (0.0,
                   math.cos(math.tau * (index + 0.5) / segment_count),
                   math.sin(math.tau * (index + 0.5) / segment_count))
        add_triangle(left[index], right[next_index], right[index], outward)
        add_triangle(left[index], left[next_index], right[next_index], outward)

    for index in range(1, segment_count - 1):
        add_triangle(left[0], left[index], left[index + 1], (-1.0, 0.0, 0.0))
        add_triangle(right[0], right[index + 1], right[index], (1.0, 0.0, 0.0))


# 16-point alternating radius produces eight readable saw teeth with only 32 vertices.
blade_radii = tuple(0.11 if index % 2 == 0 else 0.098 for index in range(16))
add_cylinder(-0.045, 0.0, 0.0, 0.018, blade_radii)

# Eight-sided motor housing and a three-piece raised handle.
add_x_cylinder(0.005, 0.115, 0.055, 0.0, 0.04, 8)
add_box((0.022, 0.072, -0.058), (0.044, 0.132, -0.026))
add_box((0.083, 0.072, -0.058), (0.105, 0.132, -0.026))
add_box((0.022, 0.119, -0.058), (0.105, 0.149, -0.026))

# A four-sided raised blade bolt. Total mesh vertex count is exactly 80.
add_cylinder(-0.045, 0.0, 0.018, 0.046, (0.022,) * 4, normal_y=0.5)

assert len(vertices) == 80
assert max(triangles) < len(vertices)

index_buffer = b"".join(struct.pack("<H", index) for index in triangles).hex()
vertex_buffer = bytearray()
for position, normal, tangent, uv in vertices:
    vertex_buffer.extend(struct.pack("<3f3f4f2f", *position, *normal, *tangent, *uv))

minimum = tuple(min(vertex[0][axis] for vertex in vertices) for axis in range(3))
maximum = tuple(max(vertex[0][axis] for vertex in vertices) for axis in range(3))
center = tuple((minimum[axis] + maximum[axis]) * 0.5 for axis in range(3))
extent = tuple((maximum[axis] - minimum[axis]) * 0.5 for axis in range(3))


def vector_yaml(value):
    return "{x: %.9g, y: %.9g, z: %.9g}" % value


mesh_yaml = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!43 &4300000
Mesh:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Circular Saw_P
  serializedVersion: 12
  m_SubMeshes:
  - serializedVersion: 2
    firstByte: 0
    indexCount: {len(triangles)}
    topology: 0
    baseVertex: 0
    firstVertex: 0
    vertexCount: {len(vertices)}
    localAABB:
      m_Center: {vector_yaml(center)}
      m_Extent: {vector_yaml(extent)}
  m_Shapes:
    vertices: []
    shapes: []
    channels: []
    fullWeights: []
  m_BindPose: []
  m_BoneNameHashes:
  m_RootBoneNameHash: 0
  m_BonesAABB: []
  m_VariableBoneCountWeights:
    m_Data:
  m_MeshCompression: 0
  m_IsReadable: 0
  m_KeepVertices: 1
  m_KeepIndices: 1
  m_IndexFormat: 0
  m_IndexBuffer: {index_buffer}
  m_VertexData:
    serializedVersion: 3
    m_VertexCount: {len(vertices)}
    m_Channels:
    - stream: 0
      offset: 0
      format: 0
      dimension: 3
    - stream: 0
      offset: 12
      format: 0
      dimension: 3
    - stream: 0
      offset: 24
      format: 0
      dimension: 4
    - stream: 0
      offset: 0
      format: 0
      dimension: 0
    - stream: 0
      offset: 40
      format: 0
      dimension: 2
    - stream: 0
      offset: 0
      format: 0
      dimension: 0
    - stream: 0
      offset: 0
      format: 0
      dimension: 0
    - stream: 0
      offset: 0
      format: 0
      dimension: 0
    - stream: 0
      offset: 0
      format: 0
      dimension: 0
    - stream: 0
      offset: 0
      format: 0
      dimension: 0
    - stream: 0
      offset: 0
      format: 0
      dimension: 0
    - stream: 0
      offset: 0
      format: 0
      dimension: 0
    - stream: 0
      offset: 0
      format: 0
      dimension: 0
    - stream: 0
      offset: 0
      format: 0
      dimension: 0
    m_DataSize: {len(vertex_buffer)}
    _typelessdata: {vertex_buffer.hex()}
  m_CompressedMesh:
    m_Vertices:
      m_NumItems: 0
      m_Range: 0
      m_Start: 0
      m_Data:
      m_BitSize: 0
    m_UV:
      m_NumItems: 0
      m_Range: 0
      m_Start: 0
      m_Data:
      m_BitSize: 0
    m_Normals:
      m_NumItems: 0
      m_Range: 0
      m_Start: 0
      m_Data:
      m_BitSize: 0
    m_Tangents:
      m_NumItems: 0
      m_Range: 0
      m_Start: 0
      m_Data:
      m_BitSize: 0
    m_Weights:
      m_NumItems: 0
      m_Data:
      m_BitSize: 0
    m_NormalSigns:
      m_NumItems: 0
      m_Data:
      m_BitSize: 0
    m_TangentSigns:
      m_NumItems: 0
      m_Data:
      m_BitSize: 0
    m_FloatColors:
      m_NumItems: 0
      m_Range: 0
      m_Start: 0
      m_Data:
      m_BitSize: 0
    m_BoneIndices:
      m_NumItems: 0
      m_Data:
      m_BitSize: 0
    m_Triangles:
      m_NumItems: 0
      m_Data:
      m_BitSize: 0
    m_UVInfo: 0
  m_LocalAABB:
    m_Center: {vector_yaml(center)}
    m_Extent: {vector_yaml(extent)}
  m_MeshUsageFlags: 0
  m_CookingOptions: 30
  m_BakedConvexCollisionMesh:
  m_BakedTriangleCollisionMesh:
  'm_MeshMetrics[0]': 0.31
  'm_MeshMetrics[1]': 1
  m_MeshOptimizationFlags: -1
  m_StreamData:
    serializedVersion: 2
    offset: 0
    size: 0
    path:
  m_MeshLodInfo:
    serializedVersion: 2
    m_LodSelectionCurve:
      serializedVersion: 1
      m_LodSlope: 0
      m_LodBias: 0
    m_NumLevels: 1
    m_SubMeshes:
    - serializedVersion: 2
      m_Levels:
      - serializedVersion: 1
        m_IndexStart: 0
        m_IndexCount: 0
"""

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
OUTPUT.write_text(mesh_yaml, encoding="utf-8", newline="\n")
print(f"Generated {OUTPUT} ({len(vertices)} vertices, {len(triangles) // 3} triangles)")
