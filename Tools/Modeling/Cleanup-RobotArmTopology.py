"""Remove detached, at-most-four-vertex scraps from the six used RobotArm FBX meshes.

Keeps source FBX object IDs, pivots, materials, UVs, normals and the intended
open boundaries. Also repairs the stale per-corner NormalsW array written by
the earlier retopology export. The Unity Assets FBX and its ArtSource staging
copy are kept byte-identical.
"""

import argparse
import json
import re
import shutil
from collections import Counter
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ASSET = ROOT / "FactorioProject/Assets/MapObject/InputOutputModule/Robot arm"
STAGING = ROOT / "ArtSource/RobotArm/Retopology"
OUTPUT = ROOT / "ArtSource/RobotArm/TopologyCleanup"
BACKUP = OUTPUT / "Backups"
FILES = ("Robot arm_Body.fbx", "Robot arm_Hand.fbx", "Robot arm_Bottom.fbx")
USED = {
    "tripo_part_15", "tripo_part_17", "tripo_part_new_0",
    "tripo_part_1", "tripo_part_7", "Robot_Arm_Bottom",
}
CORNER_VECTORS = ("Normals", "Tangents", "Binormals")
CORNER_SCALARS = ("TangentsW", "BinormalsW")
CHANGED_FIELDS = (
    "Vertices", "PolygonVertexIndex", "UV", "UVIndex", "NormalsW",
    *CORNER_VECTORS, *CORNER_SCALARS,
)


def sections(source, kind):
    for match in re.finditer(r"^\t" + kind + r': (\d+), "([^"\n]+)", "([^"\n]+)" \{', source, re.M):
        end = match.end()
        depth = 1
        while depth:
            depth += (source[end] == "{") - (source[end] == "}")
            end += 1
        yield int(match[1]), match.start(), end, source[match.start():end]


def array_pattern(name):
    return r"\b" + name + r": \*(\d+)\s*\{\s*a:\s*([^}]+)\}"


def array(block, name):
    match = re.search(array_pattern(name), block)
    if not match:
        raise ValueError(f"Missing {name}")
    values = [part.strip() for part in match[2].split(",") if part.strip()]
    if len(values) != int(match[1]):
        raise ValueError(f"Wrong {name} length")
    return values


def replace_array(block, name, values):
    replacement = f"{name}: *{len(values)} {{\n\t\t\t\ta: {','.join(values)}\n\t\t\t}}"
    changed, count = re.subn(array_pattern(name), lambda _: replacement, block, count=1)
    if count != 1:
        raise ValueError(f"Cannot replace {name}")
    return changed


def faces_from_indices(indices, vertex_count):
    faces = []
    vertices = []
    loops = []
    for loop, raw in enumerate(indices):
        value = int(raw)
        vertex = -value - 1 if value < 0 else value
        if vertex < 0 or vertex >= vertex_count:
            raise ValueError("Polygon vertex outside mesh")
        vertices.append(vertex)
        loops.append(loop)
        if value < 0:
            if len(vertices) < 3:
                raise ValueError("Degenerate polygon")
            faces.append((vertices, loops))
            vertices, loops = [], []
    if vertices:
        raise ValueError("Unterminated polygon")
    return faces


def topology(faces, vertex_count):
    parent = list(range(vertex_count))

    def find(value):
        while parent[value] != value:
            parent[value] = parent[parent[value]]
            value = parent[value]
        return value

    for vertices, _ in faces:
        root = find(vertices[0])
        for vertex in vertices[1:]:
            parent[find(vertex)] = root
    sizes = Counter(find(index) for index in range(vertex_count))
    return find, sizes


