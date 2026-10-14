# Slime Portraits

## Assets

- Tool: built-in `image_gen.imagegen`.
- Model reference: `C:/Users/dk601/AppData/Local/Temp/codex-clipboard-c786f401-8553-4e42-b309-7120938b1f59.png`.
- Art style reference: `FactorioProject/Assets/Monsters/Portraits/Bat_Green.png`.
- Blue is a new painted illustration. Green, Red and Yellow edit the generated Blue image, preserving anatomy, pose and black eyes.
- Four transparent square PNGs: `FactorioProject/Assets/Monsters/Portraits/Slime_{Blue,Green,Red,Yellow}.png`.
- Assigned to the four existing icons in `Assets/Monsters/Slime/Monster_Slime.asset`; no stats, prefabs or variant IDs changed.
- Single full-rect Sprite, centered pivot, sRGB, bilinear/clamp, mipmaps off, NPOT scaling off, alpha transparency enabled, uncompressed and GPU size capped at 512.
- No raster post-processing; generated source files remain unchanged.
- File-level bindings, PNG geometry and importer settings are checked by `Run.ps1`; live Unity import and UI rendering are not verified.

## Prompts

### Blue

```text
Use case: stylized-concept. Asset type: ONE transparent square fantasy game SLIME portrait icon, BLUE variant. Reference 1 is the EXACT slime game model anatomy, preserve that character silhouette. Reference 2 is ONLY the illustrated Bat icon's art style: matching dark outline, painted cel shadows and restrained brush texture, do NOT borrow any bat anatomy. Paint a NEW 2D illustration of the reference slime: a squat, compact, rounded dome of blue-teal slime, with two large round glossy BLACK bead-like eyes and tiny white eye glints. Rounded top narrows slightly above the eyes, base spreads into several short blunt scalloped ooze lobes, with two short protruding side lobes. Under the eyes there is just a small dark wavy crease above the rippling scalloped base. It has NO big open mouth, NO stone mouth rim, NO teeth, NO fangs, NO nose, NO ears, NO neck, NO separate chest, NO arms, NO legs, NO tentacles, NO clothes, NO horns. Soft thick slime skin, subtle irregular folds and goo ridges, NOT a transparent water balloon, no embedded objects. Blue palette: ocean cyan and deep turquoise blue, brighter restrained turquoise face highlights, dark teal underside shadows. Frontal face tilted slightly like reference 1, both eyes fully visible; calm odd little monster, no added smiling lips. Crisp fantasy RPG inventory illustration, broad simple shapes readable at 64px, dark charcoal outer contour. Show the ENTIRE creature centered, fill about 76 percent of square height, all base lobes complete with clear transparent padding, no cropping. Genuine transparent background, no gray model-viewer backdrop, no painted checkerboard, no ground shadow, no scenery, no frame, no circle, no letters, no text, no UI, no watermark. NOT a 3D render, NOT a realistic octopus. One creature only.
```

### Green

```text
Use case: precise-object-edit. Make the GREEN color variant of this exact illustrated slime game portrait. Change ONLY the blue-teal skin palette into fresh emerald/leaf green, lighter yellow-green painted highlights and deep forest-green underside shadows. Keep the exact dome-shaped body, folded scalloped base, short side ooze lobes, two glossy BLACK round eyes with unchanged white glints, dark wavy crease below the eyes, charcoal outline, brush texture, shading shapes, frontal tilt, scale, composition and genuine transparent alpha. Do not change eye color, invent teeth or stone lips, add a nose, limbs, clothing, ears, ornaments, glow, puddles or scenery. No frame, no badge, no text, no watermark, no cropping or enlargement. One square matched fantasy RPG hand-painted portrait icon, same creature and exact pose.
```

### Red

```text
Use case: precise-object-edit. Make the RED color variant of this exact illustrated slime game portrait. Change ONLY the blue-teal skin palette into rich crimson/coral red, lighter warm coral painted highlights and deep burgundy underside shadows. Keep the exact dome-shaped body, folded scalloped base, short side ooze lobes, two glossy BLACK round eyes with unchanged white glints, dark wavy crease below the eyes, charcoal outline, brush texture, shading shapes, frontal tilt, scale, composition and genuine transparent alpha. Do not change eye color, invent teeth or stone lips, add a nose, limbs, clothing, ears, ornaments, glow, puddles or scenery. No frame, no badge, no text, no watermark, no cropping or enlargement. One square matched fantasy RPG hand-painted portrait icon, same creature and exact pose.
```

### Yellow

```text
Use case: precise-object-edit. Make the YELLOW color variant of this exact illustrated slime game portrait. Change ONLY the blue-teal skin palette into golden yellow, pale lemon painted highlights and deep ochre/mustard underside shadows. Keep the exact dome-shaped body, folded scalloped base, short side ooze lobes, two glossy BLACK round eyes with unchanged white glints, dark wavy crease below the eyes, charcoal outline, brush texture, shading shapes, frontal tilt, scale, composition and genuine transparent alpha. Do not change eye color, invent teeth or stone lips, add a nose, limbs, clothing, ears, ornaments, glow, puddles or scenery. No frame, no badge, no text, no watermark, no cropping or enlargement. One square matched fantasy RPG hand-painted portrait icon, same creature and exact pose.
```
