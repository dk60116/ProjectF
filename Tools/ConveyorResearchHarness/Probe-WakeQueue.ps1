param()
$ErrorActionPreference = 'Stop'

# Compile selected production methods with a managed world boundary; never launch Unity.
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw
function Read-Method([string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production method: $signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced method: $signature" }
    $source.Substring($start, $end - $start)
}

$program = @'
using System;
using System.Collections.Generic;
static class Application { public static bool isPlaying = true; }
record struct BlockHandle(int Id);
class Block { public BlockHandle Handle = new(1); }
class TerrainGenerator {
    bool IsConveyorRuntimeRefreshDeferred;
    readonly HashSet<BlockHandle> deferredConveyorNetworkWakeBlocks = new();
    readonly List<BlockHandle> conveyorTickBuffer = new();
    readonly Dictionary<BlockHandle,int> conveyorNetworkIds = new() { [new(1)] = 1 };
    readonly Dictionary<int,float> conveyorNetworkRetryTimes = new();
    readonly HashSet<int> conveyorNetworkSleepingIds = new();
    readonly HashSet<int> conveyorNetworkSleepCheckQueuedIds = new();
    readonly HashSet<BlockHandle> conveyorDirectWakeBlocks = new();
    readonly HashSet<BlockHandle> conveyorCornerGroupWakeQueuedBlocks = new();
    readonly HashSet<int> conveyorCornerGroupWakeQueued = new();
    readonly Dictionary<int,List<BlockHandle>> conveyorCornerGroupWakeBlocksById = new();
    readonly Queue<int> conveyorCornerGroupWakeQueue = new();
    readonly Block block = new();
    int wakeCount;
    bool TryGetRuntimeBlockHandle(Block b, out BlockHandle handle) { handle = b.Handle; return true; }
    bool TryResolveLoadedRuntimeBlock(BlockHandle h, out Block b) { b = block; return true; }
    bool TryGetCachedConveyorCornerGroupSlot(Block b, out int id, out int slot, out int count, out bool cycle) {
        id=1; slot=0; count=1; cycle=false; return true;
    }
    void EnsureConveyorNetworkCache() { }
    void RefreshSleepAwakeDebugVisualsForNetwork(int id) { }
    void QueueConveyorWake(Block b) { wakeCount++; }
'@

foreach ($signature in @(
    'public void WakeConveyorNetwork(',
    'private void QueueDeferredConveyorNetworkWake(',
    'private void FlushDeferredConveyorNetworkWakes(',
    'private bool TryQueueConveyorCornerGroupWake(',
    'private void ClearQueuedConveyorCornerGroupWakeBlocks(')) {
    $program += Read-Method $signature
}

$program += @'
    int ProbeWake(bool deferred, bool requestQueue) {
        wakeCount=0;
        IsConveyorRuntimeRefreshDeferred=deferred;
        WakeConveyorNetwork(block, requestQueue);
        IsConveyorRuntimeRefreshDeferred=false;
        FlushDeferredConveyorNetworkWakes();
        return wakeCount;
    }
    public static int Main() {
        var terrain=new TerrainGenerator();
        int failures=0;
        foreach (bool deferred in new[]{false,true})
        foreach (bool request in new[]{false,true}) {
            int actual=terrain.ProbeWake(deferred,request), expected=request?1:0;
            Console.WriteLine($"deferred={deferred} queueWake={request}: expected {expected}, actual {actual}");
            if (actual!=expected) failures++;
        }

        // ClearQueued... and normal group processing both remove the dictionary entry
        // and clear the list. Check whether the next wake retains that list for reuse.
        terrain.TryQueueConveyorCornerGroupWake(terrain.block);
        var first=terrain.conveyorCornerGroupWakeBlocksById[1];
        terrain.ClearQueuedConveyorCornerGroupWakeBlocks(1);
        terrain.conveyorCornerGroupWakeQueued.Clear();
        terrain.conveyorCornerGroupWakeQueue.Clear();
        terrain.TryQueueConveyorCornerGroupWake(terrain.block);
        Console.WriteLine($"Corner wake list reused after clear: {ReferenceEquals(first,terrain.conveyorCornerGroupWakeBlocksById[1])}");
        Console.WriteLine($"Wake-intent contract failures: {failures}. Scene/queue boundaries stubbed; selected production methods unchanged. No engine launched.");
        return failures==0 ? 0 : 1;
    }
}
'@

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-WakeResearch-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Program.cs') -Value $program
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
