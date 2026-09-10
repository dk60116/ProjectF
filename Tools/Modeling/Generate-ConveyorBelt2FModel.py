#!/usr/bin/env python3
"""Generate the native Unity body/top meshes and clean prefab hierarchy for Conveyor belt 2F."""

from __future__ import annotations

import argparse
import math
import pathlib
import re
import struct
from dataclasses import dataclass
from typing import Iterable, Sequence


ROOT = pathlib.Path(__file__).resolve().parents[2]
ASSET_DIR = ROOT / "FactorioProject/Assets/MapObject/Belt/Conveyor belt 2F"
SOURCE_DIR = ROOT / "ArtSource/ConveyorBelt2F"
PREFAB_PATH = ASSET_DIR / "Conveyor belt 2F.prefab"

BODY_ASSET = ASSET_DIR / "Conveyor belt 2F Body.asset"
TOP_ASSET = ASSET_DIR / "Conveyor belt 2F Top.asset"
BODY_GUID = "709d613955da4ff5983fb30b59a682c7"
TOP_GUID = "535926f062f241ceaa0b37a891b7af67"

OLD_BODY_GO = 8427903738572836150
OLD_BODY_TRANSFORM = 7197264827721102774
OLD_TOP_RENDERER = 6697960940025018464
ROOT_TRANSFORM = 8553229865070038503
ROOT_SOURCE_TRANSFORM = 2705861293071220046

BODY_GO = 900120000000000001
BODY_TRANSFORM = 900120000000000002
BODY_FILTER = 900120000000000003
BODY_RENDERER = 900120000000000004
TOP_GO = 900120000000000005
TOP_TRANSFORM = 900120000000000006
TOP_FILTER = 900120000000000007
TOP_RENDERER = 900120000000000008
GENERATED_IDS = {
    BODY_GO, BODY_TRANSFORM, BODY_FILTER, BODY_RENDERER,
    TOP_GO, TOP_TRANSFORM, TOP_FILTER, TOP_RENDERER,
}

BODY_MATERIAL_GUID = "a248d1e072f169244a16f7003f971690"
TOP_MATERIAL_GUID = "b38abcf8d9328ce4faa869bd293e2b1b"

PATH_STATIONS = (
    (-1.50, 0.130),
    (-0.50, 0.806),
    (0.50, 0.806),
    (1.50, 0.130),
)


Vec3 = tuple[float, float, float]


def sub(a: Vec3, b: Vec3) -> Vec3:
    return a[0] - b[0], a[1] - b[1], a[2] - b[2]


def cross(a: Vec3, b: Vec3) -> Vec3:
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def dot(a: Vec3, b: Vec3) -> float:
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def normalized(v: Vec3) -> Vec3:
    length = math.sqrt(dot(v, v))
    if length <= 1e-9:
        raise ValueError("zero-length vector")
    return v[0] / length, v[1] / length, v[2] / length


@dataclass(frozen=True)
class Vertex:
    position: Vec3
    normal: Vec3
    uv: Vec2


