$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RailWorld-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
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
$root = 'FactorioProject/Assets/Scripts'
$handcarPrefab = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/MapObject/Train/Rail handcar/Rail handcar.prefab'))
$handcarDefinition = 'FactorioProject/Assets/Data/Items/Item_53_Wooden wheel.asset'
$handcarMeta = [IO.File]::ReadAllText((Join-Path $repo ($handcarDefinition + '.meta')))
$definitionGuid = [regex]::Match($handcarMeta, '(?m)^guid: ([a-f0-9]{32})\s*$').Groups[1].Value
if (!$definitionGuid -or !$handcarPrefab.Contains("itemDefinition: {fileID: 11400000, guid: $definitionGuid, type: 2}")) {
    throw 'Rail handcar interaction checks must follow the prefab-bound ItemDefinition, not the asset filename.'
}
$handcarAsset = [IO.File]::ReadAllText((Join-Path $repo $handcarDefinition)).Replace("`r`n", "`n")
$boardingGuid = '4fade2dbe45025648acb64b3e0bfe229'
$alightingGuid = '7b654ce797ed8754f8f5bfc320ab51fb'
$expectedIcons = "  interactionButtonList:`n  - {fileID: 21300000, guid: $boardingGuid, type: 3}`n  - {fileID: 21300000, guid: $alightingGuid, type: 3}`n"
if (!$handcarAsset.Contains($expectedIcons)) {
    throw 'Rail handcar ItemDefinition must expose Boarding at index 0 and Alighting at index 1; HUD hides buttons without these icons.'
}
foreach ($icon in @(@('Boarding', $boardingGuid), @('Alighting', $alightingGuid))) {
    $meta = [IO.File]::ReadAllText((Join-Path $repo ("FactorioProject/Assets/Image/UI/Item/" + $icon[0] + '.png.meta')))
    if (!$meta.Contains('guid: ' + $icon[1]) -or !$meta.Contains('textureType: 8')) { throw "Invalid vehicle interaction sprite: $($icon[0])" }
}
Write-Output 'PASS prefab-bound rail handcar Boarding/Alighting sprite references (asset contract)'
$rail = [IO.File]::ReadAllText((Join-Path $repo "$root/Object/MapObj/InstallationObject/Railload.cs"))
$generated = "using System.Collections.Generic; using UnityEngine; public partial class Railload {`n"
foreach ($constant in @('CurveSegmentCount', 'CurveRadius', 'CenterPathSmoothingIterations', 'RailEndpointCellHalfExtent')) {
    $generated += [regex]::Match($rail, "private const (int|float) $constant = [^;]+;").Value + "`n"
}
foreach ($signature in @(
    'internal static void BuildCenterPath(', 'internal static void ExtendPathEndpointsToCellEdges2D(',
    'private static Vector2 NormalizeFlatDirection(', 'private static void ExtendPathEndpointsToCellEdges(',
    'private static Vector3 FlattenDirection(', 'private static float ResolveCellEdgeExtension(Vector3',
    'private static float ResolveCellEdgeExtension(Vector2', 'private static void SmoothCenterPath(',
    'private static void AddBezierPathPoints(', 'private static Vector3 EvaluateCubicBezier(',
    'private static Vector3 CoordinateToLocalPoint(', 'private static Vector3 DirectionToLocal(',
    'public static Vector2Int NormalizeCardinalDirection(', 'private static bool IsUnitCardinal(',
    'private static void AddPathPoint(', 'private bool TryEnsureRenderedPathSamples()',
    'internal bool TryGetPathData(', 'public bool TrySampleRenderedPath(',
    'public bool TryFindNearestRenderedPathSample(', 'public bool TryGetRenderedPathLength(',
    'public bool TryGetRenderedEndpointSample(', 'public bool TryFindNearestPathPointAndTangent('
)) { $generated += (Read-Member $rail $signature) + "`n" }
$generated += "}`n"
[string]$railPlacement = [IO.File]::ReadAllText((Join-Path $repo "$root/Object/MapObj/InstallationObject/RailloadInstallationController.cs"))
$generated += "public partial class RailloadInstallationController {`n"
foreach ($constant in [regex]::Matches($railPlacement, '(?m)^\s*private const float [^;]+;')) {
    $generated += $constant.Value + "`n"
}
$generated += (Read-Member $railPlacement 'private static readonly Vector2Int[] ConnectionProbeOffsets') + ";`n"
$generated += 'private static readonly List<ProjectF.Railway.IRailTarget> connectionCandidates = new List<ProjectF.Railway.IRailTarget>(8);' + "`n"
$generated += (Read-Member $railPlacement 'private sealed class RailPathPlan') + "`n"
$geometryStart = $railPlacement.IndexOf('    private RailPathPlan BuildPlan(', [StringComparison]::Ordinal)
$geometryEnd = $railPlacement.IndexOf('    private void ValidatePlan(', $geometryStart, [StringComparison]::Ordinal)
if ($geometryStart -lt 0 -or $geometryEnd -le $geometryStart) { throw 'Missing rail placement geometry boundary' }
$generated += $railPlacement.Substring($geometryStart, $geometryEnd - $geometryStart) + "`n}`n"
[string]$installation = [IO.File]::ReadAllText((Join-Path $repo "$root/Object/MapObj/InstallationObject/InstallationObject.cs"))
$generated += (Read-Member $installation 'public enum InstallationMapFilter') + "`n"
$placement = [IO.File]::ReadAllText((Join-Path $repo "$root/Object/MapObj/InstallationObject/InstallationPlacementController.cs"))
$generated += "public partial class InstallationPlacementController {`n"
foreach ($signature in @('private static bool IsInstallationObjectAllowedForPlacement(',
    'private static bool IsRailloadSource(', 'private static bool CanInstallGridSourceShareSavedInstallationCell(',
    'private static bool TryResolveInstallationObject(', 'public static InstallationMapFilter ResolvePlacementMapFilter(')) {
    $generated += (Read-Member $placement $signature) + "`n"
}
$generated += "}`n"
$train = [IO.File]::ReadAllText((Join-Path $repo "$root/Object/MapObj/InstallationObject/Vehicle/SteamTrain.cs"))
$generated += "public partial class SteamTrain : Train {`n"
$generated += (Read-Member $train 'private static class AutoDriveRoutePlanner').Replace(
    'private static class AutoDriveRoutePlanner', 'private static partial class AutoDriveRoutePlanner')
