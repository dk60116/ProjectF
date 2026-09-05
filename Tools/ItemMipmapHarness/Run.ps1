$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ItemMipmap-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$files = @((Join-Path $PSScriptRoot 'Checks.cs'), (Join-Path $repo 'FactorioProject/Assets/Editor/ItemTextureMipmapUtility.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE

