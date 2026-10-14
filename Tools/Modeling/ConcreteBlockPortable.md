# Concrete block portable texture

- Mesh: `Assets/Items/Plate/Concrete block/Concrete block_P.mesh` (unchanged).
- Albedo: `Concrete block_P_TB.png`, opaque generated concrete surface.
- Material: `M_Concrete block_P.mat`, matching the existing `M_Object` variants.
- Item: `Item_131_Concrete block.asset`, dedicated `portableMat` binding.
- Texture import: sRGB, mipmaps, 512px cap; no runtime shader changes.

Built-in image edit prompt: enlarge aggregate roughly 6-8 times in linear scale;
large dispersed charcoal-gray and pale mineral chips in neutral light-gray cement.
Keep uniform diffuse albedo, no lighting gradients, holes, outlines, cracks or text.
The existing six UV islands sample the same material without altering geometry.

```powershell
py -3.13 Tools/Modeling/CheckConcreteBlockPortable.py --preview
```

Requires Pillow. Checks serialized mesh buffers, closed geometry, UVs, texture
opacity, material/texture/item GUID links, and importer settings. Reuses
`portable_mesh.py` to render the actual mesh and texture, not an Editor screenshot.
Preview lighting is approximate; final Unity rendering is not verified by this tool.
