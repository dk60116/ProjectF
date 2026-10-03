param([string]$Source, [switch]$Record)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$Source) { $Source = Join-Path $repo 'FactorioProject/Assets/Scripts/Map/UtilityPoleRuntime.cs' }
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PoleProbe-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
python (Join-Path $PSScriptRoot 'Generate.py') $Source $probe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0169</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
$fixtures = Join-Path $PSScriptRoot 'WireFixtures.txt'
if ($Record) { dotnet run -c Release --project (Join-Path $probe 'Probe.csproj') -- record $fixtures }
else { dotnet run -c Release --project (Join-Path $probe 'Probe.csproj') -- $fixtures }
exit $LASTEXITCODE
