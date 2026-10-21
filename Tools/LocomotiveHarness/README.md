# Locomotive Harness

Run `pwsh -NoProfile -File Tools/LocomotiveHarness/Run.ps1` from the repository root.

The probe extracts real locomotive resource/movement methods and the diesel fuel
policy, using inventory/scene boundary doubles and the actual VehicleMotionState.
It checks Diesel Can identity, wrong fuel rejection, bag/hand/rear-car supply,
water-free diesel movement, unchanged steam water use, idle/reverse consumption,
deceleration on exhaustion, fuel restoration and deterministic energy accounting.
The real fuel-role resolver and freight icon refresh also cover switching steam
and diesel suppliers, configured can identity, missing/invalid fuel data,
disconnection, inactive engines, and zero managed allocations after warm-up.
It also checks prefab bindings and shared HUD/save integration without launching
Unity or changing the configured acceleration, speed, mass or consumption amount.

For full editor assembly compilation without opening Unity:

`dotnet build FactorioProject/Assembly-CSharp-Editor.csproj --no-restore -v:q -p:CustomAfterMicrosoftCommonTargets=C:/Git/ProjectF/Tools/LocomotiveHarness/Compile.targets`

TrainAutoDriveHarness and TrainPlacementSpacingHarness separately exercise the
shared route/controller, docking, connection and movement paths. This probe does
not replace Unity play/render verification for exhaust, headlights or UI visuals.

Legacy test gaps observed during this change: BoilerWaterPassHarness still
extracts the removed TrySelectFluidOutputStorageWithAnySpaceFromCache method;
FreightCarPersistenceHarness uses the old persistence interface without optional
installation-health parameters. These failures predate the locomotive extraction.
LocomotiveHarness tests fuel-state round trips; RailWorldHarness tests saved-grid
bindings, and FluidStorageTransportHarness covers the shared water output path.
