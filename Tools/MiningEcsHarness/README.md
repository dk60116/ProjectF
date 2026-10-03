# Mining ECS harness

Run from the repository root:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/MiningEcsHarness/Run.ps1
```

The harness runs actual mining production/output sources and extracts production
registration, state-store writes, save snapshots, binary serialization, power
demand and gauge lifecycle methods. Engine, spatial indexing and UI boundaries
are test doubles; it does not launch Unity.

Shared-state checks verify that the store, miner and virtual record use one
installation/I/O DTO graph. Registration still copies caller data, and public
reads/save snapshots remain independent copies. Repeated persistence updates
production fields without cloning or rebuilding placement indices; 100,000
calls are checked for zero managed allocation in the .NET harness.

Only the mining registration path uses shared state. Placement geometry stays
fixed during mining; other installation runtimes retain their existing copy
contract. Unity heap usage, rendering and frame timings require a separate
runtime measurement.
