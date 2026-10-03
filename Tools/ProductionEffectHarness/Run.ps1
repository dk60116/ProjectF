$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ProductionEffect-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$files = @((Join-Path $PSScriptRoot 'Checks.cs'), (Join-Path $PSScriptRoot 'UnityBoundaries.cs'),
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Rendering/ProductionEffectTemplate.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj') -- $repo
exit $LASTEXITCODE
