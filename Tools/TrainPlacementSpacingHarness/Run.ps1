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
$generated = "using System.Collections.Generic;`nusing UnityEngine;`nusing Quaternion = PreviewRotation;`npublic partial class Train {`n"
foreach ($signature in @('public bool ConnectTo(', 'public bool CanConnectTo(', 'public void ClearTrainConnections(', 'private static bool TryGetConnectionPose(', 'private bool AddTrainConnection(', 'internal void SetConnectionEnd(', 'internal bool TryGetConnectionFacingSign(', 'internal static Vector2 ResolveRailConnectionForward(', 'private static Vector2 ResolveRailConnectionEndpointForward(', 'public void DisconnectFrom(')) {
    $generated += Read-Member $train $signature
}
foreach ($signature in @('public static bool CanConnectByPose(', 'internal static bool CanConnectByPose(', 'private static bool IsConnectionOffsetInRange(', 'internal static float ResolveConnectionMaxCenterDistance(', 'private static float Cross(')) {
    $generated += Read-Member $train $signature
}
$generated += "`n}`npublic partial class RailHandcar {`n"
$handcar = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/RailHandcar.cs'))
$directionDeadZone = [regex]::Match($handcar, 'private const float RailDirectionReferenceDeadZone = [^;]+;')
if (!$directionDeadZone.Success) { throw 'Missing production rail direction dead zone' }
$generated += $directionDeadZone.Value
foreach ($signature in @('protected struct RailSample', 'private struct ConnectedTrainRailMove', 'private struct ConsistPathSample', 'private Vector2 ResolveConnectedTrainFacing(', 'private static bool TryResolveFacingFromConnectedTarget(', 'private Vector2 ResolveConsistPathForward(', 'private int FindConsistPathUpperBound(', 'private static bool HasRailConnectionBridgeState(', 'private static bool TryCreateRailConnectionBridgeSample(')) {
    $generated += Read-Member $handcar $signature
}
foreach ($signature in @('private static Vector2 ResolveFollowerFacingTangent(', 'private void LogRailFacingDiscontinuity(')) {
    $generated += Read-Member $handcar $signature
}
# Execute the actual manual-input dispatch through rail selection, before speed/physics integration.
$manualInput = Read-Member $handcar 'public override void HandleMountedInput(Vector3 worldMoveDirection, float moveSpeed, float deltaTime)'
$manualEnd = $manualInput.IndexOf('        if (ShouldReleaseForeignPushForReverseInput(', [StringComparison]::Ordinal)
if ($manualEnd -lt 0) { throw 'Missing manual rail-selection boundary' }
$generated += $manualInput.Substring(0, $manualEnd).Replace('public override void HandleMountedInput(', 'public void TestManualInput(')
$generated += "TestManualFacing = currentFacing; TestManualInputAxis = inputAxis; }`n"
foreach ($signature in @('private bool TryAdvanceExistingRailConnectionBridge(', 'private bool TryResolveRailConnectionBridge(',
    'private static float ResolveRailConnectionBridgeProgress(', 'private static bool TryCreateRailSampleAtDistance(',
    'protected virtual bool TryResolvePreferredConnectedRailTravelDirection(', 'private Vector2 ResolveFacingTangent(',
    'private static Vector2 ResolveFacingTangentWithFallback(', 'private static bool TryResolveTangentReferenceSign(',
    'private static float ResolveRailConnectionTapeDistance(', 'private static RailSample ReverseRailConnectionBridgeSample(')) {
    $generated += Read-Member $handcar $signature
}
$generated += "`n}`n"
$placement = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InstallationPlacementController.cs'))
$generated += "public partial class InstallationPlacementController {`n"
foreach ($signature in @('private struct TrainPlacementRailSample', 'private struct TrainCollisionBox2D', 'private bool TrySnapTrainBlueprintToConnection(', 'private void TrySnapTrainBlueprintToNeighbor(', 'private static bool TryResolveTrainConnectionPlacementFacing(', 'private static bool TrySnapTrainPlacementRailSampleToStraightCellCenter(', 'private bool CanPlaceTrainAtPose(', 'private static bool HasRequiredTrainPlacementClearance(', 'private bool TrainCollisionBoxesOverlapWith(', 'private static bool TrainCollisionBoxesOverlap(', 'private static bool TrainCollisionBoxesOverlapOnAxis(', 'private bool TryRotateSelectedSteamTrain(', 'private void ConnectTrainToNearbyTrains(', 'private void CompleteTrainPlacement(')) {
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
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BlueprintChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditChecks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
