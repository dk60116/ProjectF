$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/HUD/ItemSlot/BagSlot.cs'))
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
$generated = "using UnityEngine; public partial class BagSlot {`n"
foreach ($signature in @(
    'private enum PickupSource', 'private struct PickupCandidate',
    'private bool TryResolvePickupCandidate(', 'private void ConsiderConveyorPickupCandidate(',
    'private void ConsiderBoxPickupCandidate(', 'private void ConsiderPickupCandidate(',
    'private bool TryResolveAutomaticPickupPreviewItem(', 'private bool TryResolvePickupPreviewItem(',
    'private bool TryHandlePickupClick('
)) { $generated += (Read-Member $signature) + "`n" }
$generated += '}'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ItemPickup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
Set-Content -LiteralPath (Join-Path $temp 'Production.cs') -Value $generated
$checks = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
Set-Content -LiteralPath (Join-Path $temp 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="' + $checks + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $temp 'Probe.csproj')
exit $LASTEXITCODE
