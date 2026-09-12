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
    if ($depth -ne 0) { throw "Unbalanced member: $signature" }
    $text.Substring($start, $end - $start)
}
$author = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs'
$arm = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArmInstance.cs'
$world = 'FactorioProject/Assets/Scripts/Map/RobotArmWorld.cs'
$manager = 'FactorioProject/Assets/Scripts/Manager/MapObjectTickManager.cs'
$source = "using System; using System.Collections.Generic; using UnityEngine; using RobotArmState = RobotArm.RobotArmState;`n"
$source += (Member $manager 'public static class DeterministicSimulationUnits') + "`n"
$source += "public class RobotArm {`n" + (Member $author 'public enum RobotArmState') + "`n" + (Member $author 'public sealed class TransferState') + "`n}`n"
$source += "public partial class RobotArmInstance {`n"
foreach ($member in @('internal enum PlannedTransferCommand', 'public bool TryGetElectricPowerDemand(', 'private static bool IsActiveTransferState(', 'internal void WakeRuntimeSleep(', 'private void TickDrop(', 'private void ApplyPlannedDrop(', 'private void BeginDropRetryDelay(', 'private static bool TickTimerStillRunning(', 'private void NormalizeRuntimeState(', 'public void PlanManagedUpdateTick(', 'public void ApplyManagedUpdateTick(', 'private bool RefreshRuntimeSleepState(', 'private bool ShouldRunRuntimeSleepCheck(', 'private bool CanRuntimeSleepInCurrentState(', 'private bool ShouldRuntimeSleep(', 'private bool ShouldRuntimeSleepWithHeldItem(', 'private bool CanPlaceHeldItemForCurrentPlan(', 'private bool CanPickupOneItemForCurrentPlan(')) {
    $source += (Member $arm $member) + "`n"
}
$source += "}`npublic partial class RobotArmWorld {`n"
foreach ($member in @('private void Observe(', 'public void Wake(', 'public void PlanManagedUpdateTick(', 'public void ApplyManagedUpdateTick(')) {
    $source += (Member $world $member) + "`n"
}
$source += "}`npublic partial class SchedulingProbe {`n"
foreach ($member in @('private void CollectDueUpdateEntries(', 'private void PlanStagedUpdateEntries(', 'private void ApplyDueUpdateEntries(', 'private static int CompareUpdateTickEntries(', 'private static long ResolveSimulationId(', 'private sealed class UpdateTickEntry', 'private sealed class UpdateTickBucket')) {
    $source += (Member $manager $member) + "`n"
}
$source += "}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RobotEcs-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/ResourceStateSlots.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE

