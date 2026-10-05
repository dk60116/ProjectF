using System;
using ProjectF.Simulation;
using ProjectF.Runtime;

// Engine boundaries only. Both clock layers are the production sources.
public static class FurnaceHarnessHost { public const bool IsPlaying = true; }
public static class MapObjectTickManager
{
    private static readonly SimulationTickWorld clock = new();
    public static long CurrentSimulationTick => clock.CurrentTick;
    public const float FixedSimulationDeltaSeconds = SimulationTickWorld.FixedSimulationDeltaSeconds;
    public static void RegisterUpdateTick(IMapObjectUpdateTick target) => clock.Register(target);
    public static void UnregisterUpdateTick(IMapObjectUpdateTick target) => clock.Unregister(target);
    public static void Step() => clock.Step();
    public static void SetDefaultInterval(float seconds) => clock.DefaultIntervalSeconds = seconds;
}
public class InstallationObject { }
public partial class InputOutputModule
{
    public bool IsBenchmarkWorking => ProjectF.Benchmark.BenchmarkRuntime.ForceWorking;
    public bool RequiresFacilityPowerEvaluation => false;
    public System.Collections.Generic.List<InputOutputPair> InputOutputPairs = new();
    public float CraftDurationSeconds = 5;
    public static bool IsFluidItemDefinition(ItemDefinition item) => item?.isFluid == true;
    public static bool IsInputItemBlockType(RectGridBlockType type) => type == RectGridBlockType.InputItem;
}
public static partial class UtilityPole
{
    public static void InvalidateRobotArmConsumers() { }
    public static bool TracksRuntimeElectricPowerDemand(InstallationObject value) => false;
    public static bool TryCaptureElectricPowerDemand(InstallationObject value, out float watts) { watts = 0; return false; }
    public static bool HasElectricPowerDemandChanged(bool a, float aw, bool b, float bw) => a != b || aw != bw;
    public static void NotifyElectricPowerConsumerStateChanged(InstallationObject value) { }
    public static int PowerPreparationCount;
    public static void PrepareSimulationPowerTick() { PowerPreparationCount++; }
    public static void BeginSimulationPowerMutationBatch() { }
    public static void EndSimulationPowerMutationBatch() { }
}
public static class MapObjectTickProfiler
{
    public static bool IsDetailedEnabled => false;
    public static long BeginSample() => 0;
    public static Scope SampleNamed(string a, string b, string c) => default;
    public static void AddRuntimeCounter(string a, string b, object c) { }
    public static void RecordNamedElapsedTicks(string a, string b, string c, long ticks) { }
    public readonly struct Scope : IDisposable { public void Dispose() { } }
}

public partial class ItemDefinition
{
    public float CraftingDurationSeconds = 5;
    public float CompleteEnergy;
    public static float ResolveCompleteEnergyAmount(ItemDefinition item) => item.CompleteEnergy;
    public static float ResolveUseEnergyRatePerSecond(ItemDefinition item) => item.Requirements.Count > 0 ? item.Requirements[0].useEnergyAmount : 0;
    public static bool TryGetPrimaryUseEnergyRequirement(ItemDefinition item, out EnergyUseRequirement value)
    { value = item.Requirements.Count > 0 ? item.Requirements[0] : default; return item.Requirements.Count > 0; }
}

public static partial class CraftingTreeRuntime
{
    public static bool TryGetIngredientsView(int item, out System.Collections.Generic.IReadOnlyList<IngredientEntry> inputs) { inputs = null; return false; }
    public static float GetOutputAmount(int item) => 1;
    public static int GetOutputCount(int item) => 1;
}

public partial class InputOutputModule
{
    public static Action OutputMutation;
    private static void PublishOutputMutation() => OutputMutation?.Invoke();
}

internal partial class ProductionRenderTemplate
{
    internal int InputPortCount = 1;
    internal ProductionRenderTemplate() { }
    internal ProductionRenderTemplate(InputOutputModule source, object controller) => throw new Exception("Use the asset-backed cached template");
}

public partial class ProductionWorld
{
    private ResourceStateSlots<State> states => slots;
    private readonly System.Collections.Generic.Dictionary<UnityEngine.Vector2Int, ProductionFacilityInstance> byKey = new();
    private readonly System.Collections.Generic.Dictionary<UnityEngine.Vector2Int, System.Collections.Generic.List<ProductionFacilityInstance>> cells = new();
    private bool markersDirty;
    public int MarkerCount;
    private static UnityEngine.Vector2Int Cell(UnityEngine.Vector3 position) => default;
    internal InputOutputModule CacheTemplate(ProductionRenderTemplate template, InputOutputModule source = null)
    {
        Current = this;
        var prototype = source ?? new InputOutputModule(); templates.Add(prototype, template); return prototype;
    }
}
public partial class TerrainGenerator { public object ResolveInstallationPlacementController() => null; }
public partial class Block
{
    public UnityEngine.Vector2Int Coordinate;
    // SetMapObject's final notification is a synchronous production boundary, not a visual operation.
    public void SetMapObject(IMapObjectTarget value)
    {
        MapObject = value;
        InputOutputModule.WakeRuntimeModulesAtCoordinate(Coordinate);
    }
}
public partial class InputOutputModule { public static void NotifyRuntimePipeTopologyChanged(object source) { } }
public partial class VirtualObjectWorld
{
    public static VirtualObjectWorld Ensure() => Current;
    public bool TryGetInstallationHandle(UnityEngine.Vector2Int key, out ProjectF.MapObjects.MapObjectHandle handle)
    { handle = new ProjectF.MapObjects.MapObjectHandle(100); return true; }
}
