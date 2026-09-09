$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

function Read-Member([string]$path, [string]$signature) {
    $text = [IO.File]::ReadAllText((Join-Path $repo $path))
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $text.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $text.Length) {
        if ($text[$end] -eq '{') { $depth++ }
        if ($text[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $text.Substring($start, $end - $start)
}

$blockPath = 'FactorioProject/Assets/Scripts/Map/Block.cs'
$armPath = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs'
$arm = [IO.File]::ReadAllText((Join-Path $repo $armPath))

if ($arm -notmatch 'Vector3 conveyorSelectionReferenceWorldPosition = GetBodyWorldPosition\(\);') {
    throw 'Conveyor preview does not use the robot-arm body position.'
}
# The resolver now returns the body reference for either conveyor source; the
# removal call consumes that reference through the shared three-argument API.
$resolver = Read-Member $armPath 'private bool TryResolvePickupCandidate('
if ($arm -notmatch 'TryTakeOneConveyorObject\(\s*referenceWorldPosition,\s*PickupItemFilter,\s*out pickedItemId\)' -or
    $resolver -notmatch 'if \(pickupSource == RobotArmPickupSource.Conveyor\s*\|\| pickupSource == RobotArmPickupSource.SavedConveyor\)\s*\{\s*referenceWorldPosition = conveyorSelectionReferenceWorldPosition;') {
    throw 'Loaded conveyor removal does not use the robot-arm body position for selection.'
}
if ($arm -notmatch 'TryTakeSavedConveyorItem\(pickupCoordinate, GetBodyWorldPosition\(\),') {
    throw 'Saved conveyor removal does not use the robot-arm body position for selection.'
}
if ($arm -notmatch 'TryPeekSavedConveyorItem\(\s*pickupCoordinate,\s*PickupItemFilter,\s*conveyorSelectionReferenceWorldPosition,') {
    throw 'Saved conveyor preview does not use the robot-arm body position for selection.'
}

$block = [IO.File]::ReadAllText((Join-Path $repo $blockPath))
$selectionParameter = $block.LastIndexOf('Vector3 selectionReferenceWorldPosition', [StringComparison]::Ordinal)
$methodStart = $block.LastIndexOf('private bool TryGetClosestConveyorItemLane(', $selectionParameter, [StringComparison]::Ordinal)
if ($selectionParameter -lt 0 -or $methodStart -lt 0) { throw 'Missing two-reference conveyor lane selector.' }
$methodBody = $block.IndexOf('{', $methodStart) + 1
$methodEnd = $methodBody
$methodDepth = 1
while ($methodDepth -gt 0 -and $methodEnd -lt $block.Length) {
    if ($block[$methodEnd] -eq '{') { $methodDepth++ }
    if ($block[$methodEnd] -eq '}') { $methodDepth-- }
    $methodEnd++
}
if ($methodDepth -ne 0) { throw 'Unbalanced two-reference conveyor lane selector.' }
$method = $block.Substring($methodStart, $methodEnd - $methodStart)
$source = @"
using System;
using UnityEngine;
partial class Block {
$method
}
"@

$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RobotArmBeltPickup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText(
    (Join-Path $probe 'Probe.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
