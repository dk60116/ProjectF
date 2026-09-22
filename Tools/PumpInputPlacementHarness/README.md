# Pump input placement and delivery regression

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/PumpInputPlacementHarness/Run.ps1`.

The harness extracts production Pump geometry, endpoint roles, runtime pass resolution,
and placement overlap checks. It checks all four rotations, facing and fluid compatibility,
PipeInput/DoubleInput body overlap, and the actual destination returned to forward fluid
searches. Mounted-tank unloading uses that same destination resolver.

World registration, item compatibility lookup and Unity placement transforms are fixtures.
SteamPipePressureHarness verifies real input-pressure queries against the production network
search. ProductionMachineFluidInputHarness checks recipe receiver capacity and fluid buffers.
No Unity process or screen is controlled.
