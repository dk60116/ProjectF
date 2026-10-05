using System;
using UnityEngine;

namespace ProjectF.MapObjects
{
    // Shared solid fuel storage/transport boundary. The owning world schedules consumption.
    internal static class FacilityFuel
    {
        internal static void RestoreLegacy(InputOutputModule.PersistentState io, ItemDefinition definition)
        {
            if (io.storedEnergyTypes.Count != 0 || io.storedEnergy <= 0) return;
            for (int i = 0; i < definition.UseEnergyRequirementCount; i++)
                if (definition.TryGetUseEnergyRequirement(i, out var requirement)
                    && requirement.energyType != ItemDefinition.EnergyType.None
                    && requirement.energyType != ItemDefinition.EnergyType.Electricity)
                {
                    io.storedEnergyTypes.Add((int)requirement.energyType);
                    io.storedEnergyUnitsByType.Add(DeterministicSimulationUnits.FromFloat(io.storedEnergy));
                    io.energyGaugeCapacityUnitsByType.Add(DeterministicSimulationUnits.FromFloat(io.energyGaugeCapacity));
                    break;
                }
        }

        internal static void Persist(InputOutputModule.PersistentState io)
        {
            io.storedEnergyUnits = io.storedEnergyTypes.Count > 0 ? io.storedEnergyUnitsByType[0] : 0;
            io.energyGaugeCapacityUnits = io.storedEnergyTypes.Count > 0 ? io.energyGaugeCapacityUnitsByType[0] : 0;
            io.storedEnergy = DeterministicSimulationUnits.ToFloat(io.storedEnergyUnits);
            io.energyGaugeCapacity = DeterministicSimulationUnits.ToFloat(io.energyGaugeCapacityUnits);
        }

        internal static long Stored(InputOutputModule.PersistentState io, ItemDefinition.EnergyType type)
        {
            int index = io.storedEnergyTypes.IndexOf((int)type);
            return index < 0 ? 0 : io.storedEnergyUnitsByType[index];
        }

        internal static int CountAt(IDataItemProducer owner, Vector2Int coordinate, int item, bool respectMinimum = true)
        {
            if (!owner.Terrain.TryGetLoadedBlock(coordinate, out var block) || owner.Terrain.IsFloorObjectCoordinateVirtualized(coordinate))
                return respectMinimum ? owner.Store.GetSavedCenterExtractableItemCount(coordinate, item) : owner.Store.GetSavedCenterItemCount(coordinate, item);
            return block.Type == Block.BlockType.Ground ? Math.Max(0, block.GetInputAreaCenterItemCount(item)
                - (respectMinimum && block.MapObject is BoxObject box ? box.MinimumRetainedItemCount : 0)) : 0;
        }

        internal static int ConsumeAt(IDataItemProducer owner, Vector2Int coordinate, int item, int count, Vector3 target, float interval,
            bool respectMinimum = true, bool animateVirtualized = false)
        {
            count = Math.Min(count, CountAt(owner, coordinate, item, respectMinimum));
            if (count <= 0) return 0;
            if (!owner.Terrain.TryGetLoadedBlock(coordinate, out var block) || owner.Terrain.IsFloorObjectCoordinateVirtualized(coordinate))
            {
                int removed = owner.Store.RemoveSavedCenterItems(coordinate, item, count);
                if (animateVirtualized && block != null)
                    for (int i = 0; i < removed; i++) block.PlayVirtualInputAreaConsumeAnimation(item, target, i * Mathf.Max(0, interval));
                return removed;
            }
            return block.ConsumeInputAreaCenterObjectsAnimated(item, count, target, interval);
        }

        internal static bool Refill(IDataItemProducer owner, ItemDefinition.EnergyType type, Vector3 target, float interval)
        {
            var io = owner.Placement.inputOutputState;
            for (int i = 0; i < io.inputEnergyCoordinates.Count; i++)
            {
                var coordinate = io.inputEnergyCoordinates[i];
                int item = owner.Terrain.TryGetLoadedBlock(coordinate, out var block) && !owner.Terrain.IsFloorObjectCoordinateVirtualized(coordinate)
                    ? block.GetInputAreaCenterItemId() : owner.Store.GetSavedCenterTopItemId(coordinate);
                var definition = InputOutputModule.ResolveItemDefinition(item);
                if (definition == null || definition.energyType != type || definition.energyAmount <= 0 || definition.isFluid) continue;
                if (ConsumeAt(owner, coordinate, item, 1, target, interval) != 1) continue;
                int index = io.storedEnergyTypes.IndexOf((int)type);
                if (index < 0)
                {
                    index = io.storedEnergyTypes.Count; io.storedEnergyTypes.Add((int)type);
                    io.storedEnergyUnitsByType.Add(0); io.energyGaugeCapacityUnitsByType.Add(0);
                }
                long units = DeterministicSimulationUnits.FromFloat(definition.energyAmount);
                io.storedEnergyUnitsByType[index] += units; io.energyGaugeCapacityUnitsByType[index] = units;
                return true;
            }
            return false;
        }

        internal static long Spend(IDataItemProducer owner, ItemDefinition.EnergyType type, long requested, Vector3 target, float interval)
        {
            var io = owner.Placement.inputOutputState;
            long remaining = requested;
            while (remaining > 0)
            {
                if (Stored(io, type) <= 0 && !Refill(owner, type, target, interval)) break;
                int index = io.storedEnergyTypes.IndexOf((int)type);
                long spent = Math.Min(remaining, io.storedEnergyUnitsByType[index]);
                io.storedEnergyUnitsByType[index] -= spent; remaining -= spent;
            }
            return requested - remaining;
        }

        internal static bool GetInputInfo(IDataItemProducer owner, out int item, out int count, out int capacity, out int burnEnergy)
        {
            var coordinates = owner.Placement.inputOutputState.inputEnergyCoordinates;
            item = -1; count = capacity = burnEnergy = 0;
            if (coordinates.Count == 0) return false;
            double totalEnergy = 0;
            for (int i = 0; i < coordinates.Count; i++)
            {
                var coordinate = coordinates[i];
                bool loaded = owner.Terrain.TryGetLoadedBlock(coordinate, out var block) && !owner.Terrain.IsFloorObjectCoordinateVirtualized(coordinate);
                int current = loaded ? block.GetInputAreaCenterItemId() : owner.Store.GetSavedCenterTopItemId(coordinate);
                var fuel = InputOutputModule.ResolveItemDefinition(current);
                int stored = loaded ? block.GetInputAreaCenterItemCount(current) : owner.Store.GetSavedCenterItemCount(coordinate, current);
                if (item < 0 && current >= 0) item = current;
                if (current == item) count = (int)Math.Min(int.MaxValue, (long)count + stored);
                capacity = (int)Math.Min(int.MaxValue, (long)capacity + (loaded ? block.GetInputAreaCenterCapacity(current) : owner.OutputPrototype.RuntimeAreaMaxObjects));
                if (fuel?.energyType == ItemDefinition.EnergyType.Burn) totalEnergy += stored * (double)fuel.energyAmount;
            }
            burnEnergy = (int)Math.Min(int.MaxValue, Math.Max(0, totalEnergy));
            return true;
        }
    }
}
