# Belt publication harness

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/BeltPublishOptimizationHarness/Run.ps1
dotnet run --configuration Release --project Tools/BeltJobsHarness/BeltJobsHarness.csproj
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/RuntimeIoQueryHarness/Run.ps1
```

The publication probe extracts actual native-summary, Block publication,
occupancy-version and render-membership methods. Engine/storage boundaries are
doubled. It checks static/dynamic/empty/deferred membership, stock counts,
real floor ingress and stack event subscribers. A warmed 100,000-block batch
must perform zero legacy lane queries, zero empty-floor initialization and zero
managed allocation. Native item totals and checkpoint invalidation are tested by
the host harness; visual publication no longer maintains duplicate per-block stock
or persistence entries. Tiered compilation is disabled in this probe so .NET JIT
warmup does not enter the allocation measurement.

The belt kernel/host harness runs the actual native simulation and publication
orchestration. It checks deterministic serial/reverse/parallel execution,
same-frame writes, checkpoint restoration, dense/sparse publication ordering,
observer filtering, settled summaries, and index cleanup after topology changes.
It also covers chunk publication coalescing, negative coordinates, camera re-entry,
off-screen subscribers, checkpoint invalidation after capture, native stock conservation
and 100,000 simultaneous changes with zero warmed managed allocation. Only visible
chunks enter managed presentation; empty source cells remain checkpointable.
The IO query probe checks sleeping-module wake coalescing and area registries.

No Unity process is launched. Probe timings are not a before/after Unity FPS
comparison. In the game profiler, compare **Belt Jobs Publish Blocks**, **Belt
Jobs Publish Input Output**, **Conveyor Item Publish Visible Chunks** and publication
peaks using the same benchmark. `PendingVisualBlocks` includes coalesced off-screen
changes, not outstanding simulation work. `LastFrameVisualPublishedBlocks` counts
cells actually reflected into presentation; `LoadedItemCount` is native stock.
