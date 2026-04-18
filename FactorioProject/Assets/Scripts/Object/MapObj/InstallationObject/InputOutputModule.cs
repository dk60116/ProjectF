using System.Collections.Generic;
using UnityEngine;

public class InputOutputModule : InstallationObject
{
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeGridCoordinates
        = new Dictionary<Vector2Int, HashSet<InputOutputModule>>();

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
    private int activeRecipeIndex = -1;
    [SerializeField]
    private int activeOutputItemId = -1;
    [SerializeField]
    private int activeOutputCount;

    private TerrainGenerator cachedTerrain;
    private ItemDefinition cachedInstalledDefinition;
    private int cachedInstalledDefinitionId = int.MinValue;
    private DefaultGauge activeEnergyGauge;
    private readonly List<Renderer> cachedEnergyGaugeRenderers = new List<Renderer>();
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
        activeRecipeIndex = state.activeRecipeIndex;
        activeOutputItemId = state.activeOutputItemId;
        activeOutputCount = Mathf.Max(0, state.activeOutputCount);
        cachedTerrain = null;
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

    public static bool TryGetOutputItemIdsAtRuntimeGridCoordinate(Vector2Int coordinate, ISet<int> outputItemIds)
    {
        if (outputItemIds == null)
        {
            return false;
        }

        if (!registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            || modules == null
            || modules.Count <= 0)
        {
            return false;
        }

        bool foundAny = false;
        foreach (InputOutputModule candidate in modules)
        {
            if (candidate == null
                || !candidate.gameObject.activeInHierarchy
                || !candidate.ContainsRuntimeOutputCoordinate(coordinate))
            {
                continue;
            }

            foundAny |= candidate.AppendOutputItemIds(outputItemIds);
        }

        return foundAny;
    }

    public static bool RuntimeOutputCoordinateProducesItemId(Vector2Int coordinate, int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

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
                || !candidate.ContainsRuntimeOutputCoordinate(coordinate))
            {
                continue;
            }

            if (candidate.HasOutputItemId(itemId))
            {
                return true;
            }
        }

        return false;
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsurePairData();
        if (hasActiveCraft)
        {
            UpdateActiveCraft();
        }
        else
        {
            TryStartNextCraft();
        }

        UpdateEnergyGaugeVisual();
    }

    private void OnEnable()
    {
        RegisterRuntimeGridCoordinates();
    }

    private void OnDisable()
    {
        UnregisterRuntimeGridCoordinates();
        ReleaseEnergyGaugeVisual();
    }

    private void OnDestroy()
    {
        UnregisterRuntimeGridCoordinates();
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
        if (runtimeOutputCoordinates == null || runtimeOutputCoordinates.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < runtimeOutputCoordinates.Count; i++)
        {
            if (runtimeOutputCoordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    private bool AppendOutputItemIds(ISet<int> outputItemIds)
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

    private void UpdateActiveCraft()
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

        if (!TryConsumeOperatingEnergy(Time.deltaTime))
        {
            return;
        }

        remainingCraftTime = Mathf.Max(0f, remainingCraftTime - Time.deltaTime);
        if (remainingCraftTime > 0f)
        {
            return;
        }

        waitingForOutput = true;
        TryCompleteActiveCraft();
    }

    private void TryStartNextCraft()
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

            hasActiveCraft = true;
            waitingForOutput = false;
            remainingCraftTime = Mathf.Max(0.1f, craftDuration);
            activeRecipeIndex = recipeIndex;
            activeOutputItemId = outputItemId;
            activeOutputCount = outputCount;
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

    private bool TryCompleteActiveCraft()
    {
        if (!hasActiveCraft || activeOutputItemId < 0 || activeOutputCount <= 0)
        {
            ClearActiveCraft();
            return false;
        }

        if (!TryResolveOutputBlock(activeOutputItemId, activeOutputCount, out Block outputBlock) || outputBlock == null)
        {
            return false;
        }

        Vector3 startWorldPosition = ResolveConsumeTargetWorldPosition();
        for (int outputIndex = 0; outputIndex < activeOutputCount; outputIndex++)
        {
            if (!outputBlock.TryAddInputAreaCenterObjectAnimated(
                    activeOutputItemId,
                    startWorldPosition,
                    outputIndex * Mathf.Max(0f, outputMoveInterval),
                    out PortableObject droppedObject))
            {
                return false;
            }

            DroppedItemPickupGate gate = droppedObject != null ? droppedObject.GetComponent<DroppedItemPickupGate>() : null;
            gate?.SetAutoPickupBlocked(true);
        }

        ClearActiveCraft();
        return true;
    }

    private bool TryConsumeOperatingEnergy(float deltaTime)
    {
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        if (storedEnergy <= 0f && !TryRefillEnergyStore(installedDefinition))
        {
            return false;
        }

        float energyCost = Mathf.Max(0f, installedDefinition.useEnergyAmount) * Mathf.Max(0f, deltaTime);
        storedEnergy = Mathf.Max(0f, storedEnergy - energyCost);
        if (storedEnergy <= 0f)
        {
            energyGaugeCapacity = 0f;
        }
        return true;
    }

    private bool TryEnsureCraftStartEnergy(ItemDefinition installedDefinition)
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

    private bool TryResolveOutputBlock(int outputItemId, int outputCount, out Block targetBlock)
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

    private bool TryGetLoadedBlock(Vector2Int coordinate, out Block block)
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
            cachedTerrain = Object.FindObjectOfType<TerrainGenerator>();
        }

        return cachedTerrain;
    }

    private ItemDefinition ResolveInstalledDefinition()
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

    private static bool RequiresOperationalEnergy(ItemDefinition installedDefinition)
    {
        return installedDefinition != null
               && installedDefinition.useEnergyType != ItemDefinition.EnergyType.None
               && installedDefinition.useEnergyAmount > 0;
    }

    private Vector3 ResolveConsumeTargetWorldPosition()
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
        if (!RequiresOperationalEnergy(installedDefinition) || !hasActiveCraft)
        {
            ReleaseEnergyGaugeVisual();
            return;
        }

        UIManager uiManager = UIManager.Instance;
        if (uiManager == null)
        {
            return;
        }

        if (activeEnergyGauge == null)
        {
            activeEnergyGauge = uiManager.AcquireEnergyGauge();
            if (activeEnergyGauge == null)
            {
                return;
            }
        }

        uiManager.UpdateEnergyGauge(
            activeEnergyGauge,
            ResolveEnergyGaugeWorldPosition(),
            ResolveEnergyGaugeFillAmount(installedDefinition));
    }

    private void ReleaseEnergyGaugeVisual()
    {
        if (activeEnergyGauge == null)
        {
            return;
        }

        UIManager uiManager = UIManager.Instance;
        if (uiManager != null)
        {
            uiManager.ReleaseEnergyGauge(activeEnergyGauge);
        }
        else
        {
            Destroy(activeEnergyGauge.gameObject);
        }

        activeEnergyGauge = null;
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

    private void ClearActiveCraft()
    {
        hasActiveCraft = false;
        waitingForOutput = false;
        remainingCraftTime = 0f;
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
    private void OnValidate()
    {
        EnsurePairData();
        EnsureRectGridData();
        EnsureRectGridPlacementData();
    }
#endif
}