class Mesh:
    def __init__(self, name: str) -> None:
        self.name = name
        self.vertices: list[Vertex] = []
        self.indices: list[int] = []

    def quad(
        self,
        points: Sequence[Vec3],
        outward: Vec3,
        uvs: Sequence[Vec2] | None = None,
    ) -> None:
        if len(points) != 4:
            raise ValueError("quad needs four points")
        normal = normalized(cross(sub(points[1], points[0]), sub(points[2], points[0])))
        order = (0, 1, 2, 3)
        if dot(normal, outward) < 0:
            order = (0, 3, 2, 1)
            normal = (-normal[0], -normal[1], -normal[2])
        if uvs is None:
            uvs = ((0, 0), (1, 0), (1, 1), (0, 1))
        start = len(self.vertices)
        for index in order:
            self.vertices.append(Vertex(points[index], normal, uvs[index]))
        self.indices.extend((start, start + 1, start + 2, start, start + 2, start + 3))

    def swept_box(
        self,
        stations: Sequence[tuple[float, float]],
        z_min: float,
        z_max: float,
        bottom_offset: float,
        top_offset: float,
    ) -> None:
        for index in range(len(stations) - 1):
            x0, y0 = stations[index]
            x1, y1 = stations[index + 1]
            top = ((x0, y0 + top_offset, z_min), (x1, y1 + top_offset, z_min),
                   (x1, y1 + top_offset, z_max), (x0, y0 + top_offset, z_max))
            bottom = ((x0, y0 + bottom_offset, z_max), (x1, y1 + bottom_offset, z_max),
                      (x1, y1 + bottom_offset, z_min), (x0, y0 + bottom_offset, z_min))
            slope_normal = normalized((-(y1 - y0), x1 - x0, 0))
            self.quad(top, slope_normal)
            self.quad(bottom, (-slope_normal[0], -slope_normal[1], 0))
            self.quad(((x0, y0 + bottom_offset, z_min), (x1, y1 + bottom_offset, z_min),
                       (x1, y1 + top_offset, z_min), (x0, y0 + top_offset, z_min)), (0, 0, -1))
            self.quad(((x0, y0 + top_offset, z_max), (x1, y1 + top_offset, z_max),
                       (x1, y1 + bottom_offset, z_max), (x0, y0 + bottom_offset, z_max)), (0, 0, 1))
        x0, y0 = stations[0]
        x1, y1 = stations[-1]
        self.quad(((x0, y0 + bottom_offset, z_max), (x0, y0 + bottom_offset, z_min),
                   (x0, y0 + top_offset, z_min), (x0, y0 + top_offset, z_max)), (-1, 0, 0))
        self.quad(((x1, y1 + bottom_offset, z_min), (x1, y1 + bottom_offset, z_max),
                   (x1, y1 + top_offset, z_max), (x1, y1 + top_offset, z_min)), (1, 0, 0))

    def bounds(self) -> tuple[Vec3, Vec3]:
        positions = [vertex.position for vertex in self.vertices]
        minimum = tuple(min(p[axis] for p in positions) for axis in range(3))
        maximum = tuple(max(p[axis] for p in positions) for axis in range(3))
        center = tuple((minimum[axis] + maximum[axis]) * 0.5 for axis in range(3))
        extent = tuple((maximum[axis] - minimum[axis]) * 0.5 for axis in range(3))
        return center, extent

    def validate(self) -> None:
        if not self.vertices or len(self.indices) % 3:
            raise ValueError(f"{self.name}: invalid mesh buffer")
        if len(self.vertices) >= 65535:
            raise ValueError(f"{self.name}: requires 32-bit indices")
        for i in range(0, len(self.indices), 3):
            a, b, c = (self.vertices[self.indices[i + n]].position for n in range(3))
            area2 = dot(cross(sub(b, a), sub(c, a)), cross(sub(b, a), sub(c, a)))
            if area2 <= 1e-12:
                raise ValueError(f"{self.name}: degenerate triangle at {i // 3}")


def build_body() -> Mesh:
    mesh = Mesh("Conveyor belt 2F Body")
    # Three joined six-faced sections: ascending slope, upper center and descending slope.
    # Shared boundaries use identical coordinates and have no internal cap faces.
    mesh.swept_box(PATH_STATIONS, -0.336, 0.336, -0.130, -0.005)
    mesh.validate()
    return mesh