$generated += "}`n"
$debugRenderer = [IO.File]::ReadAllText((Join-Path $repo "$root/Object/MapObj/InstallationObject/RailLineDebugRenderer.cs"))
$generated += "public partial class RailLineDebugRenderer {`n"
foreach ($constant in [regex]::Matches($debugRenderer, '(?m)^\s*(public|private) const float [^;]+;')) {
    $generated += $constant.Value + "`n"
}
$generated += (Read-Member $debugRenderer 'private static readonly Color[] GroupPalette') + ";`n"
foreach ($signature in @('public void SetVisible(', 'public void RefreshNow()', 'private void LateUpdate()', 'private void Rebuild()',
    'private void CollectRails()', 'private void ApplyRailLine(', 'private int ApplyRailDirectionArrows(',
    'private void ApplyArrowSegment(', 'private void DisableAllRenderers()', 'private void DisableRailArrowRenderers(',
    'private static Color ResolveGroupColor(', 'private bool TryFindRailInfoIndex(', 'private sealed class RailInfo')) {
    $generated += (Read-Member $debugRenderer $signature) + "`n"
}
$generated += "}`n"
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
foreach ($file in @('Simulation/Railway/RailWorld.cs', 'Simulation/Railway/RailPathData.cs',
    'Simulation/Railway/RailWorld.Runtime.cs', 'Simulation/Railway/RailwayTargets.cs', 'MapObjects/MapObjectHandle.cs',
    'Simulation/Core/RailPathSampling.cs', 'Object/MapObj/InstallationObject/RailConnectionUtility.cs',
    'Object/MapObj/InstallationObject/RailLineDebugRenderer.Directions.cs',
    'Object/MapObj/InstallationObject/Train station.cs')) {
    Copy-Item -LiteralPath (Join-Path $repo "$root/$file") -Destination $probe
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Boundaries.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RuntimeChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DebugRendererChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RailPlacementChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RailCurveChecks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><LangVersion>9.0</LangVersion></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$blockPlacement = Read-Member $placement 'private bool CanPlacePreviewOnTargetBlockType('
$occupantCheck = $blockPlacement.IndexOf('IsInstallationObjectAllowedForPlacement(occupyingObject, footprintSource, allowedFilter)', [StringComparison]::Ordinal)
$terrainCheck = $blockPlacement.IndexOf('&& !CanPlaceOnTerrainBiome(block, allowedFilter)', [StringComparison]::Ordinal)
if ($occupantCheck -lt 0 -or $terrainCheck -lt 0 -or $terrainCheck -gt $occupantCheck) {
    throw 'Live rail/train co-occupancy must use the shared placement rule after terrain validation.'
}
Write-Output 'PASS live placement applies the shared occupancy rule after terrain validation (source contract)'
foreach ($file in @('Map/TerrainGenerator.TrainStations.cs', 'Object/MapObj/InstallationObject/Vehicle/SteamTrain.cs',
    'Object/MapObj/InstallationObject/RailLineDebugRenderer.cs')) {
    $source = [IO.File]::ReadAllText((Join-Path $repo "$root/$file"))
    if ($source -match 'FindObjects(OfType|ByType)<(Railload|Trainstation)>') { throw "Scene-wide rail/station scan remains: $file" }
}
Write-Output 'PASS rail/station consumers use the world registry (source contract)'
$placementSource = [IO.File]::ReadAllText((Join-Path $repo "$root/Object/MapObj/InstallationObject/RailloadInstallationController.cs"))
$commit = Read-Member $placementSource 'public bool CommitCurrentPreview('
if ($commit.Contains('CreateInstallationObject(') -or !$commit.Contains('RegisterDataOnlyRailwayState(')) {
    throw 'Rail placement must register data directly without instantiating an installed rail.'
}
foreach ($file in @('Train.cs', 'RailHandcar.cs', 'SteamTrain.cs', 'FreightCar.cs', 'TrainPlacementSpacing.cs')) {
    $source = [IO.File]::ReadAllText((Join-Path $repo "$root/Object/MapObj/InstallationObject/Vehicle/$file"))
    if ($source -match '\b(Railload|Trainstation)\s+\w+') { throw "Concrete rail/station reference remains in train runtime: $file" }
}
$terrainRailway = [IO.File]::ReadAllText((Join-Path $repo "$root/Simulation/Railway/TerrainGenerator.Railway.cs"))
if (!$terrainRailway.Contains('RegisterDataOnlyInstallationSharedState(')) { throw 'Station UI edits must share the store-owned DTO.' }
$registration = Read-Member $terrainRailway 'internal bool RegisterDataOnlyRailwayState('
if ($registration.IndexOf('state.itemName = BlockStateStore.ResolveInstallationSaveItemName(') -lt 0 -or
    $registration.IndexOf('state.itemName = BlockStateStore.ResolveInstallationSaveItemName(') -gt $registration.IndexOf('RegisterDataOnlyInstallationSharedState(')) {
    throw 'Data-only rail/station identity must be named before persistence clones the DTO.'
}
$view = [IO.File]::ReadAllText((Join-Path $repo "$root/Simulation/Railway/RailWorldView.cs"))
if (!$view.Contains('batches.RenderBatches(Camera.main);') -or !$view.Contains('cell.Dirty')) { throw 'Rail presentation must retain dirty-cell geometry and bounds-based culling.' }
Write-Output 'PASS direct data placement, target-only train references, shared persistence and rail presentation source contracts'
