# Belt item rendering harness

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/BeltItemRenderingHarness/Run.ps1
dotnet run --configuration Release --project Tools/BeltJobsHarness/BeltJobsHarness.csproj
```

The first harness extracts the production native item append, render-data structs,
batch identity comparison and synchronization methods. It compiles the actual
`BeltItemVisualPath` and `ConveyorItemTransformJobProcessor` files against Unity's
managed vector/matrix types. Scheduling, native allocation and render submission
use explicit doubles; this does not launch Unity or validate Burst machine code/GPU output.

Checks cover:

- One native state read per slot, interpolation only for occupied slots.
- Deferred native positions reaching matrix translations and negative batch cells.
- Matching serial/scheduled transform results and resolved high-belt rotation.
- External arrivals, high-belt/bridge height, outlined/in-flight proxies and debug tint.
- Same render identity retaining its batch entry through 1,000 motion updates.
- Changed item identity, crossed batch cell, vacant slot, missing asset and recovery.
- Zero managed allocation in warmed transform preparation.

`BeltJobsHarness.VisualChecks` also executes the production native state reader,
pending command overlay, path caching and visual progress. It compares line/via/arc/
jump geometry to the prior formulas, checks topology invalidation, and confirms
that rendering changes neither authoritative transport state nor clock.

For an actual player benchmark, compare identical camera/item distributions:
`Conveyor Item Dynamic AppendData`, `ScheduleMatrices`, `CompleteMatrices`,
`SyncMatrix`, key cache hits/misses and matrix rebuilds. `ConveyorItemRender.DynamicNativePathItems`
counts item positions evaluated in the transform kernel; it includes main-thread
execution below the Burst threshold. High-belt surface poses remain CPU-resolved.
Measure frame p95/p99 as well as the averages. Harness timing is not an FPS measurement.
