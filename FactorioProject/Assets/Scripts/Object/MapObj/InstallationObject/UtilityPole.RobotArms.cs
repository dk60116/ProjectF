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
        networkRuntimeEvaluatedSimulationTick = -1L;
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

        robotArmOrderScratch.Sort((left, right) => left.SimulationId.CompareTo(right.SimulationId));
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
        EnsureNetworksEvaluated();
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
            supplied = required * Mathf.Clamp01(
                network.ProductionWatts / Mathf.Max(required, network.RequiredWatts));
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
            DeterministicSimulationUnits.MultiplyRatio(
                DeterministicSimulationUnits.FromFloat(requestedEnergy),
                DeterministicSimulationUnits.FromFloat(network.ProductionWatts),
                DeterministicSimulationUnits.FromFloat(Mathf.Max(requiredWatts, network.RequiredWatts))));
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
    }
}
