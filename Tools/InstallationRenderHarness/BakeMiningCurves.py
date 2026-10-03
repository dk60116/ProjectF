"""Refresh the two existing miner curve arrays from their authored .anim files.

No scene/Unity process is started. The Editor baker handles the full asset bake;
this narrow offline migration preserves the original scalar keyframes verbatim.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2] / "FactorioProject/Assets"
SOURCES = {
    "Mining Machine": "Animation/InstallationObject/Machine/Mining machine/Work.anim",
    "Electric mining machine": "Animation/InstallationObject/Machine/Electric Mining machine/Working.anim",
}


def authored_curves(path):
    text = path.read_text(encoding="utf-8-sig")
    section = text.split("  m_EditorCurves:\n", 1)[1].split("  m_EulerEditorCurves:", 1)[0]
    result = "    transformCurves:\n"
    for entry in section.split("  - serializedVersion: 2\n")[1:]:
        if not re.search(r"^    classID: 4$", entry, re.M):
            raise ValueError(f"Non-transform curve in {path}")
        attribute = re.search(r"^    attribute: (.+)$", entry, re.M)[1]
        node = re.search(r"^    path: (.*)$", entry, re.M)[1]
        curve = entry.split("    curve:\n", 1)[1].split("    attribute:", 1)[0]
        result += f"    - path: {node}\n      property: {attribute}\n      curve:\n"
        result += "".join("  " + line + "\n" for line in curve.rstrip().splitlines())
    return result


def run(check=False):
    found = 0
    for asset in (ROOT / "Data/MapObjectArchetypes").glob("*.asset"):
        text = asset.read_text(encoding="utf-8-sig")
        name = re.search(r"^  itemName: (.*)$", text, re.M)[1]
        if name not in SOURCES:
            continue
        expected = authored_curves(ROOT / SOURCES[name])
        base = text.split("    transformCurves:", 1)[0].rstrip() + "\n"
        if check:
            assert text == base + expected, f"Stale curve metadata: {asset}"
        else:
            asset.write_text(base + expected, encoding="utf-8")
        found += 1
    assert found == len(SOURCES), "Missing miner Archetype"
    print(f"{'Verified' if check else 'Baked'} {found} miner curve arrays against original animation assets")


if __name__ == "__main__":
    import sys
    run("--check" in sys.argv)
