# RectGrid placement harness

Extracts the production RectGrid placement and normalization methods from
`InputOutputModule`. It verifies that multiple `PipeOutputItem` cells survive
editor placement and serialized-data normalization, while direct output and
input-energy variants keep their existing single-placement behavior. It also
extracts the editor numbering method and verifies top-to-bottom, left-to-right
`PipeOutputItem` numbering. It also verifies that each output keeps its own
configured fluid after conversion to world coordinates and rotation.

```powershell
./Tools/RectGridPlacementHarness/Run.ps1
```

The harness is standalone and does not launch Unity.
