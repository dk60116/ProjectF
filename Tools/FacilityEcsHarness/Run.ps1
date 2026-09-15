$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-FacilityEcs-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$files = @(
    (Join-Path $PSScriptRoot 'Program.cs'),
    (Join-Path $PSScriptRoot 'HostStubs.cs'),
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Simulation/Core/SimulationTickContracts.cs'),
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Simulation/Core/FacilityFlowBatch.cs'),
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/FacilitySimulationWorld.cs'),
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/FacilityRuntimeWakeRegistry.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value $project
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
