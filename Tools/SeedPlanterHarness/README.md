# SeedPlanter completion regression harness

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/SeedPlanterHarness/Run.ps1
```

Compiles the complete production `SeedPlanter.cs` and extracts the production
`DeterministicSimulationUnits` class into a temporary .NET 9 console project.
Does not launch Unity. The surrounding power, inventory, terrain and rendering
APIs are test doubles; network topology, real animation and save-file serialization
are outside this harness's scope.

Covers completed/nearly completed save restoration, partial supply, real outage
and recovery, seed transfer ordering, legacy progress without a loaded seed,
exactly-once planting and seed return on failed placement.
