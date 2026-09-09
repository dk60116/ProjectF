# FreightCar persistence harness

`Run.ps1` extracts the freight capture and restore members from production
`FreightCar.cs`, compiles them against small engine substitutes, and verifies:

- active cargo-point item IDs survive a capture/apply round trip;
- invalid IDs and inactive cargo points are excluded;
- restored objects are settled, parented, and immediately available;
- robot arms are notified once after a completed restore;
- an empty payload clears stale runtime cargo.

It also extracts the shared cargo summary and mounted focus handlers to verify:

- InfoPanel item IDs, counts and capacities for loose cargo and mounted boxes;
- aggregation of matching items, unloading, detached boxes and fluid-tank exclusion;
- fuel-supplier roles for both locomotives and the prefab's coal sprite binding;
- left/right empty-ground clicks clear mounted focus and subsequent refreshes keep
  it cleared, while UI clicks preserve selection and object clicks select again.

Station status English text and warning lights are covered by TrainAutoDriveHarness.
These probes do not launch Unity or validate rendered layout.

Run from PowerShell:

```powershell
./Tools/FreightCarPersistenceHarness/Run.ps1
```