def build_top() -> Mesh:
    # The mesh's local Z is the belt path. The prefab rotates it +90 degrees around Y.
    mesh = Mesh("Conveyor belt 2F Top")
    cumulative = [0.0]
    segment_normals = []
    for index in range(len(PATH_STATIONS) - 1):
        x0, y0 = PATH_STATIONS[index]
        x1, y1 = PATH_STATIONS[index + 1]
        cumulative.append(cumulative[-1] + math.hypot(x1 - x0, y1 - y0))
        segment_normals.append(normalized((0, x1 - x0, -(y1 - y0))))
    total = cumulative[-1]
    for index, (z, y) in enumerate(PATH_STATIONS):
        if index == 0:
            normal = segment_normals[0]
        elif index == len(PATH_STATIONS) - 1:
            normal = segment_normals[-1]
        else:
            before = segment_normals[index - 1]
            after = segment_normals[index]
            normal = normalized((before[0] + after[0], before[1] + after[1], before[2] + after[2]))
        v = cumulative[index] / total
        mesh.vertices.append(Vertex((-0.255, y, z), normal, (0, v)))
        mesh.vertices.append(Vertex((0.255, y, z), normal, (1, v)))
    for index in range(len(PATH_STATIONS) - 1):
        left0 = index * 2
        right0 = left0 + 1
        left1 = left0 + 2
        right1 = left0 + 3
        mesh.indices.extend((left0, left1, right1, left0, right1, right0))
    mesh.validate()
    return mesh


def fmt(value: float) -> str:
    if abs(value) < 5e-8:
        value = 0.0
    return f"{value:.7g}"


def vec_yaml(value: Vec3) -> str:
    return f"{{x: {fmt(value[0])}, y: {fmt(value[1])}, z: {fmt(value[2])}}}"


def mesh_yaml(mesh: Mesh) -> str:
    center, extent = mesh.bounds()
    index_data = b"".join(struct.pack("<H", index) for index in mesh.indices).hex()
    vertex_data = bytearray()
    for vertex in mesh.vertices:
        reference = (0.0, 0.0, 1.0) if abs(vertex.normal[2]) < 0.9 else (1.0, 0.0, 0.0)
        tangent = normalized(cross(reference, vertex.normal))
        vertex_data.extend(struct.pack(
            "<3f3f4f2f",
            *vertex.position,
            *vertex.normal,
            tangent[0], tangent[1], tangent[2], 1.0,
            *vertex.uv,
        ))
    return f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!43 &4300000
Mesh:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {mesh.name}
  serializedVersion: 11
  m_SubMeshes:
  - serializedVersion: 2
    firstByte: 0
    indexCount: {len(mesh.indices)}
    topology: 0
    baseVertex: 0
    firstVertex: 0
    vertexCount: {len(mesh.vertices)}
    localAABB:
      m_Center: {vec_yaml(center)}
      m_Extent: {vec_yaml(extent)}
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
  m_IndexBuffer: {index_data}
  m_VertexData:
    serializedVersion: 3
    m_VertexCount: {len(mesh.vertices)}
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
    m_DataSize: {len(vertex_data)}
    _typelessdata: {vertex_data.hex()}
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
      m_Range: 0
      m_Start: 0
      m_Data: 
      m_BitSize: 0
    m_UVInfo: 0
  m_LocalAABB:
    m_Center: {vec_yaml(center)}
    m_Extent: {vec_yaml(extent)}
  m_MeshUsageFlags: 0
  m_CookingOptions: 30
  m_BakedConvexCollisionMesh: 
  m_BakedTriangleCollisionMesh: 
  m_MeshMetrics[0]: 1
  m_MeshMetrics[1]: 1
  m_MeshOptimizationFlags: -1
  m_StreamData:
    serializedVersion: 2
    offset: 0
    size: 0
    path: 
