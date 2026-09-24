# Quartz portable model

`QuartzPortableAssetGenerator` creates the portable Quartz item assets directly in Unity:

- `Assets/Items/Ore/Quartz/Quartz_P.mesh`
- `Assets/Items/Ore/Quartz/Quartz_P_TB.png`
- `Assets/Items/Ore/Quartz/M_Quartz_P.mat`

Run `Tools > ProjectF > Generate Quartz Portable Model` to regenerate them. Run
`Tools > ProjectF > Validation > Quartz Portable Model` to validate the generated assets.

The mesh places one tall rear crystal and two shorter front crystals in a compact triangular
cluster with flat, separately shaded facets. It uses 91 vertices and
39 triangles, staying below the requested 100-vertex limit. The eight-color palette texture uses
milky white, cool gray, and pale lavender values matching the Quartz inventory icon.

If a `Quartz` ItemDefinition exists, regeneration assigns the mesh and material automatically.
When it does not exist, the assets are still generated and validated for later assignment.
