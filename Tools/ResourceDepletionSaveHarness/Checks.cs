using System;
using System.Collections.Generic;

public readonly record struct Vector2Int(int x, int y);
public class Block { public Vector2Int Coordinate; }
public class BlockStateStore
{
    public int SaveCount;
    public Vector2Int SavedCoordinate;
    public int SavedResourceCount = -1;
    public void Save(Vector2Int coordinate, Resource resource)
    {
        SaveCount++;
        SavedCoordinate = coordinate;
        SavedResourceCount = resource.ResourceCount;
    }
}
public partial class TerrainGenerator
{
    public static TerrainGenerator Active;
    public BlockStateStore Store;
    public static TerrainGenerator ResolveActive() => Active;
    private void EnsureResourceStateStore() { }
    private BlockStateStore resourceStateStore => Store;
}
public partial class Resource
{
    private struct Status { public int resourceCount, currentGague; }
    private Status resourceStatus;
    private float accumulatedWork;
    private readonly Queue<int> reservedHarvestGaugeCosts = new();
    private int reservedHarvestGaugeCount;
    private Block owningBlock;
    public Block OwningBlock => owningBlock;
    public int ResourceCount => Math.Max(0, resourceStatus.resourceCount);
    public int CurrentGauge => Math.Max(0, resourceStatus.currentGague);
    public int MaxGauge { get; private set; } = 10;
    public bool CanHarvest => ResourceCount > 0;
    private void ClearReservedHarvestSteps() { reservedHarvestGaugeCosts.Clear(); reservedHarvestGaugeCount = 0; }
    private void UpdateBodyScale() { }
    public void Initialize(Block block, int count, int gauge)
    {
        owningBlock = block;
        resourceStatus.resourceCount = count;
        resourceStatus.currentGague = gauge;
        MaxGauge = gauge;
    }
    public int Harvest(int gauge, out bool depleted) => ConsumeGaugeDotsInternal(gauge, out depleted);
}
namespace UnityEngine
{
    public static class Mathf
    {
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
    }
}
public static class Checks
{
    private static int passed;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        passed++;
    }
    public static void Main()
    {
        var store = new BlockStateStore();
        TerrainGenerator.Active = new TerrainGenerator { Store = store };
        var block = new Block { Coordinate = new Vector2Int(17, -23) };
        var tree = new Resource();
        tree.Initialize(block, 1, 10);
        Require(tree.Harvest(5, out bool partiallyDepleted) == 0 && !partiallyDepleted,
            "partial work does not create a depletion tombstone");
        Require(store.SaveCount == 0, "partial tree state is left for normal chunk persistence");
        Require(tree.Harvest(5, out bool depleted) == 1 && depleted,
            "final work depletes exactly one resource");
        Require(store.SaveCount == 1 && store.SavedResourceCount == 0,
            "zero-count state is saved before resource deactivation");
        Require(store.SavedCoordinate == block.Coordinate,
            "depletion tombstone is stored at the harvested coordinate");

        var multi = new Resource();
        multi.Initialize(block, 2, 10);
        Require(multi.Harvest(10, out bool oneRemaining) == 1 && !oneRemaining,
            "non-final depletion keeps the resource alive");
        Require(store.SaveCount == 1, "non-final depletion does not need an immediate tombstone");
        Require(multi.Harvest(10, out bool allDepleted) == 1 && allDepleted,
            "last quantity creates final depletion");
        Require(store.SaveCount == 2 && store.SavedResourceCount == 0,
            "multi-count resources save one tombstone only at zero");

        TerrainGenerator.Active = null;
        var detached = new Resource();
        detached.Initialize(block, 1, 10);
        Require(detached.Harvest(10, out bool detachedDepleted) == 1 && detachedDepleted,
            "depletion remains safe while no terrain is active");
        Require(store.SaveCount == 2, "missing terrain does not touch stale storage");
        Console.WriteLine($"PASS: {passed} resource depletion persistence checks.");
    }
}
