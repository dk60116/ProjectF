# Installed Utility Poles — appearance-preserving UV repair

Latest concrete-only correction: see `ConcretePoleSurfaceCleanup.md` and `InstalledUtilityPoles_Clean.blend`. The concrete results below describe the preceding rebake, not the latest cleaned surface. Wood remains at the result described below.

Scope: installed Utility pole and Concrete Utility pole only. Portable meshes, portable textures and item bindings are unchanged. No generated artwork or style replacement is used.

## Changes

- Original UV islands repacked with 2.5% spacing while retaining their internal shape and relative areas.
- Original painted albedo transferred using Blender emission baking, without new lighting/shadows, into 2048px atlases with 24px edge extension. This increases atlas padding/resampling precision; it does not invent new source detail.
- Unity texture limits normalized to 2048, compression quality raised to 100 and wrapping clamped.
- Original ASCII FBX mesh names, IDs, geometry, topology, normals and transforms remain intact. Only UV/UVIndex and tangent/binormal arrays change; texture GUIDs and material bindings remain intact.

## Files

- Runtime assets remain under `Assets/MapObject/Electro/Utility pole/` and `Assets/MapObject/Electro/Concrete Utility pole/`.
- `InstalledUtilityPoles_UVRepaired.blend`: final live Blender scene with original references hidden and repaired models visible, packed textures and both SourceUV/RepairedUV layers. The concrete pole's X offset of 1.5 is presentation-only, not exported to FBX.
- `InstalledUtilityPoles.blend`: preserved pre-repair appearance.
- `UVRepair/Original_*_TB.png`: original painted sources; `UVRepair/Validation.json`: measured validation results.
- `RepairInstalledPoleUV.py`: reproducible live Blender baking/validation/export helpers. Run `bake(name)` on the original scene, then `sample_colors(name)`, `validate(name)` and `export_patch()`. Apply the emitted patch and copy the baked atlases only after reviewing the comparison.
- `python -B Tools/Modeling/CheckPoleUVRepair.py`: read-only FBX regression checks against Git HEAD.

## Verification and limits

Utility pole: 237 vertices / 426 triangles. Concrete pole: 377 vertices / 628 triangles. Geometry and IDs unchanged; nonzero UV triangle areas; no overlaps detected by a 512px interior raster test; atlas border padding exceeds 51px at 2048 resolution.

Compared original and baked RGB at corresponding surface points without lighting. Surface-area-weighted mean channel difference: 0.000895 (wood), 0.006954 (concrete), on a 0–1 scale; see JSON for per-sample maxima and percentiles. Same-light Blender before/after comparison was inspected. Unity reimport/build/in-game validation was not run.

This repairs atlas layout, boundary bleeding and resampling. Existing blur, baked painted seams or stains in the original 512px artwork are intentionally retained rather than repainted. Seven tiny concrete triangles still occupy less than 2px of UV area at 2048; preserving their original island proportions was prioritized over reshaping/repainting the original artwork.
