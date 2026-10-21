# Train Coupler Harness

Run without starting Unity:

```powershell
dotnet run --configuration Release --project Tools/TrainCouplerHarness/TrainCouplerHarness.csproj
```

Compiles the production coupler solver, rig, presenter and graph/view lifecycle
against small standalone Unity adapters. Covers physical-end combinations,
curves, reverse travel, nested pivots, missing meshes, duplicate references,
pool reuse, view detach/attach, connection changes, deletion, culling, load
suspension, unchanged-pose writes and warm-loop managed allocations.

The adapters do not validate Unity rendering, actual frustum intersection,
mesh contact gaps, shadow visibility or engine-side CPU/GC costs.

Offline project compilation including sources not yet in Unity-generated projects:

```powershell
dotnet build FactorioProject/Assembly-CSharp-Editor.csproj --no-restore -v:q -p:CustomAfterMicrosoftCommonTargets=C:/Git/ProjectF/Tools/TrainCouplerHarness/Compile.targets '-clp:ErrorsOnly;Summary'
```
