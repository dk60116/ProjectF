# Train placement spacing

Run `powershell -ExecutionPolicy Bypass -File Tools/TrainPlacementSpacingHarness/Run.ps1` from the repository root.

Uses the production Complete layout helper and spacing constant with managed Unity vector math and in-memory rail/train stubs. Checks four straight orientations, existing/new cars, freight-only curves, reversed rail segments, endpoint bridges, facing, idempotence, and failure without partial movement. It does not launch Unity or validate scene rendering or physics.

The facing regression checks extract the production coupling-end storage and movement-facing methods. They verify that Complete fixes each car's physical front/rear connection, reversed rail authoring and perpendicular junctions preserve that orientation, reversing travel keeps the car facing unchanged, and disconnection removes the coupling metadata.

Connection regressions use production bridge samples to reproduce lateral endpoint gaps and small endpoint overlaps that used to turn cars sideways or backwards. They cover both travel directions, reversed rail authoring, curved joins, a leader without future path samples, and Complete while a car straddles the gap. Body facing follows the entry/exit rail axes while position and one-cell spacing follow the existing route.

Bridge-exit checks execute the production stored-transition advance and reversal methods. They cover all four headings and both authoring orders with forward, backward and lateral endpoint corrections, plus single leaders facing either way before destination tape samples exist. Network search and unrelated path-tape merging are boundaries; the retained target, bridge progress, route-order calculation and outgoing direction are production code.

Manual-input checks execute the production mounted-input dispatch through rail selection, stopping before speed/physics integration. The world lookup supplies a selected branch, but the facing and input-axis update use the actual dispatch and facing helper. A solo locomotive moving backwards across a rail selection must keep its physical front and reverse input sign, for four headings, both authoring orders, forward/reverse input and curved exits.

Late-track checks execute the complete mounted-input entry, current/stored/nearest rail resolution, follower resolution and rail-network distance advancement. A ground-only train must remain stopped before rails exist, acquire a rail added afterwards, and advance for eight ticks without reinstalling. Cases cover all headings, reversed track, forward/reverse driving, both body orientations, idle-first acquisition, rejection beyond snap range and retention of an already-bound rail. Scene discovery, speed integration, docking, collision and transform writes are test boundaries; this is not a Unity physics or full automatic-route test.

Junction motion checks run the actual input dispatch and bridge advance for forward/lateral gaps. Branch selection must retain the current position and each subsequent frame must stay within its requested distance. Connection lookup checks prefer projected path points at overlapping endpoints while retaining the tighter limit for unrelated interior crossings. Physical facing is checked for both locomotive orientations and reversed rail authoring.

Blueprint checks extract the production snap search, connection predicate and SAT clearance checks. They cover blueprints and installed neighbors in all four orientations, both ends, repeated snap calculations, long bodies, occupied ends, curves, reversed adjacent rails and rejection of distant/unconnected rails. The harness supplies in-memory colliders and managed yaw rotation; it does not exercise Unity Transform/physics APIs or the editor UI.

Mixed-length blueprint checks require exact coupling pitch for every one-/two-cell
pair, body orientation, rail authoring order, neighbor placement state, physical
end and pointer offset. The Long Flatcar collider fixture comes from the current
prefab, so reducing its physical body length must not reduce its two-cell pitch.
Curves and junction bridges may add outward corner clearance; a straight segment
must use the nominal pitch without a coarse collision-search offset.

Curved-body checks execute the production two-support solver, common Train pose
application and driver support sampling through the existing rail-network
advancer. They cover both path authoring orders, forward/backward body headings,
both two-cell anchor choices, scaling, straight-to-curve joins and warm sampler
allocations. The corrected body midpoint follows the support chord while the
logical rail point, physical-end direction and footprint anchor remain unchanged.
These are managed geometry/lifecycle tests, not a Unity rendering or physics run.

Junction-body regressions also verify read-only support lookahead (no driving
route callbacks/cursor changes), exact logical-anchor retention after a visual
offset, and six long cars following the recorded consist path across alternating
rail authoring directions in forward/reverse travel. Network world discovery is
an adapter boundary; the production advancer, bridge and tape samplers run.

Station Center checks execute production station-distance, root-target conversion
and single-vehicle docking/snap methods with the current diesel collider fixture.
Changing the Center on the same placed instance, or on its ItemDefinition source,
must change both the arrival distance and actual snap position. Four map axes,
both body headings, reversed path authoring and a bent path are covered. Visual
correction must not change docking distance; raw pipe/tank samples stay unchanged.
Scene discovery and connected-consist movement remain adapter boundaries here.

Floor-focus checks extract the production PlayerController train marker method.
The authored body pivot, selected Center cell and footprint midpoint are separate:
changing Center must not shift markers, terrain validation or curve supports by
one cell. Tests cover both Center choices, four floor rotations, all three marker
kinds and placed/fallback coordinate sources. Marker writes are an adapter; no
Unity scene rendering is performed. Collider center supplies only the pivot
offset; focus size still comes from the one-/two-cell map footprint.

Fuel-role checks execute the production rear-supplier lookup and marker refresh
against the real TrainWorld connection graph. Only a directly rear-coupled car
may supply/advertise fuel, including when its position crosses the locomotive's
tangent plane on a bend. Reversed one-/two-cell cars align icon XZ to the authored
engine-side ItemPoint, preserving icon Y; Center edits cannot swap that end.
Tests cover offset/moving points, null entries, list order, rotated/scaled icon
parents, disconnect and warm zero-allocation refresh. The material contract requires the existing
unlit sprite shader, full opacity and normal depth occlusion. Sprite rendering
itself remains a stub and is not a visual shader test.

Locomotive tail coupling checks cover eight approaching headings for both freight cars and other locomotives, both predicate call orders, same-heading engine blueprint snapping, and rejection of nose-to-nose, distant, lateral and overlapping-center contacts. A locomotive tail is sufficient; the approaching car's heading does not reject that connection.

Edit checks extract the production rotation handler, map-edit Complete branch, contact commit helper, runtime connection eligibility and pose lookup. Rotating a locomotive tail towards either-facing freight must create a reciprocal rear connection; rotating it away must remove the invalid contact. Complete commits a five-car chain with outward-facing locomotives at both ends, normalizes center spacing, preserves facing and remains idempotent in all four map directions. Scene UI/physics remain stubbed; these checks run without Unity.

Train state migration checks compile the actual `TrainInstance`, `TrainWorld` and native connection adapter, replacing only the rail target boundary with the in-memory rail stub. The old facing/spacing/bridge checks now use the production data graph and rail-pose storage, not a second fixture-owned connection dictionary. New checks cover viewless reciprocal coupling, physical ends, graph revision/idempotence, stale entities, a 1,000-car chain split, spatial reindexing, rail removal, bridge retention, exact distance units, map-handle rebinding and separate ordinary-vehicle motion. Production Train/Vehicle lifecycle callbacks reproduce the pool's real PrepareForPool-then-OnDisable order and verify no ghost entity allocation, fresh identity on reuse and destruction cleanup. Warm data movement/query/connect/clear and native enumeration/contact paths are measured with `GC.GetAllocatedBytesForCurrentThread`; this is not a whole-game allocation or frame-rate benchmark.

Migration scope: rail pose, bridge progress, signed speed, physical coupling metadata and current coordinate membership are data-owned. Existing driving algorithms, player/HUD adapters and hierarchy-based batched rendering remain in place. Consist fixed-tick ownership, automatic schedules/resources/cargo, viewless rendering and direct train save/load are later stages; passing this harness does not claim those stages are implemented or that scene rendering/physics were visually verified.
