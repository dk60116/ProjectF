using ConveyorLine = CurrentResolver.ConveyorLine;

// Frozen from TerrainGenerator.Conveyors.cs before the runtime-proxy cache.
internal sealed class LegacyResolver
{
    private readonly BlockDataStore loadedBlocks;
    internal LegacyResolver(BlockDataStore store) { loadedBlocks = store; }

    private bool TryResolveLoadedRuntimeBlock(BlockHandle handle, out Block block)
    {
        block = null;
        return handle.IsValid
            && loadedBlocks.TryGetValue(handle, out block)
            && block != null
            && block.gameObject.activeInHierarchy;
    }

    private bool TryResolveConveyorLineBlock(
        ConveyorLine line,
        int slotIndex,
        out Block block)
    {
        block = null;
        return line != null
            && slotIndex >= 0
            && slotIndex < line.blockHandles.Count
            && TryResolveLoadedRuntimeBlock(line.blockHandles[slotIndex], out block);
    }

    internal bool Resolve(ConveyorLine line, int slot, out Block block)
        => TryResolveConveyorLineBlock(line, slot, out block);
}
