$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-TrainAutoDrive-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$trainFilterSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/HUD/ItemSlot/TrainFilter.cs'))
$trainFilterPrefab = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Prefab/UI/HUD/Filter/Train Filter.prefab'))
if (!$trainFilterSource.Contains('RefreshStationTargetColor(targetA, targetAStationColor);') -or
    !$trainFilterSource.Contains('RefreshStationTargetColor(targetB, targetBStationColor);')) {
    throw 'Train Filter must refresh both target station color images.'
}
if (!$trainFilterPrefab.Contains('targetAStationColor: {fileID: 6360845314720199763}') -or
    !$trainFilterPrefab.Contains('targetBStationColor: {fileID: 5456011993426495286}')) {
    throw 'Train Filter prefab must bind the Target A and Target B color images.'
}
function Read-PrefabBlock([string]$header) {
    $start = $trainFilterPrefab.IndexOf($header, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing Train Filter prefab block: $header" }
    $end = $trainFilterPrefab.IndexOf('--- !u!', $start + $header.Length, [StringComparison]::Ordinal)
    if ($end -lt 0) { $end = $trainFilterPrefab.Length }
    $trainFilterPrefab.Substring($start, $end - $start)
}
function Assert-PrefabBlockContains([string]$header, [string[]]$requiredText) {
    $block = Read-PrefabBlock $header
    foreach ($text in $requiredText) {
        if (!$block.Contains($text)) {
            throw "Train Filter prefab block $header is missing: $text"
        }
    }
}
Assert-PrefabBlockContains '--- !u!224 &5932625448762877212' @(
    '- {fileID: 7168427658842336564}',
    '- {fileID: 9000000000000000074}',
    '- {fileID: 9100000000000000002}')
Assert-PrefabBlockContains '--- !u!224 &7168427658842336564' @(
    '- {fileID: 3337683063725499522}',
    '- {fileID: 1434913788011842552}')
Assert-PrefabBlockContains '--- !u!224 &9000000000000000074' @(
    '- {fileID: 5190120407831880711}',
    '- {fileID: 9000000000000000052}')
Assert-PrefabBlockContains '--- !u!224 &9100000000000000002' @(
    '- {fileID: 7735309841600087034}',
    '- {fileID: 9000000000000000080}')
foreach ($rowName in @('Target Row', 'Fuel Row', 'Freight Row')) {
    if (!$trainFilterPrefab.Contains("m_Name: $rowName")) {
        throw "Train Filter prefab is missing layout row: $rowName"
    }
}
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
    'public enum InfoWarning', 'public void GetObjectInfoStatus(',
    'public override void HandleMountedInput(', 'private void TickAutoDrive(', 'private DriveMotionOutcome HandleResolvedDriveMotion(',
    'public void ApplyAutoDriveSettings(', 'public void CaptureAutoDriveState(', 'public void ApplyAutoDriveState(',
    'private void ClaimAutoDriveControl(', 'private bool IsPrimaryAutoDriveControllerForConsist(', 'private SteamTrain ResolveAutoDriveControllerForConsist(',
    'private void TransferAutoDriveControl(', 'private void ResetAutoDriveRuntimeState(', 'private void ClearAutoDriveFixedRoute(',
    'private bool TryBuildActiveRouteFromFixedRoute(', 'private bool TryAlignAutoDriveRouteSegmentsToCurrentPose(',
    'private static string NormalizeAutoDriveStationName(', 'private bool HasCompleteAutoDriveTargets()', 'private static bool HasCompleteAutoDriveTargets(',
    'private static AutoDriveFuelFilter ParseAutoDriveFuelFilter(', 'private static AutoDriveFreightFilter ParseAutoDriveFreightFilter(',
    'private static AutoDriveFuelFilter ClampAutoDriveFuelFilter(', 'private static AutoDriveFreightFilter ClampAutoDriveFreightFilter(',
    'private Vector3 ResolveAutoDriveMoveDirection(', 'private bool TryResolveAutoDriveTargets(', 'private bool TryBuildRouteLengthToStation(',
    'private void ResolveAutoDriveDepartureFilters(', 'private bool TryEvaluateAutoDriveFuelFilterSatisfied(',
    'private bool TryEvaluateAutoDriveFreightFilterSatisfied(', 'private bool TryEnsureAutoDriveRoute(', 'private void HandleAutoDriveArrived(', 'private static bool TryResolveAutoDriveDockSignedStep(',
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
foreach ($match in [regex]::Matches($source, 'private const (float|int) (ConsistPathSampleDistanceEpsilon|ConsistPathInterpolationEpsilon|ConsistPathDirectionMinDot|RailConnectionDistanceEpsilon|RailConnectionBridgePointTolerance|BranchInternalOverlapMinTangentDot|RailDirectionReferenceDeadZone|RailNetworkAdvanceMaxHops) = [^;]+;')) {
    $generated += $match.Value + "`n"
}
foreach ($signature in @(
    'protected struct RailSample', 'private struct ConnectedTrainRailMove', 'private struct ConsistPathSample', 'private struct InitialConsistPathRouteNode',
    'protected void TransferConsistPathTo(', 'private bool TryReverseConsistPathTape(',
    'private static RailSample ReverseRailConnectionBridgeSample(', 'private bool IsConsistPathTapeValid(',
    'private bool IsConsistPathAlignedWithPreparedTrainSamples(', 'private bool IsConsistPathSampleAlignedWithTrainStart(',
    'private void ResetConsistPathTape(', 'private void AddConsistPathSample(', 'private bool TrySampleConsistPathTape(',
    'private int FindConsistPathUpperBound(', 'private bool TrySampleBetweenConsistPathSamples(',
    'private static bool TryCreateLinearRailSample(', 'private static bool TryResolveRailConnectionProgressAtPoint(',
    'private static bool TryCreateRailConnectionBridgeSample(', 'private static bool TryCreateRailSampleAtDistance(',
    'private static bool IsSameRailPosition(', 'private static bool HasRailConnectionBridgeState(',
    'private static bool IsRailConnectionBridgeSample(', 'private static float ResolveConsistFollowOffset(',
    'private bool InitializeConsistPathTape(', 'private void RememberConsistPathOrder(',
    'private bool TryApplyActualConnectedTrainFollowOffsets(', 'private static float ResolveDesiredConsistPairSpacing(',
    'private bool TryBuildActualConsistPathSegment(', 'private bool TryBuildActualConsistPathSegmentInDirection(',
    'private bool TryBuildInitialConsistPathSegmentInDirection(', 'private bool TryBuildInitialConsistPathSegmentWithRouteSearch(',
    'private bool TryAddInitialConsistPathTargetRouteNode(', 'private bool TryAddInitialConsistPathInternalTargetRouteNode(',
    'private static bool AreInternalConsistBridgeTangentsAligned(', 'private void TryEnqueueInitialConsistPathEndpointRouteNode(',
    'private void TryEnqueueInitialConsistPathConnectionRouteNodes(', 'private int AddInitialConsistPathRouteNode(',
    'private bool HasInitialConsistPathRouteNode(', 'private void RebuildInitialConsistPathSamples(', 'private void ClearInitialConsistPathRouteScratch(',
    'private bool TryGetDistanceToTargetOnCurrentRail(', 'private static bool TryResolveInitialConsistSegmentDirection(',
    'private static Vector2 ResolveRailPathTangent(', 'private static Vector2 ResolveFacingTangentWithFallback(',
    'private static bool TryResolveTangentReferenceSign(',
    'private bool TryApplyPreparedConnectedTrainMoves(', 'private bool EnsureConsistPathTape(',
    'private bool TryLockConsistToRouteLeaderPath(', 'private bool AreConnectedTrainFollowOffsetsSettled(',
    'private void AppendConsistPathFrame(', 'private void RestoreConsistPathTape(', 'private void TrimConsistPathTape(',
    'private bool TryAdvanceAlongRailNetwork(', 'private bool TryAdvanceExistingRailConnectionBridge(',
    'private bool TryResolveRailConnectionBridge(', 'private static float ResolveRailConnectionBridgeProgress(',
    'private Vector2 ResolveRouteLeaderTravelDirection(', 'private static Vector2 AlignDirectionWithReference(',
    'private Vector2 ResolveConnectedTrainFacing(', 'private Vector2 ResolveConsistPathForward(',
    'private static bool TryResolveFacingFromConnectedTarget(', 'private static Vector2 ResolveFollowerFacingTangent(',
    'private bool TryApplyRememberedConsistOrder(', 'private bool CanReuseRememberedConsistOrder(',
    'private void PrepareConnectedTrainMovesForTravel(', 'private bool TryMovePushedConsistEndpointToFront(',
    'private int FindConnectedTrainMoveIndex(', 'private int FindDirectFrontNeighborMoveIndex(',
    'private bool TryEstimateDirectFrontRailGapDistance(', 'private float ResolveMaxDirectConnectedRailGapDistance(',
    'private int FindConsistEndpointMoveIndex(', 'private int FindNextDirectConnectedMoveIndex(',
    'private void MoveConnectedTrainMoveToFront(', 'private void MoveDrivenTrainToFront(Train drivenTrain)',
    'private void OrderConnectedTrainMovesFromLeader(', 'private bool IsTrainAlreadyOrdered(',
    'private bool AreTrainsMovementAdjacent(', 'private static bool AreTrainsDirectlyConnected(',
    'private bool TryEstimateForwardRailGapDistance('
)) { $generated += (Read-Member $signature) + "`n" }
foreach ($signature in @('protected virtual bool CanDockInDirection(', 'protected bool TryApplyDockingToSample(', 'private static bool TryResolveDockTravelDirection(', 'protected static bool TryBuildCurrentRailSample(', 'protected bool TryApplyConnectedTrainMemberDocking(', 'protected bool TryApplyStationDocking(')) {
    $generated += (Read-Member $signature) + "`n"
}
$generated += "}`n"
$trainSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/Train.cs'))
$spacingConstant = [regex]::Match($trainSource, 'public const float ConnectionCenterDistance = [^;]+;')
if (!$spacingConstant.Success) { throw 'Missing production spacing constant' }
$generated += 'public partial class Train { ' + $spacingConstant.Value + ' }'
$source = $trainSource
$generated += 'public partial class Train { ' + (Read-Member 'internal static Vector2 ResolveRailConnectionForward(') + (Read-Member 'private static Vector2 ResolveRailConnectionEndpointForward(') + ' }'
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/HUD/ObjectUI/ItemInfoDescription.cs'))
$uiMethod = (Read-Member 'private void RefreshSteamTrainInfo(')
[IO.File]::WriteAllText((Join-Path $probe 'TrainInfoUi.cs'), "public partial class TrainInfoProbe {`n" + $uiMethod + "`n}")
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PathTransferChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'InitialPathChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DepartureChecks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
