# Monster Texture Brightness

`Adjust.ps1` applies a deterministic, hue-preserving brightness curve to the 16
Level 1 Monster Pack texture images. It does not edit materials, import metadata,
UV layouts, image dimensions, or alpha values.

The default curve maps the maximum RGB channel to `pow(value / 255, 0.65)` and
scales all three channels equally. Black stays black and white stays white.
PNG output is lossless. JPG output uses quality 100, but JPEG re-encoding is
still lossy. Unity texture GUIDs remain unchanged.

Run with PowerShell 7 on Windows:

```powershell
./Tools/MonsterTextureBrightness/Adjust.ps1
./Tools/MonsterTextureBrightness/Adjust.ps1 -Apply
```

The first command generates previews without changing project textures. Add
`-Exponent` to adjust the curve; smaller values brighten midtones more.

Originals, corrected images, a comparison sheet and verification hashes are
stored outside Unity and Git, by default in:

`$HOME/.codex/asset-backups/ProjectF/monster-texture-brightness`

Every run starts from the backed-up originals, so correction is not cumulative.
The script refuses to overwrite textures or backups changed outside this tool.
Preview generation tracks the last applied hashes separately, so a new preview
does not invalidate the currently applied version.

Checks include all 16 image dimensions, every alpha byte, exact corrected PNG
pixels, increased visible-pixel brightness, copied output hashes and unchanged
material/import metadata hashes. No Unity Editor or game process is launched.
