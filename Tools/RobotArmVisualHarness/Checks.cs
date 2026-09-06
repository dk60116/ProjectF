using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class Mesh
{
    private static int nextId;
    private readonly int id = ++nextId;
    public Bounds bounds = new(Vector3.zero, Vector3.one);
    public int GetInstanceID() => id;
}
public class Material
{
    private static int nextId;
    private readonly int id = ++nextId;
    public bool enableInstancing = true;
    public Color SleepingColor = Color.gray;
    public int GetInstanceID() => id;
}
public static class SleepAwakeDebugVisual { public static Color GetSleepingColor(Material material) => material.SleepingColor; }
public class Transform
{
    public Matrix4x4 Matrix = Matrix4x4.identity;
    public int Reads;
    public Vector3 position;
    public Matrix4x4 localToWorldMatrix { get { Reads++; return Matrix; } }
}
public class GameObject { public bool activeSelf = true; }
public readonly struct Marker { public Scope Auto() => default; public readonly struct Scope : IDisposable { public void Dispose() { } } }
public sealed class VirtualRenderBatchCollection
{
    public readonly List<(VirtualRenderBatchKey Key, Matrix4x4 Matrix, Bounds Bounds)> Entries = new();
    public int ClearCalls, AddCalls, RenderCalls;
    public bool CameraVisible = true;
    public int VisibleEntries;
    public int ActiveBatchCount => Entries.Count;
    public void ClearActiveMatrices() { ClearCalls++; Entries.Clear(); }
    public void AddMatrix(VirtualRenderBatchKey key, Matrix4x4 matrix) { AddCalls++; Entries.Add((key, matrix, key.Mesh.bounds)); }
    public void RenderBatches(object camera) { RenderCalls++; VisibleEntries = CameraVisible ? Entries.Count : 0; }
}
public partial class PortableObject
{
    public static int RenderDataReads;
    private bool useBatchedRendering = true;
    private PortableItemRenderer portableItemRenderer;
    public GameObject gameObject = new();
    public int Id = 1, Layer;
    public bool Renderable = true, ReceiveShadows = true, SleepTint, DebugColor;
    public Mesh Mesh = new();
    public Material Material = new();
    public Matrix4x4 Matrix = Matrix4x4.identity;
    public Vector3 Position;
    public ShadowCastingMode Shadow = ShadowCastingMode.On;
    public Color32 Color = new(255, 255, 255, 255);
    public void Configure(PortableItemRenderer renderer) => portableItemRenderer = renderer;
    public bool TryGetBatchRenderData(out int itemId, out Mesh mesh, out Material material,
        out Matrix4x4 matrix, out Vector3 position, out int layer, out ShadowCastingMode shadow,
        out bool receive, out bool sleep, out bool debug, out Color32 color)
    {
        RenderDataReads++;
        itemId = Id; mesh = Mesh; material = Material; matrix = Matrix; position = Position;
        layer = Layer; shadow = Shadow; receive = ReceiveShadows; sleep = SleepTint; debug = DebugColor; color = Color;
        return useBatchedRendering && Renderable && Id >= 0 && Mesh != null && Material != null;
    }
}
public partial class PortableItemRenderer
{
    private readonly HashSet<PortableObject> registeredPortableObjects = new();
    private readonly VirtualRenderBatchCollection portableObjectBatches = new();
    private readonly List<PortableObject> portableObjectCleanupBuffer = new();
    private readonly List<PortableObjectRenderSnapshot> portableObjectRenderSnapshots = new();
    private bool portableObjectBatchesDirty = true, portableObjectRenderRefreshRequested;
    private float portableObjectBatchCellSize = 8f;
    private object mainCamera;
    private static readonly Marker RebuildPortableObjectBatchesMarker = default, RenderPortableObjectBatchesMarker = default;
    private void ResolveDependencies() { }
    private bool HasVirtualConveyorRenderWork() => false;
    private void RenderVirtualConveyorItems() { }
    public VirtualRenderBatchCollection Batches => portableObjectBatches;
    public void Frame() => LateUpdate();
    public void SetCellSize(float value) => portableObjectBatchCellSize = value;
    public void InjectDestroyed() => registeredPortableObjects.Add(null);
}
public partial class LegacyPortableItemRenderer
{
    public readonly HashSet<PortableObject> registeredPortableObjects = new();
    public readonly VirtualRenderBatchCollection portableObjectBatches = new();
    private readonly List<PortableObject> portableObjectCleanupBuffer = new();
    public float portableObjectBatchCellSize = 8f;
    public void Frame() => RebuildPortableObjectBatches();
}
public partial class RobotArm
{
    private PortableObject handItem;
    private int heldItemId = 1;
    private Predicate<int> cachedPickupItemFilter;
    public HashSet<int> AllowedItems = new();
    private int ResolveFilterBitCount(int id) => 400;
    private bool IsItemFilterEnabled(int id, int bitCount) => AllowedItems.Contains(id);
    public Predicate<int> Filter => PickupItemFilter;
    private bool instancedRenderingActive = true;
    public bool isActiveAndEnabled = true;
    public Transform transform = new();
    public RobotArmInstancedRenderPart[] instancedRenderParts;
    private bool ShouldUseSleepAwakeDarkTint() => false;
    private void EnsureInstancedRenderParts() { }
    private void RefreshHandItemVisual() { handItem.gameObject.activeSelf = true; handItem.RequestBatchedRenderDataRefresh(); }
    public RobotArm(PortableObject item) => handItem = item;
    public void Tick() => RefreshHeldItemVisualIfNeeded();
    public class RobotArmInstancedRenderPart
    {
        public bool IsValid = true;
        public Mesh Mesh = new();
        public Material[] SharedMaterials;
        public int MaterialCount, Layer;
        public ShadowCastingMode ShadowCastingMode = ShadowCastingMode.On;
        public bool ReceiveShadows = true;
        public Transform Transform = new();
    }
}
public static class Checks
{
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static void Equal(VirtualRenderBatchCollection optimized, VirtualRenderBatchCollection reference, string message)
    {
        Require(optimized.Entries.Count == reference.Entries.Count, message + ": count");
        for (int i = 0; i < reference.Entries.Count; i++)
        {
            var a = optimized.Entries[i]; var b = reference.Entries[i];
            if (!a.Key.Equals(b.Key) || !a.Matrix.Equals(b.Matrix) || !a.Bounds.Equals(b.Bounds))
                throw new Exception(message + ": entry " + i);
        }
        checks++;
    }
    public static void Main()
    {
        var optimized = new PortableItemRenderer(); var reference = new LegacyPortableItemRenderer();
        var hand = new PortableObject(); hand.Configure(optimized);
        var arm = new RobotArm(hand);
        var unrelated = new PortableObject { Id = 9 }; unrelated.Configure(optimized);
        void Add(PortableObject value) { optimized.Register(value); reference.registeredPortableObjects.Add(value); }
        void Remove(PortableObject value) { optimized.Unregister(value); reference.registeredPortableObjects.Remove(value); }
        void Frame(string label, Action afterTick = null)
        {
            arm.Tick(); afterTick?.Invoke();
            int readsBefore = PortableObject.RenderDataReads;
            optimized.Frame();
            Require(PortableObject.RenderDataReads - readsBefore
                == reference.registeredPortableObjects.Count - (reference.registeredPortableObjects.Contains(null) ? 1 : 0),
                label + ": read each object once, including changed frames");
            reference.Frame();
            Equal(optimized.Batches, reference.portableObjectBatches, label);
        }
        Add(hand); Add(unrelated);
        for (int i = 0; i < 128; i++) Add(new PortableObject { Id = i + 30 });
        Frame("initial registration");
        int initialBuilds = optimized.Batches.ClearCalls;
        for (int i = 0; i < 120; i++) Frame("stationary frame " + i);
        Require(optimized.Batches.ClearCalls == initialBuilds, "stationary frames must not clear/rebuild batches");
        Require(optimized.Batches.RenderCalls == 121, "every stationary frame must still submit rendering");
        long stationaryAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++) { arm.Tick(); optimized.Frame(); }
        long stationaryAllocated = GC.GetAllocatedBytesForCurrentThread() - stationaryAllocatedBefore;
        Require(stationaryAllocated == 0, "stationary refresh must not allocate, allocated: " + stationaryAllocated);
        Frame("animator changes after robot tick", () => hand.Matrix.m03 = 1f);
        Frame("parent transforms after robot tick", () => hand.Matrix.m13 = 2f);
        Frame("unrelated parent movement without explicit dirty", () => unrelated.Matrix.m23 = 3f);
        Frame("unrelated material replacement without explicit dirty", () => unrelated.Material = new Material());
        Frame("sub-pixel exact matrix change", () => hand.Matrix.m01 = float.Epsilon);
        Frame("position cell change", () => hand.Position = new Vector3(24, 0, -17));
        Frame("new item", () => hand.Id = 222);
        Frame("new mesh", () => hand.Mesh = new Mesh());
        Frame("new material", () => hand.Material = new Material());
        Frame("layer", () => hand.Layer = 7);
        Frame("shadow cast", () => hand.Shadow = ShadowCastingMode.Off);
        Frame("shadow receive", () => hand.ReceiveShadows = false);
        Frame("sleep tint", () => hand.SleepTint = true);
        int beforeSleepingColor = optimized.Batches.ClearCalls;
        Frame("in-place sleeping material color", () => hand.Material.SleepingColor = Color.red);
        Require(optimized.Batches.ClearCalls == beforeSleepingColor + 1, "BRG sleeping color upload must refresh");
        Frame("line debug enabled", () => hand.DebugColor = true);
        Frame("line debug color", () => hand.Color = new Color32(17, 38, 95, 255));
        Frame("mesh bounds in-place", () => hand.Mesh.bounds = new Bounds(Vector3.one, Vector3.one * 3));
        Frame("instancing disabled externally", () => hand.Material.enableInstancing = false);
        Require(hand.Material.enableInstancing, "refresh must preserve material instancing restoration");
        Frame("ancestor hidden", () => hand.Renderable = false);
        int hiddenBuilds = optimized.Batches.ClearCalls;
        Frame("hidden object motion", () => hand.Matrix.m03 = 9f);
        Require(optimized.Batches.ClearCalls == hiddenBuilds, "non-rendered object must not cause batch changes");
        Frame("ancestor visible", () => hand.Renderable = true);
        Frame("mesh missing", () => hand.Mesh = null);
        Frame("mesh restored", () => hand.Mesh = new Mesh());
        optimized.Batches.CameraVisible = false;
        Frame("culled movement", () => hand.Matrix.m13 = 8f);
        Require(optimized.Batches.VisibleEntries == 0, "offscreen rendering is culled");
        optimized.Batches.CameraVisible = true;
        Frame("camera returns");
        Require(optimized.Batches.VisibleEntries == reference.portableObjectBatches.Entries.Count, "camera return uses current matrices");
        Remove(hand); optimized.Frame(); reference.Frame(); Equal(optimized.Batches, reference.portableObjectBatches, "unregister");
        hand.Id = 333; hand.Matrix.m23 = 12f; Add(hand); Frame("same pooled object re-registered");
        optimized.SetCellSize(3f); reference.portableObjectBatchCellSize = 3f; Frame("batch cell size changed");
        int beforeExplicit = optimized.Batches.ClearCalls;
        optimized.MarkDirty(); Frame("explicit dirty unchanged data");
        Require(optimized.Batches.ClearCalls == beforeExplicit + 1, "explicit dirty must never be filtered");
        optimized.InjectDestroyed(); reference.registeredPortableObjects.Add(null); Frame("destroyed entry cleanup");
        Frame("stable after cleanup");
        for (int i = 0; i < 300; i++)
        {
            int frame = i;
            Frame("motion trace " + i, () => {
                if (frame % 5 == 0) hand.Matrix.m03 = frame * .0000001f;
                if (frame % 13 == 0) unrelated.Matrix.m23 += .01f;
                if (frame % 23 == 0) hand.Renderable = !hand.Renderable;
                if (frame % 31 == 0) optimized.MarkDirty();
            });
        }
        var part = new RobotArm.RobotArmInstancedRenderPart {
            SharedMaterials = new[] { new Material(), null, new Material() }, MaterialCount = 3 };
        part.Transform.Matrix.m03 = 5f;
        arm.instancedRenderParts = new[] { part };
        var armBatches = new VirtualRenderBatchCollection();
        arm.AppendInstancedRenderData(armBatches, 8f);
        Require(part.Transform.Reads == 1 && armBatches.Entries.Count == 2, "arm matrix read once for all valid material slots");
        Require(armBatches.Entries[0].Matrix.Equals(armBatches.Entries[1].Matrix), "material slots receive identical matrices");
        Require(armBatches.Entries[0].Key.SubmeshIndex == 0 && armBatches.Entries[1].Key.SubmeshIndex == 2, "null material retains submesh numbering");
        Predicate<int> filter = arm.Filter;
        Require(ReferenceEquals(filter, arm.Filter), "pickup delegate must be reused");
        Require(!filter(-1), "negative item ID stays rejected");
        arm.AllowedItems.Add(4);
        Require(filter(4) && !filter(5), "cached delegate reads current filter values");
        arm.AllowedItems = new HashSet<int> { 5 };
        Require(!filter(4) && filter(5), "pooled arm's replaced filter remains live");
        var secondArm = new RobotArm(hand); secondArm.AllowedItems.Add(4);
        Require(secondArm.Filter(4) && !secondArm.Filter(5) && !ReferenceEquals(filter, secondArm.Filter), "filter delegates retain their own arm");
        Console.WriteLine($"Robot arm visual differential harness: {checks} checks passed; stationary 120 frames: 0 rebuilds, 120 render submissions.");
    }
}
