# Rail Lighting Harness

Run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/RailLightingHarness/Run.ps1
```

The harness extracts the production sleeper mesh methods from `Railload.cs`. It checks that each box face has independent vertices, receives a flat normal, and keeps the same top-surface light value when rotated by 90 degrees. It also verifies that rail runtime materials enable the toon shader's opt-in world-up lighting mode while other materials retain the default surface-normal lighting.

Production material creation and caching are executed against lightweight Unity boundaries. Both rail and sleeper materials must enable GPU instancing for ECS batch submission, preserve their lighting settings, and reuse cached instances with ToonCharacter, URP/Lit, and Standard shader selection. This catches the data-only rail rendering regression without launching Unity; actual GPU output is not covered.

Blueprint checks execute the production preview refresh and combined geometry methods. They cover straight and bent paths, connected endpoints, duplicate points, short paths, customized prefab sleeper dimensions, valid/blocked tint, hiding incomplete previews, 32-bit indices for long previews, and zero managed allocation in warmed geometry generation. Native and ECS rails must share the same geometry helper. Mesh upload and renderer lifetime use test doubles; actual GPU output still needs in-game verification.
