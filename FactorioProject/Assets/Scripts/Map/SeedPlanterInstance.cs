using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Simulation;
namespace ProjectF.MapObjects
{
public sealed class SeedPlanterInstance : ForestryInstance, ISeedPlanterTarget, IDataItemProducer
{
    private InputOutputModule.PersistentState Io => Placement.inputOutputState;
    private ref PlantingProcess Process => ref Data.Planting;
    private ref long plantElapsedUnits => ref Process.ElapsedUnits;
    private ref bool hasLoadedSeed => ref Process.HasLoadedSeed;
    private ref int loadedSeedItemId => ref Process.LoadedSeedItemId;
    private Vector2Int loadedSeedInputCoordinate
    {
        get => new Vector2Int(Process.LoadedSeedInputCoordinate.X, Process.LoadedSeedInputCoordinate.Y);
        set => Process.LoadedSeedInputCoordinate = new GridCell(value.x, value.y);
    }
    private ref long seedTransferRemainingUnits => ref Process.TransferRemainingUnits;
    private ref PlantingOperatingState operatingState => ref Process.OperatingState;
    private ref int currentSeedItemId => ref Process.CurrentSeedItemId;
    private ref int currentSeedCount => ref Process.CurrentSeedCount;
    private Vector2Int currentInputCoordinate
    {
        get => new Vector2Int(Process.CurrentInputCoordinate.X, Process.CurrentInputCoordinate.Y);
        set => Process.CurrentInputCoordinate = new GridCell(value.x, value.y);
    }
    private ref bool hasCurrentInputCoordinate => ref Process.HasCurrentInputCoordinate;
    private ref bool requestingPower => ref Process.RequestingPower;
    private ref bool isOperating => ref Process.IsOperating;
    private readonly List<Vector2Int> recoveredSeedInputCoordinates = new List<Vector2Int>(2);
    private Vector3 ConsumePosition => RootMatrix.MultiplyPoint3x4(Template.ConsumePoint);
    private float InputConsumeMoveInterval => Template.InputConsumeMoveInterval;
    private float OperationalAnimationSpeedRatio => Data.SupplyRatio;
    private SeedPlanter Source => (SeedPlanter)Prototype;
    public int RuntimeAreaMaxObjects => Source.RuntimeAreaMaxObjects;
    public PlantingOperatingState CurrentOperatingState => operatingState;
    public bool IsErrorState => operatingState == PlantingOperatingState.InvalidGround;
    public bool IsOperating => isOperating;
    public int CurrentSeedItemId => currentSeedItemId;
    public int CurrentSeedCount => Mathf.Max(0, currentSeedCount);
    public float PlantDurationSeconds => SeedPlanter.ResolvePlantDuration(BoundItemDefinition);
    public float PlantElapsedSeconds => DeterministicSimulationUnits.ToFloat(Math.Min(
        DeterministicSimulationUnits.FromFloat(PlantDurationSeconds), Math.Max(0, plantElapsedUnits)));
    public float PlantProgress01 => Mathf.Clamp01(PlantElapsedSeconds / PlantDurationSeconds);
    private bool HasRuntimeOutputCoordinates => Io.outputCoordinates.Count > 0;
    private List<Vector2Int> RuntimeOutputCoordinates => Io.outputCoordinates;
    TerrainGenerator IDataItemProducer.Terrain => World.Terrain;
    BlockStateStore IDataItemProducer.Store => World.Store;
    InputOutputModule IDataItemProducer.OutputPrototype => Source;
    public IReadOnlyList<Vector2Int> OutputCoordinates => Io.outputCoordinates;
    internal override bool IsWorking => isOperating;
    internal override bool HasPowerDemand => requestingPower || operatingState == PlantingOperatingState.NoSeeds
        || operatingState == PlantingOperatingState.LoadingSeed || operatingState == PlantingOperatingState.TargetOccupied;
    internal override bool KeepScheduled => operatingState == PlantingOperatingState.LoadingSeed
        || operatingState == PlantingOperatingState.Planting
        || operatingState == PlantingOperatingState.Ready && (hasLoadedSeed || currentSeedCount > 0);
    internal SeedPlanterInstance(ForestryWorld world, int index, uint generation, MapObjectHandle handle,
        SeedPlanter source, BlockStateStore.InstallationSaveState placement, ForestryRenderTemplate template)
        : base(world, index, generation, handle, source, placement, template)
    {
        placement.inputOutputState ??= new InputOutputModule.PersistentState();
        plantElapsedUnits = Math.Min(DeterministicSimulationUnits.FromFloat(PlantDurationSeconds),
            Io.hasDeterministicUnits ? Math.Max(0, Io.seedPlanterPlantElapsedUnits)
                : DeterministicSimulationUnits.FromFloat(Io.seedPlanterPlantElapsedSeconds));
        hasLoadedSeed = Io.seedPlanterHasLoadedSeed && Io.seedPlanterLoadedSeedItemId >= 0;
        loadedSeedItemId = hasLoadedSeed ? Io.seedPlanterLoadedSeedItemId : -1;
        loadedSeedInputCoordinate = Io.seedPlanterLoadedSeedInputCoordinate;
        if (hasLoadedSeed && !ContainsInput(loadedSeedInputCoordinate, loadedSeedItemId))
            for (int i = 0; i < Io.inputItemAreas.Count; i++)
                if (Io.inputItemAreas[i].itemId < 0 || Io.inputItemAreas[i].itemId == loadedSeedItemId)
                { loadedSeedInputCoordinate = Io.inputItemAreas[i].coordinate; break; }
        seedTransferRemainingUnits = hasLoadedSeed ? Math.Max(0, Math.Min(Io.seedPlanterTransferRemainingUnits,
            DeterministicSimulationUnits.FromFloat(PortableObject.MoveToDuration))) : 0;
        if (hasLoadedSeed) ApplyLoadedSeedAsCurrentInput(); else RefreshSeedInput();
        FacilityFuel.RestoreLegacy(Io, BoundItemDefinition);
    }
    private void SetOperatingState(PlantingOperatingState state) => operatingState = state;
    private bool HasOperationalEnergyAvailable(ItemDefinition definition)
    {
        PublishPowerDemand();
        float ratio = 1;
        if (Template.Watts > 0) { UtilityPole.TryGetElectricSupplyRatio(this, Template.Watts, out ratio); }
        Data.SupplyRatio = ratio;
        if (ratio <= 0) return false;
        for (int i = 0; i < definition.UseEnergyRequirementCount; i++)
            if (definition.TryGetUseEnergyRequirement(i, out var e) && e.energyType != ItemDefinition.EnergyType.None
                && e.energyType != ItemDefinition.EnergyType.Electricity && e.useEnergyAmount > 0
                && FacilityFuel.Stored(Io, e.energyType) <= 0 && !FacilityFuel.Refill(this, e.energyType, ConsumePosition, InputConsumeMoveInterval)) return false;
        return true;
    }
    private bool TryConsumeOperatingEnergy(float deltaTime, out float consumed)
    {
        consumed = 0; float ratio = 1;
        if (Template.Watts > 0)
        {
            float requested = Template.Watts * deltaTime;
            if (!UtilityPole.TryConsumeElectricity(this, requested, deltaTime, out consumed)) { Data.SupplyRatio = 0; return false; }
            ratio = requested > 0 ? Mathf.Clamp01(consumed / requested) : 1;
        }
        int typeMask = 0;
        for (int i = 0; i < BoundItemDefinition.UseEnergyRequirementCount; i++)
        {
            if (!BoundItemDefinition.TryGetUseEnergyRequirement(i, out var e) || e.energyType == ItemDefinition.EnergyType.None
                || e.energyType == ItemDefinition.EnergyType.Electricity || e.useEnergyAmount <= 0) continue;
            int bit = 1 << (int)e.energyType;
            if ((typeMask & bit) != 0) continue;
            typeMask |= bit;
            long requested = DeterministicSimulationUnits.RateForTicks(
                ItemDefinition.ResolveUseEnergyRatePerSecond(BoundItemDefinition, e.energyType),
                DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime));
            if (requested > 0) ratio = Math.Min(ratio, (float)((double)FacilityFuel.Spend(this, e.energyType,
                requested, ConsumePosition, InputConsumeMoveInterval) / requested));
        }
        Data.SupplyRatio = ratio; return ratio > 0;
    }
    protected override void ApplyTick(float deltaTime)
    {
        requestingPower = false;
        isOperating = false;
        if (hasLoadedSeed)
        {
            ApplyLoadedSeedAsCurrentInput();
        }
        else
        {
            RefreshSeedInput();
        }

        if (deltaTime <= 0f || !IsRuntimeActive)
        {
            SetOperatingState(PlantingOperatingState.Ready);
            return;
        }

        TerrainGenerator terrain = World.Terrain;
        if (!TryResolveOutputTarget(out Vector2Int targetCoordinate)
            || terrain == null
            || !terrain.IsFarmlandAt(targetCoordinate))
        {
            if (!hasLoadedSeed)
            {
                plantElapsedUnits = 0L;
            }

            SetOperatingState(PlantingOperatingState.InvalidGround);
            return;
        }

        if (!hasLoadedSeed
            && (currentSeedItemId < 0 || currentSeedCount <= 0 || !hasCurrentInputCoordinate))
        {
            plantElapsedUnits = 0L;
            SetOperatingState(PlantingOperatingState.NoSeeds);
            return;
        }

        ItemDefinition seedDefinition = InputOutputModule.ResolveItemDefinition(currentSeedItemId);
        if (!ItemDefinition.IsPlantableSeedDefinition(seedDefinition))
        {
            plantElapsedUnits = 0L;
            SetOperatingState(PlantingOperatingState.NoSeeds);
            return;
        }

        if (!terrain.CanPlantSeedAt(targetCoordinate, seedDefinition))
        {
            plantElapsedUnits = 0L;
            SetOperatingState(PlantingOperatingState.TargetOccupied);
            return;
        }

        if (hasLoadedSeed && seedTransferRemainingUnits > 0L)
        {
            long transferDeltaUnits = DeterministicSimulationUnits.RateForTicks(
                1f,
                DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime));
            seedTransferRemainingUnits = System.Math.Max(
                0L,
                seedTransferRemainingUnits - transferDeltaUnits);
            if (seedTransferRemainingUnits > 0L)
            {
                SetOperatingState(PlantingOperatingState.LoadingSeed);
                return;
            }
        }

