# Pipe placement harness

Extracts the production duplicate-pipe placement rule from
`InstallationPlacementController`. It verifies that a new pipe cannot occupy a
coordinate already owned by another pipe, while an explicitly ignored preview,
non-pipe placement, and empty coordinate remain unaffected.

Also extracts the production fluid preference, network traversal, placement
compatibility, and port-priority rules. A world-lookup fixture checks straight continuation against three foreign branches,
parallel lines in four rotations, blueprint and committed normalization, endpoint
ownership, tie-breaking, and manual/underground ports. Tunnel tests use untyped
prefabs and a remote source: runtime records, saved pairs, and scene previews must
carry that identity across both endpoints in all four rotations. A straight
extension is accepted while a corner into a foreign-fluid row is rejected.
Pump cases cover both pipe placement beside existing pumps and new pump placement
between two tanks or pipes. Empty/same-fluid endpoints are accepted, foreign-fluid
endpoints are rejected, and both adjacent and overlapping endpoints are tested in
all four rotations. Pump geometry and world lookup are fixtures; the compatibility
and network traversal methods are extracted from production.
Unity rendering and a full placement transaction are not executed.

```powershell
./Tools/PipePlacementHarness/Run.ps1
```
