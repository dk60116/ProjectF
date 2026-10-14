# Wall Focus Regression

Run `./Tools/WallFocusHarness/Run.ps1` without launching Unity.

Brick Wall and Wooden Fence used serialized `MultiFocusMode.None` (2), which
disabled both scene-object and data-only BuildingWorld focus. All four shape
variants of each wall now use `NearOne` (1). Door behavior stays unchanged.

HUD refresh also previously rejected BuildingRuntimeRecord because its type was
missing from a per-type whitelist. PlayerHUD now uses IMapObjectTarget.IsTargetActive
for all map targets, while retaining virtual conveyor/pipe fallback validation.
This removes duplicate per-type branches and keeps removed entities invalid.

The harness checks actual Wall component blocks by script GUID, all eight focus
modes, positive proximity radii, retained item references, door focus and the
scene/data-only focus property contracts. It compiles the actual PlayerHUD
availability method against minimal boundary fixtures and tests repeated wall
refresh, removal, generic ECS/scene targets, monsters, portable items and existing
virtual conveyor/pipe fallbacks. It does not simulate mouse rays, the full HUD
update loop or verify the in-game outline/InfoPanel.

## Collision Geometry

BuildingWorld's original combined boxes had inward-facing triangles, exposing
backfaces to approaching players. Box faces now point outward, with winding
reversed again for reflected transforms. The harness compiles the production
index table and AppendBox method against minimal vector/matrix fixtures. It checks
every face, quarter-turn rotations, translated/nonuniformly scaled boxes, all
single-axis reflections and index offsets when appending multiple boxes.
This verifies geometry, not PhysX cooking, capsule casts or in-game movement.

The shared BuildingWorldView collider also now uses the Object layer. Previously
it inherited Default, which the project's Player layer excludes. The harness
checks host-layer assignment and both directions of the actual serialized
Player/Object collision matrix. No global physics settings are changed.

## Placement Lifecycle

The placement animation registers a building, suppresses its presentation, then
reveals it after a delay. A collision rebuild while suppressed emits no boxes.
Previously reveal only refreshed render batches, leaving that empty collision mesh
unchanged indefinitely. Both suppression transitions now dirty collision; creating
a replacement shared view also requests a rebuild of existing records.

The harness extracts production AttachView, OnViewDestroyed, suppression/scale
setters, Render and AppendCollisionGeometry. Minimal render/physics ports check
one-through-four-part walls across hide/reveal, repeated state, visual scaling,
load/benchmark barriers, view recreation and empty-record cleanup. Before the fix
this test failed at the first delayed reveal, despite the geometry/layer tests
passing. It verifies collision rebuild scheduling and box inclusion, not actual
PhysX capsule contacts or live player movement.
