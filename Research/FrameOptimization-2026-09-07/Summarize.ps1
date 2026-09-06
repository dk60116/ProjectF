$ErrorActionPreference = 'Stop'
$summaries = foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.jsonl') {
    $samples = @(Get-Content -LiteralPath $file.FullName | ForEach-Object { $_ | ConvertFrom-Json })
    $fps = @($samples | ForEach-Object { [double]$_.status.fps })
    $frameMs = @($samples | ForEach-Object { [double]$_.status.frameMs } | Sort-Object)
    $profiles = @($samples | Where-Object { $_.perf -and $_.perf.beltLoopProfileFrames -ge 10 } | ForEach-Object perf)
    $frameCount = ($profiles | Measure-Object -Property beltLoopProfileFrames -Sum).Sum
    $rowTotals = @{}
    foreach ($profile in $profiles) {
        foreach ($row in $profile.rows) {
            $key = "$($row.kind)/$($row.type)/$($row.itemName)"
            if (!$rowTotals.ContainsKey($key)) { $rowTotals[$key] = [ordered]@{ TotalUs=0.0; Samples=0; MaxUs=0.0 } }
            $rowTotals[$key].TotalUs += [double]$row.totalUs
            $rowTotals[$key].Samples += [int]$row.samples
            $rowTotals[$key].MaxUs = [math]::Max($rowTotals[$key].MaxUs, [double]$row.maxUs)
        }
    }
    $rows = @($rowTotals.Keys | ForEach-Object {
        [pscustomobject]@{ Name=$_; MsPerFrame=[math]::Round($rowTotals[$_].TotalUs / 1000 / $frameCount,4); CallsPerFrame=[math]::Round($rowTotals[$_].Samples/$frameCount,2); MaxCallUs=$rowTotals[$_].MaxUs }
    } | Sort-Object MsPerFrame -Descending)
    $counterAverages = [ordered]@{}
    foreach ($counterName in @('Frame/Fps','ProfilerCPU/MainThreadMs','ProfilerGPU/FrameGpuMs','ProfilerRender/SetPassCalls','ProfilerRender/Triangles','ProfilerRender/ShadowCasters','Virtualization/VirtualBeltTrackedTransformReads','Virtualization/VirtualBeltTrackedTransformUpdates','Render/RenderedChunkSurfaces','Conveyor/LoadedConveyorItems')) {
        $values = @($profiles | ForEach-Object runtimeCounters | Where-Object { "$($_.group)/$($_.name)" -eq $counterName } | ForEach-Object { [double]$_.value })
        if ($values.Count) { $counterAverages[$counterName] = [math]::Round(($values | Measure-Object -Average).Average,3) }
    }
    $loopAverages = [ordered]@{}
    if ($frameCount -gt 0) {
        foreach ($property in @('beltTryMoveAttempts','beltTryMoveSuccesses','beltStraightMoveAttempts','beltStraightMoveSuccesses','beltPlanMoveCalls','beltActiveLoopIterations')) {
            $total = 0.0
            foreach ($profile in $profiles) { $total += [double]$profile.$property * [int]$profile.beltLoopProfileFrames }
            $loopAverages[$property] = [math]::Round($total/$frameCount,3)
        }
    }
    [pscustomobject]@{
        Phase=$file.BaseName; Samples=$samples.Count
        From=$samples[0].capturedAt; To=$samples[-1].capturedAt
        MeanFps=[math]::Round(($fps | Measure-Object -Average).Average,2)
        MeanWindowMs=[math]::Round(($frameMs | Measure-Object -Average).Average,3)
        MinFps=($fps | Measure-Object -Minimum).Minimum
        MaxFps=($fps | Measure-Object -Maximum).Maximum
        MedianWindowMs=$frameMs[[int][math]::Floor($frameMs.Count/2)]
        MaxWindowMs=$frameMs[-1]
        MovingPlayerSamples=@($samples | Where-Object { [double]$_.status.playerSpeed -ne 0 }).Count
        InstallationCounts=@($samples | ForEach-Object { $_.status.installTotal } | Select-Object -Unique)
        ItemMin=($samples | ForEach-Object { [int]$_.status.beltItems } | Measure-Object -Minimum).Minimum
        ItemMax=($samples | ForEach-Object { [int]$_.status.beltItems } | Measure-Object -Maximum).Maximum
        WorldTimeFrom=$samples[0].status.time; WorldTimeTo=$samples[-1].status.time
        ProfileSnapshots=$profiles.Count; ProfileFrames=$frameCount
        Rows=$rows; Counters=$counterAverages; Loops=$loopAverages
    }
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'summary.json'), (ConvertTo-Json -InputObject @($summaries) -Depth 12))
$summaries | Select-Object Phase,Samples,MeanFps,MeanWindowMs,MinFps,MaxFps,MovingPlayerSamples,ItemMin,ItemMax,ProfileFrames | Format-Table -AutoSize
