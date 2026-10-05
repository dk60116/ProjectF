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

Burn miners use the same entity, output and scheduler path as electric miners.
Fuel checks read both actual ItemDefinition assets and cover fuel exhaustion,
partial final fuel ticks, synchronous self-wakes, blocked output, depletion,
unloaded fuel/resource storage, binary restoration, legacy native fuel, forced
mode transitions and player/robot energy-port acceptance. Shared `FacilityFuel`
is the actual implementation used by production facilities and miners.

```powershell
pwsh -NoProfile -File Tools/MiningEcsHarness/Run.ps1 -Mass
```

The mass mode runs the actual facility scheduler at 1k/10k/100k burn miners,
normally and forced. It reports boundary creation plus first evaluation, idle
ticks, and synchronized completion ticks separately, with managed allocations.
Entity state arrays, rendering, block/resource/power/animation boundaries are
doubled; these timings do not measure Unity frame time. No entity should run
between its deadlines. A synchronized completion still performs all due work
in one tick, so its peak cost must not be hidden in the idle average.

Mass checks also call the actual progress randomization adapter and service its
new deadlines. The 100k seeded case spreads completion over 600 tick buckets;
the peak after reevaluation is checked separately from the one-time wake of all
targets. Progress-only mutation must preserve fuel, resources and output.
