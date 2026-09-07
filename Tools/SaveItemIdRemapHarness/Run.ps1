param([switch]$Baseline)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = 'FactorioProject/Assets/Scripts/Manager/SaveGameItemIdRemapper.cs'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-SaveItemIdRemap-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$source = if ($Baseline) { (git -C $repo show "HEAD:$sourcePath") -join "`n" } else { [IO.File]::ReadAllText((Join-Path $repo $sourcePath)) }
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
