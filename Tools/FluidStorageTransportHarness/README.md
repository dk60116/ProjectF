# Fluid storage transport harness

Pump/underground regressions use data-only pipe records with paired remote
endpoints. In all four orientations, both inlet and outlet tunnels are checked
beside a Pump and overlapping its port. Production source/output searches must
transfer real fluid, debit/credit storage and reject reverse flow. The original
inlet overlap lost its remote edge and transferred zero. Pipe geometry/world
registration are fixtures; no Unity placement or UI is executed.

Extracts and executes the production output-cache BFS, directed boiler/generator
traversal, storage lookup, output transfer, placement wake handlers, and
`InstallationObject.TryAddFluidLiters`. Assertions check transferred liters AND
the resulting stored liters; receiver caches are not prepopulated by the tests.
Pump port admission, traversal direction and shared pressure budgets also use
the production methods, including end bodies docked onto maker output ports.
Input regressions extract the source-cache BFS, per-port connection cache,
`TryConsumeConnectedFluidInputAtCoordinate` and `InstallationObject.TryConsumeFluidLiters`.
Tank to Pump to maker input is checked at overlapping endpoints, adjacent ports,
docked outlet bodies and through ordinary pipes, in all four orientations.
Checks verify actual tank stock removal, edited Pump rates, a shared per-tick
volume budget and empty reservoirs. Input pressure and full-batch crafting gates
are covered separately by SteamPipePressureHarness and ProductionMachineFluidInputHarness.

Cases cover four cardinal orientations: pump to tank, boiler to generator,
full generator to downstream tank, registered storage without a Block owner,
disconnected pipes, reversed generators, and adding tanks/generators/generic
CanStoreFluid objects after the upstream producer sleeps with an empty cache.
Also covers the slot_01 layout (generator tail overlapping a corner pipe, then
a perpendicular generator), a corner at a boiler output, and rejection of an
overlapping pipe with no connector facing the source, in all four orientations.
Also executes `ProductionMachine.TryCompleteActiveCraft` through an output port
overlapping a Pump inlet body, six interlocked pumps and a downstream tank.
Checks the first delivery, conservation of the crafted batch, complete draining,
and rejection of a maker output facing away from the Pump inlet.
Ordinary pipe cases also cover a straight eight-pipe route and a perpendicular
pipe at the maker output. The production `UpdateActiveCraft` and `ProductionProcess`
run with the MK3 energy budget (100 kW, 3600 kJ): no delivery during the 36 second
craft, first delivery at energy completion, then draining the entire 36 L batch
at recipe Count 1 L/s before permitting the next craft.
Deterministic unit conversion is extracted from production, and the simulation
tick rate is read from `SimulationTickWorld` instead of using fixture constants.
Connection diagnostics compare a lateral pipe chain beside a virtual output
cell (no transfer, completed batch retained), the same lateral route with a
corner pipe occupying the output cell (transfer), and an external-facing chain
without a pipe occupying the output cell (transfer), in all four orientations.
These reproduce the source-level admission rules; they do not establish which
pipe records are present in a player's current scene.
The earlier lookup-only harness was replaced because it could pass while actual
storage transport remained broken.

`CacheChecks.cs` extracts the production endpoint revision, capacity, retention
and receiver selection caches. Native and ECS changes on unrelated networks
preserve all three results in the same tick; related fill/drain invalidates
positive and blocked results immediately. Also checks a non-selected endpoint
becoming the best receiver, topology additions, per-port seed changes, request
size changes, receiver activity across ticks and transfer-time acceptance.
Ten thousand unrelated mutations cause no new query searches and zero warmed
GC allocation. ECS storage ownership is doubled here; `ProductionEcsHarness`
with `-FluidBoundary` executes the actual bridge and revision notification sites.

`FluidOutputCache` diagnostics attribute exactly one reason to each failed capacity,
retention or selection query: cold/reset, topology, new tick, related storage,
request key or receiver revalidation. Attribution uses that precedence, not all
simultaneously invalid conditions. Connection topology/seed rebuilds and query
resets are counted separately because rebuilding clears the old query stamps.
The harness verifies reason totals match query miss totals, and related storage
changes and tick changes are classified correctly.

```powershell
./Tools/FluidStorageTransportHarness/Run.ps1
```

The harness is standalone and does not launch Unity. Scene registries, port
geometry and scheduling are test doubles; actual save loading, editor execution,
power network allocation and the complete maker intake/crafting cycle are not covered here.
