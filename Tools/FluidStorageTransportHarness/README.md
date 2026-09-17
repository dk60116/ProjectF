# Fluid storage transport harness

Extracts and executes the production output-cache BFS, directed boiler/generator
traversal, storage lookup, output transfer, placement wake handlers, and
`InstallationObject.TryAddFluidLiters`. Assertions check transferred liters AND
the resulting stored liters; receiver caches are not prepopulated by the tests.

Cases cover four cardinal orientations: pump to tank, boiler to generator,
full generator to downstream tank, registered storage without a Block owner,
disconnected pipes, reversed generators, and adding tanks/generators/generic
CanStoreFluid objects after the upstream producer sleeps with an empty cache.
The earlier lookup-only harness was replaced because it could pass while actual
storage transport remained broken.

```powershell
./Tools/FluidStorageTransportHarness/Run.ps1
```

The harness is standalone and does not launch Unity. Scene registries, port
geometry and scheduling are test doubles; actual save loading, editor execution,
production budgets, and power/recipe evaluation are not covered here.
