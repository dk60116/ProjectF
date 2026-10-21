$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-TrainFocus-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
function Member([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced member: $signature" }
    $source.Substring($start, $end - $start)
}
$map = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/UI/MapFocus.cs'))
$block = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/Block.cs'))
$player = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Character/Player/PlayerController.cs'))
$generated = "using System.Collections.Generic;`npublic partial class MapFocus {`n"
foreach ($signature in @('public void SetAreaVisible(',
    'private void UpdateAreaLines(', 'private Vector2 MatchAreaSizeToSingleMarker(',
    'private static float MatchAreaAxisSizeToSingleMarker(', 'private float ResolveMatchedCornerCenterlineLength(',
    'private void SetAreaCornerLine(', 'private void SetAreaLinesVisible(', 'private void HideAreaLines()')) {
    $generated += (Member $map $signature) + "`n"
}
$generated += "}`npublic partial class Block {`n"
foreach ($signature in @('public void SetFocusVisible(', 'public void SetMouseFocusVisible(', 'public void SetSelectionFocusVisible(')) {
    # The area overload follows the bool-only overload.
    $start = $block.IndexOf($signature, [StringComparison]::Ordinal)
    $areaStart = $block.IndexOf($signature, $start + 1, [StringComparison]::Ordinal)
    $generated += (Member $block.Substring($areaStart) $signature) + "`n"
}
$generated += "}`npublic partial class PlayerController {`n"
$generated += (Member $player 'private static void SetTrainFocusVisible(') + "`n"
$dispatchStart = $player.IndexOf('private static void SetBlockFocusVisible(', [StringComparison]::Ordinal)
$areaDispatchStart = $player.IndexOf('private static void SetBlockFocusVisible(', $dispatchStart + 1, [StringComparison]::Ordinal)
if ($areaDispatchStart -lt 0) { throw 'Missing area focus dispatch.' }
$generated += (Member $player.Substring($areaDispatchStart) 'private static void SetBlockFocusVisible(') + "`n}`n"
$group = Member $player 'private void RefreshGroupedFocusMarkers('
if (-not $group.Contains('SetTrainFocusVisible(') -or
    $group.IndexOf('SetTrainFocusVisible(') -gt $group.IndexOf('else if (group.count <= 1)')) {
    throw 'Train focus must override the single occupied cell marker.'
}
foreach ($kind in @('interaction', 'mouse', 'selection')) {
    if (-not $block.Contains("focus.SetAreaVisible(true, focusColor, ${kind}FocusAreaCenter, ${kind}FocusAreaSize)")) {
        throw "Missing floor area focus routing: $kind"
    }
}
if ($map.Contains('areaFollowTarget') -or $block.Contains('FocusAreaTarget')) {
    throw 'Superseded model-follow state must not remain in the focus renderer.'
}
$prefab = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/MapObject/Train/Diesel train/Diesel Train.prefab'))
if ($prefab -notmatch 'mapSizeX: 1\s+mapSizeY: 2\s+centerCellX: 0\s+centerCellY: 0') {
    throw 'Diesel footprint fixture no longer matches the configured prefab.'
}
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
