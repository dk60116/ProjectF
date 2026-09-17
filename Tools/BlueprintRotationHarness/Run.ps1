param([switch]$BeforeFix)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$path = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InstallationPlacementController.cs'
$source = if ($BeforeFix) { (git -C $repo show "HEAD:$path") -join "`n" } else { [IO.File]::ReadAllText((Join-Path $repo $path)) }
function Read-Block([string]$text, [string]$signature) {
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production block: $signature" }
    $end = $text.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($text[$end] -eq '{') { $depth++ }
        if ($text[$end] -eq '}') { $depth-- }
        $end++
    }
    return $text.Substring($start, $end - $start)
}
$rotation = Read-Block $source 'private void RotateInstallPreviewClockwise()'
$branchStart = $rotation.IndexOf('else if (TryRotateSteamGeneratorPreviewInPlaceOnStraightPipe(', [StringComparison]::Ordinal)
$branchEnd = $rotation.IndexOf('if (shouldReanchorRotatedMultiCellPreview', $branchStart, [StringComparison]::Ordinal)
if ($branchStart -lt 0 -or $branchEnd -lt 0) { throw 'Rotation branch boundaries missing' }
# Execute the real caller branch, including normal rotation fallback, and its final
# anchored position application. Unrelated belt/train/selection UI paths are excluded.
$branch = $rotation.Substring($branchStart + 5, $branchEnd - $branchStart - 5)
$apply = (Read-Block $rotation 'else if (hasAnchorBlock)').Substring(5)
$generated = @"
using System;
using System.Collections.Generic;
public partial class Probe {
public void Rotate() {
    Block anchorBlock = CurrentBlock;
    bool hasAnchorBlock = anchorBlock != null;
    Vector2Int anchorCoordinate = Marker;
    bool preservePreviewTransformOnRotation = false;
    bool shouldReanchorRotatedMultiCellPreview = false;
    $branch
    if (hasAnchorBlock) Marker = anchorCoordinate;
    $apply
    CurrentBlock = anchorBlock;
}
"@
foreach ($signature in @(
    'private bool TryRotateSteamGeneratorPreviewInPlaceOnStraightPipe(',
    'private static bool RectGridObjectCoordinatesMatch(',
    'private Vector3 GetPreviewWorldPosition(',
    "private Vector3 ResolvePlacementWorldPosition(`n        Block anchorBlock,")) {
    $normalized = $source.Replace("`r`n", "`n")
    $generated += (Read-Block $normalized $signature) + "`n"
}
$generated += '}'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BlueprintRotation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0219;0649</NoWarn></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
