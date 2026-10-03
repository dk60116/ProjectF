using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// UI/engine doubles; the view's frame loop, visibility predicate and gauge lifecycle are extracted unchanged.
public class GaugeGameObject
{
    public int layer;
    public bool Destroyed;
    public T AddComponent<T>() where T : new() => new T();
}
public class GaugeTransform
{
    public Vector3 position;
    public Quaternion rotation;
    public Matrix4x4 worldToLocalMatrix => Matrix4x4.identity;
}
public class BoxCollider
{
    public bool enabled, isTrigger;
    public Vector3 center, size;
    public object sharedMaterial;
    public int includeLayers, excludeLayers;
}
public class DefaultGauge
{
    public readonly GaugeGameObject gameObject = new();
    public float Fill;
    public Color FillColor;
    public Vector3 Position;
    public void SetFillColor(Color color) => FillColor = color;
}
public class UIManager
{
    public static UIManager Instance;
    public readonly Stack<DefaultGauge> Pool = new();
    public int Created, Acquired, Released;
    public DefaultGauge LastGauge;
    public DefaultGauge AcquireEnergyGauge()
    {
        Acquired++;
        if (Pool.Count == 0) { Created++; LastGauge = new DefaultGauge(); }
        else LastGauge = Pool.Pop();
        return LastGauge;
    }
    public void UpdateEnergyGauge(DefaultGauge gauge, Vector3 position, float fill)
    { gauge.Position = position; gauge.Fill = fill; }
    public void ReleaseEnergyGauge(DefaultGauge gauge) { Released++; Pool.Push(gauge); }
}
public class Player
{
    public readonly GaugeTransform transform = new();
    public GaugeTransform BodyTransform => transform;
}
public class GameManager
{
    public static GameManager Instance;
    public Player Player;
    public bool InstallationPlacementActive, MapEditActive;
}
public class CameraBoundary { public static CameraBoundary main => null; }
public static class MapObjectTickProfiler
{
    public readonly struct Scope : IDisposable { public void Dispose() { } }
    public static Scope SampleLateUpdateCaller<T>() => default;
}
namespace ProjectF.Rendering
{
    public class CameraRenderCulling
    {
        public bool Visible = true;
        public void Update(object camera) { }
        public bool Intersects(Bounds bounds) => Visible;
    }
}
public partial class MiningWorld
{
    internal MiningMachineInstance selectedMarkerMiner;
    public readonly List<MiningMachineInstance> Candidates = new();
    internal void BuildCandidates(ProjectF.Rendering.CameraRenderCulling culling, List<MiningMachineInstance> result)
    { result.Clear(); result.AddRange(Candidates); }
    internal void BuildNearby(Vector3 position, List<MiningMachineInstance> result) => result.Clear();
}
public sealed partial class MiningWorldView
{
    private readonly GaugeTransform transform = new();
    private readonly GaugeGameObject gameObject = new();
    private static void Destroy(GaugeGameObject value) => value.Destroyed = true;
    internal MiningWorldView(MiningWorld owner) => world = owner;
    internal void Present() => LateUpdate();
    internal void Disable() => OnDisable();
    internal bool InCamera { set => culling.Visible = value; }
    internal int GaugeCount => workGauges.Count;
    internal void AddCollider(MiningMachineInstance miner) => colliders.Add(miner, new BoxCollider());
}
public static class MiningGaugeChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        MapObjectTickManager.CurrentSimulationTick = 60;
        MapObjectTickManager.WaitingForWorldLoad = false;
        var world = new MiningWorld();
        var placement = new BlockStateStore.InstallationSaveState();
        placement.inputOutputState.outputCoordinates.Add(new Vector2Int(1, 0));
        var miner = new MiningMachineInstance(world, 0, 1, new ProjectF.MapObjects.MapObjectHandle(100),
            new MiningMachine(), placement, new MiningRenderTemplate());
        world.Value.Clock.Production = new ProjectF.Simulation.ProductionProcess
        { Active = true, OutputItemId = 1, OutputCount = 1 };
        world.Value.Clock.SupplyRatio = 1;
        world.Value.Clock.SampleTick = 0;
        // Native matrix construction is an engine boundary, not part of UI regression coverage.
        typeof(MiningMachineInstance).GetField("geometryCached", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(miner, true);
        typeof(MiningMachineInstance).GetField("cachedCullBounds", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(miner, new Bounds(new Vector3(0, 1, 0), new Vector3(2, 2, 2)));
        world.Candidates.Add(miner);
        var view = new MiningWorldView(world);
        var ui = UIManager.Instance = new UIManager();
        var player = new Player();
        GameManager.Instance = new GameManager { Player = player };
        player.transform.position = new Vector3(4, 0, 0);
        view.Present();
        Check(view.GaugeCount == 0 && ui.Created == 0, "distant miners do not create gauges");
        player.transform.position = new Vector3(3, 100, 0);
        view.Present();
        Check(view.GaugeCount == 1 && ui.Created == 1, "approaching an ECS miner shows its work gauge, ignoring elevation");
        Check(Math.Abs(ui.LastGauge.Fill - .5f) < .00001f, "gauge uses the deadline-based progress snapshot");
        Check(ui.LastGauge.Position.y == 2.25f, "gauge is placed above model bounds with authored offset");
        Check(ui.LastGauge.FillColor == miner.Template.WorkGaugeFillColor, "gauge retains authored progress color");
        Check(world.Value.Clock.Production.ConsumedEnergyUnits == 0, "presentation never advances authoritative production");
        view.Present();
        Check(ui.Acquired == 1, "stationary frames reuse the bound gauge");
        world.Value.Clock.Production.ConsumedEnergyUnits = DeterministicSimulationUnits.FromInt(30);
        world.Value.Clock.SampleTick = 60; world.Value.Clock.SupplyRatio = 0;
        MapObjectTickManager.CurrentSimulationTick = 600; view.Present();
        Check(view.GaugeCount == 1 && Math.Abs(ui.LastGauge.Fill - .5f) < .00001f, "power outage retains the paused progress gauge");
        world.Value.Clock.Production.ConsumedEnergyUnits = miner.Template.CompleteEnergy;
        world.Value.Clock.Production.WaitingForOutput = true; view.Present();
        Check(ui.LastGauge.Fill == 1, "waiting for output shows a full work gauge");
        player.transform.position = new Vector3(4, 0, 0); view.Present();
        Check(view.GaugeCount == 0 && ui.Released == 1, "moving away returns the gauge to the shared pool");
        world.selectedMarkerMiner = miner; view.Present();
        Check(view.GaugeCount == 1 && ui.Created == 1, "selection shows a distant miner and reuses its gauge");
        view.AddCollider(miner); view.Present();
        Check(view.GaugeCount == 1, "releasing a distant collision proxy does not release the selected miner's gauge");
        view.InCamera = false; view.Present();
        Check(view.GaugeCount == 0, "off-screen miners release their gauges");
        view.InCamera = true; world.selectedMarkerMiner = null;
        GameManager.Instance.MapEditActive = true; view.Present();
        Check(view.GaugeCount == 1, "edit mode follows the shared marker visibility rule");
        miner.PlacementPresentationSuppressed = true; view.Present();
        Check(view.GaugeCount == 0, "suppressed placement presentation hides the gauge");
        miner.PlacementPresentationSuppressed = false; view.Present();
        world.Value.Clock.Production.Clear(); view.Present();
        Check(view.GaugeCount == 0, "idle or depleted production releases the gauge");
        world.Value.Clock.Production.Active = true; view.Present(); view.Unbind(miner);
        Check(view.GaugeCount == 0, "removing a miner releases the gauge even without a collider proxy");
        view.Present(); MapObjectTickManager.WaitingForWorldLoad = true; view.Present();
        Check(view.GaugeCount == 0, "world loading releases bound gauges");
        MapObjectTickManager.WaitingForWorldLoad = false; view.Present();
        world.Terrain.IsBenchmarkPlacementInProgress = true; view.Present();
        Check(view.GaugeCount == 0, "bulk placement suspends gauge presentation");
        world.Terrain.IsBenchmarkPlacementInProgress = false; view.Present(); view.Disable();
        Check(view.GaugeCount == 0, "disabling the view releases all gauges");
        view.Present(); var bound = ui.LastGauge; UIManager.Instance = null; view.Present();
        Check(view.GaugeCount == 0 && bound.gameObject.Destroyed, "UI teardown clears gauges without leaking objects");
        UIManager.Instance = ui; view.Present(); world.Alive = false; view.Present();
        Check(view.GaugeCount == 0, "removed state generations cannot retain gauges");
        GameManager.Instance = null; UIManager.Instance = null;
        Console.WriteLine($"PASS {checks} mining gauge visibility/progress/pooling/lifecycle checks (actual frame loop, engine/UI doubles)");
    }
}
