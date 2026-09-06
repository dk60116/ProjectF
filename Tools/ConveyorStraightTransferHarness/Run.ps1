$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Member([string]$path, [string]$signature) {
    $text = [IO.File]::ReadAllText((Join-Path $repo $path))
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $text.IndexOf('{', $start) + 1; $depth = 1
    while ($depth -gt 0 -and $end -lt $text.Length) {
        if ($text[$end] -eq '{') { $depth++ }
        if ($text[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $text.Substring($start, $end - $start)
}
$block = 'FactorioProject/Assets/Scripts/Map/Block.cs'
$state = 'FactorioProject/Assets/Scripts/Map/Block.SimulationState.cs'
$source = "using System; using System.Collections.Generic; using UnityEngine; using ProjectF.Conveyors; partial class CurrentBlock : Block {`n"
foreach ($member in @('public bool TryMoveStraightConveyorDataLaneTo(', 'public bool TryMoveStraightConveyorDataLaneToCached(')) {
    $source += (Member $block $member) + "`n"
}
$source += "} partial class Block {`n"
foreach ($member in @('public bool HasStraightConveyorDataItemAtLane(', 'private bool IsConveyorStorageLaneIndex(', 'private int GetConveyorStoredItemIdAtLane(', 'private int GetConveyorItemIdAtLane(', 'private bool HasConveyorItemAtLane(', 'private PortableObject GetConveyorPortableObjectAtLane(', 'private bool WasConveyorItemMovedThisFrame(', 'private bool IsConveyorItemReadyToMoveAtLane(', 'private ConveyorDataMotionState InitializeConveyorDataMotionTiming(', 'private ConveyorDataMotionState EnsureConveyorDataMotionTiming(', 'private float EvaluateConveyorDataMotionProgress(')) {
    $source += ([regex]::Replace((Member $block $member), '^private ', 'public ')) + "`n"
}
$source += "}`n"
foreach ($member in @('internal struct ConveyorCornerContinuation', 'internal struct ConveyorDataMotionState', 'internal struct ConveyorPickupGateState')) {
    $source += (Member $state $member) + "`n"
}
$source += @'
partial class CurrentBlock {
    public override bool Transfer(Block destination, int from, int to, float path, bool uncached) => uncached
        ? TryMoveStraightConveyorDataLaneTo(destination, from, to)
        : destination != null && TryMoveStraightConveyorDataLaneToCached(destination, from, to, path);
}
partial class LegacyBlock {
    public override bool Transfer(Block destination, int from, int to, float path, bool uncached) => uncached
        ? TryMoveStraightConveyorDataLaneTo(destination, from, to)
        : destination != null && CanMoveStraightConveyorDataLaneToCached(destination, from, to)
          && TryMoveStraightConveyorDataLaneToCached(destination, from, to, path);
}
'@
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ConveyorTransfer-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyBlock.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/ConveyorMotionTiming.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