"""


def meta_yaml(guid: str) -> str:
    return f"""fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 4300000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def write_obj(path: pathlib.Path, mesh: Mesh, top_to_root: bool = False) -> None:
    lines = [f"o {mesh.name.replace(' ', '_')}"]
    for vertex in mesh.vertices:
        x, y, z = vertex.position
        if top_to_root:
            x, z = z, -x
        lines.append(f"v {fmt(x)} {fmt(y)} {fmt(z)}")
    for vertex in mesh.vertices:
        nx, ny, nz = vertex.normal
        if top_to_root:
            nx, nz = nz, -nx
        lines.append(f"vn {fmt(nx)} {fmt(ny)} {fmt(nz)}")
    for vertex in mesh.vertices:
        lines.append(f"vt {fmt(vertex.uv[0])} {fmt(vertex.uv[1])}")
    for i in range(0, len(mesh.indices), 3):
        face = [mesh.indices[i + n] + 1 for n in range(3)]
        lines.append("f " + " ".join(f"{v}/{v}/{v}" for v in face))
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def generated_prefab_docs() -> str:
    def game_object(go: int, transform: int, mesh_filter: int, renderer: int, name: str) -> str:
        return f"""--- !u!1 &{go}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {transform}}}
  - component: {{fileID: {mesh_filter}}}
  - component: {{fileID: {renderer}}}
  m_Layer: 7
  m_Name: {name}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
"""

    def transform(go: int, transform_id: int, rotation: str) -> str:
        return f"""--- !u!4 &{transform_id}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  serializedVersion: 2
  m_LocalRotation: {rotation}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: {ROOT_TRANSFORM}}}
  m_LocalEulerAnglesHint: {{x: 0, y: 90, z: 0}}
"""

    def mesh_filter(go: int, filter_id: int, guid: str) -> str:
        return f"""--- !u!33 &{filter_id}
MeshFilter:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_Mesh: {{fileID: 4300000, guid: {guid}, type: 2}}
"""

    def renderer(go: int, renderer_id: int, material_guid: str, cast_shadows: int) -> str:
        return f"""--- !u!23 &{renderer_id}
MeshRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_Enabled: 1
  m_CastShadows: {cast_shadows}
  m_ReceiveShadows: 1
  m_DynamicOccludee: 1
  m_StaticShadowCaster: 0
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 1
  m_RayTracingMode: 2
  m_RayTraceProcedural: 0
  m_RenderingLayerMask: 1
  m_RendererPriority: 0
  m_Materials:
  - {{fileID: 2100000, guid: {material_guid}, type: 2}}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {{fileID: 0}}
  m_ProbeAnchor: {{fileID: 0}}
  m_LightProbeVolumeOverride: {{fileID: 0}}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_PreserveUVs: 0
  m_IgnoreNormalsForChartDetection: 0
  m_ImportantGI: 0
  m_StitchLightmapSeams: 1
  m_SelectedEditorRenderState: 3
  m_MinimumChartSize: 4
  m_AutoUVMaxDistance: 0.5
  m_AutoUVMaxAngle: 89
  m_LightmapParameters: {{fileID: 0}}
  m_SortingLayerID: 0
  m_SortingLayer: 0
  m_SortingOrder: 0
  m_AdditionalVertexStreams: {{fileID: 0}}
"""

    return "".join((
        game_object(BODY_GO, BODY_TRANSFORM, BODY_FILTER, BODY_RENDERER, "Body"),
        transform(BODY_GO, BODY_TRANSFORM, "{x: 0, y: 0, z: 0, w: 1}"),
        mesh_filter(BODY_GO, BODY_FILTER, BODY_GUID),
        renderer(BODY_GO, BODY_RENDERER, BODY_MATERIAL_GUID, 1),
        game_object(TOP_GO, TOP_TRANSFORM, TOP_FILTER, TOP_RENDERER, "BeltTop"),
        transform(TOP_GO, TOP_TRANSFORM, "{x: 0, y: 0.7071068, z: 0, w: 0.7071068}"),
        mesh_filter(TOP_GO, TOP_FILTER, TOP_GUID),
        renderer(TOP_GO, TOP_RENDERER, TOP_MATERIAL_GUID, 0),
    ))


def split_unity_docs(text: str) -> tuple[str, list[tuple[int, int, str]]]:
    matches = list(re.finditer(r"(?m)^--- !u!(\d+) &(\d+)(?: stripped)?\s*$", text))
    header = text[:matches[0].start()]
    docs = []
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        docs.append((int(match.group(1)), int(match.group(2)), text[match.start():end]))
    return header, docs


