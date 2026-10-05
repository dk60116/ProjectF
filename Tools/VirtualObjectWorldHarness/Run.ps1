$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $repo 'FactorioProject/Assets/Scripts'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-VehicleMovement-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$source = [IO.File]::ReadAllText((Join-Path $scripts 'Map/BlockStateStore.cs'))
function Member([string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1; $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}
$generated = "using System; using System.Collections.Generic; using UnityEngine; public partial class BlockStateStore {`n"
foreach ($signature in @(
    'public bool MoveLiveVehicle(',
    'private static Vector2Int GetNaturalInstallationStorageKey(',
    'private static bool ShouldUseOccupiedCoordinateStorageKey(',
    'private static Vector2Int FindPreferredOccupiedStorageCoordinate(',
    'private static Vector2Int ResolveInstallationStorageKey(',
    'private static Vector2Int CreateSyntheticInstallationStorageKey(',
    'private static void AssignInstallationStorageKey(',
    'private static bool InstallationStatesCanShareStorageKey(',
    'private static bool InstallationStatesRepresentSamePlacement(',
    'private static void RegisterInstallationPlacementKey(',
    'private static void UnregisterInstallationPlacementKey(',
    'private void AdjustSavedInstallationCount(',
    'private static void AdjustItemCount(',
    'private void RegisterSavedCoordinateMappings(',
    'private void UnregisterSavedCoordinateMappings(',
    'private void RegisterLiveCoordinateMappings(',
    'private void UnregisterLiveCoordinateMappings(',
    'private void RegisterSavedCoordinateMapping(',
    'private static void RegisterSavedCoordinateStorageKey(',
    'private static void UnregisterSavedCoordinateMapping(',
    'private static void UnregisterSavedCoordinateStorageKey(',
    'private bool ShouldReplaceSavedCoordinateMapping(',
    'private bool ShouldReplaceLiveCoordinateMapping(',
    'private static bool ShouldReplaceCoordinateMapping('
)) { $generated += (Member $signature) + "`n" }
$generated += '}'
[IO.File]::WriteAllText((Join-Path $probe 'Extracted.cs'), $generated)
$files = @(
    (Join-Path $scripts 'MapObjects/MapObjectHandle.cs'),
    (Join-Path $scripts 'Map/VirtualObjectWorld.cs'),
    (Join-Path $PSScriptRoot 'Stubs.cs'),
    (Join-Path $PSScriptRoot 'Program.cs'),
    (Join-Path $PSScriptRoot 'MovementChecks.cs'),
    (Join-Path $probe 'Extracted.cs')
)
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings><DefineConstants>VEHICLE_MOVEMENT_PROBE</DefineConstants></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
