$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'
$productionPath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/ProductionMachine.cs'
$editorPath = Join-Path $repo 'FactorioProject/Assets/Editor/ItemDataEditorWindow.cs'
$autoFillPath = Join-Path $repo 'FactorioProject/Assets/Editor/ProductionMachineRecipeAutoFill.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$productionSource = [IO.File]::ReadAllText($productionPath)
$editorSource = [IO.File]::ReadAllText($editorPath)
$autoFillSource = [IO.File]::ReadAllText($autoFillPath)

$treePath = Join-Path $repo 'FactorioProject/Assets/Data/CraftingTree/crafting_tree.bytes'
$treeStream = [IO.File]::OpenRead($treePath)
$treeReader = [IO.BinaryReader]::new($treeStream)
try {
    if ($treeReader.ReadInt32() -ne 5) { throw 'Unexpected crafting tree format.' }
    $recipeCount = $treeReader.ReadInt32()
    $expectedMk3InputCounts = [System.Collections.Generic.List[int]]::new()
    for ($recipeIndex = 0; $recipeIndex -lt $recipeCount; $recipeIndex++) {
        $null = $treeReader.ReadString()
        $mapCount = $treeReader.ReadInt32()
        $allowedMapObjects = $true
        $explicitMk3 = $false
        for ($mapIndex = 0; $mapIndex -lt $mapCount; $mapIndex++) {
            $mapName = $treeReader.ReadString()
            if ($mapName -notin @('Workbench', 'Anvil')) { $allowedMapObjects = $false }
            if ($mapName -eq 'Production machine (MK3)') { $explicitMk3 = $true }
        }
        $null = $treeReader.ReadInt32()
        $ingredientCount = $treeReader.ReadInt32()
        for ($ingredientIndex = 0; $ingredientIndex -lt $ingredientCount; $ingredientIndex++) {
            $null = $treeReader.ReadString()
            $null = $treeReader.ReadInt32()
        }
        if (($ingredientCount -eq 3 -and $allowedMapObjects) -or
            ($ingredientCount -ge 1 -and $ingredientCount -le 3 -and $explicitMk3)) {
            $expectedMk3InputCounts.Add($ingredientCount)
        }
    }
    if ($treeStream.Position -ne $treeStream.Length) { throw 'Crafting tree has unread recipe data.' }
} finally {
    $treeReader.Dispose()
    $treeStream.Dispose()
}
$mk3Path = Join-Path $repo 'FactorioProject/Assets/MapObject/InputOutputModule/Production machine (MK3)/Production machine (MK3).prefab'
$mk3Prefab = [IO.File]::ReadAllText($mk3Path)
$pairsStart = $mk3Prefab.IndexOf('  inputOutputPairs:', [StringComparison]::Ordinal)
if ($pairsStart -lt 0) { throw 'MK3 local IOPair section is missing.' }
$pairsEnd = $mk3Prefab.IndexOf('  inputList:', $pairsStart, [StringComparison]::Ordinal)
if ($pairsEnd -lt 0) { throw 'MK3 local IOPair section is missing.' }
$pairSections = [regex]::Split($mk3Prefab.Substring($pairsStart, $pairsEnd - $pairsStart), '(?m)^  - inputs:[ \t]*\r?$') |
    Where-Object { $_.Contains('    outputs:') }
if ($expectedMk3InputCounts.Count -lt 1 -or $pairSections.Count -ne $expectedMk3InputCounts.Count) {
    throw "MK3 local IOPair count $($pairSections.Count) differs from crafting tree count $($expectedMk3InputCounts.Count)."
}
$actualInputCounts = [System.Collections.Generic.List[int]]::new()
for ($pairIndex = 0; $pairIndex -lt $pairSections.Count; $pairIndex++) {
    $pairSection = $pairSections[$pairIndex]
    $outputsStart = $pairSection.IndexOf('    outputs:', [StringComparison]::Ordinal)
    $inputCount = [regex]::Matches($pairSection.Substring(0, $outputsStart), '(?m)^    - itemDefinition:').Count
    $outputCount = [regex]::Matches($pairSection.Substring($outputsStart), '(?m)^    - itemDefinition:').Count
    if ($outputCount -ne 1) {
        throw "MK3 local IOPair $pairIndex has an invalid input/output shape."
    }
    $actualInputCounts.Add($inputCount)
}
if ((($actualInputCounts | Sort-Object) -join ',') -ne (($expectedMk3InputCounts | Sort-Object) -join ',')) {
    throw 'MK3 local IOPair ingredient counts differ from crafting tree counts.'
}

