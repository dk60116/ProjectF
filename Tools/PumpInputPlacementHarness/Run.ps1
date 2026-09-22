$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$base = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj'
function Read-Member([string]$file, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $base $file))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced member: $signature" }
    $source.Substring($start, $end - $start)
}
$generated = "using System; using System.Collections.Generic; using UnityEngine;`npublic partial class Pump {`n"
foreach ($signature in @(
    'public bool TryGetRuntimePipePass(',
    'internal Vector2Int ResolveRuntimeFluidDeliveryCoordinate(',
    'internal bool TryGetRuntimeFluidEndpoints(',
    'internal bool AllowsRuntimeFluidTraversal(',
    'internal bool TryGetPipePassAt(',
    'internal bool TryGetBodyPipePassEndpointAt(',
    'public bool TryGetPipePassExternalDirection(',
    'internal bool TryGetInterlockedEndpointAt('
)) { $generated += (Read-Member 'Pump.cs' $signature) + "`n" }
$generated += "} public partial class InputOutputModule {`n"
$generated += Read-Member 'InstallationObject/InputOutputModule.cs' 'internal static bool HasRuntimeFluidInputFacingAt('
$generated += "} public partial class InstallationPlacementController {`n"
foreach ($signature in @(
    'private bool TryResolveSimpleRectGridInstallPreviewTargetFast(',
    'private static bool ShouldEvaluateSimpleRectGridTargetPlacement(',
    'private bool PumpBodyOverlapsInputArea('
)) { $generated += (Read-Member 'InstallationObject/InstallationPlacementController.cs' $signature) + "`n" }
$generated += "}`n"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PumpInputPlacement-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
[IO.File]::WriteAllText((Join-Path $probeDir 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probeDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Fixture.cs') -Destination $probeDir
[IO.File]::WriteAllText((Join-Path $probeDir 'Probe.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
