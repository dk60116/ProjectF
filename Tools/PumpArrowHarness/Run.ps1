$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/Pump.cs'))
function Read-Member([string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$fieldsStart = $source.IndexOf('[SerializeField, FormerlySerializedAs(')
$fieldsEnd = $source.IndexOf('public float PressureLitersPerSecond')
$generated = "using UnityEngine; using UnityEngine.Serialization; public partial class Pump {`n"
$generated += $source.Substring($fieldsStart, $fieldsEnd - $fieldsStart)
$generated += [regex]::Match($source, 'protected override bool RequiresManagedVisualUpdate\s*=>[^;]+;').Value
foreach ($member in @('protected override void OnEnable()',
    'protected override void OnManagedVisualsResumed()',
    'protected override void OnPlacementRuntimeChanged()',
    'protected override void TickManagedVisuals(')) {
    $generated += "`n" + (Read-Member $member)
}
$generated += "`n}"
$prefab = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/MapObject/Fluid/Pump/Pump.prefab'))
if ($prefab -notmatch 'pumpArrow: \{fileID: 4578265203408366393\}' -or $prefab -match '\bpipeArrow:') {
    throw 'Pump prefab arrow binding is stale'
}
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PumpArrow-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
