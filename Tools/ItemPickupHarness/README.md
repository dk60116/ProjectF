# Item pickup harness

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ItemPickupHarness/Run.ps1` from the repository root (.NET 9).

Extracts the production BagSlot candidate selection, automatic/slot preview, and click dispatch methods. Managed doubles provide world candidates and inventory operations. Covers distance ordering across ground, focused conveyors, boxes, storage and robot arms; empty/unavailable focus; slot compatibility; ties; stack height; preview/click consistency; and stale preview revalidation. No Unity launch. Actual animations and scene interactions still require in-engine verification.
