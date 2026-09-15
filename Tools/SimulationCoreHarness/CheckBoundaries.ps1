$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $repo 'FactorioProject/Assets/Scripts'
foreach ($sourceFile in Get-ChildItem -LiteralPath (Join-Path $scripts 'Simulation/Core') -Filter '*.cs') {
    $sourceText = [IO.File]::ReadAllText($sourceFile.FullName)
    if ($sourceText -cmatch '\b(UnityEngine|GameObject|MonoBehaviour|Component|Transform|Time)\b') {
        throw "Engine dependency in core: $($sourceFile.Name)"
    }
}
foreach ($worldName in @('ConveyorWorld', 'PipeWorld', 'RobotArmWorld', 'AnimalAIWorld')) {
    $relativePath = if ($worldName -eq 'AnimalAIWorld') { "Object/Animal/$worldName.cs" } else { "Map/$worldName.cs" }
    $runtimeText = [IO.File]::ReadAllText((Join-Path $scripts $relativePath))
    $viewText = [IO.File]::ReadAllText((Join-Path $scripts "Simulation/Presentation/${worldName}View.cs"))
    if ($runtimeText -notmatch "class $worldName\s*:\s*IDisposable" -or $runtimeText -match 'private void On(Enable|Disable|Destroy)\(') {
        throw "Scene-owned lifetime remains in $worldName"
    }
    if ($viewText -match '\b(RegisterUpdateTick|UnregisterUpdateTick|ClearRecords|Remove)\s*\(') {
        # Collider dictionary removal is presentation-only, not entity removal.
        $withoutColliderRemoval = $viewText -replace '(colliders|colliderOwners)\.Remove\(', 'ReleaseCollider('
        if ($withoutColliderRemoval -match '\b(RegisterUpdateTick|UnregisterUpdateTick|ClearRecords|Remove)\s*\(') {
            throw "View mutates simulation lifetime: $worldName"
        }
    }
    Write-Output "PASS $worldName runtime lifetime is separate from its view (source contract)"
}
$animalWorldText = [IO.File]::ReadAllText((Join-Path $scripts 'Object/Animal/AnimalAIWorld.cs'))
$animalWorldRetainsOwner =
    ($animalWorldText -notmatch 'static AnimalAIWorld Ensure\(\)') -or
    ($animalWorldText -match 'EnsureFor\(GameObject') -or
    ($animalWorldText -match 'GameObject owner') -or
    ($animalWorldText -notmatch 'AttachView\(Transform parent\)')
if ($animalWorldRetainsOwner) {
    throw 'AnimalAIWorld creation still requires a presentation GameObject owner'
}
Write-Output 'PASS AnimalAIWorld creation is separate from presentation attachment (source contract)'
$virtualWorldText = [IO.File]::ReadAllText((Join-Path $scripts 'Map/VirtualObjectWorld.cs'))
$virtualWorldOwnsSceneLifetime =
    ($virtualWorldText -notmatch 'class VirtualObjectWorld\s*:\s*IDisposable') -or
    ($virtualWorldText -match 'class VirtualObjectWorld\s*:\s*MonoBehaviour') -or
    ($virtualWorldText -match 'new GameObject\(') -or
    ($virtualWorldText -match 'AddComponent<VirtualObjectWorld>') -or
    ($virtualWorldText -match '\b(InstallationObject|ResourceInstance)\b')
if ($virtualWorldOwnsSceneLifetime) {
    throw 'VirtualObjectWorld still owns a scene GameObject lifetime'
}
$staticRendererText = [IO.File]::ReadAllText((Join-Path $scripts 'MapObjects/StaticMapObjectBatchRenderer.cs'))
$typeHostText = [IO.File]::ReadAllText((Join-Path $scripts 'MapObjects/StaticMapObjectTypeHost.cs'))
$dataOnlyPresentationNeedsSource =
    ($staticRendererText -notmatch 'CopyRecords\(dataOnlyInstallations, true\)') -or
    ($staticRendererText -notmatch 'SynchronizeRecord\(record\)') -or
    ($typeHostText -notmatch 'public bool SynchronizeRecord\(VirtualObjectRecord record\)')
