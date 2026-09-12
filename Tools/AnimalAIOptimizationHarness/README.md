# Animal AI optimization and determinism checks

Run `./Tools/AnimalAIOptimizationHarness/Run.ps1` with .NET 9. This does not start Unity or the game. Also run `./Tools/AnimalFeedingHarness/Run.ps1` for food, movement, reservations and save-format regression checks.

## Runtime changes

- Terrain walkability comes from grid occupancy and persisted/generated cell data. Terrain changes, resource removal, installation changes and door changes invalidate the cache; rendering visibility does not. Static objects without a map footprint can explicitly register one using `ChangeAnimalNavigationObstacle`.
- A 32-entry bounded cache stores connected regions and a multi-source nearest-shore search shared by herd members. Roaming selects a reachable member, followed by A*; it does not repeatedly flood the entire area. Equal A* costs use the cell index as their final tie-breaker. Existing routes revalidate after a topology revision.
- Scheduling and behavior/cooldown timers use integer 60 UPS ticks. Near/mid/far decisions use 2/4/8 ticks. Stable identity determines the initial phase. The 2,048-unit work limit is cooperative between complete decisions. A reachable query has the same logical cost with a cold or warm cache, preventing cache warmth from changing which animals run.
- Authoritative positions and targets are stored as integer 1/4096-cell coordinates. Heading is an integer 1/65536 turn. Direction, normalization, random disk targets and movement use integer helpers. Frame interpolation does not feed the simulation pose back into decisions.
- AI obstacle probes use integer circle-versus-occupied-cell checks instead of PhysX queries. Animal separation uses the world's tick snapshot. Overlap escape remains supported. The collision stress fixture registers its walls with the same occupancy system.
- Eating and stand-up completion use simulation ticks derived from immutable clip lengths at configuration, preserving long clips without polling Animator playback progress. Initial distant animation suspension and external rider/leash animation behavior are retained.
- Rebuilding terrain views retains live animals and their simulation IDs. Explicit removal unregisters before destroying a view. Save position/rotation comes from the authoritative simulation pose.
- The global initial/subsequent chunk-loading barrier remains in force, including save restoration and discarding loading-time catch-up debt.

## Coverage and limits

The harness compiles the production world, spatial cache, pathfinder, integer helpers, Needs scheduler and profiler. It extracts the real actor schedule/execution/presentation methods and the tick-manager loading gate. Unity scene objects and the isolated actor's decision body are test doubles. It also reuses the animal animation checks independently of the older whole-renderer harness.

Checks cover incremental indexing against a rebuilt baseline, reverse registration order, hidden views, movement/herds/removal, work-budget fairness, cache invalidation, connected pens, nearest water across a door, cold/warm scheduling equivalence, zero allocations for warm navigation, exact timer boundaries, integer directions/positions, obstacle escape, and identical actor schedule/state at 17/30/144 rendering FPS. The feeding harness exercises actual feeding/navigation methods, item reservations and binary save records.

This is AI determinism hardening, not proof of whole-game multiplayer lockstep. Player/rider/leash commands, food/drop delivery and world-time inputs still come from the existing game systems; those systems need a shared command/tick contract for multiplayer. Some weights, health/Needs quantities and spatial calculations retain floating-point boundaries. Existing save records do not capture every transient AI/navigation/scheduler state. Cross-platform full-world replay and actual Unity gameplay/performance remain unverified.

## Profiler

`AI Detail` timings are nested; do not sum them with parent rows. `AI Render / Animal Presentation` is the render-frame interpolation pass. `Animal Reachable Target`, `Animal Connected Area Build`, `Animal Path AStar` and `Animal Grid Collision` separate the new work. Unused animal PhysX counters have been removed.

`WalkableCacheHits/Misses` and `RegionCacheHits/Builds` show cache reuse. `PathNodes` counts actual expansions once, including nested A*. `PathWorkBudget` and `PathBudgetMaxWorkPerTick` measure deterministic scheduling charges, not actual expansions or milliseconds. `PathBudgetOverruns` reports complete decisions that exceed that soft work limit. Work counters cover the last completed render frame.
