# Steam pipe pressure regression

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/SteamPipePressureHarness/Run.ps1` with .NET 9 installed. No Unity process is launched or controlled.

The harness extracts the production pipe BFS, pressure aggregation, output registry lookup, generator pass and directed connection rules, and runtime pipe record query. Fixtures match the generator prefab's input/body/body/tail offsets (-1/0/1/2). Unity placement/rotation APIs, world registries, fluid identity lookup and the boiler's 30 L/s rate are test doubles.

The original regression returns Steam with 0 L/s when a boiler output overlaps the generator anchor, or when generators are densely chained. Those 16 numerical checks failed before the fix. Checks cover all four orientations, 1/3 generators, distance loss, disabled sources, ordinary adjacent connections, reversed sources, and runtime pipe records whose connector directions differ from their shared prefab.

This verifies source reachability and the resulting pressure value, not just the presence of a method call. Live save loading, real fluid temperatures, and the visible HUD still require an in-game check.
