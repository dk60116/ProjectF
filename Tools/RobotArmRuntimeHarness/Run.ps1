$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Member([string]$path, [string]$signature) {
    $text = [IO.File]::ReadAllText((Join-Path $repo $path))
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $end = $text.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($text[$end] -eq '{') { $depth++ }
        if ($text[$end] -eq '}') { $depth-- }
        $end++
    }
    return $text.Substring($start, $end - $start)
}
$arm = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs'
$manager = 'FactorioProject/Assets/Scripts/Manager/MapObjectTickManager.cs'
$source = "using System; using System.Collections.Generic; using UnityEngine;`n"
$source += (Member $manager 'public static class DeterministicSimulationUnits') + "`n"
$source += "public partial class RobotArm {`n"
foreach ($member in @('public enum RobotArmState', 'private enum PlannedTransferCommand', 'public override bool TryGetElectricPowerDemand(', 'private static bool IsActiveTransferState(', 'private void WakeRuntimeSleep(', 'private bool ShouldIgnoreUnavailableDropWake(', 'protected override void WakeRuntimeUpdate(', 'private void SetUpdateTickRegistered(', 'private void TickDrop(', 'private void ApplyPlannedDrop(', 'private void BeginDropRetryDelay(', 'private static bool TickTimerStillRunning(', 'private void NormalizeRuntimeState(')) {
    $source += (Member $arm $member) + "`n"
}
$armSource = [IO.File]::ReadAllText((Join-Path $repo $arm))
$source += [regex]::Match($armSource, '(?m)^\s*public override float ManagedUpdateTickIntervalSeconds[^\r\n]+').Value
$source += "`n} public partial class SchedulingProbe {`n"
foreach ($member in @('private void CollectDueUpdateEntries(', 'private void PlanStagedUpdateEntries(', 'private void ApplyDueUpdateEntries(', 'private static int CompareUpdateTickEntries(', 'private static long ResolveSimulationId(', 'private sealed class UpdateTickEntry', 'private sealed class UpdateTickBucket')) {
    $source += (Member $manager $member) + "`n"
}
$source += "}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RobotRuntime-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
