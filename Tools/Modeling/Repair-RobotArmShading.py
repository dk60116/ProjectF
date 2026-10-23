"""Rebuild RobotArm FBX loop normals in Blender without changing mesh or UVs.

Usage: blender --background --python Repair-RobotArmShading.py -- [--apply]
Without --apply this only reports current and repaired normal diagnostics.
"""

import argparse
import importlib.util
import json
import math
import re
import shutil
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from bpy_extras.io_utils import axis_conversion

sys.path.insert(0, str(Path(__file__).resolve().parent))
from RobotArmNormals import rebuild_normals


ROOT = Path(__file__).resolve().parents[2]
ASSET = ROOT / "FactorioProject/Assets/MapObject/InputOutputModule/Robot arm"
STAGING = ROOT / "ArtSource/RobotArm/Retopology"
OUTPUT = ROOT / "ArtSource/RobotArm/ShadingFix"
BACKUP = OUTPUT / "Backups"
FILES = ("Robot arm_Body.fbx", "Robot arm_Hand.fbx", "Robot arm_Bottom.fbx")
ROTATION = axis_conversion(from_forward="Z", from_up="Y", to_forward="-Y", to_up="Z").to_3x3()

spec = importlib.util.spec_from_file_location("robot_arm_fbx", Path(__file__).with_name("Cleanup-RobotArmTopology.py"))
fbx = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fbx)


def vectors(block, name):
    return [float(value) for value in fbx.array(block, name)]


def diagnostic(mesh, normals):
    negative = 0
    tilted = 0
    minimum = 1.0
    total = 0
    for polygon in mesh.polygons:
        face_normal = polygon.normal
        if polygon.area <= 1e-12:
            continue
        for loop_index in polygon.loop_indices:
            normal = normals[loop_index]
            length = math.sqrt(sum(value * value for value in normal))
            dot = sum(face_normal[i] * normal[i] for i in range(3)) / length if length else 0.0
            negative += dot < 0
            tilted += dot < 0.5
            minimum = min(minimum, dot)
            total += 1
    return {"corners": total, "opposite": negative, "tilted_over_60_degrees": tilted,
            "minimum_dot": round(minimum, 4)}


