# Silicon Portable Material

`Silicon_P.mesh` is a thin semiconductor wafer with a dedicated 2048 x 2048 albedo atlas.

## Atlas layout

- Top-left quadrant: grid-cut monocrystalline silicon with restrained cyan, violet, and gold iridescence.
- Top-right quadrant: dark underside.
- Bottom half: wrap-safe blue graphite side wall.

The mesh UV layout expects these regions and is validated by vertex count, triangle count, and three anchor UVs.

## Material

- Shader: Universal Render Pipeline/Lit, with Standard as fallback.
- Metallic: 0.16.
- Smoothness: 0.72.
- GPU instancing: enabled.
- Texture: sRGB, bilinear, clamp, mipmaps enabled, high-quality compression.

## Generate and validate

1. Run `Tools/ProjectF/Generate Silicon Portable Material`.
2. Run `Tools/ProjectF/Validation/Silicon Portable Material`.

Generation assigns the mesh and material when a `Silicon` ItemDefinition exists. The assets remain available without assignment while the definition is absent.
