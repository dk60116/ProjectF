using System;
using System.Collections.Generic;
using ProjectF.Simulation;
using UnityEngine;

public class InputOutputModule : InstallationObject,
    IMapObjectUpdateTick,
    IMapObjectUpdateTickInterval,
    IMapObjectStagedUpdateTick,
    IItemLightWorkStateProvider
{
    private const int EnergyTypeSlotCount = (int)ItemDefinition.EnergyType.PetroleumGas + 1;
    private const float MinimumFluidFuelBufferLiters = 50f;
    private const float FluidFuelBufferSeconds = 2f;

    [System.Flags]
    private enum PlannedModuleCommand
    {
        None = 0,
        PullFluid = 1 << 0,
        AdvanceCraft = 1 << 1,
        StartCraft = 1 << 2
    }
    public static event System.Action<InputOutputModule> RuntimePipeTopologyChanged;

    private ProjectF.FluidTransport.FluidOutputRateMeter fluidOutputRateMeter;

    protected void RecordFluidNetworkOutput(int fluidItemId, float acceptedLiters)
    {
        if (acceptedLiters <= 0f)
        {
            return;
        }

        fluidOutputRateMeter ??= new ProjectF.FluidTransport.FluidOutputRateMeter();
        fluidOutputRateMeter.Record(
            fluidItemId,
            acceptedLiters,
            MapObjectTickManager.CurrentSimulationTimeSeconds);
    }

    public float GetObjectInfoFluidOutputLitersPerSecond(int fluidItemId)
    {
        return isActiveAndEnabled && fluidOutputRateMeter != null
            ? fluidOutputRateMeter.GetLitersPerSecond(
                fluidItemId,
                MapObjectTickManager.CurrentSimulationTimeSeconds)
            : 0f;
    }

    public virtual float GetObjectInfoFluidPressureLitersPerSecond(int fluidItemId)
    {
        return GetObjectInfoFluidOutputLitersPerSecond(fluidItemId);
    }

    public static void AppendFluidOutputSourcesAtCoordinate(
        Vector2Int coordinate,
        Vector2Int directionToPipe,
        ISet<InputOutputModule> sources)
    {
        if (sources == null)
        {
            return;
        }

        if (!registeredRuntimeFluidOutputCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> modules))
        {
            return;
        }

        foreach (InputOutputModule module in modules)
        {
            if (module == null || module is Pump || !module.isActiveAndEnabled
                || !module.ContainsRuntimeOutputCoordinate(coordinate)
                || (directionToPipe != Vector2Int.zero
                    && (!module.TryGetRuntimePipeAreaExternalDirection(coordinate, out Vector2Int externalDirection)
                        || externalDirection != directionToPipe)))
            {
                continue;
            }

            sources.Add(module);
        }
    }

    internal static bool TryGetRuntimeFluidOutputDirectionAtCoordinate(
        Vector2Int coordinate,
        out Vector2Int directionToPipe)
    {
        directionToPipe = Vector2Int.zero;
        if (!registeredRuntimeFluidOutputCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> modules))
        {
            return false;
        }

        InputOutputModule selectedSource = null;
        foreach (InputOutputModule module in modules)
        {
            if (module == null
                || !module.isActiveAndEnabled
                || !module.ContainsRuntimeOutputCoordinate(coordinate)
                || !module.TryGetRuntimePipeAreaExternalDirection(
                    coordinate,
                    out Vector2Int candidateDirection)
                || candidateDirection == Vector2Int.zero)
            {
                continue;
            }

            if (selectedSource == null || CompareSimulationOrder(module, selectedSource) < 0)
            {
                selectedSource = module;
                directionToPipe = candidateDirection;
            }
        }

        return selectedSource != null;
    }

    internal static bool HasRuntimeFluidOutputTowardsPipe(
        Vector2Int coordinate,
        Vector2Int directionToPipe)
    {
        if (directionToPipe == Vector2Int.zero
            || !registeredRuntimeFluidOutputCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> modules))
        {
            return false;
        }

        foreach (InputOutputModule module in modules)
        {
            if (module != null
                && module.isActiveAndEnabled
                && module.ContainsRuntimeOutputCoordinate(coordinate)
                && module.TryGetRuntimePipeAreaExternalDirection(
                    coordinate,
                    out Vector2Int candidateDirection)
                && candidateDirection == directionToPipe)
            {
                return true;
            }
        }

        return false;
    }

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
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeFluidOutputCoordinates
        = new Dictionary<Vector2Int, HashSet<InputOutputModule>>();
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeFluidStorageCoordinates
        = new Dictionary<Vector2Int, HashSet<InputOutputModule>>();
    private static readonly HashSet<InputOutputModule> activeRuntimeModules
        = new HashSet<InputOutputModule>();
    private static readonly List<InputOutputModule> runtimeWakeScratch
        = new List<InputOutputModule>(16);
    private static readonly HashSet<InputOutputModule> runtimeWakeSet
        = new HashSet<InputOutputModule>();
    private static readonly HashSet<SteamGenerator> directedBoilerSteamChainGenerators
        = new HashSet<SteamGenerator>();
    private static readonly Dictionary<InstallationObject, HashSet<InputOutputModule>>
        registeredFluidInputSleepWaiters =
            new Dictionary<InstallationObject, HashSet<InputOutputModule>>();
    private static readonly Dictionary<InstallationObject, HashSet<InputOutputModule>>
        registeredFluidOutputSleepWaiters =
            new Dictionary<InstallationObject, HashSet<InputOutputModule>>();
    private static readonly Stack<HashSet<InputOutputModule>> fluidSleepWaiterSetPool =
        new Stack<HashSet<InputOutputModule>>();
    private static int fluidTopologyVersion = 1;
    private static int directedBoilerSteamChainTopologyVersion;
    private static long fluidTopologyInvalidationCount;
    private static long fluidPlacementInvalidationCount;
    private static long ignoredNonFluidPlacementChangeCount;
    private static int fluidInputSleepWaiterLinkCount;
    private static int fluidOutputSleepWaiterLinkCount;
    internal static int FluidTopologyVersion => fluidTopologyVersion;
    internal static int RuntimeFluidOutputCoordinateCount => registeredRuntimeFluidOutputCoordinates.Count;
    internal static int RuntimeFluidStorageCoordinateCount => registeredRuntimeFluidStorageCoordinates.Count;
    internal static long FluidTopologyInvalidationCount => fluidTopologyInvalidationCount;
    internal static long FluidPlacementInvalidationCount => fluidPlacementInvalidationCount;
    internal static long IgnoredNonFluidPlacementChangeCount => ignoredNonFluidPlacementChangeCount;
    internal static int FluidInputSleepWaiterLinkCount => fluidInputSleepWaiterLinkCount;
    internal static int FluidOutputSleepWaiterLinkCount => fluidOutputSleepWaiterLinkCount;

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

    internal virtual bool UsesDedicatedFluidStorageAtRuntimeCoordinate(Vector2Int coordinate) => false;

    internal virtual float GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(Vector2Int coordinate) => 0f;

    internal virtual float GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(Vector2Int coordinate) => 0f;

    internal virtual float GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(
        Vector2Int coordinate, int fluidItemId) =>
        GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(coordinate);

    internal virtual float GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(
        Vector2Int coordinate, int fluidItemId) =>
        GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(coordinate);

    internal virtual bool CanAcceptDedicatedFluidAtRuntimeCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float requestedLiters) => false;

    internal virtual bool TryAddDedicatedFluidAtRuntimeCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float requestedLiters,
        float temperatureCelsius,
        out float acceptedLiters)
    {
        acceptedLiters = 0f;
        return false;
    }

    protected virtual void AppendDedicatedFluidStorageRuntimeCoordinates(List<Vector2Int> coordinates)
    {
    }

    public override float FluidStorageCapacityLiters
    {
        get
        {
            float configuredCapacity = base.FluidStorageCapacityLiters;
            ItemDefinition definition = ResolveInstalledDefinition();
            if (!TryGetFluidFuelEnergyType(definition, out ItemDefinition.EnergyType energyType))
            {
                return configuredCapacity;
            }

            float fluidFuelUseRate = ItemDefinition.ResolveUseEnergyRatePerSecond(
                definition,
                energyType);
            return Mathf.Max(
                configuredCapacity,
                Mathf.Max(
                    MinimumFluidFuelBufferLiters,
                    fluidFuelUseRate * FluidFuelBufferSeconds));
        }
    }
    internal bool RequiresFacilityPowerEvaluation =>
        RequiresElectricOperationalEnergy() || this is SteamGenerator;

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
        [Min(0.0001f)] public float count;

        public ItemIoEntry(ItemDefinition itemDefinition, float count)
        {
            this.itemDefinition = itemDefinition;
            this.count = count;
        }

        public bool IsFluid => IsFluidItemDefinition(itemDefinition);
        public float ResolvedAmount => IsFluid
            ? Mathf.Max(0.0001f, count)
            : Mathf.Max(1, Mathf.RoundToInt(count));
        public int ResolvedItemCount => Mathf.Max(1, Mathf.RoundToInt(count));
    }

    [System.Serializable]
    public sealed class InputOutputPair
    {
        public List<ItemIoEntry> inputs = new List<ItemIoEntry>();
        public List<ItemIoEntry> outputs = new List<ItemIoEntry>();

        public InputOutputPair()
        {
        }

        public InputOutputPair(ItemIoEntry input, ItemIoEntry output)
        {
            inputs.Add(input);
            outputs.Add(output);
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
        public ItemDefinition itemDefinition;

        public RectGridBlockPlacement(
            int x,
            int y,
            RectGridBlockType blockType,
            ItemDefinition itemDefinition = null)
        {
            this.x = x;
            this.y = y;
            this.blockType = blockType;
            this.itemDefinition = itemDefinition;
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
        public bool hasDeterministicUnits;
        public long storedEnergyUnits;
        public long energyGaugeCapacityUnits;
        public List<int> storedEnergyTypes = new List<int>();
        public List<long> storedEnergyUnitsByType = new List<long>();
        public List<long> energyGaugeCapacityUnitsByType = new List<long>();
        public long remainingCraftTicks;
        public long activeCraftConsumedEnergyUnits;
        public long oilDrillingProgressUnits;
        public long seedPlanterPlantElapsedUnits;
        public bool seedPlanterHasLoadedSeed;
        public int seedPlanterLoadedSeedItemId = -1;
        public Vector2Int seedPlanterLoadedSeedInputCoordinate;
        public long seedPlanterTransferRemainingUnits;
        public List<int> refineryInputFluidItemIds = new List<int>();
        public List<long> refineryInputFluidUnits = new List<long>();
        public List<float> refineryInputFluidTemperatures = new List<float>();
        public List<int> productionInputFluidItemIds = new List<int>();
        public List<long> productionInputFluidUnits = new List<long>();
        // Legacy binary save slot; continuous sprinkler watering no longer uses a spray timer.
        public float sprinklerSprayElapsedSeconds;
        public float seedPlanterPlantElapsedSeconds;
        public bool steamGeneratorHasGenerationReserve;

        public void ClearStoredEnergyAndProduction()
        {
            storedEnergy = 0f;
            energyGaugeCapacity = 0f;
            storedEnergyUnits = 0L;
            energyGaugeCapacityUnits = 0L;
            storedEnergyTypes.Clear();
            storedEnergyUnitsByType.Clear();
            energyGaugeCapacityUnitsByType.Clear();
            hasActiveCraft = false;
            waitingForOutput = false;
            remainingCraftTime = 0f;
            remainingCraftTicks = 0L;
            activeCraftConsumedEnergy = 0f;
            activeCraftConsumedEnergyUnits = 0L;
            activeRecipeIndex = -1;
            activeOutputItemId = -1;
            activeOutputCount = 0;
            boilerSteamLiterAccumulator = 0f;
            oilDrillingProgressLiters = 0f;
            oilDrillingProgressUnits = 0L;
            sprinklerSprayElapsedSeconds = 0f;
            seedPlanterPlantElapsedSeconds = 0f;
            seedPlanterPlantElapsedUnits = 0L;
            seedPlanterHasLoadedSeed = false;
            seedPlanterLoadedSeedItemId = -1;
            seedPlanterLoadedSeedInputCoordinate = default;
            seedPlanterTransferRemainingUnits = 0L;
            refineryInputFluidItemIds.Clear();
            refineryInputFluidUnits.Clear();
            refineryInputFluidTemperatures.Clear();
            productionInputFluidItemIds.Clear();
            productionInputFluidUnits.Clear();
            steamGeneratorHasGenerationReserve = false;
            hasDeterministicUnits = true;
        }

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
                hasDeterministicUnits = hasDeterministicUnits,
                storedEnergyUnits = storedEnergyUnits,
                energyGaugeCapacityUnits = energyGaugeCapacityUnits,
                storedEnergyTypes = new List<int>(storedEnergyTypes ?? new List<int>()),
                storedEnergyUnitsByType = new List<long>(storedEnergyUnitsByType ?? new List<long>()),
                energyGaugeCapacityUnitsByType = new List<long>(energyGaugeCapacityUnitsByType ?? new List<long>()),
                remainingCraftTicks = remainingCraftTicks,
                activeCraftConsumedEnergyUnits = activeCraftConsumedEnergyUnits,
                oilDrillingProgressUnits = oilDrillingProgressUnits,
                seedPlanterPlantElapsedUnits = seedPlanterPlantElapsedUnits,
                seedPlanterHasLoadedSeed = seedPlanterHasLoadedSeed,
                seedPlanterLoadedSeedItemId = seedPlanterLoadedSeedItemId,
                seedPlanterLoadedSeedInputCoordinate = seedPlanterLoadedSeedInputCoordinate,
                seedPlanterTransferRemainingUnits = seedPlanterTransferRemainingUnits,
                refineryInputFluidItemIds = new List<int>(refineryInputFluidItemIds ?? new List<int>()),
                refineryInputFluidUnits = new List<long>(refineryInputFluidUnits ?? new List<long>()),
                refineryInputFluidTemperatures = new List<float>(refineryInputFluidTemperatures ?? new List<float>()),
                productionInputFluidItemIds = new List<int>(productionInputFluidItemIds ?? new List<int>()),
                productionInputFluidUnits = new List<long>(productionInputFluidUnits ?? new List<long>()),
                sprinklerSprayElapsedSeconds = sprinklerSprayElapsedSeconds,
                seedPlanterPlantElapsedSeconds = seedPlanterPlantElapsedSeconds,
                steamGeneratorHasGenerationReserve = steamGeneratorHasGenerationReserve
            };
        }

        public long ResolveOilDrillingProgressUnits()
        {
            return hasDeterministicUnits
                ? System.Math.Max(0L, oilDrillingProgressUnits)
                : DeterministicSimulationUnits.FromFloat(oilDrillingProgressLiters);
        }
    }

    [SerializeField]
    private ItemDefinition parentInputOutputModuleItem;
    [SerializeField]
    private List<InputOutputPair> inputOutputPairs = new List<InputOutputPair>();
    [SerializeField, HideInInspector]
    private List<ItemIoEntry> inputList = new List<ItemIoEntry>();
    [SerializeField, HideInInspector]
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
    protected IReadOnlyList<Vector2Int> RuntimePipeInputCoordinates => runtimePipeInputCoordinates;
    [SerializeField]
    private List<Vector2Int> runtimeGridCoordinates = new List<Vector2Int>();
    [SerializeField]
    private List<Vector2Int> runtimeFocusCoordinates = new List<Vector2Int>();
    [SerializeField]
    private long storedEnergyUnits;
    [SerializeField]
    private long energyGaugeCapacityUnits;
    private readonly long[] secondaryStoredEnergyUnitsByType =
        new long[EnergyTypeSlotCount];
    private readonly long[] secondaryEnergyGaugeCapacityUnitsByType =
        new long[EnergyTypeSlotCount];
    // Runtime fields are consolidated here. PersistentState remains the explicit file-format boundary.
    [SerializeField]
    private ProjectF.Simulation.ProductionProcess production = ProjectF.Simulation.ProductionProcess.Empty;
    private bool hasActiveCraft { get => production.Active; set => production.Active = value; }
    private bool waitingForOutput { get => production.WaitingForOutput; set => production.WaitingForOutput = value; }
    private long remainingCraftTicks { get => production.RemainingTicks; set => production.RemainingTicks = value; }
    private long activeCraftConsumedEnergyUnits { get => production.ConsumedEnergyUnits; set => production.ConsumedEnergyUnits = value; }
    private int activeRecipeIndex { get => production.RecipeIndex; set => production.RecipeIndex = value; }
    private int activeOutputItemId { get => production.OutputItemId; set => production.OutputItemId = value; }
    private int activeOutputCount { get => production.OutputCount; set => production.OutputCount = value; }

    private TerrainGenerator cachedTerrain;
    private BlockStateStore cachedBlockStateStore;
    private ItemDefinition cachedInstalledDefinition;
    private int cachedInstalledDefinitionId = int.MinValue;
    private ItemManager cachedFluidFuelItemManager;
    private int cachedFluidFuelDefinitionCount = -1;
    private readonly int[] cachedFluidFuelItemIdsByType = new int[EnergyTypeSlotCount];
    private DefaultGauge activeEnergyGauge;
    private DefaultGauge activeCraftProgressGauge;
    private readonly List<Renderer> cachedEnergyGaugeRenderers = new List<Renderer>();
    private readonly List<Vector2Int> objectInfoInputAreaCoordinates = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> singleItemOutputVisitedCoordinates = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> runtimeAreaVisitedCoordinates = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> runtimeFluidOutputIndexCoordinates = new List<Vector2Int>(4);
    private readonly List<Vector2Int> runtimeFluidStorageIndexCoordinates = new List<Vector2Int>(4);
    private readonly HashSet<int> runtimeFluidOutputItemIdScratch = new HashSet<int>();
    private readonly struct ConnectedFluidSearchNode
    {
        public readonly Vector2Int Coordinate;
        public readonly int PipeCount;

        public ConnectedFluidSearchNode(Vector2Int coordinate, int pipeCount)
        {
            Coordinate = coordinate;
            PipeCount = pipeCount;
        }
    }

    private readonly struct DirectedSteamPort : IEquatable<DirectedSteamPort>
    {
        public readonly Vector2Int Coordinate;
        public readonly Vector2Int FlowDirection;

        public DirectedSteamPort(Vector2Int coordinate, Vector2Int flowDirection)
        {
            Coordinate = coordinate;
            FlowDirection = flowDirection;
        }

        public bool Equals(DirectedSteamPort other) =>
            Coordinate == other.Coordinate && FlowDirection == other.FlowDirection;

        public override bool Equals(object obj) => obj is DirectedSteamPort other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Coordinate.GetHashCode() * 397) ^ FlowDirection.GetHashCode();
            }
        }
    }

    private readonly Queue<ConnectedFluidSearchNode> connectedFluidSearchQueue =
        new Queue<ConnectedFluidSearchNode>(32);
    private readonly Dictionary<Vector2Int, int> connectedFluidSearchPipeCounts =
        new Dictionary<Vector2Int, int>();
    private List<RuntimePumpPipePass> connectedFluidPumpPassScratch;
    private List<Pump> connectedFluidInterlockedPumpScratch;
    private readonly HashSet<InstallationObject> connectedFluidStorageCandidates = new HashSet<InstallationObject>();
    private readonly List<InstallationObject> fluidStorageBodyScratch = new List<InstallationObject>(4);
    private readonly HashSet<SteamGenerator> directedSteamChainVisited = new HashSet<SteamGenerator>();
    private readonly Queue<DirectedSteamPort> directedSteamPortSearchQueue =
        new Queue<DirectedSteamPort>(8);
    private readonly HashSet<DirectedSteamPort> directedSteamVisitedPorts =
        new HashSet<DirectedSteamPort>();
    private readonly Queue<Vector2Int> directedSteamPipeSearchQueue = new Queue<Vector2Int>(32);
    private readonly HashSet<Vector2Int> directedSteamVisitedPipeCoordinates =
        new HashSet<Vector2Int>();
    private readonly List<Vector2Int> connectedFluidSeedCoordinates = new List<Vector2Int>(8);
    private readonly List<Vector2Int> connectedFluidSeedCoordinateScratch = new List<Vector2Int>(8);
    private readonly Dictionary<Vector2Int, Pump> connectedFluidSearchPumps = new Dictionary<Vector2Int, Pump>();
    private readonly Dictionary<InstallationObject, Pump> connectedFluidSourcePumps = new Dictionary<InstallationObject, Pump>();
    private Pump connectedFluidSearchCurrentPump;
    private readonly List<InstallationObject> cachedConnectedFluidSourceStorages = new List<InstallationObject>(8);
    private readonly List<InstallationObject> registeredFluidInputSleepStorages =
        new List<InstallationObject>(4);
    private readonly Dictionary<InstallationObject, int> cachedConnectedFluidSourcePipeDistances =
        new Dictionary<InstallationObject, int>();
    private int cachedConnectedFluidSourceStoragesTopologyVersion;
    private readonly List<FluidOutputConnection> cachedFluidOutputConnections =
        new List<FluidOutputConnection>(8);
    private readonly List<InstallationObject> registeredFluidOutputSleepStorages =
        new List<InstallationObject>(4);
    private readonly Dictionary<FluidStorageEndpointKey, int> cachedFluidOutputConnectionIndices =
        new Dictionary<FluidStorageEndpointKey, int>();
    private readonly List<Vector2Int> cachedFluidOutputSeedCoordinates = new List<Vector2Int>(4);
    private int cachedFluidOutputConnectionsTopologyVersion;
    private InstallationObject cachedConnectedFluidSource;
    private int cachedConnectedFluidSourceItemId = int.MinValue;
    private int cachedConnectedFluidSourceTopologyVersion;
    private InstallationObject cachedFluidOutputStorage;
    private int cachedFluidOutputItemId = int.MinValue;
    private int cachedFluidOutputTopologyVersion;
    private int connectedFluidSearchCurrentPipeCount;
    private sealed class FluidPortConnectionCache
    {
        public int TopologyVersion = -1;
        public readonly List<FluidOutputConnection> Connections = new List<FluidOutputConnection>(4);
    }

    private readonly Dictionary<Vector2Int, FluidPortConnectionCache> fluidInputPortConnectionCaches =
        new Dictionary<Vector2Int, FluidPortConnectionCache>();
    private readonly Dictionary<Vector2Int, FluidPortConnectionCache> fluidOutputPortConnectionCaches =
        new Dictionary<Vector2Int, FluidPortConnectionCache>();
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
    private InputOutputModuleAreaMarkerController cachedAreaMarkerController;
    private bool areaMarkerControllerResolved;
    private bool runtimeSleeping;
    private bool fluidOutputCapacityBlocked;
    private readonly List<ItemIoEntry> localInputList = new List<ItemIoEntry>();
    private readonly List<ItemIoEntry> localOutputList = new List<ItemIoEntry>();
    private readonly List<InputOutputPair> effectiveInputOutputPairs = new List<InputOutputPair>();
    private readonly List<ItemIoEntry> effectiveInputList = new List<ItemIoEntry>();
    private readonly List<ItemIoEntry> effectiveOutputList = new List<ItemIoEntry>();
    private bool effectivePairDataInitialized;
    private PlannedModuleCommand plannedModuleCommands;
    private float plannedModuleDeltaTime;
    private bool stagedModuleTickPlanned;
    private bool managedRuntimeVisualsDirty;
    private bool outputDrainCheckPending;
    private bool hasStoredOutputOnConveyor;

    public ItemDefinition ParentInputOutputModuleItem => parentInputOutputModuleItem;

    public IReadOnlyList<InputOutputPair> LocalInputOutputPairs
    {
        get
        {
            EnsurePairData();
            return inputOutputPairs;
        }
    }

    public IReadOnlyList<InputOutputPair> InputOutputPairs
    {
        get
        {
            EnsureEffectivePairData();
            return effectiveInputOutputPairs;
        }
    }

    public IReadOnlyList<ItemIoEntry> LocalInputList
    {
        get
        {
            EnsurePairData();
            return localInputList;
        }
    }

    public IReadOnlyList<ItemIoEntry> LocalOutputList
    {
        get
        {
            EnsurePairData();
            return localOutputList;
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
        bool hadRuntimePipeInputs = runtimePipeInputCoordinates.Count > 0;
        UnregisterRuntimeFluidSpatialCoordinates();
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
        RegisterRuntimeFluidSpatialCoordinates();
        cachedTerrain = null;
        cachedBlockStateStore = null;
        WakeRuntimeUpdate();
        if (hadRuntimePipeInputs || runtimePipeInputCoordinates.Count > 0)
        {
            // PlacementRuntimeChanged is raised before the world-space area
            // coordinates are configured. Invalidate again after registration
            // so pipe display groups can discover newly installed pass-throughs.
            NotifyRuntimePipeTopologyChanged(runtimePipeInputCoordinates);
        }
    }

    public void ConfigureRuntimeGridCoordinates(IReadOnlyList<Vector2Int> coordinates)
    {
        UnregisterRuntimeFluidSpatialCoordinates();
        UnregisterRuntimeGridCoordinates();
        runtimeGridCoordinates.Clear();

        AddUniqueCoordinates(coordinates, runtimeGridCoordinates);
        RegisterRuntimeGridCoordinates();
        RegisterRuntimeFluidSpatialCoordinates();
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
            hasDeterministicUnits = true,
            storedEnergy = DeterministicSimulationUnits.ToFloat(storedEnergyUnits),
            storedEnergyUnits = storedEnergyUnits,
            energyGaugeCapacity = DeterministicSimulationUnits.ToFloat(energyGaugeCapacityUnits),
            energyGaugeCapacityUnits = energyGaugeCapacityUnits,
            hasActiveCraft = hasActiveCraft,
            waitingForOutput = waitingForOutput,
            remainingCraftTime = DeterministicSimulationUnits.TicksToSeconds(remainingCraftTicks),
            remainingCraftTicks = remainingCraftTicks,
            activeCraftConsumedEnergy = DeterministicSimulationUnits.ToFloat(activeCraftConsumedEnergyUnits),
            activeCraftConsumedEnergyUnits = activeCraftConsumedEnergyUnits,
            activeRecipeIndex = activeRecipeIndex,
            activeOutputItemId = activeOutputItemId,
            activeOutputCount = activeOutputCount
        };

        for (int typeIndex = 1; typeIndex < secondaryStoredEnergyUnitsByType.Length; typeIndex++)
        {
            long storedUnits = Math.Max(0L, secondaryStoredEnergyUnitsByType[typeIndex]);
            long gaugeUnits = Math.Max(0L, secondaryEnergyGaugeCapacityUnitsByType[typeIndex]);
            if (storedUnits <= 0L && gaugeUnits <= 0L)
            {
                continue;
            }

            state.storedEnergyTypes.Add(typeIndex);
            state.storedEnergyUnitsByType.Add(storedUnits);
            state.energyGaugeCapacityUnitsByType.Add(Math.Max(gaugeUnits, storedUnits));
        }

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

        UnregisterRuntimeFluidSpatialCoordinates();
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

        storedEnergyUnits = state.hasDeterministicUnits
            ? System.Math.Max(0L, state.storedEnergyUnits)
            : DeterministicSimulationUnits.FromFloat(state.storedEnergy);
        energyGaugeCapacityUnits = state.hasDeterministicUnits
            ? System.Math.Max(0L, state.energyGaugeCapacityUnits)
            : DeterministicSimulationUnits.FromFloat(state.energyGaugeCapacity);
        Array.Clear(secondaryStoredEnergyUnitsByType, 0, secondaryStoredEnergyUnitsByType.Length);
        Array.Clear(secondaryEnergyGaugeCapacityUnitsByType, 0, secondaryEnergyGaugeCapacityUnitsByType.Length);
        int secondaryCount = Math.Min(
            state.storedEnergyTypes?.Count ?? 0,
            Math.Min(
                state.storedEnergyUnitsByType?.Count ?? 0,
                state.energyGaugeCapacityUnitsByType?.Count ?? 0));
        for (int i = 0; i < secondaryCount; i++)
        {
            int typeIndex = state.storedEnergyTypes[i];
            if (typeIndex <= 0 || typeIndex >= secondaryStoredEnergyUnitsByType.Length)
            {
                continue;
            }

            secondaryStoredEnergyUnitsByType[typeIndex] = Math.Max(0L, state.storedEnergyUnitsByType[i]);
            secondaryEnergyGaugeCapacityUnitsByType[typeIndex] = Math.Max(
                secondaryStoredEnergyUnitsByType[typeIndex],
                state.energyGaugeCapacityUnitsByType[i]);
        }
        hasActiveCraft = state.hasActiveCraft;
        waitingForOutput = state.waitingForOutput;
        remainingCraftTicks = state.hasDeterministicUnits
            ? System.Math.Max(0L, state.remainingCraftTicks)
            : DeterministicSimulationUnits.SecondsToTicks(state.remainingCraftTime);
        activeCraftConsumedEnergyUnits = state.hasDeterministicUnits
            ? System.Math.Max(0L, state.activeCraftConsumedEnergyUnits)
            : DeterministicSimulationUnits.FromFloat(state.activeCraftConsumedEnergy);
        if (hasActiveCraft && !waitingForOutput && activeCraftConsumedEnergyUnits <= 0L)
        {
            activeCraftConsumedEnergyUnits = ResolveConsumedEnergyUnitsFromRemainingTicks(
                ResolveInstalledDefinition(),
                remainingCraftTicks);
        }
        activeRecipeIndex = state.activeRecipeIndex;
        activeOutputItemId = state.activeOutputItemId;
        activeOutputCount = Mathf.Max(0, state.activeOutputCount);
        cachedTerrain = null;
        cachedBlockStateStore = null;
        MarkManagedRuntimeVisualsDirty();
        WakeRuntimeUpdate();
    }

    public void ClearStoredEnergyAndProduction()
    {
        PersistentState state = CapturePersistentState();
        state.ClearStoredEnergyAndProduction();
        plannedModuleCommands = PlannedModuleCommand.None;
        stagedModuleTickPlanned = false;
        ApplyPersistentState(state);
        SetWorkAnimatorState(false, true);
    }

    public override void PrepareForPool()
    {
        fluidOutputRateMeter?.Reset();
        FacilitySimulationWorld.Unregister(this);
        UnregisterFluidSleepWaiters();
        runtimeSleeping = false;
        fluidOutputCapacityBlocked = false;
        managedRuntimeVisualsDirty = false;
        UnregisterRuntimeFluidSpatialCoordinates();
        UnregisterRuntimeGridCoordinates();
        UnregisterRuntimeAreaCoordinates();
        ReleaseEnergyGaugeVisual();
        runtimeInputEnergyCoordinates.Clear();
        runtimeInputItemAreas.Clear();
        runtimeOutputCoordinates.Clear();
        runtimePipeInputCoordinates.Clear();
        runtimeGridCoordinates.Clear();
        runtimeFocusCoordinates.Clear();
        storedEnergyUnits = 0L;
        energyGaugeCapacityUnits = 0L;
        Array.Clear(secondaryStoredEnergyUnitsByType, 0, secondaryStoredEnergyUnitsByType.Length);
        Array.Clear(secondaryEnergyGaugeCapacityUnitsByType, 0, secondaryEnergyGaugeCapacityUnitsByType.Length);
        hasActiveCraft = false;
        waitingForOutput = false;
        remainingCraftTicks = 0L;
        activeCraftConsumedEnergyUnits = 0L;
        lastOperationalEnergySupplyRatio = 1f;
        ResetWorkAnimatorStateCache();
        activeRecipeIndex = -1;
        activeOutputItemId = -1;
        activeOutputCount = 0;
        cachedTerrain = null;
        cachedBlockStateStore = null;
        cachedInstalledDefinition = null;
        cachedInstalledDefinitionId = int.MinValue;
        cachedFluidFuelItemManager = null;
        cachedFluidFuelDefinitionCount = -1;
        Array.Fill(cachedFluidFuelItemIdsByType, -1);
        connectedFluidSeedCoordinates.Clear();
        connectedFluidSeedCoordinateScratch.Clear();
        cachedFluidOutputSeedCoordinates.Clear();
        fluidInputPortConnectionCaches.Clear();
        fluidOutputPortConnectionCaches.Clear();
        ReleaseFacilityFlowState();
        base.PrepareForPool();
    }

    private void ReleaseFacilityFlowState()
    {
        if (this is IFacilityFlowStateOwner stateOwner)
        {
            stateOwner.ReleaseFacilityFlowState();
        }
    }

    private readonly struct FluidOutputConnection
    {
        public readonly InstallationObject Storage;
        public readonly Vector2Int Coordinate;
        public readonly int PipeDistance;
        public readonly Pump PressurePump;

        public FluidOutputConnection(
            InstallationObject storage,
            Vector2Int coordinate,
            int pipeDistance,
            Pump pressurePump = null)
        {
            PressurePump = pressurePump;
            Storage = storage;
            Coordinate = coordinate;
            PipeDistance = pipeDistance;
        }
    }

    private readonly struct FluidStorageEndpointKey : IEquatable<FluidStorageEndpointKey>
    {
        public readonly InstallationObject Storage;
        public readonly Vector2Int Coordinate;

        public FluidStorageEndpointKey(InstallationObject storage, Vector2Int coordinate)
        {
            Storage = storage;
            Coordinate = coordinate;
        }

        public bool Equals(FluidStorageEndpointKey other) =>
            Storage == other.Storage && Coordinate == other.Coordinate;

        public override bool Equals(object obj) =>
            obj is FluidStorageEndpointKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Storage != null ? Storage.GetHashCode() : 0) * 397)
                       ^ Coordinate.GetHashCode();
            }
        }
    }

    private void EnsureFacilityFlowState()
    {
        if (this is IFacilityFlowStateOwner stateOwner)
        {
            stateOwner.EnsureFacilityFlowState();
        }
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

            if (module == null || CompareSimulationOrder(candidate, module) < 0)
            {
                module = candidate;
            }
        }

        return module != null;
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

            if (module == null || CompareSimulationOrder(candidate, module) < 0)
            {
                module = candidate;
            }
        }

        return module != null;
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

            if (module == null || CompareSimulationOrder(candidate, module) < 0)
            {
                module = candidate;
            }
        }

        return module != null;
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

    internal static bool TryGetSteamGeneratorPipePassAtRuntimeCoordinate(
        Vector2Int coordinate,
        out SteamGenerator generator,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        generator = null;
        otherCoordinate = default;
        externalDirection = default;
        SelectSteamGeneratorPipePass(
            registeredRuntimeAreaCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> areaModules)
                ? areaModules
                : null,
            coordinate,
            ref generator,
            ref otherCoordinate,
            ref externalDirection);
        SelectSteamGeneratorPipePass(
            registeredRuntimeGridCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> gridModules)
                ? gridModules
                : null,
            coordinate,
            ref generator,
            ref otherCoordinate,
            ref externalDirection);
        return generator != null;
    }

    internal static bool TryGetPumpPipePassAtRuntimeCoordinate(
        Vector2Int coordinate,
        out Pump pump,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        pump = null;
        otherCoordinate = default;
        externalDirection = default;
        SelectPumpPipePass(
            registeredRuntimeAreaCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> areaModules)
                ? areaModules
                : null,
            coordinate,
            ref pump,
            ref otherCoordinate,
            ref externalDirection);
        SelectPumpPipePass(
            registeredRuntimeGridCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> gridModules)
                ? gridModules
                : null,
            coordinate,
            ref pump,
            ref otherCoordinate,
            ref externalDirection);
        return pump != null;
    }

    internal static bool TryGetPassiveFluidPassAtRuntimeCoordinate(
        Vector2Int coordinate,
        out InputOutputModule passOwner,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        passOwner = null;
        otherCoordinate = default;
        externalDirection = default;
        SelectPassiveFluidPass(
            registeredRuntimeAreaCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> areaModules)
                ? areaModules
                : null,
            coordinate,
            ref passOwner,
            ref otherCoordinate,
            ref externalDirection);
        SelectPassiveFluidPass(
            registeredRuntimeGridCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> gridModules)
                ? gridModules
                : null,
            coordinate,
            ref passOwner,
            ref otherCoordinate,
            ref externalDirection);
        return passOwner != null;
    }

    internal readonly struct RuntimePumpPipePass
    {
        public readonly Pump Pump;
        public readonly Vector2Int OtherCoordinate;
        public readonly Vector2Int ExternalDirection;

        public RuntimePumpPipePass(
            Pump pump,
            Vector2Int otherCoordinate,
            Vector2Int externalDirection)
        {
            Pump = pump;
            OtherCoordinate = otherCoordinate;
            ExternalDirection = externalDirection;
        }
    }

    internal static bool HasRuntimeFluidInputFacingAt(Vector2Int coordinate, Vector2Int direction)
    {
        if (!registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules))
        {
            return false;
        }

        foreach (InputOutputModule module in modules)
        {
            if (module == null || !module.isActiveAndEnabled
                || !module.TryGetPlacementRuntime(out Vector2Int anchor, out int quarterTurns)
                || !module.TryGetRectGridBlockTypeAtCoordinate(module, anchor, quarterTurns,
                    coordinate, out RectGridBlockType blockType)
                || !AllowsPipeAreaInteraction(blockType)
                || !(IsInputItemBlockType(blockType) || IsInputEnergyBlockType(blockType)
                     || blockType == RectGridBlockType.PipeInput))
            {
                continue;
            }

            if (module.TryGetNearestRectGridObjectDirection(module, anchor, quarterTurns,
                    coordinate, out Vector2Int inwardDirection)
                && inwardDirection == direction)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool CollectPumpPipePassesAtRuntimeCoordinate(
        Vector2Int coordinate,
        List<RuntimePumpPipePass> results)
    {
        if (results == null)
        {
            return false;
        }

        int initialCount = results.Count;
        AppendPumpPipePasses(
            registeredRuntimeAreaCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> areaModules)
                ? areaModules
                : null,
            coordinate,
            results);
        AppendPumpPipePasses(
            registeredRuntimeGridCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> gridModules)
                ? gridModules
                : null,
            coordinate,
            results);
        return results.Count > initialCount;
    }

    private static void AppendPumpPipePasses(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        List<RuntimePumpPipePass> results)
    {
        if (modules == null)
        {
            return;
        }

        foreach (InputOutputModule module in modules)
        {
            if (!(module is Pump pump)
                || !pump.gameObject.activeInHierarchy
                || !pump.TryGetRuntimePipePass(
                    coordinate,
                    out Vector2Int otherCoordinate,
                    out Vector2Int externalDirection))
            {
                continue;
            }

            bool alreadyAdded = false;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Pump != pump)
                {
                    continue;
                }

                alreadyAdded = true;
                break;
            }

            if (!alreadyAdded)
            {
                results.Add(new RuntimePumpPipePass(pump, otherCoordinate, externalDirection));
            }
        }
    }

    internal static bool TryGetOverlappingSteamSourcePort(
        SteamGenerator generator,
        out Vector2Int sourceCoordinate)
    {
        sourceCoordinate = default;
        if (generator == null
            || !generator.TryGetPlacementRuntime(out Vector2Int anchor, out int quarterTurns)
            || !generator.TryGetInputCoordinateAndDirection(
                generator, anchor, quarterTurns, out Vector2Int input, out Vector2Int flowDirection)
            || input != anchor - flowDirection
            || !generator.CanReceiveSteamFromDirectedPortAtRuntime(anchor, flowDirection))
        {
            return false;
        }

        // Dense placement puts the upstream outlet at this generator's anchor,
        // one cell inside its nominal inlet. Use the same directed connection
        // rule as boiler delivery; a body cell alone is never a fluid edge.
        if (registeredRuntimeFluidOutputCoordinates.TryGetValue(anchor, out var outputs))
        {
            foreach (InputOutputModule output in outputs)
            {
                if (output is Boiler boiler && boiler.isActiveAndEnabled
                    && boiler.TryGetRuntimePipeOutputExternalDirection(anchor, out Vector2Int direction)
                    && direction == flowDirection)
                {
                    sourceCoordinate = anchor;
                    return true;
                }
            }
        }

        if (registeredRuntimeAreaCoordinates.TryGetValue(anchor, out var modules))
        {
            foreach (InputOutputModule module in modules)
            {
                if (module is SteamGenerator upstream && upstream != generator
                    && upstream.isActiveAndEnabled
                    && upstream.TryGetRuntimePipePassTail(out Vector2Int tail, out Vector2Int direction)
                    && tail == anchor && direction == flowDirection)
                {
                    sourceCoordinate = tail;
                    return true;
                }
            }
        }

        return false;
    }

    private static void SelectSteamGeneratorPipePass(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        ref SteamGenerator bestGenerator,
        ref Vector2Int otherCoordinate,
        ref Vector2Int externalDirection)
    {
        if (modules == null)
        {
            return;
        }

        foreach (InputOutputModule module in modules)
        {
            if (!(module is SteamGenerator candidate)
                || !candidate.gameObject.activeInHierarchy
                || !candidate.TryGetRuntimeSteamPass(
                    coordinate,
                    out Vector2Int candidateOtherCoordinate,
                    out Vector2Int candidateExternalDirection)
                || bestGenerator != null
                && CompareSimulationOrder(candidate, bestGenerator) >= 0)
            {
                continue;
            }

            bestGenerator = candidate;
            otherCoordinate = candidateOtherCoordinate;
            externalDirection = candidateExternalDirection;
        }
    }

    private static void SelectPumpPipePass(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        ref Pump bestPump,
        ref Vector2Int otherCoordinate,
        ref Vector2Int externalDirection)
    {
        if (modules == null)
        {
            return;
        }

        foreach (InputOutputModule module in modules)
        {
            if (!(module is Pump candidate)
                || !candidate.gameObject.activeInHierarchy
                || !candidate.TryGetRuntimePipePass(
                    coordinate,
                    out Vector2Int candidateOtherCoordinate,
                    out Vector2Int candidateExternalDirection)
                || bestPump != null && CompareSimulationOrder(candidate, bestPump) >= 0)
            {
                continue;
            }

            bestPump = candidate;
            otherCoordinate = candidateOtherCoordinate;
            externalDirection = candidateExternalDirection;
        }
    }

    private static void SelectPassiveFluidPass(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        ref InputOutputModule bestPassOwner,
        ref Vector2Int otherCoordinate,
        ref Vector2Int externalDirection)
    {
        if (modules == null)
        {
            return;
        }

        foreach (InputOutputModule candidate in modules)
        {
            if (candidate == null
                || !candidate.gameObject.activeInHierarchy
                || !candidate.TryGetRuntimePassiveFluidPass(
                    coordinate,
                    out Vector2Int candidateOtherCoordinate,
                    out Vector2Int candidateExternalDirection)
                || bestPassOwner != null
                && CompareSimulationOrder(candidate, bestPassOwner) >= 0)
            {
                continue;
            }

            bestPassOwner = candidate;
            otherCoordinate = candidateOtherCoordinate;
            externalDirection = candidateExternalDirection;
        }
    }

    public static void WakeRuntimeModulesAtCoordinate(Vector2Int coordinate)
    {
        WakeRuntimeModulesAtCoordinate(coordinate, false);
    }

    public static void WakeRuntimeOutputModulesAtCoordinate(Vector2Int coordinate)
    {
        WakeRuntimeModulesAtCoordinate(coordinate, true);
    }

    private static void WakeRuntimeModulesAtCoordinate(Vector2Int coordinate, bool outputOnly)
    {
        runtimeWakeScratch.Clear();
        runtimeWakeSet.Clear();
        CollectRuntimeModulesAtCoordinate(coordinate, outputOnly);
        WakeCollectedRuntimeModules();
    }

    internal static void WakeRuntimeModulesForChangedBlocks(IReadOnlyList<Block> changedBlocks)
    {
        runtimeWakeScratch.Clear();
        runtimeWakeSet.Clear();
        for (int i = 0; changedBlocks != null && i < changedBlocks.Count; i++)
        {
            Block block = changedBlocks[i];
            if (block != null) CollectRuntimeModulesAtCoordinate(block.Coordinate, false);
        }

        WakeCollectedRuntimeModules();
    }

    private static void CollectRuntimeModulesAtCoordinate(Vector2Int coordinate, bool outputOnly)
    {
        if (registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            && modules != null
            && modules.Count > 0)
        {
            foreach (InputOutputModule module in modules)
            {
                if (module == null
                    || !module.gameObject.activeInHierarchy
                    || !module.ContainsRuntimeAreaCoordinate(coordinate))
                {
                    continue;
                }

                bool isOutputCoordinate = module.ContainsRuntimeOutputCoordinate(coordinate);
                if (isOutputCoordinate && (!outputOnly || module.hasStoredOutputOnConveyor))
                {
                    module.outputDrainCheckPending = true;
                }

                if (!module.runtimeSleeping
                    || (outputOnly && !isOutputCoordinate)
                    || !runtimeWakeSet.Add(module))
                {
                    continue;
                }

                runtimeWakeScratch.Add(module);
            }
        }
    }

    private static void WakeCollectedRuntimeModules()
    {
        for (int i = 0; i < runtimeWakeScratch.Count; i++)
        {
            runtimeWakeScratch[i]?.WakeRuntimeUpdate();
        }

        runtimeWakeScratch.Clear();
        runtimeWakeSet.Clear();
    }

    public static void WakeElectricRuntimeModules()
    {
        WakeElectricRuntimeModules(out _);
    }

    internal static int WakeElectricRuntimeModules(out int candidateCount)
    {
        candidateCount = activeRuntimeModules.Count;
        runtimeWakeScratch.Clear();
        foreach (InputOutputModule module in activeRuntimeModules)
        {
            if (!CanWakeElectricRuntimeModule(module))
            {
                continue;
            }

            runtimeWakeScratch.Add(module);
        }

        for (int i = 0; i < runtimeWakeScratch.Count; i++)
        {
            runtimeWakeScratch[i]?.WakeRuntimeUpdate();
        }

        int wokenCount = runtimeWakeScratch.Count;
        runtimeWakeScratch.Clear();
        return wokenCount;
    }

    internal static bool WakeKnownElectricRuntimeModule(InputOutputModule module)
    {
        if (module == null
            || !module.gameObject.activeInHierarchy
            || !module.runtimeSleeping
            || module.waitingForOutput)
        {
            return false;
        }

        module.WakeRuntimeUpdate();
        return true;
    }

    private static bool CanWakeElectricRuntimeModule(InputOutputModule module)
    {
        return module != null
               && module.gameObject.activeInHierarchy
               && module.runtimeSleeping
               && !module.waitingForOutput
               && module.RequiresElectricOperationalEnergy();
    }

    private static void HandleInstallationPlacementRuntimeChanged(InstallationObject installationObject)
    {
        if (!AffectsRuntimeFluidTopology(installationObject))
        {
            ignoredNonFluidPlacementChangeCount++;
            return;
        }

        fluidPlacementInvalidationCount++;
        InvalidateFluidTopologyCache();
        WakeRuntimeFluidTopologyModules();
    }

    private static void HandleInstallationPlacementRuntimeCleared(InstallationObject installationObject)
    {
        if (!AffectsRuntimeFluidTopology(installationObject))
        {
            ignoredNonFluidPlacementChangeCount++;
            return;
        }

        fluidPlacementInvalidationCount++;
        InvalidateFluidTopologyCache();
        WakeRuntimeFluidTopologyModules();
    }

    internal static bool AffectsRuntimeFluidTopology(InstallationObject installationObject)
    {
        return installationObject is Pipe
               || installationObject is Fluidtank
               || installationObject != null && installationObject.CanStoreFluid
               || installationObject is InputOutputModule module
               && module.HasRuntimePipeTopologyCoordinates();
    }

    private static void InvalidateFluidTopologyCache()
    {
        fluidTopologyInvalidationCount++;
        unchecked
        {
            fluidTopologyVersion++;
        }

        if (fluidTopologyVersion <= 0)
        {
            fluidTopologyVersion = 1;
        }

        Pipe.InvalidateFluidDisplayNetworkCache();
    }

    internal static void NotifyRuntimePipeTopologyChanged(IReadOnlyList<Vector2Int> coordinates)
    {
        InvalidateFluidTopologyCache();
        WakeRuntimeModulesAtCoordinates(coordinates);

        WakeRuntimeFluidTopologyModules();
        RuntimePipeTopologyChanged?.Invoke(null);
    }

    private static void WakeRuntimeFluidTopologyModules()
    {
        // Installing/removing either a pipe OR a storage can change the far end
        // of a sleeping producer's route. Local-coordinate wakes miss it, and an
        // empty output cache has no storage-capacity waiter to wake it later.
        runtimeWakeScratch.Clear();
        foreach (InputOutputModule module in activeRuntimeModules)
        {
            if (module != null
                && module.gameObject.activeInHierarchy
                && module.HasRuntimePipeTopologyCoordinates())
            {
                runtimeWakeScratch.Add(module);
            }
        }

        for (int i = 0; i < runtimeWakeScratch.Count; i++)
        {
            runtimeWakeScratch[i]?.WakeRuntimeUpdate();
        }

        runtimeWakeScratch.Clear();
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
        return TryGetRuntimePipeFluidStorageAtCoordinate(
            registeredRuntimeFluidStorageCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> storageModules)
                ? storageModules
                : null,
            coordinate,
            excludedModule,
            requireStorageSpace,
            storageFilter,
            null,
            out storage);
    }

    internal static bool TryGetRuntimePipeDisplayFluidStorageAtCoordinate(
        Vector2Int coordinate,
        Pipe displayPipe,
        out InstallationObject storage)
    {
        return TryGetRuntimePipeFluidStorageAtCoordinate(
            registeredRuntimeFluidStorageCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> storageModules)
                ? storageModules
                : null,
            coordinate,
            null,
            false,
            null,
            displayPipe,
            out storage);
    }

    public static bool TryGetRuntimePipeSourceAtCoordinate(Vector2Int coordinate, out WaterPump pump)
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
        HashSet<InputOutputModule> modules,
        Vector2Int coordinate,
        InputOutputModule excludedModule,
        bool requireStorageSpace,
        System.Predicate<InstallationObject> storageFilter,
        Pipe displayPipe,
        out InstallationObject storage)
    {
        storage = null;
        if (modules == null)
        {
            return false;
        }

        foreach (InputOutputModule candidate in modules)
        {
            bool usesDedicatedStorage = candidate != null
                                        && candidate.UsesDedicatedFluidStorageAtRuntimeCoordinate(coordinate);
            if (candidate == null
                || candidate == excludedModule
                || !candidate.gameObject.activeInHierarchy
                || !candidate.ContainsRuntimePipeAreaBlockCoordinate(coordinate)
                || (!candidate.CanStoreFluid && !usesDedicatedStorage)
                || (requireStorageSpace
                    && (usesDedicatedStorage
                        ? candidate.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(coordinate)
                          <= 0.0001f
                        : !candidate.HasFluidStorageSpace))
                || (displayPipe != null
                    && !displayPipe.CanDisplayStoredFluidAtCoordinate(candidate, coordinate)))
            {
                continue;
            }

            if (storageFilter != null && !storageFilter(candidate))
            {
                continue;
            }

            if (storage == null || CompareSimulationOrder(candidate, storage) < 0)
            {
                storage = candidate;
            }
        }

        return storage != null;
    }

    private static bool TryGetRuntimePipeSourceAtCoordinate(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        ISet<InputOutputModule> visitedModules,
        out WaterPump pump)
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
                || !(candidate is WaterPump candidatePump)
                || !candidate.ContainsRuntimePipeAreaBlockCoordinate(coordinate)
                || !candidate.ContainsRuntimeOutputCoordinate(coordinate))
            {
                continue;
            }

            if (pump == null || CompareSimulationOrder(candidatePump, pump) < 0)
            {
                pump = candidatePump;
            }
        }

        return pump != null;
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
        return TryGetRuntimeCoordinateValues(coordinate, outputItemIds, TryAppendRuntimeOutputItemIdsCollector);
    }

    public static bool TryGetFluidOutputInfoAtRuntimeGridCoordinate(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;

        if (!registeredRuntimeFluidOutputCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> modules))
        {
            return false;
        }

        return TryGetFluidOutputInfoAtRuntimeGridCoordinate(
            modules,
            coordinate,
            out fluidItemId,
            out temperatureCelsius);
    }

    public static bool TryGetInputItemIdsAtRuntimeGridCoordinate(Vector2Int coordinate, ISet<int> inputItemIds)
    {
        return TryGetRuntimeCoordinateValues(coordinate, inputItemIds, TryAppendRuntimeInputItemIdsCollector);
    }

    public static bool TryGetAcceptedInputItemIdsAtRuntimeGridCoordinate(Vector2Int coordinate, ISet<int> inputItemIds)
    {
        return TryGetRuntimeCoordinateValues(coordinate, inputItemIds, TryAppendAcceptedRuntimeInputItemIdsCollector);
    }

    public static bool TryGetInputEnergyTypesAtRuntimeGridCoordinate(
        Vector2Int coordinate,
        ISet<ItemDefinition.EnergyType> energyTypes)
    {
        return TryGetRuntimeCoordinateValues(coordinate, energyTypes, TryAppendRuntimeInputEnergyTypesCollector);
    }

    private static readonly RuntimeCoordinateValueCollector<int> TryAppendRuntimeOutputItemIdsCollector = TryAppendRuntimeOutputItemIds;
    private static readonly RuntimeCoordinateValueCollector<int> TryAppendRuntimeInputItemIdsCollector = TryAppendRuntimeInputItemIds;
    private static readonly RuntimeCoordinateValueCollector<int> TryAppendAcceptedRuntimeInputItemIdsCollector = TryAppendAcceptedRuntimeInputItemIds;
    private static readonly RuntimeCoordinateValueCollector<ItemDefinition.EnergyType> TryAppendRuntimeInputEnergyTypesCollector = TryAppendRuntimeInputEnergyTypes;

    private static bool TryGetRuntimeCoordinateValues<T>(
        Vector2Int coordinate,
        ISet<T> values,
        RuntimeCoordinateValueCollector<T> collectValues)
    {
        // Input/output areas can lie outside the installation's occupied grid.
        // Their own registry is maintained on placement, restore, edit and pooling.
        if (values == null || collectValues == null
            || !registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out HashSet<InputOutputModule> modules))
        {
            return false;
        }

        bool foundAny = false;
        foreach (InputOutputModule module in modules)
        {
            if (module == null
                || !module.gameObject.activeInHierarchy)
            {
                continue;
            }

            foundAny |= collectValues(module, coordinate, values);
        }

        return foundAny;
    }

    private static bool TryGetFluidOutputInfoAtRuntimeGridCoordinate(
        HashSet<InputOutputModule> modules,
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        if (modules == null)
        {
            return false;
        }

        foreach (InputOutputModule module in modules)
        {
            if (module == null
                || !module.gameObject.activeInHierarchy
                || !module.ContainsRuntimeOutputCoordinate(coordinate))
            {
                continue;
            }

            module.runtimeFluidOutputItemIdScratch.Clear();
            if (!module.TryGetRuntimeOutputItemIdsAtCoordinate(
                    coordinate,
                    module.runtimeFluidOutputItemIdScratch))
            {
                continue;
            }

            foreach (int itemId in module.runtimeFluidOutputItemIdScratch)
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

        return module.TryGetRuntimeOutputItemIdsAtCoordinate(coordinate, outputItemIds);
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

        using var outputItemsLease = UnityEngine.Pool.HashSetPool<int>.Get(out var outputItemIds);
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

        // A box owns its storage filter. Overlapping machine input areas decide
        // what they consume, not what the box can receive from an output area.
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain != null
            && terrain.TryGetLoadedBlock(coordinate, out Block block)
            && block != null
            && block.MapObject is BoxObject box)
        {
            return box.AcceptsItem(itemId);
        }

        using var allowedItemsLease = UnityEngine.Pool.HashSetPool<int>.Get(out var allowedItemIds);
        return !TryGetRuntimeIoOverlapAllowedItemIds(coordinate, allowedItemIds)
            || allowedItemIds.Contains(itemId);
    }

    public static bool TryGetRuntimeIoOverlapAllowedItemIds(Vector2Int coordinate, ISet<int> allowedItemIds)
    {
        if (allowedItemIds == null)
        {
            return false;
        }

        using var outputItemsLease = UnityEngine.Pool.HashSetPool<int>.Get(out var outputItemIds);
        if (!TryGetOutputItemIdsAtRuntimeGridCoordinate(coordinate, outputItemIds)
            || outputItemIds.Count <= 0)
        {
            return false;
        }

        using var inputItemsLease = UnityEngine.Pool.HashSetPool<int>.Get(out var inputItemIds);
        bool hasInputItemArea = InputOutputModuleItemAreaController.TryGetAcceptedItemIds(coordinate, inputItemIds);
        hasInputItemArea |= TryGetAcceptedInputItemIdsAtRuntimeGridCoordinate(coordinate, inputItemIds);

        using var inputEnergyLease = UnityEngine.Pool.HashSetPool<ItemDefinition.EnergyType>.Get(out var inputEnergyTypes);
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
        return blockType != RectGridBlockType.None
            && TryGetRuntimeRectGridBlockPlacement(coordinate, out RectGridBlockPlacement placement)
            && placement.blockType == blockType;
    }

    private bool TryGetRuntimeRectGridBlockPlacement(
        Vector2Int coordinate,
        out RectGridBlockPlacement resolvedPlacement)
    {
        resolvedPlacement = default;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        return TryGetRectGridBlockPlacementAtCoordinate(
            this,
            anchorCoordinate,
            quarterTurns,
            coordinate,
            out resolvedPlacement);
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
        out Vector2Int coordinate,
        bool respectBoxMinimumRetainedCount = true)
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

            if (GetRuntimeInputAreaCenterItemCount(
                    inputArea.coordinate,
                    itemId,
                    respectBoxMinimumRetainedCount) < requiredCount)
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
            if (!TryGetRecipePair(recipeIndex, out _, out _, out int outputItemId, out _)
                || !IsRecipeOutputAvailable(outputItemId)
                || !TryGetInputOutputPair(recipeIndex, out InputOutputPair pair))
            {
                continue;
            }

            for (int inputIndex = 0; inputIndex < pair.inputs.Count; inputIndex++)
            {
                int inputItemId = pair.inputs[inputIndex].itemDefinition != null
                    ? pair.inputs[inputIndex].itemDefinition.id
                    : -1;
                if (inputItemId < 0 || !ContainsRuntimeInputItemArea(coordinate, inputItemId))
                {
                    continue;
                }

                inputItemIds.Add(inputItemId);
                foundAny = true;
            }
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
        return installedDefinition != null && installedDefinition.AppendUseEnergyTypes(energyTypes);
    }

    protected override bool UsesManagedVisualUpdates => true;
    protected override bool RequiresManagedVisualUpdate => managedRuntimeVisualsDirty;

    protected override void OnManagedVisualsResumed()
    {
        FlushManagedRuntimeVisuals(true);
    }

    protected override void TickManagedVisuals(float deltaTime)
    {
        FlushManagedRuntimeVisuals(false);
    }

    public virtual void ManagedUpdateTick(float deltaTime)
    {
        PlanManagedUpdateTick(deltaTime);
        ApplyManagedUpdateTick();
    }

    public virtual void PlanManagedUpdateTick(float deltaTime)
    {
        plannedModuleDeltaTime = Mathf.Max(0f, deltaTime);
        plannedModuleCommands = PlannedModuleCommand.None;
        stagedModuleTickPlanned = Application.isPlaying;
        if (!Application.isPlaying)
        {
            return;
        }

        if (CanStoreFluid && ShouldAutoPullFluidFromConnectedStorage())
        {
            plannedModuleCommands |= PlannedModuleCommand.PullFluid;
        }

        if (hasActiveCraft)
        {
            plannedModuleCommands |= PlannedModuleCommand.AdvanceCraft;
        }
        else
        {
            plannedModuleCommands |= PlannedModuleCommand.StartCraft;
        }
    }

    public virtual void ApplyManagedUpdateTick()
    {
        if (!TryBeginPlannedModuleApply(out float deltaTime))
        {
            return;
        }

        ApplyPlannedBaseModuleTick(deltaTime);
    }

    protected bool TryBeginPlannedModuleApply(out float deltaTime)
    {
        deltaTime = plannedModuleDeltaTime;
        if (!stagedModuleTickPlanned)
        {
            return false;
        }

        stagedModuleTickPlanned = false;
        return true;
    }

    protected void ApplyPlannedBaseModuleTick(float deltaTime)
    {
        runtimeSleeping = false;
        EnsureEffectivePairData();
        // Output changes and vacated conveyor lanes request a drain. A successful
        // transfer keeps the request pending until the stored stack is exhausted.
        if (outputDrainCheckPending)
        {
            outputDrainCheckPending = TryDrainOneOutputAreaItemToConveyor(out hasStoredOutputOnConveyor);
        }

        if ((plannedModuleCommands & PlannedModuleCommand.PullFluid) != 0 && CanStoreFluid)
        {
            DiscardIncompatibleStoredFluid();
            if (ShouldAutoPullFluidFromConnectedStorage())
            {
                PullFluidFromConnectedStorage(deltaTime);
            }
        }

        if ((plannedModuleCommands & PlannedModuleCommand.AdvanceCraft) != 0 && hasActiveCraft)
        {
            UpdateActiveCraft(deltaTime);
            if (!hasActiveCraft)
            {
                TryStartNextCraft();
            }
        }
        else if ((plannedModuleCommands & PlannedModuleCommand.StartCraft) != 0 && !hasActiveCraft)
        {
            TryStartNextCraft();
        }

        MarkManagedRuntimeVisualsDirty();
        RefreshRuntimeUpdateSleepState();
        plannedModuleCommands = PlannedModuleCommand.None;
        plannedModuleDeltaTime = 0f;
    }

    protected void MarkManagedRuntimeVisualsDirty()
    {
        managedRuntimeVisualsDirty = true;
    }

    protected virtual void OnManagedRuntimeVisualsFlushed()
    {
    }

    private void FlushManagedRuntimeVisuals(bool force)
    {
        if (!force && !managedRuntimeVisualsDirty)
        {
            return;
        }

        managedRuntimeVisualsDirty = false;
        if (this is MiningMachine)
        {
            using var visualSample = MapObjectTickProfiler.SampleNamed(
                "Render",
                nameof(MiningMachine),
                "Mining Visuals");
            UpdateManagedRuntimeVisuals(force);
        }
        else
        {
            UpdateManagedRuntimeVisuals(force);
        }

        OnManagedRuntimeVisualsFlushed();
    }

    private void UpdateManagedRuntimeVisuals(bool force)
    {
        if (ShouldUpdateVisuals)
        {
            UpdateEnergyGaugeVisual();
            RefreshWorkAnimatorState(force);
        }

        UpdateCraftParticleEffectVisual();
    }

    protected virtual bool ShouldKeepRuntimeUpdateTickActive()
    {
        return ShouldKeepFluidRuntimeUpdateTickActive();
    }

    // Energy-dependent work can pause while fluid intake and output transport continue.
    protected virtual bool ShouldKeepRuntimeUpdateTickActiveWithoutOperationalEnergy()
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

    protected void RefreshRuntimeUpdateSleepState()
    {
        if (!Application.isPlaying || !isActiveAndEnabled)
        {
            return;
        }

        // Output mutations and conveyor lane vacancies wake a blocked producer.
        if (outputDrainCheckPending)
        {
            SetRuntimeSleeping(false);
            return;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (RequiresOperationalEnergy(installedDefinition)
            && !HasOperationalEnergyAvailable(installedDefinition))
        {
            SetRuntimeSleeping(!ShouldKeepRuntimeUpdateTickActiveWithoutOperationalEnergy());
            return;
        }

        if ((hasActiveCraft && !waitingForOutput)
            || ShouldKeepRuntimeUpdateTickActive())
        {
            SetRuntimeSleeping(false);
            return;
        }

        SetRuntimeSleeping(true);
    }

    protected virtual void WakeRuntimeUpdate()
    {
        if (!Application.isPlaying || !isActiveAndEnabled)
        {
            return;
        }

        MarkPersistenceStateDirty();
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

        if (runtimeSleeping)
        {
            WakeRuntimeUpdate();
        }
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
        if (runtimeSleeping)
        {
            RegisterFluidSleepWaiters();
        }
        else
        {
            UnregisterFluidSleepWaiters();
        }
        FacilitySimulationWorld.SetScheduled(this, !runtimeSleeping && isActiveAndEnabled);
        SetSleepAwakeDebugSleeping(runtimeSleeping);
    }

    private void RegisterFluidSleepWaiters()
    {
        UnregisterFluidSleepWaiters();
        RegisterFluidInputSleepWaiters();
        RegisterFluidOutputSleepWaiters();
    }

    private void RegisterFluidInputSleepWaiters()
    {
        if (cachedConnectedFluidSourceStoragesTopologyVersion != fluidTopologyVersion)
        {
            return;
        }

        int requiredFluidItemId = ResolvePreferredFluidInputItemId();
        float currentFillRatio = GetFluidStorageFillRatio(this);
        for (int i = 0; i < cachedConnectedFluidSourceStorages.Count; i++)
        {
            if (CanUseConnectedFluidSource(
                    cachedConnectedFluidSourceStorages[i],
                    requiredFluidItemId,
                    currentFillRatio))
            {
                return;
            }
        }

        fluidInputSleepWaiterLinkCount += RegisterFluidSleepWaiterLinks(
            this,
            cachedConnectedFluidSourceStorages,
            registeredFluidInputSleepStorages,
            registeredFluidInputSleepWaiters);
    }

    private void RegisterFluidOutputSleepWaiters()
    {
        if (!fluidOutputCapacityBlocked
            || cachedFluidOutputConnectionsTopologyVersion != fluidTopologyVersion)
        {
            return;
        }

        for (int i = 0; i < cachedFluidOutputConnections.Count; i++)
        {
            if (RegisterFluidSleepWaiterLink(
                    this,
                    cachedFluidOutputConnections[i].Storage,
                    registeredFluidOutputSleepStorages,
                    registeredFluidOutputSleepWaiters))
            {
                fluidOutputSleepWaiterLinkCount++;
            }
        }
    }

    private static int RegisterFluidSleepWaiterLinks(
        InputOutputModule waiter,
        IReadOnlyList<InstallationObject> candidateStorages,
        List<InstallationObject> registeredStorages,
        Dictionary<InstallationObject, HashSet<InputOutputModule>> waitersByStorage)
    {
        int addedCount = 0;
        for (int i = 0; candidateStorages != null && i < candidateStorages.Count; i++)
        {
            if (RegisterFluidSleepWaiterLink(
                    waiter,
                    candidateStorages[i],
                    registeredStorages,
                    waitersByStorage))
                addedCount++;
        }

        return addedCount;
    }

    private static bool RegisterFluidSleepWaiterLink(
        InputOutputModule waiter,
        InstallationObject storage,
        List<InstallationObject> registeredStorages,
        Dictionary<InstallationObject, HashSet<InputOutputModule>> waitersByStorage)
    {
        bool hasDedicatedStorage = storage is InputOutputModule module
                                   && module.runtimeFluidStorageIndexCoordinates.Count > 0;
        if (storage == null || storage == waiter || (!storage.CanStoreFluid && !hasDedicatedStorage))
            return false;

        if (!waitersByStorage.TryGetValue(storage, out HashSet<InputOutputModule> waiters))
        {
            waiters = RentFluidSleepWaiterSet();
            waitersByStorage.Add(storage, waiters);
        }

        if (!waiters.Add(waiter)) return false;
        registeredStorages.Add(storage);
        return true;
    }

    private static HashSet<InputOutputModule> RentFluidSleepWaiterSet()
    {
        return fluidSleepWaiterSetPool.Count > 0
            ? fluidSleepWaiterSetPool.Pop()
            : new HashSet<InputOutputModule>();
    }

    private static void ReturnFluidSleepWaiterSet(HashSet<InputOutputModule> waiters)
    {
        if (waiters == null)
        {
            return;
        }

        waiters.Clear();
        fluidSleepWaiterSetPool.Push(waiters);
    }

    private void UnregisterFluidSleepWaiters()
    {
        fluidInputSleepWaiterLinkCount = Mathf.Max(
            0,
            fluidInputSleepWaiterLinkCount - UnregisterFluidSleepWaiters(
                this,
                registeredFluidInputSleepStorages,
                registeredFluidInputSleepWaiters));
        fluidOutputSleepWaiterLinkCount = Mathf.Max(
            0,
            fluidOutputSleepWaiterLinkCount - UnregisterFluidSleepWaiters(
                this,
                registeredFluidOutputSleepStorages,
                registeredFluidOutputSleepWaiters));
    }

    private static int UnregisterFluidSleepWaiters(
        InputOutputModule waiter,
        List<InstallationObject> registeredStorages,
        Dictionary<InstallationObject, HashSet<InputOutputModule>> waitersByStorage)
    {
        int removedCount = 0;
        for (int i = 0; i < registeredStorages.Count; i++)
        {
            InstallationObject storage = registeredStorages[i];
            if (ReferenceEquals(storage, null)
                || !waitersByStorage.TryGetValue(storage, out HashSet<InputOutputModule> waiters)
                || !waiters.Remove(waiter))
            {
                continue;
            }

            removedCount++;
            if (waiters.Count <= 0)
            {
                waitersByStorage.Remove(storage);
                ReturnFluidSleepWaiterSet(waiters);
            }
        }

        registeredStorages.Clear();
        return removedCount;
    }

    internal static void NotifyFluidInputAvailabilityIncreased(InstallationObject storage)
    {
        if (storage == null
            || !registeredFluidInputSleepWaiters.TryGetValue(
                storage,
                out HashSet<InputOutputModule> waiters)
            || waiters.Count <= 0)
        {
            return;
        }

        runtimeWakeScratch.Clear();
        foreach (InputOutputModule waiter in waiters)
        {
            if (waiter == null
                || !waiter.runtimeSleeping
                || !waiter.gameObject.activeInHierarchy)
            {
                continue;
            }

            int requiredFluidItemId = waiter.ResolvePreferredFluidInputItemId();
            if (waiter.CanUseConnectedFluidSource(
                    storage,
                    requiredFluidItemId,
                    GetFluidStorageFillRatio(waiter)))
            {
                runtimeWakeScratch.Add(waiter);
            }
        }

        using var wakeSample = MapObjectTickProfiler.SampleNamed(
            "Simulation",
            nameof(InputOutputModule),
            "Fluid Input Waiter Wake");
        for (int i = 0; i < runtimeWakeScratch.Count; i++)
        {
            runtimeWakeScratch[i]?.WakeRuntimeUpdate();
        }

        runtimeWakeScratch.Clear();
    }

    internal static void NotifyFluidOutputCapacityIncreased(InstallationObject storage)
    {
        if (storage == null
            || !registeredFluidOutputSleepWaiters.TryGetValue(
                storage,
                out HashSet<InputOutputModule> waiters)
            || waiters.Count <= 0)
        {
            return;
        }

        runtimeWakeScratch.Clear();
        foreach (InputOutputModule waiter in waiters)
        {
            if (waiter != null
                && waiter.runtimeSleeping
                && waiter.gameObject.activeInHierarchy)
            {
                runtimeWakeScratch.Add(waiter);
            }
        }

        using var wakeSample = MapObjectTickProfiler.SampleNamed(
            "Simulation",
            nameof(InputOutputModule),
            "Fluid Output Waiter Wake");
        for (int i = 0; i < runtimeWakeScratch.Count; i++)
        {
            runtimeWakeScratch[i]?.WakeRuntimeUpdate();
        }

        runtimeWakeScratch.Clear();
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

    protected IReadOnlyList<InstallationObject> GetConnectedFluidSourceStorages()
    {
        EnsureConnectedFluidSourceStorageCache();
        return cachedConnectedFluidSourceStorages;
    }

    protected virtual bool UsesConnectedTankNetworkStorage => false;

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
            if (maxTransferLiters - acceptedLiters <= 0.0001f
                || AvailableFluidStorageLiters <= 0.0001f)
            {
                break;
            }

            if (!TryFindConnectedFluidSource(requiredFluidItemId, out InstallationObject sourceStorage)
                || sourceStorage == null)
            {
                break;
            }

            int sourcePipeDistance = cachedConnectedFluidSourcePipeDistances.TryGetValue(
                sourceStorage,
                out int recordedDistance)
                ? recordedDistance
                : 0;
            connectedFluidSourcePumps.TryGetValue(sourceStorage, out Pump pressurePump);
            float transportLimit = maxTransferLiters
                                   * ResolvePumpTransportRatio(pressurePump, ConnectedFluidStorageTransferLitersPerSecond)
                                   * CalculateFluidPressureRetention(sourcePipeDistance);
            float remainingLiters = Mathf.Min(
                transportLimit - acceptedLiters,
                AvailableFluidStorageLiters);
            if (remainingLiters <= 0.0001f)
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
                pressurePump != null ? sourceStorage.StoredFluidLiters
                    : CalculateFluidEqualizationTransferLiters(sourceStorage, this));
            if (pressurePump != null)
                transferLiters = pressurePump.LimitTransferVolume(transferLiters, ManagedUpdateTickIntervalSeconds);
            if (transferLiters <= 0.0001f)
            {
                break;
            }

            using (MapObjectTickProfiler.SampleNamed(
                       "Simulation",
                       nameof(InputOutputModule),
                       "Fluid Input Transfer"))
            {
                float transferTemperatureCelsius = sourceStorage.GetStoredFluidTemperatureCelsius(transferFluidItemId);
                if (!sourceStorage.TryConsumeFluidLiters(
                        transferFluidItemId,
                        transferLiters,
                        out float consumedLiters)
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
                pressurePump?.RecordTransferredVolume(acceptedThisAttempt);

                float rejectedLiters = consumedLiters - acceptedThisAttempt;
                if (rejectedLiters > 0.0001f)
                {
                    sourceStorage.RestoreUnacceptedFluid(
                        transferFluidItemId, rejectedLiters, transferTemperatureCelsius);
                    break;
                }
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
               && ((connectedFluidSourcePumps.TryGetValue(sourceStorage, out Pump pump) && pump != null)
                   || GetFluidStorageFillRatio(sourceStorage) > currentFillRatio + 0.001f);
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
        using var searchSample = MapObjectTickProfiler.SampleNamed(
            "Simulation",
            nameof(InputOutputModule),
            "Fluid Input Storage Search");
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
        connectedFluidSeedCoordinateScratch.Clear();
        CollectRuntimePipeAreaCoordinates(connectedFluidSeedCoordinateScratch);
        return EnsureConnectedFluidSourceStorageCache(connectedFluidSeedCoordinateScratch);
    }

    private bool EnsureConnectedFluidSourceStorageCache(IReadOnlyList<Vector2Int> seedCoordinates)
    {
        if (cachedConnectedFluidSourceStoragesTopologyVersion == fluidTopologyVersion
            && CoordinatesMatch(connectedFluidSeedCoordinates, seedCoordinates))
        {
            return cachedConnectedFluidSourceStorages.Count > 0;
        }

        cachedConnectedFluidSourceStorages.Clear();
        cachedConnectedFluidSourcePipeDistances.Clear();
        connectedFluidSourcePumps.Clear();
        connectedFluidSearchQueue.Clear();
        connectedFluidSearchPipeCounts.Clear();
        connectedFluidSearchPumps.Clear();
        connectedFluidSearchCurrentPump = null;
        connectedFluidStorageCandidates.Clear();
        connectedFluidSeedCoordinates.Clear();
        AddUniqueCoordinates(seedCoordinates, connectedFluidSeedCoordinates);
        if (connectedFluidSeedCoordinates.Count <= 0)
        {
            cachedConnectedFluidSourceStoragesTopologyVersion = fluidTopologyVersion;
            return false;
        }

        for (int i = 0; i < connectedFluidSeedCoordinates.Count; i++)
        {
            Vector2Int seedCoordinate = connectedFluidSeedCoordinates[i];
            EnqueueConnectedFluidSearchCoordinate(
                seedCoordinate,
                TryGetConnectedPipeAtCoordinate(seedCoordinate, out _, out _, out _) ? 1 : 0);
        }

        while (connectedFluidSearchQueue.Count > 0)
        {
            ConnectedFluidSearchNode searchNode = connectedFluidSearchQueue.Dequeue();
            Vector2Int coordinate = searchNode.Coordinate;
            if (!connectedFluidSearchPipeCounts.TryGetValue(
                    coordinate,
                    out connectedFluidSearchCurrentPipeCount)
                || connectedFluidSearchCurrentPipeCount != searchNode.PipeCount)
            {
                continue;
            }

            connectedFluidSearchPumps.TryGetValue(coordinate, out connectedFluidSearchCurrentPump);
            bool isSeedCoordinate = ContainsCoordinate(connectedFluidSeedCoordinates, coordinate);
            bool hasPipe = TryGetConnectedPipeAtCoordinate(
                coordinate,
                out Pipe pipe,
                out Quaternion pipeRotation,
                out PipeRuntimeRecord pipeRecord);
            TryResolveConnectedFluidSearchStorageAtCoordinate(
                coordinate,
                out InstallationObject fluidStorage,
                out bool storageIsPipeArea);
            AddConnectedFluidStorageCacheCandidate(
                fluidStorage,
                cachedConnectedFluidSourceStorages,
                cachedConnectedFluidSourcePipeDistances,
                Mathf.Max(
                    0,
                    ResolveConnectedFluidPipeCount(connectedFluidSearchCurrentPipeCount) - 1));
            // A reservoir terminates this route; transfers must use its actual stock.
            if (fluidStorage is Fluidtank)
            {
                continue;
            }
            EnqueueFluidStoragePipePassCoordinatesAt(coordinate);
            bool hasPassiveFluidPass = TryEnqueuePassiveFluidPassesAt(
                coordinate,
                out int passivePassExternalDirectionMask);
            bool hasPumpPressureResetPass = TryEnqueuePumpPressureResetPassesAt(
                coordinate,
                true,
                out int pumpPassExternalDirectionMask);

            if (!isSeedCoordinate && !hasPipe && !storageIsPipeArea
                && !hasPassiveFluidPass && !hasPumpPressureResetPass)
            {
                continue;
            }

            for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = FluidCardinalDirections[directionIndex];
                bool pipeConnectsToDirection = hasPipe && HasConnectedPipeConnectionTowards(
                    pipe, pipeRecord, coordinate, pipeRotation, direction);
                if (hasPipe && !hasPumpPressureResetPass && !hasPassiveFluidPass
                    && !pipeConnectsToDirection)
                {
                    continue;
                }

                if ((hasPumpPressureResetPass || hasPassiveFluidPass)
                    && !(hasPumpPressureResetPass && pipeConnectsToDirection)
                    && !DirectionMaskContains(
                        pumpPassExternalDirectionMask | passivePassExternalDirectionMask,
                        directionIndex))
                {
                    continue;
                }

                if (!hasPipe && !hasPumpPressureResetPass && !hasPassiveFluidPass
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
                        out bool canContinueRoute,
                        out bool nextNodeIsPipe))
                {
                    continue;
                }

                AddConnectedFluidStorageCacheCandidate(
                    nextStorage,
                    cachedConnectedFluidSourceStorages,
                    cachedConnectedFluidSourcePipeDistances,
                    Mathf.Max(
                        0,
                        ResolveConnectedFluidPipeCount(
                            AddConnectedFluidPipeCount(
                                connectedFluidSearchCurrentPipeCount,
                                nextNodeIsPipe ? 1 : 0)) - 1));

                if (canContinueRoute || UsesConnectedTankNetworkStorage && nextStorage is Fluidtank)
                {
                    EnqueueConnectedFluidSearchCoordinate(
                        nextCoordinate,
                        AddConnectedFluidPipeCount(
                            connectedFluidSearchCurrentPipeCount,
                            nextNodeIsPipe ? 1 : 0));
                }
            }

            if (hasPipe && !hasPumpPressureResetPass && !hasPassiveFluidPass
                && TryGetConnectedPipeRemoteCoordinate(
                    pipe,
                    pipeRecord,
                    coordinate,
                    out Vector2Int remoteCoordinate))
            {
                EnqueueConnectedFluidSearchCoordinate(
                    remoteCoordinate,
                    IsConnectedFluidPipeCountFrozen(connectedFluidSearchCurrentPipeCount)
                        ? connectedFluidSearchCurrentPipeCount
                        : Pipe.AddRemoteTraversalPipeDistance(
                            connectedFluidSearchCurrentPipeCount,
                            coordinate,
                            remoteCoordinate));
            }
        }

        cachedConnectedFluidSourceStorages.Sort(CompareSimulationOrder);
        cachedConnectedFluidSourceStoragesTopologyVersion = fluidTopologyVersion;
        return cachedConnectedFluidSourceStorages.Count > 0;
    }

    private static bool CoordinatesMatch(
        IReadOnlyList<Vector2Int> first,
        IReadOnlyList<Vector2Int> second)
    {
        int firstCount = first != null ? first.Count : 0;
        int secondCount = second != null ? second.Count : 0;
        if (firstCount != secondCount)
        {
            return false;
        }

        for (int i = 0; i < firstCount; i++)
        {
            if (first[i] != second[i])
            {
                return false;
            }
        }

        return true;
    }

    private void EnqueueFluidStoragePipePassCoordinatesAt(Vector2Int coordinate)
    {
        EnqueueFluidStoragePipePassCoordinatesAt(
            registeredRuntimeAreaCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> areaModules)
                ? areaModules
                : null,
            coordinate);
        EnqueueFluidStoragePipePassCoordinatesAt(
            registeredRuntimeGridCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> gridModules)
                ? gridModules
                : null,
            coordinate);
    }

    private bool EnqueueFluidStoragePipePassCoordinatesAt(
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
            if (module is Boiler boiler
                && boiler.gameObject.activeInHierarchy
                && boiler.TryGetRuntimeWaterPass(coordinate, out Vector2Int otherCoordinate, out _))
            {
                EnqueueConnectedFluidSearchCoordinate(
                    otherCoordinate,
                    connectedFluidSearchCurrentPipeCount);
                foundPipeArea = true;
                continue;
            }

        }

        return foundPipeArea;
    }

    private bool TryEnqueuePassiveFluidPassesAt(
        Vector2Int coordinate,
        out int externalDirectionMask)
    {
        externalDirectionMask = 0;
        bool foundPass = EnqueuePassiveFluidPassesAt(
            registeredRuntimeAreaCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> areaModules)
                ? areaModules
                : null,
            coordinate,
            ref externalDirectionMask);
        return EnqueuePassiveFluidPassesAt(
                   registeredRuntimeGridCoordinates.TryGetValue(
                       coordinate,
                       out HashSet<InputOutputModule> gridModules)
                       ? gridModules
                       : null,
                   coordinate,
                   ref externalDirectionMask)
               || foundPass;
    }

    private bool EnqueuePassiveFluidPassesAt(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        ref int externalDirectionMask)
    {
        if (modules == null)
        {
            return false;
        }

        bool foundPass = false;
        foreach (InputOutputModule module in modules)
        {
            if (module == null
                || !module.gameObject.activeInHierarchy
                || !module.TryGetRuntimePassiveFluidPass(
                    coordinate,
                    out Vector2Int otherCoordinate,
                    out Vector2Int externalDirection))
            {
                continue;
            }

            externalDirectionMask |= GetDirectionMask(externalDirection);
            EnqueueConnectedFluidSearchCoordinate(
                otherCoordinate,
                connectedFluidSearchCurrentPipeCount);
            foundPass = true;
        }

        return foundPass;
    }

    private bool TryEnqueuePumpPressureResetPassesAt(
        Vector2Int coordinate,
        bool freezeCurrentPipeCount,
        out int externalDirectionMask)
    {
        externalDirectionMask = 0;
        connectedFluidPumpPassScratch ??= new List<RuntimePumpPipePass>(2);
        connectedFluidPumpPassScratch.Clear();
        if (!CollectPumpPipePassesAtRuntimeCoordinate(coordinate, connectedFluidPumpPassScratch))
        {
            return false;
        }

        for (int i = 0; i < connectedFluidPumpPassScratch.Count; i++)
        {
            RuntimePumpPipePass pass = connectedFluidPumpPassScratch[i];
            externalDirectionMask |= GetDirectionMask(pass.ExternalDirection);
            if (!pass.Pump.AllowsRuntimeFluidTraversal(coordinate, freezeCurrentPipeCount)) continue;
            // Every pump sharing this PipePass cell starts its own fresh
            // pressure-loss section. Traversing only one makes pump chains
            // loop back through the first installed pump.
            EnqueueConnectedFluidSearchCoordinate(
                pass.OtherCoordinate,
                freezeCurrentPipeCount
                    ? FreezeConnectedFluidPipeCount(connectedFluidSearchCurrentPipeCount)
                    : 0, pass.Pump);
        }

        EnqueueInterlockedPumpEndpointsAt(coordinate, freezeCurrentPipeCount);

        return true;
    }

    private void EnqueueInterlockedPumpEndpointsAt(
        Vector2Int coordinate,
        bool freezeCurrentPipeCount)
    {
        connectedFluidInterlockedPumpScratch ??= new List<Pump>(2);
        connectedFluidInterlockedPumpScratch.Clear();
        AppendInterlockedPumpEndpointsAt(
            registeredRuntimeAreaCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> areaModules)
                ? areaModules
                : null,
            coordinate,
            freezeCurrentPipeCount);
        AppendInterlockedPumpEndpointsAt(
            registeredRuntimeGridCoordinates.TryGetValue(
                coordinate,
                out HashSet<InputOutputModule> gridModules)
                ? gridModules
                : null,
            coordinate,
            freezeCurrentPipeCount);
    }

    private void AppendInterlockedPumpEndpointsAt(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        bool freezeCurrentPipeCount)
    {
        if (modules == null)
        {
            return;
        }

        for (int passIndex = 0; passIndex < connectedFluidPumpPassScratch.Count; passIndex++)
        {
            Pump sourcePump = connectedFluidPumpPassScratch[passIndex].Pump;
            foreach (InputOutputModule module in modules)
            {
                if (!(module is Pump candidatePump)
                    || candidatePump == sourcePump
                    || connectedFluidInterlockedPumpScratch.Contains(candidatePump)
                    || sourcePump.AllowsRuntimeFluidTraversal(coordinate, freezeCurrentPipeCount)
                    || !candidatePump.TryGetRuntimeInterlockedEndpoint(
                        sourcePump,
                        coordinate,
                        out Vector2Int candidateEndpoint)
                    || !candidatePump.AllowsRuntimeFluidTraversal(candidateEndpoint, freezeCurrentPipeCount))
                {
                    continue;
                }

                connectedFluidInterlockedPumpScratch.Add(candidatePump);
                // Consecutive pump meshes are authored two cells apart. Their
                // facing PipePass cells reciprocally overlap the other pump's
                // body rather than occupying the same grid cell. Treat only
                // that reciprocal overlap as the connector between pumps.
                EnqueueConnectedFluidSearchCoordinate(
                    candidateEndpoint,
                    freezeCurrentPipeCount
                        ? FreezeConnectedFluidPipeCount(connectedFluidSearchCurrentPipeCount)
                        : 0, Pump.ResolvePressureLimit(sourcePump, candidatePump));
            }
        }
    }

    private bool TrySelectConnectedFluidSourceFromCache(
        int requiredFluidItemId,
        float currentFillRatio,
        out InstallationObject sourceStorage)
    {
        sourceStorage = null;
        float bestFillRatio = -1f;
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
        List<InstallationObject> targetStorages,
        Dictionary<InstallationObject, int> targetPipeDistances,
        int pipeDistance)
    {
        if (storage == null
            || storage == this
            || !storage.CanStoreFluid
            || targetStorages == null)
        {
            return;
        }

        if (connectedFluidStorageCandidates.Add(storage))
        {
            targetStorages.Add(storage);
        }

        if (targetPipeDistances != null
            && (!targetPipeDistances.TryGetValue(storage, out int previousDistance)
                || pipeDistance < previousDistance))
        {
            targetPipeDistances[storage] = pipeDistance;
            connectedFluidSourcePumps[storage] = connectedFluidSearchCurrentPump;
        }
    }

    protected sealed class FluidTransferPreview
    {
        internal readonly Dictionary<Pump, float> PumpLiters = new Dictionary<Pump, float>();
        internal readonly Dictionary<(InstallationObject, Vector2Int), float> InputLiters =
            new Dictionary<(InstallationObject, Vector2Int), float>();
        internal readonly Dictionary<(InstallationObject, Vector2Int), float> OutputLiters =
            new Dictionary<(InstallationObject, Vector2Int), float>();

        public void Clear()
        {
            PumpLiters.Clear();
            InputLiters.Clear();
            OutputLiters.Clear();
        }
    }

    private readonly FluidTransferPreview fluidTransferPreviewScratch = new FluidTransferPreview();

    private float PreviewFluidTransfer(FluidOutputConnection connection, float capacityLiters,
        float requestedLiters, bool input, FluidTransferPreview preview)
    {
        var storageLiters = input ? preview.InputLiters : preview.OutputLiters;
        Vector2Int endpoint = !input && connection.Storage is InputOutputModule module
                             && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(connection.Coordinate)
            ? connection.Coordinate : default;
        var key = (connection.Storage, endpoint);
        storageLiters.TryGetValue(key, out float alreadyStored);
        float available = Mathf.Min(Mathf.Max(0f, capacityLiters - alreadyStored), requestedLiters);
        Pump pump = connection.PressurePump != null ? connection.PressurePump : !input ? this as Pump : null;
        if (pump != null)
        {
            preview.PumpLiters.TryGetValue(pump, out float alreadyPumped);
            float allowance = pump.LimitTransferVolume(float.MaxValue, ManagedUpdateTickIntervalSeconds);
            available = Mathf.Min(available, Mathf.Max(0f, allowance - alreadyPumped));
            preview.PumpLiters[pump] = alreadyPumped + available;
        }
        storageLiters[key] = alreadyStored + available;
        return available;
    }

    protected bool TryGetConnectedFluidInputAvailableLitersAtCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float maximumLiters,
        out float availableLiters,
        FluidTransferPreview preview = null)
    {
        availableLiters = 0f;
        if (!IsFluidItemId(fluidItemId) || maximumLiters <= 0.0001f)
        {
            return false;
        }

        if (preview == null)
        {
            preview = fluidTransferPreviewScratch;
            preview.Clear();
        }
        FluidPortConnectionCache cache = GetFluidPortConnectionCache(
            fluidInputPortConnectionCaches,
            coordinate,
            true);
        for (int i = 0; i < cache.Connections.Count; i++)
        {
            InstallationObject storage = cache.Connections[i].Storage;
            if (storage == null
                || !storage.gameObject.activeInHierarchy
                || !storage.CanProvideFluidItem(fluidItemId, 0.0001f))
            {
                continue;
            }

            availableLiters += PreviewFluidTransfer(cache.Connections[i], storage.StoredFluidLiters,
                maximumLiters - availableLiters, true, preview);
            if (availableLiters + 0.0001f >= maximumLiters)
            {
                availableLiters = maximumLiters;
                return true;
            }
        }

        return availableLiters > 0.0001f;
    }

    protected bool TryConsumeConnectedFluidInputAtCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float requestedLiters,
        out float consumedLiters,
        out float temperatureCelsius)
    {
        consumedLiters = 0f;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        if (!IsFluidItemId(fluidItemId) || requestedLiters <= 0f)
        {
            return false;
        }

        FluidPortConnectionCache cache = GetFluidPortConnectionCache(
            fluidInputPortConnectionCaches,
            coordinate,
            true);
        float weightedTemperature = 0f;
        for (int i = 0; i < cache.Connections.Count; i++)
        {
            float remainingLiters = requestedLiters - consumedLiters;
            if (remainingLiters <= 0f)
            {
                break;
            }

            InstallationObject storage = cache.Connections[i].Storage;
            if (storage == null
                || !storage.gameObject.activeInHierarchy
                || !storage.CanProvideFluidItem(fluidItemId))
            {
                continue;
            }

            Pump pressurePump = cache.Connections[i].PressurePump;
            float transferLiters = Mathf.Min(remainingLiters, storage.StoredFluidLiters);
            if (pressurePump != null)
                transferLiters = pressurePump.LimitTransferVolume(transferLiters, ManagedUpdateTickIntervalSeconds);
            if (transferLiters <= 0f) continue;
            float sourceTemperature = storage.GetStoredFluidTemperatureCelsius(fluidItemId);
            if (!storage.TryConsumeFluidLiters(
                    fluidItemId,
                    transferLiters,
                    out float consumedThisStorage)
                || consumedThisStorage <= 0f)
            {
                continue;
            }

            consumedLiters += consumedThisStorage;
            pressurePump?.RecordTransferredVolume(consumedThisStorage);
            weightedTemperature += sourceTemperature * consumedThisStorage;
        }

        if (consumedLiters > 0f)
        {
            temperatureCelsius = weightedTemperature / consumedLiters;
        }

        return consumedLiters + 0.0001f >= requestedLiters;
    }

    private FluidPortConnectionCache GetFluidPortConnectionCache(
        Dictionary<Vector2Int, FluidPortConnectionCache> caches,
        Vector2Int coordinate,
        bool input)
    {
        if (!caches.TryGetValue(coordinate, out FluidPortConnectionCache cache))
        {
            cache = new FluidPortConnectionCache();
            caches.Add(coordinate, cache);
        }

        if (cache.TopologyVersion == fluidTopologyVersion)
        {
            return cache;
        }

        cache.Connections.Clear();
        connectedFluidSeedCoordinateScratch.Clear();
        connectedFluidSeedCoordinateScratch.Add(coordinate);
        if (input)
        {
            EnsureConnectedFluidSourceStorageCache(connectedFluidSeedCoordinateScratch);
            for (int i = 0; i < cachedConnectedFluidSourceStorages.Count; i++)
            {
                InstallationObject storage = cachedConnectedFluidSourceStorages[i];
                int pipeDistance = cachedConnectedFluidSourcePipeDistances.TryGetValue(
                    storage,
                    out int distance)
                    ? distance
                    : 0;
                connectedFluidSourcePumps.TryGetValue(storage, out Pump pressurePump);
                cache.Connections.Add(new FluidOutputConnection(storage, default, pipeDistance, pressurePump));
            }
        }
        else
        {
            EnsureFluidOutputStorageCache(connectedFluidSeedCoordinateScratch);
            cache.Connections.AddRange(cachedFluidOutputConnections);
        }

        cache.TopologyVersion = fluidTopologyVersion;
        return cache;
    }

    protected virtual int ResolvePreferredFluidInputItemId()
    {
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (TryGetFluidFuelEnergyType(
                installedDefinition,
                out ItemDefinition.EnergyType fluidFuelEnergyType))
        {
            return ResolveFluidFuelItemId(fluidFuelEnergyType);
        }

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

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        bool usesFluidFuelInput = TryGetFluidFuelEnergyType(
            installedDefinition,
            out ItemDefinition.EnergyType fluidFuelEnergyType);
        int fluidFuelItemId = usesFluidFuelInput
            ? ResolveFluidFuelItemId(fluidFuelEnergyType)
            : -1;
        if (fluidItemId < 0)
        {
            return !usesFluidFuelInput || fluidFuelItemId >= 0;
        }

        if (usesFluidFuelInput && fluidItemId == fluidFuelItemId)
        {
            return true;
        }

        if (!HasFluidInputRecipe())
        {
            return !usesFluidFuelInput;
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
            || CanAcceptFluidItem(storedFluidItemId, 0f))
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

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        bool hasConfiguredFluidStorage = installedDefinition != null
                                         && installedDefinition.storesFluid
                                         && installedDefinition.fluidStorageLiters > 0f;
        if (CanStoreFluid
            && coordinates.Count == originalCount
            && hasConfiguredFluidStorage)
        {
            AddRuntimeFluidStoragePipeNodeCoordinates(RuntimeOccupiedCoordinates, coordinates);
        }
    }

    internal void AppendRuntimeFluidDisplaySourceCoordinates(List<Vector2Int> coordinates)
    {
        if (coordinates == null)
        {
            return;
        }

        AddUniqueCoordinates(runtimeFluidOutputIndexCoordinates, coordinates);
        AddUniqueCoordinates(runtimeFluidStorageIndexCoordinates, coordinates);
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
        out bool canContinueRoute,
        out bool isPipeNode)
    {
        storage = null;
        canContinueRoute = false;
        isPipeNode = false;

        // A PipePass owns its endpoint even when a pipe is installed on the
        // same cell. Checking that overlapping pipe first can reject a valid
        // pass because its resolved visual variant need not expose the inward
        // connection in the runtime pipe mask.
        if (HasRuntimePumpPipePassTowards(coordinate, directionToPrevious))
        {
            canContinueRoute = true;
            return true;
        }

        if (HasRuntimePassiveFluidPassTowards(coordinate, directionToPrevious))
        {
            canContinueRoute = true;
            return true;
        }

        if (TryGetConnectedPipeAtCoordinate(
                coordinate,
                out Pipe pipe,
                out Quaternion pipeRotation,
                out PipeRuntimeRecord pipeRecord))
        {
            if (!HasConnectedPipeConnectionTowards(
                    pipe,
                    pipeRecord,
                    coordinate,
                    pipeRotation,
                    directionToPrevious))
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
            isPipeNode = true;
            return true;
        }

        if (TryResolveConnectedFluidSearchStorageAtCoordinate(
                coordinate,
                out storage,
                out bool storageIsPipeArea))
        {
            if (this is WaterPump
                && storage is SteamTrain steamTrain
                && !steamTrain.CanAcceptWaterFromPipeDirection(
                    -directionToPrevious,
                    WaterPump.ResolveWaterItemId(null),
                    false))
            {
                storage = null;
                return false;
            }

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

            canContinueRoute = storageIsPipeArea || IsFixedFluidTank(storage);
            return true;
        }

        return false;
    }

    private bool TryGetConnectedPipeAtCoordinate(
        Vector2Int coordinate,
        out Pipe pipe,
        out Quaternion pipeRotation,
        out PipeRuntimeRecord pipeRecord)
    {
        pipe = null;
        pipeRotation = Quaternion.identity;
        pipeRecord = null;

        // Data-only pipes are owned by PipeWorld. A loaded Block binding is only
        // a view/cache and can legitimately be absent when a pipe overlaps an
        // installation pipe area. Fluid transport must therefore use the world
        // record first, just like pipe pressure and tank-network searches do.
        PipeWorld pipeWorld = PipeWorld.Current;
        if (pipeWorld != null
            && pipeWorld.TryGetAtCoordinate(coordinate, out pipeRecord)
            && pipeRecord != null)
        {
            pipe = pipeRecord.Prototype;
            pipeRotation = pipeRecord.WorldRotation;
            if (pipe != null)
            {
                return true;
            }

            pipeRecord = null;
            pipeRotation = Quaternion.identity;
        }

        if (!TryGetLoadedBlock(coordinate, out Block block)
            || block == null)
        {
            return false;
        }

        if (block.TryGetRuntimePipeRecord(out pipeRecord))
        {
            pipe = pipeRecord.Prototype;
            pipeRotation = pipeRecord.WorldRotation;
            return pipe != null;
        }

        if (!block.TryGetRuntimePipe(out Pipe candidatePipe, out pipeRotation))
        {
            return false;
        }

        pipe = candidatePipe;
        return true;
    }

    private static bool HasConnectedPipeConnectionTowards(
        Pipe pipe,
        PipeRuntimeRecord pipeRecord,
        Vector2Int coordinate,
        Quaternion pipeRotation,
        Vector2Int direction)
    {
        return pipeRecord != null
            ? pipeRecord.HasConnectionTowardsAt(coordinate, direction)
            : pipe != null && pipe.HasConnectionTowardsAt(coordinate, pipeRotation, direction);
    }

    private static bool TryGetConnectedPipeRemoteCoordinate(
        Pipe pipe,
        PipeRuntimeRecord pipeRecord,
        Vector2Int coordinate,
        out Vector2Int remoteCoordinate)
    {
        remoteCoordinate = default;
        return pipeRecord != null
            ? pipeRecord.TryGetRemoteConnectionCoordinate(coordinate, out remoteCoordinate)
            : pipe != null && pipe.TryGetRemoteConnectionCoordinate(coordinate, out remoteCoordinate);
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

        return TryResolveConnectedFluidStorageBodyAtCoordinate(coordinate, out storage, storageFilter);
    }

    private bool TryResolveConnectedFluidSearchStorageAtCoordinate(
        Vector2Int coordinate,
        out InstallationObject storage,
        out bool storageIsPipeArea)
    {
        storageIsPipeArea = false;
        if (TryGetRuntimePipeFluidStorageAtCoordinate(coordinate, this, false, out storage))
        {
            if (storage is SteamGenerator generator
                && (!(this is Boiler) || !directedSteamChainVisited.Contains(generator)))
            {
                storage = null;
                return false;
            }

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

    protected bool TryGetRuntimePipeAreaExternalDirection(Vector2Int coordinate, out Vector2Int direction)
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

    private Pipe.FluidNetworkSearchContext runtimeFluidInputPressureContext;

    protected bool TryGetRuntimeFluidInputPressure(
        Vector2Int inputCoordinate,
        int fluidItemId,
        out float pressureLitersPerSecond)
    {
        pressureLitersPerSecond = 0f;
        if (fluidItemId < 0
            || !TryGetRuntimePipeAreaExternalDirection(inputCoordinate, out Vector2Int externalDirection))
        {
            return false;
        }

        // Input areas accept a pipe, a Pump endpoint/body alias, or a passive
        // pass. A directly installed node takes precedence over an adjacent one.
        if (TryGetFluidInputPressureAt(inputCoordinate, -externalDirection, fluidItemId,
                out bool hasInputNode, out pressureLitersPerSecond))
        {
            return true;
        }

        return !hasInputNode && TryGetFluidInputPressureAt(
            inputCoordinate + externalDirection, -externalDirection, fluidItemId,
            out _, out pressureLitersPerSecond);
    }

    private bool TryGetFluidInputPressureAt(
        Vector2Int coordinate,
        Vector2Int directionToInput,
        int fluidItemId,
        out bool hasNode,
        out float pressureLitersPerSecond)
    {
        pressureLitersPerSecond = 0f;
        PipeRuntimeRecord pipeRecord = null;
        bool hasPipe = PipeWorld.Current != null
            && PipeWorld.Current.TryGetAtCoordinate(coordinate, out pipeRecord);
        connectedFluidPumpPassScratch ??= new List<RuntimePumpPipePass>(2);
        connectedFluidPumpPassScratch.Clear();
        bool hasPump = CollectPumpPipePassesAtRuntimeCoordinate(coordinate, connectedFluidPumpPassScratch);
        bool hasPass = TryGetPassiveFluidPassAtRuntimeCoordinate(
            coordinate, out _, out _, out Vector2Int passDirection);
        hasNode = hasPipe || hasPump || hasPass;
        bool connects = hasPipe && pipeRecord != null
            && pipeRecord.HasConnectionTowardsAt(coordinate, directionToInput)
            || hasPass && passDirection == directionToInput;
        for (int i = 0; i < connectedFluidPumpPassScratch.Count; i++)
        {
            RuntimePumpPipePass pass = connectedFluidPumpPassScratch[i];
            if (pass.ExternalDirection == directionToInput
                && pass.Pump.AllowsRuntimeFluidTraversal(coordinate, true))
            {
                connects = true;
                break;
            }
        }
        connectedFluidPumpPassScratch.Clear();
        if (!connects) return false;

        runtimeFluidInputPressureContext ??= new Pipe.FluidNetworkSearchContext();
        if (!Pipe.TryGetNetworkFluidInfoAt(coordinate, runtimeFluidInputPressureContext,
                false, default, true, out int connectedFluidItemId, out _, out float pressure)
            || connectedFluidItemId != fluidItemId)
        {
            return false;
        }

        pressureLitersPerSecond = Mathf.Max(0f, pressure);
        return true;
    }

    internal virtual bool TryGetRuntimePassiveFluidPass(
        Vector2Int coordinate,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        // Input ports are independent. Only a module with an explicit pass may join them.
        otherCoordinate = default;
        externalDirection = default;
        return false;
    }

    private bool TryGetNearestRuntimeObjectDirectionFromCoordinate(Vector2Int coordinate, out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        return TryGetNearestRectGridObjectDirection(
            this,
            anchorCoordinate,
            quarterTurns,
            coordinate,
            out direction);
    }

    private bool TryResolveConnectedFluidStorageBodyAtCoordinate(
        Vector2Int coordinate,
        out InstallationObject storage,
        System.Predicate<InstallationObject> storageFilter = null)
    {
        // Trains are moving installations and deliberately are not written to
        // Block.MapObject. A ready water-pipe dock registers its current grid
        // coordinate separately so the pipe search can still resolve the tank.
        if (SteamTrain.TryGetWaterPipeReceiverAtCoordinate(
                coordinate,
                out SteamTrain waterReceiver)
            && (storageFilter == null || storageFilter(waterReceiver)))
        {
            storage = waterReceiver;
            return true;
        }

        storage = null;
        // Multiple installations can share a grid cell. Block.MapObject holds
        // only the representative object; the placement index owns all storages.
        fluidStorageBodyScratch.Clear();
        CollectActiveInstallationsAtRuntimeGridCoordinate(coordinate, fluidStorageBodyScratch);
        for (int i = 0; i < fluidStorageBodyScratch.Count; i++)
        {
            InstallationObject candidate = fluidStorageBodyScratch[i];
            if (candidate == null || candidate == this
                || candidate is Pipe || candidate is WaterPump
                || !candidate.gameObject.activeInHierarchy || !candidate.CanStoreFluid
                || !ContainsRuntimeOccupiedCoordinate(candidate, coordinate)
                || storageFilter != null && !storageFilter(candidate))
            {
                continue;
            }

            if (storage == null || CompareSimulationOrder(candidate, storage) < 0)
            {
                storage = candidate;
            }
        }

        fluidStorageBodyScratch.Clear();
        return storage != null;
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

    private void EnqueueConnectedFluidSearchCoordinate(Vector2Int coordinate, int pipeCount, Pump crossedPump = null)
    {
        if (connectedFluidSearchPipeCounts.TryGetValue(
                coordinate,
                out int previousPipeCount)
            && !IsBetterConnectedFluidPipeCount(pipeCount, previousPipeCount))
        {
            return;
        }

        connectedFluidSearchPumps[coordinate] = Pump.ResolvePressureLimit(connectedFluidSearchCurrentPump, crossedPump);
        connectedFluidSearchPipeCounts[coordinate] = pipeCount;
        connectedFluidSearchQueue.Enqueue(
            new ConnectedFluidSearchNode(coordinate, pipeCount));
    }

    internal static bool HasRuntimePumpPipePassTowards(Vector2Int coordinate, Vector2Int externalDirection)
    {
        return externalDirection != Vector2Int.zero
               && (HasPumpPipePassTowards(
                       registeredRuntimeAreaCoordinates.TryGetValue(
                           coordinate,
                           out HashSet<InputOutputModule> areaModules)
                           ? areaModules
                           : null,
                       coordinate,
                       externalDirection)
                   || HasPumpPipePassTowards(
                       registeredRuntimeGridCoordinates.TryGetValue(
                           coordinate,
                           out HashSet<InputOutputModule> gridModules)
                           ? gridModules
                           : null,
                       coordinate,
                       externalDirection));
    }

    internal static bool HasRuntimePassiveFluidPassTowards(
        Vector2Int coordinate,
        Vector2Int externalDirection)
    {
        return externalDirection != Vector2Int.zero
               && (HasPassiveFluidPassTowards(
                       registeredRuntimeAreaCoordinates.TryGetValue(
                           coordinate,
                           out HashSet<InputOutputModule> areaModules)
                           ? areaModules
                           : null,
                       coordinate,
                       externalDirection)
                   || HasPassiveFluidPassTowards(
                       registeredRuntimeGridCoordinates.TryGetValue(
                           coordinate,
                           out HashSet<InputOutputModule> gridModules)
                           ? gridModules
                           : null,
                       coordinate,
                       externalDirection));
    }

    private static bool HasPassiveFluidPassTowards(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        Vector2Int externalDirection)
    {
        if (modules == null)
        {
            return false;
        }

        foreach (InputOutputModule module in modules)
        {
            if (module != null
                && module.gameObject.activeInHierarchy
                && module.TryGetRuntimePassiveFluidPass(
                    coordinate,
                    out _,
                    out Vector2Int candidateExternalDirection)
                && candidateExternalDirection == externalDirection)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPumpPipePassTowards(
        IEnumerable<InputOutputModule> modules,
        Vector2Int coordinate,
        Vector2Int externalDirection)
    {
        if (modules == null)
        {
            return false;
        }

        foreach (InputOutputModule module in modules)
        {
            if (module is Pump pump
                && pump.gameObject.activeInHierarchy
                && pump.TryGetRuntimePipePass(
                    coordinate,
                    out _,
                    out Vector2Int candidateExternalDirection)
                && candidateExternalDirection == externalDirection)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetDirectionMask(Vector2Int direction)
    {
        for (int i = 0; i < FluidCardinalDirections.Length; i++)
        {
            if (FluidCardinalDirections[i] == direction)
            {
                return 1 << i;
            }
        }

        return 0;
    }

    private static bool DirectionMaskContains(int mask, int directionIndex)
    {
        return directionIndex >= 0
               && directionIndex < FluidCardinalDirections.Length
               && (mask & (1 << directionIndex)) != 0;
    }

    private static int FreezeConnectedFluidPipeCount(int pipeCount)
    {
        return IsConnectedFluidPipeCountFrozen(pipeCount)
            ? pipeCount
            : -Mathf.Max(0, pipeCount) - 1;
    }

    private static int AddConnectedFluidPipeCount(int pipeCount, int amount)
    {
        if (IsConnectedFluidPipeCountFrozen(pipeCount))
        {
            return pipeCount;
        }

        long result = (long)Mathf.Max(0, pipeCount) + Mathf.Max(0, amount);
        return result >= int.MaxValue ? int.MaxValue : (int)result;
    }

    private static bool IsBetterConnectedFluidPipeCount(int candidate, int current)
    {
        int candidateCount = ResolveConnectedFluidPipeCount(candidate);
        int currentCount = ResolveConnectedFluidPipeCount(current);
        return candidateCount < currentCount
               || candidateCount == currentCount
               && IsConnectedFluidPipeCountFrozen(candidate)
               && !IsConnectedFluidPipeCountFrozen(current);
    }

    private static bool IsConnectedFluidPipeCountFrozen(int pipeCount)
    {
        return pipeCount < 0;
    }

    private static int ResolveConnectedFluidPipeCount(int pipeCount)
    {
        if (!IsConnectedFluidPipeCountFrozen(pipeCount))
        {
            return Mathf.Max(0, pipeCount);
        }

        long decodedCount = -(long)pipeCount - 1L;
        return decodedCount >= int.MaxValue ? int.MaxValue : (int)decodedCount;
    }

    private static bool IsFixedFluidTank(InstallationObject storage)
    {
        return storage is Fluidtank fluidTank && !fluidTank.IsFlatCarMounted;
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
        EnsureFacilityFlowState();
        cachedAreaMarkerController = null;
        areaMarkerControllerResolved = false;
        effectivePairDataInitialized = false;
        EnsureEffectivePairData();
        activeRuntimeModules.Add(this);
        FacilitySimulationWorld.Register(this);
        RegisterRuntimeGridCoordinates();
        RegisterRuntimeAreaCoordinates();
        RegisterRuntimeFluidSpatialCoordinates();
        WakeRuntimeUpdate();
        RefreshWorkAnimatorState(true);
        if (HasRuntimePipeTopologyCoordinates())
        {
            if (runtimePipeInputCoordinates.Count > 0)
            {
                NotifyRuntimePipeTopologyChanged(runtimePipeInputCoordinates);
            }
            else
            {
                RuntimePipeTopologyChanged?.Invoke(this);
            }
        }
    }

    protected override void OnDisable()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;

        fluidOutputRateMeter?.Reset();
        bool hadRuntimePipeTopologyCoordinates = HasRuntimePipeTopologyCoordinates();
        bool hadRuntimePipeInputs = runtimePipeInputCoordinates.Count > 0;
        SetWorkAnimatorState(false, true);
        StopCraftParticleEffectVisual(true);
        FacilitySimulationWorld.Unregister(this);
        UnregisterFluidSleepWaiters();
        runtimeSleeping = false;
        fluidOutputCapacityBlocked = false;
        managedRuntimeVisualsDirty = false;
        UnregisterRuntimeFluidSpatialCoordinates();
        UnregisterRuntimeGridCoordinates();
        UnregisterRuntimeAreaCoordinates();
        activeRuntimeModules.Remove(this);
        if (hadRuntimePipeTopologyCoordinates)
        {
            if (hadRuntimePipeInputs)
            {
                NotifyRuntimePipeTopologyChanged(runtimePipeInputCoordinates);
            }
            else
            {
                RuntimePipeTopologyChanged?.Invoke(this);
            }
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
        RegisterRuntimeFluidSpatialCoordinates();
        WakeRuntimeUpdate();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        UnregisterRuntimeFluidSpatialCoordinates();
        base.OnPlacementRuntimeCleared();
    }

    private void OnDestroy()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;

        FacilitySimulationWorld.Unregister(this);
        ReleaseFacilityFlowState();
        UnregisterFluidSleepWaiters();
        runtimeSleeping = false;
        fluidOutputCapacityBlocked = false;
        UnregisterRuntimeFluidSpatialCoordinates();
        UnregisterRuntimeGridCoordinates();
        UnregisterRuntimeAreaCoordinates();
        activeRuntimeModules.Remove(this);
        ReleaseEnergyGaugeVisual();
    }

    private void EnsurePairData()
    {
        if (inputOutputPairs == null)
        {
            inputOutputPairs = new List<InputOutputPair>();
        }

        if (inputList == null)
        {
            inputList = new List<ItemIoEntry>();
        }

        if (outputList == null)
        {
            outputList = new List<ItemIoEntry>();
        }

        if (inputOutputPairs.Count == 0 && (inputList.Count > 0 || outputList.Count > 0))
        {
            MigrateLegacyPairData();
        }

        localInputList.Clear();
        localOutputList.Clear();
        for (int pairIndex = 0; pairIndex < inputOutputPairs.Count; pairIndex++)
        {
            InputOutputPair pair = inputOutputPairs[pairIndex];
            if (pair == null)
            {
                pair = new InputOutputPair();
                inputOutputPairs[pairIndex] = pair;
            }

            pair.inputs ??= new List<ItemIoEntry>();
            pair.outputs ??= new List<ItemIoEntry>();
            NormalizePairEntries(pair.inputs, localInputList);
            NormalizePairEntries(pair.outputs, localOutputList);
        }
    }

    private void MigrateLegacyPairData()
    {
        int legacyPairCount = Mathf.Max(inputList.Count, outputList.Count);
        ItemIoEntry migratedOutput = output;
        migratedOutput.count = migratedOutput.ResolvedAmount;
        for (int pairIndex = 0; pairIndex < legacyPairCount; pairIndex++)
        {
            InputOutputPair pair = new InputOutputPair();
            if (pairIndex < inputList.Count)
            {
                ItemIoEntry inputEntry = inputList[pairIndex];
                inputEntry.count = inputEntry.ResolvedAmount;
                pair.inputs.Add(inputEntry);
            }

            if (pairIndex < outputList.Count)
            {
                ItemIoEntry outputEntry = outputList[pairIndex];
                outputEntry.count = outputEntry.ResolvedAmount;
                pair.outputs.Add(outputEntry);
            }
            else if (outputList.Count == 0 && inputList.Count > 0)
            {
                pair.outputs.Add(migratedOutput);
            }

            inputOutputPairs.Add(pair);
        }

        if (legacyPairCount > 0)
        {
            inputList.Clear();
            outputList.Clear();
            output = new ItemIoEntry(null, 1);
        }
    }

    private static void NormalizePairEntries(List<ItemIoEntry> entries, List<ItemIoEntry> flattenedEntries)
    {
        if (entries == null || flattenedEntries == null)
        {
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            ItemIoEntry entry = entries[i];
            entry.count = entry.ResolvedAmount;
            entries[i] = entry;
            flattenedEntries.Add(entry);
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

        if (TryGetRuntimeRectGridBlockPlacement(coordinate, out RectGridBlockPlacement placement)
            && IsOutputBlockType(placement.blockType)
            && placement.itemDefinition != null
            && placement.itemDefinition.id >= 0)
        {
            outputItemIds.Add(placement.itemDefinition.id);
            return true;
        }

        return AppendOutputItemIds(outputItemIds);
    }

    private bool RuntimeOutputCoordinateAcceptsItem(Vector2Int coordinate, int itemId)
    {
        return !TryGetRuntimeRectGridBlockPlacement(coordinate, out RectGridBlockPlacement placement)
            || !IsOutputBlockType(placement.blockType)
            || placement.itemDefinition == null
            || placement.itemDefinition.id < 0
            || placement.itemDefinition.id == itemId;
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
        if (!TryGetRectGridBlockPlacementAtCoordinate(
                footprintSource,
                anchorCoordinate,
                quarterTurns,
                coordinate,
                out RectGridBlockPlacement placement))
        {
            return false;
        }

        blockType = placement.blockType;
        return true;
    }

    public bool TryGetRectGridBlockPlacementAtCoordinate(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int coordinate,
        out RectGridBlockPlacement resolvedPlacement)
    {
        resolvedPlacement = default;
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

            resolvedPlacement = placement;
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
        if (!TryGetRectGridObjectAnchorCell(this, out Vector2Int objectCell)
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
        if (!TryGetRectGridObjectAnchorCell(this, out Vector2Int objectCell)
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
        return storedEnergyUnits > 0L;
    }

    public bool HasActiveOrPendingCraft()
    {
        return hasActiveCraft || waitingForOutput;
    }

    public virtual bool TryGetElectricPowerRequirement(out float wattsPerSecond)
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
    public float ObjectInfoStoredEnergy => DeterministicSimulationUnits.ToFloat(storedEnergyUnits);
    public float ObjectInfoEnergyGaugeCapacity => DeterministicSimulationUnits.ToFloat(
        Math.Max(energyGaugeCapacityUnits, storedEnergyUnits));
    public float ObjectInfoEnergyGaugeFillAmount => ResolveEnergyGaugeFillAmount(ResolveInstalledDefinition());
    public float ObjectInfoWorkGaugeFillAmount => ResolveCraftProgressGaugeFillAmount();
    public Color ObjectInfoEnergyGaugeFillColor => energyGaugeFillColor;
    public Color ObjectInfoWorkGaugeFillColor => craftProgressGaugeFillColor;
    public float ObjectInfoCurrentUseEnergy => ResolveObjectInfoCurrentUseEnergy();
    public float ObjectInfoCompleteEnergy => ResolveObjectInfoCompleteEnergy();
    protected float OperationalAnimationSpeedRatio => ResolveOperationalAnimationSpeedRatio();
    public virtual bool IsWorkingForItemLight
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
        effectiveInputOutputPairs.Clear();
        HashSet<InputOutputModule> visitedModules = new HashSet<InputOutputModule>();
        AppendEffectivePairData(this, visitedModules, effectiveInputOutputPairs);
        for (int pairIndex = 0; pairIndex < effectiveInputOutputPairs.Count; pairIndex++)
        {
            InputOutputPair pair = effectiveInputOutputPairs[pairIndex];
            if (pair == null)
            {
                continue;
            }

            AppendEntries(pair.inputs, effectiveInputList);
            AppendEntries(pair.outputs, effectiveOutputList);
        }
        effectivePairDataInitialized = true;
    }

    private int GetEffectiveRecipeCount()
    {
        EnsureEffectivePairData();
        return effectiveInputOutputPairs.Count;
    }

    private static void AppendEffectivePairData(
        InputOutputModule module,
        ISet<InputOutputModule> visitedModules,
        List<InputOutputPair> resolvedPairs)
    {
        if (module == null
            || visitedModules == null
            || resolvedPairs == null
            || !visitedModules.Add(module))
        {
            return;
        }

        module.EnsurePairData();
        AppendEffectivePairData(
            module.ResolveParentInputOutputModule(),
            visitedModules,
            resolvedPairs);

        for (int i = 0; i < module.inputOutputPairs.Count; i++)
        {
            InputOutputPair pair = module.inputOutputPairs[i];
            if (pair != null)
            {
                resolvedPairs.Add(pair);
            }
        }
    }

    private static void AppendEntries(IReadOnlyList<ItemIoEntry> source, List<ItemIoEntry> target)
    {
        if (source == null || target == null)
        {
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            target.Add(source[i]);
        }
    }

    private InputOutputModule ResolveParentInputOutputModule()
    {
        return parentInputOutputModuleItem != null
            ? parentInputOutputModuleItem.mapObject as InputOutputModule
            : null;
    }

    public virtual void GetObjectInfoStatus(out string statusText, out bool isProducing)
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
        bool blockedByEnergy = false;
        bool blockedByTargetFilter = false;
        bool hasFilterAllowedRecipe = false;

        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(recipeIndex, out _, out _, out int outputItemId, out _))
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

            bool missingInputArea = false;
            if (!TryGetInputOutputPair(recipeIndex, out InputOutputPair pair)
                || !HasAllRecipeInputs(pair, out missingInputArea))
            {
                if (missingInputArea)
                    blockedByInputArea = true;
                else
                    blockedByInputItem = true;
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
            || !installedDefinition.UsesEnergyType(ItemDefinition.EnergyType.Burn)
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
            ItemDefinition.EnergyType.Burn);
        return true;
    }

    public bool TryGetObjectInfoEnergyUseRate(
        out ItemDefinition.EnergyType energyType,
        out float amountPerSecond)
    {
        return TryGetObjectInfoEnergyUseRate(0, out energyType, out amountPerSecond);
    }

    public bool TryGetObjectInfoFluidStorageItemId(out int fluidItemId)
    {
        fluidItemId = StoredFluidItemId >= 0
            ? StoredFluidItemId
            : ResolvePreferredFluidInputItemId();
        return fluidItemId >= 0;
    }

    public int GetObjectInfoEnergyUseRateCount()
    {
        return ResolveObjectInfoEnergyUseRates(-1, out _, out _);
    }

    public bool TryGetObjectInfoEnergyUseRate(
        int displayIndex,
        out ItemDefinition.EnergyType energyType,
        out float amountPerSecond)
    {
        energyType = ItemDefinition.EnergyType.None;
        amountPerSecond = 0f;
        return displayIndex >= 0
               && ResolveObjectInfoEnergyUseRates(displayIndex, out energyType, out amountPerSecond)
               > displayIndex;
    }

    private int ResolveObjectInfoEnergyUseRates(
        int requestedDisplayIndex,
        out ItemDefinition.EnergyType requestedEnergyType,
        out float requestedAmountPerSecond)
    {
        requestedEnergyType = ItemDefinition.EnergyType.None;
        requestedAmountPerSecond = 0f;

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return 0;
        }

        int resultCount = 0;
        int addedTypeMask = 0;
        int requirementCount = installedDefinition.UseEnergyRequirementCount;
        for (int i = 0; i < requirementCount; i++)
        {
            if (!installedDefinition.TryGetUseEnergyRequirement(
                    i,
                    out ItemDefinition.EnergyUseRequirement requirement)
                || requirement.energyType == ItemDefinition.EnergyType.None
                || requirement.useEnergyAmount <= 0f)
            {
                continue;
            }

            int typeBit = 1 << (int)requirement.energyType;
            if ((addedTypeMask & typeBit) != 0)
            {
                continue;
            }

            addedTypeMask |= typeBit;
            if (resultCount++ != requestedDisplayIndex)
            {
                continue;
            }

            requestedEnergyType = requirement.energyType;
            requestedAmountPerSecond = ItemDefinition.ResolveUseEnergyRatePerSecond(
                installedDefinition,
                requestedEnergyType);
        }

        return resultCount;
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

        int recipeCount = GetEffectiveRecipeCount();
        for (int i = 0; i < recipeCount; i++)
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
        int recipeCount = GetEffectiveRecipeCount();
        for (int i = 0; i < recipeCount; i++)
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

        if (!TryGetInputOutputPair(recipeIndex, out InputOutputPair pair)
            || pair.inputs == null
            || pair.inputs.Count <= 0)
        {
            return false;
        }

        ItemIoEntry inputEntry = pair.inputs[0];
        inputItemId = inputEntry.itemDefinition != null ? inputEntry.itemDefinition.id : -1;
        inputCount = inputEntry.ResolvedItemCount;

        if (pair.outputs != null && pair.outputs.Count > 0)
        {
            ItemIoEntry outputEntry = pair.outputs[0];
            outputItemId = outputEntry.itemDefinition != null ? outputEntry.itemDefinition.id : -1;
            outputCount = outputEntry.ResolvedItemCount;
        }

        return inputItemId >= 0;
    }

    protected void AppendRuntimeInputItemAreaCoordinates(int itemId, List<Vector2Int> coordinates)
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
        runtimeAreaVisitedCoordinates.Clear();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!runtimeAreaVisitedCoordinates.Add(coordinate))
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
            ? Mathf.Min(defaultAreaCapacity, Mathf.Max(1, runtimeAreaVisitedCoordinates.Count))
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

        if (RequiresUniqueRectGridPlacement(blockType))
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

        int sourceIndex = FindRectGridPlacementIndex(sourceCell.x, sourceCell.y);
        if (sourceIndex < 0)
        {
            return;
        }

        int targetIndex = FindRectGridPlacementIndex(targetCell.x, targetCell.y);
        RectGridBlockPlacement sourcePlacement = rectGridPlacements[sourceIndex];
        sourcePlacement.x = targetCell.x;
        sourcePlacement.y = targetCell.y;
        if (targetIndex >= 0)
        {
            RectGridBlockPlacement targetPlacement = rectGridPlacements[targetIndex];
            targetPlacement.x = sourceCell.x;
            targetPlacement.y = sourceCell.y;
            rectGridPlacements[targetIndex] = targetPlacement;
        }

        rectGridPlacements[sourceIndex] = sourcePlacement;
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
        bool hasUniqueOutput = false;
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
            else if (IsUniqueOutputRectGridBlockType(placement.blockType))
            {
                if (hasUniqueOutput)
                {
                    continue;
                }

                hasUniqueOutput = true;
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

    private void RemoveUniqueRectGridBlockGroup(RectGridBlockType blockType)
    {
        if (IsInputEnergyBlockType(blockType))
        {
            RemoveRectGridBlocks(IsInputEnergyBlockType);
            return;
        }

        if (IsUniqueOutputRectGridBlockType(blockType))
        {
            RemoveRectGridBlocks(IsUniqueOutputRectGridBlockType);
        }
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

    private static bool RequiresUniqueRectGridPlacement(RectGridBlockType blockType)
    {
        return IsInputEnergyBlockType(blockType)
            || IsUniqueOutputRectGridBlockType(blockType);
    }

    private static bool IsUniqueOutputRectGridBlockType(RectGridBlockType blockType)
    {
        return blockType == RectGridBlockType.Output
            || blockType == RectGridBlockType.DoublePipeOutputItem;
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
        bool energyRequired = RequiresOperationalEnergy(installedDefinition);
        long acceptedEnergyUnits = 0L;
        if (energyRequired)
        {
            if (!TryConsumeOperatingEnergy(deltaTime, out float consumedEnergy))
            {
                return;
            }

            acceptedEnergyUnits = DeterministicSimulationUnits.FromFloat(consumedEnergy);
        }

        long completeEnergyUnits = energyRequired
            ? DeterministicSimulationUnits.FromFloat(ResolveCompleteEnergy(installedDefinition))
            : 0L;
        long energyRateUnits = energyRequired
            ? DeterministicSimulationUnits.FromFloat(
                Mathf.Max(0.0001f, ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition)))
            : 0L;
        if (production.Advance(
                DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime),
                energyRequired,
                acceptedEnergyUnits,
                completeEnergyUnits,
                energyRateUnits))
        {
            TryCompleteActiveCraft();
        }
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
            if (!TryGetRecipePair(recipeIndex, out _, out _, out int outputItemId, out int outputCount))
            {
                continue;
            }

            if (!IsRecipeOutputAvailable(outputItemId))
            {
                continue;
            }

            if (!TryGetInputOutputPair(recipeIndex, out InputOutputPair pair)
                || !HasAllRecipeInputs(pair, out _))
            {
                continue;
            }

            if (!TryEnsureCraftStartEnergy(installedDefinition))
            {
                continue;
            }

            if (!ConsumeRecipeInputs(pair))
            {
                continue;
            }

            BeginActiveCraft(recipeIndex, outputItemId, outputCount, installedDefinition);
            return;
        }
    }

    private bool HasAllRecipeInputs(InputOutputPair pair, out bool missingArea)
    {
        missingArea = false;
        if (pair?.inputs == null || pair.inputs.Count == 0)
        {
            missingArea = true;
            return false;
        }

        for (int inputIndex = 0; inputIndex < pair.inputs.Count; inputIndex++)
        {
            int itemId = pair.inputs[inputIndex].itemDefinition != null
                ? pair.inputs[inputIndex].itemDefinition.id
                : -1;
            if (itemId < 0)
            {
                missingArea = true;
                return false;
            }

            if (!IsFirstRecipeInputWithItemId(pair.inputs, inputIndex, itemId))
            {
                continue;
            }

            int requiredCount = CountRecipeInputItems(pair.inputs, itemId);
            if (CountAvailableRecipeInputItems(itemId, requiredCount, out bool hasArea)
                < requiredCount)
            {
                missingArea = !hasArea;
                return false;
            }
        }

        return true;
    }

    private bool ConsumeRecipeInputs(InputOutputPair pair)
    {
        Vector3 targetPosition = ResolveConsumeTargetWorldPosition();
        for (int inputIndex = 0; inputIndex < pair.inputs.Count; inputIndex++)
        {
            int itemId = pair.inputs[inputIndex].itemDefinition.id;
            if (!IsFirstRecipeInputWithItemId(pair.inputs, inputIndex, itemId))
            {
                continue;
            }

            int remainingCount = CountRecipeInputItems(pair.inputs, itemId);
            for (int areaIndex = 0; areaIndex < runtimeInputItemAreas.Count && remainingCount > 0; areaIndex++)
            {
                RuntimeInputItemArea area = runtimeInputItemAreas[areaIndex];
                if (area.itemId != itemId)
                {
                    continue;
                }

                remainingCount -= ConsumeRuntimeInputAreaCenterObjects(
                    area.coordinate,
                    itemId,
                    remainingCount,
                    targetPosition,
                    inputConsumeMoveInterval);
            }

            if (remainingCount > 0)
            {
                return false;
            }
        }

        return true;
    }

    private int CountAvailableRecipeInputItems(int itemId, int requiredCount, out bool hasArea)
    {
        hasArea = false;
        int availableCount = 0;
        for (int areaIndex = 0; areaIndex < runtimeInputItemAreas.Count; areaIndex++)
        {
            RuntimeInputItemArea area = runtimeInputItemAreas[areaIndex];
            if (area.itemId != itemId)
            {
                continue;
            }

            hasArea = true;
            availableCount += Mathf.Min(
                requiredCount - availableCount,
                GetRuntimeInputAreaCenterItemCount(area.coordinate, itemId));
            if (availableCount >= requiredCount)
            {
                break;
            }
        }

        return availableCount;
    }

    private static bool IsFirstRecipeInputWithItemId(
        IReadOnlyList<ItemIoEntry> inputs,
        int inputIndex,
        int itemId)
    {
        for (int i = 0; i < inputIndex; i++)
        {
            if (inputs[i].itemDefinition != null && inputs[i].itemDefinition.id == itemId)
            {
                return false;
            }
        }

        return true;
    }

    private static int CountRecipeInputItems(IReadOnlyList<ItemIoEntry> inputs, int itemId)
    {
        int count = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            if (inputs[i].itemDefinition != null && inputs[i].itemDefinition.id == itemId)
            {
                count += inputs[i].ResolvedItemCount;
            }
        }

        return count;
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

        return TryEmitOutputItemsToResolvedTarget(
            outputItemId,
            outputCount,
            startWorldPosition,
            outputTarget);
    }

    protected bool TryResolveOutputReservation(
        int outputItemId,
        int outputCount,
        out RuntimeAreaOutputTarget outputTarget,
        out bool usesDistributedSingleItemTargets)
    {
        outputTarget = default;
        ItemDefinition outputDefinition = ResolveItemDefinition(outputItemId);
        usesDistributedSingleItemTargets = outputDefinition != null
                                           && outputDefinition.oneItem
                                           && outputCount > 1;
        return usesDistributedSingleItemTargets
            ? CanDistributeSingleItemStacks(outputItemId, outputCount)
            : TryResolveOutputTarget(outputItemId, outputCount, out outputTarget);
    }

    protected bool TryEmitReservedOutputItems(
        int outputItemId,
        int outputCount,
        Vector3 startWorldPosition,
        RuntimeAreaOutputTarget outputTarget,
        bool usesDistributedSingleItemTargets)
    {
        return usesDistributedSingleItemTargets
            ? TryEmitSingleItemStacks(outputItemId, outputCount, startWorldPosition, true)
            : TryEmitOutputItemsToResolvedTarget(
                outputItemId,
                outputCount,
                startWorldPosition,
                outputTarget);
    }

    private bool TryEmitOutputItemsToResolvedTarget(
        int outputItemId,
        int outputCount,
        Vector3 startWorldPosition,
        RuntimeAreaOutputTarget outputTarget)
    {
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
        Vector3 startWorldPosition,
        bool capacityPrevalidated = false)
    {
        if (!capacityPrevalidated && !CanDistributeSingleItemStacks(outputItemId, outputCount))
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
            if (!TryEmitOutputItemToBlock(
                    outputBlock,
                    outputItemId,
                    startWorldPosition,
                    outputIndex * Mathf.Max(0f, outputMoveInterval),
                    out _))
            {
                return false;
            }
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
                || !RuntimeOutputCoordinateAcceptsItem(coordinate, itemId)
                || !CanAddRuntimeOutputItems(coordinate, itemId, 1, out _, out _))
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
            if (!TryEmitOutputItemToBlock(
                    outputBlock,
                    outputItemId,
                    startWorldPosition,
                    outputIndex * Mathf.Max(0f, outputMoveInterval),
                    out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryEmitOutputItemToBlock(
        Block outputBlock,
        int outputItemId,
        Vector3 startWorldPosition,
        float delay,
        out PortableObject outputObject)
    {
        outputObject = null;
        if (outputBlock == null)
        {
            return false;
        }

        if (outputBlock.IsRuntimeConveyor)
        {
            return outputBlock.TryAddConveyorObjectAnimatedAtPlacement(
                outputItemId,
                startWorldPosition,
                startWorldPosition,
                delay,
                out outputObject,
                forceAnimatedPlacement: true);
        }

        if (!outputBlock.TryAddInputAreaCenterObjectAnimated(
                outputItemId,
                startWorldPosition,
                delay,
                out outputObject))
        {
            return false;
        }

        DroppedItemPickupGate gate = outputObject != null
            ? outputObject.GetComponent<DroppedItemPickupGate>()
            : null;
        gate?.SetAutoPickupBlocked(true);
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

        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            lastOperationalEnergySupplyRatio = 0f;
            return false;
        }

        long deltaTicks = DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime);
        float minimumSupplyRatio = 1f;
        int consumedTypeMask = 0;
        int requirementCount = installedDefinition.UseEnergyRequirementCount;
        for (int i = 0; i < requirementCount; i++)
        {
            if (!installedDefinition.TryGetUseEnergyRequirement(
                    i,
                    out ItemDefinition.EnergyUseRequirement requirement)
                || requirement.energyType == ItemDefinition.EnergyType.None)
            {
                continue;
            }

            int typeIndex = (int)requirement.energyType;
            int typeBit = 1 << typeIndex;
            if ((consumedTypeMask & typeBit) != 0)
            {
                continue;
            }

            consumedTypeMask |= typeBit;
            long requestedUnits = DeterministicSimulationUnits.RateForTicks(
                ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition, requirement.energyType),
                deltaTicks);
            if (requestedUnits <= 0L)
            {
                continue;
            }

            long suppliedUnits;
            if (requirement.energyType == ItemDefinition.EnergyType.Electricity)
            {
                UtilityPole.TryConsumeElectricityUnits(this, requestedUnits, out suppliedUnits);
            }
            else
            {
                suppliedUnits = ConsumeBufferedEnergyUnits(
                    installedDefinition,
                    requirement.energyType,
                    requestedUnits);
            }

            float supplyRatio = Mathf.Clamp01((float)((double)suppliedUnits / requestedUnits));
            minimumSupplyRatio = Mathf.Min(minimumSupplyRatio, supplyRatio);
        }

        float primaryRate = ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition);
        consumedEnergy = primaryRate * Mathf.Max(0f, deltaTime) * minimumSupplyRatio;
        lastOperationalEnergySupplyRatio = minimumSupplyRatio;
        return minimumSupplyRatio > 0f;
    }

    protected bool TryEnsureCraftStartEnergy(ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        int requirementCount = installedDefinition.UseEnergyRequirementCount;
        for (int i = 0; i < requirementCount; i++)
        {
            if (!installedDefinition.TryGetUseEnergyRequirement(
                    i,
                    out ItemDefinition.EnergyUseRequirement requirement)
                || requirement.energyType == ItemDefinition.EnergyType.None
                || requirement.useEnergyAmount <= 0f)
            {
                continue;
            }

            if (requirement.energyType == ItemDefinition.EnergyType.Electricity)
            {
                if (!UtilityPole.HasElectricityAvailable(this))
                {
                    return false;
                }

                continue;
            }

            if (GetBufferedEnergyUnits(installedDefinition, requirement.energyType) <= 0L
                && !TryRefillEnergyStore(installedDefinition, requirement.energyType))
            {
                return false;
            }
        }

        return true;
    }

    private long ConsumeBufferedEnergyUnits(
        ItemDefinition installedDefinition,
        ItemDefinition.EnergyType energyType,
        long requestedUnits)
    {
        long remainingUnits = Math.Max(0L, requestedUnits);
        long consumedUnits = 0L;
        while (remainingUnits > 0L)
        {
            long storedUnits = GetBufferedEnergyUnits(installedDefinition, energyType);
            if (storedUnits <= 0L && !TryRefillEnergyStore(installedDefinition, energyType))
            {
                break;
            }

            storedUnits = GetBufferedEnergyUnits(installedDefinition, energyType);
            long spentUnits = Math.Min(storedUnits, remainingUnits);
            if (spentUnits <= 0L)
            {
                break;
            }

            SetBufferedEnergyUnits(installedDefinition, energyType, storedUnits - spentUnits);
            remainingUnits -= spentUnits;
            consumedUnits += spentUnits;
        }

        if (GetBufferedEnergyUnits(installedDefinition, energyType) <= 0L)
        {
            SetBufferedEnergyGaugeCapacityUnits(installedDefinition, energyType, 0L);
        }

        return consumedUnits;
    }

    private bool TryRefillEnergyStore(
        ItemDefinition installedDefinition,
        ItemDefinition.EnergyType energyType)
    {
        if (!RequiresOperationalEnergy(installedDefinition)
            || energyType == ItemDefinition.EnergyType.None
            || energyType == ItemDefinition.EnergyType.Electricity)
        {
            return false;
        }

        long minimumOperationalEnergyUnits = DeterministicSimulationUnits.FromFloat(
            Mathf.Max(1f, ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition, energyType)));
        if (ItemDefinition.IsFluidFuelEnergyType(energyType))
        {
            return GetBufferedEnergyUnits(installedDefinition, energyType)
                   >= minimumOperationalEnergyUnits;
        }

        bool consumedAnyEnergyItem = false;
        long storedUnits = GetBufferedEnergyUnits(installedDefinition, energyType);
        while (storedUnits < minimumOperationalEnergyUnits)
        {
            if (!TryConsumeOneEnergyItem(energyType, out int gainedEnergy))
            {
                break;
            }

            storedUnits += DeterministicSimulationUnits.FromFloat(gainedEnergy);
            consumedAnyEnergyItem = true;
        }

        SetBufferedEnergyUnits(installedDefinition, energyType, storedUnits);
        if (consumedAnyEnergyItem)
        {
            SetBufferedEnergyGaugeCapacityUnits(
                installedDefinition,
                energyType,
                Math.Max(
                    storedUnits,
                    DeterministicSimulationUnits.UnitsPerWhole));
        }

        return storedUnits >= minimumOperationalEnergyUnits;
    }

    private static ItemDefinition.EnergyType ResolvePrimaryBufferedEnergyType(ItemDefinition definition)
    {
        if (definition == null)
        {
            return ItemDefinition.EnergyType.None;
        }

        int count = definition.UseEnergyRequirementCount;
        for (int i = 0; i < count; i++)
        {
            if (definition.TryGetUseEnergyRequirement(
                    i,
                    out ItemDefinition.EnergyUseRequirement requirement)
                && requirement.energyType != ItemDefinition.EnergyType.None
                && requirement.energyType != ItemDefinition.EnergyType.Electricity
                && requirement.useEnergyAmount > 0f)
            {
                return requirement.energyType;
            }
        }

        return ItemDefinition.EnergyType.None;
    }

    private long GetBufferedEnergyUnits(
        ItemDefinition definition,
        ItemDefinition.EnergyType energyType)
    {
        if (ItemDefinition.IsFluidFuelEnergyType(energyType))
        {
            int fluidFuelItemId = ResolveFluidFuelItemId(energyType);
            return fluidFuelItemId >= 0 && StoredFluidItemId == fluidFuelItemId
                ? StoredFluidUnits
                : 0L;
        }

        int typeIndex = (int)energyType;
        if (energyType == ResolvePrimaryBufferedEnergyType(definition))
        {
            return Math.Max(0L, storedEnergyUnits);
        }

        return typeIndex > 0 && typeIndex < secondaryStoredEnergyUnitsByType.Length
            ? Math.Max(0L, secondaryStoredEnergyUnitsByType[typeIndex])
            : 0L;
    }

    private void SetBufferedEnergyUnits(
        ItemDefinition definition,
        ItemDefinition.EnergyType energyType,
        long value)
    {
        value = Math.Max(0L, value);
        if (ItemDefinition.IsFluidFuelEnergyType(energyType))
        {
            // Fluid fuels are measured directly in liters, so no duplicate
            // buffered-energy value is kept for them.
            int fluidFuelItemId = ResolveFluidFuelItemId(energyType);
            long currentUnits = GetBufferedEnergyUnits(definition, energyType);
            if (fluidFuelItemId >= 0 && value < currentUnits)
            {
                SetStoredFluidUnits(
                    fluidFuelItemId,
                    value,
                    GetStoredFluidTemperatureCelsius(fluidFuelItemId));
            }

            return;
        }

        if (energyType == ResolvePrimaryBufferedEnergyType(definition))
        {
            storedEnergyUnits = value;
            return;
        }

        int typeIndex = (int)energyType;
        if (typeIndex > 0 && typeIndex < secondaryStoredEnergyUnitsByType.Length)
        {
            secondaryStoredEnergyUnitsByType[typeIndex] = value;
        }
    }

    private void SetBufferedEnergyGaugeCapacityUnits(
        ItemDefinition definition,
        ItemDefinition.EnergyType energyType,
        long value)
    {
        value = Math.Max(0L, value);
        if (ItemDefinition.IsFluidFuelEnergyType(energyType))
        {
            return;
        }

        if (energyType == ResolvePrimaryBufferedEnergyType(definition))
        {
            energyGaugeCapacityUnits = value;
            return;
        }

        int typeIndex = (int)energyType;
        if (typeIndex > 0 && typeIndex < secondaryEnergyGaugeCapacityUnitsByType.Length)
        {
            secondaryEnergyGaugeCapacityUnitsByType[typeIndex] = value;
        }
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
                if (!RuntimeOutputCoordinateAcceptsItem(coordinate, outputItemId)
                    || !CanAddRuntimeOutputItems(
                        coordinate,
                        outputItemId,
                        outputCount,
                        out Block block,
                        out bool useSavedCenterStack))
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

    private bool CanAddRuntimeOutputItems(
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

        if (!useSavedCenterStack && block != null && block.IsRuntimeConveyor)
        {
            // A producer can sleep while this belt cell is full. Keep its cell at
            // a transport boundary so a later lane vacancy is observable and can
            // wake the producer instead of remaining hidden inside a packed run.
            block.EnsureConveyorTransportInteractionBoundary();
            return InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(coordinate, itemId)
                   && block.CanAddConveyorObjects(count);
        }

        // An unloaded conveyor has its own lane state. Never write machine output
        // into the unrelated saved center stack when those lanes are unavailable.
        if (useSavedCenterStack && CoordinateHasSavedConveyor(coordinate))
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

    private bool CoordinateHasSavedConveyor(Vector2Int coordinate)
    {
        BlockStateStore stateStore = ResolveBlockStateStore();
        if (stateStore == null
            || !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int anchorCoordinate)
            || !stateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState))
        {
            return false;
        }

        ItemDefinition definition = ResolveItemDefinition(installationState.itemId);
        return definition != null && definition.mapObject is ConveyorBelt;
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
        runtimeAreaVisitedCoordinates.Clear();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!runtimeAreaVisitedCoordinates.Add(coordinate))
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

        runtimeAreaVisitedCoordinates.Clear();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!runtimeAreaVisitedCoordinates.Add(coordinate))
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
                startWorldPosition = block.WorldPosition;
            }

            sourceBlock = block;
            return true;
        }

        return false;
    }

    private bool TryDrainOneOutputAreaItemToConveyor(out bool hasStoredOutput)
    {
        hasStoredOutput = false;
        for (int i = 0; i < runtimeOutputCoordinates.Count; i++)
        {
            if (TryGetLoadedBlock(runtimeOutputCoordinates[i], out Block block)
                && block != null
                && block.IsRuntimeConveyor
                && block.HasInputAreaCenterObjects())
            {
                hasStoredOutput = true;
                if (block.TryTransferOneInputAreaCenterObjectToConveyor(forceAnimatedPlacement: true))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryGetRecipePair(int recipeIndex, out int inputItemId, out int inputCount, out int outputItemId, out int outputCount)
    {
        inputItemId = -1;
        inputCount = 0;
        outputItemId = -1;
        outputCount = 0;

        if (!TryGetInputOutputPair(recipeIndex, out InputOutputPair pair)
            || pair.inputs == null
            || pair.inputs.Count <= 0
            || pair.outputs == null
            || pair.outputs.Count <= 0)
        {
            return false;
        }

        ItemIoEntry inputEntry = pair.inputs[0];
        ItemIoEntry outputEntry = pair.outputs[0];
        inputItemId = inputEntry.itemDefinition != null ? inputEntry.itemDefinition.id : -1;
        outputItemId = outputEntry.itemDefinition != null ? outputEntry.itemDefinition.id : -1;
        inputCount = inputEntry.ResolvedItemCount;
        outputCount = outputEntry.ResolvedItemCount;
        return inputItemId >= 0 && outputItemId >= 0;
    }

    protected bool TryGetInputOutputPair(int pairIndex, out InputOutputPair pair)
    {
        EnsureEffectivePairData();
        if (pairIndex < 0 || pairIndex >= effectiveInputOutputPairs.Count)
        {
            pair = null;
            return false;
        }

        pair = effectiveInputOutputPairs[pairIndex];
        return pair != null;
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

    protected int GetRuntimeInputAreaCenterItemCount(
        Vector2Int coordinate,
        int itemId = -1,
        bool respectBoxMinimumRetainedCount = true)
    {
        if (!TryResolveRuntimeAreaBlock(coordinate, out Block block, out bool useSavedCenterStack))
        {
            return 0;
        }

        if (useSavedCenterStack)
        {
            BlockStateStore stateStore = ResolveBlockStateStore();
            if (stateStore == null)
            {
                return 0;
            }

            return respectBoxMinimumRetainedCount
                ? stateStore.GetSavedCenterExtractableItemCount(coordinate, itemId)
                : stateStore.GetSavedCenterItemCount(coordinate, itemId);
        }

        return block != null && block.Type == Block.BlockType.Ground
            ? Mathf.Max(0, block.GetInputAreaCenterItemCount(itemId)
                - (respectBoxMinimumRetainedCount
                   && block.MapObject is BoxObject box
                    ? box.MinimumRetainedItemCount
                    : 0))
            : 0;
    }

    protected int ConsumeRuntimeInputAreaCenterObjects(
        Vector2Int coordinate,
        int itemId,
        int count,
        Vector3 consumeTargetWorldPosition,
        float moveInterval,
        bool animateVirtualizedConsumption = false,
        bool respectBoxMinimumRetainedCount = true)
    {
        if (itemId < 0 || count <= 0)
        {
            return 0;
        }

        count = Mathf.Min(
            count,
            GetRuntimeInputAreaCenterItemCount(
                coordinate,
                itemId,
                respectBoxMinimumRetainedCount));
        if (count <= 0)
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

        cachedInstalledDefinition = BoundItemDefinition != null ? BoundItemDefinition : ResolveItemDefinition(itemId);
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
               || string.Equals(itemName, "Oil", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemName, "Crude Oil", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemName, "Diesel", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemName, "Diesel Oil", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemName, "Heavy Oil", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemName, "Petroleum gas", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetFluidFuelEnergyType(
        ItemDefinition definition,
        out ItemDefinition.EnergyType energyType)
    {
        energyType = ItemDefinition.EnergyType.None;
        if (definition == null)
        {
            return false;
        }

        int count = definition.UseEnergyRequirementCount;
        for (int i = 0; i < count; i++)
        {
            if (definition.TryGetUseEnergyRequirement(
                    i,
                    out ItemDefinition.EnergyUseRequirement requirement)
                && ItemDefinition.IsFluidFuelEnergyType(requirement.energyType)
                && requirement.useEnergyAmount > 0f)
            {
                energyType = requirement.energyType;
                return true;
            }
        }

        return false;
    }

    private int ResolveFluidFuelItemId(ItemDefinition.EnergyType energyType)
    {
        if (!ItemDefinition.IsFluidFuelEnergyType(energyType))
        {
            return -1;
        }

        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        List<ItemDefinition> definitions = itemManager != null
            ? itemManager.ItemDefinitions
            : null;
        int definitionCount = definitions != null ? definitions.Count : 0;
        if (cachedFluidFuelItemManager != itemManager
            || cachedFluidFuelDefinitionCount != definitionCount)
        {
            cachedFluidFuelItemManager = itemManager;
            cachedFluidFuelDefinitionCount = definitionCount;
            Array.Fill(cachedFluidFuelItemIdsByType, -1);
            for (int i = 0; i < definitionCount; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null
                    || definition.id < 0
                    || !ItemDefinition.IsFluidFuelEnergyType(definition.energyType))
                {
                    continue;
                }

                int typeIndex = (int)definition.energyType;
                if (cachedFluidFuelItemIdsByType[typeIndex] < 0)
                {
                    cachedFluidFuelItemIdsByType[typeIndex] = definition.id;
                }
            }
        }

        int requestedTypeIndex = (int)energyType;
        return requestedTypeIndex >= 0 && requestedTypeIndex < cachedFluidFuelItemIdsByType.Length
            ? cachedFluidFuelItemIdsByType[requestedTypeIndex]
            : -1;
    }

    protected static bool RequiresOperationalEnergy(ItemDefinition installedDefinition)
    {
        return ItemDefinition.TryGetPrimaryUseEnergyRequirement(installedDefinition, out _);
    }

    protected static bool RequiresElectricOperationalEnergy(ItemDefinition installedDefinition)
    {
        return RequiresOperationalEnergy(installedDefinition)
               && installedDefinition.UsesEnergyType(ItemDefinition.EnergyType.Electricity);
    }

    protected bool HasOperationalEnergyAvailable(ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        int count = installedDefinition.UseEnergyRequirementCount;
        int checkedTypeMask = 0;
        for (int i = 0; i < count; i++)
        {
            if (!installedDefinition.TryGetUseEnergyRequirement(
                    i,
                    out ItemDefinition.EnergyUseRequirement requirement)
                || requirement.energyType == ItemDefinition.EnergyType.None
                || requirement.useEnergyAmount <= 0f)
            {
                continue;
            }

            int typeBit = 1 << (int)requirement.energyType;
            if ((checkedTypeMask & typeBit) != 0)
            {
                continue;
            }

            checkedTypeMask |= typeBit;
            if (requirement.energyType == ItemDefinition.EnergyType.Electricity)
            {
                if (!UtilityPole.HasElectricityAvailable(this))
                {
                    return false;
                }
            }
            else if (GetBufferedEnergyUnits(installedDefinition, requirement.energyType) <= 0L
                     && !HasUsableEnergyItem(requirement.energyType))
            {
                return false;
            }
        }

        return true;
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

        using (MapObjectTickProfiler.SampleNamed(
                   "Simulation",
                   nameof(InputOutputModule),
                   "Fluid Output Storage Search"))
        {
            if (TryGetCachedFluidOutputStorage(fluidItemId, fluidLiters, out targetStorage))
            {
                fluidOutputCapacityBlocked = false;
                return true;
            }

            if (!EnsureFluidOutputStorageCache()
                || !TrySelectFluidOutputStorageFromCache(
                    fluidItemId,
                    fluidLiters,
                    out targetStorage))
            {
                fluidOutputCapacityBlocked = cachedFluidOutputConnections.Count > 0;
                return false;
            }
        }

        fluidOutputCapacityBlocked = false;
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
            || runtimeOutputCoordinates.Count <= 0)
        {
            return false;
        }

        using var searchSample = MapObjectTickProfiler.SampleNamed(
            "Simulation",
            nameof(InputOutputModule),
            "Fluid Output Storage Search");
        if (!EnsureFluidOutputStorageCache())
        {
            fluidOutputCapacityBlocked = false;
            return false;
        }

        for (int i = 0; i < cachedFluidOutputConnections.Count; i++)
        {
            FluidOutputConnection connection = cachedFluidOutputConnections[i];
            if (!CanUseFluidOutputConnectionWithAnySpace(connection, fluidItemId))
            {
                continue;
            }

            availableLiters += GetFluidOutputConnectionAvailableLiters(connection, fluidItemId);
            if (availableLiters + 0.0001f >= maxLiters)
            {
                availableLiters = maxLiters;
                fluidOutputCapacityBlocked = false;
                return true;
            }
        }

        fluidOutputCapacityBlocked = cachedFluidOutputConnections.Count > 0;
        return availableLiters > 0.0001f;
    }

    protected bool TryGetFluidOutputAvailableLitersAtCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float maximumLiters,
        out float availableLiters,
        FluidTransferPreview preview = null)
    {
        availableLiters = 0f;
        if (!IsFluidItemId(fluidItemId) || maximumLiters <= 0.0001f)
        {
            return false;
        }

        if (preview == null)
        {
            preview = fluidTransferPreviewScratch;
            preview.Clear();
        }
        FluidPortConnectionCache cache = GetFluidPortConnectionCache(
            fluidOutputPortConnectionCaches,
            coordinate,
            false);
        for (int i = 0; i < cache.Connections.Count; i++)
        {
            FluidOutputConnection connection = cache.Connections[i];
            if (!CanUseFluidOutputConnectionWithAnySpace(connection, fluidItemId))
            {
                continue;
            }

            availableLiters += PreviewFluidTransfer(connection,
                GetFluidOutputConnectionAvailableLiters(connection, fluidItemId),
                maximumLiters - availableLiters, false, preview);
            if (availableLiters + 0.0001f >= maximumLiters)
            {
                availableLiters = maximumLiters;
                return true;
            }
        }

        return availableLiters > 0.0001f;
    }

    protected bool TryEmitFluidOutputAtCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float requestedLiters,
        float temperatureCelsius,
        out float acceptedLiters)
    {
        acceptedLiters = 0f;
        if (!IsFluidItemId(fluidItemId) || requestedLiters <= 0.0001f)
        {
            return false;
        }

        FluidPortConnectionCache cache = GetFluidPortConnectionCache(
            fluidOutputPortConnectionCaches,
            coordinate,
            false);
        for (int i = 0; i < cache.Connections.Count; i++)
        {
            float remainingLiters = requestedLiters - acceptedLiters;
            if (remainingLiters <= 0.0001f)
            {
                break;
            }

            FluidOutputConnection connection = cache.Connections[i];
            if (!CanUseFluidOutputConnectionWithAnySpace(connection, fluidItemId))
            {
                continue;
            }

            float transferLiters = Mathf.Min(
                remainingLiters,
                GetFluidOutputConnectionAvailableLiters(connection, fluidItemId));
            if (!TryAddFluidToOutputConnection(
                    connection,
                    fluidItemId,
                    transferLiters,
                    temperatureCelsius,
                    out float acceptedThisStorage)
                || acceptedThisStorage <= 0.0001f)
            {
                continue;
            }

            acceptedLiters += acceptedThisStorage;
        }

        RecordFluidNetworkOutput(fluidItemId, acceptedLiters);
        return acceptedLiters + 0.0001f >= requestedLiters;
    }

    private static float ResolvePumpTransportRatio(Pump pump, float sourceLitersPerSecond)
    {
        return pump != null && sourceLitersPerSecond > 0f
            ? Pump.LimitTransportRate(pump, sourceLitersPerSecond) / sourceLitersPerSecond : 1f;
    }

    protected float ResolveFluidOutputTransportRetention(int fluidItemId, float sourceLitersPerSecond = 0f)
    {
        if (!IsFluidItemId(fluidItemId)
            || runtimeOutputCoordinates == null
            || runtimeOutputCoordinates.Count <= 0)
        {
            return 1f;
        }

        using var searchSample = MapObjectTickProfiler.SampleNamed(
            "Simulation",
            nameof(InputOutputModule),
            "Fluid Output Storage Search");
        return EnsureFluidOutputStorageCache()
               && TrySelectFluidOutputConnectionWithAnySpaceFromCache(
                   fluidItemId,
                   out FluidOutputConnection connection)
               && connection.Storage != null
            ? CalculateFluidPressureRetention(connection.PipeDistance)
              * ResolvePumpTransportRatio(connection.PressurePump, sourceLitersPerSecond)
            : 1f;
    }

    protected float ResolveFluidOutputTransportRetentionAtCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float sourceLitersPerSecond = 0f)
    {
        if (!IsFluidItemId(fluidItemId))
        {
            return 0f;
        }

        FluidPortConnectionCache cache = GetFluidPortConnectionCache(
            fluidOutputPortConnectionCaches,
            coordinate,
            false);
        float bestRetention = 0f;
        for (int i = 0; i < cache.Connections.Count; i++)
        {
            FluidOutputConnection connection = cache.Connections[i];
            if (CanUseFluidOutputConnectionWithAnySpace(connection, fluidItemId))
            {
                bestRetention = Mathf.Max(
                    bestRetention,
                    CalculateFluidPressureRetention(connection.PipeDistance)
                    * ResolvePumpTransportRatio(connection.PressurePump, sourceLitersPerSecond));
            }
        }

        return bestRetention;
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
            || runtimeOutputCoordinates.Count <= 0)
        {
            return false;
        }

        using (MapObjectTickProfiler.SampleNamed(
                   "Simulation",
                   nameof(InputOutputModule),
                   "Fluid Output Storage Search"))
        {
            if (!EnsureFluidOutputStorageCache())
            {
                fluidOutputCapacityBlocked = false;
                return false;
            }
        }

        int maxAttempts = Mathf.Max(1, cachedFluidOutputConnections.Count);
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float remainingLiters = requestedLiters - acceptedLiters;
            if (remainingLiters <= 0.0001f)
            {
                break;
            }

            FluidOutputConnection targetConnection;
            using (MapObjectTickProfiler.SampleNamed(
                       "Simulation",
                       nameof(InputOutputModule),
                       "Fluid Output Storage Search"))
            {
                if (!TrySelectFluidOutputConnectionWithAnySpaceFromCache(
                        fluidItemId,
                        out targetConnection)
                    || targetConnection.Storage == null)
                {
                    break;
                }
            }

            float litersToEmit = Mathf.Min(
                remainingLiters,
                GetFluidOutputConnectionAvailableLiters(targetConnection, fluidItemId));
            if (litersToEmit <= 0.0001f)
            {
                break;
            }

            using (MapObjectTickProfiler.SampleNamed(
                       "Simulation",
                       nameof(InputOutputModule),
                       "Fluid Output Transfer"))
            {
                if (!TryAddFluidToOutputConnection(
                        targetConnection,
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
        }

        fluidOutputCapacityBlocked = acceptedLiters + 0.0001f < requestedLiters
                                     && cachedFluidOutputConnections.Count > 0;
        RecordFluidNetworkOutput(fluidItemId, acceptedLiters);
        return acceptedLiters > 0.0001f;
    }

    protected bool TryTransferFluidFromStorageToConnectedStorage(
        InstallationObject sourceStorage,
        int fluidItemId,
        float requestedLiters,
        float temperatureCelsius,
        IReadOnlyList<Vector2Int> seedCoordinates,
        out float acceptedLiters)
    {
        acceptedLiters = 0f;
        if (sourceStorage == null
            || sourceStorage == this
            || !IsFluidItemId(fluidItemId)
            || requestedLiters <= 0.0001f
            || seedCoordinates == null
            || seedCoordinates.Count <= 0
            || !sourceStorage.CanProvideFluidItem(fluidItemId, 0.0001f))
        {
            return false;
        }

        using (MapObjectTickProfiler.SampleNamed(
                   "Simulation",
                   nameof(InputOutputModule),
                   "Fluid Output Storage Search"))
        {
            if (!EnsureFluidOutputStorageCache(seedCoordinates))
            {
                return false;
            }
        }

        FluidOutputConnection bestConnection = default;
        float bestFillRatio = float.PositiveInfinity;
        bool foundTarget = false;
        for (int i = 0; i < cachedFluidOutputConnections.Count; i++)
        {
            FluidOutputConnection connection = cachedFluidOutputConnections[i];
            InstallationObject storage = connection.Storage;
            if (storage == sourceStorage
                || !CanUseFluidOutputConnectionWithAnySpace(connection, fluidItemId))
            {
                continue;
            }

            float fillRatio = GetFluidOutputConnectionFillRatio(connection, fluidItemId);
            if (foundTarget && fillRatio >= bestFillRatio)
            {
                continue;
            }

            bestConnection = connection;
            bestFillRatio = fillRatio;
            foundTarget = true;
        }

        if (!foundTarget || bestConnection.Storage == null)
        {
            return false;
        }

        float transferLiters = Mathf.Min(
            requestedLiters * CalculateFluidPressureRetention(bestConnection.PipeDistance),
            sourceStorage.StoredFluidLiters,
            GetFluidOutputConnectionAvailableLiters(bestConnection, fluidItemId));
        transferLiters = LimitFluidOutputTransfer(bestConnection, transferLiters);
        if (transferLiters <= 0.0001f
            || !sourceStorage.TryConsumeFluidLiters(
                fluidItemId,
                transferLiters,
                out float consumedLiters)
            || consumedLiters <= 0.0001f)
        {
            return false;
        }

        TryAddFluidToOutputConnection(
            bestConnection,
            fluidItemId,
            consumedLiters,
            temperatureCelsius,
            out acceptedLiters);
        float rejectedLiters = consumedLiters - Mathf.Max(0f, acceptedLiters);
        if (rejectedLiters > 0.0001f)
        {
            sourceStorage.RestoreUnacceptedFluid(fluidItemId, rejectedLiters, temperatureCelsius);
        }

        return acceptedLiters > 0.0001f;
    }

    private bool EnsureFluidOutputStorageCache()
    {
        return EnsureFluidOutputStorageCache(runtimeOutputCoordinates);
    }

    private bool EnsureFluidOutputStorageCache(IReadOnlyList<Vector2Int> seedCoordinates)
    {
        if (cachedFluidOutputConnectionsTopologyVersion == fluidTopologyVersion
            && CoordinatesMatch(cachedFluidOutputSeedCoordinates, seedCoordinates))
        {
            return cachedFluidOutputConnections.Count > 0;
        }

        cachedFluidOutputConnections.Clear();
        cachedFluidOutputConnectionIndices.Clear();
        cachedFluidOutputSeedCoordinates.Clear();
        AddUniqueCoordinates(seedCoordinates, cachedFluidOutputSeedCoordinates);

        connectedFluidSearchQueue.Clear();
        connectedFluidSearchPipeCounts.Clear();
        connectedFluidSearchPumps.Clear();
        connectedFluidSearchCurrentPump = null;
        connectedFluidStorageCandidates.Clear();

        if (this is Boiler boiler)
        {
            BuildDirectedBoilerSteamOutputCache(boiler);
        }

        for (int i = 0; i < cachedFluidOutputSeedCoordinates.Count; i++)
        {
            Vector2Int seedCoordinate = cachedFluidOutputSeedCoordinates[i];
            EnqueueConnectedFluidSearchCoordinate(
                seedCoordinate,
                TryGetConnectedPipeAtCoordinate(seedCoordinate, out _, out _, out _) ? 1 : 0);
        }

        while (connectedFluidSearchQueue.Count > 0)
        {
            ConnectedFluidSearchNode searchNode = connectedFluidSearchQueue.Dequeue();
            Vector2Int coordinate = searchNode.Coordinate;
            if (!connectedFluidSearchPipeCounts.TryGetValue(
                    coordinate,
                    out connectedFluidSearchCurrentPipeCount)
                || connectedFluidSearchCurrentPipeCount != searchNode.PipeCount)
            {
                continue;
            }

            connectedFluidSearchPumps.TryGetValue(coordinate, out connectedFluidSearchCurrentPump);
            AddFluidOutputStorageCacheCandidatesAtCoordinate(
                coordinate,
                Mathf.Max(0, connectedFluidSearchCurrentPipeCount - 1));

            bool isOutputSeed = ContainsCoordinate(cachedFluidOutputSeedCoordinates, coordinate);
            bool hasPipe = TryGetConnectedPipeAtCoordinate(
                coordinate,
                out Pipe pipe,
                out Quaternion pipeRotation,
                out PipeRuntimeRecord pipeRecord);
            TryResolveConnectedFluidSearchStorageAtCoordinate(
                coordinate,
                out InstallationObject fluidStorage,
                out bool storageIsPipeArea);
            // A reservoir terminates this route; transfers must use its actual stock.
            if (fluidStorage is Fluidtank)
            {
                continue;
            }
            EnqueueFluidStoragePipePassCoordinatesAt(coordinate);
            bool hasPassiveFluidPass = TryEnqueuePassiveFluidPassesAt(
                coordinate,
                out int passivePassExternalDirectionMask);
            bool hasPumpPressureResetPass = TryEnqueuePumpPressureResetPassesAt(
                coordinate,
                false,
                out int pumpPassExternalDirectionMask);

            if (!isOutputSeed
                && !hasPipe
                && !storageIsPipeArea
                && !hasPassiveFluidPass
                && !hasPumpPressureResetPass)
            {
                continue;
            }

            for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = FluidCardinalDirections[directionIndex];
                bool pipeConnectsToDirection = hasPipe && HasConnectedPipeConnectionTowards(
                    pipe, pipeRecord, coordinate, pipeRotation, direction);
                if (hasPipe && !hasPumpPressureResetPass && !hasPassiveFluidPass
                    && !pipeConnectsToDirection)
                {
                    continue;
                }

                if ((hasPumpPressureResetPass || hasPassiveFluidPass)
                    && !(hasPumpPressureResetPass && pipeConnectsToDirection)
                    && !DirectionMaskContains(
                        pumpPassExternalDirectionMask | passivePassExternalDirectionMask,
                        directionIndex))
                {
                    continue;
                }

                if (!hasPipe && !hasPumpPressureResetPass && !hasPassiveFluidPass
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
                        out bool canContinueRoute,
                        out bool nextNodeIsPipe))
                {
                    continue;
                }

                int nextPipeCount = connectedFluidSearchCurrentPipeCount
                                    + (nextNodeIsPipe ? 1 : 0);
                AddFluidOutputStorageCacheCandidate(
                    nextStorage,
                    nextCoordinate,
                    Mathf.Max(0, nextPipeCount - 1));

                if (canContinueRoute)
                {
                    EnqueueConnectedFluidSearchCoordinate(nextCoordinate, nextPipeCount);
                }
            }

            if (hasPipe && !hasPumpPressureResetPass && !hasPassiveFluidPass
                && TryGetConnectedPipeRemoteCoordinate(
                    pipe,
                    pipeRecord,
                    coordinate,
                    out Vector2Int remoteCoordinate))
            {
                EnqueueConnectedFluidSearchCoordinate(
                    remoteCoordinate,
                    Pipe.AddRemoteTraversalPipeDistance(
                        connectedFluidSearchCurrentPipeCount,
                        coordinate,
                        remoteCoordinate));
            }
        }

        cachedFluidOutputConnections.Sort(CompareFluidOutputConnectionOrder);
        cachedFluidOutputConnectionIndices.Clear();
        cachedFluidOutputConnectionsTopologyVersion = fluidTopologyVersion;
        return cachedFluidOutputConnections.Count > 0;
    }

    internal static bool IsInDirectedBoilerSteamChain(SteamGenerator generator)
    {
        if (generator == null || !generator.gameObject.activeInHierarchy)
        {
            return false;
        }

        EnsureDirectedBoilerSteamChainCache();
        return directedBoilerSteamChainGenerators.Contains(generator);
    }

    private static void EnsureDirectedBoilerSteamChainCache()
    {
        if (directedBoilerSteamChainTopologyVersion == fluidTopologyVersion)
        {
            return;
        }

        directedBoilerSteamChainGenerators.Clear();
        foreach (InputOutputModule module in activeRuntimeModules)
        {
            if (!(module is Boiler boiler) || !boiler.gameObject.activeInHierarchy)
            {
                continue;
            }

            AddDirectedBoilerSteamChains(boiler, directedBoilerSteamChainGenerators, null);
        }

        directedBoilerSteamChainTopologyVersion = fluidTopologyVersion;
    }

    private void BuildDirectedBoilerSteamOutputCache(Boiler boiler)
    {
        directedSteamChainVisited.Clear();
        AddDirectedBoilerSteamChains(boiler, directedSteamChainVisited, this);
        // Continue the common storage search beyond each validated generator.
        // The directed traversal discovers generators but does not collect tanks.
        foreach (SteamGenerator generator in directedSteamChainVisited)
        {
            if (generator.TryGetRuntimePipePassTail(out Vector2Int tail, out Vector2Int direction)
                && (!TryGetConnectedPipeAtCoordinate(tail, out Pipe pipe,
                        out Quaternion rotation, out PipeRuntimeRecord record)
                    || HasConnectedPipeConnectionTowards(pipe, record, tail, rotation, -direction)))
            {
                EnqueueConnectedFluidSearchCoordinate(tail, 0);
            }
        }
    }

    private static void AddDirectedBoilerSteamChains(
        Boiler boiler,
        HashSet<SteamGenerator> visited,
        InputOutputModule outputCacheOwner)
    {
        if (boiler == null || visited == null)
        {
            return;
        }

        boiler.directedSteamPortSearchQueue.Clear();
        boiler.directedSteamVisitedPorts.Clear();
        boiler.directedSteamPipeSearchQueue.Clear();
        boiler.directedSteamVisitedPipeCoordinates.Clear();

        IReadOnlyList<Vector2Int> outputCoordinates = boiler.runtimeOutputCoordinates;
        for (int outputIndex = 0; outputIndex < outputCoordinates.Count; outputIndex++)
        {
            Vector2Int sourcePortCoordinate = outputCoordinates[outputIndex];
            if (!boiler.TryGetRuntimePipeOutputExternalDirection(
                    sourcePortCoordinate,
                    out Vector2Int flowDirection))
            {
                continue;
            }

            EnqueueDirectedSteamPort(boiler, sourcePortCoordinate, flowDirection);
        }

        while (boiler.directedSteamPortSearchQueue.Count > 0)
        {
            DirectedSteamPort sourcePort = boiler.directedSteamPortSearchQueue.Dequeue();
            if (!boiler.directedSteamVisitedPorts.Add(sourcePort))
            {
                continue;
            }

            TryAppendDirectedSteamGeneratorAtPort(boiler, sourcePort, visited, outputCacheOwner);
            EnqueueDirectedSteamPipesAtPort(boiler, sourcePort);

            // Pipes are an undirected transport network, while each generator keeps
            // a directed inlet/tail. Traverse every reciprocal pipe edge, then hand
            // the flow direction of that edge to the generator inlet check.
            while (boiler.directedSteamPipeSearchQueue.Count > 0)
            {
                Vector2Int pipeCoordinate = boiler.directedSteamPipeSearchQueue.Dequeue();
                if (!boiler.directedSteamVisitedPipeCoordinates.Add(pipeCoordinate)
                    || !boiler.TryGetConnectedPipeAtCoordinate(
                        pipeCoordinate,
                        out Pipe pipe,
                        out Quaternion pipeRotation,
                        out PipeRuntimeRecord pipeRecord))
                {
                    continue;
                }

                for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
                {
                    Vector2Int direction = FluidCardinalDirections[directionIndex];
                    if (!HasConnectedPipeConnectionTowards(
                            pipe,
                            pipeRecord,
                            pipeCoordinate,
                            pipeRotation,
                            direction))
                    {
                        continue;
                    }

                    TryAppendDirectedSteamGeneratorAtPort(
                        boiler,
                        new DirectedSteamPort(pipeCoordinate, direction),
                        visited,
                        outputCacheOwner);

                    Vector2Int nextPipeCoordinate = pipeCoordinate + direction;
                    if (boiler.TryGetConnectedPipeAtCoordinate(
                            nextPipeCoordinate,
                            out Pipe nextPipe,
                            out Quaternion nextPipeRotation,
                            out PipeRuntimeRecord nextPipeRecord)
                        && HasConnectedPipeConnectionTowards(
                            nextPipe,
                            nextPipeRecord,
                            nextPipeCoordinate,
                            nextPipeRotation,
                            -direction))
                    {
                        boiler.directedSteamPipeSearchQueue.Enqueue(nextPipeCoordinate);
                    }
                }

                if (TryGetConnectedPipeRemoteCoordinate(
                        pipe,
                        pipeRecord,
                        pipeCoordinate,
                        out Vector2Int remoteCoordinate))
                {
                    boiler.directedSteamPipeSearchQueue.Enqueue(remoteCoordinate);
                }
            }
        }
    }

    private static void EnqueueDirectedSteamPort(
        Boiler boiler,
        Vector2Int sourcePortCoordinate,
        Vector2Int flowDirection)
    {
        if (boiler == null || flowDirection == Vector2Int.zero)
        {
            return;
        }

        boiler.directedSteamPortSearchQueue.Enqueue(
            new DirectedSteamPort(sourcePortCoordinate, flowDirection));
    }

    private static void TryAppendDirectedSteamGeneratorAtPort(
        Boiler boiler,
        DirectedSteamPort sourcePort,
        HashSet<SteamGenerator> visited,
        InputOutputModule outputCacheOwner)
    {
        if (!TryFindDirectedSteamGenerator(
                sourcePort.Coordinate,
                sourcePort.FlowDirection,
                visited,
                out SteamGenerator generator))
        {
            return;
        }

        visited.Add(generator);
        outputCacheOwner?.AddFluidOutputStorageCacheCandidate(
            generator,
            sourcePort.Coordinate,
            0);
        if (generator.TryGetRuntimePipePassTail(
                out Vector2Int tailCoordinate,
                out Vector2Int tailDirection))
        {
            EnqueueDirectedSteamPort(boiler, tailCoordinate, tailDirection);
        }
    }

    private static void EnqueueDirectedSteamPipesAtPort(
        Boiler boiler,
        DirectedSteamPort sourcePort)
    {
        if (boiler.TryGetConnectedPipeAtCoordinate(
                sourcePort.Coordinate,
                out Pipe overlappingPipe,
                out Quaternion overlappingPipeRotation,
                out PipeRuntimeRecord overlappingPipeRecord)
            && HasConnectedPipeConnectionTowards(
                overlappingPipe,
                overlappingPipeRecord,
                sourcePort.Coordinate,
                overlappingPipeRotation,
                // The pipe receives from the source body, opposite the outgoing flow.
                // Requiring the forward connector incorrectly rejects a corner at the port.
                -sourcePort.FlowDirection))
        {
            boiler.directedSteamPipeSearchQueue.Enqueue(sourcePort.Coordinate);
        }

        Vector2Int adjacentCoordinate = sourcePort.Coordinate + sourcePort.FlowDirection;
        if (boiler.TryGetConnectedPipeAtCoordinate(
                adjacentCoordinate,
                out Pipe adjacentPipe,
                out Quaternion adjacentPipeRotation,
                out PipeRuntimeRecord adjacentPipeRecord)
            && HasConnectedPipeConnectionTowards(
                adjacentPipe,
                adjacentPipeRecord,
                adjacentCoordinate,
                adjacentPipeRotation,
                -sourcePort.FlowDirection))
        {
            boiler.directedSteamPipeSearchQueue.Enqueue(adjacentCoordinate);
        }
    }

    private static bool TryFindDirectedSteamGenerator(
        Vector2Int sourcePortCoordinate,
        Vector2Int flowDirection,
        ISet<SteamGenerator> visited,
        out SteamGenerator generator)
    {
        generator = null;
        if (flowDirection == Vector2Int.zero)
        {
            return false;
        }

        SelectDirectedSteamGeneratorAtCoordinate(
            sourcePortCoordinate - flowDirection,
            sourcePortCoordinate,
            flowDirection,
            visited,
            ref generator);
        SelectDirectedSteamGeneratorAtCoordinate(
            sourcePortCoordinate,
            sourcePortCoordinate,
            flowDirection,
            visited,
            ref generator);
        SelectDirectedSteamGeneratorAtCoordinate(
            sourcePortCoordinate + flowDirection,
            sourcePortCoordinate,
            flowDirection,
            visited,
            ref generator);
        return generator != null;
    }

    private static void SelectDirectedSteamGeneratorAtCoordinate(
        Vector2Int searchCoordinate,
        Vector2Int sourcePortCoordinate,
        Vector2Int flowDirection,
        ISet<SteamGenerator> visited,
        ref SteamGenerator bestGenerator)
    {
        if (registeredRuntimeAreaCoordinates.TryGetValue(
                searchCoordinate,
                out HashSet<InputOutputModule> areaModules))
        {
            SelectDirectedSteamGenerator(
                areaModules,
                sourcePortCoordinate,
                flowDirection,
                visited,
                ref bestGenerator);
        }

        if (registeredRuntimeGridCoordinates.TryGetValue(
                searchCoordinate,
                out HashSet<InputOutputModule> gridModules))
        {
            SelectDirectedSteamGenerator(
                gridModules,
                sourcePortCoordinate,
                flowDirection,
                visited,
                ref bestGenerator);
        }
    }

    private static void SelectDirectedSteamGenerator(
        IEnumerable<InputOutputModule> modules,
        Vector2Int sourcePortCoordinate,
        Vector2Int flowDirection,
        ISet<SteamGenerator> visited,
        ref SteamGenerator bestGenerator)
    {
        if (modules == null)
        {
            return;
        }

        foreach (InputOutputModule module in modules)
        {
            if (!(module is SteamGenerator candidate)
                || !candidate.gameObject.activeInHierarchy
                || visited != null && visited.Contains(candidate)
                || !candidate.CanReceiveSteamFromDirectedPortAtRuntime(
                    sourcePortCoordinate,
                    flowDirection)
                || bestGenerator != null && CompareSimulationOrder(candidate, bestGenerator) >= 0)
            {
                continue;
            }

            bestGenerator = candidate;
        }
    }

    private bool TrySelectFluidOutputStorageFromCache(
        int fluidItemId,
        float fluidLiters,
        out InstallationObject targetStorage)
    {
        targetStorage = null;
        float bestTargetFillRatio = float.PositiveInfinity;
        for (int i = 0; i < cachedFluidOutputConnections.Count; i++)
        {
            InstallationObject storage = cachedFluidOutputConnections[i].Storage;
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
        bool found = TrySelectFluidOutputConnectionWithAnySpaceFromCache(
            fluidItemId,
            out FluidOutputConnection connection);
        targetStorage = connection.Storage;
        return found;
    }

    private bool TrySelectFluidOutputConnectionWithAnySpaceFromCache(
        int fluidItemId,
        out FluidOutputConnection targetConnection)
    {
        targetConnection = default;
        InstallationObject targetStorage = null;
        float bestTargetFillRatio = float.PositiveInfinity;
        for (int i = 0; i < cachedFluidOutputConnections.Count; i++)
        {
            FluidOutputConnection connection = cachedFluidOutputConnections[i];
            InstallationObject storage = connection.Storage;
            if (!CanUseFluidOutputConnectionWithAnySpace(connection, fluidItemId))
            {
                continue;
            }

            float fillRatio = GetFluidOutputConnectionFillRatio(connection, fluidItemId);
            if (targetStorage != null && fillRatio >= bestTargetFillRatio)
            {
                continue;
            }

            targetStorage = storage;
            targetConnection = connection;
            bestTargetFillRatio = fillRatio;
        }

        return targetStorage != null;
    }

    private void AddFluidOutputStorageCacheCandidatesAtCoordinate(
        Vector2Int coordinate,
        int pipeDistance)
    {
        if (TryResolveConnectedFluidStorageBodyAtCoordinate(
                coordinate,
                out InstallationObject bodyStorage))
        {
            AddFluidOutputStorageCacheCandidate(bodyStorage, coordinate, pipeDistance);
        }

        if (TryResolveConnectedFluidStorageAtCoordinate(
                coordinate,
                null,
                out InstallationObject areaStorage))
        {
            AddFluidOutputStorageCacheCandidate(areaStorage, coordinate, pipeDistance);
        }
    }

    private void AddFluidOutputStorageCacheCandidate(
        InstallationObject storage,
        Vector2Int coordinate,
        int pipeDistance)
    {
        bool usesDedicatedStorage = storage is InputOutputModule module
                                    && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(coordinate);
        if (storage == null
            || storage == this
            || (!storage.CanStoreFluid && !usesDedicatedStorage)
            || storage is SteamGenerator generator
            && (!(this is Boiler) || !directedSteamChainVisited.Contains(generator)))
        {
            return;
        }

        FluidStorageEndpointKey endpointKey = new FluidStorageEndpointKey(storage, coordinate);
        if (!cachedFluidOutputConnectionIndices.TryGetValue(endpointKey, out int connectionIndex))
        {
            cachedFluidOutputConnectionIndices.Add(endpointKey, cachedFluidOutputConnections.Count);
            cachedFluidOutputConnections.Add(
                new FluidOutputConnection(storage, coordinate, pipeDistance, connectedFluidSearchCurrentPump));
            return;
        }

        FluidOutputConnection previous = cachedFluidOutputConnections[connectionIndex];
        if (pipeDistance < previous.PipeDistance)
            cachedFluidOutputConnections[connectionIndex] =
                new FluidOutputConnection(storage, coordinate, pipeDistance, connectedFluidSearchCurrentPump);
    }

    private static int CompareFluidOutputConnectionOrder(
        FluidOutputConnection first,
        FluidOutputConnection second)
    {
        int storageOrder = CompareSimulationOrder(first.Storage, second.Storage);
        if (storageOrder != 0)
        {
            return storageOrder;
        }

        int xOrder = first.Coordinate.x.CompareTo(second.Coordinate.x);
        return xOrder != 0
            ? xOrder
            : first.Coordinate.y.CompareTo(second.Coordinate.y);
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

    private bool CanUseFluidOutputConnectionWithAnySpace(
        FluidOutputConnection connection,
        int fluidItemId)
    {
        InstallationObject storage = connection.Storage;
        if (storage == null || storage == this || !storage.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (storage is InputOutputModule module
            && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(connection.Coordinate))
        {
            return module.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(
                       connection.Coordinate, fluidItemId) > 0.0001f
                   && module.CanAcceptDedicatedFluidAtRuntimeCoordinate(
                       connection.Coordinate,
                       fluidItemId,
                       0.0001f);
        }

        return CanUseFluidOutputStorageWithAnySpace(storage, fluidItemId);
    }

    private float GetFluidOutputConnectionAvailableLiters(
        FluidOutputConnection connection, int fluidItemId)
    {
        if (connection.Storage is InputOutputModule module
            && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(connection.Coordinate))
        {
            return Mathf.Max(
                0f,
                module.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(
                    connection.Coordinate, fluidItemId));
        }

        return connection.Storage != null
            ? Mathf.Max(0f, connection.Storage.AvailableFluidStorageLiters)
            : 0f;
    }

    private float GetFluidOutputConnectionFillRatio(
        FluidOutputConnection connection, int fluidItemId)
    {
        if (connection.Storage is InputOutputModule module
            && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(connection.Coordinate))
        {
            return Mathf.Clamp01(
                module.GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(
                    connection.Coordinate, fluidItemId));
        }

        return GetFluidStorageFillRatio(connection.Storage);
    }

    private float LimitFluidOutputTransfer(FluidOutputConnection connection, float requestedLiters)
    {
        Pump pump = connection.PressurePump != null ? connection.PressurePump : this as Pump;
        return pump != null ? pump.LimitTransferVolume(requestedLiters, ManagedUpdateTickIntervalSeconds) : requestedLiters;
    }

    private bool TryAddFluidToOutputConnection(
        FluidOutputConnection connection,
        int fluidItemId,
        float requestedLiters,
        float temperatureCelsius,
        out float acceptedLiters)
    {
        Pump pressurePump = connection.PressurePump != null ? connection.PressurePump : this as Pump;
        requestedLiters = LimitFluidOutputTransfer(connection, requestedLiters);
        acceptedLiters = 0f;
        if (requestedLiters <= 0f) return false;
        bool transferred;
        if (connection.Storage is InputOutputModule module
            && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(connection.Coordinate))
        {
            transferred = module.TryAddDedicatedFluidAtRuntimeCoordinate(
                connection.Coordinate, fluidItemId, requestedLiters, temperatureCelsius, out acceptedLiters);
        }
        else
        {
            transferred = connection.Storage != null && connection.Storage.TryAddFluidLiters(
                fluidItemId, requestedLiters, temperatureCelsius, out acceptedLiters);
        }
        pressurePump?.RecordTransferredVolume(acceptedLiters);
        return transferred;
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

        float energyRate = Mathf.Max(
            0.0001f,
            ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition));
        return Mathf.Max(0.1f, ResolveCompleteEnergy(installedDefinition) / energyRate);
    }

    private long ResolveConsumedEnergyUnitsFromRemainingTicks(
        ItemDefinition installedDefinition,
        long savedRemainingCraftTicks)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return 0L;
        }

        float energyRate = Mathf.Max(0.0001f, ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition));
        float completeEnergy = ResolveCompleteEnergy(installedDefinition);
        long totalDurationTicks = DeterministicSimulationUnits.SecondsToTicks(completeEnergy / energyRate);
        long elapsedTicks = Math.Min(
            totalDurationTicks,
            Math.Max(0L, totalDurationTicks - Math.Max(0L, savedRemainingCraftTicks)));
        return Math.Min(
            DeterministicSimulationUnits.FromFloat(completeEnergy),
            DeterministicSimulationUnits.RateForTicks(energyRate, elapsedTicks));
    }

    protected virtual Vector3 ResolveConsumeTargetWorldPosition()
    {
        if (portableObj != null)
        {
            return portableObj.WorldPosition;
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
        return RequiresOperationalEnergy(installedDefinition)
               && ResolvePrimaryBufferedEnergyType(installedDefinition) != ItemDefinition.EnergyType.None;
    }

    private bool ShouldShowGaugeByAreaMarkerVisibility()
    {
        if (!areaMarkerControllerResolved)
        {
            cachedAreaMarkerController = GetComponent<InputOutputModuleAreaMarkerController>();
            areaMarkerControllerResolved = true;
        }

        return cachedAreaMarkerController == null || cachedAreaMarkerController.ShouldShowLinkedUi();
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

        if (ItemDefinition.IsFluidFuelEnergyType(
                ResolvePrimaryBufferedEnergyType(installedDefinition)))
        {
            float capacityLiters = FluidStorageCapacityLiters;
            return capacityLiters > 0.0001f
                ? Mathf.Clamp01(StoredFluidLiters / capacityLiters)
                : 0f;
        }

        if (storedEnergyUnits > energyGaugeCapacityUnits)
        {
            energyGaugeCapacityUnits = storedEnergyUnits;
        }

        long gaugeCapacityUnits = Math.Max(
            energyGaugeCapacityUnits,
            DeterministicSimulationUnits.UnitsPerWhole);
        return Mathf.Clamp01((float)((double)storedEnergyUnits / gaugeCapacityUnits));
    }

    private void UpdateCraftParticleEffectVisual()
    {
        if (!playParticleEffectWhileCrafting || particleEffect == null)
        {
            return;
        }

        if (!ShouldPlayActiveCraftVisuals())
        {
            StopCraftParticleEffectVisual(true);
            return;
        }

        SetVisualParticleActive(particleEffect, true, OperationalAnimationSpeedRatio);
    }

    private bool ShouldPlayActiveCraftVisuals()
    {
        return IsActiveCraftRunning
               && !IsWaitingForOutput
               && OperationalAnimationSpeedRatio > 0.0001f
               && HasOperationalEnergyAvailable(ResolveInstalledDefinition());
    }

    protected virtual bool ShouldPlayWorkAnimation()
    {
        return ShouldPlayActiveCraftVisuals();
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
        if (!ShouldUpdateVisuals)
            return;
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

        SetVisualParticleActive(particleEffect, false, clear: clearParticles);
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
            long completeEnergyUnits = DeterministicSimulationUnits.FromFloat(
                ResolveCompleteEnergy(installedDefinition));
            return completeEnergyUnits > 0L
                ? Mathf.Clamp01((float)((double)Math.Max(0L, activeCraftConsumedEnergyUnits) / completeEnergyUnits))
                : 0f;
        }

        long durationTicks = DeterministicSimulationUnits.SecondsToTicks(Mathf.Max(0.1f, craftDuration));
        return durationTicks > 0L
            ? Mathf.Clamp01(1f - (float)((double)Math.Max(0L, remainingCraftTicks) / durationTicks))
            : 0f;
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
                : Mathf.Clamp(
                    DeterministicSimulationUnits.ToFloat(activeCraftConsumedEnergyUnits),
                    0f,
                    completeEnergy);
        }

        float duration = Mathf.Max(0.1f, craftDuration);
        return waitingForOutput
            ? duration
            : Mathf.Clamp(
                duration - DeterministicSimulationUnits.TicksToSeconds(remainingCraftTicks),
                0f,
                duration);
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

        production.Begin(recipeIndex, outputItemId, outputCount,
            DeterministicSimulationUnits.SecondsToTicks(ResolveInitialCraftDuration(installedDefinition)));
        lastOperationalEnergySupplyRatio = 1f;
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
        production.Clear();
        lastOperationalEnergySupplyRatio = 1f;
        if (storedEnergyUnits <= 0L)
        {
            energyGaugeCapacityUnits = 0L;
        }

        for (int i = 1; i < secondaryStoredEnergyUnitsByType.Length; i++)
        {
            if (secondaryStoredEnergyUnitsByType[i] <= 0L)
            {
                secondaryEnergyGaugeCapacityUnitsByType[i] = 0L;
            }
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
        outputDrainCheckPending = true;
        hasStoredOutputOnConveyor = false;
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

    private void RegisterRuntimeFluidSpatialCoordinates()
    {
        UnregisterRuntimeFluidSpatialCoordinates();

        AddUniqueCoordinates(runtimeOutputCoordinates, runtimeFluidOutputIndexCoordinates);
        if (CanStoreFluid)
        {
            CollectRuntimePipeAreaCoordinates(runtimeFluidStorageIndexCoordinates);
        }
        AppendDedicatedFluidStorageRuntimeCoordinates(runtimeFluidStorageIndexCoordinates);

        for (int i = 0; i < runtimeFluidOutputIndexCoordinates.Count; i++)
        {
            RegisterRuntimeSpatialCoordinate(
                registeredRuntimeFluidOutputCoordinates,
                runtimeFluidOutputIndexCoordinates[i],
                this);
        }

        for (int i = 0; i < runtimeFluidStorageIndexCoordinates.Count; i++)
        {
            RegisterRuntimeSpatialCoordinate(
                registeredRuntimeFluidStorageCoordinates,
                runtimeFluidStorageIndexCoordinates[i],
                this);
        }
    }

    private void UnregisterRuntimeFluidSpatialCoordinates()
    {
        for (int i = 0; i < runtimeFluidOutputIndexCoordinates.Count; i++)
        {
            UnregisterRuntimeSpatialCoordinate(
                registeredRuntimeFluidOutputCoordinates,
                runtimeFluidOutputIndexCoordinates[i],
                this);
        }

        for (int i = 0; i < runtimeFluidStorageIndexCoordinates.Count; i++)
        {
            UnregisterRuntimeSpatialCoordinate(
                registeredRuntimeFluidStorageCoordinates,
                runtimeFluidStorageIndexCoordinates[i],
                this);
        }

        runtimeFluidOutputIndexCoordinates.Clear();
        runtimeFluidStorageIndexCoordinates.Clear();
        runtimeFluidOutputItemIdScratch.Clear();
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

    private static void RegisterRuntimeSpatialCoordinate(
        Dictionary<Vector2Int, HashSet<InputOutputModule>> registry,
        Vector2Int coordinate,
        InputOutputModule module)
    {
        if (!registry.TryGetValue(coordinate, out HashSet<InputOutputModule> modules))
        {
            modules = new HashSet<InputOutputModule>();
            registry.Add(coordinate, modules);
        }

        modules.Add(module);
    }

    private static void UnregisterRuntimeSpatialCoordinate(
        Dictionary<Vector2Int, HashSet<InputOutputModule>> registry,
        Vector2Int coordinate,
        InputOutputModule module)
    {
        if (!registry.TryGetValue(coordinate, out HashSet<InputOutputModule> modules)
            || !modules.Remove(module))
        {
            return;
        }

        if (modules.Count == 0)
        {
            registry.Remove(coordinate);
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
    // Validate explicit editor selections, never asset-load callbacks. Parent assets
    // may still be deserializing in OnValidate, so their graph is not authoritative there.
    public bool IsValidParentInputOutputModuleItem(ItemDefinition parentItem)
    {
        if (parentItem == null)
        {
            return true;
        }

        InputOutputModule current = parentItem.mapObject as InputOutputModule;
        if (current == null)
        {
            return false;
        }

        HashSet<InputOutputModule> visitedModules = new HashSet<InputOutputModule> { this };
        while (current != null)
        {
            if (!visitedModules.Add(current))
            {
                return false;
            }

            current = current.ResolveParentInputOutputModule();
        }

        return true;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        // Preserve serialized references even when another asset is not loaded yet.
        // Effective recipes are resolved lazily with a visited set to bound cycles.
        effectivePairDataInitialized = false;
        rectGridDataInitialized = false;
        rectGridPlacementDataInitialized = false;
        EnsurePairData();
        EnsureRectGridData();
        EnsureRectGridPlacementData();
    }
#endif
}