function Read-Member([string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}

if ($autoFillSource.Contains('entry.ingredients[0]')) {
    throw 'ProductionMachine auto fill still drops ingredients after the first one.'
}
foreach ($required in @(
    'serializedMachine.FindProperty("inputOutputPairs")',
    'AddIoEntries(inputsProperty, recipe.inputs)',
    'AddIoEntries(outputsProperty, recipe.outputs)',
    'entry.ingredients.Count > requiredIngredientTypes',
    'IsCraftableByProductionMachine'
)) {
    if (!$autoFillSource.Contains($required)) { throw "Missing auto-fill integration: $required" }
}
foreach ($required in @(
    'TryGetConfiguredProductionIngredients',
    'pair.inputs.Count',
    'ResolveProductionTargetPairIndex'
)) {
    if (!$productionSource.Contains($required)) { throw "Missing ProductionMachine integration: $required" }
}
foreach ($required in @(
    'pairProperty.FindPropertyRelative("inputs")',
    'pairProperty.FindPropertyRelative("outputs")',
    'public List<InputOutputJsonEntry> inputs',
    'public List<InputOutputJsonEntry> outputs',
    'GetInheritedInputOutputPairSectionFoldoutKey',
    'public float count = 1f',
    'EditorGUILayout.FloatField(',
    '"Amount (L)"',
    'countProperty.floatValue'
)) {
    if (!$editorSource.Contains($required)) { throw "Missing Item Data editor integration: $required" }
}
if ($editorSource.Contains('preferCraftingTreeIngredients')) {
    throw 'Obsolete ProductionMachine ingredient text mode still exists.'
}

$generated = @'
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ItemDefinition
{
    public int id;
    public string itemName;
    private object mapObjectValue;
    public int MapObjectReads;
    public object mapObject
    {
        get { MapObjectReads++; return mapObjectValue; }
        set { mapObjectValue = value; }
    }
}

public class InstallationObject
{
    protected virtual void OnValidate() { }
}

public partial class InputOutputModule : InstallationObject
{
    private bool rectGridDataInitialized;
    private bool rectGridPlacementDataInitialized;
    private void EnsureRectGridData() { if (!rectGridDataInitialized) rectGridDataInitialized = true; }
    private void EnsureRectGridPlacementData() { if (!rectGridPlacementDataInitialized) rectGridPlacementDataInitialized = true; }
    public void Validate() => OnValidate();
    public ItemDefinition ParentItem => parentInputOutputModuleItem;
    public void LoadParentItem(ItemDefinition item) => parentInputOutputModuleItem = item;
    private ItemDefinition parentInputOutputModuleItem;
    private List<InputOutputPair> inputOutputPairs = new List<InputOutputPair>();
    private List<ItemIoEntry> inputList = new List<ItemIoEntry>();
    private List<ItemIoEntry> outputList = new List<ItemIoEntry>();
    private ItemIoEntry output = new ItemIoEntry(null, 1);
    private readonly List<ItemIoEntry> localInputList = new List<ItemIoEntry>();
    private readonly List<ItemIoEntry> localOutputList = new List<ItemIoEntry>();
    private readonly List<InputOutputPair> effectiveInputOutputPairs = new List<InputOutputPair>();
    private readonly List<ItemIoEntry> effectiveInputList = new List<ItemIoEntry>();
    private readonly List<ItemIoEntry> effectiveOutputList = new List<ItemIoEntry>();
    private bool effectivePairDataInitialized;

    public void LoadLegacy(IReadOnlyList<ItemIoEntry> inputs, IReadOnlyList<ItemIoEntry> outputs, ItemIoEntry legacyOutput)
    {
        inputOutputPairs.Clear();
        inputList = new List<ItemIoEntry>(inputs);
        outputList = new List<ItemIoEntry>(outputs);
        output = legacyOutput;
        effectivePairDataInitialized = false;
    }

    public void LoadPairs(params InputOutputPair[] pairs)
    {
        inputOutputPairs = new List<InputOutputPair>(pairs);
        inputList.Clear();
        outputList.Clear();
        effectivePairDataInitialized = false;
    }

    public void SetParent(InputOutputModule parent)
    {
        parentInputOutputModuleItem = parent == null ? null : new ItemDefinition { mapObject = parent };
        effectivePairDataInitialized = false;
    }

    public IReadOnlyList<InputOutputPair> LocalPairs { get { EnsurePairData(); return inputOutputPairs; } }
    public IReadOnlyList<ItemIoEntry> LocalInputs { get { EnsurePairData(); return localInputList; } }
    public IReadOnlyList<ItemIoEntry> LocalOutputs { get { EnsurePairData(); return localOutputList; } }
    public IReadOnlyList<InputOutputPair> EffectivePairs { get { EnsureEffectivePairData(); return effectiveInputOutputPairs; } }
    public IReadOnlyList<ItemIoEntry> EffectiveInputs { get { EnsureEffectivePairData(); return effectiveInputList; } }
    public IReadOnlyList<ItemIoEntry> EffectiveOutputs { get { EnsureEffectivePairData(); return effectiveOutputList; } }
'@

foreach ($signature in @(
    'public struct ItemIoEntry',
    'public sealed class InputOutputPair',
    'public static bool IsFluidItemDefinition(',
    'private void EnsurePairData()',
    'private void MigrateLegacyPairData()',
    'private static void NormalizePairEntries(',
    'private void EnsureEffectivePairData()',
    'private static void AppendEffectivePairData(',
    'private static void AppendEntries(',
    'private InputOutputModule ResolveParentInputOutputModule()',
    'public bool IsValidParentInputOutputModuleItem(',
    'protected override void OnValidate()'
)) {
    $generated += "`n" + (Read-Member $signature) + "`n"
}
$generated += "}`n"
$generated = $generated.Replace('Application.isPlaying', 'false')

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-InputOutputPair-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probeDir
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
