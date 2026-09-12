$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Read-Member([string]$file, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $file))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$tempDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-AnimalAI-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDirectory | Out-Null
foreach ($file in @('AnimalAIWorld.cs', 'AnimalAIWorld.Spatial.cs', 'AnimalGridPathfinder.cs', 'AnimalGridPathfinder.Regions.cs', 'AnimalSimulationMath.cs', 'AnimalNeedsSchedule.cs', 'AnimalAIProfiler.cs')) {
    Copy-Item -LiteralPath (Join-Path $repo ('FactorioProject/Assets/Scripts/Object/Animal/' + $file)) -Destination $tempDirectory
}
$generated = "using UnityEngine; using ProjectF.Animals; using System;`npublic partial class MapObjectTickManager {`n"
$manager = 'FactorioProject/Assets/Scripts/Manager/MapObjectTickManager.cs'
foreach ($sig in @('private void Update()', 'public static bool WaitingForWorldLoad', 'private void ResetSimulationUpsMeasurement()', 'private void UpdateSimulationUpsMeasurement()')) {
    $generated += (Read-Member $manager $sig) + "`n"
}
$generated += "}`npublic partial class AnimalAIController {`n"
$controller = 'FactorioProject/Assets/Scripts/Object/Animal/AnimalAIController.cs'
foreach ($sig in @('public void TickNeeds(', 'internal bool TickScheduledNeeds(', 'internal void FlushPendingNeeds(', 'internal void TickCulledPresentation(', 'public void TickPresentation(')) {
    $generated += (Read-Member $controller $sig) + "`n"
}
$generated += "}`n"
$generated += "public partial class ActorScheduleProbe {`n"
foreach ($sig in @('public bool QueueScheduledTick(', 'public bool ExecuteScheduledTick()', 'private void ResetScheduledTick()', 'private void BeginPresentation(', 'public void TickPresentation(')) {
    $generated += (Read-Member $controller $sig) + "`n"
}
$generated += "}`n"
$generated += "public partial class AnimalAnimationProbe {`n"
foreach ($sig in @('internal void SetBehaviorAnimationActive(', 'public void SetAIAnimation(', 'private void PlayDeathAnimation()', 'private void OnDisable()')) {
    $generated += (Read-Member 'FactorioProject/Assets/Scripts/Object/Animal/Animal.cs' $sig) + "`n"
}
$generated += "}`n" + (Read-Member $controller 'public enum AnimalAIState') + "`npublic partial class AnimalControllerProbe {`n"
foreach ($sig in @('public void SetBehaviorExecutionActive(', 'private void SyncBehaviorAnimationActivity()', 'private void ApplyAnimation(')) {
    $generated += (Read-Member $controller $sig) + "`n"
}
$generated += "}`n"
Copy-Item -LiteralPath (Join-Path $repo 'Tools/ConveyorCameraCullingHarness/AnimalAnimationChecks.cs') -Destination $tempDirectory
[IO.File]::WriteAllText((Join-Path $tempDirectory 'Extracted.cs'), $generated)
foreach ($file in @('Checks.cs', 'Stubs.cs', 'ProfilerStubs.cs', 'NavigationChecks.cs', 'ActorScheduleProbe.cs')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $tempDirectory
}
[IO.File]::WriteAllText((Join-Path $tempDirectory 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $tempDirectory 'Probe.csproj')
exit $LASTEXITCODE
