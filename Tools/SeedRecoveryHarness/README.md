# Seed recovery harness

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/SeedRecoveryHarness/Run.ps1` from the repository root (.NET 9).

Extracts actual tree reward, planter input insertion, loaded/saved input restoration, logging completion/routing, floor capacity/occupancy, output-area configuration, and loaded/saved planting eligibility methods. Scene lookup, inventory storage and effects are managed doubles. Covers 36 cases including growth/chance conditions, duplicate rewards, all collected seed quantities, multiple inputs, partial capacity, saved input, mismatched planting targets, failed harvests, and seed overflow. Checks that planter soil permits logs, ordinary output areas remain protected, full ground delays harvest, logs remain on the original cell, and replanting waits for the final log to be removed. No Unity launch or scene mutation. Actual visual flight and end-to-end save/load require engine verification.
