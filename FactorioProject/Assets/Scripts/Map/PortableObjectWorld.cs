using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public readonly struct PortableObjectHandle : IEquatable<PortableObjectHandle>
{
    public PortableObjectHandle(int index, uint generation)
    {
        Index = index;
        Generation = generation;
    }

    public int Index { get; }
    public uint Generation { get; }
    public bool IsValid => Index >= 0 && Generation != 0;

    public bool Equals(PortableObjectHandle other) =>
        Index == other.Index && Generation == other.Generation;

    public override bool Equals(object value) => value is PortableObjectHandle other && Equals(other);
    public override int GetHashCode() => unchecked((Index * 397) ^ (int)Generation);
    public static bool operator ==(PortableObjectHandle left, PortableObjectHandle right) => left.Equals(right);
    public static bool operator !=(PortableObjectHandle left, PortableObjectHandle right) => !left.Equals(right);
}

/// <summary>
/// Dense runtime component for every portable item. It contains no Unity object reference,
/// so save/load preparation and future jobs can copy it without touching GameObjects.
/// </summary>
public struct PortableObjectComponent
{
    public int ItemId;
    public Vector3 WorldPosition;
    public Quaternion WorldRotation;
    public Vector3 WorldScale;
    public int Layer;
    public int LastConveyorMoveFrame;
    public Color32 BeltDebugColor;
    public bool Active;
    public bool Moving;
    public bool OnConveyor;
    public bool BatchedRendering;
    public bool SuppressRendering;
    public bool Sleeping;
    public bool BeltDebugActive;
}

/// <summary>
/// Generation-checked ECS store for portable-item components. Managed PortableObject values
/// are handles into this store, not MonoBehaviours and not scene-owned objects.
/// </summary>
public sealed class PortableObjectWorld : IDisposable
{
    private static PortableObjectWorld current;

    private readonly List<PortableObjectComponent> components = new List<PortableObjectComponent>(1024);
    private readonly List<uint> generations = new List<uint>(1024);
    private readonly List<bool> alive = new List<bool>(1024);
    private readonly Stack<int> freeIndices = new Stack<int>();
    private int aliveCount;
    private uint version;

    public static PortableObjectWorld Current => current;
    public static PortableObjectWorld Ensure() => current ??= new PortableObjectWorld();
    public int Count => aliveCount;
    public int Capacity => components.Count;
    public uint Version => version;

    public PortableObjectHandle Create(Vector3 position, Quaternion rotation, Vector3 scale, int layer)
    {
        int index;
        uint generation;
        if (freeIndices.Count > 0)
        {
            index = freeIndices.Pop();
            generation = NextGeneration(generations[index]);
            generations[index] = generation;
            components[index] = CreateDefaultComponent(position, rotation, scale, layer);
            alive[index] = true;
        }
        else
        {
            index = components.Count;
            generation = 1;
            components.Add(CreateDefaultComponent(position, rotation, scale, layer));
            generations.Add(generation);
            alive.Add(true);
        }

        aliveCount++;
        version++;
        return new PortableObjectHandle(index, generation);
    }

    public bool IsAlive(PortableObjectHandle handle) =>
        handle.IsValid
        && handle.Index < generations.Count
        && alive[handle.Index]
        && generations[handle.Index] == handle.Generation;

    public bool TryGet(PortableObjectHandle handle, out PortableObjectComponent component)
    {
        if (!IsAlive(handle))
        {
            component = default;
            return false;
        }

        component = components[handle.Index];
        return true;
    }

    public bool Set(PortableObjectHandle handle, in PortableObjectComponent component)
    {
        if (!IsAlive(handle))
        {
            return false;
        }

        components[handle.Index] = component;
        version++;
        return true;
    }

    public bool Release(PortableObjectHandle handle)
    {
        if (!IsAlive(handle))
        {
            return false;
        }

        generations[handle.Index] = NextGeneration(handle.Generation);
        components[handle.Index] = default;
        alive[handle.Index] = false;
        freeIndices.Push(handle.Index);
        aliveCount--;
        version++;
        return true;
    }

    public void CopyAliveComponents(List<PortableObjectComponent> destination)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        for (int i = 0; i < components.Count; i++)
        {
            if (alive[i])
            {
                destination.Add(components[i]);
            }
        }
    }

    public void Dispose()
    {
        components.Clear();
        generations.Clear();
        alive.Clear();
        freeIndices.Clear();
        aliveCount = 0;
        version++;
        if (ReferenceEquals(current, this))
        {
            current = null;
        }
    }

    private static PortableObjectComponent CreateDefaultComponent(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        int layer)
    {
        return new PortableObjectComponent
        {
            ItemId = -1,
            WorldPosition = position,
            WorldRotation = rotation,
            WorldScale = scale == Vector3.zero ? Vector3.one : scale,
            Layer = layer,
            LastConveyorMoveFrame = -1,
            BeltDebugColor = Color.white,
            Active = true
        };
    }

    private static uint NextGeneration(uint generation)
    {
        generation++;
        return generation == 0 ? 1u : generation;
    }
}
