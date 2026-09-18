# Pipe variant cast-iron pass

Corner, T and Cross meshes were imported from their current Unity serialized
mesh assets into the running Blender 4.5 session. Each variant was cleaned,
smart-unwrapped and baked to its own 2048px cast-iron atlas. The material matches
the dark Pump body reference and keeps subtle worn edges.

## Results

| Variant asset | Vertices | Triangles | Hidden/tiny triangles removed |
| --- | ---: | ---: | ---: |
| Pipe_Corner | 2421 -> 2424 | 3488 -> 2928 | 560 hidden |
| Pipe_T | 818 -> 922 | 1117 -> 1077 | 40 sub-nanometre-area slivers |
| Pipe_T_Cylinder | 603 -> 564 | 848 -> 592 | 256 hidden |
| Pipe_Cross | 6869 -> 4985 | 9938 -> 5510 | 4428 hidden |

Vertex count can increase when the new non-overlapping atlas needs additional UV
seams. Triangle and overdraw reduction is the geometry optimization target.

The T end-ring originally used the shared `Cylinder.asset`. That mesh is also used
by Underground Pipe, Sprinkler and other machines, so it was restored unchanged.
The cleaned/reatlased version is installed as `Pipe_T_Cylinder.asset`, referenced
only by the three T-pipe ring renderers.

`Pipe_Variants_CastIron.blend` retains packed baked images and editable procedural
source materials. Per-variant validation JSON records source hashes, UV overlap,
hidden-face removal and output counts. `original/` contains current pre-edit meshes,
prefabs and the previous Blender session.

Hidden-face deletion is conservative: a triangle is removed only after dense
sampling finds it strictly inside another closed component. 322752 exterior rays
found no first-visible-surface or silhouette changes. All three atlas checks found
zero overlapping pixels at 1024px. Unity runtime appearance was not tested.
