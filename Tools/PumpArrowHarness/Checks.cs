using System;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Check(bool condition, string scenario)
    {
        checks++;
        if (!condition) throw new Exception(scenario);
    }

    static void Main()
    {
        var pump = new Pump();
        var arrow = new SpriteRenderer();
        pump.Bind(arrow);
        pump.Enable();
        Check(!arrow.enabled, "enable hides arrow until fluid is resolved");
        Check(pump.NeedsVisuals, "enable requests initial color refresh");
        Check(pump.IsPressureBudgetReset, "arrow initialization preserves pump pressure reset");
        Pipe.FluidItemId = 30;
        pump.Tick();
        Check(arrow.enabled && arrow.color.Equals(Pipe.ResolveFluidDisplayColor(30)), "fluid enables arrow with network color");
        Check(Pipe.LastCoordinate.x == 7 && !Pipe.IncludedPressure, "queries outlet identity without pressure scan");
        int queries = Pipe.Queries, writes = arrow.Writes;
        Time.unscaledTime = .1f;
        pump.Tick();
        Check(Pipe.Queries == queries && !pump.NeedsVisuals, "no network polling before refresh deadline");
        Time.unscaledTime = .21f;
        pump.Tick();
        Check(arrow.Writes == writes, "unchanged fluid avoids renderer writes");
        Pipe.FluidItemId = 113;
        Time.unscaledTime = .42f;
        pump.Tick();
        Check(arrow.color.Equals(Pipe.ResolveFluidDisplayColor(113)), "fluid replacement updates color");
        Pipe.FluidItemId = -1;
        Time.unscaledTime = .63f;
        writes = arrow.Writes;
        pump.Tick();
        Check(!arrow.enabled && arrow.Writes == writes, "empty network hides arrow without resetting tint");
        Time.unscaledTime = .84f;
        pump.Tick();
        Check(!arrow.enabled && arrow.Writes == writes, "empty network keeps arrow hidden");
        Pipe.FluidItemId = 30;
        pump.Resume();
        Check(pump.NeedsVisuals, "return from culling requests immediate refresh");
        pump.Tick();
        Check(arrow.enabled && arrow.color.Equals(Pipe.ResolveFluidDisplayColor(30)), "fluid return restores arrow with current color");
        pump.HasPlacement = false;
        pump.PlacementChanged();
        pump.Tick();
        Check(!arrow.enabled, "unplaced preview hides arrow");
        pump.HasPlacement = true;
        pump.Enable();
        Check(!arrow.enabled, "re-enable hides stale arrow before refresh");
        pump.Tick();
        Check(arrow.enabled && arrow.color.Equals(Pipe.ResolveFluidDisplayColor(30)), "re-enable clears identity cache");
        pump.Bind(null);
        Time.unscaledTime = 2f;
        queries = Pipe.Queries;
        pump.Tick();
        Check(Pipe.Queries == queries && !pump.NeedsVisuals, "missing optional arrow skips queries");
        pump.BaseDirty = true;
        Check(pump.NeedsVisuals, "base production visual requests remain active");
        pump.Tick();
        Check(!pump.BaseDirty && pump.BaseResumes == 1 && pump.BasePlacementChanges == 1,
            "base visual and placement lifecycle remains intact");
        Console.WriteLine($"PASS PumpArrow: {checks} checks");
    }
}

public partial class Pump : InputOutputModule
{
    private long pressureBudgetTick;
    private double pressureBudgetLiters;
    private readonly Pipe.FluidNetworkSearchContext objectInfoNetworkContext = new();
    public bool HasPlacement = true;
    private bool TryGetRuntimeFluidEndpoints(out Vector2Int input, out Vector2Int output)
    {
        input = default;
        output = new Vector2Int { x = 7 };
        return HasPlacement;
    }
    public void Bind(SpriteRenderer renderer) => pumpArrow = renderer;
    public void Enable() => OnEnable();
    public void Resume() => OnManagedVisualsResumed();
    public void PlacementChanged() => OnPlacementRuntimeChanged();
    public void Tick() => TickManagedVisuals(0.1f);
    public bool NeedsVisuals => RequiresManagedVisualUpdate;
    public bool IsPressureBudgetReset => pressureBudgetTick == -1 && pressureBudgetLiters == 0d;
}

public class InputOutputModule
{
    public bool BaseDirty;
    public int BaseResumes, BasePlacementChanges;
    protected virtual bool RequiresManagedVisualUpdate => BaseDirty;
    protected virtual void OnEnable() { }
    protected virtual void OnManagedVisualsResumed() => BaseResumes++;
    protected virtual void OnPlacementRuntimeChanged() => BasePlacementChanges++;
    protected virtual void TickManagedVisuals(float deltaTime) => BaseDirty = false;
}

public static class Pipe
{
    public sealed class FluidNetworkSearchContext { }
    public static int FluidItemId = -1, Queries;
    public static Vector2Int LastCoordinate;
    public static bool IncludedPressure;
    public static bool TryGetNetworkFluidInfoAt(Vector2Int coordinate, FluidNetworkSearchContext context,
        bool ignored, Vector2Int ignoredCoordinate, bool includePressure,
        out int fluidItemId, out float temperature, out float pressure)
    {
        Queries++;
        LastCoordinate = coordinate;
        IncludedPressure = includePressure;
        fluidItemId = FluidItemId;
        temperature = pressure = 0f;
        return fluidItemId >= 0;
    }
    public static Color ResolveFluidDisplayColor(int id) => new Color(id, 1, 2, 1);
}

namespace UnityEngine
{
    public sealed class SerializeField : Attribute { }
    public struct Vector2Int { public int x, y; }
    public static class Time { public static float unscaledTime; }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1, 1);
    }
    public sealed class SpriteRenderer
    {
        private Color value = Color.white;
        public bool enabled = true;
        public int Writes;
        public Color color { get => value; set { this.value = value; Writes++; } }
    }
}

namespace UnityEngine.Serialization
{
    public sealed class FormerlySerializedAsAttribute : Attribute
    {
        public FormerlySerializedAsAttribute(string name) { }
    }
}
