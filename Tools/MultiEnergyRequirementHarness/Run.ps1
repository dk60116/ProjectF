$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$itemSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/ItemDefinition.cs'))
$moduleSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'))
if ($itemSource.Contains('OilBurn')) { throw 'Removed OilBurn energy type still exists.' }
foreach ($requiredType in @('Diesel = 6', 'HeavyOil = 7', 'PetroleumGas = 8')) {
    if (!$itemSource.Contains($requiredType)) { throw "Missing fluid fuel energy type: $requiredType" }
}
$energyAssetExpectations = @{
    'Item_4_Crude Oil.asset' = 'energyType: 0'
    'Item_4_Diesel Oil.asset' = 'energyType: 6'
    'Item_113_Heavy oil.asset' = 'energyType: 7'
    'Item_114_Petroleum gas.asset' = 'energyType: 8'
}
foreach ($entry in $energyAssetExpectations.GetEnumerator()) {
    $assetPath = Join-Path $repo ('FactorioProject/Assets/Data/Items/' + $entry.Key)
    if (![IO.File]::ReadAllText($assetPath).Contains($entry.Value)) {
        throw "Unexpected energy type in $($entry.Key): expected $($entry.Value)"
    }
}
$petroleumAsset = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Data/Items/Item_114_Petroleum gas.asset'))
if (!$petroleumAsset.Contains('itemName: Petroleum gas')) { throw 'Petroleum gas display name is missing.' }
$craftingTreeSource = [IO.File]::ReadAllBytes((Join-Path $repo 'FactorioProject/Assets/Data/CraftingTree/crafting_tree.bytes'))
$craftingTreeCopy = [IO.File]::ReadAllBytes((Join-Path $repo 'FactorioProject/Assets/Resources/Data/CraftingTree/crafting_tree.bytes'))
if (![Linq.Enumerable]::SequenceEqual([byte[]]$craftingTreeSource, [byte[]]$craftingTreeCopy)) {
    throw 'Crafting tree resource copy differs from its source.'
}
$craftingTreeText = [Text.Encoding]::UTF8.GetString($craftingTreeSource)
if (!$craftingTreeText.Contains('Petroleum gas') -or $craftingTreeText.Contains('LPG Gas')) {
    throw 'Crafting tree still uses the old petroleum gas name.'
}

function Read-Member([string]$source, [string]$signature) {
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

$generated = @'
using System;
using System.Collections.Generic;
[AttributeUsage(AttributeTargets.Field)]
public sealed class MinAttribute : Attribute { public MinAttribute(float value) { } }
public static class Mathf
{
    public static float Max(float a, float b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
public static class DeterministicSimulationUnits
{
    public static long DeltaTimeToTicks(float value) => (long)Math.Round(value * 60d);
    public static long RateForTicks(float rate, long ticks) => (long)Math.Round(rate * ticks / 60d * 1000000d);
}
public class ItemDefinition
{
    private const float KilowattsToWatts = 1000f;
    public enum EnergyType { None = 0, Burn = 1, Electricity = 2, CarnivoreFood = 3, HerbivoreFood = 4, Fertilizer = 5, Diesel = 6, HeavyOil = 7, PetroleumGas = 8 }
    private List<EnergyUseRequirement> useEnergyRequirements = new List<EnergyUseRequirement>();
    public EnergyType useEnergyType = EnergyType.None;
    public float useEnergyAmount;
    public float completeEnergy;
    public int UseEnergyRequirementCount => useEnergyRequirements != null && useEnergyRequirements.Count > 0
        ? useEnergyRequirements.Count : useEnergyType != EnergyType.None ? 1 : 0;
'@
$generated += (Read-Member $itemSource 'public struct EnergyUseRequirement') + "`n"
foreach ($signature in @(
    'private void ApplyLegacyCompleteEnergy(',
    'public bool TryGetUseEnergyRequirement(',
    'public bool UsesEnergyType(',
    'public bool AppendUseEnergyTypes(',
    'public void ReplaceUseEnergyRequirements(',
    'public static float ResolveUseEnergyRatePerSecond(ItemDefinition definition)',
    'public static float ResolveUseEnergyRatePerSecond(ItemDefinition definition, EnergyType energyType)',
    'public static float ResolveCompleteEnergyAmount(ItemDefinition definition)',
    'public static float ResolveElectricUseWatts(',
    'public static bool TryGetPrimaryUseEnergyRequirement(',
    'public static bool TryGetUseEnergyRequirement(')) {
    $generated += (Read-Member $itemSource $signature) + "`n"
}
$generated += @'
}
public static class UtilityPole
{
    public static bool HasElectricityAvailable(InputOutputModule module) => module.HasElectricity;
    public static bool TryConsumeElectricityUnits(InputOutputModule module, long requested, out long consumed)
    {
        module.ElectricConsumeCalls++;
        consumed = module.HasElectricity ? requested : 0;
        return consumed > 0;
    }
}
public partial class InputOutputModule
{
    private readonly ItemDefinition definition;
    public readonly HashSet<ItemDefinition.EnergyType> AvailableTypes = new();
    public bool HasElectricity;
    public int BurnConsumeCalls, ElectricConsumeCalls;
    private float lastOperationalEnergySupplyRatio;
    public InputOutputModule(ItemDefinition definition) => this.definition = definition;
    public bool HasAllEnergy() => HasOperationalEnergyAvailable(definition);
    public bool Consume(float deltaTime) => TryConsumeOperatingEnergy(deltaTime, out _);
    private ItemDefinition ResolveInstalledDefinition() => definition;
    private long GetBufferedEnergyUnits(ItemDefinition definition, ItemDefinition.EnergyType type) =>
        AvailableTypes.Contains(type) ? 1 : 0;
    private bool HasUsableEnergyItem(ItemDefinition.EnergyType type) => AvailableTypes.Contains(type);
    private long ConsumeBufferedEnergyUnits(ItemDefinition definition, ItemDefinition.EnergyType type, long requested)
    {
        BurnConsumeCalls++;
        return AvailableTypes.Contains(type) ? requested : 0;
    }
'@
foreach ($signature in @(
    'protected static bool RequiresOperationalEnergy(',
    'protected bool HasOperationalEnergyAvailable(',
    'protected bool TryConsumeOperatingEnergy(')) {
    $generated += (Read-Member $moduleSource $signature) + "`n"
}
$generated += "}`n"

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-MultiEnergy-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
[IO.File]::WriteAllText((Join-Path $probeDir 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probeDir
[IO.File]::WriteAllText((Join-Path $probeDir 'Probe.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
