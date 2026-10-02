# Crude oil refinery batch and transport regression

Run `./Tools/CrudeOilRefineryTransportHarness/Run.ps1` from the repository root.

The harness compiles the complete production refinery source, the shared
ProductionProcess, deterministic units, and the production craft advancement,
pipe retention, pressure and pump limit methods against small transport fixtures.

Checks cover complete intake before atomic consumption, full waiting power,
separate input capacities derived from nominal duration, intake rejection during
processing and output, blocked/disconnected/partial outputs without fluid loss,
energy pauses and slowdown without changing batch volume, weighted temperature,
time-based completion, pressure only while outputting, and snapshots during
collection, processing and partially completed output. Transport checks retain
per-port distance loss, fluid isolation and pump limits.
Intake boundary regressions cover integer deficits below/at/above the shared
0.0001 L transport cutoff, preventing a full-looking buffer from waiting forever
and ensuring a deliverable shortage still requires real supply. Consumption never
creates negative stock when an undeliverable rounding tail is tolerated.

Port placement, storage receivers and energy supply are fixtures. Unity rendering
and live scene transport are not exercised. Binary save round trips are checked
separately by SaveLoadProfileHarness `--refinery-self-check`.
