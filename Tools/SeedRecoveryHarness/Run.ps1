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
    $source.Substring($start, $end - $start)
}
$base = 'FactorioProject/Assets/Scripts/Object/MapObj/'
$generated = "using System.Collections.Generic; using UnityEngine;`npublic partial class Resource {`n"
$generated += (Read-Member ($base + 'Resource.cs') 'protected int RollNextConfiguredHarvestDropCount(') + "`n}`nnamespace ProjectF.MapObjects { public partial class Tree {`n"
$generated += (Read-Member ($base + 'Tree.cs') 'public void CollectMachineSeedDrops(') + "`n}}`npublic partial class SeedPlanter {`n"
$generated += (Read-Member ($base + 'InstallationObject/SeedPlanter.cs') 'internal int ReceiveHarvestedSeeds(') + "`n}`npublic partial class InputOutputModule {`n"
$generated += (Read-Member ($base + 'InstallationObject/InputOutputModule.cs') 'protected bool TryRestoreRuntimeInputAreaCenterObject(') + "`n}`npublic partial class LoggingMachine {`n"
$generated += (Read-Member ($base + 'InstallationObject/LoggingMachine.cs') 'private void CompleteTreeHarvest(') + "`n"
$generated += (Read-Member ($base + 'InstallationObject/LoggingMachine.cs') 'private void RecoverHarvestedSeeds(') + "`n}`npublic partial class Block {`n"
$generated += (Read-Member 'FactorioProject/Assets/Scripts/Map/Block.cs' 'public bool CanAddFloorObjects(int count, int itemId,') + "`n"
$generated += (Read-Member 'FactorioProject/Assets/Scripts/Map/Block.cs' 'private int GetAvailableFloorCapacity(int itemId,') + "`n"
$generated += (Read-Member 'FactorioProject/Assets/Scripts/Map/Block.cs' 'private bool BlocksFloorObjectStacking(') + "`n}`npublic partial class TerrainGenerator {`n"
$generated += (Read-Member 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Farming.cs' 'public bool CanPlantSeed(') + "`n}`npublic partial class InstallationPlacementController {`n"
$generated += (Read-Member ($base + 'InstallationObject/InstallationPlacementController.cs') 'private void ConfigureInstalledInputOutputOutputAreas(') + "`n"
$generated += (Read-Member ($base + 'InstallationObject/InstallationPlacementController.cs') 'private static bool ShouldInputOutputAreasBlockInstallationPlacement(') + "`n}`npublic partial class BlockStateStore {`n"
$generated += (Read-Member 'FactorioProject/Assets/Scripts/Map/BlockStateStore.PlantBackground.cs' 'public bool IsSavedCoordinateEmptyGround(') + "`n}"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-SeedRecovery-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
$checksPath = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0414;0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="' + $checksPath + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
