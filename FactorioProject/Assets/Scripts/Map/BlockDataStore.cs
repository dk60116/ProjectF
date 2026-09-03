using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum BlockCellFlags : byte
{
    None = 0,
    Registered = 1 << 0,
    HasRuntimeProxy = 1 << 1
}

public readonly struct BlockHandle : IEquatable<BlockHandle>
{
    internal BlockHandle(Vector2Int chunkCoordinate, int localIndex, uint generation)
    {
        ChunkCoordinate = chunkCoordinate;
        LocalIndex = localIndex;
        Generation = generation;
    }

    public Vector2Int ChunkCoordinate { get; }
    public int LocalIndex { get; }
    internal uint Generation { get; }
    public bool IsValid => LocalIndex >= 0 && Generation != 0;

    public bool Equals(BlockHandle other)
    {
        return ChunkCoordinate == other.ChunkCoordinate
               && LocalIndex == other.LocalIndex
               && Generation == other.Generation;
    }

    public override bool Equals(object obj)
    {
        return obj is BlockHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = ChunkCoordinate.GetHashCode();
            hash = (hash * 397) ^ LocalIndex;
            hash = (hash * 397) ^ (int)Generation;
            return hash;
        }
    }

    public static bool operator ==(BlockHandle left, BlockHandle right) => left.Equals(right);
    public static bool operator !=(BlockHandle left, BlockHandle right) => !left.Equals(right);
}

public readonly struct BlockCellData
{
    internal BlockCellData(Block.BlockType type, BlockCellFlags flags)
    {
        Type = type;
        Flags = flags;
    }

    public Block.BlockType Type { get; }
    public BlockCellFlags Flags { get; }
    public bool IsRegistered => (Flags & BlockCellFlags.Registered) != 0;
    public bool HasRuntimeProxy => (Flags & BlockCellFlags.HasRuntimeProxy) != 0;
}

/// <summary>
/// Chunk-contiguous cell storage. Stateful cells may have a Block component
/// proxy, but every proxy is hosted by the single TerrainGenerator GameObject;
/// no cell owns a GameObject or Transform.
/// </summary>
public sealed class BlockDataStore : IEnumerable<KeyValuePair<Vector2Int, Block>>
{
    internal sealed class ChunkData
    {
        public readonly Vector2Int Coordinate;
        public readonly Vector2Int Origin;
        public readonly uint Generation;
        public readonly CellRecord[] Cells;
        public readonly Dictionary<int, Block> RuntimeProxies = new Dictionary<int, Block>();
        public int RegisteredCellCount;
        public int RuntimeProxyCount;

        public ChunkData(Vector2Int coordinate, int chunkSize, uint generation)
        {
            Coordinate = coordinate;
            Origin = coordinate * chunkSize;
            Generation = generation;
            int cellCount = chunkSize * chunkSize;
            Cells = new CellRecord[cellCount];
        }
    }

    internal struct CellRecord
    {
        public byte Type;
        public BlockCellFlags Flags;
    }

    private readonly Dictionary<Vector2Int, ChunkData> chunks =
        new Dictionary<Vector2Int, ChunkData>();
    private int chunkSize;
    private int registeredCellCount;
    private int runtimeProxyCount;
    private uint nextChunkGeneration = 1;
    private bool hasRegisteredBounds;
    private bool registeredBoundsDirty;
    private Vector2Int registeredMinCoordinate;
    private Vector2Int registeredMaxCoordinate;

    public int ChunkSize => chunkSize;
    public int ChunkCount => chunks.Count;
    public int RegisteredCellCount => registeredCellCount;
    public int Count => runtimeProxyCount;

    public void ConfigureChunkSize(int value)
    {
        int normalizedSize = Mathf.Max(1, value);
        if (chunkSize == normalizedSize)
        {
            return;
        }

        Clear();
        chunkSize = normalizedSize;
    }

    public bool RegisterChunk(Vector2Int chunkCoordinate)
    {
        EnsureConfigured();
        if (chunks.ContainsKey(chunkCoordinate))
        {
            return false;
        }

        chunks.Add(chunkCoordinate, new ChunkData(
            chunkCoordinate,
            chunkSize,
            AllocateChunkGeneration()));
        return true;
    }

    public bool UnregisterChunk(Vector2Int chunkCoordinate)
    {
        if (!chunks.TryGetValue(chunkCoordinate, out ChunkData chunk))
        {
            return false;
        }

        registeredCellCount -= chunk.RegisteredCellCount;
        runtimeProxyCount -= chunk.RuntimeProxyCount;
        chunks.Remove(chunkCoordinate);
        if (chunk.RegisteredCellCount > 0)
        {
            registeredBoundsDirty = true;
        }
        return true;
    }

