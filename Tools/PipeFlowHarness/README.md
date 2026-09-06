# Pipe extraction flow checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/PipeFlowHarness/Run.ps1
```

Requires .NET 9; does not launch Unity. Uses the production rolling output meter,
pump production, standard fluid output, endpoint collection, pipe traversal, and
InfoPanel data query methods. Checks actual accepted liters, blocked output,
ground-item exclusion, one-second expiry, multiple producers, deduplication,
connector direction, underground routing, and panel query caching.

World/registry lookup, storage acceptance, clock, and Unity types are managed test
doubles. In-game connectivity, rendering, and UI layout still need engine verification.
The displayed rate is the connected pipe network's recent incoming output, not
a simulated per-segment flow split (the game transfers fluid directly to storage).
