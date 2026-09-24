param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$binaryPath = Join-Path $RepositoryRoot 'FactorioProject\Assets\Data\CraftingTree\crafting_tree.bytes'
if (-not (Test-Path -LiteralPath $binaryPath)) {
    throw "crafting_tree.bytes를 찾을 수 없음: $binaryPath"
}

$stream = [System.IO.File]::OpenRead($binaryPath)
$reader = [System.IO.BinaryReader]::new($stream, [System.Text.Encoding]::UTF8, $false)
try {
    $version = $reader.ReadInt32()
    if ($version -ne 5) {
        throw "지원하지 않는 제작 트리 버전: $version"
    }

    $recipeCount = $reader.ReadInt32()
    $recipeCountsByMapObject = @{}
    $recipeNamesByMapObject = @{}
    $ingredientsByItem = @{}
    for ($recipeIndex = 0; $recipeIndex -lt $recipeCount; $recipeIndex++) {
        $itemName = $reader.ReadString()
        $mapObjectCount = $reader.ReadInt32()
        for ($mapIndex = 0; $mapIndex -lt $mapObjectCount; $mapIndex++) {
            $mapObjectName = $reader.ReadString()
            if (-not $recipeCountsByMapObject.ContainsKey($mapObjectName)) {
                $recipeCountsByMapObject[$mapObjectName] = 0
                $recipeNamesByMapObject[$mapObjectName] = [System.Collections.Generic.HashSet[string]]::new()
            }

            $recipeCountsByMapObject[$mapObjectName]++
            $null = $recipeNamesByMapObject[$mapObjectName].Add($itemName)
        }

        $null = $reader.ReadInt32()
        $ingredientCount = $reader.ReadInt32()
        $ingredientNames = [System.Collections.Generic.List[string]]::new()
        for ($ingredientIndex = 0; $ingredientIndex -lt $ingredientCount; $ingredientIndex++) {
            $ingredientNames.Add($reader.ReadString())
            $null = $reader.ReadInt32()
        }

        $ingredientsByItem[$itemName] = $ingredientNames
    }
}
finally {
    $reader.Dispose()
}

$requiredWorkables = @('Workbench', 'Anvil')
foreach ($workableName in $requiredWorkables) {
    $count = if ($recipeCountsByMapObject.ContainsKey($workableName)) {
        [int]$recipeCountsByMapObject[$workableName]
    }
    else {
        0
    }

    if ($count -le 0) {
        throw "$workableName 제작 레시피가 없음"
    }

    Write-Output "$workableName recipes: $count"
}

$combinedRecipes = [System.Collections.Generic.HashSet[string]]::new()
$largestIndividualRecipeCount = 0
foreach ($workableName in $requiredWorkables) {
    $largestIndividualRecipeCount = [Math]::Max(
        $largestIndividualRecipeCount,
        $recipeNamesByMapObject[$workableName].Count)
    $combinedRecipes.UnionWith($recipeNamesByMapObject[$workableName])
}

if ($combinedRecipes.Count -le $largestIndividualRecipeCount) {
    throw '겹친 Workable 종류의 레시피 합집합이 확장되지 않음'
}

Write-Output "Combined Workbench + Anvil recipes: $($combinedRecipes.Count)"

$recursiveRecipeCount = 0
foreach ($recipe in $ingredientsByItem.GetEnumerator()) {
    foreach ($ingredientName in $recipe.Value) {
        if (($ingredientsByItem.ContainsKey($ingredientName)) -and
            ($ingredientsByItem[$ingredientName].Count -gt 0)) {
            $recursiveRecipeCount++
            break
        }
    }
}

if ($recursiveRecipeCount -le 0) {
    throw '하위 제작 레시피를 가진 제작 항목이 없음'
}

Write-Output "Recipes with craftable sub-items: $recursiveRecipeCount"

Write-Output 'Workable crafting binary validation passed.'
