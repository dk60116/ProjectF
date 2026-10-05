$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $repo 'FactorioProject/Assets/Scripts'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BenchmarkProgress-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$receiver = [IO.File]::ReadAllText((Join-Path $scripts 'Manager/RuntimeItemGiveReceiver.Benchmark.cs'))
$start = $receiver.IndexOf('if (command.Action == BenchmarkAction.RandomizeProgress)')
if ($start -lt 0) { throw 'Missing progress dispatch branch' }
$end = $receiver.IndexOf('{', $start) + 1; $depth = 1
while ($depth -gt 0) { if ($receiver[$end] -eq '{') { $depth++ }; if ($receiver[$end] -eq '}') { $depth-- }; $end++ }
$source = 'using ProjectF.Benchmark; public class ReceiverProgressProbe { private string BuildBenchmarkStatusTokens() => ""; public ToolResult Run(BenchmarkCommand command) {' + $receiver.Substring($start, $end - $start) + ' throw new System.Exception("Wrong action"); } }'
[IO.File]::WriteAllText((Join-Path $probe 'Receiver.cs'), $source)
foreach ($relative in @('Diagnostics/BenchmarkRuntime.Progress.cs', 'Diagnostics/BenchmarkWorkProgress.cs', 'Diagnostics/BenchmarkCommand.cs',
    'Diagnostics/BenchmarkLayout.cs', 'Simulation/Core/ProductionProcess.cs', 'Simulation/Core/SimulationTickContracts.cs')) {
    Copy-Item -LiteralPath (Join-Path $scripts $relative) -Destination $probe
}
# Only the global simulation time constants are needed by the common progress adapter.
[IO.File]::WriteAllText((Join-Path $probe 'Clock.cs'), 'namespace ProjectF.Simulation { public static class SimulationTickWorld { public const int DefaultSimulationTicksPerSecond = 60; public const float FixedSimulationDeltaSeconds = 1f / 60; } }')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Tests/ProgressChecks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
