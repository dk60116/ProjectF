param([switch]$CompileUnity)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Get-Content -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Character/Player/Player.cs') -Raw
function Read-Member([string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production animation member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PlayerAnimation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$generated = "using UnityEngine; public partial class Player {`n"
foreach ($name in @('PickHash', 'DiggingHash', 'ThrowHash', 'MoveAnimationSpeedHash', 'LocomotionBlendHash',
    'HandcartMountedHash', 'HandcartDirectionHash', 'RidingHash', 'PickStateName', 'DiggingStateName', 'IdleStateName',
    'RunningStateName', 'LocomotionBlendDampTime', 'pendingPickTriggerCount', 'wasPickStateActiveLastFrame',
    'pendingDiggingTrigger', 'wasDiggingStateActiveLastFrame', 'handcartAnimationActive', 'ridingAnimationActive')) {
    $match = [regex]::Match($source, "(?m)^    private [^\r\n]*\b$name\b[^\r\n]*;\r?$")
    if (!$match.Success) { throw "Missing production animation field: $name" }
    $generated += $match.Value + "`n"
}
foreach ($name in @('PickAnimationStartedThisFrame', 'DiggingAnimationStartedThisFrame', 'DiggingAnimationFinishedThisFrame')) {
    $generated += [regex]::Match($source, "(?m)^    public bool $name [^\r\n]+\r?$").Value + "`n"
}
foreach ($signature in @('public void QueuePickAnimation()', 'public void ClearQueuedPickAnimations()',
    'public void QueueDiggingAnimation()', 'public void CancelDiggingAnimation(', 'public bool TriggerThrowAnimation()',
    'public void StopImmediateActions()', 'public void UpdateMountedVehicleAnimation(',
    'public void ClearMountedVehicleAnimation()', 'public void SetRidingAnimation(',
    'public bool UpdateAnimationState(', 'private void InterruptDiggingAnimation(')) {
    $generated += (Read-Member $signature) + "`n"
}
foreach ($name in @('IsPickStateActive', 'IsDiggingStateActive')) {
    $generated += [regex]::Match($source, "(?m)^    private bool $name\(\) => [^\r\n]+\r?$").Value + "`n"
}
$generated += "}`n"
[IO.File]::WriteAllText((Join-Path $probe 'PlayerAnimationProduction.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Character/Player/Player.UpperBodyAnimation.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Player animation runtime checks failed.' }
$node = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'node_modules/yaml/package.json'))) {
    $pnpm = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback/pnpm.cmd'
    & $pnpm install --dir $PSScriptRoot --frozen-lockfile
    if ($LASTEXITCODE -ne 0) { throw 'Could not install the pinned asset-check YAML parser.' }
}
& $node (Join-Path $PSScriptRoot 'AssetChecks.cjs') $repo
if ($LASTEXITCODE -ne 0) { throw 'Player animation asset checks failed.' }
if ($CompileUnity) {
    & (Join-Path $repo 'Tools/MonsterDataHarness/Run.ps1') -CompileUnity
    if ($LASTEXITCODE -ne 0) { throw 'Unity compilation or combat regression checks failed.' }
}
