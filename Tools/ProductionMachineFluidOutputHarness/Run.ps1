$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$base = Join-Path $repo 'FactorioProject/Assets/Scripts'
function Member([string]$file, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $base $file))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $arrow = $source.IndexOf('=>', $start, [StringComparison]::Ordinal)
    $semicolon = $source.IndexOf(';', $start)
    $brace = $source.IndexOf('{', $start)
    if ($arrow -ge 0 -and $arrow -lt $brace -and $semicolon -lt $brace) {
        return $source.Substring($start, $semicolon - $start + 1)
    }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced member: $signature" }
    $source.Substring($start, $end - $start)
}
$module = 'Object/MapObj/InstallationObject/InputOutputModule.cs'
$machine = 'Object/MapObj/InstallationObject/ProductionMachine.cs'
$generated = "using System; using System.IO; using System.Globalization; using System.Collections.Generic; using UnityEngine; using UnityEngine.UI; using TMPro;`npublic partial class InputOutputModule {`n"
foreach ($signature in @(
    'public struct PersistentInputItemAreaState', 'public sealed class PersistentState',
    'protected void ApplyPlannedBaseModuleTick(',
    'private readonly struct FluidOutputConnection', 'private readonly struct FluidOutputTransferCandidate',
    'protected bool TryEmitFluidOutputToConnectedStorages(', 'private bool TryTransferFluidOutputConnection(',
    'private bool BuildFluidOutputTransferCandidates(', 'protected float ResolveFluidOutputTransportRetention(',
    'private static float ResolvePumpTransportRatio(', 'private float LimitFluidOutputTransfer(',
    'private bool TryAddFluidToOutputConnection(')) {
    $generated += (Member $module $signature) + "`n"
}
$generated += "} public partial class ProductionMachine {`n"
foreach ($signature in @(
    'public override PersistentState CapturePersistentState(', 'public override void ApplyPersistentState(',
    'public override void PrepareForPool(', 'public override void ApplyManagedUpdateTick(',
    'protected override bool ShouldKeepRuntimeUpdateTickActive(',
    'protected override bool ShouldKeepRuntimeUpdateTickActiveWithoutOperationalEnergy(',
    'protected override bool IsRecipeOutputAllowedByItemFilter(',
    'protected override bool TryCompleteActiveCraft(', 'private float ResolveProductionFluidOutputRate(',
    'private float ResolveProductionFluidBatchLiters(',
    'public bool TryGetObjectInfoProductionFluidOutput(',
    'public bool TryGetObjectInfoProductionFluidIngredient(',
    'public readonly struct FluidGaugeState',
    'public bool TryGetObjectInfoProductionFluidGauge(',
    'private float ResolveFluidIngredientRequiredLiters(',
    'public override float GetObjectInfoFluidPressureLitersPerSecond(',
    'private int ResolveProductionOutputCount(')) {
    $generated += (Member $machine $signature) + "`n"
}
$generated += "} public partial class Pump {`n"
foreach ($signature in @('internal static float LimitTransportRate(', 'internal float LimitTransferVolume(', 'internal void RecordTransferredVolume(')) {
    $generated += (Member 'Object/MapObj/Pump.cs' $signature) + "`n"
}
$generated += "} public static partial class SaveProbe {`n"
$serializer = 'Manager/SaveGameBinarySerializer.cs'
foreach ($signature in @(
    'private static void WriteInputOutputState(', 'private static InputOutputModule.PersistentState ReadInputOutputState(',
    'private static void WriteVector2Int(', 'private static Vector2Int ReadVector2Int(',
    'private static void WriteVector2IntList(', 'private static List<Vector2Int> ReadVector2IntList(',
    'private static void WriteInputItemAreaList(', 'private static List<InputOutputModule.PersistentInputItemAreaState> ReadInputItemAreaList(',
    'private static void WriteIntList(', 'private static List<int> ReadIntList(',
    'private static void WriteLongList(', 'private static List<long> ReadLongList(',
    'private static void WriteList<T>(', 'private static List<T> ReadList<T>(')) {
    $generated += (Member $serializer $signature) + "`n"
}
$versionSource = [IO.File]::ReadAllText((Join-Path $base 'Manager/SaveGameData.cs'))
$generated += 'public const int CurrentVersion = ' + [regex]::Match($versionSource, 'CurrentVersion = (\d+)').Groups[1].Value + '; }'
$generated += "`n" + (Member 'Simulation/Core/SimulationTickContracts.cs' 'public static class DeterministicSimulationUnits')
$generated += "`npublic partial class ItemInfoDescription {`n"
foreach ($signature in @(
    'private readonly struct ProductionFluidGauge',
    'private bool TrySetProductionMachineItemSlots(', 'private void RefreshProductionFluidGauges(',
    'private static void SetProductionFluidGauge(', 'private bool EnsureAdditionalProductionFluidGauge(',
    'private static Color ResolveProductionFluidGaugeColor(', 'private static void SetProductionConvertedFill(',
    'private static void EnsureProductionConvertedFill(',
    'private void HideAdditionalProductionFluidGauges(', 'private static void MoveItemAfter(',
    'private void SetFluidOutputRateItemSlot(', 'private static string FormatLitersPerSecond(',
    'private static string FormatFluidLiters(', 'private static string FormatGaugeNumber(')) {
    $generated += (Member 'HUD/ObjectUI/ItemInfoDescription.cs' $signature) + "`n"
}
$generated += '}'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ProductionFluidOutput-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
Set-Content -LiteralPath (Join-Path $probe 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DisplayChecks.cs') -Destination $probe
Set-Content -LiteralPath (Join-Path $probe 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
