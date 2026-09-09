param(
    [string]$SnapshotPath = (Join-Path $PSScriptRoot 'baseline-snapshot.txt'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'baseline-analysis.json')
)
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$lines = [IO.File]::ReadAllLines((Resolve-Path -LiteralPath $SnapshotPath))
$metadata = [ordered]@{}
$runtime = [ordered]@{}
$belt = [ordered]@{}
$rows = [Collections.Generic.List[object]]::new()
$section = 'metadata'
foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -in @('BeltCountersPerFrame', 'BackgroundConveyorPerTick', 'RuntimeCounters', 'Rows')) {
        $section = $line
        continue
    }
    $cells = $line.Split([char]9)
    if ($section -eq 'metadata' -and $cells.Length -eq 2) { $metadata[$cells[0]] = $cells[1] }
    elseif ($section -eq 'BeltCountersPerFrame' -and $cells.Length -eq 2) {
        $belt[$cells[0]] = [double]::Parse($cells[1], $culture)
    }
    elseif ($section -eq 'RuntimeCounters' -and $cells.Length -ge 3 -and $cells[0] -ne 'Group') {
        $runtime[($cells[0] + '.' + $cells[1])] = $cells[2]
    }
    elseif ($section -eq 'Rows' -and $cells.Length -eq 10 -and $cells[0] -ne 'Rank') {
        $rows.Add([pscustomobject]@{
            Rank = [int]$cells[0]; Kind = $cells[1]; Item = $cells[2]; Type = $cells[3]
            Samples = [long]$cells[6]; TotalMs = [double]::Parse($cells[7], $culture)
            AvgUs = [double]::Parse($cells[8], $culture); MaxUs = [double]::Parse($cells[9], $culture)
        })
    }
}
$frames = [int]$metadata['BeltLoopProfileFrames']
if ($frames -le 0 -or $rows.Count -eq 0) { throw 'Snapshot must contain belt profile frames and timing rows.' }
$normalized = @($rows | ForEach-Object {
    [pscustomobject]@{
        Rank = $_.Rank; Kind = $_.Kind; Item = $_.Item; Type = $_.Type
        Samples = $_.Samples; TotalMs = $_.TotalMs
        MsPerBeltProfileFrame = $_.TotalMs / $frames
        CallsPerBeltProfileFrame = $_.Samples / [double]$frames
        AvgCallUs = $_.AvgUs; MaxCallUs = $_.MaxUs
    }
})
function TotalForType([string]$type) {
    $selected = @($rows | Where-Object Type -eq $type)
    if ($selected.Count -ne 1) { throw "Expected exactly one timing row for $type" }
    return $selected[0].TotalMs
}
$lineDetailTypes = @('ConveyorLineNoMove', 'ConveyorLineMoveScan', 'ConveyorLineRetryWork', 'ConveyorLineBlockerScan')
$lineDetailTotal = 0.0
foreach ($type in $lineDetailTypes) { $lineDetailTotal += TotalForType $type }
$result = [ordered]@{
    SourceSha256 = (Get-FileHash -LiteralPath $SnapshotPath -Algorithm SHA256).Hash
    Metadata = $metadata
    RuntimeCountersAsReported = $runtime
    BeltCountersPerFrameAsReported = $belt
    Rows = $normalized
    Derived = [ordered]@{
        WakeQueueFractionOfBeltTick = (TotalForType 'ConveyorProcessWakeQueue') / (TotalForType 'ActiveConveyor')
        LineTimeOutsideFourListedDetailsMsPerFrame = ((TotalForType 'ConveyorWakeLine') - $lineDetailTotal) / $frames
        GeneralMoveSuccessFraction = $belt['TryMoveSuccesses'] / $belt['TryMoveAttempts']
    }
    Limits = @(
        'Timing rows are inclusive; parents and children must not be added together.'
        'Runtime counters are snapshot values, not necessarily window averages.'
        'General move counters exclude some transport paths and are not total throughput.'
        'The line residual includes uninstrumented work and measurement overhead; it is not one measured function.'
        'No frame histogram, valid GPU timing, or binary/source identity proof is present.'
    )
}
$json = $result | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false))
$normalized | Select-Object -First 8 Rank, Type, MsPerBeltProfileFrame, CallsPerBeltProfileFrame | Format-Table -AutoSize
Write-Output "Saved $OutputPath"
