using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Benchmark
{
    public static class BenchmarkRuntime
    {
        private static readonly List<InstallationObject> installations = new List<InstallationObject>();
        public static bool ForceWorking { get; private set; }
        public static int FallbackItemId { get; private set; } = -1;
        public static long ProducedItems { get; private set; }
        public static double SpilledFluidLiters { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        { ForceWorking = false; FallbackItemId = -1; ProducedItems = 0; SpilledFluidLiters = 0; installations.Clear(); }

        public static bool IsPortableItem(ItemDefinition item) => item != null && item.id >= 0
            && !item.isFluid && !ItemDefinition.IsElectricityItemDefinition(item)
            && item.portableMesh != null && item.portableMat != null;

        public static List<int> CollectPortableItemIds(ItemManager manager)
        {
            var ids = new List<int>();
            var definitions = manager != null ? manager.ItemDefinitions : null;
            if (definitions != null)
                for (int i = 0; i < definitions.Count; i++)
                    if (IsPortableItem(definitions[i])) ids.Add(definitions[i].id);
            return ids;
        }

        public static void SetForceWorking(bool enabled, int fallbackItemId = -1)
        {
            if (ForceWorking == enabled && (fallbackItemId < 0 || fallbackItemId == FallbackItemId)) return;
            ForceWorking = enabled;
            if (fallbackItemId >= 0) FallbackItemId = fallbackItemId;
            if (enabled) { ProducedItems = 0; SpilledFluidLiters = 0; }
            InstallationObject.CopyActiveInstances(installations);
            for (int i = 0; i < installations.Count; i++) Wake(installations[i]);
            installations.Clear();
            RobotArmWorld.Current?.WakeAll();
            MiningWorld.Current?.WakeAll();
        ProductionWorld.Current?.WakeAll();
            UtilityPole.NotifyFreeElectroEnergyChanged();
        }

        public static void Wake(InstallationObject installation)
        {
            if (installation == null) return;
            if (installation is InputOutputModule module) module.ResetBenchmarkWork();
            if (installation is LoggingMachine logger) logger.ResetBenchmarkWork();
        }

        internal static void RecordItems(int count) => ProducedItems += count;
        internal static void RecordSpill(float liters) => SpilledFluidLiters += System.Math.Max(0f, liters);

        internal static bool EmitItem(TerrainGenerator terrain, int itemId, Vector3 position)
        {
            if (terrain == null || itemId < 0) return false;
            var origin = TerrainGenerator.GetWorldBlockCoordinate(position);
            // Use a real floor stack, independent of the player's focused belt.
            for (int i = 0; i < 5; i++)
            {
                var coordinate = origin + (i == 0 ? Vector2Int.zero : i == 1 ? Vector2Int.up : i == 2 ? Vector2Int.right : i == 3 ? Vector2Int.down : Vector2Int.left);
                if (!terrain.TryGetLoadedBlock(coordinate, out var block)) continue;
                bool success = block.TryAddDeferredOutput(itemId, position, 0f, false, out bool handled);
                if (!handled) success = block.TryAddFloorObjectAnimated(itemId, position, 0f, out _);
                if (success) { ProducedItems++; return true; }
            }
            return false;
        }
    }
}