def rewrite_prefab() -> None:
    text = PREFAB_PATH.read_text(encoding="utf-8-sig")
    header, docs = split_unity_docs(text)
    by_id = {doc_id: (type_id, body) for type_id, doc_id, body in docs}

    go_for_transform: dict[int, int] = {}
    parent_for_transform: dict[int, int] = {}
    components_by_go: dict[int, set[int]] = {}
    for type_id, doc_id, body in docs:
        game_object = re.search(r"(?m)^  m_GameObject: \{fileID: (\d+)\}", body)
        if game_object:
            go = int(game_object.group(1))
            components_by_go.setdefault(go, set()).add(doc_id)
            if type_id == 4:
                go_for_transform[doc_id] = go
                father = re.search(r"(?m)^  m_Father: \{fileID: (\d+)\}", body)
                if father:
                    parent_for_transform[doc_id] = int(father.group(1))

    removed_transforms = {OLD_BODY_TRANSFORM}
    changed = True
    while changed:
        changed = False
        for transform_id, parent_id in parent_for_transform.items():
            if parent_id in removed_transforms and transform_id not in removed_transforms:
                removed_transforms.add(transform_id)
                changed = True
    removed_gos = {OLD_BODY_GO} | {go_for_transform[t] for t in removed_transforms if t in go_for_transform}
    removed_ids = set(GENERATED_IDS) | removed_transforms | removed_gos
    for go in removed_gos:
        removed_ids.update(components_by_go.get(go, ()))

    removed_prefab_instances = set()
    for type_id, doc_id, body in docs:
        if type_id != 1001:
            continue
        parent = re.search(r"m_TransformParent: \{fileID: (\d+)\}", body)
        if parent and int(parent.group(1)) in removed_transforms:
            removed_prefab_instances.add(doc_id)
    removed_ids.update(removed_prefab_instances)
    for _, doc_id, body in docs:
        prefab_instance = re.search(r"m_PrefabInstance: \{fileID: (\d+)\}", body)
        if prefab_instance and int(prefab_instance.group(1)) in removed_prefab_instances:
            removed_ids.add(doc_id)

    kept = []
    for type_id, doc_id, body in docs:
        if doc_id in removed_ids:
            continue
        if type_id == 1001 and "guid: e3226a70b8693db4f9da19d47abe9153" in body:
            for child_transform in (OLD_BODY_TRANSFORM, BODY_TRANSFORM, TOP_TRANSFORM):
                body = re.sub(
                    rf"    - targetCorrespondingSourceObject: \{{fileID: {ROOT_SOURCE_TRANSFORM}, guid: e3226a70b8693db4f9da19d47abe9153, type: 3\}}\n"
                    rf"      insertIndex: -1\n"
                    rf"      addedObject: \{{fileID: {child_transform}\}}\n",
                    "",
                    body,
                )
            additions = (
                f"    - targetCorrespondingSourceObject: {{fileID: {ROOT_SOURCE_TRANSFORM}, guid: e3226a70b8693db4f9da19d47abe9153, type: 3}}\n"
                f"      insertIndex: -1\n"
                f"      addedObject: {{fileID: {BODY_TRANSFORM}}}\n"
                f"    - targetCorrespondingSourceObject: {{fileID: {ROOT_SOURCE_TRANSFORM}, guid: e3226a70b8693db4f9da19d47abe9153, type: 3}}\n"
                f"      insertIndex: -1\n"
                f"      addedObject: {{fileID: {TOP_TRANSFORM}}}\n"
            )
            body = body.replace("    m_AddedGameObjects:\n", "    m_AddedGameObjects:\n" + additions, 1)
        if type_id == 114 and "objectName: Conveyor belt 2F" in body:
            body = re.sub(r"(?m)^  beltTopRenderer: \{fileID: \d+\}$", f"  beltTopRenderer: {{fileID: {TOP_RENDERER}}}", body)
        kept.append((type_id, doc_id, body))

    root_index = next(i for i, (type_id, _, body) in enumerate(kept)
                      if type_id == 1001 and "guid: e3226a70b8693db4f9da19d47abe9153" in body)
    generated = split_unity_docs(generated_prefab_docs())[1]
    kept[root_index:root_index] = generated
    replacement = PREFAB_PATH.with_suffix(".prefab.generated")
    replacement.write_text(header + "".join(body for _, _, body in kept), encoding="utf-8")
    replacement.replace(PREFAB_PATH)


