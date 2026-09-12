using ProjectF.Conveyors;
using UnityEngine;

// These stand-ins cover scene lookup, managed slot reservations and presentation only.
// The native bake/command ordering/publication/checkpoint implementation is production code.
public class Block
{
    public const int ConveyorCellItemUnit = 4;
    public readonly Vector2Int Coordinate;
    public bool IsRuntimeConveyor = true;
    public float RuntimeConveyorSpeed = 1;
    public readonly BeltLaneState[] Items = Enumerable.Repeat(BeltLaneState.Empty, 4).ToArray();
    public readonly List<(Block block, int lane)>[] Edges = Enumerable.Range(0, 4).Select(_ => new List<(Block, int)>()).ToArray();
    public readonly bool[] Valid = { true, false, false, false };
    public long Travel = BeltSimulationJob.TickUnits * 3;
    public Action OnPublished;
    public Spliterbelt Splitter;
    private readonly int[] indices = { -1, -1, -1, -1 };
    public Block(int x, int y = 0) { Coordinate = new(x, y); }
    public int BeltJobIndex(int lane) => indices[lane];
    public void BindBeltJobLane(TerrainGenerator owner, int lane, int index) => indices[lane] = index;
    public void UnbindBeltJobs() => Array.Fill(indices, -1);
    public bool HasBeltJobStoredItem(int lane) => Items[lane].ItemId >= 0;
    public void PrepareBeltJobStorage() { }
    public Spliterbelt GetBeltJobSplitter(int lane) => lane == 2 ? Splitter : null;
    public void AppendBeltJobConnections(int lane, List<(Block block, int lane)> targets)
        => targets.AddRange(Edges[lane].Where(e => e.block.IsRuntimeConveyor));
    public long GetBeltJobDuration(int lane, Block target, int targetLane) => RuntimeConveyorSpeed > 0 ? Travel : 0;
    public BeltLaneState CaptureBeltJobInput(int lane, BeltLaneState previous, bool replace, long hold)
    {
        BeltLaneState state = replace ? Items[lane] : previous;
        if (hold > state.Remaining) state.Remaining = state.Duration = hold;
        return state;
    }
    public void PublishBeltJobLane(int lane, BeltLaneState state) => Items[lane] = state;
    public void NotifyBeltJobPublished() => OnPublished?.Invoke();
    public Vector3 TransportLanePosition(int lane) => new(Coordinate.x, lane, Coordinate.y);
    public Vector3 EvaluateBeltJobSegment(int lane, Block target, int targetLane, float progress)
        => Vector3.Lerp(TransportLanePosition(lane), target.TransportLanePosition(targetLane), progress);
}

public class ConveyorRuntimeRecord
{
    public Block Left, Right;
    public bool IsItemFilterMaskInitialized;
    public BeltSplitterState State;
    public List<ulong> Mask = new();
    public IReadOnlyList<Vector2Int> OccupiedCoordinates => new[] { Left.Coordinate, Right.Coordinate };
    public bool HasSplitterItemFilter => IsItemFilterMaskInitialized;
    public IReadOnlyList<ulong> SplitterItemFilterWords => Mask;
    public BeltSplitterState CaptureBeltJobRouting() => State;
    public void ApplyBeltJobRouting(BeltSplitterState state) => State = state;
    public List<ulong> CaptureItemFilterMaskWords() => Mask;
    public bool TryGetSplitterChannel(Vector2Int cell, out int channel)
    {
        channel = GetChannel(cell);
        return channel >= 0;
    }
    public bool TryGetChannelCoordinate(int channel, out Vector2Int cell)
    { cell = (channel == 0 ? Left : Right).Coordinate; return true; }
    public int GetChannel(Vector2Int cell) => cell == Left.Coordinate ? 0 : cell == Right.Coordinate ? 1 : -1;
}

public sealed class Spliterbelt : ConveyorRuntimeRecord { }

