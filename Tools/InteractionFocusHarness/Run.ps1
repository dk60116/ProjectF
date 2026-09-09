$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Character/Player/PlayerController.cs'))
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
$generated = "using System.Collections.Generic;`npublic partial class PlayerController {`n"
foreach ($signature in @(
    'private void RefreshInteractionFocus()',
    'private void KeepClosestInteractionFocusTarget(',
    'private void ResetInteractionButtonFocusTargets()',
    'private void CacheInteractionButtonFocusTargets(',
    'private void CacheInteractionButtonFocusTarget(Block block)',
    'private void CacheInteractionButtonFocusTarget(MapObject target,',
    'private float GetMapObjectFocusSelectionDistanceSqr(',
    'private static Bounds CreateMapObjectStatusFocusBounds(')) {
    $generated += (Read-Member $signature) + "`n"
}
$generated += "}`n"

$selectedFilterMethod = Read-Member 'public bool TryGetSelectedItemFilterMapObject('
if (-not $selectedFilterMethod.Contains('currentSelectedMapObject')) {
    throw 'Item filter target is not sourced from the clicked selection.'
}
foreach ($forbidden in @('currentFocusedBlocks', 'FocusActivationRadius', 'CollectActiveRuntimeTrains')) {
    if ($selectedFilterMethod.Contains($forbidden)) {
        throw "Selected filter lookup still contains distance-based discovery: $forbidden"
    }
}

$hudSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/HUD/PlayerHUD.cs'))
function Read-HudMember([string]$signature) {
    $start = $hudSource.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing HUD member: $signature" }
    $end = $hudSource.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $hudSource.Length) {
        if ($hudSource[$end] -eq '{') { $depth++ }
        if ($hudSource[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced HUD member: $signature" }
    $hudSource.Substring($start, $end - $start)
}

$filterTargetMethod = Read-HudMember 'private bool TryGetClickedFilterTarget('
foreach ($required in @('TryGetClickedObjectInfoFocusedMapObject(', 'TryResolveTrainStation(', 'TryResolveSteamTrain(', 'TryResolveItemFilterTarget(')) {
    if (-not $filterTargetMethod.Contains($required)) {
        throw "Clicked filter routing is missing: $required"
    }
}
if ($filterTargetMethod.Contains('TryGetMouseFocusedMapObject(')) {
    throw 'Filter button target still depends on mouse hover.'
}

$filterVisibilityMethod = Read-HudMember 'private void UpdateItemFilterButtonState()'
if ((-not $filterVisibilityMethod.Contains('TryGetClickedFilterTarget(')) -or
    $filterVisibilityMethod.Contains('MouseFocusGrace')) {
    throw 'Filter button visibility must follow the clicked target without hover grace.'
}

$filterClickMethod = Read-HudMember 'private void HandleItemFilterButtonClicked()'
foreach ($required in @('ShowTrainStationFilter(', 'ShowTrainFilter(', 'ShowItemFilter(')) {
    if (-not $filterClickMethod.Contains($required)) {
        throw "Filter button cannot open every filter type: $required"
    }
}

$interactionResolutionMethod = Read-HudMember 'private bool TryResolveMapObjectInteraction('
foreach ($required in @('IsLoggingMachineFilterTarget(', 'TryResolveTrainStation(')) {
    if (-not $interactionResolutionMethod.Contains($required)) {
        throw "A filter-only target can still use the distance-based interaction button: $required"
    }
}

$interactionClickMethod = Read-HudMember 'private void HandleInteractionButtonClicked()'
foreach ($forbidden in @('ShowTrainStationFilter(', 'ShowItemFilter(', 'ShowTrainFilter(')) {
    if ($interactionClickMethod.Contains($forbidden)) {
        throw "A filter panel is still opened by the distance-based interaction button: $forbidden"
    }
}
Write-Output 'PASS filter buttons use clicked selection without player-distance discovery'

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-InteractionFocus-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
$checksPath = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0414;0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="' + $checksPath + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
