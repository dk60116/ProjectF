# Animal dropped-food checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/AnimalFeedingHarness/Run.ps1
```

Requires .NET 9; does not launch Unity. Extracts production feeding behavior,
nearest-food search, reachability/navigation-area selection, item consumption,
and hunger/digestion recovery methods. Runs the production AnimalGridPathfinder.
Checks previously untouched animals, retry after an empty search, later drops,
nearest food, arrival gating, failed routes, duplicate consumption, hunger,
rest/death gates, and settling drops. Wall scenarios cover reachable food inside
a closed pen versus closer unreachable food outside, opening an entrance on
retry, detours beyond the herd roaming radius, valid smoothed waypoints, and
preventing diagonal shortcuts through touching wall corners.

MovementChecks additionally runs production movement, avoidance, predictive
probes, waypoint preparation, blocked-route handling, and feeding updates over
multiple ticks. Covers food before a wall, interaction reach, the eating hold,
strong opposing separation, large time steps, blocked approach/retry, and
preservation of reverse escape during fleeing.
Facing checks run the production food-facing gate with a yaw rotation test double:
food behind the animal requires a gradual turn before consumption or animation,
turning holds position and ignores height, each meal consumes one item, the eating
hold preserves facing, and food directly underneath cannot stall the turn gate.
Pen roaming checks run production destination selection and herd-return decisions
with the real grid pathfinder and movement. They cover repeated walks inside a pen
outside the original herd circle, stable local routes during return retries,
returning after an entrance opens, small pens within a large herd circle,
fully blocked pens, and preserving saddled roaming restrictions.

GrowthChecks runs production food-growth accumulation, age updates, needs restore,
and animal binary record read/write. Covers displayed food energy, level rollover,
multiple levels with excess, maturity, custom requirements, version 54 round trips,
and legacy version 52 records. Rendering the growth scale is a test double.
Feeding animation checks run the production animation decision: decorative grazing
and failed extraction cannot play eating, a successful meal updates growth before
the animation starts, and young animals obey the same hunger threshold as adults.
Digestion checks cover hunger increasing by one per second, the exact 50% feeding
boundary, energy-based hunger recovery, independent ten-second meal timers, no
food-free periodic droppings, and saved per-meal deadlines.
Shared-stack checks cover ten simultaneous feeders, reservations lasting through
the eating animation, release to the next feeder, independent stacks, and cleanup
when a feeder dies. Floor storage/rendering and Unity pooling remain test doubles.
Arm delivery checks also compile production Block.AnimalFood with real lists of
test portable objects. They cover settled central-stack deliveries, one-item
removal and storage notification, meal effects, competition between floor and
central piles, disappearing targets, hidden/container storage, non-food items,
simultaneous feeders, and deliveries on the other side of a pen wall.

The original feeding cases use controlled movement outcomes. MovementChecks uses
the real movement functions with synthetic physical obstacles and separation.
Simulation scheduling, terrain walkability (blocked grid cells), Unity world
lookup, transforms/rotation, and floor stack storage remain test doubles.
Unity collider geometry, herd separation calculation, animations, and visual
presentation still need in-game verification. The harness does not launch Unity.
