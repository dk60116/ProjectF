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
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    return $source.Substring($start, $end - $start)
}

$resourceFile = 'FactorioProject/Assets/Scripts/Object/MapObj/Resource.cs'
$terrainFile = 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.ChunkPersistence.cs'
$generated = "using UnityEngine; public partial class Resource {`n"
$generated += Read-Member $resourceFile 'private int ConsumeGaugeDotsInternal('
$generated += Read-Member $resourceFile 'private void PersistDepletedResourceState()'
$generated += "}`npublic partial class TerrainGenerator {`n"
$generated += Read-Member $terrainFile 'public void SaveRuntimeResourceState('
$generated += "}`n"

$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ResourceDepletion-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
