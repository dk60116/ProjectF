$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/HUD/ItemSlot/BagSlot.cs'))
$pickupGateSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/DroppedItemPickupGate.cs'))
$outlineSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Rendering/AnimalScreenSpaceOutlineRendererFeature.cs'))
function Read-Member([string]$signature, [string]$memberSource = $source) {
    $start = $memberSource.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $memberSource.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($memberSource[$end] -eq '{') { $depth++ }
        if ($memberSource[$end] -eq '}') { $depth-- }
        $end++
    }
    $memberSource.Substring($start, $end - $start)
}
$generated = "using UnityEngine; public partial class BagSlot {`n"
foreach ($signature in @(
    'private enum PickupSource', 'private struct PickupCandidate',
    'private bool TryResolvePickupCandidate(', 'private void ConsiderGroundPickupCandidates(',
    'private void ConsiderConveyorPickupCandidate(',
    'private void ConsiderBoxPickupCandidate(', 'private void ConsiderPickupCandidate(',
    'private static void RefreshAutomaticPickupPreviewFrame(',
    'private static bool HasHoveredPickupSlot(',
    'private static bool TryResolveAutomaticPickupPreviewSource(',
    'private bool CanResolveAutomaticPickupPreviewSource(',
    'private bool TryResolveAutomaticPickupPreviewItem(', 'private bool TryResolvePickupPreviewItem(',
    'private bool TryHandlePickupClick('
)) { $generated += (Read-Member $signature) + "`n" }
$generated += '}'
$generated += "`n" + ($pickupGateSource -replace '^using UnityEngine;\s*', '')
$generated += "`n" + (Read-Member 'public static class AnimalScreenSpaceOutline' $outlineSource)
$temp = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ItemPickup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
Set-Content -LiteralPath (Join-Path $temp 'Production.cs') -Value $generated
$checks = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
Set-Content -LiteralPath (Join-Path $temp 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="' + $checks + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $temp 'Probe.csproj')
exit $LASTEXITCODE
