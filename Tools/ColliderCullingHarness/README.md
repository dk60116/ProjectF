# Collider Culling Harness

`Run.ps1` compiles the production `WorldColliderCullingManager` with managed Unity doubles.
It verifies initial sweep budgeting, zero-scan stationary frames, spatial-cell movement,
distance hysteresis, free-camera range, swap removal, global restore, and dead-target cleanup
without launching Unity.

The `IncludeNewSources.targets` validation import also lets the current Unity-generated
`Assembly-CSharp.csproj` compile newly added scripts before Unity regenerates its source list.