    public bool RegisterCell(
        Vector2Int coordinate,
        Block.BlockType type,
        out BlockHandle handle)
    {
        ChunkData chunk = GetOrCreateChunk(coordinate, out int localIndex);
        ref CellRecord cell = ref chunk.Cells[localIndex];
        if ((cell.Flags & BlockCellFlags.Registered) == 0)
        {
            cell.Flags |= BlockCellFlags.Registered;
            chunk.RegisteredCellCount++;
            registeredCellCount++;
            ExpandRegisteredBounds(coordinate);
        }

        cell.Type = (byte)type;
        handle = new BlockHandle(chunk.Coordinate, localIndex, chunk.Generation);
        return true;
    }

    public bool TryGetHandle(Vector2Int coordinate, out BlockHandle handle)
    {
        handle = default;
        if (!TryGetChunkAndLocalIndex(coordinate, out ChunkData chunk, out int localIndex)
            || (chunk.Cells[localIndex].Flags & BlockCellFlags.Registered) == 0)
        {
            return false;
        }

        handle = new BlockHandle(chunk.Coordinate, localIndex, chunk.Generation);
        return true;
    }

    public bool TryGetCell(Vector2Int coordinate, out BlockCellData cellData)
    {
        cellData = default;
        if (!TryGetChunkAndLocalIndex(coordinate, out ChunkData chunk, out int localIndex))
        {
            return false;
        }

        CellRecord cell = chunk.Cells[localIndex];
        if ((cell.Flags & BlockCellFlags.Registered) == 0)
        {
            return false;
        }

        cellData = new BlockCellData((Block.BlockType)cell.Type, cell.Flags);
        return true;
    }

    public bool TryGetCell(BlockHandle handle, out BlockCellData cellData)
    {
        cellData = default;
        if (!TryResolveHandle(handle, out ChunkData chunk))
        {
            return false;
        }

        CellRecord cell = chunk.Cells[handle.LocalIndex];
        if ((cell.Flags & BlockCellFlags.Registered) == 0)
        {
            return false;
        }

        cellData = new BlockCellData((Block.BlockType)cell.Type, cell.Flags);
        return true;
    }

    public bool TryGetCoordinate(BlockHandle handle, out Vector2Int coordinate)
    {
        coordinate = default;
        if (!TryResolveHandle(handle, out ChunkData chunk))
        {
            return false;
        }

        int localX = handle.LocalIndex % chunkSize;
        int localY = handle.LocalIndex / chunkSize;
        coordinate = chunk.Origin + new Vector2Int(localX, localY);
        return true;
    }

    public bool TryGetRegisteredBounds(out Vector2Int minCoordinate, out Vector2Int maxCoordinate)
    {
        if (registeredBoundsDirty)
        {
            RecalculateRegisteredBounds();
        }

        minCoordinate = registeredMinCoordinate;
        maxCoordinate = registeredMaxCoordinate;
        return hasRegisteredBounds;
    }

    private void ExpandRegisteredBounds(Vector2Int coordinate)
    {
        if (registeredBoundsDirty)
        {
            return;
        }

        if (!hasRegisteredBounds)
        {
            registeredMinCoordinate = coordinate;
            registeredMaxCoordinate = coordinate;
            hasRegisteredBounds = true;
            return;
        }

        registeredMinCoordinate = Vector2Int.Min(registeredMinCoordinate, coordinate);
        registeredMaxCoordinate = Vector2Int.Max(registeredMaxCoordinate, coordinate);
    }

    private void RecalculateRegisteredBounds()
    {
        hasRegisteredBounds = false;
        registeredMinCoordinate = default;
        registeredMaxCoordinate = default;
        foreach (KeyValuePair<Vector2Int, ChunkData> pair in chunks)
        {
            ChunkData chunk = pair.Value;
            if (chunk == null || chunk.RegisteredCellCount <= 0)
            {
                continue;
            }

            for (int localIndex = 0; localIndex < chunk.Cells.Length; localIndex++)
            {
                if ((chunk.Cells[localIndex].Flags & BlockCellFlags.Registered) == 0)
                {
                    continue;
                }

                int localX = localIndex % chunkSize;
                int localY = localIndex / chunkSize;
                Vector2Int coordinate = chunk.Origin + new Vector2Int(localX, localY);
                if (!hasRegisteredBounds)
                {
                    registeredMinCoordinate = coordinate;
                    registeredMaxCoordinate = coordinate;
                    hasRegisteredBounds = true;
                    continue;
                }

                registeredMinCoordinate = Vector2Int.Min(registeredMinCoordinate, coordinate);
                registeredMaxCoordinate = Vector2Int.Max(registeredMaxCoordinate, coordinate);
            }
        }

        registeredBoundsDirty = false;
    }

