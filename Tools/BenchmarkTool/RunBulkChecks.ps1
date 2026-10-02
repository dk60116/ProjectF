$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $repo 'FactorioProject/Assets/Scripts'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BenchmarkBulk-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
# Only engine boundaries are doubled. Compile the real terrain bulk path and
# saved-state removal iterator, so cancellation and registration exercise source.
function Get-Method([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced method: $signature" }
    return $source.Substring($start, $end - $start)
}
$source = [IO.File]::ReadAllText((Join-Path $scripts 'Map/BlockStateStore.cs'))
$methods = Get-Method $source 'internal IEnumerator RemoveConveyorInstallations('
$methods += Get-Method $source 'private void RemoveUtilityPoleConnectionReferences('
[IO.File]::WriteAllText((Join-Path $probe 'Store.cs'), 'using System; using System.Collections; using System.Collections.Generic; using UnityEngine; public partial class BlockStateStore {' + $methods + '}')
$world = [IO.File]::ReadAllText((Join-Path $scripts 'Map/ConveyorWorld.cs'))
$methods = Get-Method $world 'internal void SynchronizeForWorldPresentation()'
foreach ($signature in @('internal void BeginBulkUpdate()', 'internal void EndBulkUpdate()')) {
    $start = $world.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing bulk renderer boundary: $signature" }
    $methods += $world.Substring($start, $world.IndexOf("`n", $start) - $start)
}
[IO.File]::WriteAllText((Join-Path $probe 'World.cs'), 'public partial class ConveyorWorld {' + $methods + '}')
$pipe = [IO.File]::ReadAllText((Join-Path $scripts 'Map/PipeWorld.cs'))
$methods = Get-Method $pipe 'internal void SynchronizeForWorldPresentation()'
[IO.File]::WriteAllText((Join-Path $probe 'Pipe.cs'), 'public partial class PipeWorld {' + $methods + '}')
$io = [IO.File]::ReadAllText((Join-Path $scripts 'Object/MapObj/InstallationObject/InputOutputModule.cs'))
$start = $io.IndexOf('private static int runtimePipeTopologyBatchDepth;', [StringComparison]::Ordinal)
$end = $io.IndexOf("`n", $io.IndexOf('internal static void BeginRuntimePipeTopologyBatch()', [StringComparison]::Ordinal))
if ($start -lt 0 -or $end -lt $start) { throw 'Missing fluid topology batch boundary' }
$methods = $io.Substring($start, $end - $start)
foreach ($signature in @('internal static void EndRuntimePipeTopologyBatch()', 'private static void NotifyRuntimePipeTopologyObservers(',
    'internal static void NotifyRuntimePipeTopologyChanged(', 'private static void WakeRuntimeFluidTopologyModules()')) {
    $methods += Get-Method $io $signature
}
[IO.File]::WriteAllText((Join-Path $probe 'IO.cs'), 'using System.Collections.Generic; using UnityEngine; public partial class InputOutputModule {' + $methods + '}')
$receiver = [IO.File]::ReadAllText((Join-Path $scripts 'Manager/RuntimeItemGiveReceiver.Benchmark.cs'))
$methods = Get-Method $receiver 'private IEnumerator LoadBenchmarkTerrain('
$methods += Get-Method $receiver 'private IEnumerator PrepareBenchmarkPresentation('
$spawn = Get-Method $receiver 'if (command.Action == BenchmarkAction.Spawn)'
[IO.File]::WriteAllText((Join-Path $probe 'Spawn.cs'), 'using System; using System.Collections; using ProjectF.Benchmark; using UnityEngine; public class SpawnProbe { public long benchmarkDone, benchmarkTotal, benchmarkStageDone, benchmarkStageTotal; public string benchmarkResult; ' + $methods + ' public IEnumerator Run(TerrainGenerator terrain, InstallationPlacementController placement, ItemDefinition item, BenchmarkCommand command, Vector2Int center) {' + $spawn + '} }')
$files = @(
    (Join-Path $probe 'Spawn.cs'),
    (Join-Path $probe 'Store.cs'),
    (Join-Path $probe 'World.cs'),
    (Join-Path $probe 'Pipe.cs'),
    (Join-Path $probe 'IO.cs'),
    (Join-Path $PSScriptRoot 'Tests/BulkChecks.cs'),
    (Join-Path $scripts 'Diagnostics/BenchmarkLayout.cs'),
    (Join-Path $scripts 'Diagnostics/BenchmarkCommand.cs'),
    (Join-Path $scripts 'Map/TerrainGenerator.Benchmark.cs'),
    (Join-Path $scripts 'Map/TerrainGenerator.Benchmark.Spawning.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
