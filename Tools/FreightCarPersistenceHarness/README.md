# FreightCar persistence harness

`Run.ps1` extracts the freight capture and restore members from production
`FreightCar.cs`, compiles them against small engine substitutes, and verifies:

- active cargo-point item IDs survive a capture/apply round trip;
- invalid IDs and inactive cargo points are excluded;
- restored objects are settled, parented, and immediately available;
- robot arms are notified once after a completed restore;
- an empty payload clears stale runtime cargo.

Run from PowerShell:

```powershell
./Tools/FreightCarPersistenceHarness/Run.ps1
```
