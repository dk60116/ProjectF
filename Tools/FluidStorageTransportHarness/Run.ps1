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
$generated = "using System; using System.Collections.Generic; using UnityEngine; public partial class InputOutputModule {`n"
foreach ($name in @(
    'private static void HandleInstallationPlacementRuntimeChanged(', 'internal static bool AffectsRuntimeFluidTopology(',
    'private static void WakeRuntimeFluidTopologyModules(',
    'private readonly struct ConnectedFluidSearchNode', 'private readonly struct FluidOutputConnection', 'private readonly struct DirectedSteamPort',
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
    'internal virtual bool TryGetRuntimePassiveFluidPass(', 'protected bool TryGetPairedRuntimePipeInputPass(',
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
    'protected float ResolveFluidOutputTransportRetention(',
    'private void BuildDirectedBoilerSteamOutputCache(', 'private static void AddDirectedBoilerSteamChains(',
    'private static void EnqueueDirectedSteamPort(', 'private static void TryAppendDirectedSteamGeneratorAtPort(',
    'private static void EnqueueDirectedSteamPipesAtPort(', 'private static bool TryFindDirectedSteamGenerator(',
    'private static void SelectDirectedSteamGeneratorAtCoordinate(', 'private static void SelectDirectedSteamGenerator(')) {
    $generated += (Read-Member 'InputOutputModule.cs' $name) + "`n"
}
$generated += (Read-Member 'InputOutputModule.cs' 'private bool TryResolveConnectedFluidStorageAtCoordinate(' 2) + "`n"
# This expression-bodied comparison has no block for the member extractor.
$generated += 'private static int CompareFluidOutputConnectionOrder(FluidOutputConnection a, FluidOutputConnection b) => CompareSimulationOrder(a.Storage,b.Storage);'
$generated += "} public partial class SteamGenerator {`n"
foreach ($name in @('public bool CanReceiveSteamFromDirectedPortAtRuntime(', 'internal static bool IsDirectedSteamPortConnection(')) {
    $generated += (Read-Member 'SteamGenerator.cs' $name) + "`n"
}
$generated += "} public partial class InstallationObject {`n" + (Read-Member 'InstallationObject.cs' 'public bool TryAddFluidLiters(' 3)
$generated += "} public partial class Pipe {`n" + (Read-Member 'Pipe.cs' 'public static int AddRemoteTraversalPipeDistance(') + '}'
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-FluidStorageTransport-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
$checks = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup><Compile Include="' + $checks + '"/><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
