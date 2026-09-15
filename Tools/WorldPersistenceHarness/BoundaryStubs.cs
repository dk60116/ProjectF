using System.Collections;
using ProjectF.Simulation;
using UnityEngine;

namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y);
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Clamp01(float value) => Math.Clamp(value, 0, 1);
    }
    public static class Application { public static bool isPlaying = true; }
    public static class Time { public static int frameCount; public static double realtimeSinceStartupAsDouble; }
    public sealed class Coroutine { public IEnumerator Routine; }
    public class MonoBehaviour
    {
        public Coroutine Current;
        public bool DisposeWhenStopped = true;
        public Coroutine StartCoroutine(IEnumerator routine)
        {
            Current = new Coroutine { Routine = routine };
            routine.MoveNext(); // Unity executes through the initial yield synchronously.
            return Current;
        }
        public void StopCoroutine(Coroutine coroutine)
        {
            if (DisposeWhenStopped) (coroutine.Routine as IDisposable)?.Dispose();
            Current = null;
        }
        public void Frame()
        {
            Time.frameCount++; Time.realtimeSinceStartupAsDouble += 1d / 60;
            if (Current != null && !Current.Routine.MoveNext()) Current = null;
        }
    }
}
namespace Unity.Profiling
{
    public readonly struct ProfilerMarker
    {
        public Scope Auto() => default;
        public readonly struct Scope : IDisposable { public void Dispose() { } }
    }
}

// IO, scene lookup, topology building and DTOs are test doubles. Lifecycle/capture
// orchestration, scheduler, core checkpoint gate and disk publication are production methods.
public sealed class MapSaveData { public int Items; }
public sealed class TerrainSaveData { }
public sealed class WorldTimeSaveData { }
public sealed class PlayerSaveData { }
public sealed class BeltSnapshot { }
public sealed class SaveGameData
{
    public const int CurrentVersion = 62;
    public int version;
    public long savedAtUtcTicks, simulationTick, nextInstallationSimulationId;
    public object itemCatalog;
    public TerrainSaveData terrain;
    public WorldTimeSaveData worldTime;
    public MapSaveData map;
    public PlayerSaveData player;
    public BeltSnapshot beltSimulation;
}
public sealed class ItemManager { public object ItemDefinitions; }
public sealed class Clock { public WorldTimeSaveData CaptureSaveState() => new(); }
public sealed class GameManager
{
    public static GameManager Instance = new();
    public ItemManager ItemManger = new();
    public Clock WorldTime = new();
}
public static class SaveGameItemIdRemapper { public static object CaptureItemCatalog(object definitions) => new(); }
public class Player { public PlayerSaveData CaptureSaveState() => new(); }
public static class InstallationObject { public static long NextSimulationId = 101; }
public static partial class MapObjectTickManager
{
    public static SimulationTickWorld World = new();
    public static bool CanCaptureCheckpoint => World.CanCaptureCheckpoint;
    public static long CurrentSimulationTick => World.CurrentTick;
}
public partial class SaveManager
{
    private bool sceneReloadRequested;
    private Coroutine activeLoadCoroutine;
    private Task<SaveGameData> activeLoadReadTask;
    public static SaveGameData Capture(TerrainGenerator terrain) => CaptureSaveData(terrain, new Player());
}
public partial class TerrainGenerator
{
    public static TerrainGenerator Active;
    public readonly WorldRestoreProgress worldRestore = new();
    private bool pendingSavedWorldFinalization, deferConveyorItemRestoreUntilBeltTopologyReady;
    private MapSaveData pendingWorldMapSaveData;
    private Action pendingWorldReadyCallback;
    private readonly Dictionary<int, object> loadedChunks = new() { { 0, new() } };
    public bool IsChunkStreamingBusy;
    public bool IsWorldReadyForPresentation => worldRestore.IsReady;
    public bool IsWorldRestorePending => worldRestore.IsPending;
    public readonly List<string> Order = new();
    public bool ThrowInConnections, PendingBeltWrite = true;
    public int LiveItems;
    public void Begin(Action checkpoint)
    {
        BeginWorldFinalization(true, new(), checkpoint);
        worldRestore.RecordsRestored();
    }
    public void FinalizeWorld() => TryFinalizePendingWorldLoad();
    public void RemoveAllChunksForTest() => loadedChunks.Clear();
    private void RefreshLoadedConveyorBeltRuntimeViews() { Order.Add("belts"); if (ThrowInConnections) throw new Exception("topology"); }
    private void RefreshLoadedPipeRuntimeViews() => Order.Add("pipes");
    private void ExpandConveyorItemSaveRunsAfterBeltTopology(MapSaveData map) => Order.Add("expand");
    private void ApplyLoadedConveyorItemSaveStates(MapSaveData map) => Order.Add("items");
    private void RefreshLoadedRuntimeRegistrations() => Order.Add("register");
    private void RefreshLoadedRuntimeVisibility() => Order.Add("views");
    private void RebuildAuthoritativeConveyorItemTotal() => Order.Add("total");
    public BeltSnapshot CaptureBeltSimulationSnapshot() { Order.Add("beltCheckpoint"); PendingBeltWrite = false; LiveItems = 17; return new(); }
    public TerrainSaveData CaptureTerrainSaveState() => new();
    public MapSaveData CaptureMapSaveState()
    {
        if (PendingBeltWrite) throw new Exception("Map read before pending belt writes completed");
        Order.Add("mapCheckpoint"); return new() { Items = LiveItems };
    }
}
public static partial class SaveGameBinarySerializer
{
    private const string Magic = "PF_SAVE";
    public static bool ThrowAfterWrite;
    private static void WriteSaveGameData(BinaryWriter writer, SaveGameData data)
    {
        writer.Write(data.map.Items);
        if (ThrowAfterWrite) throw new IOException("Injected serialization failure");
    }
}
