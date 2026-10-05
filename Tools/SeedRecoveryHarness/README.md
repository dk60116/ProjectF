# Seed rewards and ground checks

Run `./Tools/SeedRecoveryHarness/Run.ps1` from the repository root (.NET 9).

Extracts actual tree reward rolls, floor occupancy/capacity, planter output-area
configuration and loaded/saved planting eligibility. Checks duplicate seed rewards,
growth/chance conditions, the original log cell and replanting after final log removal.
Managed storage/scene doubles; no Unity process is launched.

Installed logging/planting and loaded/saved seed recovery are now exercised by
`./Tools/ForestryEcsHarness/Run.ps1`. Removed native-component simulation doubles.
