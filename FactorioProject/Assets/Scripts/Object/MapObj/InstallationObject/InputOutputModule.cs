using System.Collections.Generic;
using UnityEngine;

public class InputOutputModule : InstallationObject, IMapObjectUpdateTick
{
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeGridCoordinates
        = new Dictionary<Vector2Int, HashSet<InputOutputModule>>();
    private static readonly HashSet<InputOutputModule> activeRuntimeModules
        = new HashSet<InputOutputModule>();

    private delegate bool RuntimeCoordinateValueCollector<T>(
        InputOutputModule module,
        Vector2Int coordinate,
        ISet<T> values);

    public enum SlotLayoutType
    {
        None = 0,
        RectGrid = 1
    }

    public enum RectGridBlockType
    {
        None = 0,
        Object = 1,
        InputEnergy = 2,
        InputItem = 3,
        Output = 4
    }

    public enum RectGridDirection
    {
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3
    }

    [System.Serializable]
    public struct ItemIoEntry
    {
        public ItemDefinition itemDefinition;
        public int count;

        public ItemIoEntry(ItemDefinition itemDefinition, int count)
        {
            this.itemDefinition = itemDefinition;
            this.count = count;
        }
    }

    [System.Serializable]
    public struct RectGridCell
    {
        public int x;
        public int y;

        public RectGridCell(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [System.Serializable]
    public struct RectGridBlockPlacement
    {
        public int x;
        public int y;
        public RectGridBlockType blockType;

        public RectGridBlockPlacement(int x, int y, RectGridBlockType blockType)
        {
            this.x = x;
            this.y = y;
            this.blockType = blockType;
        }
    }

    [System.Serializable]
    private struct RuntimeInputItemArea
    {
        public Vector2Int coordinate;
        public int itemId;

        public RuntimeInputItemArea(Vector2Int coordinate, int itemId)
        {
            this.coordinate = coordinate;
            this.itemId = itemId;
        }
    }

    [System.Serializable]
    public struct PersistentInputItemAreaState
    {
        public Vector2Int coordinate;
        public int itemId;

        public PersistentInputItemAreaState(Vector2Int coordinate, int itemId)
        {
            this.coordinate = coordinate;
            this.itemId = itemId;
        }
    }

    [System.Serializable]
    public sealed class PersistentState
    {
        public List<Vector2Int> inputEnergyCoordinates = new List<Vector2Int>();
        public List<PersistentInputItemAreaState> inputItemAreas = new List<PersistentInputItemAreaState>();
        public List<Vector2Int> outputCoordinates = new List<Vector2Int>();
        public List<Vector2Int> gridCoordinates = new List<Vector2Int>();
        public List<Vector2Int> focusCoordinates = new List<Vector2Int>();
        public float storedEnergy;
        public float energyGaugeCapacity;
        public bool hasActiveCraft;
        public bool waitingForOutput;
        public float remainingCraftTime;
        public float activeCraftConsumedEnergy;
        public int activeRecipeIndex = -1;
        public int activeOutputItemId = -1;
        public int activeOutputCount;

        public PersistentState Clone()
        {
            return new PersistentState
            {
                inputEnergyCoordinates = new List<Vector2Int>(inputEnergyCoordinates ?? new List<Vector2Int>()),
                inputItemAreas = new List<PersistentInputItemAreaState>(inputItemAreas ?? new List<PersistentInputItemAreaState>()),
                outputCoordinates = new List<Vector2Int>(outputCoordinates ?? new List<Vector2Int>()),
                gridCoordinates = new List<Vector2Int>(gridCoordinates ?? new List<Vector2Int>()),
                focusCoordinates = new List<Vector2Int>(focusCoordinates ?? new List<Vector2Int>()),
                storedEnergy = storedEnergy,
                energyGaugeCapacity = energyGaugeCapacity,
                hasActiveCraft = hasActiveCraft,
                waitingForOutput = waitingForOutput,
                remainingCraftTime = remainingCraftTime,
                activeCraftConsumedEnergy = activeCraftConsumedEnergy,
                activeRecipeIndex = activeRecipeIndex,
                activeOutputItemId = activeOutputItemId,
                activeOutputCount = activeOutputCount
            };
        }
    }

    [SerializeField]
    private List<ItemIoEntry> inputList = new List<ItemIoEntry>();
    [SerializeField]
    private List<ItemIoEntry> outputList = new List<ItemIoEntry>();
    [SerializeField, HideInInspector]
    private ItemIoEntry output = new ItemIoEntry(null, 1);
    [SerializeField]
    private SlotLayoutType slotLayoutType = SlotLayoutType.None;
    [SerializeField]
    private int rectGridWidth = 1;
    [SerializeField]
    private int rectGridHeight = 1;
    [SerializeField]
    private List<RectGridCell> rectGridCells = new List<RectGridCell>();
    [SerializeField]
    private List<RectGridBlockPlacement> rectGridPlacements = new List<RectGridBlockPlacement>();
    [SerializeField, Min(0.1f)]
    private float craftDuration = 5f;
    [SerializeField, Min(0f)]
    private float inputConsumeMoveInterval = 0.1f;
    [SerializeField, Min(0f)]
    private float outputMoveInterval = 0.1f;
    [SerializeField, Min(0f)]
    private float energyGaugeVerticalOffset = 0.25f;
    [SerializeField, Min(0f)]
    private float craftProgressGaugeCanvasVerticalOffset = 14f;
    [SerializeField]
    private Color energyGaugeFillColor = new Color(1f, 0.05f, 0f, 1f);
    [SerializeField]
    private Color craftProgressGaugeFillColor = new Color(0.026268482f, 1f, 0f, 1f);
    [SerializeField, Min(1)]
    private int runtimeAreaMaxObjects = 10;
    [SerializeField]
    private List<Vector2Int> runtimeInputEnergyCoordinates = new List<Vector2Int>();
    [SerializeField]
    private List<RuntimeInputItemArea> runtimeInputItemAreas = new List<RuntimeInputItemArea>();
    [SerializeField]
    private List<Vector2Int> runtimeOutputCoordinates = new List<Vector2Int>();
    [SerializeField]
    private List<Vector2Int> runtimeGridCoordinates = new List<Vector2Int>();
    [SerializeField]
    private List<Vector2Int> runtimeFocusCoordinates = new List<Vector2Int>();
    [SerializeField]
    private float storedEnergy;
    [SerializeField]
    private float energyGaugeCapacity;
    [SerializeField]
    private bool hasActiveCraft;
    [SerializeField]
    private bool waitingForOutput;
    [SerializeField]
    private float remainingCraftTime;
    [SerializeField]
    private float activeCraftConsumedEnergy;
    [SerializeField]
    private int activeRecipeIndex = -1;
    [SerializeField]
    private int activeOutputItemId = -1;
    [SerializeField]
    private int activeOutputCount;

    private TerrainGenerator cachedTerrain;
    private ItemDefinition cachedInstalledDefinition;
    private int cachedInstalledDefinitionId = int.MinValue;
    private DefaultGauge activeEnergyGauge;
    private DefaultGauge activeCraftProgressGauge;
    private readonly List<Renderer> cachedEnergyGaugeRenderers = new List<Renderer>();
    private readonly List<Vector2Int> objectInfoInputAreaCoordinates = new List<Vector2Int>();
    private bool energyGaugeRenderersResolved;

    public IReadOnlyList<ItemIoEntry> InputList
    {
        get
        {
            EnsurePairData();
            return inputList;
        }
    }

    public IReadOnlyList<ItemIoEntry> OutputList
    {
        get
        {
            EnsurePairData();
            return outputList;
        }
    }

    public ItemIoEntry Output
    {
        get
        {
            EnsurePairData();
            return outputList.Count > 0 ? outputList[0] : output;
        }
    }

    public SlotLayoutType LayoutType
    {
        get
        {
            EnsureRectGridData();
            return slotLayoutType;
        }
    }

    public int RectGridWidth
    {
        get
        {
            EnsureRectGridData();
            return rectGridWidth;
        }
    }

    public int RectGridHeight
    {
        get
        {
            EnsureRectGridData();
            return rectGridHeight;
        }
    }

    public IReadOnlyList<RectGridCell> RectGridCells
    {
        get
        {
            EnsureRectGridData();
            return rectGridCells;
        }
    }

    public IReadOnlyList<RectGridBlockPlacement> RectGridPlacements
    {
        get
        {
            EnsureRectGridPlacementData();
            return rectGridPlacements;
        }
    }

    public IReadOnlyList<Vector2Int> RuntimeGridCoordinates => runtimeGridCoordinates;
    public IReadOnlyList<Vector2Int> RuntimeFocusCoordinates => runtimeFocusCoordinates;

    public void ConfigureRuntimeAreas(
        IReadOnlyList<Vector2Int> inputEnergyCoordinates,
        IReadOnlyList<InputOutputModuleItemAreaBinding> inputItemBindings,
        IReadOnlyList<Vector2Int> outputCoordinates)
    {
        runtimeInputEnergyCoordinates.Clear();
        runtimeInputItemAreas.Clear();
        runtimeOutputCoordinates.Clear();

        AddUniqueCoordinates(inputEnergyCoordinates, runtimeInputEnergyCoordinates);
        AddUniqueCoordinates(outputCoordinates, runtimeOutputCoordinates);

        if (inputItemBindings != null)
        {
            for (int i = 0; i < inputItemBindings.Count; i++)
            {
                InputOutputModuleItemAreaBinding binding = inputItemBindings[i];
                if (binding.ItemId < 0 || ContainsRuntimeInputItemArea(binding.Coordinate, binding.ItemId))
                {
                    continue;
                }

                runtimeInputItemAreas.Add(new RuntimeInputItemArea(binding.Coordinate, binding.ItemId));
            }
        }

        cachedTerrain = null;
    }

    public void ConfigureRuntimeGridCoordinates(IReadOnlyList<Vector2Int> coordinates)
    {
        UnregisterRuntimeGridCoordinates();
        runtimeGridCoordinates.Clear();

        AddUniqueCoordinates(coordinates, runtimeGridCoordinates);
        RegisterRuntimeGridCoordinates();
    }

    public void ConfigureRuntimeFocusCoordinates(IReadOnlyList<Vector2Int> coordinates)
    {
        runtimeFocusCoordinates.Clear();
        AddUniqueCoordinates(coordinates, runtimeFocusCoordinates);
    }

    public PersistentState CapturePersistentState()
    {
        PersistentState state = new PersistentState
        {
            storedEnergy = storedEnergy,
            energyGaugeCapacity = energyGaugeCapacity,
            hasActiveCraft = hasActiveCraft,
            waitingForOutput = waitingForOutput,
            remainingCraftTime = remainingCraftTime,
            activeCraftConsumedEnergy = activeCraftConsumedEnergy,
            activeRecipeIndex = activeRecipeIndex,
            activeOutputItemId = activeOutputItemId,
            activeOutputCount = activeOutputCount
        };

        AddUniqueCoordinates(runtimeInputEnergyCoordinates, state.inputEnergyCoordinates);
        AddUniqueCoordinates(runtimeOutputCoordinates, state.outputCoordinates);
        AddUniqueCoordinates(runtimeGridCoordinates, state.gridCoordinates);
        AddUniqueCoordinates(runtimeFocusCoordinates, state.focusCoordinates);

        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            RuntimeInputItemArea area = runtimeInputItemAreas[i];
            state.inputItemAreas.Add(new PersistentInputItemAreaState(area.coordinate, area.itemId));
        }

        return state;
    }

