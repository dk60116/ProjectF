param([string]$SourcePath, [switch]$CaptureBaseline)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$SourcePath) { $SourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Manager/MapObjectTickManager.cs' }
$source = [IO.File]::ReadAllText($SourcePath)
$start = $source.IndexOf('public readonly struct MapObjectRuntimeCounter', [StringComparison]::Ordinal)
if ($start -lt 0) { throw 'Profiler declarations not found.' }
$source = 'using System; using System.Collections.Generic; using System.Globalization; using System.Text; using UnityEngine;' + "`n" + $source.Substring($start)
# Deterministic clock replaces only diagnostic timing, not production aggregation.
$source = $source.Replace('Stopwatch.', 'ProfilerClock.')
$tempDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ProfilerHarness-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDirectory | Out-Null
[IO.File]::WriteAllText((Join-Path $tempDirectory 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $tempDirectory
[IO.File]::WriteAllText((Join-Path $tempDirectory 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
$arguments = @((Join-Path $PSScriptRoot 'ExpectedSnapshots.json'))
if ($CaptureBaseline) { $arguments += '--capture' }
dotnet run --configuration Release --project (Join-Path $tempDirectory 'Probe.csproj') -- @arguments
exit $LASTEXITCODE
