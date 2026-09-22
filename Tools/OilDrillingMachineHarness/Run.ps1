$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$resourceDefinitionPath = Join-Path $repositoryRoot 'FactorioProject/Assets/Data/MapObject/Resource_Crude Oil.asset'
$resourcePrefabPath = Join-Path $repositoryRoot 'FactorioProject/Assets/MapObject/Ore/Crude Oil/Crude Oil.prefab'
$drillingSourcePath = Join-Path $repositoryRoot 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/OilDrillingMachine.cs'
$resourceDefinitionMetaPath = "$resourceDefinitionPath.meta"

$resourceDefinition = [IO.File]::ReadAllText($resourceDefinitionPath)
$resourcePrefab = [IO.File]::ReadAllText($resourcePrefabPath)
$drillingSource = [IO.File]::ReadAllText($drillingSourcePath)
$resourceDefinitionMeta = [IO.File]::ReadAllText($resourceDefinitionMetaPath)
$definitionGuidMatch = [regex]::Match($resourceDefinitionMeta, '(?m)^guid: ([0-9a-f]{32})$')
if (-not $definitionGuidMatch.Success) { throw 'Crude Oil resource definition GUID is missing' }
$definitionGuid = $definitionGuidMatch.Groups[1].Value

if ($resourceDefinition -notmatch '(?m)^  placementCategory: 1$') {
    throw 'Crude Oil must use ResourceDefinition.PlacementCategory.Oil'
}

if (-not $resourcePrefab.Contains("definition: {fileID: 11400000, guid: $definitionGuid, type: 2}")) {
    throw 'Crude Oil prefab does not reference the validated resource definition'
}

$missingDepositStatus = $drillingSource.IndexOf('return "No oil deposit";', [StringComparison]::Ordinal)
$depletedStatus = $drillingSource.IndexOf('return "Oil depleted";', [StringComparison]::Ordinal)
if ($missingDepositStatus -lt 0 -or $depletedStatus -lt 0) {
    throw 'Oil drilling status must distinguish a missing deposit from depletion'
}

$pressureStart = $drillingSource.IndexOf(
    'public override float GetObjectInfoFluidPressureLitersPerSecond(',
    [StringComparison]::Ordinal)
$pressureEnd = $drillingSource.IndexOf(
    'public bool TryGetObjectInfoResourceReserves(',
    $pressureStart,
    [StringComparison]::Ordinal)
if ($pressureStart -lt 0 -or $pressureEnd -le $pressureStart) {
    throw 'Oil drilling pressure method is missing'
}

$pressureMethod = $drillingSource.Substring($pressureStart, $pressureEnd - $pressureStart)
if (-not $pressureMethod.Contains('&& isExtracting')) {
    throw 'Idle oil drilling machine must report zero pipe pressure'
}

if (-not $pressureMethod.Contains('litersPerSecond * OperationalAnimationSpeedRatio')) {
    throw 'Oil drilling pressure must follow the actual energy supply ratio'
}

if (-not $drillingSource.Contains('return resource.CanHarvest && HasOilOutputSpace(resource);')) {
    throw 'Oil drilling must clear its working pressure after depletion or output blockage'
}

$fixturePath = Join-Path $PSScriptRoot 'PressureChecks.cs'
$fixture = [IO.File]::ReadAllText($fixturePath).Replace(
    '    // PRODUCTION_PRESSURE',
    ($pressureMethod.TrimEnd() -replace '(?m)^', '    '))
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) (
    'ProjectF-OilDrillingPressure-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
try {
    Set-Content -LiteralPath (Join-Path $probeDirectory 'Program.cs') -Value $fixture
    Set-Content -LiteralPath (Join-Path $probeDirectory 'Probe.csproj') -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>
'@
    dotnet run --configuration Release --project (Join-Path $probeDirectory 'Probe.csproj')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Remove-Item -LiteralPath $probeDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Oil drilling machine checks passed: 7 structural, 5 behavioral'
