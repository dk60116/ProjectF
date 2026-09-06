$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Member([string]$path, [string]$signature) {
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
$runtime = 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs'
$fields = 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.cs'
$source = "using System; using System.Collections.Generic; partial class CurrentScheduler : SchedulerFixture {`n"
foreach ($member in @(
    'private bool TryQueueConveyorCornerGroupWake(',
    'private bool TryTickConveyorCornerGroup(',
    'private bool TryAddConveyorCornerGroupTickBlock(',
    'private void ClearQueuedConveyorCornerGroupWakeBlocks(',
    'private void ClearConveyorCornerGroupWakeQueue(',
    'private void ReturnConveyorCornerWakeBuffer(',
    'private bool QueueConveyorLineWake(int lineId, ConveyorLineWakeRange wakeRange)',
    'private void DeferConveyorLineWake(',
    'private int PromoteDeferredConveyorLineWakes(',
    'private bool TryHandleStraightConveyorLineWakeRetry(')) {
    $source += (Member $runtime $member) + "`n"
}
$runtimeText = [IO.File]::ReadAllText((Join-Path $repo $runtime))
$poolLimit = [regex]::Match($runtimeText, 'private const int MaxPooledConveyorCornerWakeBuffers = \d+;')
if (-not $poolLimit.Success) { throw 'Missing pool limit' }
$source += $poolLimit.Value + "`n} partial class SchedulerFixture {`n"
foreach ($member in @('private sealed class ConveyorCornerGroup', 'private readonly struct ConveyorCornerGroupSlot', 'private struct ConveyorLineWakeRange', 'private struct ConveyorLineRetryState')) {
    # Expose nested production value types to the fixtures; bodies are unchanged.
    $source += ([regex]::Replace((Member $fields $member), '^private ', 'public ')) + "`n"
}
$source += "}`n"
$wrappers = @'
    public override bool QueueCorner(Block block) => TryQueueConveyorCornerGroupWake(block);
    public override bool TickCorner(int group, float deltaTime) => TryTickConveyorCornerGroup(group, deltaTime);
    public override void ClearCorner(int group) => ClearQueuedConveyorCornerGroupWakeBlocks(group);
    public override void ClearAllCorners() => ClearConveyorCornerGroupWakeQueue();
    public override bool QueueLine(int id, ConveyorLineWakeRange range) => QueueConveyorLineWake(id, range);
    public override void DeferLine(int id, ConveyorLineWakeRange range) => DeferConveyorLineWake(id, range);
    public override int PromoteLines() => PromoteDeferredConveyorLineWakes();
'@
foreach ($name in @('CurrentScheduler', 'LegacyScheduler')) {
    $source += "partial class $name {`n" + $wrappers + "`n}`n"
}
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ConveyorWake-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyScheduler.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
