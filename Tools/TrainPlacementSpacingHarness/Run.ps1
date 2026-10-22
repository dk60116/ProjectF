$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-TrainSpacing-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$train = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/Train.cs'))
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
    $source.Substring($start, $end - $start)
}
$constant = [regex]::Match($train, 'public const float ConnectionCenterDistance = [^;]+;')
if (!$constant.Success) { throw 'Missing production spacing constant' }
[IO.File]::WriteAllText((Join-Path $probe 'Spacing.cs'), ('public partial class Train { ' + $constant.Value + ' }'))
$generated = "using System.Collections.Generic;`nusing ProjectF.Railway;`nusing UnityEngine;`nusing Quaternion = PreviewRotation;`npublic partial class Train {`n"
foreach ($signature in @('public bool ConnectTo(', 'public bool CanConnectTo(', 'public void ClearTrainConnections(', 'private static bool TryGetConnectionPose(', 'internal void SetConnectionEnd(', 'internal bool TryGetConnectionFacingSign(', 'internal static Vector2 ResolveRailConnectionForward(', 'private static Vector2 ResolveRailConnectionEndpointForward(', 'public void DisconnectFrom(')) {
    $generated += Read-Member $train $signature
}
$generated += Read-Member $train 'internal float ResolveRailEndClearance('
$generated += Read-Member $train 'private BoxCollider ResolveTrainBodyCollider('
$generated += Read-Member $train 'internal Vector3 ResolveFootprintLocalCenter('
$generated += Read-Member $train 'internal float ResolveStationCenterOffset('
$generated += [regex]::Match($train, 'private const float MinConnectionDistance = [^;]+;').Value
foreach ($signature in @('internal float ResolveConnectionReach(', 'internal float ResolveMaximumConnectionReach(', 'internal static float ResolveConnectionSpacing(')) {
    $generated += Read-Member $train $signature
}
foreach ($signature in @('public static bool CanConnectByPose(', 'internal static bool CanConnectByPose(', 'private static bool IsConnectionOffsetInRange(', 'internal static float ResolveConnectionMaxCenterDistance(', 'private static float Cross(')) {
    $generated += Read-Member $train $signature
}
$generated += Read-Member $train 'public bool TryGetCurrentRailSample('
$generated += Read-Member $train 'internal Vector2 ResolveRailAnchorPoint('
$generated += Read-Member $train 'protected bool ApplyRailPoseToRail('
$generated += Read-Member $train 'protected void SetCurrentRailSample('
$generated += Read-Member $train 'public bool TryGetCurrentRailPose('
$generated += Read-Member $train 'public bool TryGetCurrentRailPoseUnits('
$generated += Read-Member $train 'public virtual void ApplyPlacedRailSampleUnits('
$generated += Read-Member $train 'internal bool TryGetCurrentRailConnectionTransition('
$generated += Read-Member $train 'internal void ConfigureCurrentRailConnectionTransition('
$generated += Read-Member $train 'internal void ClearCurrentRailConnectionTransition('
foreach ($signature in @('internal TrainInstance RuntimeTrain', 'protected override ref ProjectF.Simulation.VehicleMotionState MotionState',
    'protected override void OnEnable(', 'protected override void OnDisable(', 'public override void PrepareForPool(',
    'private void OnDestroy(', 'protected override void OnRuntimeMapObjectHandleChanged(',
    'protected override void OnPlacementRuntimeChanged(', 'private void SynchronizeTrainPlacement(',
    'protected override void OnPlacementRuntimeCleared(', 'public void ClearPlacedRailSample(', 'private void ClearCurrentRailSample(',
    'public bool TryGetTouchingUnconnectedTrain(', 'private void CollectConnectionActionGroup(')) {
    $generated += Read-Member $train $signature
}
$vehicle = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/Vehicle.cs')).Replace("`r`n", "`n")
$start = $vehicle.IndexOf('    private ProjectF.Simulation.VehicleMotionState vehicleMotion;', [StringComparison]::Ordinal)
$end = $vehicle.IndexOf('    private float pendingWheelVisualDistance;', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Missing vehicle motion adapter boundary' }
$generated += "`n}`npublic class VehicleMotionProbe : MapObject { private float pendingWheelVisualDistance;`n" + $vehicle.Substring($start, $end - $start)
foreach ($signature in @("protected float UpdateVehicleSignedSpeed(`n", 'protected void ClampCurrentVehicleSignedSpeed(', 'protected void ResetVehicleMotion(')) {
    $generated += Read-Member $vehicle $signature
}
foreach ($signature in @('public override void PrepareForPool(', 'protected override void OnDisable(')) {
    $generated += Read-Member $vehicle $signature
}
$generated += 'public float Speed => currentVehicleSignedSpeed; public float Drive(float input, float dt) => UpdateVehicleSignedSpeed(input, dt, 2, 4, 4); public void Clamp(float speed) => ClampCurrentVehicleSignedSpeed(speed); public void Stop() => ResetVehicleMotion();'
$generated += "`n}`npublic class TrainMotionProbe : VehicleMotionProbe { private TrainInstance runtimeTrain = TrainWorld.Shared.Create(); public TrainInstance RuntimeTrain => runtimeTrain; " + (Read-Member $train 'protected override ref ProjectF.Simulation.VehicleMotionState MotionState')
$generated += "`n}`npublic partial class RailHandcar {`n"
$handcar = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/RailHandcar.cs'))
foreach ($constant in @('RailDirectionReferenceDeadZone', 'RailNetworkAdvanceMaxHops', 'RailConnectionBridgePointTolerance', 'ConsistPathInterpolationEpsilon', 'ConsistPathSampleDistanceEpsilon')) {
    $match = [regex]::Match($handcar, "private const (float|int) $constant = [^;]+;")
    if (!$match.Success) { throw "Missing production constant: $constant" }
    $generated += $match.Value
}
foreach ($signature in @('protected struct RailSample', 'private struct ConnectedTrainRailMove', 'private struct ConsistPathSample', 'private Vector2 ResolveConnectedTrainFacing(', 'private static bool TryResolveFacingFromConnectedTarget(', 'private Vector2 ResolveConsistPathForward(', 'private int FindConsistPathUpperBound(', 'private static bool HasRailConnectionBridgeState(', 'private static bool TryCreateRailConnectionBridgeSample(')) {
    $generated += Read-Member $handcar $signature
}
foreach ($signature in @('private static Vector2 ResolveFollowerFacingTangent(', 'private void LogRailFacingDiscontinuity(')) {
    $generated += Read-Member $handcar $signature
}
$generated += Read-Member $handcar 'private bool TryResolveTrainBodyRailPose('
$generated += Read-Member $handcar 'private bool TrySampleTrainBodySupport('
foreach ($signature in @('public bool TryGetRailDockDeltaAtCoordinate(', 'public bool TryGetRailDockDistanceAtCoordinate(',
    'public bool TryDockConnectedTrainGroupAtRailCoordinate(', 'protected bool TryApplyDockingToSample(',
    'private static bool TryResolveDockTravelDirection(', 'private bool TryMoveConnectedTrainGroupForDocking(',
    'protected virtual bool CanDockInDirection(', 'protected float ResolveDockCompleteDistance(',
    'protected static bool TryBuildCurrentRailSample(', 'private bool TryFindStationCenterDockSample(',
    'protected bool TryFindRailDockSampleAtCoordinate(', 'private bool TryFindStationRailDockSample(')) {
    $generated += Read-Member $handcar $signature
}
foreach ($signature in @('private bool TrySampleConsistPathTape(', 'private bool TrySampleBetweenConsistPathSamples(',
    'private static bool TryCreateLinearRailSample(', 'private static bool TryResolveRailConnectionProgressAtPoint(')) {
    $generated += Read-Member $handcar $signature
}
foreach ($signature in @('private static bool TryPrepareBranchRailTransition(', 'private bool TryFindRailConnectionSample(',
    'private static bool TryUpdateEndpointConnectionSample(')) {
    $generated += Read-Member $handcar $signature
}
# Use the full drive entry for late-track tests and the selection prefix for junction tests.
$manualInput = Read-Member $handcar 'public override void HandleMountedInput(Vector3 worldMoveDirection, float moveSpeed, float deltaTime)'
$generated += $manualInput.Replace('public override void HandleMountedInput(', 'public void TestDriveInput(')
$manualEnd = $manualInput.IndexOf('        if (ShouldReleaseForeignPushForReverseInput(', [StringComparison]::Ordinal)
if ($manualEnd -lt 0) { throw 'Missing manual rail-selection boundary' }
$generated += $manualInput.Substring(0, $manualEnd).Replace('public override void HandleMountedInput(', 'public void TestManualInput(')
$generated += "TestManualFacing = currentFacing; TestManualInputAxis = inputAxis; TestManualSample = currentSample; }`n"
foreach ($signature in @('private bool TryGetStoredRailSample(', 'private bool TryResolveCurrentRailSample(',
    'private bool TryFindBestRailSample(', 'private static bool IsRailConnectionBridgeSample(',
    'private static void RestoreStoredRailConnectionTransition(', 'private bool TryResolveRailSampleForTrain(',
    'private bool TryAdvanceAlongRailNetwork(', 'private static Vector2 ResolveRailPathTangent(',
    'private float ResolveRailEndLimitedMoveDistance(')) {
    $generated += Read-Member $handcar $signature
}
foreach ($signature in @('private bool TryAdvanceExistingRailConnectionBridge(', 'private bool TryResolveRailConnectionBridge(',
    'private static float ResolveRailConnectionBridgeProgress(', 'private static bool TryCreateRailSampleAtDistance(',
    'protected virtual bool TryResolvePreferredConnectedRailTravelDirection(', 'private Vector2 ResolveFacingTangent(',
    'private static Vector2 ResolveFacingTangentWithFallback(', 'private static bool TryResolveTangentReferenceSign(',
    'private static float ResolveRailConnectionTapeDistance(', 'private static RailSample ReverseRailConnectionBridgeSample(')) {
    $generated += Read-Member $handcar $signature
}
$generated += "`n}`n"
$locomotive = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/Locomotive.cs'))
$generated += "public partial class Locomotive {`n" + (Read-Member $locomotive 'internal bool TryGetRearFreightCar(') + "`n}`n"
$freight = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/FreightCar.cs'))
$generated += "public partial class FreightCar {`n"
foreach ($signature in @('private bool TryGetFuelSupplyLocomotive(', 'private void RefreshFuelRoleIcon(', 'private void RefreshFuelRoleIconPosition(')) {
    $generated += Read-Member $freight $signature
}
$generated += "`n}`n"
$player = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Character/Player/PlayerController.cs'))
$generated += "public partial class PlayerController {`n"
$generated += Read-Member $player 'private static void SetTrainFocusVisible('
$generated += "`n}`n"
$placement = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InstallationPlacementController.cs'))
$generated += "public partial class InstallationPlacementController {`n"
foreach ($signature in @('private struct TrainPlacementRailSample', 'private struct TrainCollisionBox2D', 'private bool TrySnapTrainBlueprintToConnection(', 'private void TrySnapTrainBlueprintToNeighbor(', 'private static bool TrySnapTrainPlacementRailSampleToStraightCellCenter(', 'private bool CanPlaceTrainAtPose(', 'private bool CanPlaceTrainFootprintAtPose(', 'private static bool HasRequiredTrainPlacementClearance(', 'private bool TrainCollisionBoxesOverlapWith(', 'private static bool TrainCollisionBoxesOverlap(', 'private static bool TrainCollisionBoxesOverlapOnAxis(', 'private bool TryRotateSelectedSteamTrain(', 'private void ConnectTrainToNearbyTrains(', 'private void CompleteTrainPlacement(')) {
    $generated += Read-Member $placement $signature
}
# Exercise the actual edit-mode dispatch, stopping before unrelated item placement.
$complete = Read-Member $placement 'private void HandleInstallCompleteClicked('
$generated += $complete.Substring(0, $complete.IndexOf('        if (!IsInstallationModeActive())', [StringComparison]::Ordinal)) + "}`n"
$generated += "`n}`n"
$generated = $generated.Replace('ProjectF.Railway.IRailTarget', 'Railload')
[IO.File]::WriteAllText((Join-Path $probe 'Orientation.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/TrainPlacementSpacing.cs') -Destination $probe
$spacingPath = Join-Path $probe 'TrainPlacementSpacing.cs'
[IO.File]::WriteAllText($spacingPath, [IO.File]::ReadAllText($spacingPath).Replace('ProjectF.Railway.IRailTarget', 'Railload'))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FacingChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ManualFacingChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'JunctionMotionChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RailAcquisitionChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BlueprintChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WorldChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EndStopChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LongCarChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CurveBodyChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'StationCenterChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FocusChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FuelRoleChecks.cs') -Destination $probe
$bodyPose = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Simulation/Railway/TrainBodyRailPose.cs'))
[IO.File]::WriteAllText((Join-Path $probe 'TrainBodyRailPose.cs'), $bodyPose.Replace('IRailTarget', 'global::Railload'))
$preparedMoves = Read-Member $handcar 'private bool TryApplyPreparedConnectedTrainMoves('
if ($preparedMoves -notmatch 'ResolveRailEndLimitedMoveDistance\([\s\S]+?TryAdvanceAlongRailNetwork\([\s\S]+?permittedDistance,') {
    throw 'Prepared consist movement must apply the body clearance before advancing its route leader'
}
$prefab = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/MapObject/Train/Diesel train/Diesel Train.prefab'))
$body = [regex]::Match($prefab, 'BoxCollider:[\s\S]*?m_Size: \{x: [^,]+, y: [^,]+, z: ([^}]+)\}[\s\S]*?m_Center: \{x: [^,]+, y: [^,]+, z: ([^}]+)\}')
if (!$body.Success -or $prefab -notmatch 'mapSizeX: 1\s+mapSizeY: 2') { throw 'Missing diesel two-cell body contract' }
[IO.File]::WriteAllText((Join-Path $probe 'DieselBody.cs'), ('static class DieselBody { public const float Length = ' + $body.Groups[1].Value.Trim() + 'f; public const float Center = ' + $body.Groups[2].Value.Trim() + 'f; }'))
$longPrefab = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/MapObject/Train/Long Flatcar/Long Flatcar.prefab'))
$fuelMaterial = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/MapObject/Train/Flatcar/FuelRoleIcon.mat'))
if (!$fuelMaterial.Contains('guid: c397bc2f1a714684a8ae08c44646d237') -or
    !$fuelMaterial.Contains('_Color: {r: 1, g: 1, b: 1, a: 1}') -or
    !$fuelMaterial.Contains('_ZTest: 4')) {
    throw 'Fuel-role icons must use the existing unlit sprite shader at full opacity while preserving depth occlusion'
}
$longBody = [regex]::Match($longPrefab, 'BoxCollider:[\s\S]*?m_Size: \{x: [^,]+, y: [^,]+, z: ([^}]+)\}[\s\S]*?m_Center: \{x: [^,]+, y: [^,]+, z: ([^}]+)\}')
if ($longPrefab -notmatch 'mapSizeX: 1\s+mapSizeY: 2\s+centerCellX: 0\s+centerCellY: [01]' -or
    !$longBody.Success -or [float]$longBody.Groups[1].Value -le 1 -or
    [float]$longBody.Groups[1].Value -gt 2 -or [float]$longBody.Groups[2].Value -ne 0.5) {
    throw 'Long Flatcar must have a two-cell footprint and a body centered between its occupied cells'
}
[IO.File]::WriteAllText((Join-Path $probe 'LongCarBody.cs'), ('static class LongCarBody { public const float Length = ' + $longBody.Groups[1].Value.Trim() + 'f; public const float Center = ' + $longBody.Groups[2].Value.Trim() + 'f; }'))
foreach ($file in @('TrainInstance.cs', 'TrainWorld.cs', 'TrainWorld.Native.cs')) {
    $source = [IO.File]::ReadAllText((Join-Path $repo "FactorioProject/Assets/Scripts/Simulation/Railway/$file"))
    [IO.File]::WriteAllText((Join-Path $probe $file), $source.Replace('IRailTarget', 'Railload'))
}
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/MapObjects/MapObjectHandle.cs') -Destination $probe
Copy-Item -Path (Join-Path $repo 'FactorioProject/Assets/Scripts/Simulation/Core/*.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
