$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$base = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject'
function Read-Member([string]$file, [string]$signature, [int]$occurrence = 1) {
    $source = [IO.File]::ReadAllText((Join-Path $base $file))
    $start = -1
    for ($i=0; $i -lt $occurrence; $i++) {
        $start = $source.IndexOf($signature, $start+1, [StringComparison]::Ordinal)
        if ($start -lt 0) { throw "Missing production member: $signature" }
    }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}
$generated = "using System; using System.Collections.Generic; using UnityEngine; using ProjectF.Simulation; public partial class InputOutputModule {`n"
foreach ($name in @(
    'private static void HandleInstallationPlacementRuntimeChanged(', 'internal static bool AffectsRuntimeFluidTopology(',
    'private static void WakeRuntimeFluidTopologyModules(',
    'private readonly struct ConnectedFluidSearchNode', 'private readonly struct FluidOutputConnection', 'private readonly struct DirectedSteamPort',
    'private readonly struct FluidStorageEndpointKey', 'private readonly struct FluidOutputTransferCandidate',
    'private static bool CoordinatesMatch(', 'private static void AddUniqueCoordinates(',
    'private sealed class FluidPortConnectionCache',
    'private bool EnsureConnectedFluidSourceStorageCache(IReadOnlyList<Vector2Int> seedCoordinates)',
    'private void AddConnectedFluidStorageCacheCandidate(',
    'protected bool TryConsumeConnectedFluidInputAtCoordinate(',
    'private FluidPortConnectionCache GetFluidPortConnectionCache(',
    'private bool EnsureFluidOutputStorageCache()',
    'private bool EnsureFluidOutputStorageCache(IReadOnlyList<Vector2Int> seedCoordinates)',
    'protected bool TryEmitFluidOutputToConnectedStorages(',
    'private bool TryGetConnectedPipeAtCoordinate(', 'private static bool HasConnectedPipeConnectionTowards(', 'private static bool TryGetConnectedPipeRemoteCoordinate(',
    'private bool TryGetConnectedFluidNodeAtCoordinate(', 'private bool TryResolveConnectedFluidSearchStorageAtCoordinate(',
    'private bool TryResolveConnectedFluidStorageBodyAtCoordinate(', 'private static bool ContainsRuntimeOccupiedCoordinate(',
    'internal readonly struct RuntimePumpPipePass', 'internal static bool CollectPumpPipePassesAtRuntimeCoordinate(',
    'private static void AppendPumpPipePasses(',
    'private bool CanFluidSearchLeaveCoordinate(', 'private static bool CanFluidStorageConnectToDirection(',
    'private void EnqueueConnectedFluidSearchCoordinate(', 'private static int FreezeConnectedFluidPipeCount(',
    'private static int AddConnectedFluidPipeCount(', 'private static bool IsBetterConnectedFluidPipeCount(',
    'private static bool IsConnectedFluidPipeCountFrozen(', 'private static int ResolveConnectedFluidPipeCount(',
    'private bool TryEnqueuePassiveFluidPassesAt(', 'private bool EnqueuePassiveFluidPassesAt(',
    'internal virtual bool TryGetRuntimePassiveFluidPass(',
    'internal static bool HasRuntimePassiveFluidPassTowards(', 'private static bool HasPassiveFluidPassTowards(',
    'internal static bool HasRuntimePumpPipePassTowards(', 'private static bool HasPumpPipePassTowards(',
    'private static int GetDirectionMask(', 'private static bool DirectionMaskContains(',
    'private static bool IsFixedFluidTank(',
    'private void EnqueueFluidStoragePipePassCoordinatesAt(', 'private bool EnqueueFluidStoragePipePassCoordinatesAt(',
    'private bool TryEnqueuePumpPressureResetPassesAt(',
    'private void EnqueueInterlockedPumpEndpointsAt(',
    'private void AppendInterlockedPumpEndpointsAt(',
    'private void AddFluidOutputStorageCacheCandidatesAtCoordinate(', 'private void AddFluidOutputStorageCacheCandidate(',
    'private bool TrySelectFluidOutputStorageWithAnySpaceFromCache(', 'private bool TrySelectFluidOutputConnectionWithAnySpaceFromCache(',
    'private bool CanUseFluidOutputStorageWithAnySpace(', 'private static float GetFluidStorageFillRatio(',
    'protected float ResolveFluidOutputTransportRetention(', 'private static float ResolvePumpTransportRatio(',
    'private bool BuildFluidOutputTransferCandidates(', 'private bool CanUseFluidOutputConnectionWithAnySpace(',
    'private float GetFluidOutputConnectionAvailableLiters(', 'private float GetFluidOutputConnectionFillRatio(',
    'private float LimitFluidOutputTransfer(', 'private bool TryAddFluidToOutputConnection(',
    'private bool TryTransferFluidOutputConnection(',
    'private void UpdateActiveCraft(',
    'private void BuildDirectedBoilerSteamOutputCache(', 'private static void AddDirectedBoilerSteamChains(',
    'private static void EnqueueDirectedSteamPort(', 'private static void TryAppendDirectedSteamGeneratorAtPort(',
    'private static void EnqueueDirectedSteamPipesAtPort(', 'private static bool TryFindDirectedSteamGenerator(',
    'private static void SelectDirectedSteamGeneratorAtCoordinate(', 'private static void SelectDirectedSteamGenerator(')) {
    $generated += (Read-Member 'InputOutputModule.cs' $name) + "`n"
}
$generated += (Read-Member 'InputOutputModule.cs' 'private bool TryResolveConnectedFluidStorageAtCoordinate(' 2) + "`n"
$generated += (Read-Member 'InputOutputModule.cs' 'internal static bool HasRuntimeFluidOutputTowardsPipe(') + "`n"
# This expression-bodied comparison has no block for the member extractor.
$generated += (Read-Member 'InputOutputModule.cs' 'private static int CompareFluidOutputConnectionOrder(')
$generated += "} public partial class Pump {`n"
foreach ($name in @('public bool TryGetRuntimePipePass(', 'internal bool AllowsRuntimeFluidTraversal(', 'internal Vector2Int ResolveRuntimeFluidDeliveryCoordinate(',
    'internal static Pump ResolvePressureLimit(', 'internal static float LimitTransportRate(',
    'internal float LimitTransferVolume(', 'internal void RecordTransferredVolume(')) {
    $generated += (Read-Member '../Pump.cs' $name) + "`n"
}
$generated += "} public partial class ProductionMachine {`n"
foreach ($name in @('protected override bool TryCompleteActiveCraft(', 'private float ResolveProductionFluidOutputRate(', 'private float ResolveProductionFluidBatchLiters(')) {
    $generated += (Read-Member 'ProductionMachine.cs' $name) + "`n"
}
$generated += "} public partial class SteamGenerator {`n"
foreach ($name in @('public bool CanReceiveSteamFromDirectedPortAtRuntime(', 'internal static bool IsDirectedSteamPortConnection(')) {
    $generated += (Read-Member 'SteamGenerator.cs' $name) + "`n"
}
$generated += "} public partial class InstallationObject {`n" + (Read-Member 'InstallationObject.cs' 'public bool TryAddFluidLiters(' 3)
$generated += (Read-Member 'InstallationObject.cs' 'public bool TryConsumeFluidLiters(int fluidItemId,')
$generated += "} public partial class Pipe {`n" + (Read-Member 'Pipe.cs' 'public static int AddRemoteTraversalPipeDistance(') + '}'
$generated += [IO.File]::ReadAllText((Join-Path $base '../../../Simulation/Core/ProductionProcess.cs')).Replace('using System;', '')
$generated += (Read-Member '../../../Simulation/Core/SimulationTickContracts.cs' 'public static class DeterministicSimulationUnits')
$tickSource = [IO.File]::ReadAllText((Join-Path $base '../../../Simulation/Core/SimulationTickWorld.cs'))
$tickRate = [regex]::Match($tickSource, 'DefaultSimulationTicksPerSecond = (\d+)').Groups[1].Value
if (!$tickRate) { throw 'Missing simulation tick rate' }
$generated += "namespace ProjectF.Simulation { public static class SimulationTickWorld { public const int DefaultSimulationTicksPerSecond = $tickRate; public const float FixedSimulationDeltaSeconds = 1f / DefaultSimulationTicksPerSecond; } }"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-FluidStorageTransport-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
$checks = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup><Compile Include="' + $checks + '"/><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
