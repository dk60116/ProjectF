# Forestry ECS harness

Run `./Tools/ForestryEcsHarness/Run.ps1` from the repository root (.NET 9).

Executes the production ForestryWorld, data entities, filters, harvest routing,
fuel helpers, deterministic units, generation slots and coordinate wake registry.
Power topology, rendering, terrain/storage and resource depletion are managed boundaries.
No Unity process, screen control or scene mutation is used.

Checks target identity, streaming, filters, duplicate harvests, original-cell logs,
seed transfer/ownership, partial power, completion restoration, fuel de-duplication,
loaded/saved recovery, stable receiver ordering, local wakes among 1,000 facilities,
entity disposal and warm-update GC allocation.

For actual binary serialization, compile with
`./Tools/CraftingTreeQuantityHarness/Compile.ps1`, then run
`./Tools/ForestryEcsHarness/RunSerialization.ps1 -AssemblyPath <compile-probe>/bin/Debug/netstandard2.1/Assembly-CSharp.dll`.
The compile probe path is printed by Compile.ps1. This exercises complete production
installation serialization for v71, v70 backward reads, nested records, cloning and corruption.

Real instanced animation, warning lights, colliders, editor move/cancel/upgrade and
electrical network allocation still need an authorized Unity verification pass.
