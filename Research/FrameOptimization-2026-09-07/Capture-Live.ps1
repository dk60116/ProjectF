param(
    [string]$Phase = 'baseline',
    [int]$Seconds = 25,
    [int]$PerfEvery = 0,
    [string]$Before = '',
    [string]$After = ''
)
$ErrorActionPreference = 'Stop'
function Invoke-GameDiagnostic([string]$Command) {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $client.Connect('127.0.0.1', 50877)
        $client.ReceiveTimeout = 10000
        $client.SendTimeout = 10000
        $stream = $client.GetStream()
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false), 1024, $true)
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $false, 1024, $true)
        try {
            $writer.AutoFlush = $true
            $writer.WriteLine($Command)
            $response = $reader.ReadLine()
            if ($response -notmatch '^ok(?: |$)') { throw "Diagnostic failed: $response" }
            return $response
        } finally {
            $reader.Dispose()
            $writer.Dispose()
        }
    } finally { $client.Dispose() }
}
function Read-Tokens([string]$Response) {
    $values = [ordered]@{}
    foreach ($match in [regex]::Matches($Response, '(\w+)=("[^"]*"|\S+)')) {
        $values[$match.Groups[1].Value] = $match.Groups[2].Value.Trim('"')
    }
    return $values
}
$output = Join-Path $PSScriptRoot ($Phase + '.jsonl')
if (Test-Path -LiteralPath $output) { throw "Capture already exists: $output" }
$initial = Invoke-GameDiagnostic 'status'
[IO.File]::WriteAllText((Join-Path $PSScriptRoot ($Phase + '-initial-status.txt')), $initial)
Write-Output "Initial: $initial"
try {
    if ($Before) { Write-Output (Invoke-GameDiagnostic $Before) }
    Start-Sleep -Seconds 3
    for ($index = 0; $index -lt $Seconds; $index++) {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-GameDiagnostic 'status'
        $sample = [ordered]@{
            capturedAt = [DateTimeOffset]::Now.ToString('o')
            phase = $Phase
            index = $index
            status = Read-Tokens $response
        }
        if ($PerfEvery -gt 0 -and ($index % $PerfEvery) -eq 0) {
            $perf = Invoke-GameDiagnostic 'perf 256'
            if ($perf -notmatch 'perfData=([^ ]+)') { throw 'Missing perfData' }
            $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Matches[1]))
            $sample.perf = $json | ConvertFrom-Json
        }
        $sample.requestMs = $clock.Elapsed.TotalMilliseconds
        [IO.File]::AppendAllText($output, (($sample | ConvertTo-Json -Depth 16 -Compress) + [Environment]::NewLine))
        $remaining = 1000 - [int]$clock.ElapsedMilliseconds
        if ($remaining -gt 0) { Start-Sleep -Milliseconds $remaining }
    }
} finally {
    if ($After) { Write-Output (Invoke-GameDiagnostic $After) }
}
$samples = Get-Content -LiteralPath $output | ForEach-Object { $_ | ConvertFrom-Json }
$frames = @($samples | ForEach-Object { [double]$_.status.frameMs } | Sort-Object)
$fps = $samples | ForEach-Object { [double]$_.status.fps } | Measure-Object -Average -Minimum -Maximum
[pscustomobject]@{ Phase=$Phase; Samples=$samples.Count; MeanFps=[math]::Round($fps.Average,2); MinFps=$fps.Minimum; MaxFps=$fps.Maximum; MedianWindowMs=$frames[[int][math]::Floor($frames.Count/2)]; MaxWindowMs=$frames[-1] } | Format-List
