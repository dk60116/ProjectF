$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$refinerySourcePath = Join-Path $repositoryRoot 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/CrudeOilRefinery.cs'
$moduleSourcePath = Join-Path $repositoryRoot 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'
$uiSourcePath = Join-Path $repositoryRoot 'FactorioProject/Assets/Scripts/HUD/ObjectUI/ItemInfoDescription.cs'
$prefabPath = Join-Path $repositoryRoot 'FactorioProject/Assets/MapObject/InputOutputModule/Crude Oil Refinery/Crude Oil Refinery.prefab'

$refinerySource = [IO.File]::ReadAllText($refinerySourcePath)
$moduleSource = [IO.File]::ReadAllText($moduleSourcePath)
$pipeSource = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Pipe.cs'))
$uiSource = [IO.File]::ReadAllText($uiSourcePath)
$prefab = [IO.File]::ReadAllText($prefabPath)
$checks = 0

function Require([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }

    $script:checks++
}

$recipeStart = $prefab.IndexOf('  inputOutputPairs:', [StringComparison]::Ordinal)
$recipeEnd = $prefab.IndexOf('  inputList:', $recipeStart, [StringComparison]::Ordinal)
Require ($recipeStart -ge 0 -and $recipeEnd -gt $recipeStart) 'Crude Oil Refinery IOPair section is missing'
$recipe = $prefab.Substring($recipeStart, $recipeEnd - $recipeStart)
$recipeParts = $recipe -split "(?m)^    outputs:\s*$", 2
Require ($recipeParts.Count -eq 2) 'Crude Oil Refinery output recipe section is missing'
$inputGuids = [regex]::Matches($recipeParts[0], 'guid: ([0-9a-f]{32})') | ForEach-Object { $_.Groups[1].Value }
$outputGuids = [regex]::Matches($recipeParts[1], 'guid: ([0-9a-f]{32})') | ForEach-Object { $_.Groups[1].Value }
Require ($inputGuids.Count -eq 2) 'Crude Oil Refinery must have exactly two fluid inputs'
Require ($inputGuids[0] -ne $inputGuids[1]) 'Crude Oil Refinery inputs must have different fluid identities'
Require ($outputGuids.Count -eq 3) 'Crude Oil Refinery must have exactly three fluid outputs'

$placementStart = $prefab.IndexOf('  rectGridPlacements:', [StringComparison]::Ordinal)
$placementEnd = $prefab.IndexOf('  craftDuration:', $placementStart, [StringComparison]::Ordinal)
Require ($placementStart -ge 0 -and $placementEnd -gt $placementStart) 'Crude Oil Refinery RectGrid placement section is missing'
$placements = $prefab.Substring($placementStart, $placementEnd - $placementStart)
foreach ($guid in $inputGuids) {
    Require ($placements -match "blockType: 6\s+itemDefinition: \{fileID: 11400000, guid: $guid") "Input $guid is not assigned to a PipeInputItem RectArea"
}

foreach ($guid in $outputGuids) {
    Require ($placements -match "blockType: 7\s+itemDefinition: \{fileID: 11400000, guid: $guid") "Output $guid is not assigned to its own PipeOutputItem RectArea"
}

