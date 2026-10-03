using System.Collections.Generic;
using UnityEngine;

public partial class UtilityPole
{
    private sealed class RobotArmElectricBinding
    {
        internal readonly List<ElectricNetwork> Networks = new List<ElectricNetwork>(1);
        internal ElectricNetwork BestNetwork;
        internal ulong EvaluatedRuntimeVersion;
        internal float DemandWatts;

        internal void Reset()
        {
            Networks.Clear();
            BestNetwork = null;
            EvaluatedRuntimeVersion = 0UL;
            DemandWatts = 0f;
        }
    }

    private static bool robotArmConsumersDirty = true;
    private static RobotArmWorld robotArmConsumerWorld;
    private static MiningWorld miningConsumerWorld;
    private static readonly Dictionary<IDataElectricConsumer, RobotArmElectricBinding> robotArmBindings =
        new Dictionary<IDataElectricConsumer, RobotArmElectricBinding>();
    private static readonly Stack<RobotArmElectricBinding> robotArmBindingPool =
        new Stack<RobotArmElectricBinding>();
    private static readonly Dictionary<ElectricNetwork, List<IDataElectricConsumer>> robotArmsByNetwork =
        new Dictionary<ElectricNetwork, List<IDataElectricConsumer>>();
    private static readonly Stack<List<IDataElectricConsumer>> robotArmNetworkListPool =
        new Stack<List<IDataElectricConsumer>>();
    private static readonly HashSet<IDataElectricConsumer> robotArmWakeScratch =
        new HashSet<IDataElectricConsumer>();
    private static readonly List<IDataElectricConsumer> robotArmOrderScratch = new List<IDataElectricConsumer>();
    private static readonly Dictionary<ElectricNetwork, float> robotArmDemand =
        new Dictionary<ElectricNetwork, float>();
    private static readonly HashSet<ElectricNetwork> robotArmNetworkScratch =
        new HashSet<ElectricNetwork>();
    private static long robotArmPowerBindingCacheHits;
    private static long robotArmPowerBindingCacheMisses;
    private static int robotArmSingleNetworkBindingCount;
    private static ulong robotArmNetworkRuntimeVersion = 1UL;

    internal static void InvalidateRobotArmConsumers()
    {
        robotArmConsumersDirty = true;
        InvalidateNetworkRuntimeForNextTick();
        connectionLineVisualsDirty = true;
        previewConsumerLineVisualsDirty = true;
        RequestDeferredConnectionLineVisualRefresh();
        RequestDeferredPreviewConsumerLineVisualRefresh();
    }

    internal static void UnregisterRobotArmConsumer(IDataElectricConsumer arm)
    {
        if (arm != null
            && robotArmBindings.Remove(arm, out RobotArmElectricBinding binding)
            && binding != null)
        {
            binding.Reset();
            robotArmBindingPool.Push(binding);
        }

        InvalidateRobotArmConsumers();
    }

