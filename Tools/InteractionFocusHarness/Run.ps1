$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Character/Player/PlayerController.cs'))
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
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}
$generated = "using System.Collections.Generic;`npublic partial class PlayerController {`n"
foreach ($signature in @(
    'private void RefreshInteractionFocus()',
    'private void KeepClosestInteractionFocusTarget(',
    'private void ResetInteractionButtonFocusTargets()',
    'private void CacheInteractionButtonFocusTargets(',
    'private void CacheInteractionButtonFocusTarget(Block block)',
    'private void CacheInteractionButtonFocusTarget(MapObject target,',
    'private float GetMapObjectFocusSelectionDistanceSqr(',
    'private static Bounds CreateMapObjectStatusFocusBounds(')) {
    $generated += (Read-Member $signature) + "`n"
}
$generated += "}`n"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-InteractionFocus-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
$checksPath = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0414;0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="' + $checksPath + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
