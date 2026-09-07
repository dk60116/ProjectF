# Robot arm standard IO checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/RobotArmIoHarness/Run.ps1
```

Uses .NET 9 and the installed Unity managed vector assembly without launching Unity.
Reads the actual robot-arm prefab's InputItem/Object/Output grid. Extracts production
endpoint resolution, grid-coordinate rotation, wake-coordinate registration, solid
item eligibility, transfer state, and binary transfer-state read/write methods.

Checks all four rotations at positive/negative origins, cached endpoints, reordered
and extended grids, missing ports, placement clearing, wake registration at distant
ports, disabling cleanup, exclusion of fluids, and all eight saved transfer phases.
The transfer payload retains its existing 26-byte layout; module state and arm
transfer state now have separate APIs and are both handled by existing save paths.

The harness supplies placement metadata and runtime lookups as test doubles. It does
not instantiate a Unity prefab or execute animation, physics, world IO registration,
electrical networks, or full save restoration. These need in-game verification.

Also exercises the production drop fallback guards for regular, elevated and splitter
belts with overlapping item/energy areas, in loaded and saved state. Covers independent
floor/conveyor virtualization and preserves ordinary ground and machine-area stacks.
