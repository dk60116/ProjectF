$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-TrainAutoDrive-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/SteamTrain.cs'))
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
    $source.Substring($start, $end - $start)
}
$generated = "using System.Collections.Generic;`nusing UnityEngine;`npublic partial class SteamTrain : RailHandcar {`n"
$fieldSource = $source.Substring(0, $source.IndexOf('public float ObjectInfoStoredBurnEnergy'))
foreach ($line in $fieldSource.Split("`n")) {
    if ($line -cmatch '^    private .*\b(autoDrive\w*|nextAutoDriveControllerRevision|lastDrivenInputFrame)\b.*;') {
        $generated += $line + "`n"
    }
}
$properties = $source.Substring($source.IndexOf('    private SteamTrain AutoDriveSettingsOwner'), $source.IndexOf('    private bool HasAnyAutoDriveTarget') - $source.IndexOf('    private SteamTrain AutoDriveSettingsOwner'))
$generated += $properties
foreach ($signature in @(
    'private enum AutoDriveFuelFilter', 'private enum AutoDriveFreightFilter', 'private enum AutoDriveStatus', 'private enum DriveMotionOutcome',
    'public override void HandleMountedInput(', 'private void TickAutoDrive(', 'private DriveMotionOutcome HandleResolvedDriveMotion(',
    'public void ApplyAutoDriveSettings(', 'public void CaptureAutoDriveState(', 'public void ApplyAutoDriveState(',
    'private void ClaimAutoDriveControl(', 'private bool IsPrimaryAutoDriveControllerForConsist(', 'private SteamTrain ResolveAutoDriveControllerForConsist(',
    'private void TransferAutoDriveControl(', 'private void ResetAutoDriveRuntimeState(', 'private void ClearAutoDriveFixedRoute(',
    'private bool TryBuildActiveRouteFromFixedRoute(', 'private bool TryAlignAutoDriveRouteSegmentsToCurrentPose(',
    'private static string NormalizeAutoDriveStationName(', 'private bool HasCompleteAutoDriveTargets()', 'private static bool HasCompleteAutoDriveTargets(',
    'private static AutoDriveFuelFilter ParseAutoDriveFuelFilter(', 'private static AutoDriveFreightFilter ParseAutoDriveFreightFilter(',
    'private static AutoDriveFuelFilter ClampAutoDriveFuelFilter(', 'private static AutoDriveFreightFilter ClampAutoDriveFreightFilter(',
    'private Vector3 ResolveAutoDriveMoveDirection(', 'private bool TryResolveAutoDriveTargets(', 'private bool TryBuildRouteLengthToStation(',
    'private bool TryEnsureAutoDriveRoute(', 'private void HandleAutoDriveArrived(', 'private static bool TryResolveAutoDriveDockSignedStep(',
    'protected override float AdjustDrivenSignedStep(', 'protected override bool CanDockInDirection(', 'protected override float ResolveRailInputAxis(',
    'private bool TryResolveAutoDriveRouteInputAxis(',
    'private RailHandcar ResolveAutoDriveRouteReferenceTrain(', 'private bool TryResolveAutoDriveClosestEndpointTrain(',
    'private bool TryGetCachedAutoDriveRouteReferenceTrain(', 'private void CacheAutoDriveRouteReferenceTrain(',
    'private bool TryResolveAutoDriveClosestRouteReferenceTrain(', 'private bool TryGetAutoDriveRouteReferenceCandidate(',
    'private bool TryBuildRouteLengthForReferenceCandidate(', 'private void CollectAutoDriveConnectedTrains(',
    'private int CountConnectedTrainsWithinAutoDriveGroup(', 'private static bool IsValidAutoDriveRouteReferenceTrain(',
    'private bool TryApplyConsistWaterPipeDocking('
)) {
    # The route-reference overload with two arguments is the selection entry point.
    if ($signature -eq 'private RailHandcar ResolveAutoDriveRouteReferenceTrain(') {
        $signature = "private RailHandcar ResolveAutoDriveRouteReferenceTrain(`r`n        Trainstation targetStation,"
        if (!$source.Contains($signature)) { $signature = $signature.Replace("`r`n", "`n") }
    }
    if ($signature -eq 'public override void HandleMountedInput(') {
        $signature = "public override void HandleMountedInput(`r`n        Vector3 worldMoveDirection,`r`n        float moveSpeed,`r`n        float deltaTime,`r`n        Player mountedPlayer)"
        if (!$source.Contains($signature)) { $signature = $signature.Replace("`r`n", "`n") }
    }
    $generated += (Read-Member $signature) + "`n"
}
$generated += "private static partial class AutoDriveRoutePlanner {`n"
foreach ($match in [regex]::Matches($source, 'private const float Route(StartForwardDotThreshold|TurnSharpPenalty|TurnReverseDotThreshold|TurnSharpDotThreshold) = [^;]+;')) {
    $generated += $match.Value + "`n"
}
foreach ($signature in @(
    'public readonly struct RouteSegment', 'private sealed class RailInfo', 'private readonly struct RouteGraphEdge',
    'private readonly struct RouteTraversalState', 'private readonly struct RouteQueueEntry',
    'public static bool IsForwardRoute(', 'private static Vector2 ResolvePreferredRouteStartDirection(',
    'private static bool TryFindRouteGraphPath(', 'private static void PushRouteQueue(', 'private static bool TryPopRouteQueue(',
    'private static float ResolveRouteStartEdgePenalty(', 'private static float ResolveRouteTurnPenalty(',
    'private static bool TryResolveRouteEdgeTravelDirectionAtPosition(', 'private static void AppendRouteSegment(', 'public static float GetRouteLength('
)) { $generated += (Read-Member $signature) + "`n" }
$generated += "}}`n"
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/RailHandcar.cs'))
$generated += "public partial class RailHandcar {`n"
foreach ($match in [regex]::Matches($source, 'private const float (ConsistPathSampleDistanceEpsilon|ConsistPathInterpolationEpsilon|ConsistPathDirectionMinDot|RailConnectionDistanceEpsilon|RailConnectionBridgePointTolerance|BranchInternalOverlapMinTangentDot) = [^;]+;')) {
    $generated += $match.Value + "`n"
}
foreach ($signature in @(
    'protected struct RailSample', 'private struct ConnectedTrainRailMove', 'private struct ConsistPathSample',
    'protected void TransferConsistPathTo(', 'private bool TryReverseConsistPathTape(',
    'private static RailSample ReverseRailConnectionBridgeSample(', 'private bool IsConsistPathTapeValid(',
    'private bool IsConsistPathAlignedWithPreparedTrainSamples(', 'private bool IsConsistPathSampleAlignedWithTrainStart(',
    'private void ResetConsistPathTape(', 'private void AddConsistPathSample(', 'private bool TrySampleConsistPathTape(',
    'private int FindConsistPathUpperBound(', 'private bool TrySampleBetweenConsistPathSamples(',
    'private static bool TryCreateLinearRailSample(', 'private static bool TryResolveRailConnectionProgressAtPoint(',
    'private static bool TryCreateRailConnectionBridgeSample(', 'private static bool TryCreateRailSampleAtDistance(',
    'private static bool IsSameRailPosition(', 'private static bool HasRailConnectionBridgeState(',
    'private static bool IsRailConnectionBridgeSample(', 'private static float ResolveConsistFollowOffset('
)) { $generated += (Read-Member $signature) + "`n" }
foreach ($signature in @('protected virtual bool CanDockInDirection(', 'protected bool TryApplyDockingToSample(', 'private static bool TryResolveDockTravelDirection(', 'protected static bool TryBuildCurrentRailSample(', 'protected bool TryApplyConnectedTrainMemberDocking(', 'protected bool TryApplyStationDocking(')) {
    $generated += (Read-Member $signature) + "`n"
}
$generated += "}`n"
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PathTransferChecks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
