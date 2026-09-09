$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-AreaMarker-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/AreaMarker.cs'))
$boundary = $source.IndexOf('internal sealed class InstallationPlacementAreaRegistry', [StringComparison]::Ordinal)
if ($boundary -lt 0) { throw 'Area registry boundary missing' }
[IO.File]::WriteAllText((Join-Path $probe 'AreaMarker.cs'), $source.Substring(0, $boundary))
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/AreaMarkerRenderer.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityStubs.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
