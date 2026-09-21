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
    'entry.ingredients.Count != requiredIngredientTypes'
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
    public object mapObject;
}

public partial class InputOutputModule
{
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
    'private InputOutputModule ResolveParentInputOutputModule()'
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
