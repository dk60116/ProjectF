param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
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
$terrain = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs'))
$block = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/Block.cs'))
$program = "using System; using System.Collections.Generic; partial class TerrainGenerator {`n"
foreach ($signature in @(
    'private void BeginConveyorRuntimeRefreshBatch(', 'private void EndConveyorRuntimeRefreshBatch(',
    'public void WakeConveyorNetwork(', 'private void QueueDeferredConveyorNetworkWake(',
    'private void FlushDeferredConveyorNetworkWakes(', 'private void QueueConveyorDirectWake(',
    'private void ProcessQueuedConveyorBlockWake(', 'private int QueueStraightConveyorLineRetryWorkDirectFallback(')) {
    $program += (Member $terrain $signature) + "`n"
}
$program += "} partial class Block {`n"
foreach ($signature in @(
    'private bool TryMoveConveyorLaneCore(', 'private bool CanMoveConveyorLane(',
    'private bool CanMoveConveyorLaneUncached(', 'private bool TryGetCachedCanMoveConveyorLane(',
    'private void CacheCanMoveConveyorLane(', 'private static int GetCanMoveConveyorLaneCacheIndex(',
    'private void DelayConveyorLaneMoveAttempt(', 'private static void InvalidateConveyorCanMoveCaches(',
    'private bool SleepConveyorMoveAttempts(', 'private bool SleepConveyorLaneBlocked(',
    'private bool ClearMovableConveyorLaneSleepStates(', 'private bool CanRetryConveyorLaneMove(',
    'public bool ShouldTickActiveConveyor(', 'private bool HasConveyorReadyLaneForMove(',
    'private bool HasAnyConveyorObjectsNotMoveAttemptSleeping(', 'private bool HasMovableConveyorLaneSleepState(',
    'private bool CanWakeSleepingConveyorLaneCheap(', 'public bool WakeConveyorBlockedLaneWaiter(',
    'internal void WakeConveyorVacatedLanePredecessor(')) {
    $program += (Member $block $signature) + "`n"
}
$program += "}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-WakeResearch-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $program)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>')
dotnet run -c Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
