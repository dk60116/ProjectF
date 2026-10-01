# Steam pipe pressure regression

Underground/Pump regressions check both tunnel ends beside and overlapping Pump
ports in four orientations using data-only pipe records. Upstream tunnels retain
the Pump's 5 L/s supply after a reservoir, while downstream tunnels retain their
distance loss. Remote connections and Pump passes must both remain searchable.

Production phase regressions extract ProductionMachine's pressure override and
query the actual pipe search: idle, Working and drained batches contribute zero;
Outputting retains the configured fractional rate and normal downstream loss.
Recipe UI rate is checked separately from live pressure in the output harness.

Configured producer pressure checks extract the production InputOutputModule pressure method.
Output fluid counts define L/s before any delivery; fractional amounts, per-fluid values,
edited configuration, selected targets, disabled sources, alternative recipes, pipe loss
and the Pump transport cap are checked through the production network search.

Producer output boundaries are tested in all four orientations with a 30 L/s input
and a 2 L/s configured output. Pipes overlapping output ports cannot import input
pressure or export output pressure inward. Downstream pipes apply distance loss,
and Pumps receive the output rate. The production output identity lookup is also
extracted to verify that changing the selected target changes both fluid and pressure.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/SteamPipePressureHarness/Run.ps1` with .NET 9 installed. No Unity process is launched or controlled.

The harness extracts the production pipe BFS, pressure aggregation, output registry lookup, generator pass and directed connection rules, and runtime pipe record query. Fixtures match the generator prefab's input/body/body/tail offsets (-1/0/1/2). Unity placement/rotation APIs, world registries, fluid identity lookup and the boiler's 30 L/s rate are test doubles.

The original regression returns Steam with 0 L/s when a boiler output overlaps the generator anchor, or when generators are densely chained. Those 16 numerical checks failed before the fix. Checks cover all four orientations, 1/3 generators, distance loss, disabled sources, ordinary adjacent connections, reversed sources, and runtime pipe records whose connector directions differ from their shared prefab.

This verifies source reachability and the resulting pressure value, not just the presence of a method call. Live save loading, real fluid temperatures, and the visible HUD still require an in-game check.

Pump/tank regression cases also extract the production tank connection cache,
direction/compatibility rules and Pump object-info query. They cover empty,
same-fluid and foreign-fluid tanks, endpoint overlap, two pumps sharing an endpoint,
closed sides, and all four orientations. World registries and fluid storage values
are fixtures; actual vehicle unloading and Unity placement transactions are not run.

Pump pressure regressions cover native 1 L/s production remaining at 1 L/s,
stored fluid enabling the default 5 L/s pump pressure, depletion stopping output
at the reservoir boundary, and editable/zero Pump pressure. The pipe HUD shows fluid and
actual supply only. Shared-volume budget checks exercise push/pull in one tick,
staggered consumers, and bounded idle credit using simulation ticks. The harness
extracts the production Pump rate/budget methods; it does not run Unity UI.

Corner-on-Pump regression: a corner pipe occupies the Pump endpoint, with a
stored-fluid tank connected through the corner's lateral leg. Runtime pipe records
provide the installed connector directions. All four rotations and both turn
directions verify the selected corner, Pump info query, and opposite outlet.
Previously the endpoint's axial direction replaced the corner's connectors,
producing Fluid: None and zero supply while the Pump was connected.

Directional reservoir regressions extract the production tank tick and source selection
alongside the connection search. All four orientations verify that a lower-fill inlet
tank supplies a fuller outlet tank, transfer never exceeds stock, empty tanks stop,
refill resumes at 5 L/s, downstream fluid cannot backflow or pressurize the inlet,
and stock increases wake downstream receivers without duplicate subscriptions.
Storage debit/credit operations are fixtures; separate PumpFluidConservationHarness
checks cover the production refund path. The corner's upstream rate remains at the
native source rate (with pipe loss); only the Pump outlet receives the stored-fluid boost.

Direct machine-input regressions extract InputOutputModule's production input-pressure
query and run it against the production pipe-network search. With no PipeRuntimeRecord
installed, a Pump overlapping or adjacent to the input supplies its configured rate;
wrong-facing ports, foreign fluid, empty reservoirs and unboosted native production
are checked in all four rotations. Pump body overlap and forward delivery coordinates
are exercised separately by PumpInputPlacementHarness using the production geometry.
