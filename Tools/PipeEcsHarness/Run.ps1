$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

function Read-Source([string]$relativePath) {
    [IO.File]::ReadAllText((Join-Path $repo $relativePath))
}

function Require-Text([string]$source, [string]$text, [string]$label) {
    if (-not $source.Contains($text, [StringComparison]::Ordinal)) {
        throw "FAIL ${label}: missing '$text'"
    }
    Write-Output "PASS $label"
}

$pipeWorld = Read-Source 'FactorioProject/Assets/Scripts/Map/PipeWorld.cs'
$terrainItems = Read-Source 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Items.cs'
$chunkPersistence = Read-Source 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.ChunkPersistence.cs'
$placement = Read-Source 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InstallationPlacementController.cs'
$block = Read-Source 'FactorioProject/Assets/Scripts/Map/Block.cs'
$store = Read-Source 'FactorioProject/Assets/Scripts/Map/BlockStateStore.cs'
$focus = Read-Source 'FactorioProject/Assets/Scripts/Character/Player/PlayerController.cs'
$playerHud = Read-Source 'FactorioProject/Assets/Scripts/HUD/PlayerHUD.cs'
$objectInfoPanel = Read-Source 'FactorioProject/Assets/Scripts/HUD/ObjectUI/ObjectInfoPanel.cs'
$itemInfoDescription = Read-Source 'FactorioProject/Assets/Scripts/HUD/ObjectUI/ItemInfoDescription.cs'
$pump = Read-Source 'FactorioProject/Assets/Scripts/Object/MapObj/Pump.cs'
$underground = Read-Source 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/UndergroundPipe.cs'
$inputOutput = Read-Source 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'
$steamTrain = Read-Source 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/SteamTrain.cs'

