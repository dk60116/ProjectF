"""Close RobotArm's open mesh boundaries in Blender and preserve FBX IDs.

Use --check-assets before --apply-assets. The default checks the loaded .blend;
--preview-blend writes a filled copy of it for visual inspection.
"""

import argparse
import importlib.util
import json
import re
import shutil
import sys
from collections import defaultdict
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector
from bpy_extras.io_utils import axis_conversion

sys.path.insert(0, str(Path(__file__).resolve().parent))
from RobotArmNormals import rebuild_normals

ROOT = Path(__file__).resolve().parents[2]
ASSET = ROOT / "FactorioProject/Assets/MapObject/InputOutputModule/Robot arm"
STAGING = ROOT / "ArtSource/RobotArm/Retopology"
OUTPUT = ROOT / "ArtSource/RobotArm/HoleFill"
BACKUP = OUTPUT / "Backups"
FILES = ("Robot arm_Body.fbx", "Robot arm_Hand.fbx", "Robot arm_Bottom.fbx")
ROTATION = axis_conversion(from_forward="Z", from_up="Y", to_forward="-Y", to_up="Z").to_3x3()

spec = importlib.util.spec_from_file_location("robot_arm_fbx", Path(__file__).with_name("Cleanup-RobotArmTopology.py"))
fbx = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fbx)


def boundary_components(bm):
    edges = {edge for edge in bm.edges if edge.is_boundary}
    adjacency = defaultdict(list)
    for edge in edges:
        for vertex in edge.verts:
            adjacency[vertex].append(edge)
    components = []
    remaining = set(edges)
    while remaining:
        stack = [remaining.pop()]
        component = set(stack)
        while stack:
            for vertex in stack.pop().verts:
                for edge in adjacency[vertex]:
                    if edge in remaining:
                        remaining.remove(edge)
                        component.add(edge)
                        stack.append(edge)
        components.append((component, adjacency))
    return components


def boundary_cycles(edges, adjacency):
    unused = set(edges)
    cycles = []
    vertices = {vertex for edge in edges for vertex in edge.verts}
    branches = [vertex for vertex in vertices if len(adjacency[vertex]) > 2]
    if len(branches) > 1:
        raise ValueError("Multiple branching vertices in a boundary component")
    while unused:
        if branches:
            start = branches[0]
            edge = next(edge for edge in adjacency[start] if edge in unused)
        else:
            edge = next(iter(unused))
            start = edge.verts[0]
        path = [start]
        current = start
        while True:
            unused.remove(edge)
            current = edge.other_vert(current)
            if current == start:
                break
            if current in path:
                raise ValueError("Boundary path intersects itself at a vertex")
            path.append(current)
            candidates = [candidate for candidate in adjacency[current] if candidate in unused]
            if len(candidates) != 1:
                raise ValueError("Open or branching boundary path")
            edge = candidates[0]
        if len(path) < 3:
            raise ValueError("Boundary cycle has fewer than three vertices")
        first_edge = next(edge for edge in edges if set(edge.verts) == {path[0], path[1]})
        adjacent_loop = first_edge.link_loops[0]
        if adjacent_loop.vert == path[0] and adjacent_loop.link_loop_next.vert == path[1]:
            path.reverse()
        cycles.append(path)
    return cycles


