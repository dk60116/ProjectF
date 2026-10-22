$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$vehicle = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle'
$source = [IO.File]::ReadAllText((Join-Path $vehicle 'Locomotive.cs'))
function Member([string]$text, [string]$signature) {
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $text.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $text.Length) {
        if ($text[$end] -eq '{') { $depth++ }
        if ($text[$end] -eq '}') { $depth-- }
        $end++
    }
    $text.Substring($start, $end - $start)
}
function Expression([string]$text, [string]$signature) {
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production expression: $signature" }
    $text.Substring($start, $text.IndexOf(';', $start) - $start + 1)
}
$production = "using System; using System.Collections.Generic; using UnityEngine; public partial class Locomotive : RailHandcar {`n"
foreach ($signature in @('public virtual bool UsesWaterSupply',
    'protected virtual ItemDefinition.EnergyType PropulsionEnergyType',
    'protected virtual bool NeedsConfiguredFuelUse',
    'protected virtual bool AcceptsPropulsionFuel', 'private System.Predicate<int> FuelItemFilter',
    'protected bool IsFuelDisplayDefinition')) {
    $production += (Expression $source $signature) + "`n"
}
foreach ($signature in @('private enum DriveMotionOutcome',
    'public virtual ItemDefinition FuelDisplayDefinition',
    'public float ObjectInfoBurnEnergyUseRatePerSecond',
    'private DriveMotionOutcome HandleResolvedDriveMotion(',
    'private bool RequiresPoweredBurnEnergy(', 'private bool RequiresWater(',
    'private bool TryEnsureWaterAvailable(', 'private bool TryEnsureBurnEnergyAvailable(',
    'private bool TryConsumeOneBurnEnergyItem(', 'private bool TryConsumeBurnEnergyFromRearFreightCar(',
    'private bool TryConsumeBurnEnergyFromMountedPlayer(', 'private bool TryConsumeBurnEnergyFromBag(',
    'private bool IsUsableBurnEnergyItem(', 'private bool TryResolveBurnEnergyAmount(',
    'private void SpendStoredBurnEnergyUnits(', 'private void SpendStoredWaterUnits(',
    'public void CaptureBurnEnergyState(', 'public void CaptureBurnEnergyStateUnits(',
    'public void ApplyBurnEnergyState(', 'public void ApplyBurnEnergyStateUnits(', 'public void ClearBurnEnergyState(')) {
    $production += (Member $source $signature) + "`n"
}
$diesel = [IO.File]::ReadAllText((Join-Path $vehicle 'DieselTrain.cs'))
$production += "} public partial class DieselTrain : Locomotive {`n"
foreach ($signature in @('public override bool UsesWaterSupply',
    'public override ItemDefinition FuelDisplayDefinition',
    'protected override ItemDefinition.EnergyType PropulsionEnergyType',
    'protected override bool NeedsConfiguredFuelUse',
    'protected override bool AcceptsPropulsionFuel')) {
    $production += (Expression $diesel $signature) + "`n"
}
$units = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Simulation/Core/SimulationTickContracts.cs'))
$production += "} public static class DeterministicSimulationUnits {`n"
$production += [regex]::Match($units, 'public const long UnitsPerWhole = [^;]+;').Value + "`n"
foreach ($signature in @('public static long FromFloat(', 'public static float ToFloat(')) {
    $production += (Member $units $signature) + "`n"
}
$freight = [IO.File]::ReadAllText((Join-Path $vehicle 'FreightCar.cs'))
$production += "} public partial class FreightCar {`n"
$production += (Expression $freight 'public bool IsFuelSupplyCar') + "`n"
foreach ($signature in @('private bool TryGetFuelSupplyLocomotive(', 'private void RefreshFuelRoleIcon()', 'private void RefreshFuelRoleIconPosition(')) {
    $production += (Member $freight $signature) + "`n"
}
$production += "}`n"
if (!(Member $freight 'private void LateUpdate()').Contains('RefreshFuelRoleIcon();')) {
    throw 'Fuel role icon is not refreshed after train connection changes.'
}
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-Locomotive-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $production)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Simulation/Core/VehicleMotionState.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$item = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Data/Items/Item_139_Diesel train.asset'))
$prefab = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/MapObject/Train/Diesel train/Diesel Train.prefab'))
foreach ($required in @('vehicleAccelerationPerSecond: 2', 'vehicleMaxSpeed: 6.2',
    'vehicleDecelerationPerSecond: 3.2', 'vehicleMass: 15',
    'dieselCan: {fileID: 11400000, guid: 7f2a2e4d57ee0fe4da72c59d6aba4f07, type: 2}',
    'exhaustPrefab: {fileID: 1850189683619851348, guid: c885ff713c6f94c46a3edf9e93f5f899, type: 3}')) {
    if (!$prefab.Contains($required)) { throw "Diesel prefab contract missing: $required" }
}
# ItemData owns the rate and interaction icons. Accept both current energy rows and legacy fields.
if ($item -notmatch '(?m)^\s+(?:-\s*)?(?:useEnergyType|energyType): 6\r?$') {
    throw 'Diesel propulsion energy requirement is missing'
}
foreach ($file in @('Map/BlockStateStore.cs', 'Map/TerrainGenerator.ChunkPersistence.cs',
    'Map/BlockStateStore.MapObjectItemClear.cs', 'HUD/PlayerHUD.cs', 'HUD/ItemSlot/TrainFilter.cs',
    'HUD/ObjectUI/ItemInfoDescription.cs', 'Object/MapObj/InstallationObject/InstallationPlacementController.cs')) {
    $text = [IO.File]::ReadAllText((Join-Path $repo ('FactorioProject/Assets/Scripts/' + $file)))
    if (!$text.Contains('Locomotive')) { throw "Missing common locomotive integration: $file" }
}
Write-Host 'PASS diesel prefab, configured fuel type and shared UI/persistence integration (editor-owned rate/icons preserved).'
