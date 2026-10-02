$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $repo 'FactorioProject/Assets/Scripts'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BenchmarkItems-' + [guid]::NewGuid().ToString('N'))
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
$runtime = [IO.File]::ReadAllText((Join-Path $scripts 'Diagnostics/BenchmarkRuntime.cs'))
$start = $runtime.IndexOf('public static bool IsPortableItem(')
$end = $runtime.IndexOf(';', $start) + 1
$methods = $runtime.Substring($start, $end - $start) + (Get-Block $runtime 'public static List<int> CollectPortableItemIds(')
[IO.File]::WriteAllText((Join-Path $probe 'Runtime.cs'), 'using System.Collections.Generic; namespace ProjectF.Benchmark { public static class BenchmarkRuntime {' + $methods + '} }')
$receiver = [IO.File]::ReadAllText((Join-Path $scripts 'Manager/RuntimeItemGiveReceiver.Benchmark.cs'))
$fill = Get-Block $receiver 'if (command.Action == BenchmarkAction.Fill || command.Action == BenchmarkAction.FillRandom)'
[IO.File]::WriteAllText((Join-Path $probe 'Fill.cs'), 'using System; using System.Collections; using System.Collections.Generic; using ProjectF.Benchmark; public class FillProbe { public long benchmarkDone, benchmarkTotal; public IEnumerator Run(TerrainGenerator terrain, BenchmarkCommand command, ItemDefinition item, List<int> randomItems) {' + $fill + '} }')
$form = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Program.cs'))
$methods = ''
foreach ($signature in @('private async Task ConnectAsync()', 'private async Task LoadCatalogAsync()', 'private async Task PollAsync()',
    'private void RefreshPickers()', 'private static bool Matches(', 'private static void FillPicker(', 'private void UpdateEnabled()', 'private void ApplyStatus(',
    'private static string Token(', 'private static long Integer(',
    'private static Dictionary<string, string> Tokens(')) {
    if ($signature -in @('private static bool Matches(', 'private static string Token(', 'private static long Integer(')) {
        $start = $form.IndexOf($signature); $end = $form.IndexOf(';', $start) + 1
        $methods += $form.Substring($start, $end - $start)
    } else { $methods += Get-Block $form $signature }
}
$methods += Get-Block $form 'private sealed class Catalog {'
$methods += Get-Block $form 'private sealed class CatalogItem'
[IO.File]::WriteAllText((Join-Path $probe 'Catalog.cs'), 'using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Text; using System.Text.Json; using System.Threading.Tasks; public partial class CatalogProbe {' + $methods + '}')
$files = @((Join-Path $probe 'Runtime.cs'), (Join-Path $probe 'Fill.cs'), (Join-Path $probe 'Catalog.cs'),
    (Join-Path $PSScriptRoot 'Tests/ItemChecks.cs'), (Join-Path $scripts 'Diagnostics/BenchmarkLayout.cs'),
    (Join-Path $scripts 'Diagnostics/BenchmarkCommand.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