Require-Text $pipeWorld 'private const string HostName = "PipeWorld";' 'all installed pipes share the PipeWorld host'
Require-Text $pipeWorld 'Dictionary<Vector2Int, PipeRuntimeRecord> recordsByStorageKey' 'installed pipe state is data-only'
Require-Text $pipeWorld 'VirtualRenderBatchCollection bodyBatches' 'pipe bodies use central instanced batches'
Require-Text $pipeWorld 'VirtualRenderBatchCollection fluidBatches' 'fluid displays use independent central batches'
Require-Text $pipeWorld 'private readonly Bounds[] focusBounds;' 'data-only pipes cache mesh-derived focus bounds'
Require-Text $pipeWorld 'public bool TryRaycast(' 'PipeWorld resolves focus without per-pipe colliders'
Require-Text $terrainItems 'RegisterDataOnlyPipeInstallation(Pipe pipe' 'newly installed pipes convert to runtime records'
Require-Text $terrainItems 'if (installationObject is Pipe)' 'temporary pipe presentations are destroyed instead of pooled'
Require-Text $chunkPersistence 'TryRestoreDataOnlyPipe(savedState)' 'saved pipes restore without scene instances'
Require-Text $placement 'TryMaterializeDataOnlyPipeForEditing' 'editing can temporarily materialize a pipe'
Require-Text $block 'BindRuntimePipe(PipeRuntimeRecord record)' 'loaded blocks bind to central pipe records'
Require-Text $store 'PipeWorld.Current?.Remove(storageKey);' 'removed installations clear central pipe records'
Require-Text $focus 'TryGetMatchingPipeRecord' 'data-only pipes retain mouse and interaction focus'
Require-Text $focus 'pipeWorld.TryRaycast(' 'mouse focus queries the central pipe bounds'
Require-Text $focus 'pipeWorld.TryGetAtCoordinate(coordinate, out PipeRuntimeRecord pipeRecord)' 'distance focus queries PipeWorld directly'
Require-Text $focus 'public bool TryGetFocusedPipe(out Pipe focusedPipe, out Block focusedBlock)' 'distance-focused pipes expose their concrete block'
Require-Text $playerHud 'PipeWorld.Current.TryGetMatchingAtCoordinate(' 'clicked pipe targets remain valid while their record exists'
Require-Text $objectInfoPanel 'target is ConveyorBelt || target is Pipe ? focusBlock : null' 'pipe info panels retain the selected endpoint block'
Require-Text $objectInfoPanel 'if (mapObject is Pump pump)' 'standard pumps use a dedicated info panel branch'
Require-Text $objectInfoPanel 'infoLine.ShowPump(pump, underlyingResource);' 'standard pump info is forwarded to the detail renderer'
Require-Text $itemInfoDescription 'public void ShowPump(Pump pump' 'standard pump info uses the shared fluid detail layout'
Require-Text $pump 'public bool TryGetObjectInfoFluidInfo(' 'standard pumps resolve connected fluid info'
Require-Text $pump 'CollectPumpPipePassesAtRuntimeCoordinate(coordinate, objectInfoPumpPasses)' 'pump info traverses consecutive pump connections'
Require-Text $underground 'PipeWorld.Current.HasOverlappingUndergroundRoute' 'underground collision checks include data-only routes'
Require-Text $pipeWorld 'InputOutputModule.NotifyRuntimePipeTopologyChanged(record.OccupiedCoordinates);' 'pipe record changes invalidate cached fluid routes'
Require-Text $inputOutput 'foreach (InputOutputModule module in activeRuntimeModules)' 'pipe changes wake sleeping fluid producers across the changed network'
Require-Text $inputOutput 'pipeRecord.HasConnectionTowardsAt(coordinate, direction)' 'fluid traversal uses data-only pipe connection rules'
Require-Text $inputOutput 'pipeRecord.TryGetRemoteConnectionCoordinate(coordinate, out remoteCoordinate)' 'fluid traversal crosses data-only underground pipe endpoints'
Require-Text $block 'TryGetRuntimePipeRecord(out PipeRuntimeRecord runtimeRecord);' 'direction arrows resolve the installed pipe record'
Require-Text $block 'runtimeRecord.HasConnectionTowardsAt(coordinate, direction)' 'direction arrows use authoritative tee connection masks'
Require-Text $block 'TryGetFluidDirectionPipeAtCoordinate(' 'direction arrows discover data-only neighbor pipes'
Require-Text $inputOutput 'SteamTrain.TryGetWaterPipeReceiverAtCoordinate(' 'fluid traversal resolves moving train water receivers outside Block.MapObject'
Require-Text $steamTrain 'WaterPipeReceiversByCoordinate' 'ready train docks publish their current receiver coordinate'
Require-Text $steamTrain 'InputOutputModule.NotifyRuntimePipeTopologyChanged(null);' 'train docking changes invalidate sleeping pump routes'
Require-Text $steamTrain 'pipeRecord.HasConnectionTowardsAt(coordinate, direction)' 'train water-source search uses data-only pipe connection rules'
Require-Text $steamTrain 'HasPumpWaterPipeRailPass(' 'train docking recognizes every Pump PipePass placed on rail'
Require-Text $steamTrain 'Do not gate it behind a second pipe-network traversal' 'train docking relies on the authoritative producer fluid traversal'
Require-Text $steamTrain '? lockedWaterPipeDockCoordinate' 'PipePass docking registers the rail cell as the water receiver'
Require-Text $inputOutput 'TryEnqueuePumpPressureResetPassesAt(' 'fluid traversal crosses every pump sharing a PipePass cell'
Require-Text $inputOutput 'HasRuntimePumpPipePassTowards(coordinate, directionToPrevious)' 'fluid nodes accept any matching pump at an overlapped PipePass'
Require-Text $inputOutput 'EnqueueInterlockedPumpEndpointsAt(coordinate, freezeCurrentPipeCount);' 'fluid traversal crosses reciprocally interlocked pump bodies'

Write-Output '41 pipe ECS integration checks passed.'
