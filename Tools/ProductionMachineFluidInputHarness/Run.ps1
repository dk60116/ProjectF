$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$start = $source.IndexOf('    private Pipe.FluidNetworkSearchContext runtimeFluidInputPressureContext;', [StringComparison]::Ordinal)
$end = $source.IndexOf('    internal virtual bool TryGetRuntimePassiveFluidPass(', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -lt 0) { throw 'Missing fluid-input network query' }
$generated = "using UnityEngine; using System.Collections.Generic; public partial class InputOutputModule {`n"
$generated += $source.Substring($start, $end - $start) + "`n}"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ProductionFluidInput-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probeDir
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'ReceiverRun.ps1')
exit $LASTEXITCODE
