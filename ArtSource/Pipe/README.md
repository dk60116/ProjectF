# Pipe cast-iron texture

Authored and baked in the running Blender 4.5 session from the current Unity
`Assets/MapObject/Fluid/Pipe/Pipe.asset` mesh, using Pump_Icon.png as the material reference.

- `Pipe_CastIron.blend`: packed 2048px baked texture and editable procedural source material.
- `Pipe_CastIron_TB.png`: UV atlas, not a tiling texture.
- `Pipe_Baked.asset`: generated Unity mesh with matching UV0 and rebuilt tangents.
- `validation.json`: geometry preservation and rasterized UV overlap checks.
- `original/`: pre-edit mesh, prefab and Blender scene backups.

Only the straight Pipe prefab uses M_Pipe_CastIron. Other variants still use
their existing material/UVs and must not be assigned this atlas without rebaking.

The current Unity mesh has 1600 triangles and 1468 vertices. The first cleanup
merged equal position/normal/UV records (2846 to 2058 vertices). The second cleanup
removed 808 hidden sphere triangles and their unused vertices. UV, normal and
tangent-handedness seams stay split. Blender's authoring mesh has 848 vertices
and 1564 faces; it stores UVs/normals per corner and omits 36 coincident backfaces
that are retained in Unity. Surviving positions, normals and both UVs are unchanged.

`vertex_cleanup_validation.json` records exact per-corner preservation and tangent
validation. `before_vertex_cleanup/Pipe.asset` is the recoverable pre-cleanup backup.
`cleanup_unity_mesh()` generates the compact asset without installing it; it refuses
to overwrite that backup. `export_mesh()` now includes the same compaction step,
so future exports do not reintroduce source-ID vertex duplication.

`cull_hidden_spheres.py` tests each end-sphere triangle against the convex collar
and the actual hollow tube wall. It retains the endcaps visible through the central
window, and conservatively keeps intersection-boundary triangles. Its `analyze()`
checks first surface hits for 202800 exterior rays; `apply()` removes the approved
faces in both assets. `hidden_surface_validation.json` records the current counts;
`before_hidden_surface_cleanup/` contains recoverable mesh and Blender backups.
The original `validation.json` and vertex cleanup report describe earlier stages.

`build_pipe_material.py` contains prepare, unwrap, bake and export_mesh entry points
for a live Blender session. Initialization requires an empty scene and refuses
to overwrite backups. It generates review outputs rather than installing assets.
The saved blend supports direct editing; the script's import/export session state
exists only during the original live session.

Blender appearance and UVs were verified. In-game Unity appearance was not tested.
