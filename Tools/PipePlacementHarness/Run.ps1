$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InstallationPlacementController.cs'
$source = [IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n")

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
)) {
    $generated += "`n" + (Read-Member ($signature -replace "`r?`n", "`n"))
}
$generated += "`n}`n"

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PipePlacement-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probeDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NeighborFixture.cs') -Destination $probeDir
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
