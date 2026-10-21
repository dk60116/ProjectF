# Train Focus Harness

Run `powershell -ExecutionPolicy Bypass -File Tools/TrainFocusHarness/Run.ps1` from the repository root.

Extracts the actual MapFocus area geometry methods, Block area setters, and PlayerController train focus dispatch. Transform and renderer boundaries are lightweight doubles; Unity is never launched.

Covers 1x1/1x2/2x1/2x2 footprints and all anchor offsets, interaction/mouse/selection routing, positive and negative headings snapped to grid axes, runtime floor anchors independent of the hovered footprint cell, model movement that must not move the floor marker, placement fallback, ground height, and zero managed allocations in warmed focus refreshes. Also checks the diesel prefab's configured 1x2 footprint and removal of superseded model-follow state.

Unity visual framing and native LineRenderer rendering still require an in-game check.
