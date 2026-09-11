using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Runtime;

public readonly struct ResourceHandle : IEquatable<ResourceHandle>
{
    internal readonly ResourceTypeWorld World;
    internal readonly int Index;
    internal readonly uint Generation;
    internal ResourceHandle(ResourceTypeWorld world, int index, uint generation)
    { World = world; Index = index; Generation = generation; }
    public bool IsValid => World != null && World.IsValid(this);
    public bool TryResolve(out ResourceInstance resource)
    {
        resource = IsValid ? World.GetInstance(this) : null;
        return resource != null;
    }
    public bool Equals(ResourceHandle other) => ReferenceEquals(World, other.World) && Index == other.Index && Generation == other.Generation;
    public override bool Equals(object obj) => obj is ResourceHandle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(World, Index, Generation);
}

// Frequently changing values are owned by one array per resource type, not a MonoBehaviour per cell.
internal struct ResourceRuntimeState
{
    internal Resource.ResourceStatus Status;
    internal Vector3 Position, GaugeFill;
    internal float AccumulatedWork, MinimumScale, MaximumScale, BodyScale;
    internal int ReservedGaugeCount, InitialResourceCount, YawStep, ScaleMaximumCount;
    internal bool HasYaw, DynamicScale, BodyVisible;
    internal float Growth, GrowthWater, GrowthFertilizer, GrowthElapsed;
}

public sealed partial class ResourceTypeWorld
{
    private readonly ResourceStateSlots<ResourceRuntimeState> stateSlots = new ResourceStateSlots<ResourceRuntimeState>();
    private ResourceInstance[] slots = new ResourceInstance[64];
    public IEnumerable<ResourceInstance> Instances => instances;

    internal bool IsValid(ResourceHandle handle) => ReferenceEquals(handle.World, this)
        && stateSlots.Contains(handle.Index, handle.Generation) && slots[handle.Index] != null;
    internal ResourceInstance GetInstance(ResourceHandle handle) => IsValid(handle) ? slots[handle.Index] : null;
    internal ref ResourceRuntimeState GetState(ResourceHandle handle)
    {
        // Construction initializes state before publishing the managed identity in slots.
        if (!ReferenceEquals(handle.World, this))
            throw new InvalidOperationException("Resource handle has expired.");
        return ref stateSlots.Get(handle.Index, handle.Generation);
    }
    private ResourceHandle Allocate(Resource prefab, Vector3 position)
    {
        Resource.ResourceSaveState defaults = prefab.CaptureState();
        ResourceStateSlots<ResourceRuntimeState>.Slot slot = stateSlots.Allocate(new ResourceRuntimeState { Status = prefab.InitialStatus, Position = position,
            DynamicScale = true, BodyVisible = true, BodyScale = 1f, MinimumScale = prefab.MinimumScale,
            MaximumScale = prefab.MaximumScale, ScaleMaximumCount = prefab.ScaleMaximumCount,
            Growth = defaults.growth, GrowthWater = defaults.growthWaterLiters,
            GrowthFertilizer = defaults.growthFertilizerAmount, GrowthElapsed = defaults.growthElapsedSeconds });
        if (slot.Index >= slots.Length) Array.Resize(ref slots, checked(slots.Length * 2));
        return new ResourceHandle(this, slot.Index, slot.Generation);
    }
    private void Free(ResourceHandle handle)
    {
        if (!IsValid(handle)) return;
        slots[handle.Index] = null;
        stateSlots.Release(handle.Index, handle.Generation);
    }
}
