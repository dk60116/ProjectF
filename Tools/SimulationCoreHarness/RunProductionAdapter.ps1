$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$modulePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'
$source = [IO.File]::ReadAllText($modulePath)
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
$generated = "using System; using ProjectF.Simulation;`npublic partial class CraftAdapterProbe {`n"
$start = $source.IndexOf('private ProjectF.Simulation.ProductionProcess production', [StringComparison]::Ordinal)
$end = $source.IndexOf('private TerrainGenerator cachedTerrain', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Missing production state/forwarding properties' }
$generated += $source.Substring($start, $end - $start)
foreach ($signature in @('private void UpdateActiveCraft(', 'protected void BeginActiveCraft(',
    'protected void ClearActiveCraft()', 'protected virtual bool TryCompleteActiveCraft()')) {
    $generated += (Read-Member $signature) + "`n"
}
$generated += "}`n"
$tempDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ProductionAdapter-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDirectory | Out-Null
[IO.File]::WriteAllText((Join-Path $tempDirectory 'Extracted.cs'), $generated)
Copy-Item -Path (Join-Path $repo 'FactorioProject/Assets/Scripts/Simulation/Core/*.cs') -Destination $tempDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ProductionAdapterChecks.cs') -Destination $tempDirectory
[IO.File]::WriteAllText((Join-Path $tempDirectory 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $tempDirectory 'Probe.csproj')
exit $LASTEXITCODE