def render_preview(path: pathlib.Path, meshes: Iterable[tuple[Mesh, tuple[int, int, int]]]) -> None:
    from PIL import Image, ImageDraw

    width, height = 1100, 700
    image = Image.new("RGB", (width, height), (30, 34, 39))
    draw = ImageDraw.Draw(image)
    camera = normalized((4.5, 3.1, -5.2))
    right = normalized(cross((0, 1, 0), camera))
    up = normalized(cross(camera, right))
    light = normalized((-0.4, 1.0, -0.5))
    polygons = []

    def project(point: Vec3) -> tuple[float, float, float]:
        scale = 270.0
        return (
            width * 0.5 + dot(point, right) * scale,
            height * 0.61 - dot(point, up) * scale,
            dot(point, camera),
        )

    for mesh_order, (mesh, base_color) in enumerate(meshes):
        for index in range(0, len(mesh.indices), 3):
            vertices = [mesh.vertices[mesh.indices[index + n]] for n in range(3)]
            points = [project(vertex.position) for vertex in vertices]
            normal = vertices[0].normal
            shade = 0.35 + 0.65 * max(0.0, dot(normal, light))
            color = tuple(min(255, int(channel * shade)) for channel in base_color)
            polygons.append((mesh_order, sum(point[2] for point in points) / 3.0, points, color))
    for _, _, points, color in sorted(polygons):
        xy = [(point[0], point[1]) for point in points]
        draw.polygon(xy, fill=color)
        draw.line(xy + [xy[0]], fill=(18, 20, 23), width=1)
    draw.text((32, 28), "Conveyor belt 2F - generated model preview", fill=(230, 234, 238))
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--preview", type=pathlib.Path)
    parser.add_argument("--no-prefab", action="store_true")
    args = parser.parse_args()

    body = build_body()
    top = build_top()
    ASSET_DIR.mkdir(parents=True, exist_ok=True)
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    BODY_ASSET.write_text(mesh_yaml(body), encoding="utf-8")
    TOP_ASSET.write_text(mesh_yaml(top), encoding="utf-8")
    (BODY_ASSET.with_suffix(BODY_ASSET.suffix + ".meta")).write_text(meta_yaml(BODY_GUID), encoding="utf-8")
    (TOP_ASSET.with_suffix(TOP_ASSET.suffix + ".meta")).write_text(meta_yaml(TOP_GUID), encoding="utf-8")
    write_obj(SOURCE_DIR / "Conveyor belt 2F Body.obj", body)
    write_obj(SOURCE_DIR / "Conveyor belt 2F Top.obj", top, top_to_root=True)
    for legacy in (
        ASSET_DIR / "Conveyor belt 2F Frame.asset",
        ASSET_DIR / "Conveyor belt 2F Frame.asset.meta",
        SOURCE_DIR / "Conveyor belt 2F Frame.obj",
    ):
        if legacy.exists():
            legacy.unlink()
    if not args.no_prefab:
        rewrite_prefab()
    if args.preview:
        preview_top = Mesh(top.name)
        for vertex in top.vertices:
            x, y, z = vertex.position
            nx, ny, nz = vertex.normal
            preview_top.vertices.append(Vertex((z, y, -x), (nz, ny, -nx), vertex.uv))
        preview_top.indices = list(top.indices)
        render_preview(args.preview, ((body, (112, 121, 132)), (preview_top, (196, 139, 55))))
    print(f"body: {len(body.vertices)} vertices, {len(body.indices) // 3} triangles")
    print(f"top: {len(top.vertices)} vertices, {len(top.indices) // 3} triangles")


if __name__ == "__main__":
    main()
