using System;
using System.Collections.Generic;
using ProjectF.Railway;
using UnityEngine;

// Only scene lifetime, persistence DTOs and item definitions are substituted.
static class Application { public static bool isPlaying => false; }
static class ProjectFApplicationLifecycle { public static bool IsQuitting => false; }
public sealed class SceneObject { public bool activeInHierarchy = true; public string name = "Test"; }
public sealed class SceneTransform { public Vector3 forward = Vector3.right, position, localScale = Vector3.one; public Matrix4x4 worldToLocalMatrix => Matrix4x4.identity; public Matrix4x4 localToWorldMatrix => Matrix4x4.identity; }
public class MapObject
{ public enum MultiFocusMode { None } public enum MapObjectStatus { None } }
public interface IMapObjectTarget
{
    MapObject SceneObject { get; } bool IsTargetActive { get; } Vector3 WorldPosition { get; }
    string ObjectName { get; } bool AllowsFocus { get; } bool AllowsAnimalTraversal { get; }
    MapObject.MultiFocusMode FocusMode { get; } MapObject.MapObjectStatus Status { get; }
    ItemDefinition BoundItemDefinition { get; } int ResolveItemId(); int ResolvedItemId { get; } int ID { get; }
}
public interface IMapObjectSimulationIdentity { long SimulationId { get; } }
public static class MapObjectTargetExtensions { public static bool IsAlive(this IMapObjectTarget target) => target != null && target.IsTargetActive; }
public class InstallationObject : MapObject, IMapObjectTarget, IRailwayTarget
{
    public readonly SceneObject gameObject = new SceneObject();
    public readonly SceneTransform transform = new SceneTransform();
    public string name = "Test";
    public bool isActiveAndEnabled = true;
    public Vector2Int RuntimeAnchorCoordinate;
    public int RuntimeQuarterTurns;
    public long RuntimePlacementSequence;
    public List<Vector2Int> RuntimeOccupiedCoordinates = new List<Vector2Int>();
    public ItemDefinition BoundItemDefinition;
    public MapObject SceneObject => this;
    public bool IsTargetActive => isActiveAndEnabled;
    public Vector3 WorldPosition => transform.position;
    public string ObjectName => name;
    public bool AllowsFocus => true;
    public bool AllowsAnimalTraversal => true;
    public MultiFocusMode FocusMode => MultiFocusMode.None;
    public MapObjectStatus Status => MapObjectStatus.None;
    public int ResolvedItemId => ItemId;
    public int ID => ItemId;
    ItemDefinition IMapObjectTarget.BoundItemDefinition => BoundItemDefinition;
    long IRailwayTarget.RuntimePlacementSequence => RuntimePlacementSequence;
    Vector2Int IRailwayTarget.RuntimeAnchorCoordinate => RuntimeAnchorCoordinate;
    IReadOnlyList<Vector2Int> IRailwayTarget.RuntimeOccupiedCoordinates => RuntimeOccupiedCoordinates;
    public int ItemId = 1;
    public long SimulationId => RuntimePlacementSequence;
    public int ResolveItemId() => ItemId;
    public bool TryGetPlacementRuntime(out Vector2Int anchor, out int rotation)
    { anchor = RuntimeAnchorCoordinate; rotation = RuntimeQuarterTurns; return RuntimePlacementSequence > 0; }
    public static int CompareSimulationOrder(InstallationObject a, InstallationObject b)
        => a.RuntimePlacementSequence.CompareTo(b.RuntimePlacementSequence);
    public T GetComponent<T>() where T : class => this as T;
    public T GetComponentInChildren<T>(bool includeInactive) where T : class => this as T;
    public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class => Array.Empty<T>();
    protected virtual void OnEnable() { }
    protected virtual void OnDisable() { }
    protected virtual void OnPlacementRuntimeChanged() { }
    protected virtual void OnPlacementRuntimeCleared() { }
    public virtual void PrepareForPool() { }
    public void NotifyChanged() => OnPlacementRuntimeChanged();
    public void Disable() { isActiveAndEnabled = false; gameObject.activeInHierarchy = false; OnDisable(); }
    public void ClearPlacement() => OnPlacementRuntimeCleared();
}
public partial class Railload : InstallationObject, IRailTarget
{
    public const float ConnectionEndpointSnapMaxDistance = .55f;
    private readonly RailPathData renderedPath = new RailPathData();
    private bool renderedPathCacheDirty = true;
    private List<Vector2> runtimeVisualPathPoints = new List<Vector2>();
    private bool runtimeVisualPathExtendsStart, runtimeVisualPathExtendsEnd;
    public IReadOnlyList<Vector2> RuntimeVisualPathPoints => runtimeVisualPathPoints;
    public bool RuntimeVisualPathExtendsStart => runtimeVisualPathExtendsStart;
    public bool RuntimeVisualPathExtendsEnd => runtimeVisualPathExtendsEnd;
    public int RequiredItemCount => 1;
    public static int ResolveRequiredItemCount(IReadOnlyList<Vector2Int> coordinates) => 1;
    public void Configure(Vector2[] points, bool extendStart = false, bool extendEnd = false)
    {
        runtimeVisualPathPoints = new List<Vector2>(points);
        runtimeVisualPathExtendsStart = extendStart; runtimeVisualPathExtendsEnd = extendEnd;
        renderedPathCacheDirty = true;
    }
}
public class Train : InstallationObject
{
    public IRailTarget Rail;
    public float Distance;
    public bool TryGetCurrentRailPose(out IRailTarget rail, out float distance, out Vector2 point, out Vector2 tangent)
    { rail = Rail; distance = Distance; point = tangent = default; return rail != null && rail.TrySampleRenderedPath(distance, out point, out tangent); }
}
public class RailHandcar : Train
{
    public bool TryGetRailForwardDirection(out Vector2 direction)
    { direction = new Vector2(transform.forward.x, transform.forward.z); return true; }
}
public class ItemDefinition { public int id; public string name, itemName; public InstallationObject mapObject; }
public sealed class ItemManager { public List<ItemDefinition> ItemDefinitions = new List<ItemDefinition>(); }
public sealed class GameManager { public static GameManager Instance = new GameManager(); public ItemManager ItemManger = new ItemManager(); }
static class InputOutputModule
{
    public static ItemDefinition ResolveItemDefinition(int id) => GameManager.Instance.ItemManger.ItemDefinitions.Find(d => d.id == id);
}
public class BlockStateStore
{
    public class InstallationSaveState
    {
        public int itemId = 1, railRequiredItemCount = 1, quarterTurns;
        public long placementSequence;
        public Vector2Int anchorCoordinate, storageKey;
        public bool hasStorageKey, railVisualPathExtendsStart, railVisualPathExtendsEnd;
        public List<Vector2> railVisualPathPoints;
        public List<Vector2Int> occupiedCoordinates;
        public Vector3 worldPosition;
        public Quaternion worldRotation = Quaternion.identity;
        public string stationName;
        public Color32 stationColor;
        public bool stationColorAssigned;
    }
    public static Vector2Int GetInstallationStorageKey(InstallationSaveState state)
        => state.hasStorageKey ? state.storageKey : state.anchorCoordinate;
}
public sealed class TerrainGenerator
{
    public readonly SceneTransform transform = new SceneTransform();
    public static TerrainGenerator Active;
    public readonly RailWorld World = new RailWorld();
    public RailWorld GetRailWorld() => World;
    public static TerrainGenerator ResolveActive() => Active;
    public string ResolveUniqueTrainStationName(ITrainStationTarget station, string name) => name;
    public void SaveRuntimeInstallationState(InstallationObject obj) { }
    public int MarkerChanges;
    public void NotifyTrainStationMapChanged() => MarkerChanges++;
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) { block = null; return false; }
}
public sealed class Block { public IMapObjectTarget MapObject; public void SetMapObject(IMapObjectTarget value) => MapObject = value; }
namespace ProjectF.Simulation { }
namespace ProjectF.Railway
{
    public sealed class RailWorldView
    {
        public static RailWorldView Create(RailWorld world, TerrainGenerator terrain) => new RailWorldView();
        public void Add(RailwayInstance instance) { }
        public void Remove(RailwayInstance instance) { }
        public void Invalidate(RailwayInstance instance) { }
        public void Release() { }
    }
}
