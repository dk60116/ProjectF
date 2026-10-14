# Player Split Animation Harness

Run `pwsh -NoProfile -File Tools/PlayerAnimationHarness/Run.ps1 -CompileUnity`.

The runtime fixture compiles the actual Player animation queue/update/cancellation methods
and Player.UpperBodyAnimation partial. Animator evaluation is the boundary, not a second
implementation of the animation policy. Checks cover movement during Pick/Throw, start/end
signals, weight blending, full-body digging cancellation, death/mount/reset behavior,
controller cache invalidation and warmed zero-GC updates.

Asset checks use the pinned YAML parser to validate the real controller's local references,
layer/state ownership, exit rules, mask GUID and generic Mixamo bone paths. The first run
installs the local parser dependency. The optional compile switch also runs the monster
combat/knife harness and builds Unity runtime/editor C# without launching the Editor.

Pick and Throw are upper-body overrides. Digging, Handcart Drive and Riding remain full-body.
Manual walking can begin an in-range knife swing and does not cancel an ongoing swing;
manual input still cancels an out-of-range automatic approach. Range/line checks at impact
are unchanged. Root motion remains disabled on the existing player prefab.

Actual Animator sampling, bone poses and visual blending still require Editor play validation.
