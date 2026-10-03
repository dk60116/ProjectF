# Portable output harness

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/PortableOutputHarness/Run.ps1
```

Runs actual item storage, movement, culling, deferred output and stack completion
methods with engine/camera boundaries doubled. Movement interpolation is wrapped
with a counter to verify that offscreen updates and timed arrivals do not evaluate
the jump position. Checks include inputs/outputs, delay, view re-entry, flight
bounds crossing the view, layer/culling settings, UI targets, cancellation and
exactly-once completion. World items with individual presentation also use
offscreen timed transfers; only Canvas targets retain the UI exemption.

Repeated and simultaneous 100,000-item transfers check zero recurring managed
allocation after warming. Deferred output checks preserve real stock counts,
capacity, save state and cold offscreen emission without item wrappers.

This harness does not launch Unity or measure actual game FPS.