    private static void RefreshRobotArmConsumers()
    {
        RobotArmWorld world = RobotArmWorld.Current;
        if (!robotArmConsumersDirty && robotArmConsumerWorld == world && miningConsumerWorld == MiningWorld.Current)
        {
            return;
        }

        bool worldChanged = robotArmConsumerWorld != world || miningConsumerWorld != MiningWorld.Current;
        robotArmConsumersDirty = false;
        robotArmConsumerWorld = world;
        miningConsumerWorld = MiningWorld.Current;
        if (worldChanged)
        {
            robotArmPowerBindingCacheHits = 0L;
            robotArmPowerBindingCacheMisses = 0L;
        }
        ReleaseRobotArmBindings();
        robotArmDemand.Clear();
        robotArmSingleNetworkBindingCount = 0;
        robotArmOrderScratch.Clear();
        IReadOnlyList<IDataElectricConsumer> instances = world != null ? world.Instances : System.Array.Empty<IDataElectricConsumer>();
        for (int i = 0; i < instances.Count; i++)
        {
            IDataElectricConsumer arm = instances[i];
            if (arm != null && arm.IsRuntimeActive)
            {
                robotArmOrderScratch.Add(arm);
            }
        }

        if (miningConsumerWorld != null)
            for (int i = 0; i < miningConsumerWorld.Instances.Count; i++)
                robotArmOrderScratch.Add(miningConsumerWorld.Instances[i]);
        robotArmOrderScratch.Sort(CompareRobotArmSimulationOrder);
        for (int armIndex = 0; armIndex < robotArmOrderScratch.Count; armIndex++)
        {
            IDataElectricConsumer arm = robotArmOrderScratch[armIndex];
            robotArmNetworkScratch.Clear();
            IReadOnlyList<Vector2Int> occupiedCoordinates = arm.RuntimeOccupiedCoordinates;
            for (int coordinateIndex = 0; coordinateIndex < occupiedCoordinates.Count; coordinateIndex++)
            {
                Vector2Int coordinate = occupiedCoordinates[coordinateIndex];
                if (!supplyPolesByCoordinate.TryGetValue(coordinate, out List<UtilityPole> poles))
                {
                    continue;
                }

                for (int poleIndex = 0; poleIndex < poles.Count; poleIndex++)
                {
                    UtilityPole pole = poles[poleIndex];
                    if (pole != null && electricNetworkByPole.TryGetValue(pole, out ElectricNetwork network))
                    {
                        robotArmNetworkScratch.Add(network);
                    }
                }
            }

            RobotArmElectricBinding binding = robotArmBindingPool.Count > 0
                ? robotArmBindingPool.Pop()
                : new RobotArmElectricBinding();
            binding.Reset();
            bool hasDemand = arm.TryGetElectricPowerDemand(out float watts);
            binding.DemandWatts = hasDemand ? watts : 0f;

            // networks is topology ordered. Demand accumulation therefore remains deterministic.
            for (int networkIndex = 0; networkIndex < networks.Count; networkIndex++)
            {
                ElectricNetwork network = networks[networkIndex];
                if (!robotArmNetworkScratch.Contains(network))
                {
                    continue;
                }

                binding.Networks.Add(network);
                AddRobotArmNetworkBinding(network, arm);
                if (hasDemand)
                {
                    robotArmDemand.TryGetValue(network, out float total);
                    robotArmDemand[network] = total + watts;
                }
            }

            if (binding.Networks.Count == 1)
            {
                robotArmSingleNetworkBindingCount++;
            }

            robotArmBindings.Add(arm, binding);
        }

        robotArmOrderScratch.Clear();
        robotArmNetworkScratch.Clear();
    }

    private static void ReleaseRobotArmBindings()
    {
        ClearRobotArmsByNetwork();
        foreach (KeyValuePair<IDataElectricConsumer, RobotArmElectricBinding> entry in robotArmBindings)
        {
            RobotArmElectricBinding binding = entry.Value;
            if (binding == null)
            {
                continue;
            }

            binding.Reset();
            robotArmBindingPool.Push(binding);
        }

        robotArmBindings.Clear();
    }

    private static void AddRobotArmNetworkBinding(ElectricNetwork network, IDataElectricConsumer arm)
    {
        if (network == null || arm == null)
        {
            return;
        }

        if (!robotArmsByNetwork.TryGetValue(network, out List<IDataElectricConsumer> arms))
        {
            arms = robotArmNetworkListPool.Count > 0
                ? robotArmNetworkListPool.Pop()
                : new List<IDataElectricConsumer>(4);
            robotArmsByNetwork.Add(network, arms);
        }

        arms.Add(arm);
    }

    private static void ClearRobotArmsByNetwork()
    {
        foreach (KeyValuePair<ElectricNetwork, List<IDataElectricConsumer>> entry in robotArmsByNetwork)
        {
            List<IDataElectricConsumer> arms = entry.Value;
            if (arms == null)
            {
                continue;
            }

            arms.Clear();
            robotArmNetworkListPool.Push(arms);
        }

        robotArmsByNetwork.Clear();
    }

    private static int WakeElectricRuntimeArmsForNetworks(
        HashSet<ElectricNetwork> wakeNetworks,
        out int candidateCount)
    {
        candidateCount = 0;
        if (wakeNetworks == null || wakeNetworks.Count == 0)
        {
            return 0;
        }

        robotArmWakeScratch.Clear();
        robotArmOrderScratch.Clear();
        for (int networkIndex = 0; networkIndex < networks.Count; networkIndex++)
        {
            ElectricNetwork network = networks[networkIndex];
            if (network == null
                || !wakeNetworks.Contains(network)
                || !robotArmsByNetwork.TryGetValue(network, out List<IDataElectricConsumer> arms))
            {
                continue;
            }

            for (int i = 0; i < arms.Count; i++)
            {
                IDataElectricConsumer arm = arms[i];
                if (arm != null && robotArmWakeScratch.Add(arm))
                {
                    robotArmOrderScratch.Add(arm);
                }
            }
        }

        candidateCount = robotArmOrderScratch.Count;
        int wokenCount = 0;
        for (int i = 0; i < robotArmOrderScratch.Count; i++)
        {
            IDataElectricConsumer consumer = robotArmOrderScratch[i];
            if (WakeDataElectricConsumer(consumer)) wokenCount++;
        }

        robotArmWakeScratch.Clear();
        robotArmOrderScratch.Clear();
        return wokenCount;
    }

