# Installation Render Harness

```powershell
./Tools/InstallationRenderHarness/Run.ps1
```

Runs the actual installation renderer, material resolver and rigid animation
template against engine boundary doubles. No Unity, game, screen or GPU is started.

Covers lifecycle/pooling, load suspension, vehicle movement, shared animation,
native shader fallback, runtime-added parts, material/shader/mesh changes,
property blocks, sprite colors/flips and cached spatial/shadow/layer settings.
Warmed submission and material resolution must allocate zero managed bytes.
Also verifies visible-only traversal with 100000 registered records and a
snapshot-shaped scene of 224 visible models and 1521 single-submesh parts.
Unchanged plain parts make no new shader keyword queries or property-block copies.
Runtime material lists and transforms are still inspected to detect external changes;
the matrix cache reuses cell/winding metadata, not stale transform values.

New profiler rows split `Installation Model Submit` into `Installation Model Gather`,
`Installation Model Build` and `Installation Model Draw`. These are inclusive child
scopes and must not be added to the parent as independent costs.
`MatrixCacheHits`, `MatrixCacheMisses` and `BatchKeyRebuilds` describe the last frame.
Shader/property layout/block skip counters are cumulative over the host lifetime.
Actual native API latency, GPU rendering and FPS still require an in-game snapshot.
