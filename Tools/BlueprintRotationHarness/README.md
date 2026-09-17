# Blueprint rotation regression

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/BlueprintRotationHarness/Run.ps1` from the repository root.
Add `-BeforeFix` to run against the original HEAD code and reproduce the lost anchor.

The harness extracts the production SteamGenerator rotation call and normal-rotation
fallback, the complete pipe-flip resolver and footprint comparison, and the final
anchored position application/resolver. Terrain lookup and Unity types are managed
doubles; it does not launch or control Unity or verify rendered pixels.

Cases cover repeated ordinary rotations at positive/negative nonzero coordinates,
other installation types passing through the optional resolver, successful 180-degree
pipe flips in four directions, and failed flips with unloaded candidate blocks.
Body position and IO anchor must agree after each operation. A failed optional
resolver must not clear the caller's valid block and move the body to world origin.
