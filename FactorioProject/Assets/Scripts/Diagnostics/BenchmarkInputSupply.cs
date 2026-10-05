using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Benchmark
{
    // Registered IO cells replenish on stack notifications, never by scanning every facility each tick.
    internal static class BenchmarkInputSupply
    {
        private sealed class Supply
        {
            internal TerrainGenerator Terrain;
            internal BlockStateStore Store;
            internal Vector2Int Coordinate;
            internal ItemDefinition Item;
            internal int Count;
        }
        private sealed class Fuel
        {
            internal Supply Supply;
            internal long Rate, Units, Remaining;
        }
        private sealed class OwnerState
        {
            internal readonly List<Supply> Supplies = new List<Supply>();
            internal readonly List<Fuel> Fuels = new List<Fuel>();
            internal long EnergyTick = MapObjectTickManager.CurrentSimulationTick;
        }
        private static readonly Dictionary<object, OwnerState> owners = new Dictionary<object, OwnerState>();
        private static readonly Dictionary<Vector2Int, List<Supply>> cells = new Dictionary<Vector2Int, List<Supply>>();
        private static readonly Dictionary<ItemDefinition.EnergyType, ItemDefinition> fuels = new Dictionary<ItemDefinition.EnergyType, ItemDefinition>();
        private static bool refilling;
        internal static int Version { get; private set; }

        internal static void Clear()
        { owners.Clear(); cells.Clear(); fuels.Clear(); refilling = false; Version++; }

        internal static void Remove(object owner)
        {
            if (!owners.TryGetValue(owner, out var state)) return;
            owners.Remove(owner);
            for (int i = 0; i < state.Supplies.Count; i++)
            {
                var supply = state.Supplies[i];
                if (!cells.TryGetValue(supply.Coordinate, out var list)) continue;
                list.Remove(supply); if (list.Count == 0) cells.Remove(supply.Coordinate);
            }
        }

        internal static void Add(object owner, TerrainGenerator terrain, BlockStateStore store, Vector2Int coordinate, int item, int required, int capacity = 16)
        {
            if (!BenchmarkRuntime.ForceWorking || terrain == null) return;
            var definition = InputOutputModule.ResolveItemDefinition(item);
            if (!BenchmarkRuntime.IsPortableItem(definition)) return;
            if (!owners.TryGetValue(owner, out var state)) owners.Add(owner, state = new OwnerState());
            for (int i = 0; i < state.Supplies.Count; i++)
                if (state.Supplies[i].Coordinate == coordinate && state.Supplies[i].Item.id == item) return;
            var supply = new Supply { Terrain = terrain, Store = store, Coordinate = coordinate,
                Item = definition, Count = Math.Max(1, Math.Max(required, capacity)) };
            state.Supplies.Add(supply);
            if (!cells.TryGetValue(coordinate, out var list)) cells.Add(coordinate, list = new List<Supply>(1));
            list.Add(supply); Refill(coordinate);
        }

        internal static void AddEnergy(object owner, TerrainGenerator terrain, BlockStateStore store,
            IReadOnlyList<Vector2Int> coordinates, ItemDefinition definition, int capacity = 16)
        {
            if (!BenchmarkRuntime.ForceWorking || coordinates == null || coordinates.Count == 0 || definition == null) return;
            int mask = 0;
            for (int i = 0; i < definition.UseEnergyRequirementCount; i++)
            {
                if (!definition.TryGetUseEnergyRequirement(i, out var requirement)) continue;
                var type = requirement.energyType; int bit = 1 << (int)type;
                if (type == ItemDefinition.EnergyType.None || type == ItemDefinition.EnergyType.Electricity || (mask & bit) != 0) continue;
                mask |= bit;
                var item = ResolveFuel(type);
                if (item == null) continue;
                Supply first = null;
                for (int j = 0; j < coordinates.Count; j++)
                {
                    // Preserve an existing compatible fuel rather than replacing the player's stack.
                    int current = terrain.TryGetLoadedBlock(coordinates[j], out var block) && !terrain.IsFloorObjectCoordinateVirtualized(coordinates[j])
                        ? block.GetInputAreaCenterItemId() : store != null ? store.GetSavedCenterTopItemId(coordinates[j]) : -1;
                    var existing = InputOutputModule.ResolveItemDefinition(current);
                    var chosen = existing != null && existing.energyType == type && existing.energyAmount > 0
                        && BenchmarkRuntime.IsPortableItem(existing) ? existing : item;
                    Add(owner, terrain, store, coordinates[j], chosen.id, 1, capacity);
                    if (first == null && owners.TryGetValue(owner, out var state))
                        for (int k = 0; k < state.Supplies.Count; k++)
                            if (state.Supplies[k].Coordinate == coordinates[j] && state.Supplies[k].Item.id == chosen.id) { first = state.Supplies[k]; break; }
                }
                if (first != null)
                    owners[owner].Fuels.Add(new Fuel { Supply = first,
                        Rate = DeterministicSimulationUnits.RateForTicks(ItemDefinition.ResolveUseEnergyRatePerSecond(definition, type), 1),
                        Units = DeterministicSimulationUnits.FromFloat(first.Item.energyAmount) });
            }
        }
        private static ItemDefinition ResolveFuel(ItemDefinition.EnergyType type)
        {
            if (fuels.TryGetValue(type, out var cached)) return cached;
            var items = GameManager.Instance?.ItemManger?.ItemDefinitions;
            if (items != null)
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null && items[i].energyType == type && items[i].energyAmount > 0 && BenchmarkRuntime.IsPortableItem(items[i]))
                    { fuels.Add(type, items[i]); return items[i]; }
            return null;
        }

        internal static void Refill(Vector2Int coordinate)
        {
            if (!BenchmarkRuntime.ForceWorking || refilling || !cells.TryGetValue(coordinate, out var list)) return;
            refilling = true;
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var supply = list[i];
                    var terrain = supply.Terrain;
                    if (terrain == null) continue;
                    if (terrain.TryGetLoadedBlock(coordinate, out var block) && !terrain.IsFloorObjectCoordinateVirtualized(coordinate))
                    {
                        int missing = supply.Count - block.GetInputAreaCenterItemCount(supply.Item.id);
                        for (int j = 0; j < missing; j++)
                        {
                            bool added = block.TryAddDeferredOutput(supply.Item.id, block.WorldPosition, 0, true, out bool handled, supply.Item);
                            if (!handled || block.IsRuntimeConveyor) added = block.TryAddInputAreaCenterObjectAnimated(supply.Item.id, block.WorldPosition, 0, out _);
                            if (!added) break;
                        }
                    }
                    else if (supply.Store != null)
                    {
                        int missing = supply.Count - supply.Store.GetSavedCenterItemCount(coordinate, supply.Item.id);
                        // Box and single-item stack limits can be smaller than the benchmark reserve.
                        int low = 0, high = Math.Max(0, missing);
                        while (low < high)
                        {
                            int middle = low + (int)(((long)high - low + 1) / 2);
                            if (supply.Store.CanAddSavedCenterItems(coordinate, supply.Item.id, middle, supply.Count)) low = middle;
                            else high = middle - 1;
                        }
                        if (low > 0) supply.Store.TryAddSavedCenterItems(coordinate, supply.Item.id, low, supply.Count);
                    }
                }
            }
            finally { refilling = false; }
        }

        internal static int Consume(object owner, TerrainGenerator terrain, Vector2Int coordinate, int item, int count, Vector3 target, float interval)
        {
            Refill(coordinate);
            // Consumption is presentation only. Stored/player-supplied items remain authoritative and intact.
            if (terrain != null && terrain.TryGetLoadedBlock(coordinate, out var block))
                for (int i = 0; i < Math.Min(count, 4); i++) block.PlayVirtualInputAreaConsumeAnimation(item, target, i * interval);
            return Math.Max(0, count);
        }
        internal static void SampleEnergy(object owner, Vector3 target, bool working)
        {
            if (!BenchmarkRuntime.ForceWorking || !owners.TryGetValue(owner, out var state)) return;
            long now = MapObjectTickManager.CurrentSimulationTick, ticks = Math.Max(0, now - state.EnergyTick);
            state.EnergyTick = now;
            if (!working || ticks == 0) return;
            for (int i = 0; i < state.Fuels.Count; i++)
            {
                var fuel = state.Fuels[i];
                if (fuel.Rate <= 0 || fuel.Units <= 0) continue;
                long requested = ticks > long.MaxValue / fuel.Rate ? long.MaxValue : ticks * fuel.Rate;
                if (requested <= fuel.Remaining) { fuel.Remaining -= requested; continue; }
                long missing = requested - fuel.Remaining;
                long count = 1 + (missing - 1) / fuel.Units;
                fuel.Remaining = (fuel.Units - missing % fuel.Units) % fuel.Units;
                Consume(owner, fuel.Supply.Terrain, fuel.Supply.Coordinate, fuel.Supply.Item.id, (int)Math.Min(4, count), target, .1f);
            }
        }
    }
}
