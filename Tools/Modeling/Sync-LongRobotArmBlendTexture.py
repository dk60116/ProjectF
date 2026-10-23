"""Pack the cleaned atlas into the current Long Robot arm authoring blend."""

from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parents[2]
TEXTURE = ROOT / "ArtSource/LongRobotArm/Long Robot arm_TB.png"
BLEND = ROOT / "ArtSource/LongRobotArm/Long Robot arm_TopologyClean.blend"

replacement = bpy.data.images.load(str(TEXTURE), check_existing=False)
replacement.pack()
replaced = 0
for material in bpy.data.materials:
    if material.name != "Long Robot arm_Rebaked" or not material.use_nodes:
        continue
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image and node.image.name.startswith("Long Robot arm_TB.png"):
            old_image = node.image
            node.image = replacement
            if old_image.users == 0:
                bpy.data.images.remove(old_image)
            replaced += 1
if replaced != 1:
    raise ValueError(f"Expected one target atlas node, found {replaced}")
for image in list(bpy.data.images):
    if image.users == 0 and image.name.startswith("Long Robot arm_TB.png"):
        bpy.data.images.remove(image)
replacement.name = "Long Robot arm_TB.png"
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
print(f"Packed cleaned Long Robot arm atlas into {BLEND}")
