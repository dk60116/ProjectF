using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;

// Engine, render templates and persistence are boundaries; the real world/index/entities run.
public interface IMapObjectTarget
{
    bool IsTargetActive { get; }
    int ResolveItemId();
}
public sealed class BoxCollider { }
public class MapObject
{
    public enum MultiFocusMode { All }
    public enum MapObjectStatus { Normal }
    public static bool IsItemAllowedByFilterMask(int item, bool initialized, List<ulong> words) =>
        !initialized || item < 0 || item >> 6 >= words.Count || (words[item >> 6] & (1UL << (item & 63))) != 0;
}
public sealed class ItemDefinition
{
    public int id;
    public Archetype MapObjectArchetype = new();
    public sealed class Archetype { public readonly List<int> RenderParts = new() { 0 }; }
}
public sealed class WorkableObject : InstallationObject, IMapObjectTarget
{
    public bool IsTargetActive => true;
    internal static readonly WorkableRangeIndex RangeIndex = new();
    internal static void EnsureRangeVisualHost() { }
    internal static void RegisterTarget(IWorkableTarget target) => RangeIndex.Update(target);
    internal static void UnregisterTarget(IWorkableTarget target) => RangeIndex.Remove(target);
    internal static void SetTargetSelected(IWorkableTarget target, bool selected) { }
    public ItemDefinition BoundItemDefinition = new() { id = 34 };
    public uint RangeCells = 2;
    public string ObjectName => "Workbench";
    public bool AllowsFocus => true;
    public bool AllowsAnimalTraversal => false;
    public MapObject.MultiFocusMode FocusMode => MapObject.MultiFocusMode.All;
    public MapObject.MapObjectStatus Status => MapObject.MapObjectStatus.Normal;
    public int ResolveItemId() => BoundItemDefinition.id;
    public T GetComponent<T>() where T : class => new BoxCollider() as T;
}
public class InstallationObject
{
    private static long sequence;
    public readonly Pose transform = new();
    public BlockStateStore.InstallationSaveState Placement;
    public static long ClaimNextPlacementSequence(long previous) => previous > 0 ? previous : ++sequence;
    public void ApplyItemFilterMask(List<ulong> words, bool initialized)
    { Placement.itemFilterMaskWords.Clear(); Placement.itemFilterMaskWords.AddRange(words); Placement.itemFilterMaskInitialized = initialized; }
}
public sealed class Pose
{
    public Vector3 position, localScale = Vector3.one;
    public Quaternion rotation = Quaternion.identity;
    public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }
}
public static class InputOutputModule { public static ItemDefinition ResolveItemDefinition(int id) => new() { id = id }; }
public readonly record struct MapObjectHandle(int Id);
public sealed class VirtualObjectWorld
{
    public static VirtualObjectWorld Current { get; private set; }
    private readonly Dictionary<Vector2Int, MapObjectHandle> handles = new();
    private readonly HashSet<MapObjectHandle> alive = new();
    private int sequence;
    public static VirtualObjectWorld Ensure() => Current ??= new();
    public void Register(Vector2Int key) { var h = new MapObjectHandle(++sequence); handles[key] = h; alive.Add(h); }
    public bool TryGetInstallationHandle(Vector2Int key, out MapObjectHandle h) => handles.TryGetValue(key, out h);
    public bool IsHandleAlive(MapObjectHandle h) => alive.Contains(h);
    public void Expire(MapObjectHandle h) => alive.Remove(h);
}
public sealed class BlockStateStore
{
    public readonly Dictionary<Vector2Int, InstallationSaveState> States = new();
    public bool TryCaptureInstallationState(WorkableObject source, out InstallationSaveState state)
    { state = source.Placement?.Clone(); return state != null; }
    public bool RegisterDataOnlyInstallationSharedState(InstallationSaveState state, out InstallationSaveState stored)
    {
        stored = state.Clone(); States[state.anchorCoordinate] = stored;
        VirtualObjectWorld.Ensure().Register(state.anchorCoordinate); return true;
    }
    public sealed class InstallationSaveState
    {
        public Vector2Int anchorCoordinate;
        public int itemId;
        public int quarterTurns;
        public bool hasWorldPose;
        public long placementSequence;
        public Vector3 worldPosition;
        public Quaternion worldRotation = Quaternion.identity;
        public readonly List<Vector2Int> occupiedCoordinates = new();
        public bool itemFilterMaskInitialized;
        public readonly List<ulong> itemFilterMaskWords = new();
        public InstallationSaveState Clone()
        {
            var copy = new InstallationSaveState { anchorCoordinate = anchorCoordinate, itemId = itemId, quarterTurns = quarterTurns,
                hasWorldPose = hasWorldPose, worldPosition = worldPosition, worldRotation = worldRotation,
                placementSequence = placementSequence, itemFilterMaskInitialized = itemFilterMaskInitialized };
            copy.occupiedCoordinates.AddRange(occupiedCoordinates); copy.itemFilterMaskWords.AddRange(itemFilterMaskWords); return copy;
        }
    }
    public static Vector2Int GetInstallationStorageKey(InstallationSaveState state) => state.anchorCoordinate;
}
public sealed class Block
{
    public IMapObjectTarget MapObject;
    public void SetMapObject(IMapObjectTarget target) => MapObject = target;
}
public sealed partial class TerrainGenerator
{
    public readonly Pose transform = new();
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
    private readonly BlockStateStore resourceStateStore = new();
    public BlockStateStore Store => resourceStateStore;
    public WorkableObject Source;
    private void EnsureResourceStateStore() { }
    private ItemDefinition ResolveInstallationDefinition(BlockStateStore.InstallationSaveState state) => Source.BoundItemDefinition;
    private InstallationPlacementController ResolveInstallationPlacementController() => new(this);
    private InstallationObject ResolveInstallationSourcePrefab(BlockStateStore.InstallationSaveState state, InstallationPlacementController c, ItemDefinition definition) => Source;
    public bool Restore(BlockStateStore.InstallationSaveState state) => TryRestoreDataOnlyWorkable(state);
    public InstallationObject CreateInstallationObject(WorkableObject source, object parent) => new WorkableObject { BoundItemDefinition = source.BoundItemDefinition, RangeCells = source.RangeCells };
}
public sealed partial class InstallationPlacementController
{
    private readonly TerrainGenerator terrain;
    public InstallationPlacementController(TerrainGenerator terrain) { this.terrain = terrain; }
    private TerrainGenerator ResolveInstallPreviewTerrain() => terrain;
    public Vector3 GetInstalledObjectWorldPosition(Vector2Int c, WorkableObject source, int turns) => new(c.x, 0, c.y);
    public Quaternion GetInstalledObjectRotation(WorkableObject source, int turns) => Quaternion.identity;
    private void ConfigureInstalledObjectRuntime(InstallationObject proxy, Vector2Int c, int turns, long placementSequence, IReadOnlyList<Vector2Int> occupiedCoordinatesOverride)
    {
        proxy.Placement = new() { anchorCoordinate = c, quarterTurns = turns, placementSequence = placementSequence,
            itemId = ((WorkableObject)proxy).ResolveItemId(), hasWorldPose = true, worldPosition = proxy.transform.position, worldRotation = proxy.transform.rotation };
        foreach (var coordinate in occupiedCoordinatesOverride) proxy.Placement.occupiedCoordinates.Add(coordinate);
    }
    public bool Materialize(WorkableInstance instance, out InstallationObject proxy) => TryMaterializeDataOnlyWorkableForEditing(instance, out proxy);
}
public static class MapObjectTickProfiler { public static void AddRuntimeCounter(string category, string key, long value) { } }
namespace ProjectF.MapObjects
{
    internal sealed class WorkableRenderTemplate
    {
        internal readonly ItemDefinition Definition;
        internal readonly Vector3 Scale = Vector3.one;
        internal readonly Bounds LocalBounds = new(Vector3.zero, Vector3.one);
        internal readonly uint RangeCells;
        internal readonly bool ShowRange = true;
        internal readonly float RangeYOffset = .04f;
        internal WorkableRenderTemplate(WorkableObject source) { Definition = source.BoundItemDefinition; RangeCells = source.RangeCells; }
    }
    public sealed class WorkableWorldView
    {
        internal static WorkableWorldView Create(WorkableWorld world, object parent) => new();
        public int VisibleCount => 0;
        internal void Unbind(WorkableInstance instance) { }
        internal void Release() { }
    }
}
namespace ProjectF.Rendering
{
    public sealed class CameraRenderCulling
    {
        public Vector2Int Minimum, Maximum;
        public bool Enabled;
        public bool TryGetVisibleCellRange(int size, int padding, out Vector2Int min, out Vector2Int max)
        { min = Minimum; max = Maximum; return Enabled; }
    }
}
public static class VirtualRenderBatchCollection
{
    public static Bounds CalculateWorldBounds(Bounds local, Matrix4x4 matrix) => new(matrix.MultiplyPoint3x4(local.center), local.size);
}
public static class HarnessEngine
{
    public static bool IntersectRay(Bounds bounds, Ray ray, out float distance)
    {
        float near = 0, far = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float origin = ray.origin[axis], direction = ray.direction[axis];
            if (Math.Abs(direction) < 1e-8f)
            { if (origin < bounds.min[axis] || origin > bounds.max[axis]) { distance = 0; return false; } continue; }
            float a = (bounds.min[axis] - origin) / direction, b = (bounds.max[axis] - origin) / direction;
            near = Math.Max(near, Math.Min(a, b)); far = Math.Min(far, Math.Max(a, b));
            if (near > far) { distance = 0; return false; }
        }
        distance = near;
        return true;
    }
}
