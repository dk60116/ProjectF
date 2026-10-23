$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assets = Join-Path $repo 'FactorioProject/Assets'
function Read([string]$path) { [IO.File]::ReadAllText((Join-Path $assets $path)) }
function GuidAt([string]$path) { [regex]::Match((Read ($path + '.meta')), '(?m)^guid: ([a-f0-9]{32})').Groups[1].Value }
function Require([bool]$value, [string]$message) { if (!$value) { throw $message } }
$itemPath = 'Data/Items/Item_141_Fast Conveyor belt.asset'
$item = Read $itemPath
$itemGuid = GuidAt $itemPath
$itemId = [regex]::Match($item, '(?m)^  id: (\d+)').Groups[1].Value
Require ($itemId -ne '' -and $item.Contains('itemName: Fast Conveyor belt')) 'Fast belt identity is missing.'
$ids = @{}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $assets 'Data/Items') -Filter '*.asset' -File) {
    $match = [regex]::Match([IO.File]::ReadAllText($file.FullName), '(?m)^  id: (\d+)')
    if (!$match.Success) { continue }
    $id = $match.Groups[1].Value
    Require (!$ids.ContainsKey($id)) "Duplicate ItemDefinition id $id"
    $ids[$id] = $file.Name
}
$folder = 'MapObject/Belt/Fast Conveyor belt'
$prefabs = @('Fast Conveyor belt.prefab','Fast Conveyor belt_Corner.prefab','Fast Conveyor belt_Corner_Reverse.prefab')
$baseNames = @('Conveyor belt.prefab','Conveyor belt_Corner.prefab','Conveyor belt_Corner_Reverse.prefab')
$family = @{}
for ($i = 0; $i -lt 3; $i++) { $family[@('straightVariantPrefab','cornerVariantPrefab','reverseCornerVariantPrefab')[$i]] = GuidAt ($folder + '/' + $prefabs[$i]) }
Require ($item.Contains('guid: ' + $family.straightVariantPrefab)) 'Item must spawn the fast straight prefab.'
for ($i = 0; $i -lt 3; $i++) {
    $prefab = Read ($folder + '/' + $prefabs[$i])
    $basePath = 'MapObject/Belt/Conveyor belt/' + $baseNames[$i]
    $baseGuid = GuidAt $basePath
    $source = Read $basePath
    Require ($prefab.Contains('m_SourcePrefab: {fileID: 100100000, guid: ' + $baseGuid)) 'Fast prefabs must inherit existing geometry and behavior.'
    $speed = [regex]::Match($prefab,'propertyPath: conveyorSpeed\r?\n      value: ([0-9.]+)').Groups[1].Value
    $baseSpeed = [regex]::Match($source,'(?m)^  conveyorSpeed: ([0-9.]+)').Groups[1].Value
    Require ([double]$speed -eq [double]$baseSpeed * 2) 'All fast orientations must move at twice the normal speed.'
    Require ($prefab.Contains('guid: ' + $itemGuid)) 'All orientations must retain fast item identity for drops and saves.'
    foreach ($property in $family.Keys) {
        $match = [regex]::Match($prefab,'propertyPath: ' + $property + '\r?\n      value: *\r?\n      objectReference: \{fileID: 7247929509108097568, guid: ([a-f0-9]{32})')
        Require ($match.Success -and $match.Groups[1].Value -eq $family[$property]) 'Corner resolution must stay in the fast family.'
    }
    foreach ($match in [regex]::Matches($prefab, '- target: \{fileID: (-?\d+), guid: ' + $baseGuid)) {
        Require ([regex]::IsMatch($source,'(?m)^--- !u!\d+ &' + $match.Groups[1].Value + '(?: |\r?$)')) 'Variant override targets must exist in the base prefab.'
    }
    $swaps = [regex]::Matches($prefab,'propertyPath: m_Materials.Array.data\[0\]')
    Require ($swaps.Count -eq $(if ($i -eq 0) { 1 } else { 3 })) 'Every fast belt top and corner seam must use its yellow material.'
}
foreach ($name in @('M_FastBeltTop.mat','M_FastBeltTop_Corner.mat','M_FastBeltTop_Seam_Input.mat','M_FastBeltTop_Seam_Output.mat')) {
    $material = Read ($folder + '/' + $name)
    Require ($material.Contains('_ArrowColor: {r: 1, g: 0.8, b: 0, a: 1}') -and $material.Contains('_ArrowMask:')) 'Fast materials must mask only yellow arrows.'
}
$archetype = Read 'Data/MapObjectArchetypes/MapObjectArchetype_141_Fast Conveyor belt.asset'
Require ($archetype.Contains('itemId: ' + $itemId) -and $archetype.Contains('guid: ' + $family.straightVariantPrefab) -and $archetype.Contains('guid: ' + $itemGuid)) 'Cached rendering metadata must point at the new item and prefab.'
Require ($archetype.Contains('guid: ' + (GuidAt ($folder + '/M_FastBeltTop.mat')))) 'Cached render parts must retain yellow arrows.'
$manager = Read 'MapObject/Fluid/Steam generator/GameManager.prefab'
$scene = Read 'Scenes/GameScene.unity'
function CheckRegistry([string]$guid) {
    Require ($manager.Contains('guid: ' + $guid)) 'Prefab item registry is missing a fast belt.'
    $count = [regex]::Match($scene, 'itemDefinitions.Array.size\r?\n      value: (\d+)')
    $entry = [regex]::Matches($scene, "itemDefinitions.Array.data\[(\d+)\]'\r?\n      value: *\r?\n      objectReference: \{fileID: 11400000, guid: " + $guid)
    Require ($count.Success -and $entry.Count -eq 1 -and [int]$entry[0].Groups[1].Value -lt [int]$count.Groups[1].Value) 'Active scene must register each fast belt exactly once within the array.'
}
CheckRegistry $itemGuid
. (Join-Path $repo 'Tools/CraftingTreeRecovery/Inspect.ps1') -FunctionsOnly
$bytes = [IO.File]::ReadAllBytes((Join-Path $assets 'Data/CraftingTree/crafting_tree.bytes'))
$tree = Read-CraftingTree $bytes
$recipe = @($tree.Recipes | Where-Object Name -eq 'Fast Conveyor belt')
Require ($recipe.Count -eq 1 -and $recipe[0].Output -eq 1 -and $recipe[0].Machines.Count -eq 1 -and $recipe[0].Machines[0] -eq 'Workbench') 'Fast belt workbench recipe is missing.'
# Base fast-belt costs remain designer-owned; verify their references rather than freezing the old gear recipe.
foreach ($ingredient in $recipe[0].Ingredients) {
    Require ($ingredient.Amount -gt 0 -and @($tree.Recipes | Where-Object Name -eq $ingredient.Name).Count -eq 1) 'Fast recipe ingredients must resolve in the crafting tree.'
}
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $assets 'Data/CraftingTree/crafting_tree.bytes')).Hash
$resourceHash = (Get-FileHash -LiteralPath (Join-Path $assets 'Resources/Data/CraftingTree/crafting_tree.bytes')).Hash
Require ($sourceHash -eq $resourceHash) 'Packaged recipe data differs from its binary source.'
Add-Type -AssemblyName System.Drawing
function CheckIcon([string]$definition, [string]$iconPath) {
    $icon = [Drawing.Bitmap]::new((Join-Path $assets $iconPath))
    try {
    Require ($icon.Width -eq 256 -and $icon.Height -eq 256 -and $icon.GetPixel(0,0).A -eq 0) 'Fast icon must be a transparent 256px sprite.'
    $yellow = 0
    for ($y = 0; $y -lt 256; $y++) { for ($x = 0; $x -lt 256; $x++) { $pixel = $icon.GetPixel($x,$y); if ($pixel.A -gt 200 -and $pixel.R -gt 180 -and $pixel.G -gt 130 -and $pixel.B -lt 100) { $yellow++ } } }
    Require ($yellow -gt 400) 'Inventory icon must show the yellow arrows.'
    }
    finally { $icon.Dispose() }
    Require ($definition.Contains('guid: ' + (GuidAt $iconPath))) 'Fast item must use its yellow icon.'
}
CheckIcon $item 'Items/Belt/Fast Conveyor belt/Fast Conveyor belt_Icon.png'