    public bool BindRuntimeProxy(Vector2Int coordinate, Block block, out BlockHandle handle)
    {
        handle = default;
        if (block == null)
        {
            return false;
        }

        RegisterCell(coordinate, block.Type, out handle);
        ChunkData chunk = chunks[handle.ChunkCoordinate];
        Block previous = GetRuntimeProxy(chunk, handle.LocalIndex);
        if (previous == block)
        {
            return true;
        }

        if ((chunk.Cells[handle.LocalIndex].Flags & BlockCellFlags.HasRuntimeProxy) == 0)
        {
            chunk.RuntimeProxyCount++;
            runtimeProxyCount++;
        }

        SetRuntimeProxy(chunk, handle.LocalIndex, block);
        chunk.Cells[handle.LocalIndex].Flags |= BlockCellFlags.HasRuntimeProxy;
        return true;
    }

    public bool TryGetValue(Vector2Int coordinate, out Block block)
    {
        block = null;
        if (!TryGetChunkAndLocalIndex(coordinate, out ChunkData chunk, out int localIndex)
            || (chunk.Cells[localIndex].Flags & BlockCellFlags.HasRuntimeProxy) == 0)
        {
            return false;
        }

        block = GetRuntimeProxy(chunk, localIndex);
        if (block != null)
        {
            return true;
        }

        ClearRuntimeProxy(chunk, localIndex);
        return false;
    }

    public bool TryGetValue(BlockHandle handle, out Block block)
    {
        block = null;
        if (!TryResolveHandle(handle, out ChunkData chunk)
            || (chunk.Cells[handle.LocalIndex].Flags & BlockCellFlags.HasRuntimeProxy) == 0)
        {
            return false;
        }

        block = GetRuntimeProxy(chunk, handle.LocalIndex);
        if (block != null)
        {
            return true;
        }

        ClearRuntimeProxy(chunk, handle.LocalIndex);
        return false;
    }

    public bool Remove(Vector2Int coordinate)
    {
        if (!TryGetChunkAndLocalIndex(coordinate, out ChunkData chunk, out int localIndex)
            || (chunk.Cells[localIndex].Flags & BlockCellFlags.HasRuntimeProxy) == 0)
        {
            return false;
        }

        ClearRuntimeProxy(chunk, localIndex);
        return true;
    }

    public void CompactRuntimeProxyStorage(Vector2Int chunkCoordinate)
    {
        if (!chunks.TryGetValue(chunkCoordinate, out ChunkData chunk))
        {
            return;
        }

        chunk.RuntimeProxies.TrimExcess();
    }

    public void CopyRuntimeProxies(Vector2Int chunkCoordinate, List<Block> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        if (!chunks.TryGetValue(chunkCoordinate, out ChunkData chunk))
        {
            return;
        }

        foreach (KeyValuePair<int, Block> pair in chunk.RuntimeProxies)
        {
            if (pair.Value != null)
            {
                results.Add(pair.Value);
            }
        }
    }

    public void CopyRegisteredCoordinates(Vector2Int chunkCoordinate, List<Vector2Int> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        if (!chunks.TryGetValue(chunkCoordinate, out ChunkData chunk))
        {
            return;
        }

        for (int localIndex = 0; localIndex < chunk.Cells.Length; localIndex++)
        {
            if ((chunk.Cells[localIndex].Flags & BlockCellFlags.Registered) == 0)
            {
                continue;
            }

            int localX = localIndex % chunkSize;
            int localY = localIndex / chunkSize;
            results.Add(chunk.Origin + new Vector2Int(localX, localY));
        }
    }

    public void Clear()
    {
        chunks.Clear();
        registeredCellCount = 0;
        runtimeProxyCount = 0;
        hasRegisteredBounds = false;
        registeredBoundsDirty = false;
        registeredMinCoordinate = default;
        registeredMaxCoordinate = default;
    }

    public Enumerator GetEnumerator() => new Enumerator(chunks, chunkSize);

    IEnumerator<KeyValuePair<Vector2Int, Block>> IEnumerable<KeyValuePair<Vector2Int, Block>>.GetEnumerator()
        => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private ChunkData GetOrCreateChunk(Vector2Int coordinate, out int localIndex)
    {
        EnsureConfigured();
        Vector2Int chunkCoordinate = GetChunkCoordinate(coordinate);
        if (!chunks.TryGetValue(chunkCoordinate, out ChunkData chunk))
        {
            chunk = new ChunkData(chunkCoordinate, chunkSize, AllocateChunkGeneration());
            chunks.Add(chunkCoordinate, chunk);
        }

        localIndex = GetLocalIndex(coordinate, chunkCoordinate);
        return chunk;
    }