    private static int CompareRobotArmSimulationOrder(IDataElectricConsumer left, IDataElectricConsumer right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        return left.SimulationId.CompareTo(right.SimulationId);
    }

    private static void RenderRobotArmPowerLines(bool previewPolesOnly)
    {
        IReadOnlyList<IDataElectricConsumer> instances = RobotArmWorld.Current != null
            ? RobotArmWorld.Current.Instances : System.Array.Empty<IDataElectricConsumer>();
        RenderDataConsumerPowerLines(instances, previewPolesOnly);
        if (MiningWorld.Current != null) RenderDataConsumerPowerLines(MiningWorld.Current.Instances, previewPolesOnly);
    }
    private static void RenderDataConsumerPowerLines(IReadOnlyList<IDataElectricConsumer> instances, bool previewPolesOnly)
    {
        for (int i = 0; i < instances.Count; i++)
        {
            IDataElectricConsumer arm = instances[i];
            if (arm == null
                || !arm.IsRuntimeActive
                || !arm.TryGetElectricPowerRequirement(out _)
                || !TryResolveRobotArmPowerLinePole(
                    arm,
                    previewPolesOnly,
                    out UtilityPole supplyingPole))
            {
                continue;
            }

            if (previewPolesOnly)
            {
                supplyingPole.RenderPreviewConsumerPowerLine(arm.PowerLineWorldPosition);
            }
            else
            {
                supplyingPole.RenderConsumerPowerLine(arm.PowerLineWorldPosition);
            }
        }
    }

    private static bool TryResolveRobotArmPowerLinePole(
        IDataElectricConsumer arm,
        bool previewPolesOnly,
        out UtilityPole supplyingPole)
    {
        supplyingPole = null;
        if (arm == null || !arm.IsRuntimeActive)
        {
            return false;
        }

        Vector3 consumerPosition = arm.PowerLineWorldPosition;
        float bestDistanceSqr = float.MaxValue;
        consumerPoleScratch.Clear();
        IReadOnlyList<Vector2Int> occupiedCoordinates = arm.RuntimeOccupiedCoordinates;
        for (int coordinateIndex = 0; coordinateIndex < occupiedCoordinates.Count; coordinateIndex++)
        {
            Vector2Int coordinate = occupiedCoordinates[coordinateIndex];
            if (!supplyPolesByCoordinate.TryGetValue(coordinate, out List<UtilityPole> poles))
            {
                continue;
            }

            for (int poleIndex = 0; poleIndex < poles.Count; poleIndex++)
            {
                UtilityPole pole = poles[poleIndex];
                if (pole != null && consumerPoleScratch.Add(pole))
                {
                    TrySelectConsumerPowerLinePole(
                        pole,
                        consumerPosition,
                        ref supplyingPole,
                        ref bestDistanceSqr);
                }
            }
        }

        if (previewPolesOnly)
        {
            foreach (KeyValuePair<UtilityPole, PreviewPoleRuntime> entry in previewPoleRuntimes)
            {
                UtilityPole previewPole = entry.Key;
                if (!IsValidPreviewPole(previewPole)
                    || !consumerPoleScratch.Add(previewPole)
                    || !PoleSuppliesRobotArm(previewPole, arm))
                {
                    continue;
                }

                TrySelectConsumerPowerLinePole(
                    previewPole,
                    consumerPosition,
                    ref supplyingPole,
                    ref bestDistanceSqr);
            }
        }

        consumerPoleScratch.Clear();
        return supplyingPole != null
               && (!previewPolesOnly || IsPreviewPole(supplyingPole));
    }

