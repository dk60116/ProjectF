# Rail Lighting Harness

Run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/RailLightingHarness/Run.ps1
```

The harness extracts the production sleeper mesh methods from `Railload.cs`. It checks that each box face has independent vertices, receives a flat normal, and keeps the same top-surface light value when rotated by 90 degrees. It also verifies that rail runtime materials enable the toon shader's opt-in world-up lighting mode while other materials retain the default surface-normal lighting.
