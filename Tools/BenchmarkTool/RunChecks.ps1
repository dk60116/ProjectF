$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $repo 'FactorioProject/Assets/Scripts'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BenchmarkChecks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$files = @(
    (Join-Path $PSScriptRoot 'Tests/Checks.cs'),
    (Join-Path $scripts 'Diagnostics/BenchmarkLayout.cs'),
    (Join-Path $scripts 'Diagnostics/BenchmarkCommand.cs'),
    (Join-Path $scripts 'Diagnostics/BenchmarkWorkProgress.cs'),
    (Join-Path $scripts 'Object/MapObj/InstallationObject/InputOutputModule.Benchmark.cs'),
    (Join-Path $scripts 'Object/MapObj/InstallationObject/LoggingMachine.Benchmark.cs'),
    (Join-Path $scripts 'Simulation/Core/ProductionProcess.cs'),
    (Join-Path $scripts 'Simulation/Core/SimulationTickContracts.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