    public void ApplyPersistentState(PersistentState state)
    {
        if (state == null)
        {
            return;
        }

        runtimeInputEnergyCoordinates.Clear();
        runtimeInputItemAreas.Clear();
        runtimeOutputCoordinates.Clear();
        runtimeFocusCoordinates.Clear();

        AddUniqueCoordinates(state.inputEnergyCoordinates, runtimeInputEnergyCoordinates);
        AddUniqueCoordinates(state.outputCoordinates, runtimeOutputCoordinates);
        AddUniqueCoordinates(state.focusCoordinates, runtimeFocusCoordinates);

        if (state.inputItemAreas != null)
        {
            for (int i = 0; i < state.inputItemAreas.Count; i++)
            {
                PersistentInputItemAreaState area = state.inputItemAreas[i];
                if (area.itemId < 0 || ContainsRuntimeInputItemArea(area.coordinate, area.itemId))
                {
                    continue;
                }

                runtimeInputItemAreas.Add(new RuntimeInputItemArea(area.coordinate, area.itemId));
            }
        }

        ConfigureRuntimeGridCoordinates(state.gridCoordinates);

        storedEnergy = Mathf.Max(0f, state.storedEnergy);
        energyGaugeCapacity = Mathf.Max(0f, state.energyGaugeCapacity);
        hasActiveCraft = state.hasActiveCraft;
        waitingForOutput = state.waitingForOutput;
        remainingCraftTime = Mathf.Max(0f, state.remainingCraftTime);
        activeCraftConsumedEnergy = Mathf.Max(0f, state.activeCraftConsumedEnergy);
        if (hasActiveCraft && !waitingForOutput && activeCraftConsumedEnergy <= 0.0001f)
        {
            activeCraftConsumedEnergy = ResolveConsumedEnergyFromRemainingTime(
                ResolveInstalledDefinition(),
                remainingCraftTime);
        }
        activeRecipeIndex = state.activeRecipeIndex;
        activeOutputItemId = state.activeOutputItemId;
        activeOutputCount = Mathf.Max(0, state.activeOutputCount);
        cachedTerrain = null;
    }

    public override void PrepareForPool()
    {
        UnregisterRuntimeGridCoordinates();
        ReleaseEnergyGaugeVisual();
        runtimeInputEnergyCoordinates.Clear();
        runtimeInputItemAreas.Clear();
        runtimeOutputCoordinates.Clear();
        runtimeGridCoordinates.Clear();
        runtimeFocusCoordinates.Clear();
        storedEnergy = 0f;
        energyGaugeCapacity = 0f;
        hasActiveCraft = false;
        waitingForOutput = false;
        remainingCraftTime = 0f;
        activeCraftConsumedEnergy = 0f;
        activeRecipeIndex = -1;
        activeOutputItemId = -1;
        activeOutputCount = 0;
        cachedTerrain = null;
        cachedInstalledDefinition = null;
        cachedInstalledDefinitionId = int.MinValue;
        base.PrepareForPool();
    }

    public static bool TryGetModuleAtRuntimeGridCoordinate(Vector2Int coordinate, out InputOutputModule module)
    {
        module = null;
        if (!registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            || modules == null
            || modules.Count <= 0)
        {
            return false;
        }

        foreach (InputOutputModule candidate in modules)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            module = candidate;
            return true;
        }

        return false;
    }

    public static bool TryGetModuleAtRuntimeAreaCoordinate(Vector2Int coordinate, out InputOutputModule module)
    {
        module = null;
        if (!registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            || modules == null
            || modules.Count <= 0)
        {
            return false;
        }

        foreach (InputOutputModule candidate in modules)
        {
            if (candidate == null
                || !candidate.gameObject.activeInHierarchy
                || !candidate.ContainsRuntimeAreaCoordinate(coordinate))
            {
                continue;
            }

            module = candidate;
            return true;
        }

        return false;
    }

    public static bool CoordinateIsRuntimeRectGridBlockType(Vector2Int coordinate, RectGridBlockType blockType)
    {
        if (blockType == RectGridBlockType.None
            || !registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            || modules == null
            || modules.Count <= 0)
        {
            return false;
        }

        foreach (InputOutputModule candidate in modules)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (candidate.ContainsRuntimeRectGridBlockType(coordinate, blockType))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetOutputItemIdsAtRuntimeGridCoordinate(Vector2Int coordinate, ISet<int> outputItemIds)
    {
        return TryGetRuntimeCoordinateValues(coordinate, outputItemIds, TryAppendRuntimeOutputItemIds);
    }

    public static bool TryGetInputItemIdsAtRuntimeGridCoordinate(Vector2Int coordinate, ISet<int> inputItemIds)
    {
        return TryGetRuntimeCoordinateValues(coordinate, inputItemIds, TryAppendRuntimeInputItemIds);
    }

    public static bool TryGetAcceptedInputItemIdsAtRuntimeGridCoordinate(Vector2Int coordinate, ISet<int> inputItemIds)
    {
        return TryGetRuntimeCoordinateValues(coordinate, inputItemIds, TryAppendAcceptedRuntimeInputItemIds);
    }

    public static bool TryGetInputEnergyTypesAtRuntimeGridCoordinate(
        Vector2Int coordinate,
        ISet<ItemDefinition.EnergyType> energyTypes)
    {
        return TryGetRuntimeCoordinateValues(coordinate, energyTypes, TryAppendRuntimeInputEnergyTypes);
    }

    private static bool TryGetRuntimeCoordinateValues<T>(
        Vector2Int coordinate,
        ISet<T> values,
        RuntimeCoordinateValueCollector<T> collectValues)
    {
        if (values == null || collectValues == null)
        {
            return false;
        }

        HashSet<InputOutputModule> visitedModules = new HashSet<InputOutputModule>();
        bool foundAny = false;
        if (registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            && modules != null
            && modules.Count > 0)
        {
            foundAny |= AppendRuntimeCoordinateValues(
                modules,
                coordinate,
                values,
                visitedModules,
                collectValues);
        }

        foundAny |= AppendRuntimeCoordinateValues(
            activeRuntimeModules,
            coordinate,
            values,
            visitedModules,
            collectValues);
        return foundAny;
    }

    private static bool AppendRuntimeCoordinateValues<T>(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        ISet<T> values,
        ISet<InputOutputModule> visitedModules,
        RuntimeCoordinateValueCollector<T> collectValues)
    {
        if (modules == null || values == null || collectValues == null)
        {
            return false;
        }

        bool foundAny = false;
        foreach (InputOutputModule module in modules)
        {
            if (module == null
                || !module.gameObject.activeInHierarchy
                || (visitedModules != null && !visitedModules.Add(module)))
            {
                continue;
            }

            foundAny |= collectValues(module, coordinate, values);
        }

        return foundAny;
    }

    private static bool TryAppendRuntimeOutputItemIds(
        InputOutputModule module,
        Vector2Int coordinate,
        ISet<int> outputItemIds)
    {
        if (module == null || !module.ContainsRuntimeOutputCoordinate(coordinate))
        {
            return false;
        }

        return module.AppendOutputItemIds(outputItemIds);
    }

    private static bool TryAppendRuntimeInputItemIds(
        InputOutputModule module,
        Vector2Int coordinate,
        ISet<int> inputItemIds)
    {
        return module != null && module.AppendRuntimeInputItemIdsAtCoordinate(coordinate, inputItemIds);
    }

    private static bool TryAppendAcceptedRuntimeInputItemIds(
        InputOutputModule module,
        Vector2Int coordinate,
        ISet<int> inputItemIds)
    {
        return module != null && module.AppendAcceptedRuntimeInputItemIdsAtCoordinate(coordinate, inputItemIds);
    }

    private static bool TryAppendRuntimeInputEnergyTypes(
        InputOutputModule module,
        Vector2Int coordinate,
        ISet<ItemDefinition.EnergyType> energyTypes)
    {
        return module != null && module.AppendRuntimeInputEnergyTypesAtCoordinate(coordinate, energyTypes);
    }

    public static bool RuntimeOutputCoordinateProducesItemId(Vector2Int coordinate, int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

        HashSet<int> outputItemIds = new HashSet<int>();
        return TryGetOutputItemIdsAtRuntimeGridCoordinate(coordinate, outputItemIds)
            && outputItemIds.Contains(itemId)
            && CanAddItemToRuntimeIoOverlapCoordinate(coordinate, itemId);
    }

    public static bool CanAddItemToRuntimeIoOverlapCoordinate(Vector2Int coordinate, int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

        HashSet<int> allowedItemIds = new HashSet<int>();
        return !TryGetRuntimeIoOverlapAllowedItemIds(coordinate, allowedItemIds)
            || allowedItemIds.Contains(itemId);
    }

    public static bool TryGetRuntimeIoOverlapAllowedItemIds(Vector2Int coordinate, ISet<int> allowedItemIds)
    {
        if (allowedItemIds == null)
        {
            return false;
        }

        HashSet<int> outputItemIds = new HashSet<int>();
        if (!TryGetOutputItemIdsAtRuntimeGridCoordinate(coordinate, outputItemIds)
            || outputItemIds.Count <= 0)
        {
            return false;
        }

        HashSet<int> inputItemIds = new HashSet<int>();
        bool hasInputItemArea = InputOutputModuleItemAreaController.TryGetAcceptedItemIds(coordinate, inputItemIds);
        hasInputItemArea |= TryGetAcceptedInputItemIdsAtRuntimeGridCoordinate(coordinate, inputItemIds);

        HashSet<ItemDefinition.EnergyType> inputEnergyTypes = new HashSet<ItemDefinition.EnergyType>();
        bool hasInputEnergyArea = InputOutputModuleEnergyAreaController.TryGetAcceptedEnergyTypes(coordinate, inputEnergyTypes);
        hasInputEnergyArea |= TryGetInputEnergyTypesAtRuntimeGridCoordinate(coordinate, inputEnergyTypes);

        if (!hasInputItemArea && !hasInputEnergyArea)
        {
            return false;
        }

        foreach (int outputItemId in outputItemIds)
        {
            if (outputItemId < 0)
            {
                continue;
            }

            if (hasInputItemArea && inputItemIds.Contains(outputItemId))
            {
                allowedItemIds.Add(outputItemId);
                continue;
            }

            if (hasInputEnergyArea && OutputItemMatchesEnergyTypes(outputItemId, inputEnergyTypes))
            {
                allowedItemIds.Add(outputItemId);
            }
        }

        return true;
    }

    private static bool OutputItemMatchesEnergyTypes(
        int outputItemId,
        ISet<ItemDefinition.EnergyType> energyTypes)
    {
        if (outputItemId < 0 || energyTypes == null || energyTypes.Count <= 0)
        {
            return false;
        }

        ItemDefinition outputDefinition = ResolveItemDefinition(outputItemId);
        return outputDefinition != null
            && outputDefinition.energyType != ItemDefinition.EnergyType.None
            && outputDefinition.energyAmount > 0
            && energyTypes.Contains(outputDefinition.energyType);
    }

    public bool ContainsRuntimeRectGridBlockType(Vector2Int coordinate, RectGridBlockType blockType)
    {
        if (blockType == RectGridBlockType.None
            || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
            || !TryGetPrimaryObjectCell(out Vector2Int objectCell))
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != blockType)
            {
                continue;
            }

            Vector2Int localOffset = new Vector2Int(placement.x - objectCell.x, placement.y - objectCell.y);
            Vector2Int runtimeCoordinate = anchorCoordinate + RotateCellOffset(localOffset, quarterTurns);
            if (runtimeCoordinate == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetRuntimeInputBlock(TerrainGenerator terrainGenerator, int preferredItemId, out Block block)
    {
        block = null;
        if (terrainGenerator == null || runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return false;
        }

        if (preferredItemId >= 0)
        {
            for (int i = 0; i < runtimeInputItemAreas.Count; i++)
            {
                RuntimeInputItemArea inputArea = runtimeInputItemAreas[i];
                if (inputArea.itemId != preferredItemId)
                {
                    continue;
                }

                if (terrainGenerator.TryGetLoadedBlock(inputArea.coordinate, out block) && block != null)
                {
                    return true;
                }
            }
        }

        Block firstBlock = null;
        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            RuntimeInputItemArea inputArea = runtimeInputItemAreas[i];
            if (!terrainGenerator.TryGetLoadedBlock(inputArea.coordinate, out Block candidateBlock) || candidateBlock == null)
            {
                continue;
            }

            if (firstBlock == null)
            {
                firstBlock = candidateBlock;
            }

            if (candidateBlock.GetInputAreaCenterItemCount() > 0)
            {
                block = candidateBlock;
                return true;
            }
        }

        block = firstBlock;
        return block != null;
    }

    public bool AppendRuntimeInputItemIds(ISet<int> inputItemIds)
    {
        if (inputItemIds == null || runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return false;
        }

        bool foundAny = false;
        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            int itemId = runtimeInputItemAreas[i].itemId;
            if (itemId < 0)
            {
                continue;
            }

            inputItemIds.Add(itemId);
            foundAny = true;
        }

        return foundAny;
    }

