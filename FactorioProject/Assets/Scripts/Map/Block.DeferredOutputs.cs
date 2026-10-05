using System;
using ProjectF.Simulation;
using UnityEngine;

public partial class Block
{
    private OutputStackBatch deferredFloorOutput, deferredCenterOutput;
    private Vector3 deferredFloorStart, deferredCenterStart;
    private Bounds deferredOutputBounds;
    private bool materializingDeferredOutput;
    private PortableItemRenderer deferredOutputRenderer;
    internal int DeferredOutputCount => deferredFloorOutput.Count + deferredCenterOutput.Count;
    internal Bounds DeferredOutputBounds => deferredOutputBounds;

    // Unsupported individual visuals retain the existing path. Capacity/filters remain authoritative.
    internal bool TryAddDeferredOutput(int itemId, Vector3 start, float delay, bool center, out bool handled, ItemDefinition definition = null)
    {
        using var sample = ProjectF.Diagnostics.DeferredOutputTiming.Measure();
        handled = false;
        if (definition == null || definition.id != itemId) definition = InputOutputModule.ResolveItemDefinition(itemId);
        if (definition == null || definition.isFluid || definition.mapObject is Bucket
            || definition.lightMode != ItemDefinition.ItemLightMode.None
            || definition.portableMesh == null || definition.portableMat == null
            || floorObjectPrefab == null
            || !center && IsFarmlandFertilizerItem(itemId)) return false;
        handled = true;
        if (IsRuntimeConveyor || !IsRuntimeActive) return false;
        EnsureFloorObjectsInitialized(false);
        if (center)
        {
            if (!CanAddInputAreaCenterObjects(1, itemId)) return false;
        }
        else if (BlocksFloorObjectStacking(itemId) || !CanAddDeferredFloorOutput(itemId)) return false;
        ref OutputStackBatch batch = ref (center ? ref deferredCenterOutput : ref deferredFloorOutput);
        ref Vector3 origin = ref (center ? ref deferredCenterStart : ref deferredFloorStart);
        float now = Time.time;
        if (batch.MovingCount > 0 && now < batch.LastArrivalTime && origin != start)
        {
            MaterializeDeferredOutputs(int.MaxValue);
            if (batch.MovingCount > 0 && now < batch.LastArrivalTime) return false;
        }
        if (!batch.TryAppend(itemId, now, delay, PortableObject.MoveToDuration))
        {
            MaterializeDeferredOutputs(int.MaxValue);
            if (!batch.TryAppend(itemId, now, delay, PortableObject.MoveToDuration)) return false;
        }
        origin = start;
        TerrainGenerator terrain = cachedTerrainGenerator != null ? cachedTerrainGenerator : TerrainGenerator.Active;
        deferredOutputRenderer = PortableItemRenderer.EnsureFor(terrain.gameObject);
        int index = center ? inputAreaCenterStack.Count + batch.Count - 1 : floorStacks[0].Count + batch.Count - 1;
        var bounds = new Bounds(start, new Vector3(2, 4, 2));
        bounds.Encapsulate(new Bounds(GetItemStackPlacementPosition(center, index), new Vector3(2, 4, 2)));
        if (DeferredOutputCount == 1) deferredOutputBounds = bounds;
        else deferredOutputBounds.Encapsulate(bounds);
        deferredOutputRenderer.DeferOutputBlock(this, bounds);
        NotifyRuntimeItemStackChanged();
        return true;
    }

    private bool CanAddDeferredFloorOutput(int itemId)
    {
        var stack = floorStacks.Count > 0 ? floorStacks[0] : null;
        return stack != null && IsStackCompatible(stack, itemId)
            && (deferredFloorOutput.Count == 0 || deferredFloorOutput.ItemId == itemId)
            && stack.Count + deferredFloorOutput.Count < ResolveFloorStackCapacity(itemId);
    }

