$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InstallationPlacementController.cs'
$source = [IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n")

function Read-Member([string]$signature, [string]$memberSource = $source) {
    $start = $memberSource.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $memberSource.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $memberSource.Length) {
        if ($memberSource[$end] -eq '{') { $depth++ }
        if ($memberSource[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $memberSource.Substring($start, $end - $start)
}

$generated = @'
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class InstallationPlacementController
{
'@
foreach ($signature in @(
    'private static bool IsDuplicatePipePlacement(',
    'private bool CanProposedPumpConnectionsMatchExisting(',
    'private int ResolvePreferredPipeVariantFluidItemId(',
    'private bool TryGetAdjacentFluidTankItemId(',
    'private int ResolvePipeContinuationPriority(',
    'private bool ShouldPipeNeighborContributeToVariant(',
    'private static bool CanPipePotentiallyConnectTowards(',
    'private static bool PipeConnectionMaskContainsDirection(',
    'private bool HasEffectivePipeConnectionTowardsAt(',
    'private bool TryGetEffectivePipeRemoteConnectionCoordinate(',
    'private bool AuthoritativePipeHasConnectionTowardsAt(',
    'private bool TryGetAdjacentPipeBranchFluidItemId(',
    'private bool CanPipePlacementFluidConnectionsMatch(
        Vector2Int pipeCoordinate,
        Pipe pipe,
        Quaternion pipeRotation,',
    'private bool TryCollectPipeNetworkFluidConstraints(
        Vector2Int startCoordinate,
        Pipe startPipe,
        Quaternion startPipeRotation,
        MapObject previewToIgnore,
        HashSet<int> compatibleFluidItemIds,
        ref bool hasFluidConstraint)',
    'private bool TryCollectPipeNetworkFluidConstraintsExcludingConnection(',
    'private bool TryCollectPipeNetworkFluidConstraints(
        Vector2Int startCoordinate,
        Pipe startPipe,
        Quaternion startPipeRotation,
        MapObject previewToIgnore,
        HashSet<int> compatibleFluidItemIds,
        ref bool hasFluidConstraint,
        bool hasExcludedConnection,',
    'private static bool PipeConnectionMatchesExcludedConnection(',
    'private static bool TryMergeFluidCompatibilityConstraint('
    'private bool TryMergeInstalledPipeFluidConstraint('
    'private bool IsPipeBlueprintFluidResolution('
    'private bool TryGetInstalledPipeNetworkFluidItemId('
    'private bool TryResolvePipePlacementVariant('
    'private bool TryResolvePipePlacementVariantWithCompatibleAdjacency('
    'private bool TryAlignPipeVariantToExposedNeighborConnections('
    'private bool TryResolvePipeStraightQuarterTurns('
    'private bool TryResolvePipeVariantQuarterTurns('
    'private bool TryResolveBestCompatibleTeePipeVariant('
    'private bool TryResolveFixedConnectorCornerPipeVariant('
    'private bool TryResolveFluidSeparatedPipeVariant('
    'private bool TryResolveCompatiblePipeVariantCandidate('
    'private static List<Vector2Int> MergePipeDirections('
    'private static void AddPipeDirections('
    'private static void AddPipeDirection('
    'private static void AddPipeConnectionMaskDirections('
    'private static bool HasPerpendicularPipeNeighborDirections('
    'private static bool HasOppositePipeNeighborDirections('
    'private void RemoveIncompatibleRuntimePipeDirections('
    'private void RemoveIncompatibleFixedFluidConnectorDirections('
    'private static bool DirectionsArePerpendicular('
    'private bool TryResolvePipeVariantForForcedConnection('
    'private bool TryResolvePipeVariantForConnectionDirections('
    'private bool TryUndoPackedInstallation('
    'private void RestorePackedInstallationHistory('
    'public static MapObject ResolvePipeVariantPrefab('
    'public bool TryResolvePipeLoadPlacement(
        ItemDefinition definition,
        Vector2Int anchorCoordinate,
        int preferredQuarterTurns,
        int savedVariantKind,'
    'public bool TryResolvePipeQuarterTurnsFromConnectionMask('
    'private bool TryResolvePipePlacementFromConnectionMask('
    'public void NormalizeLoadedLegacyPipeVariants('
    'private List<Vector2Int> GetPipeNeighborConnectionDirections('
    'private bool CanPipeNeighborContributeToVariant('
    'private bool TryCollectCandidatePipeFluidConstraintsForNeighborDirection('
    'private static bool PipeDirectionsContain('
    'private List<Vector2Int> GetPipeVariantFixedConnectionDirections('
    'private List<Vector2Int> GetFixedFluidConnectorNeighborConnectionDirections('
    'private List<Vector2Int> GetFluidTankNeighborConnectionDirections('
    'private bool CanPipeConnectionMatchFluidTank('
    'private int ResolveFluidTankBlueprintNetworkFluidItemId('
    'private bool TryResolveFluidTankBlueprintConnection('
)) {
    $generated += "`n" + (Read-Member ($signature -replace "`r?`n", "`n"))
}
$generated += "`n}`n"
$pipeWorldSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/PipeWorld.cs'))
$generated += "`npublic sealed partial class PipeRuntimeRecord {`n" +
    (Read-Member 'public bool TryGetObjectInfoFluidInfo(' $pipeWorldSource) + "`n}`n"

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PipePlacement-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probeDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NeighborFixture.cs') -Destination $probeDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VariantChecks.cs') -Destination $probeDir
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
