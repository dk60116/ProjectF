using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1f, 1f, 1f);
    }

    public struct Quaternion { public static Quaternion identity => new(); }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
    public sealed class GameObject
    {
        public bool activeInHierarchy = true;
        public bool activeSelf;
        public void SetActive(bool active) { activeSelf = active; }
    }
    public sealed class Transform
    {
        public readonly GameObject gameObject = new();
        public Vector3 position, localPosition, localScale;
        public Quaternion localRotation;
        public Transform parent;
        public void SetParent(Transform value, bool worldPositionStays) { parent = value; }
    }
}

public interface IPersistentInstallationItemCollectionStorage
{
    void CapturePersistentStoredItemIds(List<int> destination);
    void ApplyPersistentStoredItemIds(IReadOnlyList<int> itemIds);
}

public sealed class DroppedItemPickupGate
{
    public bool Settled;
    public void MarkSettled() { Settled = true; }
}

public sealed class PortableObject
{
    public readonly UnityEngine.Transform transform = new();
    public UnityEngine.GameObject gameObject => transform.gameObject;
    public int ItemId;
    public bool MoveCancelled;
    public bool BatchedRendering;
    public readonly DroppedItemPickupGate Gate = new();
    public void CancelMove() { MoveCancelled = true; }
    public void SetBatchedRendering(bool enabled) { BatchedRendering = enabled; }
    public DroppedItemPickupGate GetOrAddPickupGate() => Gate;
}

public partial class FreightCar
{
    const int Capacity = 2;
    readonly List<List<PortableObject>> itemPointStacks = new();
    readonly List<List<PortableObject>> boxPointItemStacks = new();
    readonly List<UnityEngine.Transform> itemPoints = new();
    readonly List<UnityEngine.Transform> boxPoints = new();
    readonly List<bool> activeBoxPoints = new();
    readonly UnityEngine.Transform transform = new();
    float itemStackVerticalSpacing = .05f;
    public int RobotArmNotifications;

    public FreightCar(int itemPointCount, params bool[] activeBoxes)
    {
        for (int i = 0; i < itemPointCount; i++)
        {
            itemPointStacks.Add(new());
            itemPoints.Add(new());
        }
        foreach (bool active in activeBoxes)
        {
            boxPointItemStacks.Add(new());
            boxPoints.Add(new());
            activeBoxPoints.Add(active);
        }
    }

    public void SeedItemPoint(int point, params int[] itemIds)
    {
        foreach (int itemId in itemIds) itemPointStacks[point].Add(NewItem(itemId));
    }
    public void SeedBoxPoint(int point, params int[] itemIds)
    {
        foreach (int itemId in itemIds) boxPointItemStacks[point].Add(NewItem(itemId));
    }
    public IEnumerable<PortableObject> AllItems()
    {
        foreach (var stack in itemPointStacks) foreach (var item in stack) yield return item;
        foreach (var stack in boxPointItemStacks) foreach (var item in stack) yield return item;
    }

    void EnsureItemPointStacks() { }
    void EnsureBoxPointBoxes() { }
    bool IsBoxPointStorageActive(int index) => index >= 0 && index < activeBoxPoints.Count && activeBoxPoints[index];
    static void CleanupItemStack(List<PortableObject> stack) => stack?.RemoveAll(item => item == null);
    void ClearLoadedItems()
    {
        foreach (var stack in itemPointStacks) stack.Clear();
        foreach (var stack in boxPointItemStacks) stack.Clear();
    }
    void NotifyRobotArmsAtRuntimeCoordinates() { RobotArmNotifications++; }
    PortableObject CreateItemPortableObject(int itemId) => NewItem(itemId);
    static PortableObject NewItem(int itemId) => new() { ItemId = itemId };

    bool TryGetBestItemStorageStack(
        int itemId,
        UnityEngine.Vector3 referenceWorldPosition,
        out UnityEngine.Transform point,
        out List<PortableObject> stack)
    {
        point = null;
        stack = null;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < itemPointStacks.Count; i++)
                if (Select(itemPointStacks[i], itemId, pass)) { point = itemPoints[i]; stack = itemPointStacks[i]; return true; }
            for (int i = 0; i < boxPointItemStacks.Count; i++)
                if (IsBoxPointStorageActive(i) && Select(boxPointItemStacks[i], itemId, pass))
                { point = boxPoints[i]; stack = boxPointItemStacks[i]; return true; }
        }
        return false;
    }

    static bool Select(List<PortableObject> stack, int itemId, int pass)
    {
        bool hasItems = stack.Count > 0;
        return stack.Count < Capacity
               && (hasItems ? stack[0].ItemId == itemId : true)
               && (pass == 0 ? hasItems : !hasItems);
    }
}

static class Checks
{
    static int checks;
    static void Expect(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Main()
    {
        var source = new FreightCar(2, true, false);
        source.SeedItemPoint(0, 4, 4);
        source.SeedItemPoint(1, 5);
        source.SeedBoxPoint(0, 6, 6);
        source.SeedBoxPoint(1, 99);
        var saved = new List<int>();
        source.CapturePersistentStoredItemIds(saved);
        Expect(string.Join(',', saved) == "4,4,5,6,6", "Inactive box-point cargo must not enter the save payload.");

        var restored = new FreightCar(2, true);
        restored.ApplyPersistentStoredItemIds(new[] { 4, 4, -1, 5, 6, 6 });
        var roundTrip = new List<int>();
        restored.CapturePersistentStoredItemIds(roundTrip);
        Expect(string.Join(',', roundTrip) == "4,4,5,6,6", "Valid freight IDs must survive apply/capture round trip.");
        Expect(restored.RobotArmNotifications == 1, "Restore must wake nearby robot arms once.");
        foreach (PortableObject item in restored.AllItems())
        {
            Expect(item.MoveCancelled, "Restored freight must not retain a movement animation.");
            Expect(item.Gate.Settled, "Restored freight must be immediately pickable.");
            Expect(item.transform.parent != null, "Restored freight must be parented to a cargo point.");
        }

        restored.ApplyPersistentStoredItemIds(null);
        roundTrip.Clear();
        restored.CapturePersistentStoredItemIds(roundTrip);
        Expect(roundTrip.Count == 0, "Applying an empty payload must clear stale freight.");
        Console.WriteLine($"FreightCar persistence checks passed: {checks}");
    }
}
