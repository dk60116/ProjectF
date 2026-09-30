# Pump shape cleanup — 2026-09-29

## Outputs

- `Pump_shape_cleanup.blend`: editable mesh, packed existing Pump_TB texture.
- `Pump_shape_cleanup.fbx`: FBX with the original `Scene` mesh name, existing texture referenced by absolute path. Unity triangulates the polygons on import.
- `original/before_shape_cleanup_20260929`: untouched project FBX and initial imported Blender scene.
- The final FBX is installed at `FactorioProject/Assets/MapObject/Fluid/Pump/Pump.fbx`; its original importer GUID and prefab overrides are retained.

## Changes

- Limited Dissolve at 0.5 degrees with UV delimit enabled.
- Flattened the topmost roof ring in front orthographic view.
- Repaired three collinear T-junctions with six edge splits and welding.
- Circularized 22 complete connector rings.
- Enlarged the smaller recessed flange to approximately match the opposite flange diameter; bore and mouth dimensions were preserved. The two connectors were not replaced by identical copies.
- Recalculated outside normals. Retained object name, transform, UV layer and texture.

## Validation

- 2641 mesh vertices, 3708 editable polygons, 5278 exported triangles.
- Boundary edges: 0; nonmanifold edges: 0; zero-area polygons: 0.
- Exported FBX was reimported into Blender: 2641 vertices, one UV layer, maximum world-coordinate difference 0.0.
- Inspected textured model in front and angled views.
- Unity/game verification was not performed.

`shape_cleanup.py` performs the precise repair and connector adjustments after the two manual operations above. It refuses to run twice on an already-cleaned object.

## Upper-box correction

- `align_upper_box.py` rectifies the measured XY shear of the tall actuator and aligns its upright edges and core side planes.
- The cap's lower, shoulder and top bevel rings now share center (-0.125, 0.0234375), have symmetric rectangular dimensions and level heights.
- Ring height error: 0.0 m. Maximum measured central-symmetry error: 3.73e-8 m.
- Top and front orthographic views inspected in Blender. Mesh remains manifold with 2641 vertices and the existing UV layer.
- Original custom normals were reset to calculated normals after reshaping.
- Updated the same `.blend` and `.fbx` outputs. Previous versions are preserved under `original/before_box_alignment_20260929`.
- Unity assets remain unchanged; the updated export was not tested in Unity.

## Mirrored inlet/outlet assemblies

- Rebuilt both ports from a common axial profile, mirrored about world X=0. Measured generated-vertex mirror error: 0.0 m.
- Identical 24-sided flange/mouth geometry. Eight beveled hex-head bolts with washers on each outer flange face, at 45-degree intervals and 0.157 m pitch radius.
- The main housing, upper actuator, mounting feet and their original material/UVs remain. This revision does not rearrange the housing's other decorative fasteners.
- Added matching steel, brass and bore materials (four slots including the original). New components use these materials rather than the old irregular port texture islands.
- Assemblies are separate closed shells overlapping capped body necks in the same mesh. Internal fluid passage continuity/3D-print boolean union is not modeled.
- Final result: 4146 mesh vertices, 8220 triangles. Manifold/degenerate-face assertions passed on creation. Reimported FBX has 4146 vertices, four materials and 0.0 maximum world-coordinate difference.
- Inspected left, right, front and angled views. Updated the same `.blend` and `.fbx` files; Unity integration remains pending.
- Live pre-edit scene and previous FBX are preserved in `original/before_port_symmetry_20260929`. `symmetrize_ports.py` includes the final shoulder and shading refinements.

## Common-centerline actuator and body fasteners

- `center_box_and_fasteners.py` shifts the tall actuator laterally by -0.0224609375 m, retaining its longitudinal position. Roof center is now Y=0.0009765625, identical to the inlet/outlet axis; measured lateral error is 0.0 m.
- Translation blends into the lower attachment. The exposed connecting rod is straightened along the same axis.
- Four foot fasteners use a symmetric pitch, common height, and matching hex heads. Opposed central bolts, actuator end bolts and mounting-lug pairs are aligned; smaller head fasteners have normalized profiles and depths.
- Irregular housing bolt triangulation is retained and projected onto matching octagonal profiles. This is local fastener cleanup, not a full remesh or mirroring of the entire textured housing.
- 422 fastener/rod vertices normalized. Existing topology, UVs, object transform and 4146-vertex count retained. Detached-mesh checks passed: manifold, no zero-area faces, generated port coordinates unchanged.
- Pre-edit live scene and previous export are preserved under `original/before_body_alignment_20260929`. Same working blend/FBX outputs updated.

## Unity Pump integration