$twoFPath = 'Data/Items/Item_142_Fast Conveyor belt 2F.asset'
$twoF = Read $twoFPath
$twoFGuid = GuidAt $twoFPath
$twoFId = [regex]::Match($twoF, '(?m)^  id: (\d+)').Groups[1].Value
Require ($twoFId -ne '' -and $twoF.Contains('itemName: Fast Conveyor belt 2F')) 'Fast 2F identity is missing.'
CheckRegistry $twoFGuid
$basePath = 'MapObject/Belt/Conveyor belt 2F/Conveyor belt 2F.prefab'
$twoFPrefabPath = 'MapObject/Belt/Fast Conveyor belt 2F/Fast Conveyor belt 2F.prefab'
$baseGuid = GuidAt $basePath
$source = Read $basePath
$prefab = Read $twoFPrefabPath
$twoFPrefabGuid = GuidAt $twoFPrefabPath
$topGuid = GuidAt ($folder + '/M_FastBeltTop.mat')
Require ($twoF.Contains('fileID: 4923497655988066394, guid: ' + $twoFPrefabGuid)) '2F item must spawn its derived 2F component.'
Require ($prefab.Contains('m_SourcePrefab: {fileID: 100100000, guid: ' + $baseGuid) -and $prefab.Contains('guid: ' + $twoFGuid)) 'Fast 2F must inherit the slope and retain its own item identity.'
$speed = [regex]::Match($prefab,'propertyPath: conveyorSpeed\r?\n      value: ([0-9.]+)').Groups[1].Value
$baseSpeed = [regex]::Match($source,'(?m)^  conveyorSpeed: ([0-9.]+)').Groups[1].Value
Require ([double]$speed -eq 2 * [double]$baseSpeed) 'Fast 2F must move at twice the base 2F speed.'
Require ($prefab.Contains('guid: 7016f55366c82f1498026434646b80a2') -and !$prefab.Contains('propertyPath: mapStatus') -and !$prefab.Contains('propertyPath: m_Mesh')) '2F must retain the inherited runtime type, footprint and meshes.'
$surfaceOverrides = [regex]::Matches($prefab,'propertyPath: m_Materials.Array.data\[0\]\r?\n      value: *\r?\n      objectReference: \{fileID: 2100000, guid: ' + $topGuid)
Require ($surfaceOverrides.Count -eq 3) 'Fast 2F top and both seams must reuse the yellow fast material.'
foreach ($match in [regex]::Matches($prefab, '- target: \{fileID: (-?\d+), guid: ' + $baseGuid)) {
    Require ([regex]::IsMatch($source,'(?m)^--- !u!\d+ &' + $match.Groups[1].Value + '(?: |\r?$)')) 'Fast 2F override target is missing in its source.'
}
$twoFArchetype = Read 'Data/MapObjectArchetypes/MapObjectArchetype_142_Fast Conveyor belt 2F.asset'
$baseArchetype = Read 'Data/MapObjectArchetypes/MapObjectArchetype_45_Conveyor belt 2F.asset'
Require ($twoFArchetype.Contains('itemId: ' + $twoFId) -and $twoFArchetype.Contains('guid: ' + $twoFGuid) -and $twoFArchetype.Contains('guid: ' + $twoFPrefabGuid)) 'Fast 2F cached identity must match ItemDefinition.'
Require ($twoF.Contains('guid: ' + (GuidAt 'Data/MapObjectArchetypes/MapObjectArchetype_142_Fast Conveyor belt 2F.asset'))) 'Fast 2F item must use its render cache.'
$footprintPattern = '(?ms)^  mapStatus:\r?\n(.*?)(?=^  \w)'
Require ([regex]::Match($twoFArchetype,$footprintPattern).Value -eq [regex]::Match($baseArchetype,$footprintPattern).Value) 'Fast 2F cache must retain the three-cell footprint.'
Require ($twoFArchetype.Contains('sourceRuntimeType: ConvayorBelt2F,') -and $twoFArchetype.Contains('guid: ' + $topGuid) -and !$twoFArchetype.Contains('guid: ' + (GuidAt 'MapObject/Belt/Conveyor belt/M_BeltTop.mat'))) 'Fast 2F cache must retain the 2F type and replace every base top material.'
$recipe2F = @($tree.Recipes | Where-Object Name -eq 'Fast Conveyor belt 2F')
Require ($recipe2F.Count -eq 1 -and $recipe2F[0].Output -eq 1 -and $recipe2F[0].Machines.Count -eq 1 -and $recipe2F[0].Machines[0] -eq 'Workbench' -and $recipe2F[0].Ingredients.Count -eq 1 -and $recipe2F[0].Ingredients[0].Name -eq 'Fast Conveyor belt' -and $recipe2F[0].Ingredients[0].Amount -eq 3) 'Fast 2F recipe must convert three fast belts into one slope at a workbench.'
CheckIcon $twoF 'Items/Belt/Fast Conveyor belt 2F/Fast Conveyor belt 2F_Icon.png'
Write-Output 'PASS fast belt family + 2F: unique IDs, inheritance/footprint, 2x speed, yellow top/seams/cache/icons, registries and binary recipes'