public partial class TerrainGenerator : IDisposable
{
    private double harnessAccumulator;
    private bool worldReadyForPresentation = true;
    private bool IsConveyorRuntimeRefreshDeferred => false;
    private readonly Dictionary<Vector2Int, Block> loadedBlocks = new();
    private readonly BeltSplitGraph beltSplitGraph = new();
    private readonly List<Block> beltSplitBlocks = new();
    private readonly List<(Block block, int lane)> beltSplitLanes = new(), beltSplitConnections = new();
    private readonly Dictionary<(Block block, int lane), int> beltSplitIndices = new();
    public void Add(Block block) { loadedBlocks.Add(block.Coordinate, block); Dirty(); }
    public void Remove(Block block) { loadedBlocks.Remove(block.Coordinate); block.IsRuntimeConveyor = false; Dirty(); }
    public void Dirty() => beltJobsDirty = true;
    public void Frame(float seconds)
    {
        Time.frameCount++;
        EnsureBeltJobs();
        harnessAccumulator += Math.Max(0, seconds);
        const double interval = 1d / BeltSimulationJob.TickRate;
        while (harnessAccumulator + 0.000000001d >= interval)
        {
            harnessAccumulator -= interval;
            TickManagedBeltSimulation();
        }

        MapObjectTickManager.SimulationBacklogTicks = harnessAccumulator * BeltSimulationJob.TickRate;
    }
    public void Put(Block block, int lane, int id, long hold = 0)
    {
        block.Items[lane] = id < 0 ? BeltLaneState.Empty : new BeltLaneState { ItemId = id, Origin = -1, GateBits = 8 };
        QueueBeltJobWrite(block, lane, true, hold);
    }
    public BeltLaneState Read(Block block, int lane = 0) => beltJobBuffers.Lanes[beltJobIndices[(block, lane)]];
    public string Committed()
    {
        var result = new System.Text.StringBuilder();
        result.Append(beltSimulationTick).Append('|');
        for (int i = 0; i < beltJobNodes.Count; i++)
        {
            var node = beltJobNodes[i]; var state = beltJobBuffers.Lanes[i];
            result.Append($"{node.block.Coordinate.x},{node.lane}:{state.ItemId}:{state.Remaining}:{state.Duration}:{state.Origin}:{beltJobBuffers.MergeCursor[i]}|");
        }
        return result.ToString();
    }
    public void Dispose() => ClearBeltJobs();
    private bool TryGetLoadedBlock(Vector2Int cell, out Block block) => loadedBlocks.TryGetValue(cell, out block);
    private static int CompareBeltSplitBlocks(Block a, Block b)
    {
        int x = a.Coordinate.x.CompareTo(b.Coordinate.x);
        return x != 0 ? x : a.Coordinate.y.CompareTo(b.Coordinate.y);
    }
    private void EnsureBeltSplitGroups()
    {
        beltSplitBlocks.Clear(); beltSplitLanes.Clear(); beltSplitIndices.Clear();
        beltSplitBlocks.AddRange(loadedBlocks.Values); beltSplitBlocks.Sort(CompareBeltSplitBlocks);
        foreach (Block block in beltSplitBlocks)
            for (int lane = 0; lane < 4; lane++) if (block.Valid[lane])
            { beltSplitIndices.Add((block, lane), beltSplitLanes.Count); beltSplitLanes.Add((block, lane)); }
        beltSplitGraph.Reset(beltSplitLanes.Count);
        for (int i = 0; i < beltSplitLanes.Count; i++)
        {
            var node = beltSplitLanes[i];
            foreach (var next in node.block.Edges[node.lane])
                if (beltSplitIndices.TryGetValue(next, out int target)) beltSplitGraph.Connect(i, target);
        }
    }
}
public static class MapObjectTickProfiler
{
    public static bool IsEnabled => false;
    public static long BeginSample() => 0;
    public static void EndNamedSample(string kind, string typeName, string itemName, long startTimestamp) { }
    public static void AddRuntimeCounter(string category, string name, object value) { }
}
public static class MapObjectTickManager
{
    public static double SimulationBacklogTicks;
    public static float SimulationInterpolationAlpha =>
        (float)Math.Min(1d, SimulationBacklogTicks);
}
