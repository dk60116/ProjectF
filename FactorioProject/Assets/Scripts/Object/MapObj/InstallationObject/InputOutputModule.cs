using System.Collections.Generic;
using UnityEngine;

public class InputOutputModule : InstallationObject,
    IMapObjectUpdateTick,
    IMapObjectUpdateTickInterval,
    IItemLightWorkStateProvider
{
    public static event System.Action<InputOutputModule> RuntimePipeTopologyChanged;

    private const float DefaultManagedUpdateTickIntervalSeconds = 0.1f;
    private static readonly int WorkAnimatorBoolHash = Animator.StringToHash("bWork");
    private static readonly Vector2Int[] FluidCardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeGridCoordinates
        = new Dictionary<Vector2Int, HashSet<InputOutputModule>>();
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeAreaCoordinates
        = new Dictionary<Vector2Int, HashSet<InputOutputModule>>();
    private static readonly HashSet<InputOutputModule> activeRuntimeModules
        = new HashSet<InputOutputModule>();
    private static readonly List<InputOutputModule> runtimeWakeScratch
        = new List<InputOutputModule>(16);
    private static int fluidTopologyVersion = 1;

    private delegate bool RuntimeCoordinateValueCollector<T>(
        InputOutputModule module,
        Vector2Int coordinate,
        ISet<T> values);

    static InputOutputModule()
    {
        InstallationObject.PlacementRuntimeChanged += HandleInstallationPlacementRuntimeChanged;
        InstallationObject.PlacementRuntimeCleared += HandleInstallationPlacementRuntimeCleared;
    }

    public enum SlotLayoutType
    {
        None = 0,
        RectGrid = 1
    }

    public virtual float ManagedUpdateTickIntervalSeconds => DefaultManagedUpdateTickIntervalSeconds;

    public enum RectGridBlockType
    {
        None = 0,
        Object = 1,
        InputEnergy = 2,
        InputItem = 3,
        Output = 4,
        PipeInputEnergy = 5,
        PipeInputItem = 6,
        PipeOutputItem = 7,
        DoubleEnergy = 8,
        DoubleInputItem = 9,
        DoublePipeOutputItem = 10,
        PipeInput = 11
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

    protected readonly struct RuntimeAreaOutputTarget
    {
        public readonly Block block;
        public readonly Vector2Int coordinate;
        public readonly bool useSavedCenterStack;

        public RuntimeAreaOutputTarget(Block block, Vector2Int coordinate, bool useSavedCenterStack)
        {
            this.block = block;
            this.coordinate = coordinate;
            this.useSavedCenterStack = useSavedCenterStack;
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
        public List<Vector2Int> pipeInputCoordinates = new List<Vector2Int>();
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
        public float boilerWaterTemperatureCelsius;
        public float boilerSteamLiterAccumulator;
        public float oilDrillingProgressLiters;
        public float sprinklerSprayElapsedSeconds;
        public float seedPlanterPlantElapsedSeconds;
        public bool steamGeneratorHasGenerationReserve;

        public PersistentState Clone()
        {
            return new PersistentState
            {
                inputEnergyCoordinates = new List<Vector2Int>(inputEnergyCoordinates ?? new List<Vector2Int>()),
                inputItemAreas = new List<PersistentInputItemAreaState>(inputItemAreas ?? new List<PersistentInputItemAreaState>()),
                outputCoordinates = new List<Vector2Int>(outputCoordinates ?? new List<Vector2Int>()),
                pipeInputCoordinates = new List<Vector2Int>(pipeInputCoordinates ?? new List<Vector2Int>()),
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
                activeOutputCount = activeOutputCount,
                boilerWaterTemperatureCelsius = boilerWaterTemperatureCelsius,
                boilerSteamLiterAccumulator = boilerSteamLiterAccumulator,
                oilDrillingProgressLiters = oilDrillingProgressLiters,
                sprinklerSprayElapsedSeconds = sprinklerSprayElapsedSeconds,
                seedPlanterPlantElapsedSeconds = seedPlanterPlantElapsedSeconds,
                steamGeneratorHasGenerationReserve = steamGeneratorHasGenerationReserve
            };
        }
    }

    [SerializeField]
    private ItemDefinition parentInputOutputModuleItem;
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
    [System.NonSerialized]
    private bool rectGridDataInitialized;
    [SerializeField]
    private List<RectGridBlockPlacement> rectGridPlacements = new List<RectGridBlockPlacement>();
    [System.NonSerialized]
    private bool rectGridPlacementDataInitialized;
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
    [SerializeField]
    private bool playParticleEffectWhileCrafting;
    [SerializeField, Min(1)]
    private int runtimeAreaMaxObjects = 10;
    [SerializeField]
    private List<Vector2Int> runtimeInputEnergyCoordinates = new List<Vector2Int>();
    [SerializeField]
    private List<RuntimeInputItemArea> runtimeInputItemAreas = new List<RuntimeInputItemArea>();
    [SerializeField]
    private List<Vector2Int> runtimeOutputCoordinates = new List<Vector2Int>();
    [SerializeField]
    private List<Vector2Int> runtimePipeInputCoordinates = new List<Vector2Int>();
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
    private BlockStateStore cachedBlockStateStore;
    private ItemDefinition cachedInstalledDefinition;
    private int cachedInstalledDefinitionId = int.MinValue;
    private DefaultGauge activeEnergyGauge;
    private DefaultGauge activeCraftProgressGauge;
    private readonly List<Renderer> cachedEnergyGaugeRenderers = new List<Renderer>();
    private readonly List<Vector2Int> objectInfoInputAreaCoordinates = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> singleItemOutputVisitedCoordinates = new HashSet<Vector2Int>();
    private readonly Queue<Vector2Int> connectedFluidSearchQueue = new Queue<Vector2Int>(32);
    private readonly HashSet<Vector2Int> connectedFluidSearchVisited = new HashSet<Vector2Int>();
    private readonly HashSet<InstallationObject> connectedFluidStorageCandidates = new HashSet<InstallationObject>();
    private readonly List<Vector2Int> connectedFluidSeedCoordinates = new List<Vector2Int>(8);
    private readonly List<InstallationObject> cachedConnectedFluidSourceStorages = new List<InstallationObject>(8);
    private int cachedConnectedFluidSourceStoragesTopologyVersion;
    private readonly List<InstallationObject> cachedFluidOutputStorages = new List<InstallationObject>(8);
    private int cachedFluidOutputStoragesTopologyVersion;
    private InstallationObject cachedConnectedFluidSource;
    private int cachedConnectedFluidSourceItemId = int.MinValue;
    private int cachedConnectedFluidSourceTopologyVersion;
    private InstallationObject cachedFluidOutputStorage;
    private int cachedFluidOutputItemId = int.MinValue;
    private int cachedFluidOutputTopologyVersion;
    private bool energyGaugeRenderersResolved;
    private bool energyGaugeWorldPositionResolved;
    private Vector3 cachedEnergyGaugeWorldPosition;
    private long cachedEnergyGaugePlacementSequence;
    private Vector3 cachedEnergyGaugeTransformPosition;
    private Quaternion cachedEnergyGaugeTransformRotation;
    private Vector3 cachedEnergyGaugeTransformScale;
    private float lastOperationalEnergySupplyRatio = 1f;
    private Animator cachedWorkAnimator;
    private bool hasCheckedWorkAnimatorParameter;
    private bool workAnimatorHasWorkParameter;
    private bool workAnimatorStateInitialized;
    private bool lastWorkAnimatorState;
    private bool runtimeSleeping;
    private readonly List<ItemIoEntry> effectiveInputList = new List<ItemIoEntry>();
    private readonly List<ItemIoEntry> effectiveOutputList = new List<ItemIoEntry>();
    private bool effectivePairDataInitialized;

    public ItemDefinition ParentInputOutputModuleItem => parentInputOutputModuleItem;

    public IReadOnlyList<ItemIoEntry> LocalInputList
    {
        get
        {
            EnsurePairData();
            return inputList;
        }
    }

    public IReadOnlyList<ItemIoEntry> LocalOutputList
    {
        get
        {
            EnsurePairData();
            return outputList;
        }
    }

    public IReadOnlyList<ItemIoEntry> InputList
    {
        get
        {
            EnsureEffectivePairData();
            return effectiveInputList;
        }
    }

    public IReadOnlyList<ItemIoEntry> OutputList
    {
        get
        {
            EnsureEffectivePairData();
            return effectiveOutputList;
        }
    }

    public ItemIoEntry Output
    {
        get
        {
            EnsureEffectivePairData();
            return effectiveOutputList.Count > 0 ? effectiveOutputList[0] : output;
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
        IReadOnlyList<Vector2Int> outputCoordinates,
        IReadOnlyList<Vector2Int> pipeInputCoordinates)
    {
        UnregisterRuntimeAreaCoordinates();
        runtimeInputEnergyCoordinates.Clear();
        runtimeInputItemAreas.Clear();
        runtimeOutputCoordinates.Clear();
        runtimePipeInputCoordinates.Clear();

        AddUniqueCoordinates(inputEnergyCoordinates, runtimeInputEnergyCoordinates);
        AddUniqueCoordinates(outputCoordinates, runtimeOutputCoordinates);
        AddUniqueCoordinates(pipeInputCoordinates, runtimePipeInputCoordinates);

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

        ExpandRuntimeInputItemAreasForAdditionalItemIds();
        RegisterRuntimeAreaCoordinates();
        cachedTerrain = null;
        cachedBlockStateStore = null;
        WakeRuntimeUpdate();
    }

    public void ConfigureRuntimeGridCoordinates(IReadOnlyList<Vector2Int> coordinates)
    {
        UnregisterRuntimeGridCoordinates();
        runtimeGridCoordinates.Clear();

        AddUniqueCoordinates(coordinates, runtimeGridCoordinates);
        RegisterRuntimeGridCoordinates();
        WakeRuntimeUpdate();
        RuntimePipeTopologyChanged?.Invoke(this);
    }

    public void ConfigureRuntimeFocusCoordinates(IReadOnlyList<Vector2Int> coordinates)
    {
        runtimeFocusCoordinates.Clear();
        AddUniqueCoordinates(coordinates, runtimeFocusCoordinates);
    }

    public virtual PersistentState CapturePersistentState()
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
        AddUniqueCoordinates(runtimePipeInputCoordinates, state.pipeInputCoordinates);
        AddUniqueCoordinates(runtimeGridCoordinates, state.gridCoordinates);
        AddUniqueCoordinates(runtimeFocusCoordinates, state.focusCoordinates);

        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            RuntimeInputItemArea area = runtimeInputItemAreas[i];
            state.inputItemAreas.Add(new PersistentInputItemAreaState(area.coordinate, area.itemId));
        }

        return state;
    }

    public virtual void ApplyPersistentState(PersistentState state)
    {
        if (state == null)
        {
            return;
        }

        UnregisterRuntimeAreaCoordinates();
        runtimeInputEnergyCoordinates.Clear();
        runtimeInputItemAreas.Clear();
        runtimeOutputCoordinates.Clear();
        runtimePipeInputCoordinates.Clear();
        runtimeFocusCoordinates.Clear();

        AddUniqueCoordinates(state.inputEnergyCoordinates, runtimeInputEnergyCoordinates);
        AddUniqueCoordinates(state.outputCoordinates, runtimeOutputCoordinates);
        AddUniqueCoordinates(state.pipeInputCoordinates, runtimePipeInputCoordinates);
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

        ExpandRuntimeInputItemAreasForAdditionalItemIds();
        RegisterRuntimeAreaCoordinates();
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
        cachedBlockStateStore = null;
        WakeRuntimeUpdate();
    }

    public override void PrepareForPool()
    {
        SetRuntimeUpdateTickRegistered(false);
        runtimeSleeping = false;
        UnregisterRuntimeGridCoordinates();
        UnregisterRuntimeAreaCoordinates();
        ReleaseEnergyGaugeVisual();
        runtimeInputEnergyCoordinates.Clear();
        runtimeInputItemAreas.Clear();
        runtimeOutputCoordinates.Clear();
        runtimePipeInputCoordinates.Clear();
        runtimeGridCoordinates.Clear();
        runtimeFocusCoordinates.Clear();
        storedEnergy = 0f;
        energyGaugeCapacity = 0f;
        hasActiveCraft = false;
        waitingForOutput = false;
        remainingCraftTime = 0f;
        activeCraftConsumedEnergy = 0f;
        lastOperationalEnergySupplyRatio = 1f;
        ResetWorkAnimatorStateCache();
        activeRecipeIndex = -1;
        activeOutputItemId = -1;
        activeOutputCount = 0;
        cachedTerrain = null;
        cachedBlockStateStore = null;
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

    public static bool CollectModulesAtRuntimeGridCoordinate(Vector2Int coordinate, List<InputOutputModule> results)
    {
        if (results == null
            || !registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            || modules == null
            || modules.Count <= 0)
        {
            return false;
        }

        bool added = false;
        foreach (InputOutputModule candidate in modules)
        {
            if (candidate == null
                || !candidate.gameObject.activeInHierarchy
                || results.Contains(candidate))
            {
                continue;
            }

            results.Add(candidate);
            added = true;
        }

        return added;
    }

    public static bool TryGetModuleAtRuntimeRectGridBlockType(
        Vector2Int coordinate,
        RectGridBlockType blockType,
        out InputOutputModule module)
    {
        module = null;
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

            if (!candidate.ContainsRuntimeRectGridBlockType(coordinate, blockType))
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
        if (!registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
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

    public static bool CollectModulesAtRuntimeAreaCoordinate(Vector2Int coordinate, List<InputOutputModule> results)
    {
        if (results == null
            || !registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            || modules == null
            || modules.Count <= 0)
        {
            return false;
        }

        bool added = false;
        foreach (InputOutputModule candidate in modules)
        {
            if (candidate == null
                || !candidate.gameObject.activeInHierarchy
                || !candidate.ContainsRuntimeAreaCoordinate(coordinate)
                || results.Contains(candidate))
            {
                continue;
            }

            results.Add(candidate);
            added = true;
        }

        return added;
    }

    public static void WakeRuntimeModulesAtCoordinate(Vector2Int coordinate)
    {
        runtimeWakeScratch.Clear();
        if (registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            && modules != null
            && modules.Count > 0)
        {
            foreach (InputOutputModule module in modules)
            {
                if (module == null
                    || !module.gameObject.activeInHierarchy
                    || !module.ContainsRuntimeAreaCoordinate(coordinate)
                    || runtimeWakeScratch.Contains(module))
                {
                    continue;
                }

                runtimeWakeScratch.Add(module);
            }
        }

        for (int i = 0; i < runtimeWakeScratch.Count; i++)
        {
            runtimeWakeScratch[i]?.WakeRuntimeUpdate();
        }

        runtimeWakeScratch.Clear();
    }

    public static void WakeElectricRuntimeModules()
    {
        runtimeWakeScratch.Clear();
        foreach (InputOutputModule module in activeRuntimeModules)
        {
            if (module == null
                || !module.gameObject.activeInHierarchy
                || !module.RequiresElectricOperationalEnergy())
            {
                continue;
            }

            runtimeWakeScratch.Add(module);
        }

        for (int i = 0; i < runtimeWakeScratch.Count; i++)
        {
            runtimeWakeScratch[i]?.WakeRuntimeUpdate();
        }

        runtimeWakeScratch.Clear();
    }

    private static void HandleInstallationPlacementRuntimeChanged(InstallationObject installationObject)
    {
        InvalidateFluidTopologyCache();
        WakeRuntimeModulesAroundInstallation(installationObject);
    }

    private static void HandleInstallationPlacementRuntimeCleared(InstallationObject installationObject)
    {
        InvalidateFluidTopologyCache();
        WakeRuntimeModulesAroundInstallation(installationObject);
    }

    private static void InvalidateFluidTopologyCache()
    {
        unchecked
        {
            fluidTopologyVersion++;
        }

        if (fluidTopologyVersion <= 0)
        {
            fluidTopologyVersion = 1;
        }
    }

    private static void WakeRuntimeModulesAroundInstallation(InstallationObject installationObject)
    {
        if (installationObject == null)
        {
            return;
        }

        WakeRuntimeModulesAtCoordinates(installationObject.RuntimeOccupiedCoordinates);
        if (installationObject is InputOutputModule module)
        {
            WakeRuntimeModulesAtCoordinates(module.runtimePipeInputCoordinates);
            WakeRuntimeModulesAtCoordinates(module.runtimeOutputCoordinates);
            WakeRuntimeModulesAtCoordinates(module.runtimeGridCoordinates);
        }
    }

    public static bool TryGetRuntimePipeFluidStorageAtCoordinate(
        Vector2Int coordinate,
        InputOutputModule excludedModule,
        out InstallationObject storage)
    {
        return TryGetRuntimePipeFluidStorageAtCoordinate(
            coordinate,
            excludedModule,
            true,
            out storage);
    }

    public static bool TryGetRuntimePipeFluidStorageAtCoordinate(
        Vector2Int coordinate,
        InputOutputModule excludedModule,
        bool requireStorageSpace,
        out InstallationObject storage)
    {
        return TryGetRuntimePipeFluidStorageAtCoordinate(
            coordinate,
            excludedModule,
            requireStorageSpace,
            null,
            out storage);
    }

    public static bool TryGetRuntimePipeFluidStorageAtCoordinate(
        Vector2Int coordinate,
        InputOutputModule excludedModule,
        bool requireStorageSpace,
        System.Predicate<InstallationObject> storageFilter,
        out InstallationObject storage)
    {
        storage = null;
        if (TryGetRuntimePipeFluidStorageAtCoordinate(
                registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> areaModules)
                    ? areaModules
                    : null,
                coordinate,
                excludedModule,
                requireStorageSpace,
                storageFilter,
                null,
                out storage))
        {
            return true;
        }

        return TryGetRuntimePipeFluidStorageAtCoordinate(
            registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> gridModules)
                ? gridModules
                : null,
            coordinate,
            excludedModule,
            requireStorageSpace,
            storageFilter,
            null,
            out storage);
    }

    public static bool TryGetRuntimePipeSourceAtCoordinate(Vector2Int coordinate, out Pump pump)
    {
        pump = null;
        if (TryGetRuntimePipeSourceAtCoordinate(
                registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> areaModules)
                    ? areaModules
                    : null,
                coordinate,
                null,
                out pump))
        {
            return true;
        }

        return TryGetRuntimePipeSourceAtCoordinate(
            registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> gridModules)
                ? gridModules
                : null,
            coordinate,
            null,
            out pump);
    }

    private static bool TryGetRuntimePipeFluidStorageAtCoordinate(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        InputOutputModule excludedModule,
        bool requireStorageSpace,
        System.Predicate<InstallationObject> storageFilter,
        ISet<InputOutputModule> visitedModules,
        out InstallationObject storage)
    {
        storage = null;
        if (modules == null)
        {
            return false;
        }

        foreach (InputOutputModule candidate in modules)
        {
            if (candidate == null
                || candidate == excludedModule
                || !candidate.gameObject.activeInHierarchy
                || !candidate.ContainsRuntimePipeAreaBlockCoordinate(coordinate)
                || !candidate.CanStoreFluid
                || (requireStorageSpace && !candidate.HasFluidStorageSpace)
                || (visitedModules != null && !visitedModules.Add(candidate)))
            {
                continue;
            }

            if (storageFilter != null && !storageFilter(candidate))
            {
                continue;
            }

            storage = candidate;
            return true;
        }

        return false;
    }

    private static bool TryGetRuntimePipeSourceAtCoordinate(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        ISet<InputOutputModule> visitedModules,
        out Pump pump)
    {
        pump = null;
        if (modules == null)
        {
            return false;
        }

        foreach (InputOutputModule candidate in modules)
        {
            if (candidate == null
                || !candidate.gameObject.activeInHierarchy
                || (visitedModules != null && !visitedModules.Add(candidate))
                || !(candidate is Pump candidatePump)
                || !candidate.ContainsRuntimePipeAreaBlockCoordinate(coordinate)
                || !candidate.ContainsRuntimeOutputCoordinate(coordinate))
            {
                continue;
            }

            pump = candidatePump;
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

    public static bool TryGetFluidOutputInfoAtRuntimeGridCoordinate(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;

        HashSet<InputOutputModule> visitedModules = new HashSet<InputOutputModule>();
        if (registeredRuntimeGridCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            && TryGetFluidOutputInfoAtRuntimeGridCoordinate(
                modules,
                coordinate,
                visitedModules,
                out fluidItemId,
                out temperatureCelsius))
        {
            return true;
        }

        return TryGetFluidOutputInfoAtRuntimeGridCoordinate(
            activeRuntimeModules,
            coordinate,
            visitedModules,
            out fluidItemId,
            out temperatureCelsius);
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

    private static bool TryGetFluidOutputInfoAtRuntimeGridCoordinate(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        ISet<InputOutputModule> visitedModules,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        if (modules == null)
        {
            return false;
        }

        HashSet<int> outputItemIds = new HashSet<int>();
        foreach (InputOutputModule module in modules)
        {
            if (module == null
                || !module.gameObject.activeInHierarchy
                || (visitedModules != null && !visitedModules.Add(module))
                || !module.ContainsRuntimeOutputCoordinate(coordinate))
            {
                continue;
            }

            outputItemIds.Clear();
            if (!module.AppendOutputItemIds(outputItemIds))
            {
                continue;
            }

            foreach (int itemId in outputItemIds)
            {
                if (!IsFluidItemId(itemId))
                {
                    continue;
                }

                fluidItemId = itemId;
                temperatureCelsius = module.GetStoredFluidTemperatureCelsius(itemId);
                return true;
            }
        }

        return false;
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
            Block firstCompatibleBlock = null;
            for (int pass = 0; pass < 2; pass++)
            {
                bool requireExistingStack = pass == 0;
                for (int i = 0; i < runtimeInputItemAreas.Count; i++)
                {
                    RuntimeInputItemArea inputArea = runtimeInputItemAreas[i];
                    if (inputArea.itemId != preferredItemId)
                    {
                        continue;
                    }

                    if (!terrainGenerator.TryGetLoadedBlock(inputArea.coordinate, out Block candidateBlock)
                        || candidateBlock == null
                        || !candidateBlock.CanAddInputAreaCenterObjects(1, preferredItemId))
                    {
                        continue;
                    }

                    if (firstCompatibleBlock == null)
                    {
                        firstCompatibleBlock = candidateBlock;
                    }

                    if (!requireExistingStack || candidateBlock.HasInputAreaCenterItem(preferredItemId))
                    {
                        block = candidateBlock;
                        return true;
                    }
                }
            }

            if (firstCompatibleBlock != null)
            {
                block = firstCompatibleBlock;
                return true;
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

    protected bool HasRuntimeInputItemArea(int itemId)
    {
        if (itemId < 0 || runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            if (runtimeInputItemAreas[i].itemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    protected bool TryResolveRuntimeInputItemBlock(
        int itemId,
        int requiredCount,
        ISet<Vector2Int> excludedCoordinates,
        out Block block,
        out Vector2Int coordinate)
    {
        block = null;
        coordinate = default;
        if (itemId < 0
            || requiredCount <= 0
            || runtimeInputItemAreas == null
            || runtimeInputItemAreas.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            RuntimeInputItemArea inputArea = runtimeInputItemAreas[i];
            if (inputArea.itemId != itemId
                || (excludedCoordinates != null && excludedCoordinates.Contains(inputArea.coordinate)))
            {
                continue;
            }

            if (GetRuntimeInputAreaCenterItemCount(inputArea.coordinate, itemId) < requiredCount)
            {
                continue;
            }

            TryGetLoadedBlock(inputArea.coordinate, out Block candidateBlock);
            block = candidateBlock;
            coordinate = inputArea.coordinate;
            return true;
        }

        return false;
    }

    protected virtual bool TryCollectAdditionalRuntimeInputItemIds(ICollection<int> itemIds)
    {
        return false;
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

    protected virtual bool AppendAcceptedRuntimeInputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> inputItemIds)
    {
        if (inputItemIds == null || runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return false;
        }

        int recipeCount = GetEffectiveRecipeCount();
        if (recipeCount <= 0)
        {
            return false;
        }

        bool foundAny = false;
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(recipeIndex, out int inputItemId, out _, out int outputItemId, out _)
                || inputItemId < 0
                || !IsRecipeOutputAvailable(outputItemId)
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
            || !ContainsRuntimeInputEnergyCoordinate(coordinate))
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

        runtimeSleeping = false;
        EnsureEffectivePairData();
        if (CanStoreFluid)
        {
            DiscardIncompatibleStoredFluid();
            if (ShouldAutoPullFluidFromConnectedStorage())
            {
                PullFluidFromConnectedStorage(deltaTime);
            }
        }

        if (hasActiveCraft)
        {
            UpdateActiveCraft(deltaTime);
            if (!hasActiveCraft)
            {
                TryStartNextCraft();
            }
        }
        else
        {
            TryStartNextCraft();
        }

        UpdateEnergyGaugeVisual();
        UpdateCraftParticleEffectVisual();
        RefreshWorkAnimatorState();
        RefreshRuntimeUpdateSleepState();
    }

    protected virtual bool ShouldKeepRuntimeUpdateTickActive()
    {
        return ShouldKeepFluidRuntimeUpdateTickActive();
    }

    protected virtual bool ShouldAutoPullFluidFromConnectedStorage()
    {
        return true;
    }

    protected virtual bool ShouldKeepFluidRuntimeUpdateTickActive()
    {
        if (!CanStoreFluid
            || !HasFluidStorageSpace
            || !ShouldAutoPullFluidFromConnectedStorage())
        {
            return false;
        }

        return HasConnectedFluidSource(ResolvePreferredFluidInputItemId());
    }

    private void RefreshRuntimeUpdateSleepState()
    {
        if (!Application.isPlaying || !isActiveAndEnabled)
        {
            return;
        }

        if (ShouldKeepRuntimeUpdateTickActive() || HasActiveOrPendingCraft())
        {
            SetRuntimeSleeping(false);
            return;
        }

        SetRuntimeSleeping(true);
    }

    protected void WakeRuntimeUpdate()
    {
        if (!Application.isPlaying || !isActiveAndEnabled)
        {
            return;
        }

        SetRuntimeSleeping(false, true);
    }

    protected override void OnStoredFluidChanged(
        int previousFluidItemId,
        float previousStoredLiters,
        int currentFluidItemId,
        float currentStoredLiters)
    {
        base.OnStoredFluidChanged(
            previousFluidItemId,
            previousStoredLiters,
            currentFluidItemId,
            currentStoredLiters);

        WakeRuntimeUpdate();
        WakeRuntimeModulesAtFluidRuntimeCoordinates();
    }

    private void WakeRuntimeModulesAtFluidRuntimeCoordinates()
    {
        WakeRuntimeModulesAtCoordinates(runtimePipeInputCoordinates);
        WakeRuntimeModulesAtCoordinates(runtimeOutputCoordinates);
        WakeRuntimeModulesAtCoordinates(runtimeGridCoordinates);
    }

    private static void WakeRuntimeModulesAtCoordinates(IReadOnlyList<Vector2Int> coordinates)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            WakeRuntimeModulesAtCoordinate(coordinate);
            for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
            {
                WakeRuntimeModulesAtCoordinate(coordinate + FluidCardinalDirections[directionIndex]);
            }
        }
    }

    private void SetRuntimeSleeping(bool sleeping, bool force = false)
    {
        if (!force && runtimeSleeping == sleeping)
        {
            return;
        }

        runtimeSleeping = sleeping;
        SetRuntimeUpdateTickRegistered(!runtimeSleeping && isActiveAndEnabled);
    }

    private void SetRuntimeUpdateTickRegistered(bool registered)
    {
        if (registered)
        {
            MapObjectTickManager.RegisterUpdateTick(this);
        }
        else
        {
            MapObjectTickManager.UnregisterUpdateTick(this);
        }
    }

    private void PullFluidFromConnectedStorage(float deltaTime)
    {
        if (deltaTime <= 0f || !CanStoreFluid || !HasFluidStorageSpace)
        {
            return;
        }

        int requiredFluidItemId = ResolvePreferredFluidInputItemId();
        TryPullFluidFromConnectedStorage(
            requiredFluidItemId,
            ConnectedFluidStorageTransferLitersPerSecond * deltaTime,
            out _);
    }

    protected bool HasConnectedFluidSource(int requiredFluidItemId)
    {
        return TryFindConnectedFluidSource(requiredFluidItemId, out _);
    }

    protected bool TryPullFluidFromConnectedStorage(
        int requiredFluidItemId,
        float maxTransferLiters,
        out float acceptedLiters)
    {
        acceptedLiters = 0f;
        if (maxTransferLiters <= 0.0001f || !CanStoreFluid || !HasFluidStorageSpace)
        {
            return false;
        }

        int maxAttempts = Mathf.Max(1, activeRuntimeModules.Count);
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float remainingLiters = Mathf.Min(
                maxTransferLiters - acceptedLiters,
                AvailableFluidStorageLiters);
            if (remainingLiters <= 0.0001f)
            {
                break;
            }

            if (!TryFindConnectedFluidSource(requiredFluidItemId, out InstallationObject sourceStorage)
                || sourceStorage == null)
            {
                break;
            }

            int transferFluidItemId = requiredFluidItemId >= 0
                ? requiredFluidItemId
                : sourceStorage.StoredFluidItemId;
            if (transferFluidItemId < 0)
            {
                break;
            }

            float transferLiters = Mathf.Min(
                remainingLiters,
                sourceStorage.StoredFluidLiters,
                CalculateFluidEqualizationTransferLiters(sourceStorage, this));
            float transferTemperatureCelsius = sourceStorage.GetStoredFluidTemperatureCelsius(transferFluidItemId);
            if (transferLiters <= 0.0001f
                || !sourceStorage.TryConsumeFluidLiters(transferFluidItemId, transferLiters, out float consumedLiters)
                || consumedLiters <= 0.0001f)
            {
                break;
            }

            TryAddFluidLiters(
                transferFluidItemId,
                consumedLiters,
                transferTemperatureCelsius,
                out float acceptedThisAttempt);
            acceptedThisAttempt = Mathf.Max(0f, acceptedThisAttempt);
            acceptedLiters += acceptedThisAttempt;

            float rejectedLiters = consumedLiters - acceptedThisAttempt;
            if (rejectedLiters > 0.0001f)
            {
                sourceStorage.TryAddFluidLiters(
                    transferFluidItemId,
                    rejectedLiters,
                    transferTemperatureCelsius,
                    out _);
                break;
            }
        }

        return acceptedLiters > 0.0001f;
    }

    private bool TryGetCachedConnectedFluidSource(
        int requiredFluidItemId,
        float currentFillRatio,
        out InstallationObject sourceStorage)
    {
        sourceStorage = cachedConnectedFluidSource;
        if (cachedConnectedFluidSourceTopologyVersion != fluidTopologyVersion
            || cachedConnectedFluidSourceItemId != requiredFluidItemId
            || !CanUseConnectedFluidSource(sourceStorage, requiredFluidItemId, currentFillRatio))
        {
            ClearCachedConnectedFluidSource();
            sourceStorage = null;
            return false;
        }

        return true;
    }

    private bool CanUseConnectedFluidSource(
        InstallationObject sourceStorage,
        int requiredFluidItemId,
        float currentFillRatio)
    {
        return sourceStorage != null
               && sourceStorage != this
               && sourceStorage.gameObject.activeInHierarchy
               && sourceStorage.CanProvideFluidItem(requiredFluidItemId)
               && GetFluidStorageFillRatio(sourceStorage) > currentFillRatio + 0.001f;
    }

    private void CacheConnectedFluidSource(int requiredFluidItemId, InstallationObject sourceStorage)
    {
        cachedConnectedFluidSource = sourceStorage;
        cachedConnectedFluidSourceItemId = requiredFluidItemId;
        cachedConnectedFluidSourceTopologyVersion = fluidTopologyVersion;
    }

    private void ClearCachedConnectedFluidSource()
    {
        cachedConnectedFluidSource = null;
        cachedConnectedFluidSourceItemId = int.MinValue;
        cachedConnectedFluidSourceTopologyVersion = 0;
    }

    private bool TryFindConnectedFluidSource(int requiredFluidItemId, out InstallationObject sourceStorage)
    {
        sourceStorage = null;
        float currentFillRatio = GetFluidStorageFillRatio(this);
        if (TryGetCachedConnectedFluidSource(requiredFluidItemId, currentFillRatio, out sourceStorage))
        {
            return true;
        }

        if (!EnsureConnectedFluidSourceStorageCache()
            || !TrySelectConnectedFluidSourceFromCache(
                requiredFluidItemId,
                currentFillRatio,
                out sourceStorage))
        {
            return false;
        }

        CacheConnectedFluidSource(requiredFluidItemId, sourceStorage);
        return true;
    }

    private bool EnsureConnectedFluidSourceStorageCache()
    {
        if (cachedConnectedFluidSourceStoragesTopologyVersion == fluidTopologyVersion)
        {
            return cachedConnectedFluidSourceStorages.Count > 0;
        }

        cachedConnectedFluidSourceStorages.Clear();
        connectedFluidSearchQueue.Clear();
        connectedFluidSearchVisited.Clear();
        connectedFluidStorageCandidates.Clear();
        connectedFluidSeedCoordinates.Clear();

        CollectRuntimePipeAreaCoordinates(connectedFluidSeedCoordinates);
        if (connectedFluidSeedCoordinates.Count <= 0)
        {
            cachedConnectedFluidSourceStoragesTopologyVersion = fluidTopologyVersion;
            return false;
        }

        for (int i = 0; i < connectedFluidSeedCoordinates.Count; i++)
        {
            EnqueueConnectedFluidSearchCoordinate(connectedFluidSeedCoordinates[i]);
        }

        while (connectedFluidSearchQueue.Count > 0)
        {
            Vector2Int coordinate = connectedFluidSearchQueue.Dequeue();
            EnqueueSteamGeneratorPipePassCoordinatesAt(coordinate);
            bool isSeedCoordinate = ContainsCoordinate(connectedFluidSeedCoordinates, coordinate);
            bool hasPipe = TryGetConnectedPipeAtCoordinate(coordinate, out Pipe pipe, out Quaternion pipeRotation);
            TryResolveConnectedFluidSearchStorageAtCoordinate(
                coordinate,
                out InstallationObject fluidStorage,
                out bool storageIsPipeArea);

            AddConnectedFluidStorageCacheCandidate(
                fluidStorage,
                cachedConnectedFluidSourceStorages);

            if (!isSeedCoordinate && !hasPipe && !storageIsPipeArea)
            {
                continue;
            }

            for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = FluidCardinalDirections[directionIndex];
                if (hasPipe && !pipe.HasConnectionTowardsAt(coordinate, pipeRotation, direction))
                {
                    continue;
                }

                if (!hasPipe
                    && !CanFluidSearchLeaveCoordinate(
                        coordinate,
                        isSeedCoordinate,
                        fluidStorage,
                        storageIsPipeArea,
                        direction))
                {
                    continue;
                }

                Vector2Int nextCoordinate = coordinate + direction;
                if (!TryGetConnectedFluidNodeAtCoordinate(
                        nextCoordinate,
                        -direction,
                        out InstallationObject nextStorage,
                        out bool canContinueRoute))
                {
                    continue;
                }

                AddConnectedFluidStorageCacheCandidate(
                    nextStorage,
                    cachedConnectedFluidSourceStorages);

                if (canContinueRoute)
                {
                    EnqueueConnectedFluidSearchCoordinate(nextCoordinate);
                }
            }

            if (hasPipe
                && pipe.TryGetRemoteConnectionCoordinate(coordinate, out Vector2Int remoteCoordinate))
            {
                EnqueueConnectedFluidSearchCoordinate(remoteCoordinate);
            }
        }

        cachedConnectedFluidSourceStoragesTopologyVersion = fluidTopologyVersion;
        return cachedConnectedFluidSourceStorages.Count > 0;
    }

    private void EnqueueSteamGeneratorPipePassCoordinatesAt(Vector2Int coordinate)
    {
        bool hasSteamGeneratorPipeArea = EnqueueSteamGeneratorPipePassCoordinatesAt(
            registeredRuntimeAreaCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> areaModules)
                ? areaModules
                : null,
            coordinate);
        hasSteamGeneratorPipeArea |= EnqueueSteamGeneratorPipePassCoordinatesAt(
            registeredRuntimeGridCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> gridModules)
                ? gridModules
                : null,
            coordinate);

        if (!hasSteamGeneratorPipeArea
            || !TryResolveConnectedFluidStorageBodyAtCoordinate(
                coordinate,
                out InstallationObject bodyStorage)
            || !(bodyStorage is SteamGenerator bodyGenerator))
        {
            return;
        }

        EnqueueSteamGeneratorPipePassCoordinates(bodyGenerator);
    }

    private bool EnqueueSteamGeneratorPipePassCoordinatesAt(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate)
    {
        if (modules == null)
        {
            return false;
        }

        bool foundPipeArea = false;
        foreach (InputOutputModule module in modules)
        {
            if (!(module is SteamGenerator steamGenerator)
                || !steamGenerator.gameObject.activeInHierarchy
                || !steamGenerator.ContainsRuntimePipeAreaBlockCoordinate(coordinate))
            {
                continue;
            }

            foundPipeArea = true;
            EnqueueSteamGeneratorPipePassCoordinates(steamGenerator);
        }

        return foundPipeArea;
    }

    private void EnqueueSteamGeneratorPipePassCoordinates(SteamGenerator steamGenerator)
    {
        if (steamGenerator == null
            || !steamGenerator.TryGetRuntimePipePassCoordinates(
                out Vector2Int inputCoordinate,
                out Vector2Int tailCoordinate))
        {
            return;
        }

        EnqueueConnectedFluidSearchCoordinate(inputCoordinate);
        EnqueueConnectedFluidSearchCoordinate(tailCoordinate);
    }

    private bool TrySelectConnectedFluidSourceFromCache(
        int requiredFluidItemId,
        float currentFillRatio,
        out InstallationObject sourceStorage)
    {
        sourceStorage = null;
        float bestFillRatio = currentFillRatio;
        for (int i = 0; i < cachedConnectedFluidSourceStorages.Count; i++)
        {
            InstallationObject storage = cachedConnectedFluidSourceStorages[i];
            if (!CanUseConnectedFluidSource(storage, requiredFluidItemId, currentFillRatio))
            {
                continue;
            }

            float fillRatio = GetFluidStorageFillRatio(storage);
            if (fillRatio <= bestFillRatio)
            {
                continue;
            }

            sourceStorage = storage;
            bestFillRatio = fillRatio;
        }

        return sourceStorage != null;
    }

    private void AddConnectedFluidStorageCacheCandidate(
        InstallationObject storage,
        List<InstallationObject> targetStorages)
    {
        if (storage == null
            || storage == this
            || !storage.CanStoreFluid
            || targetStorages == null
            || !connectedFluidStorageCandidates.Add(storage))
        {
            return;
        }

        targetStorages.Add(storage);
    }

    protected virtual int ResolvePreferredFluidInputItemId()
    {
        int recipeCount = GetEffectiveRecipeCount();
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(recipeIndex, out int inputItemId, out _, out int outputItemId, out _)
                || !IsFluidItemId(inputItemId)
                || !IsRecipeOutputAvailable(outputItemId))
            {
                continue;
            }

            return inputItemId;
        }

        return -1;
    }

    public override bool CanAcceptFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        if (!base.CanAcceptFluidItem(fluidItemId, requestedLiters))
        {
            return false;
        }

        if (fluidItemId < 0)
        {
            return true;
        }

        if (!HasFluidInputRecipe())
        {
            return true;
        }

        return CanAcceptFluidInputItem(fluidItemId);
    }

    private bool HasFluidInputRecipe()
    {
        int recipeCount = GetEffectiveRecipeCount();
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(recipeIndex, out int inputItemId, out _, out _, out _))
            {
                continue;
            }

            if (IsFluidItemId(inputItemId))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanAcceptFluidInputItem(int fluidItemId)
    {
        int recipeCount = GetEffectiveRecipeCount();
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(
                    recipeIndex,
                    out int inputItemId,
                    out _,
                    out _,
                    out _)
                || !IsFluidItemId(inputItemId)
                || inputItemId != fluidItemId)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void DiscardIncompatibleStoredFluid()
    {
        int storedFluidItemId = StoredFluidItemId;
        if (storedFluidItemId < 0
            || !HasFluidInputRecipe()
            || CanAcceptFluidInputItem(storedFluidItemId))
        {
            return;
        }

        SetStoredFluid(-1, 0f);
    }

    private void CollectRuntimePipeAreaCoordinates(List<Vector2Int> coordinates)
    {
        if (coordinates == null)
        {
            return;
        }

        int originalCount = coordinates.Count;
        AddRuntimePipeAreaCoordinates(runtimeInputEnergyCoordinates, coordinates);
        if (runtimeInputItemAreas != null)
        {
            for (int i = 0; i < runtimeInputItemAreas.Count; i++)
            {
                AddRuntimePipeAreaCoordinate(runtimeInputItemAreas[i].coordinate, coordinates);
            }
        }

        AddRuntimePipeAreaCoordinates(runtimeOutputCoordinates, coordinates);
        AddRuntimePipeAreaCoordinates(runtimePipeInputCoordinates, coordinates);

        if (CanStoreFluid && coordinates.Count == originalCount)
        {
            AddRuntimeFluidStoragePipeNodeCoordinates(RuntimeOccupiedCoordinates, coordinates);
        }
    }

    private static void AddRuntimeFluidStoragePipeNodeCoordinates(
        IReadOnlyList<Vector2Int> source,
        List<Vector2Int> target)
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

    private void AddRuntimePipeAreaCoordinates(IReadOnlyList<Vector2Int> source, List<Vector2Int> target)
    {
        if (source == null || target == null)
        {
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            AddRuntimePipeAreaCoordinate(source[i], target);
        }
    }

    private void AddRuntimePipeAreaCoordinate(Vector2Int coordinate, List<Vector2Int> target)
    {
        if (target == null
            || !ContainsRuntimePipeAreaBlockCoordinate(coordinate)
            || target.Contains(coordinate))
        {
            return;
        }

        target.Add(coordinate);
    }

    private bool TryGetConnectedFluidNodeAtCoordinate(
        Vector2Int coordinate,
        Vector2Int directionToPrevious,
        out InstallationObject storage,
        out bool canContinueRoute)
    {
        storage = null;
        canContinueRoute = false;

        if (TryGetConnectedPipeAtCoordinate(coordinate, out Pipe pipe, out Quaternion pipeRotation))
        {
            if (!pipe.HasConnectionTowardsAt(coordinate, pipeRotation, directionToPrevious))
            {
                return false;
            }

            TryResolveConnectedFluidSearchStorageAtCoordinate(coordinate, out storage, out _);
            if (storage != null
                && !CanFluidStorageConnectToDirection(storage, coordinate, directionToPrevious))
            {
                storage = null;
            }

            canContinueRoute = true;
            return true;
        }

        if (TryResolveConnectedFluidSearchStorageAtCoordinate(
                coordinate,
                out storage,
                out bool storageIsPipeArea))
        {
            if (storage is Fluidtank fluidTank
                && !fluidTank.HasFluidNetworkConnectionTowards(
                    coordinate,
                    directionToPrevious))
            {
                storage = null;
                return false;
            }

            if (storageIsPipeArea
                && !CanFluidStorageConnectToDirection(storage, coordinate, directionToPrevious))
            {
                storage = null;
                return false;
            }

            canContinueRoute = storageIsPipeArea;
            return true;
        }

        return false;
    }

    private bool TryGetConnectedPipeAtCoordinate(Vector2Int coordinate, out Pipe pipe, out Quaternion pipeRotation)
    {
        pipe = null;
        pipeRotation = Quaternion.identity;
        if (!TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is Pipe candidatePipe)
            || !candidatePipe.gameObject.activeInHierarchy)
        {
            return false;
        }

        pipe = candidatePipe;
        pipeRotation = candidatePipe.transform.rotation;
        return true;
    }

    private bool TryResolveConnectedFluidStorageAtCoordinate(
        Vector2Int coordinate,
        out InstallationObject storage)
    {
        return TryResolveConnectedFluidStorageAtCoordinate(coordinate, null, out storage);
    }

    private bool TryResolveConnectedFluidStorageAtCoordinate(
        Vector2Int coordinate,
        System.Predicate<InstallationObject> storageFilter,
        out InstallationObject storage)
    {
        if (TryGetRuntimePipeFluidStorageAtCoordinate(
                coordinate,
                this,
                false,
                storageFilter,
                out storage))
        {
            return true;
        }

        storage = null;
        if (!TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is InstallationObject installationObject)
            || installationObject == this
            || installationObject is Pipe
            || installationObject is Pump
            || !installationObject.gameObject.activeInHierarchy
            || !installationObject.CanStoreFluid
            || (storageFilter != null && !storageFilter(installationObject)))
        {
            return false;
        }

        storage = installationObject;
        return true;
    }

    private bool TryResolveConnectedFluidSearchStorageAtCoordinate(
        Vector2Int coordinate,
        out InstallationObject storage,
        out bool storageIsPipeArea)
    {
        storageIsPipeArea = false;
        if (TryGetRuntimePipeFluidStorageAtCoordinate(coordinate, this, false, out storage))
        {
            storageIsPipeArea = true;
            return true;
        }

        if (!TryResolveConnectedFluidStorageBodyAtCoordinate(coordinate, out storage))
        {
            return false;
        }

        // A generator body is not a pipe. Steam generators may share steam only
        // through their PipeArea/PipePass cells, otherwise parallel bodies leak
        // steam into each other without a real connector.
        if (storage is SteamGenerator)
        {
            storage = null;
            return false;
        }

        return true;
    }

    private bool CanFluidSearchLeaveCoordinate(
        Vector2Int coordinate,
        bool isSeedCoordinate,
        InstallationObject storage,
        bool storageIsPipeArea,
        Vector2Int direction)
    {
        if (direction == Vector2Int.zero)
        {
            return false;
        }

        if (storageIsPipeArea)
        {
            return CanFluidStorageConnectToDirection(storage, coordinate, direction);
        }

        if (storage is Fluidtank fluidTank)
        {
            return fluidTank.HasFluidNetworkConnectionTowards(coordinate, direction);
        }

        if (isSeedCoordinate && TryGetRuntimePipeAreaExternalDirection(coordinate, out Vector2Int seedDirection))
        {
            return seedDirection == direction;
        }

        return true;
    }

    private static bool CanFluidStorageConnectToDirection(
        InstallationObject storage,
        Vector2Int coordinate,
        Vector2Int direction)
    {
        return storage is InputOutputModule module
               && module.TryGetRuntimePipeAreaExternalDirection(coordinate, out Vector2Int externalDirection)
               && externalDirection == direction;
    }

    private bool TryGetRuntimePipeAreaExternalDirection(Vector2Int coordinate, out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        if (!ContainsRuntimePipeAreaBlockCoordinate(coordinate)
            || !TryGetNearestRuntimeObjectDirectionFromCoordinate(coordinate, out Vector2Int objectDirection)
            || objectDirection == Vector2Int.zero)
        {
            return false;
        }

        direction = -objectDirection;
        return direction != Vector2Int.zero;
    }

    private bool TryGetNearestRuntimeObjectDirectionFromCoordinate(Vector2Int coordinate, out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
            || !TryGetPrimaryObjectCell(out Vector2Int primaryObjectCell))
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != RectGridBlockType.Object)
            {
                continue;
            }

            Vector2Int localOffset = new Vector2Int(
                placement.x - primaryObjectCell.x,
                placement.y - primaryObjectCell.y);
            Vector2Int objectCoordinate = anchorCoordinate + RotateCellOffset(localOffset, quarterTurns);
            Vector2Int candidateDirection = objectCoordinate - coordinate;
            int distance = Mathf.Abs(candidateDirection.x) + Mathf.Abs(candidateDirection.y);
            if (distance <= 0 || distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            if (Mathf.Abs(candidateDirection.x) >= Mathf.Abs(candidateDirection.y))
            {
                direction = new Vector2Int(candidateDirection.x >= 0 ? 1 : -1, 0);
            }
            else
            {
                direction = new Vector2Int(0, candidateDirection.y >= 0 ? 1 : -1);
            }
        }

        return direction != Vector2Int.zero;
    }

    private bool TryResolveConnectedFluidStorageBodyAtCoordinate(
        Vector2Int coordinate,
        out InstallationObject storage)
    {
        storage = null;
        if (!TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is InstallationObject installationObject)
            || installationObject == this
            || installationObject is Pipe
            || installationObject is Pump
            || !installationObject.gameObject.activeInHierarchy
            || !installationObject.CanStoreFluid
            || !ContainsRuntimeOccupiedCoordinate(installationObject, coordinate))
        {
            return false;
        }

        storage = installationObject;
        return true;
    }

    private static bool ContainsRuntimeOccupiedCoordinate(InstallationObject installationObject, Vector2Int coordinate)
    {
        IReadOnlyList<Vector2Int> occupiedCoordinates = installationObject != null
            ? installationObject.RuntimeOccupiedCoordinates
            : null;
        if (occupiedCoordinates == null)
        {
            return false;
        }

        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            if (occupiedCoordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    private void EnqueueConnectedFluidSearchCoordinate(Vector2Int coordinate)
    {
        if (connectedFluidSearchVisited.Add(coordinate))
        {
            connectedFluidSearchQueue.Enqueue(coordinate);
        }
    }

    private static float GetFluidStorageFillRatio(InstallationObject storage)
    {
        if (storage == null)
        {
            return 0f;
        }

        float capacity = Mathf.Max(0f, storage.FluidStorageCapacityLiters);
        return capacity > 0.0001f
            ? Mathf.Clamp01(storage.StoredFluidLiters / capacity)
            : 0f;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        effectivePairDataInitialized = false;
        EnsureEffectivePairData();
        activeRuntimeModules.Add(this);
        RegisterRuntimeGridCoordinates();
        RegisterRuntimeAreaCoordinates();
        WakeRuntimeUpdate();
        RefreshWorkAnimatorState(true);
        if (HasRuntimePipeTopologyCoordinates())
        {
            RuntimePipeTopologyChanged?.Invoke(this);
        }
    }

    protected override void OnDisable()
    {
        bool hadRuntimePipeTopologyCoordinates = HasRuntimePipeTopologyCoordinates();
        SetWorkAnimatorState(false, true);
        StopCraftParticleEffectVisual(true);
        SetRuntimeUpdateTickRegistered(false);
        runtimeSleeping = false;
        UnregisterRuntimeGridCoordinates();
        UnregisterRuntimeAreaCoordinates();
        activeRuntimeModules.Remove(this);
        if (hadRuntimePipeTopologyCoordinates)
        {
            RuntimePipeTopologyChanged?.Invoke(this);
        }
        ReleaseEnergyGaugeVisual();
        base.OnDisable();
    }

    private bool HasRuntimePipeTopologyCoordinates()
    {
        return (runtimeGridCoordinates != null && runtimeGridCoordinates.Count > 0)
               || (runtimeOutputCoordinates != null && runtimeOutputCoordinates.Count > 0)
               || (runtimePipeInputCoordinates != null && runtimePipeInputCoordinates.Count > 0);
    }

    protected override void OnPlacementRuntimeChanged()
    {
        InvalidateEnergyGaugeWorldPosition();
        base.OnPlacementRuntimeChanged();
        WakeRuntimeUpdate();
    }

    private void OnDestroy()
    {
        SetRuntimeUpdateTickRegistered(false);
        runtimeSleeping = false;
        UnregisterRuntimeGridCoordinates();
        UnregisterRuntimeAreaCoordinates();
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

    public bool IsRuntimeOutputCoordinate(Vector2Int coordinate)
    {
        return ContainsRuntimeOutputCoordinate(coordinate);
    }

    public bool TryGetRuntimePipeOutputExternalDirection(
        Vector2Int coordinate,
        out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        bool isPipeOutput = ContainsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeOutputItem)
                            || ContainsRuntimeRectGridBlockType(
                                coordinate,
                                RectGridBlockType.DoublePipeOutputItem);
        return isPipeOutput
               && TryGetRuntimePipeAreaExternalDirection(coordinate, out direction);
    }

    public bool TryGetRuntimeOutputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> outputItemIds)
    {
        if (outputItemIds == null || !ContainsRuntimeOutputCoordinate(coordinate))
        {
            return false;
        }

        return AppendOutputItemIds(outputItemIds);
    }

    public bool TryAppendConfiguredOutputItemIds(ISet<int> outputItemIds)
    {
        return AppendOutputItemIds(outputItemIds);
    }

    public bool CanExposeStoredFluidAtRuntimePipeCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        ISet<int> scratchItemIds)
    {
        if (fluidItemId < 0 || !ContainsRuntimePipeAreaBlockCoordinate(coordinate))
        {
            return false;
        }

        if (scratchItemIds != null)
        {
            scratchItemIds.Clear();
        }

        if (ContainsRuntimeOutputCoordinate(coordinate))
        {
            bool matchesOutput = scratchItemIds != null
                && TryGetRuntimeOutputItemIdsAtCoordinate(coordinate, scratchItemIds)
                && scratchItemIds.Contains(fluidItemId);
            scratchItemIds?.Clear();
            return matchesOutput;
        }

        if (scratchItemIds != null)
        {
            if (AppendAcceptedRuntimeInputItemIdsAtCoordinate(coordinate, scratchItemIds)
                || AppendRuntimeInputItemIdsAtCoordinate(coordinate, scratchItemIds))
            {
                bool matchesInput = scratchItemIds.Contains(fluidItemId);
                scratchItemIds.Clear();
                return matchesInput;
            }

            scratchItemIds.Clear();
        }

        return !HasFluidInputRecipe() || CanAcceptFluidInputItem(fluidItemId);
    }

    private bool ContainsRuntimePipeInputCoordinate(Vector2Int coordinate)
    {
        return ContainsCoordinate(runtimePipeInputCoordinates, coordinate);
    }

    protected virtual bool AppendOutputItemIds(ISet<int> outputItemIds)
    {
        if (outputItemIds == null)
        {
            return false;
        }

        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        bool foundAny = false;
        for (int i = 0; i < outputs.Count; i++)
        {
            ItemDefinition itemDefinition = outputs[i].itemDefinition;
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

        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        for (int i = 0; i < outputs.Count; i++)
        {
            ItemDefinition itemDefinition = outputs[i].itemDefinition;
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
        rectGridDataInitialized = false;
        rectGridPlacementDataInitialized = false;
        RebuildRectGridCells();
        rectGridDataInitialized = true;
        EnsureRectGridPlacementData();
    }

    public void ClearRectGrid()
    {
        slotLayoutType = SlotLayoutType.None;
        rectGridCells.Clear();
        rectGridPlacements.Clear();
        rectGridDataInitialized = true;
        rectGridPlacementDataInitialized = true;
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

    public bool TryGetOutputRectGridBlockCell(out Vector2Int cell)
    {
        EnsureRectGridPlacementData();
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (!IsOutputBlockType(placement.blockType))
            {
                continue;
            }

            cell = new Vector2Int(placement.x, placement.y);
            return true;
        }

        cell = default;
        return false;
    }

    public bool TryGetRectGridObjectAnchorCell(MapObject footprintSource, out Vector2Int objectAnchorCell)
    {
        objectAnchorCell = Vector2Int.zero;
        EnsureRectGridData();
        EnsureRectGridPlacementData();
        if (slotLayoutType != SlotLayoutType.RectGrid || rectGridPlacements == null || rectGridPlacements.Count <= 0)
        {
            return false;
        }

        bool foundObject = false;
        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minY = int.MaxValue;
        int maxY = int.MinValue;
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.blockType != RectGridBlockType.Object)
            {
                continue;
            }

            foundObject = true;
            minX = Mathf.Min(minX, placement.x);
            maxX = Mathf.Max(maxX, placement.x);
            minY = Mathf.Min(minY, placement.y);
            maxY = Mathf.Max(maxY, placement.y);
        }

        if (!foundObject)
        {
            return false;
        }

        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        Vector2Int centerCell = anchorSource != null
            ? anchorSource.PlacementCenterCell
            : Vector2Int.zero;
        Vector2Int desiredCell = new Vector2Int(
            minX + Mathf.Clamp(centerCell.x, 0, Mathf.Max(0, maxX - minX)),
            minY + Mathf.Clamp(centerCell.y, 0, Mathf.Max(0, maxY - minY)));
        if (GetRectGridBlockAt(desiredCell.x, desiredCell.y) == RectGridBlockType.Object)
        {
            objectAnchorCell = desiredCell;
            return true;
        }

        int bestIndex = -1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.blockType != RectGridBlockType.Object)
            {
                continue;
            }

            int distance = Mathf.Abs(placement.x - desiredCell.x) + Mathf.Abs(placement.y - desiredCell.y);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bestIndex = i;
        }

        if (bestIndex < 0)
        {
            return false;
        }

        RectGridBlockPlacement nearestObjectPlacement = rectGridPlacements[bestIndex];
        objectAnchorCell = new Vector2Int(nearestObjectPlacement.x, nearestObjectPlacement.y);
        return true;
    }

    public bool TryGetRectGridPlacementCoordinate(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        RectGridBlockPlacement placement,
        out Vector2Int coordinate)
    {
        coordinate = Vector2Int.zero;
        EnsureRectGridData();
        EnsureRectGridPlacementData();
        if (slotLayoutType != SlotLayoutType.RectGrid
            || placement.blockType == RectGridBlockType.None
            || !IsValidRectGridCell(placement.x, placement.y)
            || !TryGetRectGridObjectAnchorCell(footprintSource, out Vector2Int objectAnchorCell))
        {
            return false;
        }

        Vector2Int localOffset = new Vector2Int(
            placement.x - objectAnchorCell.x,
            placement.y - objectAnchorCell.y);
        coordinate = anchorCoordinate + RotateRectGridOffset(localOffset, quarterTurns);
        return true;
    }

    public bool TryGetRectGridBlockTypeAtCoordinate(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int coordinate,
        out RectGridBlockType blockType)
    {
        blockType = RectGridBlockType.None;
        EnsureRectGridData();
        EnsureRectGridPlacementData();
        if (slotLayoutType != SlotLayoutType.RectGrid || rectGridPlacements == null)
        {
            return false;
        }

        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (!TryGetRectGridPlacementCoordinate(
                    footprintSource,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int placementCoordinate)
                || placementCoordinate != coordinate)
            {
                continue;
            }

            blockType = placement.blockType;
            return true;
        }

        return false;
    }

    public bool TryGetNearestRectGridObjectDirection(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int coordinate,
        out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        EnsureRectGridData();
        EnsureRectGridPlacementData();
        if (slotLayoutType != SlotLayoutType.RectGrid
            || rectGridPlacements == null
            || !TryGetRectGridObjectAnchorCell(footprintSource, out Vector2Int objectAnchorCell))
        {
            return false;
        }

        Vector2Int bestDelta = Vector2Int.zero;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.blockType != RectGridBlockType.Object)
            {
                continue;
            }

            Vector2Int localOffset = new Vector2Int(
                placement.x - objectAnchorCell.x,
                placement.y - objectAnchorCell.y);
            Vector2Int objectCoordinate = anchorCoordinate + RotateRectGridOffset(localOffset, quarterTurns);
            Vector2Int delta = objectCoordinate - coordinate;
            int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            if (distance <= 0 || distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bestDelta = delta;
        }

        return TryGetDominantCardinalDirection(bestDelta, out direction);
    }

    public static Vector2Int RotateRectGridOffset(Vector2Int offset, int quarterTurns)
    {
        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        switch (normalizedQuarterTurns)
        {
            case 1:
                return new Vector2Int(offset.y, -offset.x);
            case 2:
                return new Vector2Int(-offset.x, -offset.y);
            case 3:
                return new Vector2Int(-offset.y, offset.x);
            default:
                return offset;
        }
    }

    public static bool TryGetDominantCardinalDirection(Vector2Int offset, out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        if (offset == Vector2Int.zero)
        {
            return false;
        }

        if (Mathf.Abs(offset.x) >= Mathf.Abs(offset.y) && offset.x != 0)
        {
            direction = new Vector2Int(offset.x > 0 ? 1 : -1, 0);
            return true;
        }

        if (offset.y != 0)
        {
            direction = new Vector2Int(0, offset.y > 0 ? 1 : -1);
            return true;
        }

        return false;
    }

    public bool TryGetInitialOutputDirection(out RectGridDirection direction)
    {
        EnsureRectGridPlacementData();
        direction = RectGridDirection.Right;
        if (!TryGetPrimaryObjectCell(out Vector2Int objectCell)
            || !TryGetOutputRectGridBlockCell(out Vector2Int outputCell))
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
            || !TryGetOutputRectGridBlockCell(out Vector2Int outputCell))
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

    public bool TryGetElectricPowerRequirement(out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!RequiresElectricOperationalEnergy(installedDefinition))
        {
            return false;
        }

        wattsPerSecond = ItemDefinition.ResolveElectricUseWatts(installedDefinition);
        return wattsPerSecond > 0.0001f;
    }

    private bool RequiresElectricOperationalEnergy()
    {
        return RequiresElectricOperationalEnergy(ResolveInstalledDefinition());
    }

    public virtual bool TryGetElectricPowerDemand(out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        if (!hasActiveCraft || waitingForOutput)
        {
            return false;
        }

        return TryGetElectricPowerRequirement(out wattsPerSecond);
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
    protected float OperationalAnimationSpeedRatio => ResolveOperationalAnimationSpeedRatio();
    public bool IsWorkingForItemLight
    {
        get
        {
            ResolveObjectInfoStatus(out bool isWorking);
            return isWorking;
        }
    }

    private void EnsureEffectivePairData()
    {
        if (effectivePairDataInitialized && Application.isPlaying)
        {
            return;
        }

        effectiveInputList.Clear();
        effectiveOutputList.Clear();
        HashSet<InputOutputModule> visitedModules = new HashSet<InputOutputModule>();
        AppendEffectivePairData(this, visitedModules, effectiveInputList, effectiveOutputList);
        effectivePairDataInitialized = true;
    }

    private int GetEffectiveRecipeCount()
    {
        EnsureEffectivePairData();
        return Mathf.Min(effectiveInputList.Count, effectiveOutputList.Count);
    }

    private static void AppendEffectivePairData(
        InputOutputModule module,
        ISet<InputOutputModule> visitedModules,
        List<ItemIoEntry> resolvedInputs,
        List<ItemIoEntry> resolvedOutputs)
    {
        if (module == null
            || visitedModules == null
            || resolvedInputs == null
            || resolvedOutputs == null
            || !visitedModules.Add(module))
        {
            return;
        }

        module.EnsurePairData();
        AppendEffectivePairData(
            module.ResolveParentInputOutputModule(),
            visitedModules,
            resolvedInputs,
            resolvedOutputs);

        int localPairCount = Mathf.Min(module.inputList.Count, module.outputList.Count);
        for (int i = 0; i < localPairCount; i++)
        {
            resolvedInputs.Add(module.inputList[i]);
            resolvedOutputs.Add(module.outputList[i]);
        }
    }

    private bool HasCircularParentReference()
    {
        HashSet<InputOutputModule> visitedModules = new HashSet<InputOutputModule>();
        InputOutputModule current = this;
        while (current != null)
        {
            if (!visitedModules.Add(current))
            {
                return true;
            }

            current = current.ResolveParentInputOutputModule();
        }

        return false;
    }

    private InputOutputModule ResolveParentInputOutputModule()
    {
        return parentInputOutputModuleItem != null
            ? parentInputOutputModuleItem.mapObject as InputOutputModule
            : null;
    }

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

        int recipeCount = GetEffectiveRecipeCount();
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
            if (!IsRecipeOutputAvailable(outputItemId))
            {
                blockedByTargetFilter = true;
                continue;
            }

            hasFilterAllowedRecipe = true;

            if (!TryResolveRuntimeInputItemArea(recipeIndex, inputItemId, out RuntimeInputItemArea inputArea))
            {
                blockedByInputArea = true;
                continue;
            }

            if (GetRuntimeInputAreaCenterItemCount(inputArea.coordinate, inputItemId) < inputCount)
            {
                blockedByInputItem = true;
                continue;
            }

            if (!CanResolveOutputTarget(outputItemId, outputCount))
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

        energyItemId = GetRuntimeAreaTopItemId(runtimeInputEnergyCoordinates);
        energyAreaCapacity = Mathf.Max(
            ResolveRuntimeAreaCapacity(runtimeInputEnergyCoordinates, energyItemId),
            1);
        if (energyItemId >= 0)
        {
            energyAreaCount = GetRuntimeAreaObjectCount(runtimeInputEnergyCoordinates, energyItemId);
            energyAreaCapacity = Mathf.Max(energyAreaCapacity, energyAreaCount);
        }

        return true;
    }

    public bool TryGetObjectInfoBurnEnergyInput(
        out int burnEnergyAmount,
        out int energyAreaCapacity)
    {
        burnEnergyAmount = 0;
        energyAreaCapacity = 0;

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null
            || installedDefinition.useEnergyType != ItemDefinition.EnergyType.Burn
            || runtimeInputEnergyCoordinates == null
            || runtimeInputEnergyCoordinates.Count <= 0)
        {
            return false;
        }

        int energyItemId = GetRuntimeAreaTopItemId(runtimeInputEnergyCoordinates);
        energyAreaCapacity = Mathf.Max(
            ResolveRuntimeAreaCapacity(runtimeInputEnergyCoordinates, energyItemId),
            1);
        burnEnergyAmount = GetRuntimeAreaEnergyAmount(
            runtimeInputEnergyCoordinates,
            installedDefinition.useEnergyType);
        return true;
    }

    public bool TryGetObjectInfoEnergyUseRate(
        out ItemDefinition.EnergyType energyType,
        out float amountPerSecond)
    {
        energyType = ItemDefinition.EnergyType.None;
        amountPerSecond = 0f;

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return false;
        }

        energyType = installedDefinition.useEnergyType;
        amountPerSecond = ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition);
        return energyType != ItemDefinition.EnergyType.None && amountPerSecond > 0.0001f;
    }

    public bool TryGetObjectInfoItemPair(
        out int inputItemId,
        out int inputAreaCount,
        out int inputAreaCapacity,
        out int outputItemId,
        out int outputAreaCount,
        out int outputAreaCapacity)
    {
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

        IReadOnlyList<ItemIoEntry> inputs = InputList;
        for (int i = 0; i < inputs.Count; i++)
        {
            if (TryGetObjectInfoRecipeLine(
                    i,
                    out inputItemId,
                    out inputRecipeCount,
                    out outputItemId,
                    out outputRecipeCount))
            {
                if (!IsRecipeOutputAvailable(outputItemId))
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
        IReadOnlyList<ItemIoEntry> inputs = InputList;
        for (int i = 0; i < inputs.Count; i++)
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

            if (!IsRecipeOutputAvailable(outputItemId))
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
        inputAreaCapacity = ResolveObjectInfoAreaCapacity(
            objectInfoInputAreaCoordinates,
            inputItemId,
            inputAreaCount,
            inputRecipeCount);

        if (outputItemId >= 0)
        {
            outputAreaCount = GetRuntimeAreaObjectCount(runtimeOutputCoordinates, outputItemId);
            outputAreaCapacity = ResolveObjectInfoAreaCapacity(
                runtimeOutputCoordinates,
                outputItemId,
                outputAreaCount,
                outputRecipeCount);
        }
        else
        {
            outputAreaCount = 0;
            outputAreaCapacity = 0;
        }

        return true;
    }

    protected bool TryResolveObjectInfoInputAreaCounts(
        int inputItemId,
        int inputRecipeCount,
        out int inputAreaCount,
        out int inputAreaCapacity)
    {
        inputAreaCount = 0;
        inputAreaCapacity = 0;
        if (inputItemId < 0)
        {
            return false;
        }

        objectInfoInputAreaCoordinates.Clear();
        AppendRuntimeInputItemAreaCoordinates(inputItemId, objectInfoInputAreaCoordinates);
        if (objectInfoInputAreaCoordinates.Count <= 0)
        {
            return false;
        }

        inputAreaCount = GetRuntimeAreaObjectCount(objectInfoInputAreaCoordinates, inputItemId);
        inputAreaCapacity = ResolveObjectInfoAreaCapacity(
            objectInfoInputAreaCoordinates,
            inputItemId,
            inputAreaCount,
            Mathf.Max(1, inputRecipeCount));
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
        outputAreaCapacity = ResolveObjectInfoAreaCapacity(
            runtimeOutputCoordinates,
            outputItemId,
            outputAreaCount,
            Mathf.Max(1, outputRecipeCount));
        return true;
    }

    private int ResolveObjectInfoAreaCapacity(
        IReadOnlyList<Vector2Int> coordinates,
        int itemId,
        int currentCount,
        int recipeCount)
    {
        int capacity = ResolveRuntimeAreaCapacity(coordinates, itemId);
        ItemDefinition definition = ResolveItemDefinition(itemId);
        return definition != null && definition.oneItem
            ? capacity
            : Mathf.Max(capacity, Mathf.Max(currentCount, recipeCount));
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

        IReadOnlyList<ItemIoEntry> inputs = InputList;
        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        if (recipeIndex < 0 || recipeIndex >= inputs.Count)
        {
            return false;
        }

        ItemIoEntry inputEntry = inputs[recipeIndex];
        inputItemId = inputEntry.itemDefinition != null ? inputEntry.itemDefinition.id : -1;
        inputCount = Mathf.Max(1, inputEntry.count);

        if (recipeIndex < outputs.Count)
        {
            ItemIoEntry outputEntry = outputs[recipeIndex];
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

    public int ResolveRuntimeAreaCapacity(IReadOnlyList<Vector2Int> coordinates, int itemId = -1)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return ResolveItemStackCapacity(itemId, RuntimeAreaMaxObjects);
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

            if (!TryResolveRuntimeBlockCenterCapacity(coordinate, out int blockCapacity))
            {
                continue;
            }

            installedCapacityTotal += ResolveItemStackCapacity(itemId, blockCapacity);
            hasInstalledCapacity = true;
        }

        if (hasInstalledCapacity)
        {
            return Mathf.Max(1, installedCapacityTotal);
        }

        int defaultAreaCapacity = RuntimeAreaMaxObjects;
        ItemDefinition definition = ResolveItemDefinition(itemId);
        return definition != null && definition.oneItem
            ? Mathf.Min(defaultAreaCapacity, Mathf.Max(1, visitedCoordinates.Count))
            : defaultAreaCapacity;
    }

    private int ResolveRuntimeBlockCenterCapacity(Vector2Int coordinate, int itemId, int defaultCapacity)
    {
        int capacity = TryResolveRuntimeBlockCenterCapacity(coordinate, out int installedCapacity)
            ? Mathf.Max(1, installedCapacity)
            : Mathf.Max(1, defaultCapacity);
        return ResolveItemStackCapacity(itemId, capacity);
    }

    private static int ResolveItemStackCapacity(int itemId, int defaultCapacity)
    {
        return ItemDefinition.ResolveStackCapacity(
            ResolveItemDefinition(itemId),
            defaultCapacity);
    }

    private bool TryResolveRuntimeBlockCenterCapacity(Vector2Int coordinate, out int capacity)
    {
        capacity = 0;
        if (TryGetLoadedBlock(coordinate, out Block block)
            && block != null
            && block.TryGetInstalledItemAreaCapacity(out capacity))
        {
            capacity = Mathf.Max(1, capacity);
            return true;
        }

        BlockStateStore stateStore = ResolveBlockStateStore();
        if (stateStore == null
            || !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int anchorCoordinate)
            || !stateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState))
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveItemDefinition(installationState.itemId);
        if (installedDefinition == null
            || !(installedDefinition.mapObject is InstallationObject installationObject)
            || (installationObject.MapFilter & InstallationMapFilter.ItemArea) == 0)
        {
            return false;
        }

        capacity = installedDefinition.capacity > 0 ? installedDefinition.capacity : RuntimeAreaMaxObjects;
        return true;
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
            RemoveUniqueRectGridBlockGroup(blockType);
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
        if (rectGridDataInitialized && Application.isPlaying)
        {
            return;
        }

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

            rectGridDataInitialized = true;
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

        rectGridDataInitialized = true;
    }

    private void EnsureRectGridPlacementData()
    {
        if (rectGridPlacementDataInitialized && Application.isPlaying)
        {
            return;
        }

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

            rectGridPlacementDataInitialized = true;
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
            else if (IsInputEnergyBlockType(placement.blockType))
            {
                if (hasInputEnergy)
                {
                    continue;
                }

                hasInputEnergy = true;
            }
            else if (IsOutputBlockType(placement.blockType))
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
        rectGridPlacementDataInitialized = true;
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

    private void RemoveUniqueRectGridBlockGroup(RectGridBlockType blockType)
    {
        if (IsInputEnergyBlockType(blockType))
        {
            RemoveRectGridBlocks(IsInputEnergyBlockType);
            return;
        }

        if (IsOutputBlockType(blockType))
        {
            RemoveRectGridBlocks(IsOutputBlockType);
            return;
        }

        RemoveRectGridBlock(blockType);
    }

    private void RemoveRectGridBlocks(System.Predicate<RectGridBlockType> predicate)
    {
        if (predicate == null)
        {
            return;
        }

        for (int i = rectGridPlacements.Count - 1; i >= 0; i--)
        {
            if (predicate(rectGridPlacements[i].blockType))
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
        return IsInputEnergyBlockType(blockType)
            || IsOutputBlockType(blockType);
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

        int recipeCount = GetEffectiveRecipeCount();
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(recipeIndex, out int inputItemId, out int inputCount, out int outputItemId, out int outputCount))
            {
                continue;
            }

            if (!IsRecipeOutputAvailable(outputItemId))
            {
                continue;
            }

            if (!TryResolveRuntimeInputItemArea(recipeIndex, inputItemId, out RuntimeInputItemArea inputArea))
            {
                continue;
            }

            if (GetRuntimeInputAreaCenterItemCount(inputArea.coordinate, inputItemId) < inputCount)
            {
                continue;
            }

            if (!CanResolveOutputTarget(outputItemId, outputCount))
            {
                continue;
            }

            if (!TryEnsureCraftStartEnergy(installedDefinition))
            {
                continue;
            }

            if (ConsumeRuntimeInputAreaCenterObjects(
                    inputArea.coordinate,
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
        ItemDefinition outputDefinition = ResolveItemDefinition(outputItemId);
        if (outputDefinition != null && outputDefinition.oneItem && outputCount > 1)
        {
            return TryEmitSingleItemStacks(
                outputItemId,
                outputCount,
                startWorldPosition);
        }

        if (!TryResolveOutputTarget(outputItemId, outputCount, out RuntimeAreaOutputTarget outputTarget))
        {
            return false;
        }

        if (outputTarget.useSavedCenterStack)
        {
            BlockStateStore stateStore = ResolveBlockStateStore();
            int capacity = ResolveRuntimeBlockCenterCapacity(
                outputTarget.coordinate,
                outputItemId,
                RuntimeAreaMaxObjects);
            return stateStore != null
                   && stateStore.TryAddSavedCenterItems(outputTarget.coordinate, outputItemId, outputCount, capacity);
        }

        return TryEmitOutputItemsToBlock(outputTarget.block, outputItemId, outputCount, startWorldPosition);
    }

    private bool TryEmitSingleItemStacks(
        int outputItemId,
        int outputCount,
        Vector3 startWorldPosition)
    {
        if (!CanDistributeSingleItemStacks(outputItemId, outputCount))
        {
            return false;
        }

        for (int outputIndex = 0; outputIndex < outputCount; outputIndex++)
        {
            if (!TryResolveOutputTarget(outputItemId, 1, out RuntimeAreaOutputTarget outputTarget))
            {
                return false;
            }

            if (outputTarget.useSavedCenterStack)
            {
                BlockStateStore stateStore = ResolveBlockStateStore();
                int capacity = ResolveRuntimeBlockCenterCapacity(
                    outputTarget.coordinate,
                    outputItemId,
                    RuntimeAreaMaxObjects);
                if (stateStore == null
                    || !stateStore.TryAddSavedCenterItems(
                        outputTarget.coordinate,
                        outputItemId,
                        1,
                        capacity))
                {
                    return false;
                }

                continue;
            }

            Block outputBlock = outputTarget.block;
            if (outputBlock == null
                || !outputBlock.TryAddInputAreaCenterObjectAnimated(
                    outputItemId,
                    startWorldPosition,
                    outputIndex * Mathf.Max(0f, outputMoveInterval),
                    out PortableObject droppedObject))
            {
                return false;
            }

            DroppedItemPickupGate gate = droppedObject != null
                ? droppedObject.GetComponent<DroppedItemPickupGate>()
                : null;
            gate?.SetAutoPickupBlocked(true);
        }

        return true;
    }

    private bool CanDistributeSingleItemStacks(int itemId, int count)
    {
        if (itemId < 0
            || count <= 0
            || GetRuntimeAreaObjectCount(runtimeOutputCoordinates) + count
               > ResolveRuntimeAreaCapacity(runtimeOutputCoordinates, itemId))
        {
            return false;
        }

        int availableStackCount = 0;
        singleItemOutputVisitedCoordinates.Clear();
        for (int i = 0; i < runtimeOutputCoordinates.Count; i++)
        {
            Vector2Int coordinate = runtimeOutputCoordinates[i];
            if (!singleItemOutputVisitedCoordinates.Add(coordinate)
                || !CanAddRuntimeCenterItems(coordinate, itemId, 1, out _, out _))
            {
                continue;
            }

            availableStackCount++;
            if (availableStackCount >= count)
            {
                return true;
            }
        }

        return false;
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

    protected bool TryConsumeOperatingEnergy(float deltaTime, out float consumedEnergy)
    {
        consumedEnergy = 0f;
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            lastOperationalEnergySupplyRatio = 1f;
            return true;
        }

        float remainingEnergyCost = ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition) * Mathf.Max(0f, deltaTime);
        float requestedEnergyCost = remainingEnergyCost;
        if (remainingEnergyCost <= 0.0001f)
        {
            lastOperationalEnergySupplyRatio = 0f;
            return false;
        }

        if (installedDefinition.useEnergyType == ItemDefinition.EnergyType.Electricity)
        {
            energyGaugeCapacity = 0f;
            bool consumedElectricity = UtilityPole.TryConsumeElectricity(
                this,
                remainingEnergyCost,
                deltaTime,
                out consumedEnergy);
            lastOperationalEnergySupplyRatio = consumedElectricity
                ? Mathf.Clamp01(consumedEnergy / requestedEnergyCost)
                : 0f;
            return consumedElectricity;
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

        lastOperationalEnergySupplyRatio = Mathf.Clamp01(consumedEnergy / requestedEnergyCost);
        return consumedEnergy > 0.0001f;
    }

    protected bool TryEnsureCraftStartEnergy(ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        if (installedDefinition.useEnergyType == ItemDefinition.EnergyType.Electricity)
        {
            storedEnergy = 0f;
            energyGaugeCapacity = 0f;
            return UtilityPole.HasElectricityAvailable(this);
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

        if (installedDefinition.useEnergyType == ItemDefinition.EnergyType.Electricity)
        {
            return false;
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
            Vector2Int coordinate = runtimeInputEnergyCoordinates[i];
            int energyItemId = GetRuntimeAreaTopItemId(coordinate);
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

            if (ConsumeRuntimeInputAreaCenterObjects(
                    coordinate,
                    energyItemId,
                    1,
                    ResolveConsumeTargetWorldPosition(),
                    0f,
                    ShouldAnimateVirtualizedEnergyConsumption()) != 1)
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
        if (!TryResolveOutputTarget(outputItemId, outputCount, out RuntimeAreaOutputTarget target)
            || target.useSavedCenterStack
            || target.block == null)
        {
            return false;
        }

        targetBlock = target.block;
        return true;
    }

    protected bool CanResolveOutputTarget(int outputItemId, int outputCount)
    {
        ItemDefinition outputDefinition = ResolveItemDefinition(outputItemId);
        if (outputDefinition != null && outputDefinition.oneItem && outputCount > 1)
        {
            return CanDistributeSingleItemStacks(outputItemId, outputCount);
        }

        return TryResolveOutputTarget(outputItemId, outputCount, out _);
    }

    protected bool TryResolveOutputTarget(int outputItemId, int outputCount, out RuntimeAreaOutputTarget target)
    {
        target = default;
        if (outputItemId < 0
            || outputCount <= 0
            || runtimeOutputCoordinates.Count <= 0
            || IsFluidItemId(outputItemId))
        {
            return false;
        }

        if (GetRuntimeAreaObjectCount(runtimeOutputCoordinates) + outputCount
            > ResolveRuntimeAreaCapacity(runtimeOutputCoordinates, outputItemId))
        {
            return false;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireExistingCenterStack = pass == 0;
            for (int i = 0; i < runtimeOutputCoordinates.Count; i++)
            {
                Vector2Int coordinate = runtimeOutputCoordinates[i];
                if (!CanAddRuntimeCenterItems(coordinate, outputItemId, outputCount, out Block block, out bool useSavedCenterStack))
                {
                    continue;
                }

                if (requireExistingCenterStack && GetRuntimeAreaTopItemId(coordinate) != outputItemId)
                {
                    continue;
                }

                target = new RuntimeAreaOutputTarget(block, coordinate, useSavedCenterStack);
                return true;
            }
        }

        return false;
    }

    private bool CanAddRuntimeCenterItems(
        Vector2Int coordinate,
        int itemId,
        int count,
        out Block block,
        out bool useSavedCenterStack)
    {
        block = null;
        useSavedCenterStack = false;
        if (itemId < 0 || count <= 0)
        {
            return false;
        }

        if (!TryResolveRuntimeAreaBlock(coordinate, out block, out useSavedCenterStack))
        {
            return false;
        }

        if (!RuntimeCenterStorageAcceptsItem(coordinate, itemId, block, useSavedCenterStack))
        {
            return false;
        }

        if (useSavedCenterStack)
        {
            BlockStateStore stateStore = ResolveBlockStateStore();
            int capacity = ResolveRuntimeBlockCenterCapacity(coordinate, itemId, RuntimeAreaMaxObjects);
            return stateStore != null
                   && stateStore.CanAddSavedCenterItems(coordinate, itemId, count, capacity);
        }

        return block != null
               && block.Type == Block.BlockType.Ground
               && block.CanAddInputAreaCenterObjects(count, itemId);
    }

    private bool RuntimeCenterStorageAcceptsItem(
        Vector2Int coordinate,
        int itemId,
        Block block,
        bool useSavedCenterStack)
    {
        if (block != null && block.MapObject is BoxObject loadedBox)
        {
            bool acceptsItem = loadedBox.AcceptsItem(itemId);
            if (!acceptsItem)
            {
                loadedBox.EnsureClosedFilterIconVisible();
            }

            return acceptsItem;
        }

        if (!useSavedCenterStack)
        {
            return true;
        }

        BlockStateStore stateStore = ResolveBlockStateStore();
        if (stateStore == null
            || !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int anchorCoordinate)
            || !stateStore.TryGetInstallationState(
                anchorCoordinate,
                out BlockStateStore.InstallationSaveState installationState))
        {
            return true;
        }

        ItemDefinition installedDefinition = ResolveItemDefinition(installationState.itemId);
        if (installedDefinition == null || !(installedDefinition.mapObject is BoxObject))
        {
            return true;
        }

        return MapObject.IsItemAllowedByFilterMask(
            itemId,
            installationState.itemFilterMaskInitialized,
            installationState.itemFilterMaskWords);
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

            if (!TryResolveRuntimeAreaBlock(coordinate, out Block block, out bool useSavedCenterStack))
            {
                continue;
            }

            if (useSavedCenterStack)
            {
                BlockStateStore stateStore = ResolveBlockStateStore();
                count += stateStore != null ? stateStore.GetSavedCenterItemCount(coordinate, itemId) : 0;
                continue;
            }

            if (block != null && block.Type == Block.BlockType.Ground)
            {
                count += block.GetInputAreaCenterItemCount(itemId);
            }
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

            int itemId = GetRuntimeAreaTopItemId(coordinate);
            if (itemId >= 0)
            {
                return itemId;
            }
        }

        return -1;
    }

    private int GetRuntimeAreaTopItemId(Vector2Int coordinate)
    {
        if (!TryResolveRuntimeAreaBlock(coordinate, out Block block, out bool useSavedCenterStack))
        {
            return -1;
        }

        if (useSavedCenterStack)
        {
            BlockStateStore stateStore = ResolveBlockStateStore();
            return stateStore != null ? stateStore.GetSavedCenterTopItemId(coordinate) : -1;
        }

        return block != null && block.Type == Block.BlockType.Ground
            ? block.GetInputAreaCenterItemId()
            : -1;
    }

    private int GetRuntimeAreaEnergyAmount(
        IReadOnlyList<Vector2Int> coordinates,
        ItemDefinition.EnergyType energyType)
    {
        if (coordinates == null || coordinates.Count <= 0 || energyType == ItemDefinition.EnergyType.None)
        {
            return 0;
        }

        int totalEnergy = 0;
        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            int itemId = GetRuntimeAreaTopItemId(coordinate);
            if (itemId < 0)
            {
                continue;
            }

            ItemDefinition energyDefinition = ResolveItemDefinition(itemId);
            if (energyDefinition == null
                || energyDefinition.energyType != energyType
                || energyDefinition.energyAmount <= 0)
            {
                continue;
            }

            int itemCount = GetRuntimeInputAreaCenterItemCount(coordinate, itemId);
            totalEnergy += Mathf.Max(0, itemCount) * energyDefinition.energyAmount;
        }

        return Mathf.Max(0, totalEnergy);
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

        IReadOnlyList<ItemIoEntry> inputs = InputList;
        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        if (recipeIndex < 0 || recipeIndex >= inputs.Count || recipeIndex >= outputs.Count)
        {
            return false;
        }

        ItemIoEntry inputEntry = inputs[recipeIndex];
        ItemIoEntry outputEntry = outputs[recipeIndex];
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

    private bool TryResolveRuntimeAreaBlock(
        Vector2Int coordinate,
        out Block block,
        out bool useSavedCenterStack)
    {
        block = null;
        TerrainGenerator terrain = ResolveTerrain();
        bool hasLoadedBlock = terrain != null && terrain.TryGetLoadedBlock(coordinate, out block) && block != null;
        useSavedCenterStack = !hasLoadedBlock
                              || (terrain != null && terrain.IsFloorObjectCoordinateVirtualized(coordinate));
        return hasLoadedBlock || useSavedCenterStack;
    }

    protected int GetRuntimeInputAreaCenterItemCount(Vector2Int coordinate, int itemId = -1)
    {
        if (!TryResolveRuntimeAreaBlock(coordinate, out Block block, out bool useSavedCenterStack))
        {
            return 0;
        }

        if (useSavedCenterStack)
        {
            BlockStateStore stateStore = ResolveBlockStateStore();
            return stateStore != null ? stateStore.GetSavedCenterItemCount(coordinate, itemId) : 0;
        }

        return block != null && block.Type == Block.BlockType.Ground
            ? block.GetInputAreaCenterItemCount(itemId)
            : 0;
    }

    protected int ConsumeRuntimeInputAreaCenterObjects(
        Vector2Int coordinate,
        int itemId,
        int count,
        Vector3 consumeTargetWorldPosition,
        float moveInterval,
        bool animateVirtualizedConsumption = false)
    {
        if (itemId < 0 || count <= 0)
        {
            return 0;
        }

        if (!TryResolveRuntimeAreaBlock(coordinate, out Block block, out bool useSavedCenterStack))
        {
            return 0;
        }

        if (useSavedCenterStack)
        {
            BlockStateStore stateStore = ResolveBlockStateStore();
            int removedCount = stateStore != null
                ? stateStore.RemoveSavedCenterItems(coordinate, itemId, count)
                : 0;
            if (animateVirtualizedConsumption && block != null)
            {
                float interval = Mathf.Max(0f, moveInterval);
                for (int removedIndex = 0; removedIndex < removedCount; removedIndex++)
                {
                    block.PlayVirtualInputAreaConsumeAnimation(
                        itemId,
                        consumeTargetWorldPosition,
                        removedIndex * interval);
                }
            }

            return removedCount;
        }

        return block != null
            ? block.ConsumeInputAreaCenterObjectsAnimated(itemId, count, consumeTargetWorldPosition, moveInterval)
            : 0;
    }

    protected bool TryRestoreRuntimeInputAreaCenterObject(
        Vector2Int coordinate,
        int itemId,
        Vector3 restoreStartWorldPosition)
    {
        if (itemId < 0
            || !TryResolveRuntimeAreaBlock(coordinate, out Block block, out bool useSavedCenterStack))
        {
            return false;
        }

        if (useSavedCenterStack)
        {
            BlockStateStore stateStore = ResolveBlockStateStore();
            int capacity = ResolveRuntimeBlockCenterCapacity(
                coordinate,
                itemId,
                RuntimeAreaMaxObjects);
            return stateStore != null
                   && stateStore.TryAddSavedCenterItems(coordinate, itemId, 1, capacity);
        }

        return block != null
               && block.TryAddInputAreaCenterObjectAnimated(
                   itemId,
                   restoreStartWorldPosition,
                   0f,
                   out _);
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

    private BlockStateStore ResolveBlockStateStore()
    {
        if (cachedBlockStateStore != null)
        {
            return cachedBlockStateStore;
        }

        TerrainGenerator terrain = ResolveTerrain();
        cachedBlockStateStore = terrain != null ? terrain.GetComponent<BlockStateStore>() : null;
        return cachedBlockStateStore;
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

    public static ItemDefinition ResolveItemDefinition(int itemId)
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

    public static bool IsFluidItemId(int itemId)
    {
        return IsFluidItemDefinition(ResolveItemDefinition(itemId));
    }

    public static bool IsFluidItemDefinition(ItemDefinition definition)
    {
        if (definition == null)
        {
            return false;
        }

        string itemName = definition.itemName;
        return string.Equals(itemName, "Water", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemName, "Steam", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemName, "Oil", System.StringComparison.OrdinalIgnoreCase);
    }

    protected static bool RequiresOperationalEnergy(ItemDefinition installedDefinition)
    {
        return installedDefinition != null
               && installedDefinition.useEnergyType != ItemDefinition.EnergyType.None
               && ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition) > 0f;
    }

    protected static bool RequiresElectricOperationalEnergy(ItemDefinition installedDefinition)
    {
        return RequiresOperationalEnergy(installedDefinition)
               && installedDefinition.useEnergyType == ItemDefinition.EnergyType.Electricity;
    }

    protected bool HasOperationalEnergyAvailable(ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        if (installedDefinition.useEnergyType == ItemDefinition.EnergyType.Electricity)
        {
            return UtilityPole.HasElectricityAvailable(this);
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
            int energyItemId = GetRuntimeAreaTopItemId(runtimeInputEnergyCoordinates[i]);
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

    private float ResolveOperationalAnimationSpeedRatio()
    {
        float speedRatio = Mathf.Clamp01(lastOperationalEnergySupplyRatio);
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!RequiresElectricOperationalEnergy(installedDefinition))
        {
            return speedRatio;
        }

        float requestedWatts = ItemDefinition.ResolveElectricUseWatts(installedDefinition);
        if (!UtilityPole.TryGetElectricSupplyRatio(this, requestedWatts, out float networkSupplyRatio))
        {
            return 0f;
        }

        return Mathf.Min(speedRatio, Mathf.Clamp01(networkSupplyRatio));
    }

    private bool TryGetCachedFluidOutputStorage(
        int fluidItemId,
        float fluidLiters,
        out InstallationObject targetStorage)
    {
        targetStorage = cachedFluidOutputStorage;
        if (cachedFluidOutputTopologyVersion != fluidTopologyVersion
            || cachedFluidOutputItemId != fluidItemId
            || !CanUseFluidOutputStorage(targetStorage, fluidItemId, fluidLiters))
        {
            ClearCachedFluidOutputStorage();
            targetStorage = null;
            return false;
        }

        return true;
    }

    private void CacheFluidOutputStorage(int fluidItemId, InstallationObject targetStorage)
    {
        cachedFluidOutputStorage = targetStorage;
        cachedFluidOutputItemId = fluidItemId;
        cachedFluidOutputTopologyVersion = fluidTopologyVersion;
    }

    private void ClearCachedFluidOutputStorage()
    {
        cachedFluidOutputStorage = null;
        cachedFluidOutputItemId = int.MinValue;
        cachedFluidOutputTopologyVersion = 0;
    }

    protected bool TryResolveFluidOutputStorage(int fluidItemId, float fluidLiters, out InstallationObject targetStorage)
    {
        targetStorage = null;
        if (!IsFluidItemId(fluidItemId)
            || fluidLiters <= 0f
            || runtimeOutputCoordinates == null
            || runtimeOutputCoordinates.Count <= 0)
        {
            return false;
        }

        if (TryGetCachedFluidOutputStorage(fluidItemId, fluidLiters, out targetStorage))
        {
            return true;
        }

        if (!EnsureFluidOutputStorageCache()
            || !TrySelectFluidOutputStorageFromCache(
                fluidItemId,
                fluidLiters,
                out targetStorage))
        {
            return false;
        }

        CacheFluidOutputStorage(fluidItemId, targetStorage);
        return true;
    }

    protected bool TryGetFluidOutputAvailableLiters(
        int fluidItemId,
        float maxLiters,
        out float availableLiters)
    {
        availableLiters = 0f;
        if (!IsFluidItemId(fluidItemId)
            || maxLiters <= 0.0001f
            || runtimeOutputCoordinates == null
            || runtimeOutputCoordinates.Count <= 0
            || !EnsureFluidOutputStorageCache())
        {
            return false;
        }

        for (int i = 0; i < cachedFluidOutputStorages.Count; i++)
        {
            InstallationObject storage = cachedFluidOutputStorages[i];
            if (!CanUseFluidOutputStorageWithAnySpace(storage, fluidItemId))
            {
                continue;
            }

            availableLiters += Mathf.Max(0f, storage.AvailableFluidStorageLiters);
            if (availableLiters + 0.0001f >= maxLiters)
            {
                availableLiters = maxLiters;
                return true;
            }
        }

        return availableLiters > 0.0001f;
    }

    protected bool TryEmitFluidOutputToConnectedStorages(
        int fluidItemId,
        float requestedLiters,
        float temperatureCelsius,
        out float acceptedLiters)
    {
        acceptedLiters = 0f;
        if (!IsFluidItemId(fluidItemId)
            || requestedLiters <= 0.0001f
            || runtimeOutputCoordinates == null
            || runtimeOutputCoordinates.Count <= 0
            || !EnsureFluidOutputStorageCache())
        {
            return false;
        }

        int maxAttempts = Mathf.Max(1, cachedFluidOutputStorages.Count);
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float remainingLiters = requestedLiters - acceptedLiters;
            if (remainingLiters <= 0.0001f)
            {
                break;
            }

            if (!TrySelectFluidOutputStorageWithAnySpaceFromCache(fluidItemId, out InstallationObject targetStorage)
                || targetStorage == null)
            {
                break;
            }

            float litersToEmit = Mathf.Min(
                remainingLiters,
                Mathf.Max(0f, targetStorage.AvailableFluidStorageLiters));
            if (litersToEmit <= 0.0001f)
            {
                break;
            }

            if (!targetStorage.TryAddFluidLiters(
                    fluidItemId,
                    litersToEmit,
                    temperatureCelsius,
                    out float acceptedThisAttempt)
                || acceptedThisAttempt <= 0.0001f)
            {
                break;
            }

            acceptedLiters += Mathf.Max(0f, acceptedThisAttempt);
        }

        return acceptedLiters > 0.0001f;
    }

    private bool EnsureFluidOutputStorageCache()
    {
        if (cachedFluidOutputStoragesTopologyVersion == fluidTopologyVersion)
        {
            return cachedFluidOutputStorages.Count > 0;
        }

        cachedFluidOutputStorages.Clear();
        connectedFluidSearchQueue.Clear();
        connectedFluidSearchVisited.Clear();
        connectedFluidStorageCandidates.Clear();

        for (int i = 0; i < runtimeOutputCoordinates.Count; i++)
        {
            EnqueueConnectedFluidSearchCoordinate(runtimeOutputCoordinates[i]);
        }

        while (connectedFluidSearchQueue.Count > 0)
        {
            Vector2Int coordinate = connectedFluidSearchQueue.Dequeue();
            EnqueueSteamGeneratorPipePassCoordinatesAt(coordinate);
            AddFluidOutputStorageCacheCandidatesAtCoordinate(coordinate);

            bool isOutputSeed = ContainsCoordinate(runtimeOutputCoordinates, coordinate);
            bool hasPipe = TryGetConnectedPipeAtCoordinate(coordinate, out Pipe pipe, out Quaternion pipeRotation);
            TryResolveConnectedFluidSearchStorageAtCoordinate(
                coordinate,
                out InstallationObject fluidStorage,
                out bool storageIsPipeArea);

            if (!isOutputSeed && !hasPipe && !storageIsPipeArea)
            {
                continue;
            }

            for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = FluidCardinalDirections[directionIndex];
                if (hasPipe && !pipe.HasConnectionTowardsAt(coordinate, pipeRotation, direction))
                {
                    continue;
                }

                if (!hasPipe
                    && !CanFluidSearchLeaveCoordinate(
                        coordinate,
                        isOutputSeed,
                        fluidStorage,
                        storageIsPipeArea,
                        direction))
                {
                    continue;
                }

                Vector2Int nextCoordinate = coordinate + direction;
                if (!TryGetConnectedFluidNodeAtCoordinate(
                        nextCoordinate,
                        -direction,
                        out InstallationObject nextStorage,
                        out bool canContinueRoute))
                {
                    continue;
                }

                AddFluidOutputStorageCacheCandidate(nextStorage);

                if (canContinueRoute)
                {
                    EnqueueConnectedFluidSearchCoordinate(nextCoordinate);
                }
            }

            if (hasPipe
                && pipe.TryGetRemoteConnectionCoordinate(coordinate, out Vector2Int remoteCoordinate))
            {
                EnqueueConnectedFluidSearchCoordinate(remoteCoordinate);
            }
        }

        cachedFluidOutputStoragesTopologyVersion = fluidTopologyVersion;
        return cachedFluidOutputStorages.Count > 0;
    }

    private bool TrySelectFluidOutputStorageFromCache(
        int fluidItemId,
        float fluidLiters,
        out InstallationObject targetStorage)
    {
        targetStorage = null;
        float bestTargetFillRatio = float.PositiveInfinity;
        for (int i = 0; i < cachedFluidOutputStorages.Count; i++)
        {
            InstallationObject storage = cachedFluidOutputStorages[i];
            if (!CanUseFluidOutputStorage(storage, fluidItemId, fluidLiters))
            {
                continue;
            }

            float fillRatio = GetFluidStorageFillRatio(storage);
            if (targetStorage != null && fillRatio >= bestTargetFillRatio)
            {
                continue;
            }

            targetStorage = storage;
            bestTargetFillRatio = fillRatio;
        }

        return targetStorage != null;
    }

    private bool TrySelectFluidOutputStorageWithAnySpaceFromCache(
        int fluidItemId,
        out InstallationObject targetStorage)
    {
        targetStorage = null;
        float bestTargetFillRatio = float.PositiveInfinity;
        for (int i = 0; i < cachedFluidOutputStorages.Count; i++)
        {
            InstallationObject storage = cachedFluidOutputStorages[i];
            if (!CanUseFluidOutputStorageWithAnySpace(storage, fluidItemId))
            {
                continue;
            }

            float fillRatio = GetFluidStorageFillRatio(storage);
            if (targetStorage != null && fillRatio >= bestTargetFillRatio)
            {
                continue;
            }

            targetStorage = storage;
            bestTargetFillRatio = fillRatio;
        }

        return targetStorage != null;
    }

    private void AddFluidOutputStorageCacheCandidatesAtCoordinate(Vector2Int coordinate)
    {
        if (TryResolveConnectedFluidStorageBodyAtCoordinate(
                coordinate,
                out InstallationObject bodyStorage))
        {
            AddFluidOutputStorageCacheCandidate(bodyStorage);
        }

        if (TryResolveConnectedFluidStorageAtCoordinate(
                coordinate,
                null,
                out InstallationObject areaStorage))
        {
            AddFluidOutputStorageCacheCandidate(areaStorage);
        }
    }

    private void AddFluidOutputStorageCacheCandidate(InstallationObject storage)
    {
        if (storage == null
            || storage == this
            || !storage.CanStoreFluid
            || !connectedFluidStorageCandidates.Add(storage))
        {
            return;
        }

        cachedFluidOutputStorages.Add(storage);
    }

    private bool CanUseFluidOutputStorage(InstallationObject storage, int fluidItemId, float fluidLiters)
    {
        return storage != null
               && storage != this
               && storage.gameObject.activeInHierarchy
               && storage.CanStoreFluid
               && storage.CanAcceptFluidItem(fluidItemId, fluidLiters);
    }

    private bool CanUseFluidOutputStorageWithAnySpace(InstallationObject storage, int fluidItemId)
    {
        return storage != null
               && storage != this
               && storage.gameObject.activeInHierarchy
               && storage.CanStoreFluid
               && storage.AvailableFluidStorageLiters > 0.0001f
               && storage.CanAcceptFluidItem(fluidItemId, 0.0001f);
    }

    protected bool TryEmitFluidOutputToConnectedStorage(int fluidItemId, int fluidLiters)
    {
        if (!TryResolveFluidOutputStorage(fluidItemId, fluidLiters, out InstallationObject targetStorage)
            || targetStorage == null
            || !targetStorage.TryAddFluidLiters(
                fluidItemId,
                fluidLiters,
                GetStoredFluidTemperatureCelsius(fluidItemId),
                out float acceptedLiters))
        {
            return false;
        }

        return acceptedLiters + 0.0001f >= fluidLiters;
    }

    public static bool CoordinateIsRuntimeInputEnergyBlock(Vector2Int coordinate)
    {
        return CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.InputEnergy)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeInputEnergy)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.DoubleEnergy);
    }

    public static bool CoordinateIsRuntimeInputItemBlock(Vector2Int coordinate)
    {
        return CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.InputItem)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeInputItem)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.DoubleInputItem);
    }

    public static bool CoordinateIsRuntimeOutputBlock(Vector2Int coordinate)
    {
        return CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.Output)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeOutputItem)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.DoublePipeOutputItem);
    }

    public static bool CoordinateIsRuntimePipeInputBlock(Vector2Int coordinate)
    {
        return CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeInput);
    }

    public static bool CoordinateIsRuntimeInputOutputAreaBlock(Vector2Int coordinate)
    {
        return CoordinateIsRuntimeInputEnergyBlock(coordinate)
            || CoordinateIsRuntimeInputItemBlock(coordinate)
            || CoordinateIsRuntimeOutputBlock(coordinate)
            || CoordinateIsRuntimePipeInputBlock(coordinate);
    }

    public static bool CoordinateAllowsRuntimePipeBlock(Vector2Int coordinate)
    {
        return CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeInputEnergy)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeInputItem)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeOutputItem)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeInput)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.DoubleEnergy)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.DoubleInputItem)
            || CoordinateIsRuntimeRectGridBlockType(coordinate, RectGridBlockType.DoublePipeOutputItem);
    }

    public static bool IsInputEnergyBlockType(RectGridBlockType blockType)
    {
        return blockType == RectGridBlockType.InputEnergy
            || blockType == RectGridBlockType.PipeInputEnergy
            || blockType == RectGridBlockType.DoubleEnergy;
    }

    public static bool IsInputItemBlockType(RectGridBlockType blockType)
    {
        return blockType == RectGridBlockType.InputItem
            || blockType == RectGridBlockType.PipeInputItem
            || blockType == RectGridBlockType.DoubleInputItem;
    }

    public static bool IsOutputBlockType(RectGridBlockType blockType)
    {
        return blockType == RectGridBlockType.Output
            || blockType == RectGridBlockType.PipeOutputItem
            || blockType == RectGridBlockType.DoublePipeOutputItem;
    }

    public static bool IsInputOutputAreaBlockType(RectGridBlockType blockType)
    {
        return IsInputEnergyBlockType(blockType)
            || IsInputItemBlockType(blockType)
            || IsOutputBlockType(blockType)
            || blockType == RectGridBlockType.PipeInput;
    }

    public static bool AllowsDirectAreaInteraction(RectGridBlockType blockType)
    {
        return blockType == RectGridBlockType.InputEnergy
            || blockType == RectGridBlockType.InputItem
            || blockType == RectGridBlockType.Output
            || blockType == RectGridBlockType.DoubleEnergy
            || blockType == RectGridBlockType.DoubleInputItem
            || blockType == RectGridBlockType.DoublePipeOutputItem;
    }

    public static bool AllowsPipeAreaInteraction(RectGridBlockType blockType)
    {
        return blockType == RectGridBlockType.PipeInputEnergy
            || blockType == RectGridBlockType.PipeInputItem
            || blockType == RectGridBlockType.PipeOutputItem
            || blockType == RectGridBlockType.PipeInput
            || blockType == RectGridBlockType.DoubleEnergy
            || blockType == RectGridBlockType.DoubleInputItem
            || blockType == RectGridBlockType.DoublePipeOutputItem;
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

        float configuredCompleteEnergy = ItemDefinition.ResolveCompleteEnergyAmount(installedDefinition);
        if (configuredCompleteEnergy > 0.0001f)
        {
            return configuredCompleteEnergy;
        }

        return Mathf.Max(0.1f, fallbackCraftDuration)
               * ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition);
    }

    protected float ResolveInitialCraftDuration(ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return CraftDurationSeconds;
        }

        float energyRate = Mathf.Max(0.0001f, ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition));
        return Mathf.Max(0.1f, ResolveCompleteEnergy(installedDefinition) / energyRate);
    }

    private float ResolveRemainingEnergyCraftTime(ItemDefinition installedDefinition, float consumedEnergy)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return Mathf.Max(0f, remainingCraftTime);
        }

        float energyRate = Mathf.Max(0.0001f, ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition));
        float remainingEnergy = Mathf.Max(0f, ResolveCompleteEnergy(installedDefinition) - Mathf.Max(0f, consumedEnergy));
        return remainingEnergy / energyRate;
    }

    private float ResolveConsumedEnergyFromRemainingTime(ItemDefinition installedDefinition, float savedRemainingCraftTime)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return 0f;
        }

        float energyRate = Mathf.Max(0.0001f, ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition));
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

    protected virtual bool ShouldAnimateVirtualizedEnergyConsumption()
    {
        return false;
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

        bool showWorldEnergyGauge = ShouldShowWorldEnergyGauge(installedDefinition);
        UIManager uiManager = UIManager.Instance;
        if (uiManager == null)
        {
            return;
        }

        if (showWorldEnergyGauge)
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
        if (showWorldEnergyGauge)
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
            showWorldEnergyGauge
                ? new Vector2(0f, -Mathf.Max(0f, craftProgressGaugeCanvasVerticalOffset))
                : Vector2.zero);
    }

    protected virtual bool ShouldShowWorldEnergyGauge(ItemDefinition installedDefinition)
    {
        return RequiresOperationalEnergy(installedDefinition);
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

    private void UpdateCraftParticleEffectVisual()
    {
        if (!playParticleEffectWhileCrafting || particleEffect == null)
        {
            return;
        }

        if (!hasActiveCraft || waitingForOutput)
        {
            StopCraftParticleEffectVisual(false);
            return;
        }

        ApplyCraftParticleEffectSpeed(OperationalAnimationSpeedRatio);
        if (!particleEffect.isPlaying)
        {
            particleEffect.Play();
        }
    }

    protected virtual bool ShouldPlayWorkAnimation()
    {
        return IsActiveCraftRunning
               && !IsWaitingForOutput
               && HasOperationalEnergyAvailable(ResolveInstalledDefinition());
    }

    protected bool IsWorkAnimatorStateActive => lastWorkAnimatorState;

    protected virtual float ResolveWorkAnimationSpeedMultiplier()
    {
        return 1f;
    }

    protected void RefreshWorkAnimatorState(bool force = false)
    {
        SetWorkAnimatorState(ShouldPlayWorkAnimation(), force);
    }

    protected void SetWorkAnimatorState(bool isWorking, bool force = false)
    {
        Animator targetAnimator = ResolveWorkAnimator();
        if (targetAnimator == null)
        {
            workAnimatorStateInitialized = false;
            lastWorkAnimatorState = false;
            return;
        }

        targetAnimator.speed = isWorking
            ? OperationalAnimationSpeedRatio * Mathf.Max(0f, ResolveWorkAnimationSpeedMultiplier())
            : 1f;
        if (!HasWorkAnimatorBoolParameter(targetAnimator))
        {
            workAnimatorStateInitialized = false;
            lastWorkAnimatorState = isWorking;
            return;
        }

        if (!force && workAnimatorStateInitialized && lastWorkAnimatorState == isWorking)
        {
            return;
        }

        targetAnimator.SetBool(WorkAnimatorBoolHash, isWorking);
        workAnimatorStateInitialized = true;
        lastWorkAnimatorState = isWorking;
    }

    private Animator ResolveWorkAnimator()
    {
        Animator targetAnimator = ResolveInstallationAnimator();
        if (targetAnimator != cachedWorkAnimator)
        {
            cachedWorkAnimator = targetAnimator;
            hasCheckedWorkAnimatorParameter = false;
            workAnimatorHasWorkParameter = false;
            workAnimatorStateInitialized = false;
            lastWorkAnimatorState = false;
        }

        return cachedWorkAnimator;
    }

    private bool HasWorkAnimatorBoolParameter(Animator targetAnimator)
    {
        if (targetAnimator == null)
        {
            return false;
        }

        if (hasCheckedWorkAnimatorParameter)
        {
            return workAnimatorHasWorkParameter;
        }

        hasCheckedWorkAnimatorParameter = true;
        workAnimatorHasWorkParameter = false;

        AnimatorControllerParameter[] parameters = targetAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter != null
                && parameter.type == AnimatorControllerParameterType.Bool
                && parameter.nameHash == WorkAnimatorBoolHash)
            {
                workAnimatorHasWorkParameter = true;
                break;
            }
        }

        return workAnimatorHasWorkParameter;
    }

    private void ResetWorkAnimatorStateCache()
    {
        if (cachedWorkAnimator != null)
        {
            cachedWorkAnimator.speed = 1f;
        }

        cachedWorkAnimator = null;
        hasCheckedWorkAnimatorParameter = false;
        workAnimatorHasWorkParameter = false;
        workAnimatorStateInitialized = false;
        lastWorkAnimatorState = false;
    }

    private void StopCraftParticleEffectVisual(bool clearParticles)
    {
        if (!playParticleEffectWhileCrafting || particleEffect == null)
        {
            return;
        }

        ApplyCraftParticleEffectSpeed(1f);
        if (!particleEffect.isPlaying)
        {
            return;
        }

        particleEffect.Stop(
            true,
            clearParticles
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting);
    }

    private void ApplyCraftParticleEffectSpeed(float speedRatio)
    {
        if (particleEffect == null)
        {
            return;
        }

        ParticleSystem.MainModule main = particleEffect.main;
        main.simulationSpeed = Mathf.Max(0f, speedRatio);
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
        long placementSequence = RuntimePlacementSequence;
        if (energyGaugeWorldPositionResolved
            && cachedEnergyGaugePlacementSequence == placementSequence
            && transform.position == cachedEnergyGaugeTransformPosition
            && transform.rotation == cachedEnergyGaugeTransformRotation
            && transform.lossyScale == cachedEnergyGaugeTransformScale)
        {
            return cachedEnergyGaugeWorldPosition;
        }

        cachedEnergyGaugeWorldPosition = CalculateEnergyGaugeWorldPosition();
        cachedEnergyGaugePlacementSequence = placementSequence;
        cachedEnergyGaugeTransformPosition = transform.position;
        cachedEnergyGaugeTransformRotation = transform.rotation;
        cachedEnergyGaugeTransformScale = transform.lossyScale;
        energyGaugeWorldPositionResolved = true;
        return cachedEnergyGaugeWorldPosition;
    }

    private Vector3 CalculateEnergyGaugeWorldPosition()
    {
        Bounds bounds = default;
        bool hasBounds = false;
        IReadOnlyList<Renderer> renderers = ResolveEnergyGaugeRenderers();
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsEnergyGaugeBoundsRenderer(renderer)
                || !renderer.enabled
                || !renderer.gameObject.activeInHierarchy)
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
            if (IsEnergyGaugeBoundsRenderer(renderer))
            {
                cachedEnergyGaugeRenderers.Add(renderer);
            }
        }

        InvalidateEnergyGaugeWorldPosition();
        return cachedEnergyGaugeRenderers;
    }

    private static bool IsEnergyGaugeBoundsRenderer(Renderer renderer)
    {
        return renderer != null && !(renderer is ParticleSystemRenderer);
    }

    private void InvalidateEnergyGaugeWorldPosition()
    {
        energyGaugeWorldPositionResolved = false;
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
        lastOperationalEnergySupplyRatio = 1f;
        activeRecipeIndex = recipeIndex;
        activeOutputItemId = outputItemId;
        activeOutputCount = outputCount;
        WakeRuntimeUpdate();
    }

    protected int ActiveOutputItemId => activeOutputItemId;
    protected int ActiveOutputCount => activeOutputCount;
    protected bool IsActiveCraftRunning => hasActiveCraft;
    protected bool IsWaitingForOutput => waitingForOutput;
    protected bool HasRuntimeOutputCoordinates => runtimeOutputCoordinates != null && runtimeOutputCoordinates.Count > 0;
    protected IReadOnlyList<Vector2Int> RuntimeOutputCoordinates => runtimeOutputCoordinates;
    protected float InputConsumeMoveInterval => Mathf.Max(0f, inputConsumeMoveInterval);

    protected virtual bool IsRecipeOutputAllowedByItemFilter(int outputItemId)
    {
        return true;
    }

    protected bool IsRecipeOutputAvailable(int outputItemId)
    {
        return HasRequiredCraftingManual(outputItemId)
               && IsRecipeOutputAllowedByItemFilter(outputItemId);
    }

    protected static bool HasRequiredCraftingManual(int outputItemId)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        return itemManager != null && itemManager.IsManualRequirementSatisfied(outputItemId);
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
        lastOperationalEnergySupplyRatio = 1f;
        activeRecipeIndex = -1;
        activeOutputItemId = -1;
        activeOutputCount = 0;
        if (storedEnergy <= 0f)
        {
            energyGaugeCapacity = 0f;
        }
    }

    protected bool ContainsRuntimeInputItemArea(Vector2Int coordinate, int itemId)
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

    private void ExpandRuntimeInputItemAreasForAdditionalItemIds()
    {
        if (runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return;
        }

        List<int> itemIds = new List<int>();
        if (!TryCollectAdditionalRuntimeInputItemIds(itemIds) || itemIds.Count <= 0)
        {
            return;
        }

        List<Vector2Int> coordinates = new List<Vector2Int>();
        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            Vector2Int coordinate = runtimeInputItemAreas[i].coordinate;
            if (!coordinates.Contains(coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        for (int coordinateIndex = 0; coordinateIndex < coordinates.Count; coordinateIndex++)
        {
            Vector2Int coordinate = coordinates[coordinateIndex];
            for (int itemIndex = 0; itemIndex < itemIds.Count; itemIndex++)
            {
                int itemId = itemIds[itemIndex];
                if (itemId < 0 || ContainsRuntimeInputItemArea(coordinate, itemId))
                {
                    continue;
                }

                runtimeInputItemAreas.Add(new RuntimeInputItemArea(coordinate, itemId));
            }
        }
    }

    private bool ContainsRuntimeAreaCoordinate(Vector2Int coordinate)
    {
        return ContainsRuntimeInputItemAreaCoordinate(coordinate)
            || ContainsRuntimeInputEnergyCoordinate(coordinate)
            || ContainsRuntimeOutputCoordinate(coordinate)
            || ContainsRuntimePipeInputCoordinate(coordinate);
    }

    private bool ContainsRuntimePipeAreaBlockCoordinate(Vector2Int coordinate)
    {
        return ContainsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeInputEnergy)
            || ContainsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeInputItem)
            || ContainsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeOutputItem)
            || ContainsRuntimeRectGridBlockType(coordinate, RectGridBlockType.PipeInput)
            || ContainsRuntimeRectGridBlockType(coordinate, RectGridBlockType.DoubleEnergy)
            || ContainsRuntimeRectGridBlockType(coordinate, RectGridBlockType.DoubleInputItem)
            || ContainsRuntimeRectGridBlockType(coordinate, RectGridBlockType.DoublePipeOutputItem);
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
            RegisterRuntimeCoordinate(registeredRuntimeGridCoordinates, runtimeGridCoordinates[i], this);
        }
    }

    private void RegisterRuntimeAreaCoordinates()
    {
        RegisterRuntimeAreaCoordinates(runtimeInputEnergyCoordinates);
        RegisterRuntimeInputItemAreaCoordinates();
        RegisterRuntimeAreaCoordinates(runtimeOutputCoordinates);
        RegisterRuntimeAreaCoordinates(runtimePipeInputCoordinates);
    }

    private void RegisterRuntimeAreaCoordinates(IReadOnlyList<Vector2Int> coordinates)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            RegisterRuntimeCoordinate(registeredRuntimeAreaCoordinates, coordinates[i], this);
        }
    }

    private void RegisterRuntimeInputItemAreaCoordinates()
    {
        if (runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            RegisterRuntimeCoordinate(registeredRuntimeAreaCoordinates, runtimeInputItemAreas[i].coordinate, this);
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
            UnregisterRuntimeCoordinate(registeredRuntimeGridCoordinates, runtimeGridCoordinates[i], this);
        }
    }

    private void UnregisterRuntimeAreaCoordinates()
    {
        UnregisterRuntimeAreaCoordinates(runtimeInputEnergyCoordinates);
        UnregisterRuntimeInputItemAreaCoordinates();
        UnregisterRuntimeAreaCoordinates(runtimeOutputCoordinates);
        UnregisterRuntimeAreaCoordinates(runtimePipeInputCoordinates);
    }

    private void UnregisterRuntimeAreaCoordinates(IReadOnlyList<Vector2Int> coordinates)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            UnregisterRuntimeCoordinate(registeredRuntimeAreaCoordinates, coordinates[i], this);
        }
    }

    private void UnregisterRuntimeInputItemAreaCoordinates()
    {
        if (runtimeInputItemAreas == null || runtimeInputItemAreas.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < runtimeInputItemAreas.Count; i++)
        {
            UnregisterRuntimeCoordinate(registeredRuntimeAreaCoordinates, runtimeInputItemAreas[i].coordinate, this);
        }
    }

    private static void RegisterRuntimeCoordinate(
        Dictionary<Vector2Int, HashSet<InputOutputModule>> registry,
        Vector2Int coordinate,
        InputOutputModule module)
    {
        if (registry == null || module == null)
        {
            return;
        }

        if (!registry.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            || modules == null)
        {
            modules = new HashSet<InputOutputModule>();
            registry[coordinate] = modules;
        }

        if (modules.Add(module))
        {
            InvalidateFluidTopologyCache();
        }
    }

    private static void UnregisterRuntimeCoordinate(
        Dictionary<Vector2Int, HashSet<InputOutputModule>> registry,
        Vector2Int coordinate,
        InputOutputModule module)
    {
        if (registry == null || module == null)
        {
            return;
        }

        if (!registry.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            || modules == null)
        {
            return;
        }

        if (!modules.Remove(module))
        {
            return;
        }

        InvalidateFluidTopologyCache();
        if (modules.Count <= 0)
        {
            registry.Remove(coordinate);
        }
    }
#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        InputOutputModule resolvedParentModule = ResolveParentInputOutputModule();
        if (parentInputOutputModuleItem != null && resolvedParentModule == null)
        {
            Debug.LogWarning(
                $"InputOutputModule: '{name}'의 Parent IOModule Item은 InputOutputModule 아이템이어야 합니다.",
                this);
            parentInputOutputModuleItem = null;
        }
        else if (resolvedParentModule == this || HasCircularParentReference())
        {
            Debug.LogWarning(
                $"InputOutputModule: '{name}'의 Parent IOModule Item 순환 참조를 제거했습니다.",
                this);
            parentInputOutputModuleItem = null;
        }

        effectivePairDataInitialized = false;
        rectGridDataInitialized = false;
        rectGridPlacementDataInitialized = false;
        EnsureEffectivePairData();
        EnsureRectGridData();
        EnsureRectGridPlacementData();
    }
#endif
}
