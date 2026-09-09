# AreaMarker CPU harness

Run `pwsh -File Tools/AreaMarkerHarness/Run.ps1` from the repository root. Requires .NET 9; does not start Unity.

The runner copies the production marker controller, visibility context and central renderer into a temporary .NET project. `UnityStubs.cs` provides an in-memory rendering facade and System.Numerics transforms. Tests exercise actual production registration, batching, geometry composition and cleanup rather than a separately implemented marker algorithm.

Coverage:
- Distance boundary, height independence, missing player, selection and placement/edit visibility.
- 1,000 markers sharing two layers, scratch-list ownership, unchanged configuration/frame reuse.
- Visibility transitions, disable/re-enable, duplicate registration, clearing and native resource disposal.
- Parent movement/reconfiguration, icon rotation with non-uniform scale, atlas UV and alpha preservation.
- Moving preview changes leave placed mesh uploads untouched.
- Normal, station and preview depth/queue configuration; negative chunk coordinates and absent icons.
- More than 65,535 vertices in one batch with valid 32-bit triangle indices.

This harness does **not** verify GPU output, URP transparency sorting, shader compilation, frustum culling, Unity destroyed-object null semantics or measured frame time. After the owner starts Unity, visually check marker overlap with belts/floors, station markers, rotated previews, and camera movement. Profile `AreaMarker.Visibility`, `AreaMarker.RebuildMeshes` and `AreaMarker.SubmitBatches`. `AreaMarkerRenderer` exposes registered/visible counts, current batches and rebuild count for debugger/harness inspection. No marker GameObjects should appear in the scene hierarchy.
