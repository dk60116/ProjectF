using System;
using System.Collections.Generic;
using ProjectF.MapObjects;
using UnityEngine;

namespace UnityEngine
{
    public sealed class DisallowMultipleComponent : Attribute { }
    public sealed class DefaultExecutionOrder(int order) : Attribute { public readonly int Order = order; }
    public sealed class MinAttribute(float minimum) : Attribute { public readonly float Minimum = minimum; }
    public class MonoBehaviour
    {
        public GameObject gameObject;
        public Transform transform = new();
        protected static void Destroy(GameObject target) => target.Destroyed = true;
        protected static void DestroyImmediate(GameObject target) => target.Destroyed = true;
    }
    public sealed class GameObject(string name)
    {
        public readonly string Name = name;
        public Transform transform = new();
        public bool Destroyed;
        public T AddComponent<T>() where T : MonoBehaviour, new() => new() { gameObject = this };
        public void SetActive(bool active) { }
    }
    public sealed class Camera { public static Camera main = new(); }
    public static class Time { public static int frameCount; }
    public static class Application { public static bool isPlaying = true; }
}
public static class TransformExtensions
{
    public static void SetParent(this Transform self, Transform parent, bool worldPositionStays) { }
}
public sealed class ItemDefinition
{
    public string itemName = "fixture";
    public MapObjectArchetype MapObjectArchetype = new();
}
public sealed class MapObjectArchetype { public bool Supported; public bool FailSubmission; }
public sealed class ItemManager
{
    public readonly Dictionary<int, ItemDefinition> Definitions = new();
    public int Queries;
    public bool TryGetItemDefinitionById(int id, out ItemDefinition definition)
    { Queries++; return Definitions.TryGetValue(id, out definition); }
}
public sealed class GameManager { public static GameManager Instance; public ItemManager ItemManger; }
public sealed class TerrainGenerator
{
    public static TerrainGenerator Active;
    public bool IsBenchmarkPlacementInProgress;
}
public static class MapObjectTickManager { public static bool WaitingForWorldLoad; }
public static class ProjectFApplicationLifecycle { public static bool IsQuitting; }
public static class MapObjectTickProfiler
{
    public readonly struct Scope : IDisposable { public void Dispose() { } }
    public static Scope SampleNamed(string group, string type, string name) => default;
    public static Scope SampleLateUpdateCaller<T>() => default;
}
public class SpecializedWorld
{
    public readonly HashSet<Vector2Int> Keys = new();
    public bool TryGet(Vector2Int key, out object record) { record = null; return Keys.Contains(key); }
}
namespace ProjectF.Power { public sealed class UtilityPoleWorld : SpecializedWorld { public static UtilityPoleWorld Current; } }
public sealed class ProductionWorld : SpecializedWorld { public static ProductionWorld Current; }
public sealed class MiningWorld : SpecializedWorld { public static MiningWorld Current; }
namespace ProjectF.Benchmark
{
    public static class BenchmarkLayout
    {
        public static int ForcedYields;
        public static bool IsWorkSliceExpired(long start, long now, long frequency)
        { if (ForcedYields == 0) return false; ForcedYields--; return true; }
    }
}
namespace ProjectF.MapObjects
{
    // GPU/type-host boundary only: synchronization policy and world indices are production code.
    public sealed class StaticMapObjectTypeHost : MonoBehaviour
    {
        public static readonly Dictionary<int, StaticMapObjectTypeHost> Hosts = new();
        public static bool ThrowNextSubmission;
        public readonly HashSet<MapObjectHandle> Handles = new();
        public int BeginCalls;
        public int ItemId;
        public MapObjectArchetype Archetype;
        public int InstanceCount => Handles.Count;
        public int ActiveBatchCount => Handles.Count == 0 ? 0 : 1;
        public int ActiveMatrixCount => Handles.Count;
        public int EstimatedDrawCallCount => ActiveBatchCount;
        public int LastVisibleBatchCount => ActiveBatchCount;
        public int LastCulledBatchCount => 0;
        public int LastCandidateBatchCount => ActiveBatchCount;
        public int LastCandidateCellCount => ActiveBatchCount;
        public int LastLegacySubmittedMatrixCount => Handles.Count;
        public int LastLegacyDrawCallCount => ActiveBatchCount;
        public int LastBatchRendererGroupBatchCount => 0;
        public int LastBatchRendererGroupMatrixCount => 0;
        public static bool IsSupportedArchetype(MapObjectArchetype archetype) => archetype?.Supported == true;
        public void Configure(int itemId, MapObjectArchetype archetype, float cellSize)
        { ItemId = itemId; Archetype = archetype; Hosts[itemId] = this; }
        public void BeginSynchronization() { BeginCalls++; Handles.Clear(); }
        public bool SynchronizeRecord(VirtualObjectRecord record)
        {
            if (ThrowNextSubmission) { ThrowNextSubmission = false; throw new InvalidOperationException("fixture submission failure"); }
            if (Archetype.FailSubmission) return false;
            return Handles.Add(record.mapObjectHandle);
        }
        public void CompleteSynchronization() { }
        public void AbortSynchronization() => Handles.Clear();
        public void Render(Camera camera) { }
        public void Suspend() { }
        public void Release() { Handles.Clear(); Hosts.Remove(ItemId); }
    }
}
