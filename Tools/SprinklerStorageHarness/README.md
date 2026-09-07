# Sprinkler shared water storage checks

Run from the repository root with .NET 9 installed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/SprinklerStorageHarness/Run.ps1
```

The harness extracts the production sprinkler storage aggregation, spray withdrawal,
status, auto-pull policy, connection traversal/cache, UI formatting, and base fluid withdrawal methods.
It checks shared consumption, insufficient supply without partial loss, multiple tanks,
fluid type filtering, local input fallback, and live storage amounts. Production
update and spray methods also verify immediate proportional consumption/watering,
the configured average rate, partial supply, and unplaced/invalid-world handling.
Range checks verify that each plant receives only its own coordinate's fixed share,
unused water is not pulled from empty or saturated neighboring cells, duplicate
references are supplied once, and plants outside the range receive no water. Empty
or saturated ranges still consume the configured whole-range spray amount.

Unity objects, notifications, and coordinate/connector lookup are managed test doubles.
Traversal checks include serial tanks, pipes beyond tanks, cycles, disconnection,
blocked connector directions, and underground endpoints. They use the production
search and deduplication methods. Actual pipe placement, connector lookup, UI rendering, and save/load execution
require in-game verification. This harness does not launch Unity.