        long plantDurationUnits = DeterministicSimulationUnits.FromFloat(PlantDurationSeconds);
        // A restored completed operation needs no additional energy. It must still
        // own a loaded seed before committing the planting result.
        if (hasLoadedSeed && plantElapsedUnits >= plantDurationUnits)
        {
            CompletePlanting(terrain, targetCoordinate, seedDefinition);
            return;
        }

        ItemDefinition installedDefinition = BoundItemDefinition;
        requestingPower = true;
        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            SetOperatingState(PlantingOperatingState.NoPower);
            return;
        }

        if (!hasLoadedSeed)
        {
            Vector3 planterWorldPosition = ConsumePosition;
            int consumed = ConsumeRuntimeInputAreaCenterObjects(
                currentInputCoordinate,
                currentSeedItemId,
                1,
                planterWorldPosition,
                InputConsumeMoveInterval,
                animateVirtualizedConsumption: true,
                respectBoxMinimumRetainedCount: false);
            if (consumed != 1)
            {
                plantElapsedUnits = 0L;
                RefreshSeedInput();
                SetOperatingState(currentSeedCount > 0
                    ? PlantingOperatingState.Ready
                    : PlantingOperatingState.NoSeeds);
                return;
            }

            hasLoadedSeed = true;
            loadedSeedItemId = currentSeedItemId;
            loadedSeedInputCoordinate = currentInputCoordinate;
            seedTransferRemainingUnits = DeterministicSimulationUnits.FromFloat(
                PortableObject.MoveToDuration);
            ApplyLoadedSeedAsCurrentInput();
            requestingPower = false;
            SetOperatingState(PlantingOperatingState.LoadingSeed);
            return;
        }

        // Apply supply to a full simulation step, then clamp the accumulated work.
        // Scaling the remaining work instead approaches completion asymptotically
        // under partial power and can strand a seed at the final integer units.
        long requestedOperationUnits = DeterministicSimulationUnits.RateForTicks(
            1f,
            DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime));
        if (!TryConsumeOperatingEnergy(deltaTime, out _))
        {
            SetOperatingState(PlantingOperatingState.NoPower);
            return;
        }

        isOperating = true;
        SetOperatingState(PlantingOperatingState.Planting);
        long speedRatioUnits = DeterministicSimulationUnits.FromFloat(OperationalAnimationSpeedRatio);
        plantElapsedUnits = System.Math.Min(
            plantDurationUnits,
            plantElapsedUnits + DeterministicSimulationUnits.MultiplyRatio(
                requestedOperationUnits,
                speedRatioUnits,
                DeterministicSimulationUnits.UnitsPerWhole));

        if (plantElapsedUnits >= plantDurationUnits)
        {
            CompletePlanting(terrain, targetCoordinate, seedDefinition);
        }
    }

    private void CompletePlanting(
        TerrainGenerator terrain,
        Vector2Int targetCoordinate,
        ItemDefinition seedDefinition)
    {
        int seedItemId = loadedSeedItemId;
        Vector2Int inputCoordinate = loadedSeedInputCoordinate;
        Vector3 planterWorldPosition = ConsumePosition;
        plantElapsedUnits = 0L;
        isOperating = false;
        requestingPower = false;
        if (terrain.TryPlantSeedAt(targetCoordinate, seedDefinition))
        {
            PlaySeedDropAnimation(terrain, targetCoordinate, seedItemId, planterWorldPosition);
            ClearLoadedSeed();
            SetOperatingState(PlantingOperatingState.TargetOccupied);
        }
        else
        {
            if (TryRestoreRuntimeInputAreaCenterObject(
                    inputCoordinate,
                    seedItemId,
                    planterWorldPosition))
            {
                ClearLoadedSeed();
                RefreshSeedInput();
            }
            else
            {
                Debug.LogError($"{nameof(SeedPlanterInstance)} failed to restore loaded seed item {seedItemId} after planting failed.");
            }

            SetOperatingState(PlantingOperatingState.TargetOccupied);
        }
    }

    internal int ReceiveHarvestedSeeds(Vector2Int harvestedCoordinate, int seedItemId,
        int count, Vector3 startWorldPosition)
    {
        if (count <= 0 || !IsRuntimeActive
            || !TryResolveOutputTarget(out Vector2Int plantingCoordinate)
            || plantingCoordinate != harvestedCoordinate
            || !ItemDefinition.IsPlantableSeedDefinition(InputOutputModule.ResolveItemDefinition(seedItemId)))
            return 0;

        recoveredSeedInputCoordinates.Clear();
        AppendRuntimeInputItemAreaCoordinates(seedItemId, recoveredSeedInputCoordinates);
        int accepted = 0;
        for (int i = 0; i < recoveredSeedInputCoordinates.Count && accepted < count; i++)
        {
            Vector2Int coordinate = recoveredSeedInputCoordinates[i];
            if (!CanAddItemToRuntimeIoOverlapCoordinate(coordinate, seedItemId))
                continue;
            // Shares the existing loaded/saved input-stack path and its capacity/type checks.
            while (accepted < count
                && TryRestoreRuntimeInputAreaCenterObject(coordinate, seedItemId, startWorldPosition))
                accepted++;
        }
        if (accepted > 0)
        {
            if (hasLoadedSeed) ApplyLoadedSeedAsCurrentInput(); else RefreshSeedInput();
            Wake();
        }
        return accepted;
    }

    private bool TryResolveOutputTarget(out Vector2Int coordinate)
    {
        coordinate = default;
        if (!HasRuntimeOutputCoordinates || RuntimeOutputCoordinates.Count <= 0)
        {
            return false;
        }

        coordinate = RuntimeOutputCoordinates[0];
        return true;
    }

    private void ApplyLoadedSeedAsCurrentInput()
    {
        currentSeedItemId = loadedSeedItemId;
        currentSeedCount = hasLoadedSeed ? 1 : 0;
        currentInputCoordinate = loadedSeedInputCoordinate;
        hasCurrentInputCoordinate = hasLoadedSeed;
    }

    private void ClearLoadedSeed()
    {
        hasLoadedSeed = false;
        loadedSeedItemId = -1;
        loadedSeedInputCoordinate = default;
        seedTransferRemainingUnits = 0L;
        currentSeedItemId = -1;
        currentSeedCount = 0;
        hasCurrentInputCoordinate = false;
    }

    private static void PlaySeedDropAnimation(
        TerrainGenerator terrain,
        Vector2Int targetCoordinate,
        int seedItemId,
        Vector3 planterWorldPosition)
    {
        if (terrain == null
            || seedItemId < 0
            || !terrain.TryGetLoadedBlock(targetCoordinate, out Block targetBlock)
            || targetBlock == null)
        {
            return;
        }

        targetBlock.PlayTransientItemToFloorAnimation(
            seedItemId,
            planterWorldPosition);
    }

    private void RefreshSeedInput()
    {
        currentSeedItemId = -1;
        currentSeedCount = 0;
        hasCurrentInputCoordinate = false;
        if (GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return;
        }

        var seeds = Template.SeedItemIds;
        for (int i = 0; i < seeds.Length; i++)
        {
            ItemDefinition definition = InputOutputModule.ResolveItemDefinition(seeds[i]);
            if (!ItemDefinition.IsPlantableSeedDefinition(definition)
                || !TryResolveRuntimeInputItemBlock(
                    definition.id,
                    1,
                    null,
                    out _,
                    out Vector2Int coordinate,
                    respectBoxMinimumRetainedCount: false))
            {
                continue;
            }

            currentSeedItemId = definition.id;
            currentInputCoordinate = coordinate;
            hasCurrentInputCoordinate = true;
            currentSeedCount = GetRuntimeInputAreaCenterItemCount(
                coordinate,
                definition.id,
                respectBoxMinimumRetainedCount: false);
            return;
        }
    }
    private bool TryResolveRuntimeInputItemBlock(int item, int count, object ignored, out Block block,
        out Vector2Int coordinate, bool respectBoxMinimumRetainedCount)
    {
        block = null; coordinate = default;
        for (int i = 0; i < Io.inputItemAreas.Count; i++)
        {
            var area = Io.inputItemAreas[i];
            if (area.itemId >= 0 && area.itemId != item || FacilityFuel.CountAt(this, area.coordinate, item, respectBoxMinimumRetainedCount) < count) continue;
            coordinate = area.coordinate; World.Terrain.TryGetLoadedBlock(coordinate, out block); return true;
        }
        return false;
    }
    private int GetRuntimeInputAreaCenterItemCount(Vector2Int coordinate, int item, bool respectBoxMinimumRetainedCount)
        => FacilityFuel.CountAt(this, coordinate, item, respectBoxMinimumRetainedCount);
    private int ConsumeRuntimeInputAreaCenterObjects(Vector2Int coordinate, int item, int count, Vector3 position,
        float interval, bool animateVirtualizedConsumption, bool respectBoxMinimumRetainedCount)
        => FacilityFuel.ConsumeAt(this, coordinate, item, count, position, interval, respectBoxMinimumRetainedCount, animateVirtualizedConsumption);
    private bool TryRestoreRuntimeInputAreaCenterObject(Vector2Int coordinate, int item, Vector3 position)
    {
        if (World.Terrain.TryGetLoadedBlock(coordinate, out var block) && !World.Terrain.IsFloorObjectCoordinateVirtualized(coordinate))
            return block != null && block.TryAddInputAreaCenterObjectAnimated(item, position, 0, out _);
        int defaultCapacity = RuntimeAreaMaxObjects;
        if (World.Store.TryGetInstallationAnchorAtCoordinate(coordinate, out var anchor)
            && World.Store.TryGetInstallationState(anchor, out var installed))
        {
            var definition = InputOutputModule.ResolveItemDefinition(installed.itemId);
            if (definition != null) defaultCapacity = ItemDefinition.ResolveStackCapacity(definition, defaultCapacity);
        }
        int capacity = ItemDefinition.ResolveStackCapacity(InputOutputModule.ResolveItemDefinition(item), defaultCapacity);
        return World.Store.TryAddSavedCenterItems(coordinate, item, 1, capacity);
    }
    internal bool ContainsArea(Vector2Int coordinate, InputOutputModule.RectGridBlockType type)
        => Source.TryGetRectGridBlockTypeAtCoordinate(Source, AnchorCoordinate, Placement.quarterTurns, coordinate, out var actual) && actual == type;
    internal bool AppendInputItemIds(Vector2Int coordinate, ISet<int> ids)
    {
        bool found = false;
        var seeds = Template.SeedItemIds;
        for (int i = 0; i < seeds.Length; i++)
            if (ContainsInput(coordinate, seeds[i])) found |= ids.Add(seeds[i]);
        return found;
    }
    private bool ContainsInput(Vector2Int coordinate, int item)
    {
        for (int i = 0; i < Io.inputItemAreas.Count; i++)
            if (Io.inputItemAreas[i].coordinate == coordinate && (Io.inputItemAreas[i].itemId < 0 || Io.inputItemAreas[i].itemId == item)) return true;
        return false;
    }
    private bool CanAddItemToRuntimeIoOverlapCoordinate(Vector2Int coordinate, int item)
        => ContainsInput(coordinate, item) && InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(coordinate, item);
    private void AppendRuntimeInputItemAreaCoordinates(int item, List<Vector2Int> coordinates)
    {
        for (int i = 0; i < Io.inputItemAreas.Count; i++)
            if ((Io.inputItemAreas[i].itemId < 0 || Io.inputItemAreas[i].itemId == item) && !coordinates.Contains(Io.inputItemAreas[i].coordinate))
                coordinates.Add(Io.inputItemAreas[i].coordinate);
    }
    public void GetObjectInfoStatus(out string text, out bool working, out bool warning)
    {
        working = isOperating; warning = false;
        if (Template.Watts > 0 && HasPowerDemand && !UtilityPole.HasElectricityAvailable(this)) text = "No energy";
        else
        {
            switch (operatingState)
            {
                case PlantingOperatingState.LoadingSeed: text = "Loading seed"; break;
                case PlantingOperatingState.Planting: text = "Planting"; break;
                case PlantingOperatingState.NoSeeds: text = "Waiting for seeds"; warning = true; break;
                case PlantingOperatingState.NoPower: text = "No energy"; break;
                case PlantingOperatingState.InvalidGround: text = "Invalid ground"; break;
                case PlantingOperatingState.TargetOccupied: text = "Waiting for output"; warning = true; break;
                default: text = "Ready"; break;
            }
        }
    }
    internal override void ClearItems() { plantElapsedUnits = 0; ClearLoadedSeed(); Wake(); }
    public override void Persist()
    {
        if (!IsRuntimeActive) return;
        Io.hasDeterministicUnits = true;
        Io.seedPlanterPlantElapsedSeconds = PlantElapsedSeconds;
        Io.seedPlanterPlantElapsedUnits = plantElapsedUnits;
        Io.seedPlanterHasLoadedSeed = hasLoadedSeed;
        Io.seedPlanterLoadedSeedItemId = loadedSeedItemId;
        Io.seedPlanterLoadedSeedInputCoordinate = loadedSeedInputCoordinate;
        Io.seedPlanterTransferRemainingUnits = seedTransferRemainingUnits;
        FacilityFuel.Persist(Io);
    }
}
}