    private bool TryGetChunkAndLocalIndex(
        Vector2Int coordinate,
        out ChunkData chunk,
        out int localIndex)
    {
        chunk = null;
        localIndex = -1;
        if (chunkSize <= 0)
        {
            return false;
        }

        Vector2Int chunkCoordinate = GetChunkCoordinate(coordinate);
        if (!chunks.TryGetValue(chunkCoordinate, out chunk))
        {
            return false;
        }

        localIndex = GetLocalIndex(coordinate, chunkCoordinate);
        return localIndex >= 0 && localIndex < chunk.Cells.Length;
    }

    private bool TryResolveHandle(BlockHandle handle, out ChunkData chunk)
    {
        chunk = null;
        return handle.IsValid
               && chunks.TryGetValue(handle.ChunkCoordinate, out chunk)
               && chunk.Generation == handle.Generation
               && handle.LocalIndex >= 0
               && handle.LocalIndex < chunk.Cells.Length;
    }

    private Vector2Int GetChunkCoordinate(Vector2Int coordinate)
    {
        return new Vector2Int(
            FloorDivide(coordinate.x, chunkSize),
            FloorDivide(coordinate.y, chunkSize));
    }

    private int GetLocalIndex(Vector2Int coordinate, Vector2Int chunkCoordinate)
    {
        int localX = coordinate.x - (chunkCoordinate.x * chunkSize);
        int localY = coordinate.y - (chunkCoordinate.y * chunkSize);
        return localX + (localY * chunkSize);
    }

    private static int FloorDivide(int value, int divisor)
    {
        return value >= 0 ? value / divisor : ((value + 1) / divisor) - 1;
    }

    private uint AllocateChunkGeneration()
    {
        uint generation = nextChunkGeneration++;
        if (generation != 0)
        {
            return generation;
        }

        generation = nextChunkGeneration++;
        return generation == 0 ? 1u : generation;
    }

    private void EnsureConfigured()
    {
        if (chunkSize <= 0)
        {
            throw new InvalidOperationException("BlockDataStore must be configured before use.");
        }
    }

    private void ClearRuntimeProxy(ChunkData chunk, int localIndex)
    {
        if ((chunk.Cells[localIndex].Flags & BlockCellFlags.HasRuntimeProxy) == 0)
        {
            return;
        }

        chunk.RuntimeProxies.Remove(localIndex);
        chunk.Cells[localIndex].Flags &= ~BlockCellFlags.HasRuntimeProxy;
        chunk.RuntimeProxyCount--;
        runtimeProxyCount--;
    }

    private static Block GetRuntimeProxy(ChunkData chunk, int localIndex)
    {
        return chunk.RuntimeProxies.TryGetValue(localIndex, out Block block)
            ? block
            : null;
    }

    private static void SetRuntimeProxy(ChunkData chunk, int localIndex, Block block)
    {
        chunk.RuntimeProxies[localIndex] = block;
    }

    public struct Enumerator : IEnumerator<KeyValuePair<Vector2Int, Block>>
    {
        private Dictionary<Vector2Int, ChunkData>.Enumerator chunkEnumerator;
        private readonly int chunkSize;
        private ChunkData currentChunk;
        private Dictionary<int, Block>.Enumerator runtimeProxyEnumerator;
        private KeyValuePair<Vector2Int, Block> current;

        internal Enumerator(Dictionary<Vector2Int, ChunkData> chunks, int chunkSize)
        {
            chunkEnumerator = chunks.GetEnumerator();
            this.chunkSize = chunkSize;
            currentChunk = null;
            runtimeProxyEnumerator = default;
            current = default;
        }

        public KeyValuePair<Vector2Int, Block> Current => current;
        object IEnumerator.Current => current;

        public bool MoveNext()
        {
            while (true)
            {
                if (currentChunk != null)
                {
                    while (runtimeProxyEnumerator.MoveNext())
                    {
                        KeyValuePair<int, Block> pair = runtimeProxyEnumerator.Current;
                        if (pair.Value == null)
                        {
                            continue;
                        }

                        int localX = pair.Key % chunkSize;
                        int localY = pair.Key / chunkSize;
                        Vector2Int coordinate = currentChunk.Origin + new Vector2Int(localX, localY);
                        current = new KeyValuePair<Vector2Int, Block>(coordinate, pair.Value);
                        return true;
                    }
                }

                if (!chunkEnumerator.MoveNext())
                {
                    current = default;
                    return false;
                }

                currentChunk = chunkEnumerator.Current.Value;
                runtimeProxyEnumerator = currentChunk.RuntimeProxies.GetEnumerator();
            }
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
            runtimeProxyEnumerator.Dispose();
            chunkEnumerator.Dispose();
        }
    }
}
