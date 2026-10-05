# Player collision with data-only pipes and corner belts

Run `./Tools/PipePlayerCollisionHarness/Run.ps1` from the repository root.
The runner uses the Unity managed assemblies referenced by Assembly-CSharp.csproj;
it does not launch Unity or capture/control the desktop.

The harness executes the production PlayerController movement sweep, pipe-record
collision query and rounded-box sweep. World registries and capsule geometry are
fixtures, while vector/bounds math uses Unity's managed types.

Checks cover straight-pipe crossing at high speed, contact distance/normals,
sliding, overlap escape, ignored layers, disabled capsules, placement suppression,
removal, nearest-hit precedence, elbow/T/cross clearances, raised paths, and both
underground mouths with a walkable gap. Authored BoxCollider transforms are read
once by the runtime record on installation/load. Prefab loading and actual Rigidbody
movement still require in-game verification.

Corner-belt checks execute the real PlayerController sweep. Both corner variants
and four rotations cover walkable side/diagonal entry, open input/output, stepping
off, short movement steps across capsule overlap, raised paths, ignored layers,
suppression, removal, and straight belts. A corner at the same coordinate never
bypasses pipe or physical obstacles. Low corner belts have no artificial side
barriers; raised 2F side barriers are checked by ConveyorPathHarness.

The authored-corner regression reads Pipe_Corner.prefab, Player.prefab and the
project's actual Physics layer matrix. It reconstructs the two authored box arms
(with child translation and four grid rotations), preserving their collider layers,
and passes those shapes through the production movement sweep. This reproduced the
corner's Default-layer failure before changing its collider owner to Object. Synthetic
all-layer fixtures alone cannot detect that regression. The fixture explicitly rejects
unsupported child rotations/scales rather than silently using incorrect geometry.

ProductionMachine asset checks read all four prefab tiers, their authored solid
BoxColliders, Player's actual layer and the project Physics collision matrix.
The Player must collide with each Collider owner layer; geometry-only fixtures
would miss the Default-layer regression.
