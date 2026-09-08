$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Read-Member([string]$file, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $file))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$definition = 'FactorioProject/Assets/Scripts/Object/ResourceDefinition.cs'
$itemDefinition = 'FactorioProject/Assets/Scripts/Object/ItemDefinition.cs'
$paper = 'FactorioProject/Assets/Scripts/HUD/Map/MapPaper.cs'
$generated = "using System; using UnityEngine;`n"
$generated += Read-Member $definition 'public enum MapMarkerSize'
$generated += "`npublic partial class ResourceDefinition {`n"
$generated += Read-Member $definition 'public enum MapColorMode'
$generated += Read-Member $definition 'public bool TryGetMapColor32('
$generated += "}`npublic partial class ItemDefinition {`n"
$generated += Read-Member $itemDefinition 'public bool TryGetMapColor32('
$generated += "}`npublic partial class MapPaper {`n"
$generated += Read-Member $paper 'private void RefreshMapMarkers('
$generated += Read-Member $paper 'private void StampSmallResourceMarkers('
$generated += Read-Member $paper 'private bool HasAdjacentSmallResource('
$generated += Read-Member $paper 'private void StampMapMarker('
$generated += Read-Member $paper 'private static int GetScaledMarkerDimension('
$generated += Read-Member $paper 'private static int GetCenteredMarkerMinimum('
$generated += Read-Member $paper 'private static int DistanceOutsideRange('
$generated += "}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ResourceMap-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.MapResources.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
