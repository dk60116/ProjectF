$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $repo 'FactorioProject/Assets/Scripts'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BenchmarkPreparation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
function Get-Block([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing source: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced source: $signature" }
    return $source.Substring($start, $end - $start)
}
$surface = [IO.File]::ReadAllText((Join-Path $scripts 'Map/TerrainGenerator.Surface.cs'))
$methods = Get-Block $surface 'private ChunkSurfaceBuildData BuildBenchmarkChunkSurface('
[IO.File]::WriteAllText((Join-Path $probe 'Surface.cs'), 'public partial class SurfaceProbe {' + $methods + '}')
$renderer = [IO.File]::ReadAllText((Join-Path $scripts 'MapObjects/StaticMapObjectBatchRenderer.cs'))
$methods = (Get-Block $renderer 'internal IEnumerator PrepareBenchmarkPresentation(') + (Get-Block $renderer 'private IEnumerator SynchronizeHostsCore(')
# A deterministic clock replaces the timing boundary, not the renderer algorithm.
$methods = $methods.Replace('System.Diagnostics.Stopwatch', 'ProbeClock')
[IO.File]::WriteAllText((Join-Path $probe 'Renderer.cs'), 'using System.Collections; using ProjectF.Benchmark; public partial class RendererProbe {' + $methods + '}')
$pipe = [IO.File]::ReadAllText((Join-Path $scripts 'Map/PipeWorld.cs'))
$methods = (Get-Block $pipe 'internal IEnumerator PrepareBenchmarkPresentation(') + (Get-Block $pipe 'private IEnumerator RebuildBodyBatchesCore(')
$methods = $methods.Replace('System.Diagnostics.Stopwatch', 'ProbeClock')
[IO.File]::WriteAllText((Join-Path $probe 'Pipe.cs'), 'using System.Collections; public partial class PipeProbe {' + $methods + '}')
$files = @((Join-Path $probe 'Surface.cs'), (Join-Path $probe 'Renderer.cs'), (Join-Path $probe 'Pipe.cs'),
    (Join-Path $PSScriptRoot 'Tests/PreparationChecks.cs'), (Join-Path $scripts 'Diagnostics/BenchmarkLayout.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
