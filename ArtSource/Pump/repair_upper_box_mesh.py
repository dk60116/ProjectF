"""Restore polygons dropped when Unity re-exported the Pump FBX.

The Blender FBX is the topology reference. Its vertices map one-to-one to the
Unity export after a uniform transform, so only absent polygons are added.
Run without arguments to validate; pass --repair to write the Unity FBX.
"""

from collections import Counter, defaultdict
from pathlib import Path
import argparse
import hashlib
import re
import struct
import zlib

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "ArtSource/Pump/Pump_shape_cleanup.fbx"
TARGET = ROOT / "FactorioProject/Assets/MapObject/Fluid/Pump/Pump.fbx"
TEXTURE = TARGET.with_name("Pump_TB.png")
TEXTURE_SHA256 = "ae7c7de3ac175a158ab049f86470f7147cf8c382841bbe9f4842da11aac26789"
# Dark steel islands in the installed 512 px atlas, clear of their bright rims.
PANEL_WINDOWS = {
    2262: (452, 366, 488, 404),
    2372: (452, 378, 488, 388),
    1813: (380, 210, 396, 230),
}


def binary_geometry(path):
    data = path.read_bytes()
    assert data.startswith(b"Kaydara FBX Binary")
    wide = struct.unpack_from("<I", data, 23)[0] >= 7500
    header = 25 if wide else 13
    record = "<QQQB" if wide else "<IIIB"

    def nodes(position, end):
        while position + header <= end:
            offset, count, _, name_size = struct.unpack_from(record, data, position)
            if not offset:
                break
            cursor = position + header
            name = data[cursor:cursor + name_size].decode("utf-8")
            cursor += name_size
            values = []
            for _ in range(count):
                code = chr(data[cursor])
                cursor += 1
                if code in "YIFDLC":
                    fmt = {"Y": "h", "I": "i", "F": "f", "D": "d", "L": "q", "C": "?"}[code]
                    values.append(struct.unpack_from("<" + fmt, data, cursor)[0])
                    cursor += struct.calcsize(fmt)
                elif code in "dfilbc":
                    length, encoding, byte_count = struct.unpack_from("<III", data, cursor)
                    cursor += 12
                    payload = data[cursor:cursor + byte_count]
                    cursor += byte_count
                    if encoding:
                        payload = zlib.decompress(payload)
                    fmt = {"d": "d", "f": "f", "i": "i", "l": "q", "b": "b", "c": "?"}[code]
                    values.append(struct.unpack("<" + str(length) + fmt, payload))
                elif code in "SR":
                    length = struct.unpack_from("<I", data, cursor)[0]
                    cursor += 4
                    values.append(data[cursor:cursor + length])
                    cursor += length
                else:
                    raise ValueError((code, cursor))
            children = list(nodes(cursor, offset - header)) if cursor < offset - header else []
            yield name, values, children
            position = offset

    objects = next(children for name, _, children in nodes(27, len(data)) if name == "Objects")
    geometry = next(children for name, _, children in objects if name == "Geometry")
    arrays = {name: values[0] for name, values, _ in geometry if name in {"Vertices", "PolygonVertexIndex"}}
    uv_layer = next(children for name, _, children in geometry if name == "LayerElementUV")
    normal_layer = next(children for name, _, children in geometry if name == "LayerElementNormal")
    arrays.update({name: values[0] for name, values, _ in uv_layer if name in {"UV", "UVIndex"}})
    arrays.update({name: values[0] for name, values, _ in normal_layer if name in {"Normals", "NormalsIndex"}})
    return arrays


def ascii_array(text, name, cast):
    match = re.search(r"\b" + name + r": \*(\d+)\s*\{\s*a:\s*([^}]+)", text)
    assert match, name
    result = [cast(value) for value in match.group(2).split(",") if value.strip()]
    assert len(result) == int(match.group(1)), name
    return result


def append_array(text, name, values):
    pattern = re.compile(r"(\b" + name + r": \*)(\d+)(\s*\{\s*a:\s*)([^}]*)(\})")
    match = pattern.search(text)
    assert match, name
    body = match.group(4)
    content = body.rstrip()
    suffix = body[len(content):]
    added = ",".join(format(value, ".17g") if isinstance(value, float) else str(value) for value in values)
    new_body = content + ("" if content.endswith(",") else ",") + "\n\t\t\t" + added + suffix
    return text[:match.start()] + match.group(1) + str(int(match.group(2)) + len(values)) + match.group(3) + new_body + match.group(5) + text[match.end():]


