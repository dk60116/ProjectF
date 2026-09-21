# UtilityPole_P

Portable wooden utility pole: tapered post, crossbar, two diagonal braces, three porcelain insulators and an iron bolt.

- 120 serialized vertices (including UV/normal splits), 156 triangles, one submesh.
- Y-up, bottom origin. Dedicated wood/porcelain/iron UV atlas.
- Main post thickness increased by 40%: base radius 0.0238, top radius 0.0175; height, crossbar, insulators and UVs unchanged.
- Assets: `Assets/Items/Electro/Utility pole/UtilityPole_P.mesh`, `UtilityPole_P_TB.png`, `M_UtilityPole_P.mat`.
- Bound to item ID 37 via `Assets/Data/Items/Item_39_Utility pole.asset`; installed pole assets are unchanged.
- Shader: `Custom/ToonCharacter`, parent `M_Object`, white tint, specular disabled.
- Editable source: `UtilityPole_P.obj`; generator: `Generate-UtilityPolePortable.py`, shared exporter: `portable_mesh.py`.

Run the generator with `--check` to verify vertex budget, nondegenerate faces/UVs, outward normals, closed welded components and exact serialized buffers. See the script's command-line options for regeneration and an offline textured preview.

Preview: `Previews/UtilityPole_P.png`. This is an offline mesh/texture preview, not an in-game capture. Unity import and in-game appearance have not been tested.

Texture generation prompt: `UtilityPole_P_TexturePrompt.md`.