    private bool AppendRuntimeInputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> inputItemIds)
    {
        if (inputItemIds == null || runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return false;
        }

        bool foundAny = false;
        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            RuntimeInputItemArea inputArea = runtimeInputItemAreas[i];
            if (inputArea.coordinate != coordinate || inputArea.itemId < 0)
            {
                continue;
            }

            inputItemIds.Add(inputArea.itemId);
            foundAny = true;
        }

        return foundAny;
    }

    private bool AppendAcceptedRuntimeInputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> inputItemIds)
    {
        if (inputItemIds == null || runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return false;
        }

        int recipeCount = Mathf.Min(inputList.Count, outputList.Count);
        if (recipeCount <= 0)
        {
            return false;
        }

        bool foundAny = false;
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(recipeIndex, out int inputItemId, out _, out int outputItemId, out _)
                || inputItemId < 0
                || !IsRecipeOutputAllowedByItemFilter(outputItemId)
                || !TryResolveRuntimeInputItemArea(recipeIndex, inputItemId, out RuntimeInputItemArea inputArea)
                || inputArea.coordinate != coordinate)
            {
                continue;
            }

            inputItemIds.Add(inputItemId);
            foundAny = true;
        }

        return foundAny;
    }

    private bool AppendRuntimeInputEnergyTypesAtCoordinate(
        Vector2Int coordinate,
        ISet<ItemDefinition.EnergyType> energyTypes)
    {
        if (energyTypes == null
            || runtimeInputEnergyCoordinates == null
            || runtimeInputEnergyCoordinates.Count <= 0
            || !ContainsRuntimeRectGridBlockType(coordinate, RectGridBlockType.InputEnergy))
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null || installedDefinition.useEnergyType == ItemDefinition.EnergyType.None)
        {
            return false;
        }

        energyTypes.Add(installedDefinition.useEnergyType);
        return true;
    }

    public virtual void ManagedUpdateTick(float deltaTime)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsurePairData();
        if (hasActiveCraft)
        {
            UpdateActiveCraft(deltaTime);
        }
        else
        {
            TryStartNextCraft();
        }

        UpdateEnergyGaugeVisual();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        activeRuntimeModules.Add(this);
        RegisterRuntimeGridCoordinates();
        MapObjectTickManager.RegisterUpdateTick(this);
    }

    protected override void OnDisable()
    {
        MapObjectTickManager.UnregisterUpdateTick(this);
        UnregisterRuntimeGridCoordinates();
        activeRuntimeModules.Remove(this);
        ReleaseEnergyGaugeVisual();
        base.OnDisable();
    }

    private void OnDestroy()
    {
        MapObjectTickManager.UnregisterUpdateTick(this);
        UnregisterRuntimeGridCoordinates();
        activeRuntimeModules.Remove(this);
        ReleaseEnergyGaugeVisual();
    }

    private void EnsurePairData()
    {
        if (inputList == null)
        {
            inputList = new List<ItemIoEntry>();
        }

        if (outputList == null)
        {
            outputList = new List<ItemIoEntry>();
        }

        for (int i = 0; i < inputList.Count; i++)
        {
            ItemIoEntry entry = inputList[i];
            entry.count = Mathf.Max(1, entry.count);
            inputList[i] = entry;
        }

        if (outputList.Count == 0 && inputList.Count > 0)
        {
            ItemIoEntry migratedOutput = output;
            migratedOutput.count = Mathf.Max(1, migratedOutput.count);

            for (int i = 0; i < inputList.Count; i++)
            {
                outputList.Add(migratedOutput);
            }

            output = new ItemIoEntry(null, 1);
        }

        while (outputList.Count < inputList.Count)
        {
            outputList.Add(new ItemIoEntry(null, 1));
        }

        while (outputList.Count > inputList.Count)
        {
            outputList.RemoveAt(outputList.Count - 1);
        }

        for (int i = 0; i < outputList.Count; i++)
        {
            ItemIoEntry entry = outputList[i];
            entry.count = Mathf.Max(1, entry.count);
            outputList[i] = entry;
        }
    }

    private bool ContainsRuntimeOutputCoordinate(Vector2Int coordinate)
    {
        return ContainsCoordinate(runtimeOutputCoordinates, coordinate);
    }

    protected virtual bool AppendOutputItemIds(ISet<int> outputItemIds)
    {
        if (outputItemIds == null)
        {
            return false;
        }

        EnsurePairData();
        bool foundAny = false;
        for (int i = 0; i < outputList.Count; i++)
        {
            ItemDefinition itemDefinition = outputList[i].itemDefinition;
            if (itemDefinition == null || itemDefinition.id < 0)
            {
                continue;
            }

            outputItemIds.Add(itemDefinition.id);
            foundAny = true;
        }

        return foundAny;
    }

    private bool HasOutputItemId(int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

        EnsurePairData();
        for (int i = 0; i < outputList.Count; i++)
        {
            ItemDefinition itemDefinition = outputList[i].itemDefinition;
            if (itemDefinition != null && itemDefinition.id == itemId)
            {
                return true;
            }
        }

        return false;
    }

    public void ConfigureRectGrid(int width, int height)
    {
        slotLayoutType = SlotLayoutType.RectGrid;
        rectGridWidth = Mathf.Max(1, width);
        rectGridHeight = Mathf.Max(1, height);
        RebuildRectGridCells();
        EnsureRectGridPlacementData();
    }

    public void ClearRectGrid()
    {
        slotLayoutType = SlotLayoutType.None;
        rectGridCells.Clear();
        rectGridPlacements.Clear();
    }

    public RectGridBlockType GetRectGridBlockAt(int x, int y)
    {
        EnsureRectGridPlacementData();
        int placementIndex = FindRectGridPlacementIndex(x, y);
        return placementIndex >= 0
            ? rectGridPlacements[placementIndex].blockType
            : RectGridBlockType.None;
    }

    public bool TryGetRectGridBlockCell(RectGridBlockType blockType, out Vector2Int cell)
    {
        EnsureRectGridPlacementData();
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.blockType != blockType)
            {
                continue;
            }

            cell = new Vector2Int(placement.x, placement.y);
            return true;
        }

        cell = default;
        return false;
    }

    public bool TryGetPrimaryObjectCell(out Vector2Int cell)
    {
        EnsureRectGridPlacementData();
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.blockType != RectGridBlockType.Object)
            {
                continue;
            }

            cell = new Vector2Int(placement.x, placement.y);
            return true;
        }

        cell = default;
        return false;
    }

    public bool TryGetInitialOutputDirection(out RectGridDirection direction)
    {
        EnsureRectGridPlacementData();
        direction = RectGridDirection.Right;
        if (!TryGetPrimaryObjectCell(out Vector2Int objectCell)
            || !TryGetRectGridBlockCell(RectGridBlockType.Output, out Vector2Int outputCell))
        {
            return false;
        }

        Vector2Int delta = outputCell - objectCell;
        return TryConvertOffsetToDirection(delta, out direction);
    }

    public static RectGridDirection RotateDirection(RectGridDirection direction, int quarterTurns)
    {
        int normalizedTurns = ((quarterTurns % 4) + 4) % 4;
        return (RectGridDirection)(((int)direction + normalizedTurns) % 4);
    }

    public bool TryGetOutputDirection(int quarterTurns, out RectGridDirection direction)
    {
        EnsureRectGridPlacementData();
        direction = RectGridDirection.Right;
        if (!TryGetPrimaryObjectCell(out Vector2Int objectCell)
            || !TryGetRectGridBlockCell(RectGridBlockType.Output, out Vector2Int outputCell))
        {
            return false;
        }

        Vector2Int delta = outputCell - objectCell;
        delta = RotateCellOffset(delta, quarterTurns);
        return TryConvertOffsetToDirection(delta, out direction);
    }

    public bool HasStoredOperationalEnergy()
    {
        return storedEnergy > 0f;
    }

    public bool HasActiveOrPendingCraft()
    {
        return hasActiveCraft || waitingForOutput;
    }

    public int RuntimeAreaMaxObjects => Mathf.Max(1, runtimeAreaMaxObjects);
    public float CraftDurationSeconds => Mathf.Max(0.1f, craftDuration);
    public float ObjectInfoStoredEnergy => Mathf.Max(0f, storedEnergy);
    public float ObjectInfoEnergyGaugeCapacity => Mathf.Max(0f, energyGaugeCapacity, storedEnergy);
    public float ObjectInfoEnergyGaugeFillAmount => ResolveEnergyGaugeFillAmount(ResolveInstalledDefinition());
    public float ObjectInfoWorkGaugeFillAmount => ResolveCraftProgressGaugeFillAmount();
    public Color ObjectInfoEnergyGaugeFillColor => energyGaugeFillColor;
    public Color ObjectInfoWorkGaugeFillColor => craftProgressGaugeFillColor;
    public float ObjectInfoCurrentUseEnergy => ResolveObjectInfoCurrentUseEnergy();
    public float ObjectInfoCompleteEnergy => ResolveObjectInfoCompleteEnergy();

    public void GetObjectInfoStatus(out string statusText, out bool isProducing)
    {
        statusText = ResolveObjectInfoStatus(out isProducing);
    }

    protected virtual string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = false;

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null)
        {
            return "No machine";
        }

        if (waitingForOutput)
        {
            return "Output full";
        }

        if (hasActiveCraft)
        {
            if (!HasOperationalEnergyAvailable(installedDefinition))
            {
                return "No energy";
            }

            isProducing = true;
            return "Working";
        }

        if (runtimeOutputCoordinates == null || runtimeOutputCoordinates.Count <= 0)
        {
            return "No output area";
        }

        int recipeCount = Mathf.Min(inputList.Count, outputList.Count);
        if (recipeCount <= 0)
        {
            return "No recipe";
        }

        bool hasRecipe = false;
        bool blockedByInputArea = false;
        bool blockedByInputItem = false;
        bool blockedByOutput = false;
        bool blockedByEnergy = false;
        bool blockedByTargetFilter = false;
        bool hasFilterAllowedRecipe = false;

        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(recipeIndex, out int inputItemId, out int inputCount, out int outputItemId, out int outputCount))
            {
                continue;
            }

            hasRecipe = true;
            if (!IsRecipeOutputAllowedByItemFilter(outputItemId))
            {
                blockedByTargetFilter = true;
                continue;
            }

            hasFilterAllowedRecipe = true;

            if (!TryResolveRuntimeInputItemArea(recipeIndex, inputItemId, out RuntimeInputItemArea inputArea)
                || !TryGetLoadedBlock(inputArea.coordinate, out Block inputBlock)
                || inputBlock == null)
            {
                blockedByInputArea = true;
                continue;
            }

            if (inputBlock.GetInputAreaCenterItemCount(inputItemId) < inputCount)
            {
                blockedByInputItem = true;
                continue;
            }

            if (!TryResolveOutputBlock(outputItemId, outputCount, out _))
            {
                blockedByOutput = true;
                continue;
            }

            if (!HasOperationalEnergyAvailable(installedDefinition))
            {
                blockedByEnergy = true;
                continue;
            }

            isProducing = true;
            return "Working";
        }

        if (!hasRecipe)
        {
            return "No recipe";
        }

        if (blockedByOutput)
        {
            return "Output full";
        }

        if (blockedByEnergy)
        {
            return "No energy";
        }

        if (blockedByTargetFilter && !hasFilterAllowedRecipe)
        {
            return "No target";
        }

        if (blockedByInputItem)
        {
            return "No input item";
        }

        if (blockedByInputArea)
        {
            return "No input area";
        }

        return "Stopped";
    }

    public bool TryGetObjectInfoEnergyInput(
        out int energyItemId,
        out int energyAreaCount,
        out int energyAreaCapacity)
    {
        energyItemId = -1;
        energyAreaCount = 0;
        energyAreaCapacity = 0;

        if (!RequiresOperationalEnergy(ResolveInstalledDefinition())
            || runtimeInputEnergyCoordinates == null
            || runtimeInputEnergyCoordinates.Count <= 0)
        {
            return false;
        }

        energyAreaCapacity = Mathf.Max(ResolveRuntimeAreaCapacity(runtimeInputEnergyCoordinates), 1);
        energyItemId = GetRuntimeAreaTopItemId(runtimeInputEnergyCoordinates);
        if (energyItemId >= 0)
        {
            energyAreaCount = GetRuntimeAreaObjectCount(runtimeInputEnergyCoordinates, energyItemId);
            energyAreaCapacity = Mathf.Max(energyAreaCapacity, energyAreaCount);
        }

        return true;
    }

    public bool TryGetObjectInfoItemPair(
        out int inputItemId,
        out int inputAreaCount,
        out int inputAreaCapacity,
        out int outputItemId,
        out int outputAreaCount,
        out int outputAreaCapacity)
    {
        EnsurePairData();

        int inputRecipeCount;
        int outputRecipeCount;
        if ((hasActiveCraft || waitingForOutput)
            && activeRecipeIndex >= 0
            && TryGetObjectInfoRecipeLine(
                activeRecipeIndex,
                out inputItemId,
                out inputRecipeCount,
                out outputItemId,
                out outputRecipeCount))
        {
            if (activeOutputItemId >= 0 && activeOutputCount > 0)
            {
                outputItemId = activeOutputItemId;
                outputRecipeCount = activeOutputCount;
            }

            if (!TryResolveObjectInfoAreaCounts(
                inputItemId,
                outputItemId,
                inputRecipeCount,
                outputRecipeCount,
                out inputAreaCount,
                out inputAreaCapacity,
                out outputAreaCount,
                out outputAreaCapacity))
            {
                ResetObjectInfoItemPair(
                    out inputItemId,
                    out inputAreaCount,
                    out inputAreaCapacity,
                    out outputItemId,
                    out outputAreaCount,
                    out outputAreaCapacity);
                return false;
            }

            return true;
        }

        if (TryGetOccupiedObjectInfoItemPair(
            out inputItemId,
            out inputAreaCount,
            out inputAreaCapacity,
            out outputItemId,
            out outputAreaCount,
            out outputAreaCapacity))
        {
            return true;
        }

        for (int i = 0; i < inputList.Count; i++)
        {
            if (TryGetObjectInfoRecipeLine(
                    i,
                    out inputItemId,
                    out inputRecipeCount,
                    out outputItemId,
                    out outputRecipeCount))
            {
                if (!IsRecipeOutputAllowedByItemFilter(outputItemId))
                {
                    continue;
                }

                if (!TryResolveObjectInfoAreaCounts(
                    inputItemId,
                    outputItemId,
                    inputRecipeCount,
                    outputRecipeCount,
                    out inputAreaCount,
                    out inputAreaCapacity,
                    out outputAreaCount,
                    out outputAreaCapacity))
                {
                    continue;
                }

                if (inputAreaCount <= 0
                    && outputAreaCount <= 0
                    && !ShouldShowObjectInfoEmptyRecipeLine(outputItemId))
                {
                    continue;
                }

                return true;
            }
        }

        if (ShouldShowObjectInfoEmptyInputOutputSlots())
        {
            ResetObjectInfoItemPair(
                out inputItemId,
                out inputAreaCount,
                out inputAreaCapacity,
                out outputItemId,
                out outputAreaCount,
                out outputAreaCapacity);
            return true;
        }

        ResetObjectInfoItemPair(
            out inputItemId,
            out inputAreaCount,
            out inputAreaCapacity,
            out outputItemId,
            out outputAreaCount,
            out outputAreaCapacity);
        return false;
    }

    public virtual bool TryGetObjectInfoOutput(
        out int outputItemId,
        out int outputAreaCount,
        out int outputAreaCapacity,
        out bool displayZeroCountItem)
    {
        outputItemId = -1;
        outputAreaCount = 0;
        outputAreaCapacity = 0;
        displayZeroCountItem = false;

        if (activeOutputItemId < 0 || activeOutputCount <= 0)
        {
            return false;
        }

        outputItemId = activeOutputItemId;
        displayZeroCountItem = true;
        return TryResolveObjectInfoOutputAreaCounts(
            outputItemId,
            activeOutputCount,
            out outputAreaCount,
            out outputAreaCapacity);
    }

    private bool TryGetOccupiedObjectInfoItemPair(
        out int inputItemId,
        out int inputAreaCount,
        out int inputAreaCapacity,
        out int outputItemId,
        out int outputAreaCount,
        out int outputAreaCapacity)
    {
        int inputRecipeCount;
        int outputRecipeCount;
        for (int i = 0; i < inputList.Count; i++)
        {
            if (!TryGetObjectInfoRecipeLine(
                    i,
                    out inputItemId,
                    out inputRecipeCount,
                    out outputItemId,
                    out outputRecipeCount))
            {
                continue;
            }

            if (!IsRecipeOutputAllowedByItemFilter(outputItemId))
            {
                continue;
            }

            if (!TryResolveObjectInfoAreaCounts(
                    inputItemId,
                    outputItemId,
                    inputRecipeCount,
                    outputRecipeCount,
                    out inputAreaCount,
                    out inputAreaCapacity,
                    out outputAreaCount,
                    out outputAreaCapacity)
                || inputAreaCount <= 0)
            {
                continue;
            }

            return true;
        }

        ResetObjectInfoItemPair(
            out inputItemId,
            out inputAreaCount,
            out inputAreaCapacity,
            out outputItemId,
            out outputAreaCount,
            out outputAreaCapacity);
        return false;
    }

    private static void ResetObjectInfoItemPair(
        out int inputItemId,
        out int inputAreaCount,
        out int inputAreaCapacity,
        out int outputItemId,
        out int outputAreaCount,
        out int outputAreaCapacity)
    {
        inputItemId = -1;
        inputAreaCount = 0;
        inputAreaCapacity = 0;
        outputItemId = -1;
        outputAreaCount = 0;
        outputAreaCapacity = 0;
    }

    private bool TryResolveObjectInfoAreaCounts(
        int inputItemId,
        int outputItemId,
        int inputRecipeCount,
        int outputRecipeCount,
        out int inputAreaCount,
        out int inputAreaCapacity,
        out int outputAreaCount,
        out int outputAreaCapacity)
    {
        objectInfoInputAreaCoordinates.Clear();
        AppendRuntimeInputItemAreaCoordinates(inputItemId, objectInfoInputAreaCoordinates);
        if (objectInfoInputAreaCoordinates.Count <= 0)
        {
            inputAreaCount = 0;
            inputAreaCapacity = 0;
            outputAreaCount = 0;
            outputAreaCapacity = 0;
            return false;
        }

        inputAreaCount = GetRuntimeAreaObjectCount(objectInfoInputAreaCoordinates, inputItemId);
        inputAreaCapacity = Mathf.Max(
            ResolveRuntimeAreaCapacity(objectInfoInputAreaCoordinates),
            Mathf.Max(inputAreaCount, inputRecipeCount));

        if (outputItemId >= 0)
        {
            outputAreaCount = GetRuntimeAreaObjectCount(runtimeOutputCoordinates, outputItemId);
            outputAreaCapacity = Mathf.Max(
                ResolveRuntimeAreaCapacity(runtimeOutputCoordinates),
                Mathf.Max(outputAreaCount, outputRecipeCount));
        }
        else
        {
            outputAreaCount = 0;
            outputAreaCapacity = 0;
        }

        return true;
    }

    protected bool TryResolveObjectInfoOutputAreaCounts(
        int outputItemId,
        int outputRecipeCount,
        out int outputAreaCount,
        out int outputAreaCapacity)
    {
        outputAreaCount = 0;
        outputAreaCapacity = 0;

        if (outputItemId < 0)
        {
            return false;
        }

        outputAreaCount = GetRuntimeAreaObjectCount(runtimeOutputCoordinates, outputItemId);
        outputAreaCapacity = Mathf.Max(
            ResolveRuntimeAreaCapacity(runtimeOutputCoordinates),
            Mathf.Max(outputAreaCount, Mathf.Max(1, outputRecipeCount)));
        return true;
    }

    private bool TryGetObjectInfoRecipeLine(
        int recipeIndex,
        out int inputItemId,
        out int inputCount,
        out int outputItemId,
        out int outputCount)
    {
        inputItemId = -1;
        inputCount = 0;
        outputItemId = -1;
        outputCount = 0;

        if (recipeIndex < 0 || recipeIndex >= inputList.Count)
        {
            return false;
        }

        ItemIoEntry inputEntry = inputList[recipeIndex];
        inputItemId = inputEntry.itemDefinition != null ? inputEntry.itemDefinition.id : -1;
        inputCount = Mathf.Max(1, inputEntry.count);

        if (recipeIndex < outputList.Count)
        {
            ItemIoEntry outputEntry = outputList[recipeIndex];
            outputItemId = outputEntry.itemDefinition != null ? outputEntry.itemDefinition.id : -1;
            outputCount = Mathf.Max(1, outputEntry.count);
        }

        return inputItemId >= 0;
    }

    private void AppendRuntimeInputItemAreaCoordinates(int itemId, List<Vector2Int> coordinates)
    {
        if (itemId < 0 || coordinates == null || runtimeInputItemAreas == null)
        {
            return;
        }

        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            RuntimeInputItemArea area = runtimeInputItemAreas[i];
            if (area.itemId == itemId && !coordinates.Contains(area.coordinate))
            {
                coordinates.Add(area.coordinate);
            }
        }
    }

    public int ResolveRuntimeAreaCapacity(IReadOnlyList<Vector2Int> coordinates)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return RuntimeAreaMaxObjects;
        }

        int installedCapacityTotal = 0;
        bool hasInstalledCapacity = false;
        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            if (!TryGetLoadedBlock(coordinate, out Block block) || block == null)
            {
                continue;
            }

            if (!block.TryGetInstalledItemAreaCapacity(out int blockCapacity))
            {
                continue;
            }

            installedCapacityTotal += Mathf.Max(1, blockCapacity);
            hasInstalledCapacity = true;
        }

        return hasInstalledCapacity
            ? Mathf.Max(1, installedCapacityTotal)
            : RuntimeAreaMaxObjects;
    }

    public bool HasAvailableOutputItem(int itemId)
    {
        return TryFindOutputSourceBlock(itemId, out _, out _);
    }

    public bool TryMoveOneOutputItemToInput(int itemId, Vector2Int targetCoordinate)
    {
        if (itemId < 0)
        {
            return false;
        }

        if (!TryGetLoadedBlock(targetCoordinate, out Block targetBlock) || targetBlock == null)
        {
            return false;
        }

        if (targetBlock.Type != Block.BlockType.Ground || !targetBlock.CanAddInputAreaCenterObjects(1, itemId))
        {
            return false;
        }

        if (!TryFindOutputSourceBlock(itemId, out Block sourceBlock, out Vector3 startWorldPosition)
            || sourceBlock == null
            || sourceBlock == targetBlock)
        {
            return false;
        }

        if (!sourceBlock.TryConsumeOneInputAreaCenterObject(itemId, out int consumedItemId) || consumedItemId != itemId)
        {
            return false;
        }

        if (targetBlock.TryAddInputAreaCenterObjectAnimated(itemId, startWorldPosition, 0f, out PortableObject droppedObject))
        {
            DroppedItemPickupGate gate = droppedObject != null ? droppedObject.GetComponent<DroppedItemPickupGate>() : null;
            gate?.SetAutoPickupBlocked(true);
            return true;
        }

        sourceBlock.TryAddInputAreaCenterObjectAnimated(itemId, startWorldPosition, 0f, out PortableObject restoredObject);
        DroppedItemPickupGate restoreGate = restoredObject != null ? restoredObject.GetComponent<DroppedItemPickupGate>() : null;
        restoreGate?.SetAutoPickupBlocked(true);
        return false;
    }

    private static bool TryConvertOffsetToDirection(Vector2Int delta, out RectGridDirection direction)
    {
        direction = RectGridDirection.Right;
        if (delta == Vector2Int.zero)
        {
            return false;
        }

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
        {
            direction = delta.x >= 0 ? RectGridDirection.Right : RectGridDirection.Left;
            return true;
        }

        direction = delta.y >= 0 ? RectGridDirection.Up : RectGridDirection.Down;
        return true;
    }

    private static Vector2Int RotateCellOffset(Vector2Int offset, int quarterTurns)
    {
        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        return normalizedQuarterTurns switch
        {
            1 => new Vector2Int(offset.y, -offset.x),
            2 => new Vector2Int(-offset.x, -offset.y),
            3 => new Vector2Int(-offset.y, offset.x),
            _ => offset
        };
    }

    public void SetRectGridBlock(int x, int y, RectGridBlockType blockType)
    {
        EnsureRectGridData();
        EnsureRectGridPlacementData();
        if (!IsValidRectGridCell(x, y))
        {
            return;
        }

        RemoveRectGridBlockAt(x, y);
        if (blockType == RectGridBlockType.None)
        {
            return;
        }

        if (IsUniqueRectGridBlock(blockType))
        {
            RemoveRectGridBlock(blockType);
        }

        if (blockType == RectGridBlockType.Object && GetRectGridObjectCount() >= GetMaxObjectBlockCount())
        {
            return;
        }

        rectGridPlacements.Add(new RectGridBlockPlacement(x, y, blockType));
    }

    public void MoveOrSwapRectGridBlock(Vector2Int sourceCell, Vector2Int targetCell)
    {
        EnsureRectGridData();
        EnsureRectGridPlacementData();
        if (!IsValidRectGridCell(sourceCell.x, sourceCell.y) || !IsValidRectGridCell(targetCell.x, targetCell.y))
        {
            return;
        }

        if (sourceCell == targetCell)
        {
            return;
        }

        RectGridBlockType sourceBlockType = GetRectGridBlockAt(sourceCell.x, sourceCell.y);
        if (sourceBlockType == RectGridBlockType.None)
        {
            return;
        }

        RectGridBlockType targetBlockType = GetRectGridBlockAt(targetCell.x, targetCell.y);
        SetRectGridBlockInternal(targetCell.x, targetCell.y, sourceBlockType);
        SetRectGridBlockInternal(sourceCell.x, sourceCell.y, targetBlockType);
        EnsureRectGridPlacementData();
    }

    public void RemoveRectGridBlockAt(int x, int y)
    {
        EnsureRectGridPlacementData();
        int placementIndex = FindRectGridPlacementIndex(x, y);
        if (placementIndex >= 0)
        {
            rectGridPlacements.RemoveAt(placementIndex);
        }
    }

    private void EnsureRectGridData()
    {
        rectGridWidth = Mathf.Max(1, rectGridWidth);
        rectGridHeight = Mathf.Max(1, rectGridHeight);

        if (rectGridCells == null)
        {
            rectGridCells = new List<RectGridCell>();
        }

        if (slotLayoutType != SlotLayoutType.RectGrid)
        {
            if (rectGridCells.Count > 0)
            {
                rectGridCells.Clear();
            }

            return;
        }

        int expectedCount = Mathf.Max(1, rectGridWidth) * Mathf.Max(1, rectGridHeight);
        bool requiresRebuild = rectGridCells.Count != expectedCount;

        if (!requiresRebuild)
        {
            int index = 0;
            for (int y = rectGridHeight - 1; y >= 0 && !requiresRebuild; y--)
            {
                for (int x = 0; x < rectGridWidth; x++)
                {
                    RectGridCell cell = rectGridCells[index++];
                    if (cell.x != x || cell.y != y)
                    {
                        requiresRebuild = true;
                        break;
                    }
                }
            }
        }

        if (requiresRebuild)
        {
            RebuildRectGridCells();
        }
    }

    private void EnsureRectGridPlacementData()
    {
        if (rectGridPlacements == null)
        {
            rectGridPlacements = new List<RectGridBlockPlacement>();
        }

        if (slotLayoutType != SlotLayoutType.RectGrid)
        {
            if (rectGridPlacements.Count > 0)
            {
                rectGridPlacements.Clear();
            }

            return;
        }

        List<RectGridBlockPlacement> normalizedPlacements = new List<RectGridBlockPlacement>();
        HashSet<int> occupiedCells = new HashSet<int>();
        int objectCount = 0;
        bool hasInputEnergy = false;
        bool hasOutput = false;
        int maxObjectCount = GetMaxObjectBlockCount();

        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.blockType == RectGridBlockType.None || !IsValidRectGridCell(placement.x, placement.y))
            {
                continue;
            }

            int cellKey = placement.y * rectGridWidth + placement.x;
            if (occupiedCells.Contains(cellKey))
            {
                continue;
            }

            if (placement.blockType == RectGridBlockType.Object)
            {
                if (objectCount >= maxObjectCount)
                {
                    continue;
                }

                objectCount++;
            }
            else if (placement.blockType == RectGridBlockType.InputEnergy)
            {
                if (hasInputEnergy)
                {
                    continue;
                }

                hasInputEnergy = true;
            }
            else if (placement.blockType == RectGridBlockType.Output)
            {
                if (hasOutput)
                {
                    continue;
                }

                hasOutput = true;
            }

            occupiedCells.Add(cellKey);
            normalizedPlacements.Add(placement);
        }

        rectGridPlacements = normalizedPlacements;
    }

    private void RebuildRectGridCells()
    {
        if (rectGridCells == null)
        {
            rectGridCells = new List<RectGridCell>();
        }

        rectGridCells.Clear();
        if (slotLayoutType != SlotLayoutType.RectGrid)
        {
            return;
        }

        for (int y = rectGridHeight - 1; y >= 0; y--)
        {
            for (int x = 0; x < rectGridWidth; x++)
            {
                rectGridCells.Add(new RectGridCell(x, y));
            }
        }
    }

    private bool IsValidRectGridCell(int x, int y)
    {
        return x >= 0 && x < rectGridWidth && y >= 0 && y < rectGridHeight;
    }

    private int FindRectGridPlacementIndex(int x, int y)
    {
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.x == x && placement.y == y)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveRectGridBlock(RectGridBlockType blockType)
    {
        for (int i = rectGridPlacements.Count - 1; i >= 0; i--)
        {
            if (rectGridPlacements[i].blockType == blockType)
            {
                rectGridPlacements.RemoveAt(i);
            }
        }
    }

    private void SetRectGridBlockInternal(int x, int y, RectGridBlockType blockType)
    {
        int placementIndex = FindRectGridPlacementIndex(x, y);
        if (blockType == RectGridBlockType.None)
        {
            if (placementIndex >= 0)
            {
                rectGridPlacements.RemoveAt(placementIndex);
            }

            return;
        }

        RectGridBlockPlacement placement = new RectGridBlockPlacement(x, y, blockType);
        if (placementIndex >= 0)
        {
            rectGridPlacements[placementIndex] = placement;
            return;
        }

        rectGridPlacements.Add(placement);
    }

    private static bool IsUniqueRectGridBlock(RectGridBlockType blockType)
    {
        return blockType == RectGridBlockType.InputEnergy
            || blockType == RectGridBlockType.Output;
    }

    private int GetRectGridObjectCount()
    {
        int count = 0;
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            if (rectGridPlacements[i].blockType == RectGridBlockType.Object)
            {
                count++;
            }
        }

        return count;
    }

    private int GetMaxObjectBlockCount()
    {
        int mapSizeX = Mathf.Max(1, Status.mapSizeX);
        int mapSizeY = Mathf.Max(1, Status.mapSizeY);
        return mapSizeX * mapSizeY;
    }

    private void UpdateActiveCraft(float deltaTime)
    {
        if (!hasActiveCraft)
        {
            return;
        }

        if (waitingForOutput)
        {
            TryCompleteActiveCraft();
            return;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (RequiresOperationalEnergy(installedDefinition))
        {
            if (!TryConsumeOperatingEnergy(deltaTime, out float consumedEnergy))
            {
                return;
            }

            activeCraftConsumedEnergy += consumedEnergy;
            remainingCraftTime = ResolveRemainingEnergyCraftTime(installedDefinition, activeCraftConsumedEnergy);
            if (activeCraftConsumedEnergy + 0.0001f < ResolveCompleteEnergy(installedDefinition))
            {
                return;
            }
        }
        else
        {
            remainingCraftTime = Mathf.Max(0f, remainingCraftTime - deltaTime);
            if (remainingCraftTime > 0f)
            {
                return;
            }
        }

        waitingForOutput = true;
        TryCompleteActiveCraft();
    }

    protected virtual void TryStartNextCraft()
    {
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null || runtimeInputItemAreas.Count <= 0 || runtimeOutputCoordinates.Count <= 0)
        {
            return;
        }

        int recipeCount = Mathf.Min(inputList.Count, outputList.Count);
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(recipeIndex, out int inputItemId, out int inputCount, out int outputItemId, out int outputCount))
            {
                continue;
            }

            if (!IsRecipeOutputAllowedByItemFilter(outputItemId))
            {
                continue;
            }

            if (!TryResolveRuntimeInputItemArea(recipeIndex, inputItemId, out RuntimeInputItemArea inputArea))
            {
                continue;
            }

            if (!TryGetLoadedBlock(inputArea.coordinate, out Block inputBlock) || inputBlock == null)
            {
                continue;
            }

            if (inputBlock.GetInputAreaCenterItemCount(inputItemId) < inputCount)
            {
                continue;
            }

            if (!TryResolveOutputBlock(outputItemId, outputCount, out _))
            {
                continue;
            }

            if (!TryEnsureCraftStartEnergy(installedDefinition))
            {
                continue;
            }

            if (inputBlock.ConsumeInputAreaCenterObjectsAnimated(
                    inputItemId,
                    inputCount,
                    ResolveConsumeTargetWorldPosition(),
                    inputConsumeMoveInterval) != inputCount)
            {
                continue;
            }

            BeginActiveCraft(recipeIndex, outputItemId, outputCount, installedDefinition);
            return;
        }
    }

    private bool TryResolveRuntimeInputItemArea(int recipeIndex, int inputItemId, out RuntimeInputItemArea inputArea)
    {
        inputArea = default;
        if (inputItemId < 0 || runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return false;
        }

        if (recipeIndex >= 0 && recipeIndex < runtimeInputItemAreas.Count)
        {
            RuntimeInputItemArea indexedArea = runtimeInputItemAreas[recipeIndex];
            if (indexedArea.itemId == inputItemId)
            {
                inputArea = indexedArea;
                return true;
            }
        }

        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            RuntimeInputItemArea candidateArea = runtimeInputItemAreas[i];
            if (candidateArea.itemId != inputItemId)
            {
                continue;
            }

            inputArea = candidateArea;
            return true;
        }

        return false;
    }

    protected virtual bool TryCompleteActiveCraft()
    {
        if (!hasActiveCraft || activeOutputItemId < 0 || activeOutputCount <= 0)
        {
            ClearActiveCraft();
            return false;
        }

        Vector3 startWorldPosition = ResolveConsumeTargetWorldPosition();
        if (!TryEmitOutputItems(activeOutputItemId, activeOutputCount, startWorldPosition))
        {
            return false;
        }

        ClearActiveCraft();
        return true;
    }

    protected bool TryEmitOutputItems(int outputItemId, int outputCount, Vector3 startWorldPosition)
    {
        if (!TryResolveOutputBlock(outputItemId, outputCount, out Block outputBlock) || outputBlock == null)
        {
            return false;
        }

        return TryEmitOutputItemsToBlock(outputBlock, outputItemId, outputCount, startWorldPosition);
    }

    protected bool TryEmitOutputItemsToBlock(Block outputBlock, int outputItemId, int outputCount, Vector3 startWorldPosition)
    {
        if (outputBlock == null || outputItemId < 0 || outputCount <= 0)
        {
            return false;
        }

        for (int outputIndex = 0; outputIndex < outputCount; outputIndex++)
        {
            if (!outputBlock.TryAddInputAreaCenterObjectAnimated(
                    outputItemId,
                    startWorldPosition,
                    outputIndex * Mathf.Max(0f, outputMoveInterval),
                    out PortableObject droppedObject))
            {
                return false;
            }

            DroppedItemPickupGate gate = droppedObject != null ? droppedObject.GetComponent<DroppedItemPickupGate>() : null;
            gate?.SetAutoPickupBlocked(true);
        }

        return true;
    }

    private bool TryConsumeOperatingEnergy(float deltaTime, out float consumedEnergy)
    {
        consumedEnergy = 0f;
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        float remainingEnergyCost = Mathf.Max(0f, installedDefinition.useEnergyAmount) * Mathf.Max(0f, deltaTime);
        if (remainingEnergyCost <= 0.0001f)
        {
            return false;
        }

        while (remainingEnergyCost > 0.0001f)
        {
            if (storedEnergy <= 0.0001f && !TryRefillEnergyStore(installedDefinition))
            {
                break;
            }

            float spentEnergy = Mathf.Min(storedEnergy, remainingEnergyCost);
            if (spentEnergy <= 0.0001f)
            {
                break;
            }

            storedEnergy = Mathf.Max(0f, storedEnergy - spentEnergy);
            remainingEnergyCost -= spentEnergy;
            consumedEnergy += spentEnergy;
        }

        if (storedEnergy <= 0f)
        {
            energyGaugeCapacity = 0f;
        }
        return consumedEnergy > 0.0001f;
    }

    protected bool TryEnsureCraftStartEnergy(ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        if (storedEnergy > 0f)
        {
            return true;
        }

        return TryRefillEnergyStore(installedDefinition);
    }

    private bool TryRefillEnergyStore(ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        float minimumOperationalEnergy = Mathf.Max(1, installedDefinition.useEnergyAmount);
        bool consumedAnyEnergyItem = false;
        while (storedEnergy < minimumOperationalEnergy)
        {
            if (!TryConsumeOneEnergyItem(installedDefinition.useEnergyType, out int gainedEnergy))
            {
                break;
            }

            storedEnergy += gainedEnergy;
            consumedAnyEnergyItem = true;
        }

        if (consumedAnyEnergyItem)
        {
            energyGaugeCapacity = Mathf.Max(storedEnergy, 1f);
        }

        return storedEnergy >= minimumOperationalEnergy;
    }

    private bool TryConsumeOneEnergyItem(ItemDefinition.EnergyType requiredEnergyType, out int gainedEnergy)
    {
        gainedEnergy = 0;
        if (requiredEnergyType == ItemDefinition.EnergyType.None || runtimeInputEnergyCoordinates.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < runtimeInputEnergyCoordinates.Count; i++)
        {
            if (!TryGetLoadedBlock(runtimeInputEnergyCoordinates[i], out Block block) || block == null)
            {
                continue;
            }

            int energyItemId = block.GetInputAreaCenterItemId();
            if (energyItemId < 0)
            {
                continue;
            }

            ItemDefinition energyDefinition = ResolveItemDefinition(energyItemId);
            if (energyDefinition == null
                || energyDefinition.energyType != requiredEnergyType
                || energyDefinition.energyAmount <= 0)
            {
                continue;
            }

            if (!block.TryConsumeOneInputAreaCenterObjectAnimated(
                    energyItemId,
                    ResolveConsumeTargetWorldPosition(),
                    out int consumedItemId) || consumedItemId != energyItemId)
            {
                continue;
            }

            gainedEnergy = energyDefinition.energyAmount;
            return true;
        }

        return false;
    }

    protected bool TryResolveOutputBlock(int outputItemId, int outputCount, out Block targetBlock)
    {
        targetBlock = null;
        if (outputItemId < 0 || outputCount <= 0 || runtimeOutputCoordinates.Count <= 0)
        {
            return false;
        }

        if (GetRuntimeAreaObjectCount(runtimeOutputCoordinates) + outputCount > ResolveRuntimeAreaCapacity(runtimeOutputCoordinates))
        {
            return false;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireExistingCenterStack = pass == 0;
            for (int i = 0; i < runtimeOutputCoordinates.Count; i++)
            {
                if (!TryGetLoadedBlock(runtimeOutputCoordinates[i], out Block block) || block == null)
                {
                    continue;
                }

                if (block.Type != Block.BlockType.Ground || !block.CanAddInputAreaCenterObjects(outputCount, outputItemId))
                {
                    continue;
                }

                if (requireExistingCenterStack && !block.HasInputAreaCenterItem(outputItemId))
                {
                    continue;
                }

                targetBlock = block;
                return true;
            }
        }

        return false;
    }

    private int GetRuntimeAreaObjectCount(IReadOnlyList<Vector2Int> coordinates, int itemId = -1)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return 0;
        }

        int count = 0;
        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            if (!TryGetLoadedBlock(coordinate, out Block block) || block == null || block.Type != Block.BlockType.Ground)
            {
                continue;
            }

            count += block.GetInputAreaCenterItemCount(itemId);
        }

        return count;
    }

    private int GetRuntimeAreaTopItemId(IReadOnlyList<Vector2Int> coordinates)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return -1;
        }

        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            if (!TryGetLoadedBlock(coordinate, out Block block) || block == null || block.Type != Block.BlockType.Ground)
            {
                continue;
            }

            int itemId = block.GetInputAreaCenterItemId();
            if (itemId >= 0)
            {
                return itemId;
            }
        }

        return -1;
    }

    private bool TryFindOutputSourceBlock(int itemId, out Block sourceBlock, out Vector3 startWorldPosition)
    {
        sourceBlock = null;
        startWorldPosition = transform.position;
        if (itemId < 0 || runtimeOutputCoordinates.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < runtimeOutputCoordinates.Count; i++)
        {
            if (!TryGetLoadedBlock(runtimeOutputCoordinates[i], out Block block) || block == null)
            {
                continue;
            }

            if (block.Type != Block.BlockType.Ground || !block.HasInputAreaCenterItem(itemId))
            {
                continue;
            }

            if (!block.TryGetInputAreaCenterTopWorldPosition(itemId, out startWorldPosition))
            {
                startWorldPosition = block.transform.position;
            }

            sourceBlock = block;
            return true;
        }

        return false;
    }

    private bool TryGetRecipePair(int recipeIndex, out int inputItemId, out int inputCount, out int outputItemId, out int outputCount)
    {
        inputItemId = -1;
        inputCount = 0;
        outputItemId = -1;
        outputCount = 0;

        if (recipeIndex < 0 || recipeIndex >= inputList.Count || recipeIndex >= outputList.Count)
        {
            return false;
        }

        ItemIoEntry inputEntry = inputList[recipeIndex];
        ItemIoEntry outputEntry = outputList[recipeIndex];
        inputItemId = inputEntry.itemDefinition != null ? inputEntry.itemDefinition.id : -1;
        outputItemId = outputEntry.itemDefinition != null ? outputEntry.itemDefinition.id : -1;
        inputCount = Mathf.Max(1, inputEntry.count);
        outputCount = Mathf.Max(1, outputEntry.count);
        return inputItemId >= 0 && outputItemId >= 0;
    }

    protected bool TryGetLoadedBlock(Vector2Int coordinate, out Block block)
    {
        block = null;
        TerrainGenerator terrain = ResolveTerrain();
        return terrain != null && terrain.TryGetLoadedBlock(coordinate, out block);
    }

    private TerrainGenerator ResolveTerrain()
    {
        if (cachedTerrain != null)
        {
            return cachedTerrain;
        }

        cachedTerrain = GetComponentInParent<TerrainGenerator>();
        if (cachedTerrain == null)
        {
            cachedTerrain = TerrainGenerator.ResolveActive();
        }

        return cachedTerrain;
    }

    protected ItemDefinition ResolveInstalledDefinition()
    {
        int itemId = ResolveItemId();
        if (cachedInstalledDefinition != null && cachedInstalledDefinitionId == itemId)
        {
            return cachedInstalledDefinition;
        }

        cachedInstalledDefinition = ResolveItemDefinition(itemId);
        cachedInstalledDefinitionId = itemId;
        return cachedInstalledDefinition;
    }

    private static ItemDefinition ResolveItemDefinition(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    protected static bool RequiresOperationalEnergy(ItemDefinition installedDefinition)
    {
        return installedDefinition != null
               && installedDefinition.useEnergyType != ItemDefinition.EnergyType.None
               && installedDefinition.useEnergyAmount > 0;
    }

    protected bool HasOperationalEnergyAvailable(ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        return storedEnergy > 0f || HasUsableEnergyItem(installedDefinition.useEnergyType);
    }

    private bool HasUsableEnergyItem(ItemDefinition.EnergyType requiredEnergyType)
    {
        if (requiredEnergyType == ItemDefinition.EnergyType.None || runtimeInputEnergyCoordinates == null)
        {
            return false;
        }

        for (int i = 0; i < runtimeInputEnergyCoordinates.Count; i++)
        {
            if (!TryGetLoadedBlock(runtimeInputEnergyCoordinates[i], out Block block) || block == null)
            {
                continue;
            }

            int energyItemId = block.GetInputAreaCenterItemId();
            if (energyItemId < 0)
            {
                continue;
            }

            ItemDefinition energyDefinition = ResolveItemDefinition(energyItemId);
            if (energyDefinition != null
                && energyDefinition.energyType == requiredEnergyType
                && energyDefinition.energyAmount > 0)
            {
                return true;
            }
        }

        return false;
    }

    private float ResolveCompleteEnergy(ItemDefinition installedDefinition)
    {
        return ResolveCompleteEnergy(installedDefinition, CraftDurationSeconds);
    }

    public static float ResolveCompleteEnergy(ItemDefinition installedDefinition, float fallbackCraftDuration)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return 0f;
        }

        float configuredCompleteEnergy = Mathf.Max(0f, installedDefinition.completeEnergy);
        if (configuredCompleteEnergy > 0.0001f)
        {
            return configuredCompleteEnergy;
        }

        return Mathf.Max(0.1f, fallbackCraftDuration) * Mathf.Max(0f, installedDefinition.useEnergyAmount);
    }

    protected float ResolveInitialCraftDuration(ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return CraftDurationSeconds;
        }

        float energyRate = Mathf.Max(0.0001f, installedDefinition.useEnergyAmount);
        return Mathf.Max(0.1f, ResolveCompleteEnergy(installedDefinition) / energyRate);
    }

    private float ResolveRemainingEnergyCraftTime(ItemDefinition installedDefinition, float consumedEnergy)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return Mathf.Max(0f, remainingCraftTime);
        }

        float energyRate = Mathf.Max(0.0001f, installedDefinition.useEnergyAmount);
        float remainingEnergy = Mathf.Max(0f, ResolveCompleteEnergy(installedDefinition) - Mathf.Max(0f, consumedEnergy));
        return remainingEnergy / energyRate;
    }

    private float ResolveConsumedEnergyFromRemainingTime(ItemDefinition installedDefinition, float savedRemainingCraftTime)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return 0f;
        }

        float energyRate = Mathf.Max(0.0001f, installedDefinition.useEnergyAmount);
        float completeEnergy = ResolveCompleteEnergy(installedDefinition);
        float totalDuration = completeEnergy / energyRate;
        float elapsedDuration = Mathf.Clamp(totalDuration - Mathf.Max(0f, savedRemainingCraftTime), 0f, totalDuration);
        return Mathf.Min(completeEnergy, elapsedDuration * energyRate);
    }

    protected virtual Vector3 ResolveConsumeTargetWorldPosition()
    {
        if (portableObj != null)
        {
            return portableObj.transform.position;
        }

        return transform.position;
    }

    private void UpdateEnergyGaugeVisual()
    {
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!hasActiveCraft)
        {
            ReleaseEnergyGaugeVisual();
            return;
        }

        if (!ShouldShowGaugeByAreaMarkerVisibility())
        {
            ReleaseEnergyGaugeVisual();
            return;
        }

        bool requiresOperationalEnergy = RequiresOperationalEnergy(installedDefinition);
        UIManager uiManager = UIManager.Instance;
        if (uiManager == null)
        {
            return;
        }

        if (requiresOperationalEnergy)
        {
            if (activeEnergyGauge == null)
            {
                activeEnergyGauge = uiManager.AcquireEnergyGauge();
                if (activeEnergyGauge == null)
                {
                    return;
                }
            }
        }
        else
        {
            ReleaseGaugeVisual(ref activeEnergyGauge);
        }

        if (activeCraftProgressGauge == null)
        {
            activeCraftProgressGauge = uiManager.AcquireEnergyGauge();
            if (activeCraftProgressGauge == null)
            {
                return;
            }
        }

        activeCraftProgressGauge.SetFillColor(craftProgressGaugeFillColor);
        Vector3 gaugeWorldPosition = ResolveEnergyGaugeWorldPosition();
        if (requiresOperationalEnergy)
        {
            activeEnergyGauge.SetFillColor(energyGaugeFillColor);
            uiManager.UpdateEnergyGauge(
                activeEnergyGauge,
                gaugeWorldPosition,
                ResolveEnergyGaugeFillAmount(installedDefinition));
        }

        uiManager.UpdateEnergyGauge(
            activeCraftProgressGauge,
            gaugeWorldPosition,
            ResolveCraftProgressGaugeFillAmount(),
            requiresOperationalEnergy
                ? new Vector2(0f, -Mathf.Max(0f, craftProgressGaugeCanvasVerticalOffset))
                : Vector2.zero);
    }

    private bool ShouldShowGaugeByAreaMarkerVisibility()
    {
        InputOutputModuleAreaMarkerController markerController = GetComponent<InputOutputModuleAreaMarkerController>();
        return markerController == null || markerController.ShouldShowLinkedUi();
    }

    private void ReleaseEnergyGaugeVisual()
    {
        if (activeEnergyGauge == null && activeCraftProgressGauge == null)
        {
            return;
        }

        ReleaseGaugeVisual(ref activeEnergyGauge);
        ReleaseGaugeVisual(ref activeCraftProgressGauge);
    }

    private void ReleaseGaugeVisual(ref DefaultGauge gauge)
    {
        if (gauge == null)
        {
            return;
        }

        UIManager uiManager = UIManager.Instance;
        if (uiManager != null)
        {
            uiManager.ReleaseEnergyGauge(gauge);
        }
        else
        {
            Destroy(gauge.gameObject);
        }

        gauge = null;
    }

    private float ResolveEnergyGaugeFillAmount(ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return 0f;
        }

        if (storedEnergy > energyGaugeCapacity)
        {
            energyGaugeCapacity = storedEnergy;
        }

        float gaugeCapacity = Mathf.Max(energyGaugeCapacity, 1f);
        return Mathf.Clamp01(storedEnergy / gaugeCapacity);
    }

    private float ResolveCraftProgressGaugeFillAmount()
    {
        if (!hasActiveCraft)
        {
            return 0f;
        }

        if (waitingForOutput)
        {
            return 1f;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (RequiresOperationalEnergy(installedDefinition))
        {
            float completeEnergy = ResolveCompleteEnergy(installedDefinition);
            return completeEnergy > 0.0001f
                ? Mathf.Clamp01(Mathf.Max(0f, activeCraftConsumedEnergy) / completeEnergy)
                : 0f;
        }

        float duration = Mathf.Max(0.1f, craftDuration);
        return Mathf.Clamp01(1f - (Mathf.Max(0f, remainingCraftTime) / duration));
    }

    private float ResolveObjectInfoCurrentUseEnergy()
    {
        if (!hasActiveCraft)
        {
            return 0f;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (RequiresOperationalEnergy(installedDefinition))
        {
            float completeEnergy = ResolveCompleteEnergy(installedDefinition);
            return waitingForOutput
                ? completeEnergy
                : Mathf.Clamp(Mathf.Max(0f, activeCraftConsumedEnergy), 0f, completeEnergy);
        }

        float duration = Mathf.Max(0.1f, craftDuration);
        return waitingForOutput
            ? duration
            : Mathf.Clamp(duration - Mathf.Max(0f, remainingCraftTime), 0f, duration);
    }

    private float ResolveObjectInfoCompleteEnergy()
    {
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (RequiresOperationalEnergy(installedDefinition))
        {
            return ResolveCompleteEnergy(installedDefinition);
        }

        return hasActiveCraft || waitingForOutput
            ? Mathf.Max(0.1f, craftDuration)
            : 0f;
    }

    private Vector3 ResolveEnergyGaugeWorldPosition()
    {
        Bounds bounds = default;
        bool hasBounds = false;
        IReadOnlyList<Renderer> renderers = ResolveEnergyGaugeRenderers();
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            return transform.position + Vector3.up * (1f + energyGaugeVerticalOffset);
        }

        return new Vector3(bounds.center.x, bounds.max.y + energyGaugeVerticalOffset, bounds.center.z);
    }

    private IReadOnlyList<Renderer> ResolveEnergyGaugeRenderers()
    {
        bool requiresRefresh = !energyGaugeRenderersResolved || cachedEnergyGaugeRenderers.Count == 0;
        if (!requiresRefresh)
        {
            for (int i = 0; i < cachedEnergyGaugeRenderers.Count; i++)
            {
                if (cachedEnergyGaugeRenderers[i] == null)
                {
                    requiresRefresh = true;
                    break;
                }
            }
        }

        if (!requiresRefresh)
        {
            return cachedEnergyGaugeRenderers;
        }

        energyGaugeRenderersResolved = true;
        cachedEnergyGaugeRenderers.Clear();
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null)
            {
                cachedEnergyGaugeRenderers.Add(renderer);
            }
        }

        return cachedEnergyGaugeRenderers;
    }

    protected void BeginActiveCraft(int recipeIndex, int outputItemId, int outputCount, ItemDefinition installedDefinition)
    {
        if (outputItemId < 0 || outputCount <= 0)
        {
            ClearActiveCraft();
            return;
        }

        hasActiveCraft = true;
        waitingForOutput = false;
        remainingCraftTime = ResolveInitialCraftDuration(installedDefinition);
        activeCraftConsumedEnergy = 0f;
        activeRecipeIndex = recipeIndex;
        activeOutputItemId = outputItemId;
        activeOutputCount = outputCount;
    }

    protected int ActiveOutputItemId => activeOutputItemId;
    protected int ActiveOutputCount => activeOutputCount;
    protected bool IsActiveCraftRunning => hasActiveCraft;
    protected bool IsWaitingForOutput => waitingForOutput;

    protected virtual bool IsRecipeOutputAllowedByItemFilter(int outputItemId)
    {
        return true;
    }

    protected virtual bool ShouldShowObjectInfoEmptyRecipeLine(int outputItemId)
    {
        return false;
    }

    protected virtual bool ShouldShowObjectInfoEmptyInputOutputSlots()
    {
        return runtimeInputItemAreas != null && runtimeInputItemAreas.Count > 0;
    }

    protected void ClearActiveCraft()
    {
        hasActiveCraft = false;
        waitingForOutput = false;
        remainingCraftTime = 0f;
        activeCraftConsumedEnergy = 0f;
        activeRecipeIndex = -1;
        activeOutputItemId = -1;
        activeOutputCount = 0;
        if (storedEnergy <= 0f)
        {
            energyGaugeCapacity = 0f;
        }
    }

    private bool ContainsRuntimeInputItemArea(Vector2Int coordinate, int itemId)
    {
        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            RuntimeInputItemArea area = runtimeInputItemAreas[i];
            if (area.coordinate == coordinate && area.itemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    private bool ContainsRuntimeAreaCoordinate(Vector2Int coordinate)
    {
        return ContainsRuntimeInputItemAreaCoordinate(coordinate)
            || ContainsRuntimeInputEnergyCoordinate(coordinate)
            || ContainsRuntimeOutputCoordinate(coordinate);
    }

    private bool ContainsRuntimeInputItemAreaCoordinate(Vector2Int coordinate)
    {
        if (runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            if (runtimeInputItemAreas[i].coordinate == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    private bool ContainsRuntimeInputEnergyCoordinate(Vector2Int coordinate)
    {
        return ContainsCoordinate(runtimeInputEnergyCoordinates, coordinate);
    }

    private static void AddUniqueCoordinates(IReadOnlyList<Vector2Int> source, List<Vector2Int> target)
    {
        if (source == null || target == null)
        {
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            Vector2Int coordinate = source[i];
            if (!target.Contains(coordinate))
            {
                target.Add(coordinate);
            }
        }
    }

    private static bool ContainsCoordinate(IReadOnlyList<Vector2Int> coordinates, Vector2Int coordinate)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            if (coordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterRuntimeGridCoordinates()
    {
        if (runtimeGridCoordinates == null || runtimeGridCoordinates.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < runtimeGridCoordinates.Count; i++)
        {
            Vector2Int coordinate = runtimeGridCoordinates[i];
            if (!registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
                || modules == null)
            {
                modules = new HashSet<InputOutputModule>();
                registeredRuntimeGridCoordinates[coordinate] = modules;
            }

            modules.Add(this);
        }
    }

    private void UnregisterRuntimeGridCoordinates()
    {
        if (runtimeGridCoordinates == null || runtimeGridCoordinates.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < runtimeGridCoordinates.Count; i++)
        {
            Vector2Int coordinate = runtimeGridCoordinates[i];
            if (!registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
                || modules == null)
            {
                continue;
            }

            modules.Remove(this);
            if (modules.Count <= 0)
            {
                registeredRuntimeGridCoordinates.Remove(coordinate);
            }
        }
    }
#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        EnsurePairData();
        EnsureRectGridData();
        EnsureRectGridPlacementData();
    }
#endif
}
