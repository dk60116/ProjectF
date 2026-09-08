# Train placement spacing

Run `powershell -ExecutionPolicy Bypass -File Tools/TrainPlacementSpacingHarness/Run.ps1` from the repository root.

Uses the production Complete layout helper and spacing constant with managed Unity vector math and in-memory rail/train stubs. Checks four straight orientations, existing/new cars, freight-only curves, reversed rail segments, endpoint bridges, facing, idempotence, and failure without partial movement. It does not launch Unity or validate scene rendering or physics.

The facing regression checks extract the production coupling-end storage and movement-facing methods. They verify that Complete fixes each car's physical front/rear connection, reversed rail authoring and perpendicular junctions preserve that orientation, reversing travel keeps the car facing unchanged, and disconnection removes the coupling metadata.

Connection regressions use production bridge samples to reproduce lateral endpoint gaps and small endpoint overlaps that used to turn cars sideways or backwards. They cover both travel directions, reversed rail authoring, curved joins, a leader without future path samples, and Complete while a car straddles the gap. Body facing follows the entry/exit rail axes while position and one-cell spacing follow the existing route.

Blueprint checks extract the production snap search, connection predicate and SAT clearance checks. They cover blueprints and installed neighbors in all four orientations, both ends, repeated snap calculations, long bodies, occupied ends, curves, reversed adjacent rails and rejection of distant/unconnected rails. The harness supplies in-memory colliders and managed yaw rotation; it does not exercise Unity Transform/physics APIs or the editor UI.

Locomotive tail coupling checks cover eight approaching headings for both freight cars and other locomotives, both predicate call orders, same-heading engine blueprint snapping, and rejection of nose-to-nose, distant, lateral and overlapping-center contacts. A locomotive tail is sufficient; the approaching car's heading does not reject that connection.

Edit checks extract the production rotation handler, map-edit Complete branch, contact commit helper, runtime connection eligibility and pose lookup. Rotating a locomotive tail towards either-facing freight must create a reciprocal rear connection; rotating it away must remove the invalid contact. Complete commits a five-car chain with outward-facing locomotives at both ends, normalizes center spacing, preserves facing and remains idempotent in all four map directions. Scene UI/physics remain stubbed; these checks run without Unity.
