# Boiler water supply regression checks

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/BoilerWaterPassHarness/Run.ps1`.

Requires .NET 9 and the Unity CoreModule reference installed at the path in Run.ps1.
Does not launch Unity. Extracts production fluid-output distribution,
boiler water-port routing and full-storage checks. The Unity-free SimulationCoreHarness
separately checks authoritative SoA heat/cooling and steam-budget calculation. Simulates two and three
connected boilers in all four rotations while the first consumes 5 L/s and the pump
supplies 15 L/s. Verifies downstream full-storage heating, upstream water availability,
water conservation and separation from the steam outlet.
The same run checks the directed boiler/generator chain rule for all four rotations,
including dense serial overlap, normal input alignment, side-neighbour rejection and
reverse-facing rejection. It also verifies that steam crosses connected pipe segments,
continues from a generator tail into another pipe segment, and stops at a pipe whose
reciprocal connector is missing. The run also checks all four rotations for overlapping,
adjacent, reversed and side-connected generator pass-through pipes, and verifies that the
runtime pipe graph and pipe fluid search retain their production integration points.
It also rejects the old consumer-subtraction path so a fully utilized boiler network
still reports its connected source pressure instead of `0.0 L/s`.
The harness also guards stored-steam generation against transient directed-chain cache
misses and verifies that steam trains resolve both ECS-only pipes and legacy loaded-Block
pipe bindings before registering their water receiver.
Partial steam intervals are verified as proportional power output, so `0.5 L` supplied
against a `1 L` scheduled demand produces 50% output instead of remaining inactive.

Registry lookup, grid placement, storage operations and energy are managed doubles;
live scene connectivity and complete steam/temperature simulation still require an
in-game check. Fluid transport now keeps producer-specific pipe distances, so the
old shared pump-network profiler counters are intentionally absent.
