# Pump texture style alignment

## Scope

Changed only `FactorioProject/Assets/MapObject/Fluid/Pump/Pump_TB.png` among runtime assets.
Existing boiler and water-pump textures are the style references: simple broad color
fields and restrained shading instead of photographic pitting and bright worn rims.
Pump retains gray/charcoal metal and ochre brass assignments. Geometry, UVs, material,
texture GUID/import settings, prefab, icon, and gameplay scripts are unchanged.

The original texture is backed up here as `Pump_TB_Before.png`.
The generated replacement is 1254 x 1254 (requested 2048 x 2048; tool-selected output).
Unity retains the existing NPOT import handling. Normalized mesh UVs are unchanged.

## Validation

- Parsed the actual ASCII Pump FBX: 1,882 source vertices, 3,186 triangles.
- Rendered actual geometry and UVs against both textures using the same offline renderer.
- Preview images: `../Previews/Pump_StyleBefore.png`, `../Previews/Pump_StyleAfter.png`.
- Sampled 12,744 triangle-interior locations: near-black fraction 2.61% before, 0% after
  (max RGB < 8). This checks sampled interior coverage, not a guarantee of pixel-exact
  atlas boundaries or mipmap seam behavior.
- Visually inspected the offline mesh preview: cleaner broad surfaces, simple brass,
  no obvious black missing patches. Unity lighting and distant mipmaps were not tested.
- Preview renderer expands polygon corners; its vertex label counts render corners,
  not source vertices. It is not a Unity screenshot.

Reproduce previews from the workspace root:

```powershell
python Tools/Modeling/PreviewPumpStyle.py Tools/Modeling/PumpStyle/Pump_TB_Before.png --name Pump_StyleBefore
python Tools/Modeling/PreviewPumpStyle.py FactorioProject/Assets/MapObject/Fluid/Pump/Pump_TB.png --name Pump_StyleAfter
```

## Generation

Used the imagegen skill, built-in image generation/edit mode (not CLI).
Edit target: original Pump_TB.png.
Style-only references: Boiler/Boiler_TB.png and Water pump/Water pump_TB.png.
Exact final prompt is in `Prompt.txt`.
