$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

function Read-Member([string]$path, [string]$signature) {
    $text = [IO.File]::ReadAllText((Join-Path $repo $path))
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $text.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $text.Length) {
        if ($text[$end] -eq '{') { $depth++ }
        elseif ($text[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $text.Substring($start, $end - $start)
}

$conveyors = 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs'
$blockJobs = 'FactorioProject/Assets/Scripts/Map/Block.ConveyorJobs.cs'
$source = "using System; using System.Collections.Generic; using UnityEngine;`npublic partial class TerrainGenerator {`n"
foreach ($signature in @(
    'internal void MarkBeltJobItemVisualDirty(',
    'private void SetDynamicConveyorItemVisualBlockTracked(BlockHandle handle',
    'private void CacheConveyorBlockItemCount(BlockHandle handle',
    'private void RemoveCachedConveyorBlockItemCount(BlockHandle handle')) {
    $source += (Read-Member $conveyors $signature) + "`n"
}
$source += "}`npublic partial class Block {`n"
$source += (Read-Member $blockJobs 'internal void CaptureBeltJobItemVisualState(') + "`n}`n"

$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BeltPublish-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
