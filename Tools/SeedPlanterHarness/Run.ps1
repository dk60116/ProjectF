$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Manager/MapObjectTickManager.cs'))
$start = $source.IndexOf('public static class DeterministicSimulationUnits', [StringComparison]::Ordinal)
if ($start -lt 0) { throw 'Missing production deterministic units class.' }
$end = $source.IndexOf('{', $start) + 1
$depth = 1
while ($depth -gt 0 -and $end -lt $source.Length) {
    if ($source[$end] -eq '{') { $depth++ }
    if ($source[$end] -eq '}') { $depth-- }
    $end++
}
if ($depth -ne 0) { throw 'Unbalanced production class.' }
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-SeedPlanter-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Units.cs') -Value ("using System;`n" + $source.Substring($start, $end - $start))
$includes = @(
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/SeedPlanter.cs'),
    (Join-Path $PSScriptRoot 'Stubs.cs'),
    (Join-Path $PSScriptRoot 'Checks.cs')
) | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + ($includes -join '') + '</ItemGroup></Project>'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value $project
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