    private static bool PoleSuppliesRobotArm(UtilityPole pole, IDataElectricConsumer arm)
    {
        if (pole == null
            || arm == null
            || !arm.IsRuntimeActive
            || !TryGetPoleAnchorCoordinate(pole, out Vector2Int poleAnchor))
        {
            return false;
        }

        int radius = pole.SupplyRadiusCells;
        IReadOnlyList<Vector2Int> occupiedCoordinates = arm.RuntimeOccupiedCoordinates;
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            if (ChebyshevDistance(poleAnchor, occupiedCoordinates[i]) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private static ElectricNetwork ResolveRobotArmNetwork(IDataElectricConsumer arm)
    {
        EnsureNetworksEvaluated();
        if (arm == null
            || !arm.IsRuntimeActive
            || !robotArmBindings.TryGetValue(arm, out RobotArmElectricBinding binding)
            || binding == null
            || binding.Networks.Count == 0)
        {
            return null;
        }

        if (binding.Networks.Count == 1)
        {
            robotArmPowerBindingCacheHits++;
            ElectricNetwork onlyNetwork = binding.Networks[0];
            return onlyNetwork.HasPowerSource ? onlyNetwork : null;
        }

        if (binding.EvaluatedRuntimeVersion == robotArmNetworkRuntimeVersion)
        {
            robotArmPowerBindingCacheHits++;
            return binding.BestNetwork;
        }

        robotArmPowerBindingCacheMisses++;
        ElectricNetwork bestNetwork = null;
        float bestScore = 0f;
        for (int i = 0; i < binding.Networks.Count; i++)
        {
            ElectricNetwork network = binding.Networks[i];
            float score = ResolveNetworkScore(network);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestNetwork = network;
        }

        binding.BestNetwork = bestNetwork;
        binding.EvaluatedRuntimeVersion = robotArmNetworkRuntimeVersion;
        return bestNetwork;
    }

    private static void AdvanceRobotArmNetworkRuntimeVersion()
    {
        unchecked
        {
            robotArmNetworkRuntimeVersion++;
            if (robotArmNetworkRuntimeVersion == 0UL)
            {
                robotArmNetworkRuntimeVersion = 1UL;
                foreach (KeyValuePair<IDataElectricConsumer, RobotArmElectricBinding> entry in robotArmBindings)
                {
                    if (entry.Value != null)
                    {
                        entry.Value.EvaluatedRuntimeVersion = 0UL;
                    }
                }
            }
        }
    }

    internal static void InvalidateDataConsumerDemand(IDataElectricConsumer consumer)
    {
        if (!robotArmBindings.TryGetValue(consumer, out var binding)) return;
        float demand = consumer.TryGetElectricPowerDemand(out float watts) ? watts : 0f;
        float delta = demand - binding.DemandWatts;
        if (Mathf.Abs(delta) <= EnergyEpsilon) return;
        binding.DemandWatts = demand;
        for (int i = 0; i < binding.Networks.Count; i++)
        {
            ElectricNetwork network = binding.Networks[i];
            robotArmDemand.TryGetValue(network, out float total);
            robotArmDemand[network] = Mathf.Max(0f, total + delta);
            MarkNetworkRuntimeDirty(network);
            if (network != null && !electricRuntimeWakeAllPending) electricRuntimeWakeNetworks.Add(network);
        }
        InvalidateNetworkRuntimeEvaluationForNextTick();
        RequestElectricRuntimeNetworkWake();
    }
    internal static int WakeAllDataElectricConsumers(out int candidates)
    {
        int woken = 0; candidates = 0;
        WakeDataConsumers(RobotArmWorld.Current?.Instances, ref candidates, ref woken);
        WakeDataConsumers(MiningWorld.Current?.Instances, ref candidates, ref woken);
        return woken;
    }
    private static void WakeDataConsumers(IReadOnlyList<IDataElectricConsumer> consumers, ref int candidates, ref int woken)
    {
        if (consumers == null) return;
        candidates += consumers.Count;
        for (int i = 0; i < consumers.Count; i++)
            if (WakeDataElectricConsumer(consumers[i])) woken++;
    }
    private static bool WakeDataElectricConsumer(IDataElectricConsumer consumer)
    {
        if (consumer == null || !consumer.IsRuntimeActive) return false;
        // Arms already sample power while moving. Only their power-blocked sleepers need a wake.
        if (consumer is RobotArmInstance arm ? !arm.IsElectricPowerBlocked : !consumer.TryGetElectricPowerDemand(out _)) return false;
        consumer.WakeForElectricPowerChange(); return true;
    }
    public static bool TryGetElectricSupplyRatio(IDataElectricConsumer consumer, float watts, out float ratio)
    {
        ratio = 0f;
        if (consumer == null || !consumer.IsRuntimeActive) return false;
        if (IsFreeElectroEnergyEnabled()) { ratio = 1f; return true; }
        ElectricNetwork network = ResolveRobotArmNetwork(consumer);
        if (network == null) return false;
        ratio = network.Power.GetConsumerRatio(watts, true);
        return true;
    }

    internal static void PrepareRobotArmPowerTick()
    {
        // Evaluate the shared network once before the entity loop. Individual arms then
        // perform only their cached binding lookup and fixed-point supply calculation.
        PrepareSimulationPowerTick();
    }

    public static bool HasElectricityAvailable(IDataElectricConsumer arm)
    {
        if (IsFreeElectroEnergyEnabled())
        {
            return true;
        }

        ElectricNetwork network = ResolveRobotArmNetwork(arm);
        return network != null && network.ProductionWatts > EnergyEpsilon;
    }

    public static bool TryGetElectricPowerInfo(IDataElectricConsumer arm, out float supplied, out float required)
    {
        supplied = required = 0f;
        if (arm == null || !arm.IsRuntimeActive || !arm.TryGetElectricPowerRequirement(out required))
        {
            return false;
        }

        if (IsFreeElectroEnergyEnabled())
        {
            supplied = required;
            return true;
        }

        ElectricNetwork network = ResolveRobotArmNetwork(arm);
        if (network != null)
        {
            supplied = required * network.Power.GetConsumerRatio(required, true);
        }

        return true;
    }

    public static bool TryConsumeElectricity(
        IDataElectricConsumer arm,
        float requested,
        float deltaTime,
        out float consumed)
    {
        if (arm == null || !arm.TryGetElectricPowerRequirement(out float watts))
        {
            consumed = 0f;
            return false;
        }

        return TryConsumeRobotArmElectricity(arm, watts, requested, out consumed);
    }

    internal static bool TryConsumeRobotArmElectricity(
        IDataElectricConsumer arm,
        float requiredWatts,
        float requestedEnergy,
        out float consumedEnergy)
    {
        consumedEnergy = 0f;
        if (requestedEnergy <= 0f || requiredWatts <= EnergyEpsilon || arm == null || !arm.IsRuntimeActive)
        {
            return false;
        }

        if (IsFreeElectroEnergyEnabled())
        {
            consumedEnergy = requestedEnergy;
            return true;
        }

        ElectricNetwork network = ResolveRobotArmNetwork(arm);
        if (network == null)
        {
            return false;
        }

        consumedEnergy = DeterministicSimulationUnits.ToFloat(
            network.Power.GrantEnergy(DeterministicSimulationUnits.FromFloat(requestedEnergy), requiredWatts));
        return consumedEnergy > 0f;
    }

    internal static void AppendRobotArmPowerProfilerCounters()
    {
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmPower", "Bindings", robotArmBindings.Count);
        MapObjectTickProfiler.AddRuntimeCounter(
            "RobotArmPower",
            "SingleNetworkBindings",
            robotArmSingleNetworkBindingCount,
            "Common path resolves without scanning candidate networks.");
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmPower", "BindingCacheHits", robotArmPowerBindingCacheHits);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmPower", "BindingCacheMisses", robotArmPowerBindingCacheMisses);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "RuntimeEvaluations",
            networkRuntimeEvaluationCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "RuntimeCleanSkips",
            networkRuntimeCleanSkipCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "RuntimeNetworkRefreshes",
            networkRuntimeNetworkRefreshCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "LastDirtyNetworks",
            lastNetworkRuntimeDirtyNetworkCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "LastRemappedConsumers",
            lastNetworkRuntimeRemappedConsumerCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "DeferredRuntimeInvalidations",
            networkRuntimeDeferredInvalidationCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "RuntimeWakeBatches",
            electricRuntimeWakeBatchCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "TargetedWakeBatches",
            electricRuntimeTargetedWakeBatchCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "FullWakeBatches",
            electricRuntimeFullWakeBatchCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "CoalescedRuntimeWakes",
            electricRuntimeWakeCoalescedCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "LastWakeNetworks",
            lastElectricRuntimeWakeNetworkCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "LastWakeConsumers",
            lastElectricRuntimeWakeConsumerCandidateCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "LastWakeRobotArms",
            lastElectricRuntimeWakeRobotArmCandidateCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "LastActuallyWoken",
            lastElectricRuntimeActuallyWokenCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ElectricPower",
            "RuntimeWakePending",
            electricRuntimeWakePending ? 1 : 0);
    }
}
