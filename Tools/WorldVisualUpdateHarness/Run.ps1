$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$utilityPoleSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/UtilityPole.cs'))
if ($utilityPoleSource -match '\bvoid\s+LateUpdate\s*\(') {
    throw 'FAIL utility poles still register one LateUpdate callback per instance'
}
if ($utilityPoleSource.IndexOf('internal static void FlushDeferredVisualRefreshes()', [StringComparison]::Ordinal) -lt 0) {
    throw 'FAIL centralized utility-pole visual flush entry point is missing'
}
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-VisualUpdates-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$files = @(
    (Join-Path $PSScriptRoot 'Checks.cs'),
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Rendering/InstallationVisualState.cs'),
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Rendering/WorldVisualUpdateManager.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value $project
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE

