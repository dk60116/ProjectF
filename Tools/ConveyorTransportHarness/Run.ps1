$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
dotnet run -c Release --project (Join-Path $PSScriptRoot 'ConveyorTransportHarness.csproj')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-TransportWorld-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
foreach ($file in @('ConveyorTransportStore.cs','ConveyorTransportRun.cs','Block.ConveyorTransport.cs','TerrainGenerator.ConveyorTransport.cs')) {
    Copy-Item -LiteralPath (Join-Path $repo "FactorioProject/Assets/Scripts/Map/$file") -Destination $probe
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WorldChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WakeChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TimingChecks.cs') -Destination $probe
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
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}
$blockSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/Block.cs'))
$dynamicRender = Member $blockSource 'public void AppendDynamicVirtualConveyorItemRenderData('
if ($dynamicRender.IndexOf('!HasDynamicVirtualConveyorItemVisuals()') -lt 0) {
    throw 'Dynamic belt item rendering must include transport-owned straight runs.'
}
if ($dynamicRender.IndexOf('!HasConveyorMotionStates()') -ge 0) {
    throw 'Dynamic belt item rendering must not depend only on legacy motion state.'
}
$reads = "using ProjectF.Conveyors; public partial class Block {`n"
foreach ($signature in @('private int GetConveyorStoredItemIdAtLane(', 'private ConveyorPickupGateState GetConveyorPickupGateStateAtLane(', 'private void SetConveyorPickupGateStateAtLane(', 'private void IncrementConveyorLaneOccupancyVersion(', 'private int GetConveyorLaneOccupancyVersion(', 'public int ConveyorItemVisualVersion')) {
    $reads += (Member $blockSource $signature) + "`n"
}
[IO.File]::WriteAllText((Join-Path $probe 'ProductionReads.cs'), $reads + "}`n")
# New direct identity writers must be reviewed for the ownership boundary.
if ([regex]::Matches($blockSource, 'conveyorItemIds\[\w+\] = [^;]+;').Count -ne 3) { throw 'Review changed conveyor item identity write sites.' }
$terrainSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs'))
$dispatch = Member $terrainSource 'private bool TryTickStraightConveyorLine(int lineId, ConveyorLineWakeRange wakeRange, Block directFallbackBlock)'
if ($dispatch.IndexOf('RouteOwnedConveyorLineWake') -lt 0 -or $dispatch.IndexOf('RouteOwnedConveyorLineWake') -gt $dispatch.IndexOf('HasStraightConveyorLineRetryWork(line')) {
    throw 'Owned lines must bypass legacy retry/blocker scans.'
}
$scheduler = "using System; using System.Collections.Generic; using UnityEngine; public partial class TerrainGenerator {`n"
foreach ($signature in @(
    'private void QueueConveyorDirectWake(',
    'private bool QueueConveyorLineWake(int lineId, ConveyorLineWakeRange wakeRange)',
    'private void DeferConveyorLineWake(',
    'private int PromoteDeferredConveyorLineWakes(',
    'private void ProcessQueuedConveyorBlockWake(',
    'private static void ResolveStraightConveyorLineWakeRange(',
    'private static void ResolveStraightConveyorLineSlotRange(')) {
    $scheduler += (Member $terrainSource $signature) + "`n"
}
$fields = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.cs'))
$scheduler += (Member $fields 'private struct ConveyorLineWakeRange') + "`n}`n"
[IO.File]::WriteAllText((Join-Path $probe 'ProductionScheduler.cs'), $scheduler)
$rebuildCount = Member $fields 'private void RebuildAuthoritativeConveyorItemTotal()'
if ($rebuildCount.IndexOf('CalculateConveyorItemCountSnapshot()') -lt 0 -or
    $rebuildCount.IndexOf('authoritativeConveyorItemTotalInitialized = true') -lt 0) {
    throw 'Authoritative conveyor item totals must rebuild from the complete runtime and saved snapshot.'
}
$finalizeLoad = Member $fields 'private void TryFinalizePendingWorldLoad()'
$expandIndex = $finalizeLoad.IndexOf('ExpandConveyorItemSaveRunsAfterBeltTopology', [StringComparison]::Ordinal)
$registrationIndex = $finalizeLoad.IndexOf('RefreshLoadedRuntimeRegistrations', [StringComparison]::Ordinal)
$countIndex = $finalizeLoad.IndexOf('RebuildAuthoritativeConveyorItemTotal', [StringComparison]::Ordinal)
if ($expandIndex -lt 0 -or $registrationIndex -le $expandIndex -or $countIndex -le $registrationIndex) {
    throw 'Saved-world finalization must rebuild the item total after run expansion and runtime registration.'
}
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>')
dotnet run -c Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
