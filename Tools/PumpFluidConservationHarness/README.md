# Pump fluid conservation regression

Run `./Tools/PumpFluidConservationHarness/Run.ps1` from the repository root with .NET 9.
The harness compiles the production Pump budget, storage-to-storage transfer,
output acceptance, fluid rollback, and input/output preflight methods.
Unity world lookup and storage endpoints are fixtures; no Unity process is opened.

The original implementation failed nine checks: it withdrew fluid before applying
the Pump allowance, then tried to return rejected fluid through the external fill
gate of an unloading-only source. A depleted allowance could therefore drain the
source without delivering anything. Refinery preflight also counted raw tank
contents/free space without accounting for the same Pump's remaining allowance.

Checks cover partial and exhausted Pump allowances, rejected and partial receiver
acceptance, restoration of the last fluid in an unloading-only source, conservation
of source-plus-destination volume, shared branches, and a shared refinery preflight
across input/output ports. Preflight must not spend the actual Pump budget.

The fixture does not simulate train-stop animation or the live HUD.
