"""Save the Long Robot arm in Blender with its exact Unity prefab hierarchy."""

import importlib.util
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from LongRobotArmConfig import ATLAS_PARTS


ROOT = Path(__file__).resolve().parents[2]
ASSET = ROOT / "FactorioProject/Assets/MapObject/InputOutputModule/Long Robot arm"
OUTPUT = ROOT / "ArtSource/LongRobotArm/Long Robot arm_UnityMatched.blend"
FBX = "Long Robot arm_Split_Split.fbx"
TEXTURE = "Long Robot arm_TB.png"
GUID = "27f4b71b0b425704b94ecbe39a42f66b"

MODEL_BY_FILE_ID = {file_id: model for _, model, file_id in ATLAS_PARTS if file_id}

spec = importlib.util.spec_from_file_location(
    "robot_arm_assembly", Path(__file__).with_name("Open-RobotArmAssembly.py"))
assembly = importlib.util.module_from_spec(spec)
spec.loader.exec_module(assembly)
assembly.ASSET = ASSET
assembly.PREFAB = ASSET / "Long Robot arm.prefab"
assembly.OUTPUT = OUTPUT
assembly.MESHES = {(GUID, file_id): (FBX, model, TEXTURE)
                   for file_id, model in MODEL_BY_FILE_ID.items()}
assembly.EXPECTED_MESH_COUNTS = (9, 8)


if __name__ == "__main__":
    if "--original" in sys.argv:
        assembly.ASSET = OUTPUT.parent / "Backups"
        assembly.OUTPUT = OUTPUT.with_name("Long Robot arm_Original.blend")
    assembly.main()
