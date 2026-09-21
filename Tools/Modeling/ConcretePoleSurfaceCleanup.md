# Concrete pole surface cleanup

Final Blender file: `InstalledUtilityPoles_Clean.blend`. Visible final mesh: `Concrete Utility pole_SurfaceClean`; earlier concrete meshes are hidden references.

The preceding rebake preserved defects in the source artwork and retained very thin shaft UV islands. This pass cleans the surface itself using material colors measured from the original: warm grey concrete, medium grey steel, ivory porcelain and muted yellow/charcoal warning paint. Removes baked vertical streaks and false black boundary marks; regenerates continuous diagonal warning stripes using object-space coordinates. No wood or portable asset is changed.

The shaft's 28 side triangles now use a spacious cylindrical UV strip. Caps and fittings have separate packed islands. The final 2048px albedo is baked with 12px edge extension into the original texture path/GUID. FBX changes remain restricted to UV and tangent-space arrays; all 377 vertices, 628 triangles, normals, transforms and mesh IDs are retained. No extra runtime material slots or procedural shaders are needed.

Validation: zero overlapping interior pixels at 512px test resolution, zero triangles under 2px UV area at 2048, minimum atlas border 61px; results in `UVRepair/ConcreteSurfaceValidation.json`. `CheckPoleUVRepair.py` passes. Inspected the actual baked-texture preview up close in Blender; Unity reimport/in-game rendering remains untested.

Reproduction: on the preceding comparison scene, `CleanConcretePoleSurface.py` -> `build_and_bake()`. The shared `RepairInstalledPoleUV.py` exporter accepts a name-to-object mapping for the concrete-only FBX patch. Repainting intentionally changes the damaged source pixel pattern while retaining its measured material palette; prior albedo identity metrics do not apply to this cleaned version.
