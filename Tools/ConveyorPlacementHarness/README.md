# Conveyor placement completion regression

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ConveyorPlacementHarness/Run.ps1` (.NET 9, no Unity launch).

Extracts production animated insertion, landing completion and movement-hold methods. Managed doubles supply tween completion, cached readiness, inventory/rendering and wake boundaries. Checks immediate landing readiness, virtual/materialized parity, occupied destinations, explicit longer holds, snap/data-only placement, stale callbacks and placement without a timed hold.

`-BeforeFix` extracts the committed `Block.cs` to reproduce the failure: landing leaves the nominal hold and cached not-ready result behind. This comparison assumes HEAD still predates the fix. Actual game frame ordering, line wake processing and rendered motion require engine verification.