$inputPreflight = $refinerySource.IndexOf('TryGetConnectedFluidInputAvailableLitersAtCoordinate(', [StringComparison]::Ordinal)
$energyConsume = $refinerySource.IndexOf('TryConsumeOperatingEnergy(', [StringComparison]::Ordinal)
$inputConsume = $refinerySource.IndexOf('TryConsumeInputPort(', [StringComparison]::Ordinal)
$outputEmit = $refinerySource.IndexOf('TryEmitFluidOutputAtCoordinate(', [StringComparison]::Ordinal)
Require ($inputPreflight -ge 0 -and $inputPreflight -lt $energyConsume) 'Input availability must be checked before consuming power'
Require ($energyConsume -lt $inputConsume -and $inputConsume -lt $outputEmit) 'Refinery commit order must be power, inputs, then outputs'
Require ($refinerySource.Contains('IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;')) 'Refinery ports must resolve from the current RectGrid placements'
Require ($refinerySource.Contains('TryGetRectGridPlacementCoordinate(')) 'Refinery ports must follow runtime RectArea rotation and position'
Require ($moduleSource.Contains('fluidInputPortConnectionCaches')) 'Per-input-port fluid connection caches are missing'
Require ($moduleSource.Contains('fluidOutputPortConnectionCaches')) 'Per-output-port fluid connection caches are missing'
Require ($moduleSource.Contains('CoordinatesMatch(cachedFluidOutputSeedCoordinates, seedCoordinates)')) 'Fluid output cache is not keyed by its output-port coordinates'
Require ($moduleSource.Contains('AppendDedicatedFluidStorageRuntimeCoordinates(runtimeFluidStorageIndexCoordinates)')) 'Dedicated refinery input buffers are not registered as pipe storage endpoints'
Require ($moduleSource.Contains('TryAddFluidToOutputConnection(')) 'Fluid producers do not route output into dedicated input-port buffers'
Require ($moduleSource.Contains('refineryInputFluidUnits')) 'Refinery input-port buffers are not persisted'
Require (-not $refinerySource.Contains('RefineryState.OutputFull') -and -not $refinerySource.Contains('FluidOutputCoordinatesShareStorage(')) 'Unavailable outputs must not block independent byproducts'
Require ($refinerySource.Contains('TryAddDedicatedFluidAtRuntimeCoordinate(')) 'Refinery InputPipeArea does not accept direct pipe output'
Require ($refinerySource.Contains('TryConsumeInputPort(')) 'Refinery does not consume its direct input-port buffer'
$objectInfoInputStart = $refinerySource.IndexOf('public bool TryGetObjectInfoInput(', [StringComparison]::Ordinal)
$objectInfoInputEnd = $refinerySource.IndexOf('public bool TryGetObjectInfoOutput(', $objectInfoInputStart, [StringComparison]::Ordinal)
Require ($objectInfoInputStart -ge 0 -and $objectInfoInputEnd -gt $objectInfoInputStart) 'Refinery input object-info method is missing'
$objectInfoInput = $refinerySource.Substring($objectInfoInputStart, $objectInfoInputEnd - $objectInfoInputStart)
Require (-not $objectInfoInput.Contains('TryGetConnectedFluidInputAvailableLitersAtCoordinate(')) 'Refinery input UI must not add the whole connected tank network to its local buffer amount'
Require ($objectInfoInput.Contains('TryGetRuntimeFluidInputPressure(')) 'Refinery input UI must read pressure from its own connected input pipe'
Require ($refinerySource.Contains('InputPressureRefreshIntervalSeconds')) 'Refinery input pressure UI must not search the pipe network every frame'
Require ($moduleSource.Contains('protected bool TryGetRuntimeFluidInputPressure(')) 'Runtime input-pipe pressure query is missing'
Require ($moduleSource -match 'internal virtual bool TryGetRuntimePassiveFluidPass\([\s\S]*?out Vector2Int externalDirection\)\s*\{\s*//[^\r\n]*\r?\n\s*otherCoordinate = default;\s*externalDirection = default;\s*return false;') 'Independent fluid inputs must not create a passive pipe bridge'
Require (-not $moduleSource.Contains('TryGetPairedRuntimePipeInputPass(')) 'Implicit paired-input fluid bridge must be removed'
Require ($pipeSource -match 'TryGetRuntimePipeDisplayFluidStorageAtCoordinate\([\s\S]*?out InstallationObject areaStorage\)\s*&& areaStorage != null\s*&& CanDisplayStoredFluidAtCoordinate\(') 'Stored fluid identity must match the queried port'
Require ($pipeSource -match 'if \(\(neighborConnects \|\| neighborOutputFacesPipe\)\s*&& !foundFluid\s*&& TryGetAuthoritativeFluidInfoAtPipeNetworkCoordinate\(') 'Disconnected neighboring input storage must not choose pipe fluid identity'
Require ($moduleSource.Contains('pipeRecord.HasConnectionTowardsAt(')) 'Input pressure query must reject a pipe that does not face the refinery port'
Require ($uiSource.Contains('S/N: {FormatGaugeNumber(supplyLitersPerSecond, true)}/{FormatGaugeNumber(requiredLitersPerSecond, true)} L/s')) 'Refinery input UI must distinguish supply from required flow without wrapping'
Require ($uiSource.Contains('MoveDefaultItemAfter(nextDefaultItemIndex, previousInputRoot);')) 'Additional refinery inputs must follow the Input slot'
Require ($uiSource.Contains('MoveDefaultItemAfter(nextDefaultItemIndex, previousOutputRoot);')) 'Additional refinery outputs must follow the Output slot'
Require ($uiSource.Contains('previousOutputRoot = GetListItem(defaultItem, nextDefaultItemIndex++);')) 'Additional refinery outputs must preserve their order'
Require ($uiSource.Contains('RefreshCrudeOilRefineryInfo(')) 'Crude Oil Refinery details UI is missing'
Require ($uiSource.Contains('ObjectInfoInputCount') -and $uiSource.Contains('ObjectInfoOutputCount')) 'Crude Oil Refinery UI does not enumerate every recipe fluid'
Require ($prefab.Contains('  animator: {fileID: 0}')) 'Crude Oil Refinery must remain animation-free'

Write-Host "Crude Oil Refinery checks passed: $checks"
