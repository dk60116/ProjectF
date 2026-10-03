param([switch]$BeforeFix)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Read-Member([string]$file, [string]$signature) {
    $source = if ($BeforeFix) { (git -C $repo show "HEAD:$file") -join "`n" }
        else { [IO.File]::ReadAllText((Join-Path $repo $file)) }
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}
$generated = "using System.Collections.Generic; using UnityEngine;`npublic partial class Block {`n"
$blockFile = 'FactorioProject/Assets/Scripts/Map/Block.cs'
foreach ($signature in @('public bool TryGetRuntimePipeRecord(', 'public bool TryGetRuntimeConveyorRecord(',
    'public bool TryAddFloorObject(', 'private bool TryGetAvailableFloorStack(',
    'public bool SupportsFloorObjectDrops', 'public bool CanAddFloorObjects(int count, int itemId,',
    'private int GetAvailableFloorCapacity(int itemId,', 'private bool BlocksFloorObjectStacking(')) {
    $generated += (Read-Member $blockFile $signature) + "`n"
}
$generated += "}`npublic partial class TerrainGenerator {`n"
foreach ($signature in @('private Block FindPreferredDropBlock(', 'private Block FindNearestDropBlock(',
    'private bool IsValidDropBlock(')) {
    $generated += (Read-Member 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.ChunkPersistence.cs' $signature) + "`n"
}
$generated += '}'
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-FloorDrop-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
$checks = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="' + $checks + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
