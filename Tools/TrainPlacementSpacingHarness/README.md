# Train placement spacing

Run `powershell -ExecutionPolicy Bypass -File Tools/TrainPlacementSpacingHarness/Run.ps1` from the repository root.

Uses the production Complete layout helper and spacing constant with managed Unity vector math and in-memory rail/train stubs. Checks four straight orientations, existing/new cars, freight-only curves, reversed rail segments, endpoint bridges, facing, idempotence, and failure without partial movement. It does not launch Unity or validate scene rendering or physics.