def fill_mesh(mesh, apply=False):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    before = sum(edge.is_boundary for edge in bm.edges)
    tiny_welds = 0
    for edges, _ in boundary_components(bm):
        perimeter = sum(edge.calc_length() for edge in edges)
        if perimeter >= 0.003:
            continue
        vertices = {vertex for edge in edges for vertex in edge.verts}
        center = sum((vertex.co for vertex in vertices), Vector()) / len(vertices)
        bmesh.ops.pointmerge(bm, verts=list(vertices), merge_co=center)
        tiny_welds += 1
    bm.normal_update()
    cycles = []
    for edges, adjacency in boundary_components(bm):
        cycles.extend(boundary_cycles(edges, adjacency))
    cap_faces = []
    uv_layer = bm.loops.layers.uv.active
    if uv_layer is None:
        raise ValueError(f"Missing UV layer in {mesh.name}")
    for path in cycles:
        edge = bm.edges.get((path[0], path[1]))
        source_face = edge.link_faces[0]
        sample_uv = sum((loop[uv_layer].uv for loop in source_face.loops), Vector((0.0, 0.0))) / len(source_face.loops)
        center = sum((vertex.co for vertex in path), Vector()) / len(path)
        pole = bm.verts.new(center)
        current_cap = []
        for start, end in zip(path, path[1:] + path[:1]):
            face = bm.faces.new((start, end, pole))
            for loop, offset in zip(face.loops, ((-0.0001, -0.0001), (0.0001, -0.0001), (0, 0.0001))):
                loop[uv_layer].uv = sample_uv + Vector(offset)
            cap_faces.append(face)
            current_cap.append(face)
        bm.normal_update()
        if any(face.calc_area() < 1e-10 for face in current_cap):
            original = next(face for face in edge.link_faces if face not in current_cap)
            pole.co += original.normal * 0.001
    bm.normal_update()
    after = sum(edge.is_boundary for edge in bm.edges)
    report = {
        "mesh": mesh.name,
        "boundary_edges_before": before,
        "boundary_edges_after": after,
        "tiny_welds": tiny_welds,
        "capped_cycles": len(cycles),
        "cap_faces": len(cap_faces),
        "nonmanifold_edges_after": sum(len(edge.link_faces) > 2 for edge in bm.edges),
        "degenerate_faces_after": sum(face.calc_area() < 1e-10 for face in bm.faces),
        "degenerate_cap_edges": [round((face.verts[0].co - face.verts[1].co).length, 9)
                                 for face in cap_faces if face.calc_area() < 1e-10],
        "cap_area_min": min((face.calc_area() for face in cap_faces), default=0),
    }
    if after or report["nonmanifold_edges_after"] or report["degenerate_faces_after"]:
        raise ValueError(f"Invalid hole repair: {report}")
    if apply:
        bm.to_mesh(mesh)
        mesh.update()
    bm.free()
    return report


def source_blocks(source):
    models = {identifier: re.search(r'"Model::([^"\n]+)"', block)[1]
              for identifier, _, _, block in fbx.sections(source, "Model")}
    connections = {int(a): int(b) for a, b in re.findall(r'C: "OO",(\d+),(\d+)', source)}
    for identifier, start, end, block in fbx.sections(source, "Geometry"):
        yield models[connections[identifier]], start, end, block