def clean_geometry(block, model):
    xyz = array(block, "Vertices")
    indices = array(block, "PolygonVertexIndex")
    if len(xyz) % 3:
        raise ValueError(f"Invalid vertices in {model}")
    vertex_count = len(xyz) // 3
    faces = faces_from_indices(indices, vertex_count)
    find, sizes = topology(faces, vertex_count)
    scrap_roots = {root for root, size in sizes.items() if size <= 4}
    keep_faces = [(vertices, loops) for vertices, loops in faces if find(vertices[0]) not in scrap_roots]
    used_vertices = {vertex for vertices, _ in keep_faces for vertex in vertices}
    kept_vertex_ids = sorted(used_vertices)
    vertex_map = {old: new for new, old in enumerate(kept_vertex_ids)}
    kept_loops = [loop for _, loops in keep_faces for loop in loops]
    removed_faces = len(faces) - len(keep_faces)

    changed = block
    old_loop_count = len(indices)
    if removed_faces:
        changed = replace_array(changed, "Vertices", [value for i in kept_vertex_ids for value in xyz[3 * i:3 * i + 3]])
        new_indices = []
        for vertices, _ in keep_faces:
            mapped = [vertex_map[vertex] for vertex in vertices]
            mapped[-1] = -mapped[-1] - 1
            new_indices.extend(str(value) for value in mapped)
        changed = replace_array(changed, "PolygonVertexIndex", new_indices)

        uv = array(block, "UV")
        uv_indices = [int(value) for value in array(block, "UVIndex")]
        if len(uv) % 2 or len(uv_indices) != old_loop_count:
            raise ValueError(f"Invalid UV data in {model}")
        used_uvs = sorted({uv_indices[loop] for loop in kept_loops})
        if any(index < 0 or 2 * index + 1 >= len(uv) for index in used_uvs):
            raise ValueError(f"UV index outside atlas in {model}")
        uv_map = {old: new for new, old in enumerate(used_uvs)}
        changed = replace_array(changed, "UV", [value for i in used_uvs for value in uv[2 * i:2 * i + 2]])
        changed = replace_array(changed, "UVIndex", [str(uv_map[uv_indices[loop]]) for loop in kept_loops])

        for field in CORNER_VECTORS:
            values = array(block, field)
            if len(values) != 3 * old_loop_count:
                raise ValueError(f"Invalid {field} in {model}")
            changed = replace_array(changed, field, [value for loop in kept_loops for value in values[3 * loop:3 * loop + 3]])
        for field in CORNER_SCALARS:
            values = array(block, field)
            if len(values) != old_loop_count:
                raise ValueError(f"Invalid {field} in {model}")
            changed = replace_array(changed, field, [values[loop] for loop in kept_loops])

    normals_w = array(block, "NormalsW")
    if any(value != "1" for value in normals_w):
        raise ValueError(f"Unexpected NormalsW value in {model}")
    new_loop_count = len(kept_loops) if removed_faces else old_loop_count
    if len(normals_w) != new_loop_count or removed_faces:
        changed = replace_array(changed, "NormalsW", ["1"] * new_loop_count)

    if changed != block:
        before = block
        after = changed
        for field in CHANGED_FIELDS:
            pattern = array_pattern(field)
            before = re.sub(pattern, field, before)
            after = re.sub(pattern, field, after)
        if before != after:
            raise ValueError(f"Non-mesh FBX data changed in {model}")

    cleaned_xyz = array(changed, "Vertices")
    cleaned_indices = array(changed, "PolygonVertexIndex")
    cleaned_faces = faces_from_indices(cleaned_indices, len(cleaned_xyz) // 3)
    _, new_sizes = topology(cleaned_faces, len(cleaned_xyz) // 3)
    if any(size <= 4 for size in new_sizes.values()):
        raise ValueError(f"Detached scrap remains in {model}")
    if len(array(changed, "Normals")) != 3 * len(cleaned_indices) or len(array(changed, "UVIndex")) != len(cleaned_indices):
        raise ValueError(f"Corner data mismatch in {model}")
    if len(array(changed, "NormalsW")) != len(cleaned_indices):
        raise ValueError(f"NormalsW mismatch in {model}")

    return changed, {
        "mesh": model,
        "scrap_components_removed": len(scrap_roots),
        "faces_removed": removed_faces,
        "vertices_removed": vertex_count - len(cleaned_xyz) // 3,
        "faces_after": len(cleaned_faces),
        "vertices_after": len(cleaned_xyz) // 3,
        "components_after": len(new_sizes),
        "normals_w_repaired": len(normals_w) != new_loop_count,
    }


def process_file(filename):
    path = ASSET / filename
    staging = STAGING / filename
    if path.read_bytes() != staging.read_bytes():
        raise ValueError(f"Source and staging differ: {filename}")
    with path.open("r", encoding="utf-8", newline="") as handle:
        source = handle.read()
    models = {identifier: re.search(r'"Model::([^"\n]+)"', block)[1]
              for identifier, _, _, block in sections(source, "Model")}
    connections = {int(a): int(b) for a, b in re.findall(r'C: "OO",(\d+),(\d+)', source)}
    changes = []
    reports = []
    for geometry_id, start, end, block in sections(source, "Geometry"):
        model = models[connections[geometry_id]]
        if model not in USED:
            continue
        changed, report = clean_geometry(block, model)
        changes.append((start, end, changed))
        reports.append(report)
    updated = source
    for start, end, changed in reversed(changes):
        updated = updated[:start] + changed + updated[end:]
    return path, staging, updated.encode("utf-8"), reports


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    results = [process_file(filename) for filename in FILES]
    report = [entry for _, _, _, entries in results for entry in entries]
    if len(report) != 6:
        raise ValueError(f"Expected six used meshes, got {len(report)}")
    print(json.dumps(report, indent=2))
    if args.dry_run:
        return
    BACKUP.mkdir(parents=True, exist_ok=True)
    for path, staging, content, _ in results:
        if path.read_bytes() != content:
            backup = BACKUP / path.name
            if not backup.exists():
                shutil.copy2(path, backup)
            path.write_bytes(content)
            staging.write_bytes(content)
    (OUTPUT / "cleanup-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("TOPOLOGY_CLEANUP_OK")


if __name__ == "__main__":
    main()
