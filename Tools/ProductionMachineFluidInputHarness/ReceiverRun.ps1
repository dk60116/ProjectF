$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/ProductionMachine.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$startSignature = 'protected override void AppendDedicatedFluidStorageRuntimeCoordinates('
$endSignature = 'private int ResolveMaximumProductionIngredientTypes()'
$start = $source.IndexOf($startSignature, [StringComparison]::Ordinal)
$end = $source.IndexOf($endSignature, $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -lt 0) { throw 'Missing ProductionMachine fluid receiver methods' }

$generated = "using System; using System.Collections.Generic; using UnityEngine;`n"
$generated += "public partial class ProductionMachine : InputOutputModule {`n"
$generated += $source.Substring($start, $end - $start) + "`n}"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ProductionFluidReceiver-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ReceiverChecks.cs') -Destination $probeDir
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
