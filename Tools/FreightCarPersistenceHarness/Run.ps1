$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-FreightCarPersistence-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null

$sourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/FreightCar.cs'
$source = [IO.File]::ReadAllText($sourcePath)
function Read-Member([string]$signature) {
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

if (!$source.Contains('IPersistentInstallationItemCollectionStorage')) {
    throw 'FreightCar is not connected to installation item persistence.'
}

$generated = @'
using System;
using System.Collections.Generic;
using UnityEngine;
public partial class FreightCar : IPersistentInstallationItemCollectionStorage {
'@
foreach ($signature in @(
    'public void CapturePersistentStoredItemIds(',
    'public void ApplyPersistentStoredItemIds(',
    'private static void AppendPersistentItemIds(',
    'private bool TryAddRestoredItem('
)) {
    $generated += (Read-Member $signature) + "`n"
}
$generated += "}`n"
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText(
    (Join-Path $probe 'Probe.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