def replace_appended_uv(text, values):
    """Replace only the 36 new triangles' UVs; keep the imported atlas intact."""
    pattern = re.compile(r"(\bUV: \*\d+\s*\{\s*a:\s*)([^}]*)(\})")
    match = pattern.search(text)
    assert match
    body = match.group(2)
    content = body.rstrip()
    tail_start = content.rfind("\n\t\t\t")
    assert tail_start >= 0
    old_tail = content[tail_start:].strip().split(",")
    assert len(old_tail) == len(values) == 216
    replacement = "\n\t\t\t" + ",".join(format(float(value), ".17g") for value in values) + body[len(content):]
    return text[:match.start()] + match.group(1) + body[:tail_start] + replacement + match.group(3) + text[match.end():]


def polygons(indices):
    faces = []
    current = []
    for index in indices:
        current.append(index if index >= 0 else -index - 1)
        if index < 0:
            faces.append(tuple(current))
            current = []
    assert not current
    return faces


def boundary_count(faces):
    edges = Counter(tuple(sorted((a, b))) for face in faces for a, b in zip(face, face[1:] + face[:1]))
    return sum(count == 1 for count in edges.values()), sum(count > 2 for count in edges.values())


def panel_uvs(source_face_uv, panel):
    x0, y0, x1, y1 = PANEL_WINDOWS[panel]
    uv = np.asarray(source_face_uv)
    low = uv.min(axis=0)
    span = uv.max(axis=0) - low
    assert min(span) > 1e-7
    normalized = (uv - low) / span
    return np.column_stack(((x0 + normalized[:, 0] * (x1 - x0)) / 512,
                            1 - (y1 - normalized[:, 1] * (y1 - y0)) / 512))


def triangulate(face, points, normals):
    """Find a small-area 3D tessellation for the source's nonplanar n-gon."""
    xyz = points[np.asarray(face)]
    count = len(face)
    best = [[float("inf")] * count for _ in range(count)]
    split = [[None] * count for _ in range(count)]
    for i in range(count - 1):
        best[i][i + 1] = 0.
    for width in range(2, count):
        for i in range(count - width):
            j = i + width
            for k in range(i + 1, j):
                cross = np.cross(xyz[k] - xyz[i], xyz[j] - xyz[i])
                area = np.linalg.norm(cross) / 2
                if area < 1e-7:
                    continue
                expected = normals[i] + normals[k] + normals[j]
                backwards = np.dot(cross, expected) < 0
                perimeter = (np.linalg.norm(xyz[k] - xyz[i]) +
                             np.linalg.norm(xyz[j] - xyz[k]) +
                             np.linalg.norm(xyz[j] - xyz[i]))
                cost = best[i][k] + best[k][j] + area * (4 if backwards else 1) + .01 * perimeter
                if cost < best[i][j]:
                    best[i][j] = cost
                    split[i][j] = k
    assert split[0][count - 1] is not None
    result = []
    def emit(i, j):
        if j == i + 1:
            return
        k = split[i][j]
        result.append((i, k, j))
        emit(i, k)
        emit(k, j)
    emit(0, count - 1)
    assert len(result) == count - 2
    return result


