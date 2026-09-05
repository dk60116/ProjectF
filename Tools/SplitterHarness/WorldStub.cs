using System;
using System.Collections.Generic;
using UnityEngine;

// Managed world boundary only: the linked routing and Block integration are production code.
public class ConveyorBelt
{
    private readonly HashSet<int> selected = new HashSet<int>();
    public bool IsItemFilterMaskInitialized { get; private set; }
    public virtual bool IsItemFilterEnabled(int itemId, int total) => selected.Contains(itemId);
    public virtual void SetItemFilterEnabled(int itemId, int total, bool enabled)
    {
        if (enabled) selected.Add(itemId); else selected.Remove(itemId);
        IsItemFilterMaskInitialized = true;
        OnItemFilterMaskChanged();
    }
    public void ApplyItemFilterMask(IReadOnlyList<ulong> words, bool initialized)
    {
        selected.Clear(); IsItemFilterMaskInitialized = initialized;
        for (int i = 0; words != null && i < words.Count * 64; i++)
            if ((words[i >> 6] & (1UL << (i & 63))) != 0) selected.Add(i);
    }
    protected virtual void OnItemFilterMaskChanged() { }
    public virtual void PrepareForPool() { selected.Clear(); IsItemFilterMaskInitialized = false; }
}

public partial class Spliterbelt : ConveyorBelt
{
    public static readonly Dictionary<Vector2Int, Spliterbelt> Coverage = new();
    public readonly Vector2Int[] Coordinates;
    public bool IsRuntimeRootAvailable = true;
    public float WheelAnimationTime;
    public void SetWheelTransitionDelay(float value) => wheelTransitionDelay = value;
    public Spliterbelt(Vector2Int left, Vector2Int right)
    {
        Coordinates = new[] { left, right };
        Coverage[left] = Coverage[right] = this;
    }
    public static bool TryFindCoveringBelt(Vector2Int c, out Spliterbelt belt) => Coverage.TryGetValue(c, out belt);
    public bool TryGetChannelCoordinate(int channel, out Vector2Int c) { c = Coordinates[channel]; return true; }
    public int GetChannel(Vector2Int c) => c == Coordinates[0] ? 0 : 1;
    public bool TryGetChannel(Vector2Int c, out int channel)
    {
        channel = c == Coordinates[0] ? 0 : c == Coordinates[1] ? 1 : -1;
        return channel >= 0;
    }
    public void RefreshCoveredBlocks() { }
}

public class TerrainGenerator
{
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public bool TryGetLoadedBlock(Vector2Int c, out Block b) => Blocks.TryGetValue(c, out b);
}

public partial class Block
{
    private object mapObject;
    private Vector2Int coordinate;
    private readonly TerrainGenerator world;
    public int Input = -1;
    public int Output = -1;
    public bool Ready = true;
    public bool MovedThisFrame;
    public bool OutputReserved;
    public int WakeCount, ClearCount, RegistrationCount;
    public Block(TerrainGenerator world, Vector2Int c, Spliterbelt mapped)
    {
        this.world = world; coordinate = c; mapObject = mapped; world.Blocks[c] = this;
    }
    private bool TryResolveOwningTerrainGenerator(out TerrainGenerator terrain) { terrain = world; return true; }
    private bool HasConveyorItemAtLane(int lane) => (lane == 2 ? Input : Output) >= 0;
    private bool WasConveyorItemMovedThisFrame(int lane) => MovedThisFrame;
    private bool IsConveyorItemReadyToMoveAtLane(int lane) => Ready;
    private int GetConveyorItemIdAtLane(int lane) => lane == 2 ? Input : Output;
    private static bool IsConveyorDestinationLaneOccupied(Block b, int lane) => b.Output >= 0 || b.OutputReserved;
    private void ClearConveyorPlanFailureCache(int lane) { ClearCount++; }
    private void WakeConveyorMoveAttempts(bool clear) { WakeCount++; }
    private void RefreshConveyorActivityRegistration() { RegistrationCount++; }
    public bool Probe(out Block destination)
    {
        destination = null;
        return TryGetRuntimeSplitter(out Spliterbelt belt) && TryGetSplitterSuccessor(belt, out destination, out _);
    }
    public bool Move()
    {
        if (!Probe(out Block destination)) return false;
        if (destination.Output >= 0) throw new Exception("Occupied output overwritten");
        destination.Output = Input; Input = -1;
        CommitSplitterTransfer(2, destination, 0);
        return true;
    }
}
