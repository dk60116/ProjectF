# Logging machine target checks

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/LoggingMachineTargetHarness/Run.ps1
```

The harness extracts the production logging target methods and rejects any restored
output-space gate. It verifies that valid adjacent trees are recognized while growth,
filter, active-state, harvest-mode, and rotation rules remain enforced. Log placement
and harvest routing are covered by `SeedRecoveryHarness`. No Unity process is launched.
