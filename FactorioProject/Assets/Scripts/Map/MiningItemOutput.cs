using UnityEngine;

// Engine boundary only: production/resource decisions are owned by the data entity.
internal static class MiningItemOutput
{
    internal readonly struct Reservation
    {
        internal readonly Block Block;
        internal readonly Vector2Int Coordinate;
        internal readonly int Capacity;
        internal readonly bool Saved, Distributed;
        internal Reservation(Block block, Vector2Int coordinate, int capacity, bool saved, bool distributed = false)
        { Block = block; Coordinate = coordinate; Capacity = capacity; Saved = saved; Distributed = distributed; }
    }
    internal static bool TryReserve(IDataItemProducer miner, int itemId, int count, out Reservation reservation)
    {
        reservation = default;
        ItemDefinition item = InputOutputModule.ResolveItemDefinition(itemId);
        if (item == null || item.isFluid || itemId < 0 || count <= 0) return false;
        if (!ProjectF.Benchmark.BenchmarkRuntime.ForceWorking && item.oneItem && count > 1)
        {
            int available = 0;
            foreach (var coordinate in miner.OutputCoordinates)
                if (TryReserveCoordinate(miner, itemId, 1, coordinate, false, out _)) available++;
            if (available < count) return false;
            reservation = new Reservation(null, default, 0, false, true); return true;
        }
        var coordinates = miner.OutputCoordinates;
        // Prefer an existing stack, as the normal IOModule does.
        for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i < coordinates.Count; i++)
        {
            if (TryReserveCoordinate(miner, itemId, count, coordinates[i], pass == 0, out reservation)) return true;
        }
        return false;
    }
    private static bool TryReserveCoordinate(IDataItemProducer miner, int itemId, int count, Vector2Int coordinate,
        bool requireExisting, out Reservation reservation)
    {
        reservation = default;
        if (miner.OutputPrototype.TryGetRectGridBlockPlacementAtCoordinate(miner.OutputPrototype, miner.AnchorCoordinate,
            miner.Placement.quarterTurns, coordinate, out var placement) && placement.itemDefinition != null
            && placement.itemDefinition.id >= 0 && placement.itemDefinition.id != itemId) return false;
        bool loaded = miner.Terrain.TryGetLoadedBlock(coordinate, out Block block) && block != null;
        if (loaded && block.IsRuntimeConveyor)
        {
            block.EnsureConveyorTransportInteractionBoundary();
            if (InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(coordinate, itemId)
                && block.CanAddConveyorObjects(count))
            { reservation = new Reservation(block, coordinate, 0, false); return true; }
            return false;
        }
        int capacity = ItemDefinition.ResolveStackCapacity(InputOutputModule.ResolveItemDefinition(itemId), miner.OutputPrototype.RuntimeAreaMaxObjects);
        bool savedBox = false;
        if (loaded)
        {
            if (block.MapObject is BoxObject box && !box.AcceptsItem(itemId)) return false;
            if (block.Type != Block.BlockType.Ground || !block.CanAddInputAreaCenterObjects(count, itemId)
                || requireExisting && block.GetInputAreaCenterItemId() != itemId) return false;
            reservation = new Reservation(block, coordinate, block.GetInputAreaCenterCapacity(itemId), false); return true;
        }
        // An unloaded belt owns lane state. Never write into its saved floor stack.
        if (miner.Store.TryGetInstallationAnchorAtCoordinate(coordinate, out var anchor)
            && miner.Store.TryGetInstallationState(anchor, out var installed))
        {
            var definition = InputOutputModule.ResolveItemDefinition(installed.itemId);
            savedBox = definition?.mapObject is BoxObject;
            if (definition?.mapObject is ConveyorBelt) return false;
            if (definition?.mapObject is BoxObject && !MapObject.IsItemAllowedByFilterMask(itemId,
                installed.itemFilterMaskInitialized, installed.itemFilterMaskWords)) return false;
            if (definition != null && definition.capacity > 0) capacity = ItemDefinition.ResolveStackCapacity(InputOutputModule.ResolveItemDefinition(itemId), definition.capacity);
        }
        if (ProjectF.Benchmark.BenchmarkRuntime.ForceWorking && !savedBox) capacity = int.MaxValue;
        if (!miner.Store.CanAddSavedCenterItems(coordinate, itemId, count, capacity)
            || requireExisting && miner.Store.GetSavedCenterTopItemId(coordinate) != itemId) return false;
        reservation = new Reservation(null, coordinate, capacity, true); return true;
    }
    internal static bool Emit(IDataItemProducer miner, Reservation target, int itemId,
        ref int remaining, Vector3 start)
    {
        if (target.Distributed)
        {
            while (remaining > 0)
            {
                if (!TryReserve(miner, itemId, 1, out var single)) return false;
                int one = 1;
                if (!Emit(miner, single, itemId, ref one, start)) return false;
                remaining--;
            }
            return true;
        }
        if (target.Saved)
        {
            if (!miner.Store.TryAddSavedCenterItems(target.Coordinate, itemId, remaining, target.Capacity)) return false;
            remaining = 0; return true;
        }
        int emitted = 0;
        while (remaining > 0)
        {
            bool success = InputOutputModule.TryEmitOutputItemToBlock(target.Block, itemId, start, emitted * miner.OutputPrototype.OutputMoveInterval, out _);
            if (!success) return false;
            remaining--; emitted++;
        }
        return true;
    }
}