    internal int MaterializeDeferredOutputs(int budget)
    {
        if (materializingDeferredOutput || DeferredOutputCount == 0 || budget <= 0) return 0;
        materializingDeferredOutput = true;
        int created = 0;
        try
        {
            EnsureFloorObjectsInitialized(false);
            if (!ResolveFloorObjectPool()) return 0;
            created += MaterializeDeferredStack(false, budget);
            created += MaterializeDeferredStack(true, budget - created);
        }
        finally
        {
            materializingDeferredOutput = false;
            deferredOutputRenderer?.RemoveDeferredOutputItems(created);
            if (DeferredOutputCount == 0) deferredOutputRenderer?.RemoveDeferredOutputBlock(this);
        }
        return created;
    }

    private int MaterializeDeferredStack(bool center, int budget)
    {
        ref OutputStackBatch batch = ref (center ? ref deferredCenterOutput : ref deferredFloorOutput);
        if (batch.Count == 0 || budget <= 0) return 0;
        if (center) EnsureInputAreaCenterAnchorInitialized();
        Transform anchor = center ? inputAreaCenterAnchor : ResolveFloorObjectDropAnchor();
        if (anchor == null) return 0;
        var stack = center ? inputAreaCenterStack : floorStacks[0];
        Vector3 start = center ? deferredCenterStart : deferredFloorStart;
        int created = 0;
        while (created < budget && batch.TryPeekBottom(Time.time, out float launch, out bool moving))
        {
            var item = floorObjectPool.Get(floorObjectPrefab);
            if (item == null || !TryInitializePooledPortableObject(item, batch.ItemId)) break;
            int index = stack.Count;
            stack.Add(item);
            item.SetCachedParent(anchor, true);
            item.SetWorldScale(Vector3.one);
            item.SetCachedActive(!center || inputAreaCenterObjectsVisible);
            var gate = item.GetOrAddPickupGate();
            gate.SetAutoPickupBlocked(center);
            if (moving)
            {
                item.SetWorldPose(start, Quaternion.identity);
                // Preserve the original clock, including any future launch delay.
                item.MoveToBlockStack(this, center, index, Mathf.Max(0, launch - Time.time), null,
                    null, true, batch.Duration, Mathf.Min(Time.time, launch));
                item.SampleScheduledMoveNow();
            }
            else
            {
                if (center) ApplyInputAreaCenterObjectVisibility(item, index);
                else { ConfigureFloorObjectTransform(item, anchor, index); item.SetBatchedRendering(true); }
                gate.MarkSettled();
            }
            batch.RemoveBottom(); created++;
        }
        return created;
    }

    private void ClearDeferredOutputs()
    {
        deferredOutputRenderer?.RemoveDeferredOutputItems(DeferredOutputCount);
        deferredFloorOutput = deferredCenterOutput = default;
        deferredOutputBounds = default;
        deferredOutputRenderer?.RemoveDeferredOutputBlock(this);
        deferredOutputRenderer = null;
    }
}

namespace ProjectF.Diagnostics
{
    // Aggregate timestamps once per presentation frame; avoid 100k profiler dictionary writes per burst.
    internal static class DeferredOutputTiming
    {
        private static long elapsed;
        internal static Scope Measure() => new Scope(MapObjectTickProfiler.IsDetailedEnabled);
        internal static void Flush()
        {
            if (elapsed > 0) MapObjectTickProfiler.RecordNamedElapsedTicks("ItemOutput", nameof(Block), "Output Data Commit", elapsed);
            elapsed = 0;
        }
        internal readonly struct Scope : IDisposable
        {
            private readonly bool enabled;
            private readonly long start;
            internal Scope(bool measure) { enabled = measure; start = measure ? MapObjectTickProfiler.BeginSample() : 0; }
            public void Dispose() { if (enabled) elapsed += Math.Max(0, MapObjectTickProfiler.BeginSample() - start); }
        }
    }
}
