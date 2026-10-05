$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Member([string]$relative, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $repo ('FactorioProject/Assets/Scripts/' + $relative)))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1; $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }; if ($source[$end] -eq '}') { $depth-- }; $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}
$arm = 'Object/MapObj/InstallationObject/RobotArmInstance.cs'
$world = 'Map/RobotArmWorld.cs'
$view = 'Simulation/Presentation/RobotArmWorldView.cs'
$generated = "using System; using System.Collections.Generic; using UnityEngine; using ProjectF.Rendering; using RobotArmState = RobotArm.RobotArmState; using TransferState = RobotArm.TransferState;`n"
$generated += "public class RobotArm {`n" + (Member 'Object/MapObj/InstallationObject/RobotArm.cs' 'public enum RobotArmState') + "`n"
$generated += (Member 'Object/MapObj/InstallationObject/RobotArm.cs' 'public sealed class TransferState') + "`n}`npublic partial class RobotArmInstance {`n"
foreach ($signature in @('internal void PersistTransferState(', 'public TransferState CaptureTransferState(',
    'private void WriteTransferState(', 'private static bool IsTurningState(', 'private Quaternion GetOutputBodyLocalRotation(',
    'private bool TryGetElectricOperationalPowerRequirement(', 'internal enum PlannedTransferCommand',
    'internal Quaternion BodyRotation', 'private void SetBodyLocalRotation(', 'private bool RotateBodyToward(',
    'private void AdvanceAnimation(', 'private void BeginPlannedTick(', 'private float ResolvePoweredDeltaTime(',
    'private void TickWaitBeforePickupTake(', 'private void TickWaitAfterPickupTake(', 'private void TickWaitBeforeDropPlace(',
    'private void TickWaitAfterDropPlace(', 'private void TickTurnToDrop(', 'private void TickTurnToPickup(',
    'private static bool TickTimerStillRunning(', 'private bool CanRuntimeSleepInCurrentState(')) { $generated += (Member $arm $signature) + "`n" }
$generated += "}`npublic partial class RobotArmWorld {`n"
foreach ($signature in @('internal RobotArmRenderTemplate GetTemplate(', 'private static Vector2Int GetMarkerCell(',
    'internal void BuildNearby(', 'public bool TryRaycast(', 'private static bool TryRaycastSphere(')) {
    $generated += (Member $world $signature) + "`n"
}
$generated += "}`npublic partial class RobotArmWorldView {`n"
foreach ($signature in @('private void BindCollider(', 'public void Unbind(', 'private void RefreshColliders(', 'private void ClearColliders(')) {
    $generated += (Member $view $signature) + "`n"
}
$generated += "}`nnamespace ProjectF.Rendering {`n" + (Member 'Rendering/CameraRenderCulling.cs' 'internal struct SpatialRayCellTraversal') + "`n}`n"
$generated += "internal partial class RobotArmRenderTemplate {`n"
foreach ($signature in @('internal Vector3 HandWorld(', 'private Matrix4x4 EvaluateHandLocalChain(',
    'private Matrix4x4 EvaluateChain(', 'private Quaternion EvaluateRotation(', 'private static Quaternion EvaluateAnimationRotation(')) {
    $generated += (Member 'Map/RobotArmRenderTemplate.cs' $signature) + "`n"
}
$generated += "}`n"
$generated += "namespace ProjectF.Diagnostics {`n" + (Member $world 'internal sealed class RobotArmTickTiming') + "`n}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RobotOptimization-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MotionChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'StateTickChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/RobotArmRuntimeState.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup></Project>')
dotnet run -c Release --project (Join-Path $probe 'Probe.csproj')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# Verify the engine-only template/registration wiring omitted from the managed probe.
$worldSource = [IO.File]::ReadAllText((Join-Path $repo ('FactorioProject/Assets/Scripts/' + $world)))
if ($worldSource.Contains('view.Bind(') -or $worldSource.Contains('view?.Bind(')) { throw 'Registration eagerly creates arm colliders.' }
$registration = Member 'Map/TerrainGenerator.RobotArms.cs' 'public RobotArmInstance RegisterDataOnlyRobotArm('
if (!$registration.Contains('RegisterDataOnlyInstallationSharedState')) { throw 'Arm registration copies store-owned placement state.' }
$template = Member 'Map/RobotArmRenderTemplate.cs' 'internal RobotArmRenderTemplate('
if (!$template.Contains('Definition = InputOutputModule.ResolveItemDefinition(itemId);') -or !$template.Contains('ElectricUseWatts = ItemDefinition.ResolveElectricUseWatts(Definition);')) { throw 'Template does not cache authoritative item power.' }
$entitySource = [IO.File]::ReadAllText((Join-Path $repo ('FactorioProject/Assets/Scripts/' + $arm)))
if (!$entitySource.Contains('BoundItemDefinition => Template.Definition;')) { throw 'Arm still searches definitions in its hot path.' }
foreach ($name in @('Pickup', 'Drop')) {
    if ($entitySource.Contains('"Robot Arm ' + $name + ' Query"')) { throw "Unsampled per-entity $name query timing remains." }
    if (!$entitySource.Contains('World.TickTiming.Measure(ProjectF.Diagnostics.RobotArmTickTiming.Phase.' + $name + 'Query)')) { throw "Missing sampled $name query timing." }
}
$savedPickup = Member $arm 'private void TryResolveSavedPickupCandidate('
if ($savedPickup.Contains('GetHandWorldPosition()') -or !$savedPickup.Contains('Vector3 referenceWorldPosition,')) { throw 'Saved pickup recalculates the hand reference.' }
$pickup = Member $arm 'private bool TryResolvePickupCandidate('
if ($pickup -notmatch 'TryResolveSavedPickupCandidate\(\s*terrainGenerator,\s*pickupCoordinate,\s*hasLoadedPickupBlock,\s*referenceWorldPosition,') { throw 'Saved pickup does not reuse the loaded query hand reference.' }
$drop = Member $arm 'private bool TryPlaceHeldItem('
if (!$drop.Contains('Vector3 dropStartWorldPosition = dropReferenceWorldPosition;')) { throw 'Drop start recalculates the same hand position.' }
if ($entitySource.Contains('GetHandRestWorldPosition') -or $entitySource.Contains('GetDropReferencePosition')) { throw 'Obsolete hand-position aliases remain.' }
$player = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Character/Player/PlayerController.cs'))
if (!$player.Contains('RobotArmWorld.Current.TryRaycast(')) { throw 'Remote mouse focus lost its data-only arm query.' }
Write-Output 'PASS registration without native colliders, shared save ownership, cached power/definition and remote mouse focus wiring.'
Write-Output 'PASS sampled pickup/drop queries, shared hand references and removal of obsolete position aliases.'
