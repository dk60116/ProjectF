# Bat Portraits

Generated with the built-in image generation tool, not an Editor render.

## Source And Scope

- Anatomy reference: the user-supplied model image at `C:/Users/dk601/AppData/Local/Temp/codex-clipboard-814d38f4-9134-4a28-aea7-2e8ca6c2fa0c.png`.
- Four variants only: Green, Red, Violet, Yellow. Ghost, Rabbit and Slime portraits are deferred.
- Spherical head/body, attached raised wings, round eyes, short snout and two small fangs. No large ears, chest or ruff.
- Native outputs are 1254 x 1254 RGBA PNGs with genuine transparency. No raster postprocessing was used.
- Unity imports as single full-rect Sprite, centered pivot, sRGB, clamp, no mipmaps, no compression, 512 px GPU limit, no generated physics shape.
- `Assets/Monsters/Bat/Monster_Bat.asset` maps each color to its Sprite GUID. Gameplay stats, prefabs, meshes, materials and animations are unchanged.
- The earlier unbound Green prototype was replaced. Its generated original remains in Codex's generated-images directory.

## Outputs

- `Assets/Monsters/Portraits/Bat_Green.png` (source: `C:/Users/dk601/.codex/generated_images/01a10a97-2cf4-77c2-866c-80302ecfb2ff/exec-88a90bfd-7f03-4464-be11-fda30b5cd8d0.png`).
- `Assets/Monsters/Portraits/Bat_Red.png` (source: `C:/Users/dk601/.codex/generated_images/01a10a97-2cf4-77c2-866c-80302ecfb2ff/exec-d28cac8c-3b20-4389-bfef-6d82cef2ee63.png`).
- `Assets/Monsters/Portraits/Bat_Violet.png` (source: `C:/Users/dk601/.codex/generated_images/01a10a97-2cf4-77c2-866c-80302ecfb2ff/exec-d5bb8251-9334-483d-afb3-6b14de2c32f2.png`).
- `Assets/Monsters/Portraits/Bat_Yellow.png` (source: `C:/Users/dk601/.codex/generated_images/01a10a97-2cf4-77c2-866c-80302ecfb2ff/exec-22fe227c-c9dc-4871-a5f4-90d698475dab.png`).

## Prompts

### Green

Use case: stylized-concept. Asset type: ONE transparent square fantasy game monster portrait icon, GREEN BAT variant. The attached screenshot is the EXACT character anatomy reference, not a style reference: translate this actual game monster into a new crisp hand-painted 2D illustration. It is a small round floating bat monster consisting of ONE compact spherical head/body and TWO angular bat wings attached directly to its sides. IMPORTANT: NO large ears, NO pointed ears, NO horns, NO neck, NO shoulders, NO chest, NO shaggy chest ruff, NO limbs, NO clothing. Its rounded forehead is smooth with a few leathery creases, round glossy dark violet eyes with tiny white highlights, a short pig-like dark snout with two nostrils and TWO small ivory fang tips, one at each side of the mouth. Keep the source anatomy and pose: front-facing spherical head in the middle, wings lifted upward on either side into two angular triangular peaks, folded leathery wing membranes extending down at their outer tips. Wings are real attached wings, not ears. Green variant palette: deep forest-green head and wing ribs, desaturated teal/navy-green membranes with jade green highlights, no glowing aura. Fantasy RPG inventory illustration, thick dark charcoal outer outline, clean painted cel shading and restrained brush texture, readable slightly mischievous expression. Face must remain clear, not lost in shadows. Center the ENTIRE creature, keep both wing tips complete and 8 percent genuinely transparent padding on every side. Fill most of the square width; compact silhouette with balanced negative space above and below. Transparent background, no gray screenshot backdrop, no checkerboard painted into the image, no frame, no badge, no text, no UI, no scenery, no 3D render, no extra objects. One character only.

### Red

Use case: precise-object-edit. Make the RED color variant of this exact illustrated bat monster portrait. Change ONLY colors; preserve the exact spherical head/body, short pig snout, two fang tips, round eye sizes and placement, wing attachment, raised triangular wing peaks, folded angular wings, outline, brush texture, frontal pose, scale, framing and transparent canvas. Recolor the green head and wing ribs to dark warm charcoal-brown with burgundy highlights. Recolor the blue/teal wing membranes to muted crimson/rose-red with clear red painted highlights. Eye irises become deep burgundy with the same small white highlights; fangs remain ivory, muzzle warm gray. Do not add ears, horns, neck, chest, ruff, arms, legs, clothing or background. No glow, no frame, no lettering, no text, no watermark. Keep this exact character and genuine alpha transparency, no cropping or enlargement. One square matched game icon.

### Violet

Use case: precise-object-edit. Make the VIOLET color variant of this exact illustrated bat monster portrait. Change ONLY the palette. Preserve precisely the spherical head/body, smooth forehead creases, short pig snout, two small ivory fang tips, round eye size and placement, attached wings with two raised triangular peaks, folded angular wing membranes, outline, brush texture, frontal pose, proportions, framing and genuine transparent alpha. Head and wing ribs stay very dark forest green/charcoal with muted violet rim highlights, as the game's violet variant has a dark green face. Recolor all blue/teal wing membranes to rich muted violet/lavender, with lavender highlights and deep plum shadows, visibly PURPLE overall. Eye irises become dark plum-violet with the same small white glints. Fangs remain ivory and snout neutral gray. No ears, horns, neck, chest, shaggy ruff, limbs, clothing, glow, text, frame, scene, background, watermark, crop or enlargement. One square matched fantasy RPG illustrated icon, same character and exact pose.

### Yellow

Use case: precise-object-edit. Make the YELLOW color variant of this exact illustrated bat monster portrait. Change ONLY the palette. Preserve precisely the spherical head/body, smooth forehead creases, short pig snout, two small ivory fang tips, round eye size and placement, attached wings with two raised triangular peaks, folded angular wing membranes, outline, brush texture, frontal pose, proportions, framing and genuine transparent alpha. Recolor green head and wing ribs to warm olive-golden brown with golden ochre highlights. Recolor blue/teal wing membranes to warm golden yellow, pale amber highlights and ochre shadows, visibly YELLOW overall, not red or orange. Eye irises become deep amber-brown with the same tiny white glints; fangs remain ivory and snout warm gray. No ears, horns, neck, chest, shaggy ruff, limbs, clothes, aura, text, frame, scene, background, watermark, crop or enlargement. One square matched fantasy RPG illustrated icon, same character and exact pose.