def main(repair):
    original = binary_geometry(SOURCE)
    text = TARGET.read_text(encoding="utf-8")
    source_points = np.asarray(original["Vertices"]).reshape(-1, 3)
    points = np.asarray(ascii_array(text, "Vertices", float)).reshape(-1, 3)
    source_faces = polygons(original["PolygonVertexIndex"])
    faces = polygons(ascii_array(text, "PolygonVertexIndex", int))
    assert len(source_points) == len(points) == 4242
    assert all(len(face) == 3 for face in faces)
    loop_count = 3 * len(faces)
    for name in ("Normals", "Tangents", "Binormals"):
        assert len(ascii_array(text, name, float)) == 3 * loop_count, name
    all_uv = ascii_array(text, "UV", float)
    all_uv_indices = ascii_array(text, "UVIndex", int)
    assert len(all_uv) % 2 == 0 and len(all_uv_indices) == loop_count
    assert all(0 <= index < len(all_uv) // 2 for index in all_uv_indices)
    scale = (points[:, 0].max() - points[:, 0].min()) / (source_points[:, 0].max() - source_points[:, 0].min())
    shift = (points.min(axis=0) + points.max(axis=0) - scale * (source_points.min(axis=0) + source_points.max(axis=0))) / 2
    target_to_source = []
    distances = []
    for start in range(0, len(points), 128):
        squared = ((points[start:start + 128, None, :] - (scale * source_points[None, :, :] + shift)) ** 2).sum(axis=2)
        near = squared.argmin(axis=1)
        target_to_source.extend(near.tolist())
        distances.extend(np.sqrt(squared[np.arange(len(near)), near]).tolist())
    assert len(set(target_to_source)) == len(points) and max(distances) < .01
    source_to_target = {source: target for target, source in enumerate(target_to_source)}

    target_triangles = [set(face) for face in faces]
    touching = defaultdict(set)
    for fi, face in enumerate(faces):
        for vertex in face:
            touching[vertex].add(fi)
    missing = []
    for si, source_face in enumerate(source_faces):
        mapped = tuple(source_to_target[vertex] for vertex in source_face)
        candidates = set().union(*(touching[vertex] for vertex in mapped))
        present = sum(target_triangles[fi] <= set(mapped) for fi in candidates)
        if present != len(mapped) - 2:
            assert present == 0, (si, present)
            missing.append((si, mapped))
    before = boundary_count(faces)
    assert hashlib.sha256(TEXTURE.read_bytes()).hexdigest() == TEXTURE_SHA256, "Pump atlas changed; inspect UV windows"
    source_uv = np.asarray(original["UV"]).reshape(-1, 2)
    source_uv_indices = original["UVIndex"]
    loop_starts = np.cumsum([0] + [len(face) for face in source_faces])
    panels = (2262, 2372, 1813)
    mapped_panel_uv = {}
    for panel in panels:
        face_uv = source_uv[np.asarray(source_uv_indices[loop_starts[panel]:loop_starts[panel + 1]])]
        mapped_panel_uv[panel] = panel_uvs(face_uv, panel)
    if not missing:
        assert before == (0, 0), before
        assert len(faces) == 8404
        assert all(np.linalg.norm(np.cross(points[b] - points[a], points[c] - points[a])) > 1e-7
                   for a, b, c in faces[-36:])
        expected = []
        offset = len(faces) - 36
        for panel in panels:
            source_face = source_faces[panel]
            mapped = {source_to_target[vertex] for vertex in source_face}
            for face in faces[offset:offset + len(source_face) - 2]:
                assert set(face) <= mapped
                for vertex in face:
                    expected.extend(mapped_panel_uv[panel][source_face.index(target_to_source[vertex])])
            offset += len(source_face) - 2
        assert offset == len(faces)
        assert all_uv_indices[-108:] == list(range(len(all_uv) // 2 - 108, len(all_uv) // 2))
        current = np.asarray(all_uv[-216:])
        delta = float(np.max(np.abs(current - expected)))
        if delta > 1e-8:
            if not repair:
                print("Pump FBX mesh is closed; 36 repaired triangles still need atlas UV correction")
                return
            TARGET.write_text(replace_appended_uv(text, expected), encoding="utf-8", newline="")
            print("Corrected 36 Pump triangles to the installed atlas's dark steel texture islands")
            return
        print("Pump FBX complete:", len(points), "vertices,", len(faces), "triangles, no open edges; UVs aligned")
        return
    assert [si for si, _ in missing] == [1813, 2262, 2372], missing
    assert before == (42, 0), before
    print("Missing source polygons:", [(si, len(face)) for si, face in missing])
    if not repair:
        return

    uv = np.asarray(all_uv).reshape(-1, 2)
    source_normals = np.asarray(original["Normals"]).reshape(-1, 3)
    source_normal_indices = original["NormalsIndex"]
    added_indices, added_uv, added_uv_indices = [], [], []
    added_normals, added_tangents, added_binormals = [], [], []
    new_faces = []
    for si, mapped in sorted(missing, key=lambda item: panels.index(item[0])):
        corner_uv = mapped_panel_uv[si]
        corner_normals = [source_normals[source_normal_indices[loop_starts[si] + k]] for k in range(len(mapped))]
        print("Fill", si, "with", len(mapped) - 2, "textured triangles")
        for local_triangle in triangulate(mapped, points, corner_normals):
            triangle = tuple(mapped[k] for k in local_triangle)
            new_faces.append(triangle)
            added_indices.extend((triangle[0], triangle[1], -triangle[2] - 1))
            for k in local_triangle:
                normal = corner_normals[k]
                normal = normal / np.linalg.norm(normal)
                tangent = np.array([0., 1., 0.]) - normal[1] * normal
                tangent /= np.linalg.norm(tangent)
                binormal = np.cross(normal, tangent)
                added_normals.extend(float(value) for value in normal)
                added_tangents.extend(float(value) for value in tangent)
                added_binormals.extend(float(value) for value in binormal)
                added_uv_indices.append(len(uv) + len(added_uv) // 2)
                added_uv.extend(float(value) for value in corner_uv[k])
    assert len(new_faces) == 36 and boundary_count(faces + new_faces) == (0, 0)
    for name, values in (("PolygonVertexIndex", added_indices), ("Normals", added_normals),
                         ("Tangents", added_tangents), ("Binormals", added_binormals),
                         ("UV", added_uv), ("UVIndex", added_uv_indices)):
        text = append_array(text, name, values)
    TARGET.write_text(text, encoding="utf-8", newline="")
    print("Repaired Pump FBX:", len(faces) + len(new_faces), "triangles, no open edges")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--repair", action="store_true")
    main(parser.parse_args().repair)
