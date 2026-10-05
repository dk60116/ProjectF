# LoggingMachineTargetHarness compatibility entry point

Run `./Tools/LoggingMachineTargetHarness/Run.ps1` from the repository root (.NET 9).

Forwards to ForestryEcsHarness, which executes the installed data entities instead of
obsolete native-component simulation. Includes growth/type/rotation rules, target identity,
power loss/recovery, saved progress, exactly-once completion, seed ownership and recovery.
No Unity process is launched. Rendering and real electrical topology need engine verification.
