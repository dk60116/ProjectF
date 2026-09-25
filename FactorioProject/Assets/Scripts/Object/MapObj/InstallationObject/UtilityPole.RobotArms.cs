using System.Collections.Generic;
using UnityEngine;

public partial class UtilityPole
{
    private sealed class RobotArmElectricBinding
    {
        internal readonly List<ElectricNetwork> Networks = new List<ElectricNetwork>(1);
        internal ElectricNetwork BestNetwork;
        internal ulong EvaluatedRuntimeVersion;

        internal void Reset()
        {
            Networks.Clear();
            BestNetwork = null;
            EvaluatedRuntimeVersion = 0UL;
        }
    }

    private static bool robotArmConsumersDirty = true;
    private static RobotArmWorld robotArmConsumerWorld;
    private static readonly Dictionary<RobotArmInstance, RobotArmElectricBinding> robotArmBindings =
        new Dictionary<RobotArmInstance, RobotArmElectricBinding>();
    private static readonly Stack<RobotArmElectricBinding> robotArmBindingPool =
        new Stack<RobotArmElectricBinding>();
    private static readonly Dictionary<ElectricNetwork, List<RobotArmInstance>> robotArmsByNetwork =
        new Dictionary<ElectricNetwork, List<RobotArmInstance>>();
    private static readonly Stack<List<RobotArmInstance>> robotArmNetworkListPool =
        new Stack<List<RobotArmInstance>>();
    private static readonly HashSet<RobotArmInstance> robotArmWakeScratch =
        new HashSet<RobotArmInstance>();
    private static readonly List<RobotArmInstance> robotArmOrderScratch = new List<RobotArmInstance>();
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

    internal static void UnregisterRobotArmConsumer(RobotArmInstance arm)
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
        if (!robotArmConsumersDirty && robotArmConsumerWorld == world)
        {
            return;
        }

        bool worldChanged = robotArmConsumerWorld != world;
        robotArmConsumersDirty = false;
        robotArmConsumerWorld = world;
        if (worldChanged)
        {
            robotArmPowerBindingCacheHits = 0L;
            robotArmPowerBindingCacheMisses = 0L;
        }
        ReleaseRobotArmBindings();
        robotArmDemand.Clear();
        robotArmSingleNetworkBindingCount = 0;
        if (world == null)
        {
            return;
        }

        robotArmOrderScratch.Clear();
        IReadOnlyList<RobotArmInstance> instances = world.Instances;
        for (int i = 0; i < instances.Count; i++)
        {
            RobotArmInstance arm = instances[i];
            if (arm != null && arm.IsRuntimeActive)
            {
                robotArmOrderScratch.Add(arm);
            }
        }

        robotArmOrderScratch.Sort(CompareRobotArmSimulationOrder);
        for (int armIndex = 0; armIndex < robotArmOrderScratch.Count; armIndex++)
        {
            RobotArmInstance arm = robotArmOrderScratch[armIndex];
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
        foreach (KeyValuePair<RobotArmInstance, RobotArmElectricBinding> entry in robotArmBindings)
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

    private static void AddRobotArmNetworkBinding(ElectricNetwork network, RobotArmInstance arm)
    {
        if (network == null || arm == null)
        {
            return;
        }

        if (!robotArmsByNetwork.TryGetValue(network, out List<RobotArmInstance> arms))
        {
            arms = robotArmNetworkListPool.Count > 0
                ? robotArmNetworkListPool.Pop()
                : new List<RobotArmInstance>(4);
            robotArmsByNetwork.Add(network, arms);
        }

        arms.Add(arm);
    }

    private static void ClearRobotArmsByNetwork()
    {
        foreach (KeyValuePair<ElectricNetwork, List<RobotArmInstance>> entry in robotArmsByNetwork)
        {
            List<RobotArmInstance> arms = entry.Value;
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
        RobotArmWorld world = RobotArmWorld.Current;
        if (world == null || wakeNetworks == null || wakeNetworks.Count == 0)
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
                || !robotArmsByNetwork.TryGetValue(network, out List<RobotArmInstance> arms))
            {
                continue;
            }

            for (int i = 0; i < arms.Count; i++)
            {
                RobotArmInstance arm = arms[i];
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
            if (world.WakeElectricRuntimeArm(robotArmOrderScratch[i]))
            {
                wokenCount++;
            }
        }

        robotArmWakeScratch.Clear();
        robotArmOrderScratch.Clear();
        return wokenCount;
    }

    private static int CompareRobotArmSimulationOrder(RobotArmInstance left, RobotArmInstance right)
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
        RobotArmWorld world = RobotArmWorld.Current;
        if (world == null)
        {
            return;
        }

        IReadOnlyList<RobotArmInstance> instances = world.Instances;
        for (int i = 0; i < instances.Count; i++)
        {
            RobotArmInstance arm = instances[i];
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
        RobotArmInstance arm,
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

    private static bool PoleSuppliesRobotArm(UtilityPole pole, RobotArmInstance arm)
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

    private static ElectricNetwork ResolveRobotArmNetwork(RobotArmInstance arm)
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
                foreach (KeyValuePair<RobotArmInstance, RobotArmElectricBinding> entry in robotArmBindings)
                {
                    if (entry.Value != null)
                    {
                        entry.Value.EvaluatedRuntimeVersion = 0UL;
                    }
                }
            }
        }
    }

    internal static void PrepareRobotArmPowerTick()
    {
        // Evaluate the shared network once before the entity loop. Individual arms then
        // perform only their cached binding lookup and fixed-point supply calculation.
        PrepareSimulationPowerTick();
    }

    public static bool HasElectricityAvailable(RobotArmInstance arm)
    {
        if (IsFreeElectroEnergyEnabled())
        {
            return true;
        }

        ElectricNetwork network = ResolveRobotArmNetwork(arm);
        return network != null && network.ProductionWatts > EnergyEpsilon;
    }

    public static bool TryGetElectricPowerInfo(RobotArmInstance arm, out float supplied, out float required)
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
        RobotArmInstance arm,
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
        RobotArmInstance arm,
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
