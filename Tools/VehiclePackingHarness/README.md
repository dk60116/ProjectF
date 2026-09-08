# Vehicle packing harness

Run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/VehiclePackingHarness/Run.ps1
```

The harness extracts the production packing eligibility methods from
`InstallationPlacementController.cs`. It verifies that the player's exact mounted
vehicle is rejected while unoccupied vehicles, other vehicles, regular
installations and empty rails keep their previous behavior. It does not launch or
control the Unity editor.
