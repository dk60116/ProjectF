using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

internal static class HarnessMetrics { internal static long HandleLookups; }
// Ownership is outside this resolver-only harness; the world harness runs the actual transport.
namespace ProjectF.Conveyors { internal sealed class ConveyorTransportRun { } }
public sealed class BlockRuntimeSimulationState { }
public sealed class Block : UnityEngine.Object
{
    public enum BlockType : byte { Empty, Ground, Conveyor }
    public BlockType Type = BlockType.Conveyor;
    public readonly GameObject gameObject = new GameObject();
}
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool aNull = ReferenceEquals(a, null) || a.Destroyed;
            bool bNull = ReferenceEquals(b, null) || b.Destroyed;
            return aNull || bNull ? aNull == bNull : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
    public sealed class GameObject
    {
        public bool activeSelf = true;
        public GameObject Parent;
        public bool activeInHierarchy => activeSelf && (Parent == null || Parent.activeInHierarchy);
    }
    public readonly struct Vector2Int : IEquatable<Vector2Int>
    {
        public readonly int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new Vector2Int(a.x + b.x, a.y + b.y);
        public static Vector2Int operator *(Vector2Int a, int b) => new Vector2Int(a.x * b, a.y * b);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.Equals(b);
        public static bool operator !=(Vector2Int a, Vector2Int b) => !a.Equals(b);
        public static Vector2Int Min(Vector2Int a, Vector2Int b) => new Vector2Int(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        public static Vector2Int Max(Vector2Int a, Vector2Int b) => new Vector2Int(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Vector2Int other && Equals(other);
        public override int GetHashCode() => unchecked(x * 397 ^ y);
    }
    public static class Mathf { public static int Max(int a, int b) => Math.Max(a, b); }
}

internal partial class CurrentResolver
{
    private readonly BlockDataStore loadedBlocks;
    internal CurrentResolver(BlockDataStore store) { loadedBlocks = store; }
    internal bool Resolve(ConveyorLine line, int slot, out Block block) => TryResolveConveyorLineBlock(line, slot, out block);
    internal static ConveyorLine Line(params BlockHandle[] handles)
    {
        var line = new ConveyorLine(1);
        line.blockHandles.AddRange(handles);
        return line; // Production resolver must allocate its cache lazily.
    }
}

internal sealed class StorePair
{
    internal readonly BlockDataStore Legacy = new BlockDataStore();
    internal readonly BlockDataStore Current = new BlockDataStore();
    internal readonly LegacyResolver LegacyResolver;
    internal readonly CurrentResolver CurrentResolver;
    internal StorePair()
    {
        Legacy.ConfigureChunkSize(4);
        Current.ConfigureChunkSize(4);
        LegacyResolver = new LegacyResolver(Legacy);
        CurrentResolver = new CurrentResolver(Current);
    }
    internal BlockHandle Bind(Vector2Int coordinate, Block block)
    {
        bool oldResult = Legacy.BindRuntimeProxy(coordinate, block, out BlockHandle oldHandle);
        bool newResult = Current.BindRuntimeProxy(coordinate, block, out BlockHandle newHandle);
        Checks.Require(oldResult == newResult && oldHandle == newHandle, "bind result/handle");
        return newHandle;
    }
    internal void Remove(Vector2Int coordinate)
        => Checks.Require(Legacy.Remove(coordinate) == Current.Remove(coordinate), "remove result");
    internal void Unload(Vector2Int chunk)
        => Checks.Require(Legacy.UnregisterChunk(chunk) == Current.UnregisterChunk(chunk), "unload result");
    internal void Compare(CurrentResolver.ConveyorLine line, int slot, string name)
    {
        bool oldResult = LegacyResolver.Resolve(line, slot, out Block oldBlock);
        bool newResult = CurrentResolver.Resolve(line, slot, out Block newBlock);
        Checks.Require(oldResult == newResult && ReferenceEquals(oldBlock, newBlock), name + " return/out reference");
        Checks.Require(Legacy.Count == Current.Count && Legacy.ChunkCount == Current.ChunkCount
            && Legacy.RegisteredCellCount == Current.RegisteredCellCount
            && Legacy.RuntimeSimulationStateCount == Current.RuntimeSimulationStateCount, name + " store counts");
        if (line != null && slot >= 0 && slot < line.blockHandles.Count)
        {
            bool oldCell = Legacy.TryGetCell(line.blockHandles[slot], out BlockCellData a);
            bool newCell = Current.TryGetCell(line.blockHandles[slot], out BlockCellData b);
            Checks.Require(oldCell == newCell && a.Flags == b.Flags && a.Type == b.Type, name + " cell flags");
        }
    }
}

internal static class Checks
{
    private static int count;
    internal static void Require(bool condition, string name)
    {
        count++;
        if (!condition) throw new Exception(name);
    }
    private static void CompareTwice(StorePair pair, CurrentResolver.ConveyorLine line, string name)
    {
        pair.Compare(line, 0, name + " first");
        pair.Compare(line, 0, name + " repeated");
    }
    public static int Main()
    {
        var pair = new StorePair();
        var at = new Vector2Int(-1, -5);
        var first = new Block();
        BlockHandle handle = pair.Bind(at, first);
        var line = CurrentResolver.Line(handle);
        CompareTwice(pair, line, "cold/hot negative-coordinate");
        Require(line.runtimeBlocks.Length == 1, "production lazy array allocation");
        pair.Compare(null, 0, "null line");
        pair.Compare(line, -1, "negative slot");
        pair.Compare(line, 1, "slot outside line");
        pair.Compare(CurrentResolver.Line(), 0, "empty line");
        pair.Compare(CurrentResolver.Line(default(BlockHandle)), 0, "invalid handle");

        first.gameObject.activeSelf = false;
        CompareTwice(pair, line, "inactive self preserves non-null out");
        first.gameObject.activeSelf = true;
        CompareTwice(pair, line, "reactivated self");
        first.gameObject.Parent = new GameObject { Parent = new GameObject() };
        first.gameObject.Parent.Parent.activeSelf = false;
        CompareTwice(pair, line, "disabled grandparent");
        first.gameObject.Parent.Parent.activeSelf = true;
        CompareTwice(pair, line, "enabled grandparent");

        long before = HarnessMetrics.HandleLookups;
        pair.Bind(at, first);
        bool rebound = pair.CurrentResolver.Resolve(line, 0, out Block same);
        Require(rebound && ReferenceEquals(same, first) && HarnessMetrics.HandleLookups == before,
            "same proxy binding keeps hot cache");

        var replacement = new Block();
        Require(pair.Bind(at, replacement) == handle, "same cell replacement retains handle");
        CompareTwice(pair, line, "replaced proxy");
        pair.Remove(at);
        CompareTwice(pair, line, "removed proxy with retained registered handle");
        Require(pair.Bind(at, first) == handle, "rebound removed cell retains handle");
        CompareTwice(pair, line, "rebound removed cell");
        first.Destroyed = true;
        CompareTwice(pair, line, "destroyed fake-null cleans proxy count and preserves first out");
        Require(pair.Current.Count == 0, "destroyed proxy removed from storage");
        pair.Bind(at, replacement);
        CompareTwice(pair, line, "replacement after fake-null cleanup");

        pair.Unload(handle.ChunkCoordinate);
        CompareTwice(pair, line, "unloaded chunk stale handle");
        BlockHandle reloaded = pair.Bind(at, replacement);
        Require(reloaded != handle, "reload changes generation");
        CompareTwice(pair, line, "old generation cannot alias reload");
        line.blockHandles[0] = reloaded;
        CompareTwice(pair, line, "reloaded handle in existing slot");

        var otherAt = new Vector2Int(8, 9);
        var otherBlock = new Block();
        BlockHandle otherHandle = pair.Bind(otherAt, otherBlock);
        line.blockHandles[0] = otherHandle;
        CompareTwice(pair, line, "same slot index replaced handle");
        line.blockHandles.Add(reloaded);
        pair.Compare(line, 1, "expanded line allocates correct cache length");
        Require(line.runtimeBlocks.Length == 2, "resized array matches expanded line");
        line.blockHandles.RemoveAt(1);
        CompareTwice(pair, line, "shrunk line");
        Require(line.runtimeBlocks.Length == 1, "resized array matches shrunk line");
        line.runtimeBlocks = Array.Empty<BlockDataStore.RuntimeProxyCache>();
        CompareTwice(pair, line, "empty cache array");

        before = HarnessMetrics.HandleLookups;
        pair.Legacy.ConfigureChunkSize(4);
        pair.Current.ConfigureChunkSize(4);
        pair.CurrentResolver.Resolve(line, 0, out _);
        Require(HarnessMetrics.HandleLookups == before, "unchanged chunk size preserves cache");
        pair.Legacy.Clear(); pair.Current.Clear();
        CompareTwice(pair, line, "clear all storage");
        otherHandle = pair.Bind(otherAt, otherBlock);
        line.blockHandles[0] = otherHandle;
        CompareTwice(pair, line, "new proxy after clear");
        pair.Legacy.ConfigureChunkSize(8); pair.Current.ConfigureChunkSize(8);
        CompareTwice(pair, line, "reconfigure chunk size clears cached storage");
        line.blockHandles[0] = pair.Bind(otherAt, otherBlock);
        CompareTwice(pair, line, "proxy after reconfigure");

        pair.Legacy.RegisterCell(new Vector2Int(100, 100), Block.BlockType.Ground, out BlockHandle legacyMissing);
        pair.Current.RegisterCell(new Vector2Int(100, 100), Block.BlockType.Ground, out BlockHandle currentMissing);
        Require(legacyMissing == currentMissing, "registered cells have matching handles");
        CompareTwice(pair, CurrentResolver.Line(currentMissing), "registered cell without proxy");
        CompareTwice(pair, CurrentResolver.Line(new BlockHandle(new Vector2Int(999, 999), 0, 1)), "unregistered chunk");

        CheckOwnerIsolation();
        CheckMissingBatch();
        CheckMixedLifecycle();
        CheckStableLookups();
        Console.WriteLine($"PASS: {count} line-cache lifecycle/result checks.");
        return 0;
    }

    private static void CheckOwnerIsolation()
    {
        var a = new StorePair();
        var b = new StorePair();
        var position = new Vector2Int(0, 0);
        BlockHandle aHandle = a.Bind(position, new Block());
        BlockHandle bHandle = b.Bind(position, new Block());
        Require(aHandle == bHandle, "distinct stores may issue equal handles");
        var aLine = CurrentResolver.Line(aHandle);
        var bLine = CurrentResolver.Line(bHandle);
        a.Compare(aLine, 0, "owner A cache prepared");
        bLine.runtimeBlocks = aLine.runtimeBlocks;
        CompareTwice(b, bLine, "cache imported from other owner");
        CompareTwice(a, aLine, "shared cache written by other owner");
        a.CurrentResolver.Resolve(aLine, 0, out _);
        long before = HarnessMetrics.HandleLookups;
        b.Bind(new Vector2Int(2, 2), new Block());
        a.CurrentResolver.Resolve(aLine, 0, out _);
        Require(HarnessMetrics.HandleLookups == before, "other store mutation does not invalidate owner");
    }

    private static void CheckMissingBatch()
    {
        var pair = new StorePair();
        var handles = new BlockHandle[100];
        for (int i = 0; i < handles.Length; i++)
        {
            var at = new Vector2Int(i, 0);
            pair.Legacy.RegisterCell(at, Block.BlockType.Ground, out _);
            pair.Current.RegisterCell(at, Block.BlockType.Ground, out handles[i]);
        }
        var line = CurrentResolver.Line(handles);
        for (int repeat = 0; repeat < 2; repeat++)
            for (int i = 0; i < handles.Length; i++) pair.Compare(line, i, "all missing batch");
    }

    private static void CheckStableLookups()
    {
        var pair = new StorePair();
        var handles = new BlockHandle[100];
        for (int i = 0; i < handles.Length; i++) handles[i] = pair.Bind(new Vector2Int(i, 0), new Block());
        var line = CurrentResolver.Line(handles);
        for (int i = 0; i < handles.Length; i++) pair.CurrentResolver.Resolve(line, i, out _);
        long initial = HarnessMetrics.HandleLookups;
        for (int pass = 0; pass < 100; pass++)
            for (int slot = 0; slot < handles.Length; slot++) pair.LegacyResolver.Resolve(line, slot, out _);
        long legacyCalls = HarnessMetrics.HandleLookups - initial;
        initial = HarnessMetrics.HandleLookups;
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        int resolved = 0;
        for (int pass = 0; pass < 100; pass++)
            for (int slot = 0; slot < handles.Length; slot++)
                if (pair.CurrentResolver.Resolve(line, slot, out Block block) && !ReferenceEquals(block, null)) resolved++;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        long cacheCalls = HarnessMetrics.HandleLookups - initial;
        Require(resolved == 10000, "stable sweep resolves all proxies");
        Require(legacyCalls == 10000 && cacheCalls == 0, "stable sweep eliminates dictionary resolver calls");
        Require(allocated == 0, "stable sweep allocates no managed heap bytes");
        Console.WriteLine($"Stable 100 slots x 100 passes: uncached handle lookups {legacyCalls} -> {cacheCalls}; warm allocations {allocated} bytes.");
    }

    private static void CheckMixedLifecycle()
    {
        var pair = new StorePair();
        var positions = new[] { new Vector2Int(-3, 2), new Vector2Int(0, 1), new Vector2Int(7, -8), new Vector2Int(8, 8) };
        var blocks = new Block[positions.Length];
        var handles = new BlockHandle[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            blocks[i] = new Block();
            handles[i] = pair.Bind(positions[i], blocks[i]);
        }
        var line = CurrentResolver.Line(handles);
        var random = new Random(60907);
        for (int step = 0; step < 400; step++)
        {
            int index = random.Next(positions.Length);
            switch (random.Next(7))
            {
                case 0: pair.Remove(positions[index]); break;
                case 1:
                    blocks[index] = new Block();
                    line.blockHandles[index] = pair.Bind(positions[index], blocks[index]);
                    break;
                case 2: blocks[index].gameObject.activeSelf = !blocks[index].gameObject.activeSelf; break;
                case 3: blocks[index].Destroyed = true; break;
                case 4: pair.Unload(line.blockHandles[index].ChunkCoordinate); break;
                case 5:
                    pair.Legacy.Clear();
                    pair.Current.Clear();
                    break;
                case 6:
                    blocks[index].gameObject.Parent = new GameObject { activeSelf = random.Next(2) == 0 };
                    break;
            }
            for (int slot = 0; slot < positions.Length; slot++) pair.Compare(line, slot, "mixed lifecycle");
        }
    }
}
