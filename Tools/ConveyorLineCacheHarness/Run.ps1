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
$store = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/BlockDataStore.cs'))
$lookup = 'public bool TryGetValue(BlockHandle handle, out Block block)'
$start = $store.IndexOf($lookup, [StringComparison]::Ordinal)
if ($start -lt 0) { throw 'Missing uncached handle resolver.' }
$body = $store.IndexOf('{', $start) + 1
# Instrument only the temporary harness copy; runtime code has no counter.
$store = $store.Insert($body, "`n        HarnessMetrics.HandleLookups++;`n")
$runtime = 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs'
$fields = 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.cs'
$source = "using System; using System.Collections.Generic; using UnityEngine; internal partial class CurrentResolver {`n"
foreach ($signature in @('private bool TryResolveLoadedRuntimeBlock(', 'private bool TryResolveConveyorLineBlock(')) {
    $source += (Member $runtime $signature) + "`n"
}
$source += ([regex]::Replace((Member $fields 'private sealed class ConveyorLine'), '^private ', 'internal ')) + "`n}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-LineCache-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'BlockDataStore.cs'), $store)
[IO.File]::WriteAllText((Join-Path $probe 'ProductionResolvers.cs'), $source)
foreach ($file in @('Checks.cs', 'LegacyResolver.cs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $probe }
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
