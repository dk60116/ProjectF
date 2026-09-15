# Fluid Jobs Harness

Runs the production fluid topology checksum kernel against managed stand-ins for Unity's
native containers. It checks worker-order determinism, dirty-network isolation, topology/state
coverage, display-source priority, and persistent-buffer disposal.

```powershell
dotnet run --project Tools/FluidJobsHarness/FluidJobsHarness.csproj -c Release
```

This first migration stage is a read-only shadow path. Existing managed fluid transfer remains
authoritative until endpoint storage and transfer requests are represented in the native model.
