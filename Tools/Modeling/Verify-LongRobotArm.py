"""Read-only verification of the Long Robot arm FBX/atlas authoring result."""

import importlib.util
import json
import re
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from LongRobotArmConfig import ATLAS_PARTS


ROOT = Path(__file__).resolve().parents[2]
ASSET = ROOT / "FactorioProject/Assets/MapObject/InputOutputModule/Long Robot arm"
OUTPUT = ROOT / "ArtSource/LongRobotArm"
FBX_NAME = "Long Robot arm_Split_Split.fbx"
TEXTURE_NAME = "Long Robot arm_TB.png"
SKIP = {"Group_07_FFFFFF"}

spec = importlib.util.spec_from_file_location(
    "robot_arm_fbx", Path(__file__).with_name("Cleanup-RobotArmTopology.py"))
fbx = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fbx)


def blocks(source):
    models = {identifier: re.search(r'"Model::([^"\n]+)"', block)[1]
              for identifier, _, _, block in fbx.sections(source, "Model")}
    links = {int(a): int(b) for a, b in re.findall(r'C: "OO",(\d+),(\d+)', source)}
    return {models[links[identifier]]: block
            for identifier, _, _, block in fbx.sections(source, "Geometry")}


def masked(source):
    for field in fbx.CHANGED_FIELDS:
        source = re.sub(fbx.array_pattern(field), field, source)
    return source


def normalized_text(source):
    return "\n".join(line.rstrip() for line in source.split("\n"))


def inspect_mesh(name, block, cell_index):
    vertices = fbx.array(block, "Vertices")
    faces = fbx.faces_from_indices(fbx.array(block, "PolygonVertexIndex"), len(vertices) // 3)
    loops = sum(len(face) for face, _ in faces)
    uv = [float(value) for value in fbx.array(block, "UV")]
    uv_indices = [int(value) for value in fbx.array(block, "UVIndex")]
    assert len(uv) % 2 == 0 and len(uv_indices) == loops
    assert len(fbx.array(block, "Normals")) == loops * 3
    assert all(0 <= index < len(uv) // 2 for index in uv_indices)
    assert all(-1e-5 <= value <= 1.00001 for value in uv)
    column, row_from_bottom = cell_index % 3, 2 - cell_index // 3
    assert all(column / 3 - 1e-5 <= uv[2 * index] <= (column + 1) / 3 + 1e-5
               and row_from_bottom / 3 - 1e-5 <= uv[2 * index + 1] <=
               (row_from_bottom + 1) / 3 + 1e-5 for index in uv_indices), name
    counts = Counter(tuple(sorted((face[index], face[(index + 1) % len(face)])))
                     for face, _ in faces for index in range(len(face)))
    boundary = sum(count == 1 for count in counts.values())
    branching = sum(count > 2 for count in counts.values())
    assert boundary == 0 and branching == 0, (name, boundary, branching)
    return {"mesh": name, "vertices": len(vertices) // 3,
            "faces": len(faces), "quads": sum(len(face) == 4 for face, _ in faces),
            "boundary_edges": boundary, "nonmanifold_edges": branching}


def main():
    game_fbx = (ASSET / FBX_NAME).read_bytes()
    assert game_fbx == (OUTPUT / FBX_NAME).read_bytes(), "Game and staging FBX differ"
    assert (ASSET / TEXTURE_NAME).read_bytes() == (OUTPUT / TEXTURE_NAME).read_bytes(), \
        "Game and staging texture differ"
    source = (ASSET / FBX_NAME).read_text(encoding="utf-8-sig")
    original = (OUTPUT / "Backups" / FBX_NAME).read_text(encoding="utf-8-sig")
    assert normalized_text(masked(source)) == normalized_text(masked(original)), \
        "FBX IDs, hierarchy or transforms changed"
    original_blocks = blocks(original)
    changed_blocks = blocks(source)
    assert original_blocks.keys() == changed_blocks.keys()
    assert all(normalized_text(original_blocks[name]) == normalized_text(changed_blocks[name])
               for name in SKIP)
    cells = {model: index for index, (_, model, _) in enumerate(ATLAS_PARTS)}
    results = [inspect_mesh(name, block, cells[name])
               for name, block in changed_blocks.items() if name not in SKIP]
    assert len(results) == 9
    layout = json.loads((OUTPUT / "atlas-layout.json").read_text(encoding="utf-8"))
    assert [(cell["part"], cell["mesh"], cell["column"], cell["row_from_top"])
            for cell in layout["cells"]] == [
                (part, model, index % 3, index // 3)
                for index, (part, model, _) in enumerate(ATLAS_PARTS)]
    assert (OUTPUT / "atlas-guide.svg").is_file()
    texture = (ASSET / TEXTURE_NAME).read_bytes()
    assert texture[:8] == b"\x89PNG\r\n\x1a\n"
    assert (int.from_bytes(texture[16:20], "big"), int.from_bytes(texture[20:24], "big")) == (2048, 2048)
    texture_meta = (ASSET / (TEXTURE_NAME + ".meta")).read_text(encoding="utf-8")
    assert re.search(r"buildTarget: DefaultTexturePlatform\s+maxTextureSize: 2048", texture_meta)
    assert (OUTPUT / "Long Robot arm_TopologyClean.blend").stat().st_size > 100_000
    print(json.dumps({"meshes": results, "texture_bytes": len(texture)}, indent=2))


if __name__ == "__main__":
    main()
