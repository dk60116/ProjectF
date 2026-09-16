# MapPaper Optimization Harness

Runs deterministic checks for the optimized map composition path without launching Unity.

- Verifies the periodic train refresh does not perform the full coordinate scan.
- Verifies the static layer is cached and restored before train markers are stamped.
- Extracts and compiles the production marker-state comparison.
- Verifies visual changes invalidate the static map while unrelated saved payload does not.

Run with:

```powershell
Tools/MapPaperOptimizationHarness/Run.ps1
```
