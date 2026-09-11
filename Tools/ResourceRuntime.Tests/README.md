# Resource runtime lifetime checks

Run without starting Unity:

```powershell
dotnet run --project Tools/ResourceRuntime.Tests/ResourceRuntime.Tests.csproj --configuration Release
```

This runner compiles the production `ResourceStateSlots.cs` directly. It checks array growth, independent resource values, stale harvest handles after slot reuse, initial reservation values after replanting, restored values, bulk unload and 50,000 deterministic allocate/release operations. It does not validate Unity rendering, input, physics or the save-file serializer.

In a running game, `Tools/ProjectF/Diagnostics/Validate Shared Resources` checks resource handles, unique coordinates, block ownership, save-state quantities, render matrices, collider ownership and the absence of per-resource MonoBehaviours on each type host. Run this diagnostic after generating/loading a map and after harvesting and replanting.

Manual checks: focus adjacent resources separately; finish a harvest while another machine targets that resource; harvest the last unit and reload the chunk/save; plant/grow/water/fertilize a seed; mine ore/oil through an installation; compare remaining units in the HUD and profiler. Each type host should contain a single `ResourceTypeWorld` MonoBehaviour plus native Collider components.