- Copied the final `Pump_shape_cleanup.fbx` to `FactorioProject/Assets/MapObject/Fluid/Pump/Pump.fbx`, preserving the existing `.fbx.meta` GUID and prefab transform overrides.
- Added steel, brass and bore Unity materials for imported submesh slots 1–3. Existing textured `M_Pump` remains slot 0; all four FBX material names now map explicitly to their Unity material assets through `Pump.fbx.meta`.
- Existing `Pump.fbx`, `.fbx.meta` and `Pump.prefab` immediately before integration are preserved in `original/before_unity_integration_20260929`.
- File hashes and serialized asset references were checked. Unity Editor import and in-game appearance were not run.

## Texture import repair

- The first integrated export named its geometry `Scene.003`, whereas the original FBX used `Scene`. This can change imported mesh identity. The repaired export keeps `Scene` and the original model name.
- The original textured body retained UVs: 2612 of 2712 distinct UV pairs in final body faces match the original FBX exactly; none of the body's UV corners are zero.
- Blender exported without forced triangulation or modifier evaluation, preventing the temporary mesh suffix. Unity's FBX importer performs triangulation.
- `Pump.fbx.meta` maps `tripo_mat_23e97eb0` to `M_Pump` and the three new slot materials explicitly. Redundant new material overrides were removed from `Pump.prefab`; its existing slot-0 override and all transform settings remain.
- Previous integrated FBX and prefab files are in `original/before_texture_repair_20260929`. The project's existing `Pump_TB.png` and `M_Pump.mat` were not changed by this repair.

## Single-atlas port texturing

- `texture_ports_single_atlas.py` maps the 1632 steel, 936 brass and 99 dark-recess faces (plus the original 2132 textured faces) to the existing `Pump_TB.png` UV atlas. It removes three temporary port material slots, leaving one material, `tripo_mat_23e97eb0`. No bitmap edits were made.
- The port, flange, bore and fastener UVs now point to steel, brass and recess swatches on that same image. `M_Pump` is the only Unity material mapping; the three port-only Unity materials were removed because nothing else referenced them.
- Blender Texture Paint view was visually inspected: magenta untextured port surfaces disappeared, with both ports and all fasteners showing the atlas. `export_single_atlas.py` checks that all 4799 faces use slot 0, all 17818 UV loops are inside the image, and there are no open/nonmanifold edges or zero-area faces before exporting.
- A separate raycast check found no uncovered points inside either port bore, but the original body cap was deep and dark enough to look empty. The later port-surface repair below moves the visible closure forward.
- The updated `.blend`, source FBX and Unity `Pump.fbx` were saved. The previous files are in `original/before_single_texture_20260929`; Unity Editor reimport and in-game rendering were not verified.

## Port surface and recess repair

- The Unity screenshot exposed a checkerboard-like inlet/outlet rim and an apparently empty dark center. `inspect_port_surface.py` confirmed both 24-face front annuli were complete, but each 456-face port shell had 912 UV seam edges because the previous atlas projection restarted on every face.
- `repair_port_surface.py` projects connected steel, brass and bore bands continuously across shared vertices. Both ports now have 96 UV seams each, only at intended material-color transitions; 1440 front-ring ray samples still hit the complete rims.
- `close_port_recesses.py` adds matching, shallow closed metal inserts to both port centers, 25 mm behind the outer lip. The original body vertices and UVs are unchanged. All 384 sampled rays into the former dark centers hit the new inset faces.
- Final editable mesh: 4242 vertices, 4851 polygons, 18106 UV loops, one material and one image atlas. All edges remain manifold and every face has nonzero area. Blender's textured right-orthographic view shows a solid center and smooth, non-grid flange.
- The source `.blend` and `.fbx`, plus Unity `Pump.fbx`, were updated. The pre-repair versions are under `original/before_port_surface_repair_20260929`. Unity Editor import and in-game appearance still need verification.

## Upper-box missing surfaces after Unity re-export

- The later Unity FBX export dropped three complex source polygons: two beside the upper actuator box and one at the side neck. The source FBX remained closed, while the Unity FBX had 42 boundary edges and 36 fewer triangles.
- `repair_upper_box_mesh.py --repair` matches source and Unity vertices, triangulates just those three missing surfaces in 3D, and appends their UV, normal and tangent data to the Unity FBX. Its default mode validates the FBX and reports remaining gaps.
- The repaired Unity FBX has 4242 vertices, 8404 triangles, zero boundary edges and zero nonmanifold edges. Existing vertex positions, object links, texture, material and prefab data are unchanged.
- The editable `.blend` still contains the original complex polygons. Repeating the Unity FBX export may drop them again; rerun the repair script after export. Unity Editor reimport and visual appearance were not checked.
- Follow-up UV correction: the first fill sampled a single brown or light atlas pixel for each missing polygon, which made the new surfaces visibly inconsistent. The repair script now maps those 36 triangles across dark steel islands in the installed 512 px atlas, preserving the source polygon's relative UV layout. Existing face UVs and the texture image remain untouched. Sampled new-face colors stay in a dark gray range instead of crossing the gold atlas strips.
