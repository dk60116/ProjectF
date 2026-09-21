# CampFire_P

- Portable campfire: six irregular stones and three crossed, five-sided logs.
- **120 runtime vertices / 144 triangles / 1 submesh** including cap normal splits.
- Y-up, meters, base centered at `(0, 0, 0)`.
- Dedicated `CampFire_P_TB.png` atlas: bark in the left half, stone in the upper
  right quadrant and end-grain in the lower right quadrant (image coordinates).
- `M_CampFire_P.mat` uses the existing ToonCharacter shader with specular disabled.
- Runtime import limit: 1024 pixels, sRGB, mipmaps, trilinear filtering, clamp wrap.
- Native asset: `FactorioProject/Assets/Items/InputOutputModule/Camp fire/CampFire_P.mesh`.
- Bound to item id 29 (`Camp fire`). Its existing asset filename is
  `Item_53_Rail handcar.asset`; the filename does not match its current contents.
- `CampFire_P.obj` is the editable geometry copy; it shares vertex, normal and UV indices.
- `Previews/CampFire_P.png` is an offline rasterization of this mesh and its atlas,
  not a Unity screenshot. The source atlas was generated with built-in imagegen;
  its exact prompt is recorded in `CampFire_P_TexturePrompt.md`.

Regenerate: `python Tools/Modeling/Generate-CampFirePortable.py`

Validate serialized buffers without edits:
`python Tools/Modeling/Generate-CampFirePortable.py --check`

Validation checks the actual Unity vertex count, index bounds, serialized normals,
UV range, degenerate faces, outward winding and closed components after welding
only the coincident UV/normal splits. No application is launched by the script.