if ($dataOnlyPresentationNeedsSource) {
    throw 'Data-only installation presentation still requires a source InstallationObject'
}
Write-Output 'PASS VirtualObjectWorld lifetime and data-only presentation are separate (source contract)'
$blockText = [IO.File]::ReadAllText((Join-Path $scripts 'Map/Block.cs'))
$blockStoreText = [IO.File]::ReadAllText((Join-Path $scripts 'Map/BlockDataStore.cs'))
$terrainText = [IO.File]::ReadAllText((Join-Path $scripts 'Map/TerrainGenerator.cs'))
$blockTemplateText = [IO.File]::ReadAllText((Join-Path $scripts 'Map/BlockTemplate.cs'))
$blockTemplateMeta = [IO.File]::ReadAllText((Join-Path $scripts 'Map/BlockTemplate.cs.meta'))
$blockSourceMeta = [IO.File]::ReadAllText((Join-Path $scripts 'Map/Block.cs.meta'))
$blockPrefabText = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Prefab/Enviroment/Block/Block.prefab'))
$templateGuid = [regex]::Match($blockTemplateMeta, '(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value
$sourceGuid = [regex]::Match($blockSourceMeta, '(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value
$runtimeSources = Get-ChildItem -LiteralPath $scripts -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/]Diagnostics[\\/]' }
$blockComponentCreation = @(
    foreach ($sourceFile in $runtimeSources) {
        if ([IO.File]::ReadAllText($sourceFile.FullName) -match 'AddComponent<Block>') {
            $sourceFile.FullName
        }
    }
)
$blockOwnsSceneLifetime =
    ($blockText -match 'partial class Block\s*:\s*(BaseObject|MonoBehaviour)') -or
    ($blockComponentCreation.Count -gt 0) -or
    ($terrainText -notmatch 'Block block = new Block\(\)') -or
    ($terrainText -notmatch 'loadedBlocks\.BindEntity\(') -or
    ($blockStoreText -notmatch 'No cell owns a GameObject, Transform or MonoBehaviour') -or
    ($blockTemplateText -notmatch 'sealed class BlockTemplate\s*:\s*MonoBehaviour') -or
    ([string]::IsNullOrEmpty($templateGuid)) -or
    ($blockPrefabText -notmatch [regex]::Escape("guid: $templateGuid")) -or
    ($blockPrefabText -match [regex]::Escape("guid: $sourceGuid"))
if ($blockOwnsSceneLifetime) {
    throw "Block runtime still owns scene lifetime: $($blockComponentCreation -join ', ')"
}
Write-Output 'PASS Block runtime is a handle-owned entity and BlockTemplate is authoring-only (source contract)'
$installationRoot = Join-Path $scripts 'Object/MapObj/InstallationObject'
$facilityWorldText = [IO.File]::ReadAllText((Join-Path $scripts 'Map/FacilitySimulationWorld.cs'))
$tickManagerText = [IO.File]::ReadAllText((Join-Path $scripts 'Manager/MapObjectTickManager.cs'))
$facilityTickIsCentralized =
    ($facilityWorldText -match 'class FacilitySimulationWorld\s*:\s*\r?\n\s*IMapObjectUpdateTick') -and
    ($facilityWorldText -notmatch 'class FacilitySimulationWorld\s*:\s*MonoBehaviour') -and
    ($facilityWorldText -match 'MapObjectTickManager\.RegisterUpdateTick\(this\)') -and
    ($tickManagerText -match 'FacilitySimulationWorld\.RestoreSimulationTick\(manager\.simulationTick\)')
if (-not $facilityTickIsCentralized) {
    throw 'Fluid/power facility Tick is not owned by one data world'
}
foreach ($relativePath in @('Bucket.cs', 'Fluid tank.cs', 'InputOutputModule.cs')) {
    $sourceText = [IO.File]::ReadAllText((Join-Path $installationRoot $relativePath))
    if ($sourceText -match 'MapObjectTickManager\.(Register|Unregister)UpdateTick\(this\)') {
        throw "Scene-owned fluid/power Tick remains in $relativePath"
    }
}
Write-Output 'PASS fluid/power facility Tick is centralized outside MonoBehaviour registration (source contract)'
$inputOutputText = [IO.File]::ReadAllText((Join-Path $installationRoot 'InputOutputModule.cs'))
$pipeText = [IO.File]::ReadAllText((Join-Path $installationRoot 'Pipe.cs'))
$installationText = [IO.File]::ReadAllText((Join-Path $installationRoot 'InstallationObject.cs'))
$fluidJobsText = [IO.File]::ReadAllText((Join-Path $scripts 'Map/TerrainGenerator.FluidJobs.cs'))
$fluidOutputLookupStart = $inputOutputText.IndexOf(
    'public static bool TryGetFluidOutputInfoAtRuntimeGridCoordinate(',
    [StringComparison]::Ordinal)
$fluidOutputLookupEnd = $inputOutputText.IndexOf(
    'public static bool TryGetInputItemIdsAtRuntimeGridCoordinate(',
    $fluidOutputLookupStart,
    [StringComparison]::Ordinal)
if ($fluidOutputLookupStart -lt 0 -or $fluidOutputLookupEnd -le $fluidOutputLookupStart) {
    throw 'Fluid output lookup boundary could not be located'
}
$fluidOutputLookupText = $inputOutputText.Substring(
    $fluidOutputLookupStart,
    $fluidOutputLookupEnd - $fluidOutputLookupStart)
$fluidDisplayLookupIsIndexedAndLocal =
    ($inputOutputText -match 'registeredRuntimeFluidOutputCoordinates') -and
    ($inputOutputText -match 'registeredRuntimeFluidStorageCoordinates') -and
    ($fluidOutputLookupText -notmatch 'activeRuntimeModules') -and
    ($fluidOutputLookupText -notmatch 'new HashSet') -and
    ($pipeText -match 'TryGetRuntimePipeDisplayFluidStorageAtCoordinate') -and
    ($pipeText -notmatch 'storage => CanDisplayStoredFluidAtCoordinate') -and
    ($installationText -match 'InvalidateFluidDisplayNetworkCache\(this\)') -and
    ($fluidJobsText -match 'fluidJobSourceCoordinateNetworks') -and
    ($fluidJobsText -match 'MarkFluidJobDisplayNetworkDirty') -and
    ($fluidJobsText -match 'DisplayResolveJob\.Run\(') -and
    ($fluidJobsText -notmatch 'DisplayResolveJob\.Schedule\(')
if (-not $fluidDisplayLookupIsIndexedAndLocal) {
    throw 'Fluid display still performs global, allocating or schedule-then-wait lookup work'
}
Write-Output 'PASS fluid display uses indexed, allocation-free, per-network dirty resolution (source contract)'
$knownSceneTickOwners = @(
    'LoggingMachine.cs',
    'Vehicle/SteamTrain.cs'
)
$remainingSceneTickOwners = @(
    foreach ($sourceFile in Get-ChildItem -LiteralPath $installationRoot -Recurse -Filter '*.cs') {
        $sourceText = [IO.File]::ReadAllText($sourceFile.FullName)
        if ($sourceText -match 'MapObjectTickManager\.RegisterUpdateTick\(this\)') {
            $relativePath = [IO.Path]::GetRelativePath($installationRoot, $sourceFile.FullName).Replace('\', '/')
            $relativePath
        }
    }
)
$unexpectedSceneTickOwners = @(
    $remainingSceneTickOwners | Where-Object { $knownSceneTickOwners -notcontains $_ }
)
if ($unexpectedSceneTickOwners.Count -gt 0) {
    throw "New scene-owned installation Tick path: $($unexpectedSceneTickOwners -join ', ')"
}
Write-Output "DEBT $($remainingSceneTickOwners.Count) known scene-owned installation Tick paths remain: $($remainingSceneTickOwners -join ', ')"
Write-Output 'PASS core has no engine dependency (source contract)'
