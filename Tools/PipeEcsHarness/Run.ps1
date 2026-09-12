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
$underground = Read-Source 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/UndergroundPipe.cs'

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
Require-Text $underground 'PipeWorld.Current.HasOverlappingUndergroundRoute' 'underground collision checks include data-only routes'

Write-Output '19 pipe ECS integration checks passed.'