def make_mesh(block, model, stage_coordinates=False):
    xyz = [float(value) for value in fbx.array(block, "Vertices")]
    indices = fbx.array(block, "PolygonVertexIndex")
    faces = fbx.faces_from_indices(indices, len(xyz) // 3)
    coordinates = [Vector(xyz[i:i + 3]) * 0.01 for i in range(0, len(xyz), 3)]
    if stage_coordinates:
        coordinates = [ROTATION @ point for point in coordinates]
    mesh = bpy.data.meshes.new(model)
    mesh.from_pydata(coordinates, [], [vertices for vertices, _ in faces])
    mesh.update()
    uv = [float(value) for value in fbx.array(block, "UV")]
    uv_indices = [int(value) for value in fbx.array(block, "UVIndex")]
    if len(mesh.loops) != len(uv_indices):
        raise ValueError(f"UV loop count differs in {model}")
    layer = mesh.uv_layers.new(name="UV_Unwrapped" if stage_coordinates else "UV")
    for loop, index in zip(layer.data, uv_indices):
        loop.uv = uv[2 * index:2 * index + 2]
    if stage_coordinates:
        normals = [float(value) for value in fbx.array(block, "Normals")]
        mesh.normals_split_custom_set([ROTATION @ Vector(normals[3 * i:3 * i + 3])
                                       for i in range(len(mesh.loops))])
    return mesh


def encode_mesh(mesh, block):
    mesh.calc_tangents(uvmap="UV")
    fields = {name: [] for name in fbx.CHANGED_FIELDS}
    for vertex in mesh.vertices:
        fields["Vertices"].extend(format(value * 100, ".9g") for value in vertex.co)
    for polygon in mesh.polygons:
        indices = list(polygon.vertices)
        indices[-1] = -indices[-1] - 1
        fields["PolygonVertexIndex"].extend(str(index) for index in indices)
    for index, loop in enumerate(mesh.loops):
        uv = mesh.uv_layers.active.data[index].uv
        fields["UV"].extend(format(value, ".9g") for value in uv)
        fields["UVIndex"].append(str(index))
        normal = mesh.corner_normals[index].vector
        tangent = loop.tangent
        binormal = normal.cross(tangent) * loop.bitangent_sign
        for field, vector in (("Normals", normal), ("Tangents", tangent), ("Binormals", binormal)):
            fields[field].extend(format(value, ".9g") for value in vector)
        fields["NormalsW"].append("1")
        fields["TangentsW"].append("0")
        fields["BinormalsW"].append("0")
    changed = block
    for name in fbx.CHANGED_FIELDS:
        changed = fbx.replace_array(changed, name, fields[name])
    before_structure = block
    after_structure = changed
    for name in fbx.CHANGED_FIELDS:
        pattern = fbx.array_pattern(name)
        before_structure = re.sub(pattern, name, before_structure)
        after_structure = re.sub(pattern, name, after_structure)
    if before_structure != after_structure:
        raise ValueError("FBX non-mesh data changed")
    return changed


def process_file(filename):
    path = ASSET / filename
    staging = STAGING / filename
    if path.read_bytes() != staging.read_bytes():
        raise ValueError(f"Unity and staging FBX differ: {filename}")
    source = path.read_bytes().decode("utf-8")
    changes = []
    reports = []
    for model, start, end, block in source_blocks(source):
        if model not in fbx.USED:
            continue
        mesh = make_mesh(block, model)
        obj = bpy.data.objects.new(model, mesh)
        bpy.context.collection.objects.link(obj)
        report = fill_mesh(mesh, apply=True)
        rebuild_normals(obj)
        changed = encode_mesh(mesh, block)
        changes.append((start, end, changed))
        reports.append(report)
    updated = source
    for start, end, changed in reversed(changes):
        updated = updated[:start] + changed + updated[end:]
    return path, staging, updated.encode("utf-8"), reports


def sync_staging_blends():
    for filename in FILES:
        source = (ASSET / filename).read_bytes().decode("utf-8")
        for model, _, _, block in source_blocks(source):
            if model not in fbx.USED:
                continue
            blend = STAGING / f"{filename[:-4]}-{model}.blend"
            bpy.ops.wm.open_mainfile(filepath=str(blend))
            if bpy.context.object and bpy.context.object.mode != "OBJECT":
                bpy.ops.object.mode_set(mode="OBJECT")
            obj = bpy.data.objects.get(model)
            if obj is None or obj.type != "MESH":
                raise ValueError(f"Missing staged mesh in {blend}")
            old = obj.data
            mesh = make_mesh(block, model, stage_coordinates=True)
            for material in old.materials:
                mesh.materials.append(material)
            obj.data = mesh
            backup = BACKUP / blend.name
            if not backup.exists():
                shutil.copy2(blend, backup)
            bpy.context.preferences.filepaths.save_version = 0
            bpy.ops.wm.save_as_mainfile(filepath=str(blend))
            print(f"STAGED_BLEND_SYNCED {blend}")


def apply_assets():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    results = [process_file(filename) for filename in FILES]
    reports = [report for _, _, _, parts in results for report in parts]
    BACKUP.mkdir(parents=True, exist_ok=True)
    for path, staging, data, _ in results:
        backup = BACKUP / path.name
        if not backup.exists():
            shutil.copy2(path, backup)
        path.write_bytes(data)
        staging.write_bytes(data)
    (OUTPUT / "fill-report.json").write_text(json.dumps(reports, indent=2), encoding="utf-8")
    sync_staging_blends()
    print("HOLE_FILL_APPLIED " + json.dumps(reports))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--preview-blend", type=str)
    parser.add_argument("--check-assets", action="store_true")
    parser.add_argument("--apply-assets", action="store_true")
    options = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if options.check_assets:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        results = [process_file(filename) for filename in FILES]
        print("HOLE_FILL_ASSET_CHECK " + json.dumps([report for _, _, _, parts in results for report in parts]))
        return
    if options.apply_assets:
        apply_assets()
        return
    objects = {obj.data.name: obj for obj in bpy.data.objects if obj.type == "MESH" and not obj.hide_render}
    reports = []
    for obj in objects.values():
        reports.append(fill_mesh(obj.data, apply=bool(options.preview_blend)))
        if options.preview_blend:
            shared = obj.data
            other_users = [other for other in bpy.data.objects if other != obj and other.data == shared]
            if other_users:
                obj.data = shared.copy()
            rebuild_normals(obj)
            for other in other_users:
                other.data = obj.data
            if other_users and shared.users == 0:
                bpy.data.meshes.remove(shared)
    print("HOLE_FILL_PROBE " + json.dumps(reports))
    if options.preview_blend:
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=options.preview_blend)


if __name__ == "__main__":
    main()
