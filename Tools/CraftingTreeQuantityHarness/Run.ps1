$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assets = Join-Path $repo 'FactorioProject/Assets'
function Member([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $brace = $source.IndexOf('{', $start)
    $semicolon = $source.IndexOf(';', $start)
    if ($semicolon -ge 0 -and $semicolon -lt $brace) { return $source.Substring($start, $semicolon-$start+1) }
    $depth = 1; $end = $brace+1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end-$start)
}
function Constants([string]$source) {
    ([regex]::Matches($source, 'private const int \w+CraftingTreeFileVersion = [^;]+;|private const int MultiCraftingMapObjectGuidFileVersion = [^;]+;') | ForEach-Object { $_.Value }) -join "`n"
}
$generated = "using System; using System.IO; using System.Collections.Generic; using ProjectF.Crafting; using UnityEngine; using UnityEditor;`n"
$editor = [IO.File]::ReadAllText((Join-Path $assets 'Editor/CraftingTreeEditorWindow.cs'))
$generated += "public partial class EditorProbe {`n" + (Constants $editor) + "`n"
foreach ($signature in @('private struct IngredientEntry', 'private class CraftingTreeJsonEntry', 'private class CraftingIngredientJsonEntry', 'private class CraftingMapObjectJsonEntry',
    'private void WriteCraftingTree(', 'private float GetOutputCount(', 'private float GetEditableOutputCount(', 'private static float DrawPositiveCountField(',
    'private static ItemDefinition FindDefinitionById(', 'private static string GetPersistedItemName(')) {
    $generated += (Member $editor $signature) + "`n"
}
$generated += "} public static partial class RemapProbe {`n"
$remap = [IO.File]::ReadAllText((Join-Path $assets 'Editor/CraftingTreeItemIdRemapper.cs'))
$generated += (Constants $remap) + "`n"
foreach ($signature in @('private struct BinaryIngredientEntry', 'private sealed class BinaryRecipeEntry', 'private sealed class DefinitionIdentity',
    'private static bool TryReadCurrentBinaryFile(', 'private static int ReadItemId(', 'private static void WriteCurrentBinaryFile(',
    'private static string GetRequiredItemName(', 'private static void EnsureParentFolder(', 'private static ItemDefinition FindDefinitionById(')) {
    $generated += (Member $remap $signature) + "`n"
}
$generated += "} public static partial class AutoFillProbe {`n"
$auto = [IO.File]::ReadAllText((Join-Path $assets 'Editor/ProductionMachineRecipeAutoFill.cs'))
$generated += (Constants $auto) + "`n"
foreach ($signature in @('private sealed class CraftingTreeJsonEntry', 'private sealed class CraftingIngredientJsonEntry', 'private sealed class CraftingMapObjectJsonEntry',
    'private sealed class RecipeEntry', 'private static string GetDefinitionDisplayName(')) {
    $generated += (Member $auto $signature) + "`n"
}
$start = $auto.IndexOf('private static bool TryLoadCraftingTreeBytes(', [StringComparison]::Ordinal)
$end = $auto.IndexOf('private static CraftingTreeJsonFile LoadCraftingTreeJson(', $start, [StringComparison]::Ordinal)
$generated += $auto.Substring($start, $end-$start) + "`n}"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-CraftingQuantity-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
Set-Content -LiteralPath (Join-Path $probe 'EditorProbes.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $assets 'Scripts/Manager/CraftingTreeRuntime.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $assets 'Scripts/Manager/CraftingTreeQuantity.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Set-Content -LiteralPath (Join-Path $probe 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj') -- (Join-Path $assets 'Data/CraftingTree/crafting_tree.bytes')
exit $LASTEXITCODE
