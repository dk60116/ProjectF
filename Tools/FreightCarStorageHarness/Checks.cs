using System;
using System.Collections.Generic;
using UnityEngine;

public struct Quaternion { public static Quaternion identity => default; }
public class GameObject { public bool activeInHierarchy = true; }
public class Transform
{
    public readonly GameObject gameObject = new();
    public Vector3 position, lossyScale = Vector3.one;
    public Quaternion rotation;
    public Vector3 TransformPoint(Vector3 local) => position + local;
}
public class PickupGate { public void MarkSettled() { } }
public class PortableObject
{
    public const float MoveToDuration = .2f;
    public int ItemId;
    public Transform Parent;
    public Vector3 Start;
    readonly PickupGate gate = new();
    public void SetBatchedRendering(bool value) { }
    public void SetCachedParent(Transform parent, bool keepWorld) => Parent = parent;
    public void SetWorldPose(Vector3 position, Quaternion rotation) => Start = position;
    public void SetWorldScale(Vector3 scale) { }
    public void SetCachedActive(bool active) { }
    public void SetLocalPose(Transform parent, Vector3 point, Quaternion rotation, Vector3 scale) => Parent = parent;
    public PickupGate GetOrAddPickupGate() => gate;
    public void MoveTo(Func<Vector3> destination, float delay, Func<Vector3> source, Action complete,
        bool a, bool b, float duration, bool c) => complete();
}

public partial class FreightCar
{
    public (int mapSizeX, int mapSizeY) Status = (1, 2);
    readonly List<Transform> itemPointList = new(), boxPointList = new();
    readonly List<List<PortableObject>> itemPointStacks = new(), boxPointItemStacks = new();
    readonly List<bool> activeBoxPoints = new();
    Predicate<int> boxPointStorageFilter;
    bool HasTank;
    float itemStackVerticalSpacing = .05f;
    bool TryGetAttachedFluidTank(out object tank) { tank = null; return HasTank; }
    void EnsureItemPointStacks() { }
    bool IsBoxPointStorageActive(int index) => activeBoxPoints[index];
    int GetStackCapacityForItem(int itemId) => 3;
    static void CleanupItemStack(List<PortableObject> stack)
    { if (stack != null) for (int i = stack.Count - 1; i >= 0; i--) if (stack[i] == null) stack.RemoveAt(i); }
    static bool IsStackCompatible(List<PortableObject> stack, int itemId) => stack.Count == 0 || stack[0].ItemId == itemId;
    PortableObject CreateItemPortableObject(int itemId) => new() { ItemId = itemId };
    void MarkPersistenceStateDirty() { }
    void NotifyRobotArmsAtRuntimeCoordinates() { }

    void AddPoint(Vector3 position, bool box = false, bool active = true)
    {
        var point = new Transform { position = position };
        if (box) { boxPointList.Add(point); boxPointItemStacks.Add(new()); activeBoxPoints.Add(active); }
        else { itemPointList.Add(point); itemPointStacks.Add(new()); }
    }

    static int checks;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    bool Chooses(int itemId, Vector3 source, Transform expected)
        => TryGetBestItemStorageStack(itemId, source, out var point, out _) && point == expected;

    public static void Main()
    {
        foreach (Vector3 axis in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
        foreach (bool reverseOrder in new[] { false, true })
        foreach (bool nearBox in new[] { false, true })
        {
            var car = new FreightCar();
            Vector3 near = axis * .1f, far = axis * 1.1f;
            if (reverseOrder) { car.AddPoint(far); car.AddPoint(near, nearBox); }
            else { car.AddPoint(near, nearBox); car.AddPoint(far); }
            Transform nearPoint = nearBox ? car.boxPointList[0] : car.itemPointList[reverseOrder ? 1 : 0];
            Transform farPoint = car.itemPointList[reverseOrder || nearBox ? 0 : 1];
            var nearStack = nearBox ? car.boxPointItemStacks[0] : car.itemPointStacks[reverseOrder ? 1 : 0];
            var farStack = car.itemPointStacks[reverseOrder || nearBox ? 0 : 1];
            farStack.Add(new PortableObject { ItemId = 4 });
            Check(car.Chooses(4, Vector3.zero, nearPoint), "Near empty cell must beat a distant matching stack");
            Check(car.Chooses(4, Vector3.up * 50, nearPoint), "Source height cannot bias floor-cell proximity");
            car.Status = (1, 1);
            Check(car.Chooses(4, Vector3.zero, farPoint), "One-cell car must preserve existing stack consolidation");
            car.Status = (1, 2);
            nearStack.Add(new PortableObject { ItemId = 5 });
            Check(car.Chooses(4, Vector3.zero, farPoint), "A nearby incompatible stack must be skipped");
            nearStack.Clear();
            nearStack.AddRange(new[] { new PortableObject { ItemId = 4 }, new PortableObject { ItemId = 4 }, new PortableObject { ItemId = 4 } });
            Check(car.Chooses(4, Vector3.zero, farPoint), "A full near stack must spill into the next usable cell");
            nearStack.Clear();
            if (nearBox) car.activeBoxPoints[0] = false;
            else nearPoint.gameObject.activeInHierarchy = false;
            Check(car.Chooses(4, Vector3.zero, farPoint), "Inactive or unavailable box storage must not accept items");
            if (nearBox) car.activeBoxPoints[0] = true;
            else nearPoint.gameObject.activeInHierarchy = true;

            Check(car.TryAddItem(4, far * 10, () => near, 0, out var added) && added.Parent == nearPoint && added.Start == near,
                "Actual insertion must choose from the current provider position, not the stale transfer origin");
            Check(car.TryAddItem(4, near, null, 0, out added) && added.Parent == nearPoint,
                "Hand/robot transfers without a provider must use their supplied origin");
            Check(car.TryAddItem(4, near, null, 0, out added) && added.Parent == nearPoint,
                "Nearest stack must fill to its capacity before spilling");
            Check(car.TryAddItem(4, near, null, 0, out added) && added.Parent == farPoint,
                "Actual insertion must spill after the near stack becomes full");
            car.HasTank = true;
            Check(!car.TryGetBestItemStorageStack(4, near, out _, out _), "A mounted fluid tank must still reject solid cargo");
            car.HasTank = false;
            farStack.Add(new PortableObject { ItemId = 4 });
            Check(!car.TryGetBestItemStorageStack(4, near, out _, out _), "All full points must reject insertion");
        }

        var warm = new FreightCar();
        warm.AddPoint(Vector3.zero);
        warm.AddPoint(Vector3.forward, true);
        for (int i = 0; i < 100; i++) warm.TryGetBestItemStorageStack(4, Vector3.zero, out _, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) warm.TryGetBestItemStorageStack(4, Vector3.zero, out _, out _);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Warm selection must allocate zero managed bytes");
        Console.WriteLine($"Freight storage selection passed: {checks} checks; warm selection 0 bytes allocated");
    }
}
