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
Wake notifications are dispatched through the production callback, including repeated
notifications at extended ports and their adjacent cells. Moving freight targets must
keep the drop retry active until they stop, even without a grid-coordinate change;
full stopped targets retain event-driven sleep.
The transfer payload retains its existing 26-byte layout; module state and arm
transfer state now have separate APIs and are both handled by existing save paths.

The harness supplies placement metadata and runtime lookups as test doubles. It does
not instantiate a Unity prefab or execute animation, physics, world IO registration,
electrical networks, or full save restoration. These need in-game verification.

Also exercises the production drop fallback guards for regular, elevated and splitter
belts with overlapping item/energy areas, in loaded and saved state. Covers independent
floor/conveyor virtualization and preserves ordinary ground and machine-area stacks.
It also checks conveyor placement eligibility on direct item-output areas and verifies
that machine output uses conveyor lanes, applies backpressure when full, and never falls
back to the area's center stack. A direct output must be empty before a conveyor can be
placed over it. The reverse installation order is covered as well: a direct item output
cell may be placed over a normal, elevated-endpoint, or splitter belt, while pipe-only
outputs and the raised 2F bridge center remain blocked.
For elevated belts, manual drops choose the available footprint cell nearest the player
and skip a lower crossing belt that occupies the bridge center.
The harness also verifies that motion anywhere in a connected train consist blocks
robot-arm pickup from and placement into its freight cars until the consist stops.
Machine outputs placed over belts are checked as observable transport boundaries. A
full output belt must wake its sleeping producer when automatic transport vacates a lane.