def rebuild_geometry(block, model):
    xyz = vectors(block, "Vertices")
    source_indices = [int(value) for value in fbx.array(block, "PolygonVertexIndex")]
    source_faces = fbx.faces_from_indices(source_indices, len(xyz) // 3)
    mesh = bpy.data.meshes.new(model)
    mesh.from_pydata([xyz[i:i + 3] for i in range(0, len(xyz), 3)], [],
                     [vertices for vertices, _ in source_faces])
    mesh.update()
    if len(mesh.loops) != len(source_indices):
        raise ValueError(f"Loop count changed in {model}")

    old_normals = vectors(block, "Normals")
    if len(old_normals) != 3 * len(mesh.loops):
        raise ValueError(f"Bad source normals in {model}")
    before = diagnostic(mesh, [old_normals[3 * i:3 * i + 3] for i in range(len(mesh.loops))])

    uv = vectors(block, "UV")
    uv_indices = [int(value) for value in fbx.array(block, "UVIndex")]
    if len(uv_indices) != len(mesh.loops):
        raise ValueError(f"Bad UV indices in {model}")
    layer = mesh.uv_layers.new(name="UV")
    for loop, index in zip(layer.data, uv_indices):
        loop.uv = uv[2 * index:2 * index + 2]

    obj = bpy.data.objects.new(model, mesh)
    bpy.context.collection.objects.link(obj)
    rebuild_normals(obj)
    mesh.calc_tangents(uvmap="UV")
    normals = [tuple(loop.vector) for loop in mesh.corner_normals]
    after = diagnostic(mesh, normals)
    if after["opposite"]:
        raise ValueError(f"Normals remain invalid in {model}: {after}")

    new_normals = []
    tangents = []
    binormals = []
    for loop_index, loop in enumerate(mesh.loops):
        normal = mesh.corner_normals[loop_index].vector
        tangent = loop.tangent
        binormal = normal.cross(tangent) * loop.bitangent_sign
        new_normals.extend(format(value, ".9g") for value in normal)
        tangents.extend(format(value, ".9g") for value in tangent)
        binormals.extend(format(value, ".9g") for value in binormal)

    changed = block
    for name, values in (("Normals", new_normals), ("Tangents", tangents), ("Binormals", binormals)):
        changed = fbx.replace_array(changed, name, values)
    return changed, {"mesh": model, "before": before, "after": after,
                     "vertices": len(mesh.vertices), "faces": len(mesh.polygons)}


def process_file(filename):
    path = ASSET / filename
    staging = STAGING / filename
    if path.read_bytes() != staging.read_bytes():
        raise ValueError(f"Unity and staging FBX differ: {filename}")
    source = path.read_text(encoding="utf-8")
    models = {identifier: re.search(r'"Model::([^"\n]+)"', block)[1]
              for identifier, _, _, block in fbx.sections(source, "Model")}
    connections = {int(a): int(b) for a, b in re.findall(r'C: "OO",(\d+),(\d+)', source)}
    changes = []
    reports = []
    for geometry_id, start, end, block in fbx.sections(source, "Geometry"):
        model = models[connections[geometry_id]]
        if model not in fbx.USED:
            continue
        changed, report = rebuild_geometry(block, model)
        changes.append((start, end, changed))
        reports.append(report)
    updated = source
    for start, end, changed in reversed(changes):
        updated = updated[:start] + changed + updated[end:]
    return path, staging, updated.encode("utf-8"), reports


def sync_staging_blends():
    for filename in FILES:
        source = (ASSET / filename).read_text(encoding="utf-8")
        models = {identifier: re.search(r'"Model::([^"\n]+)"', block)[1]
                  for identifier, _, _, block in fbx.sections(source, "Model")}
        connections = {int(a): int(b) for a, b in re.findall(r'C: "OO",(\d+),(\d+)', source)}
        for geometry_id, _, _, block in fbx.sections(source, "Geometry"):
            model = models[connections[geometry_id]]
            if model not in fbx.USED:
                continue
            blend = STAGING / f"{filename[:-4]}-{model}.blend"
            bpy.ops.wm.open_mainfile(filepath=str(blend))
            obj = bpy.data.objects.get(model)
            if obj is None or obj.type != "MESH":
                raise ValueError(f"Missing staged mesh in {blend}")
            mesh = obj.data
            xyz = vectors(block, "Vertices")
            indices = [int(value) for value in fbx.array(block, "PolygonVertexIndex")]
            normals = vectors(block, "Normals")
            faces = fbx.faces_from_indices(indices, len(xyz) // 3)
            if len(mesh.vertices) != len(xyz) // 3 or len(mesh.loops) != len(indices):
                raise ValueError(f"Staged topology differs in {blend}")
            for index, vertex in enumerate(mesh.vertices):
                expected = ROTATION @ Vector(xyz[3 * index:3 * index + 3]) * 0.01
                if (vertex.co - expected).length > 1e-5:
                    raise ValueError(f"Staged vertex order differs in {blend}")
            for polygon, (vertices, _) in zip(mesh.polygons, faces):
                if tuple(polygon.vertices) != tuple(vertices):
                    raise ValueError(f"Staged face order differs in {blend}")
            mesh.normals_split_custom_set([ROTATION @ Vector(normals[3 * i:3 * i + 3])
                                           for i in range(len(mesh.loops))])
            mesh.update()
            backup = BACKUP / blend.name
            if not backup.exists():
                shutil.copy2(blend, backup)
            bpy.context.preferences.filepaths.save_version = 0
            bpy.ops.wm.save_as_mainfile(filepath=str(blend))
            print(f"STAGED_BLEND_SYNCED {blend}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--sync-blends-only", action="store_true")
    options = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if options.sync_blends_only:
        BACKUP.mkdir(parents=True, exist_ok=True)
        sync_staging_blends()
        return
    bpy.ops.wm.read_factory_settings(use_empty=True)
    results = [process_file(filename) for filename in FILES]
    reports = [report for _, _, _, part_reports in results for report in part_reports]
    print("SHADING_NORMAL_REPORT " + json.dumps(reports))
    if options.apply:
        BACKUP.mkdir(parents=True, exist_ok=True)
        for path, staging, data, _ in results:
            backup = BACKUP / path.name
            if not backup.exists():
                shutil.copy2(path, backup)
            path.write_bytes(data)
            staging.write_bytes(data)
        (OUTPUT / "normal-report.json").write_text(json.dumps(reports, indent=2), encoding="utf-8")
        sync_staging_blends()


if __name__ == "__main__":
    main()
