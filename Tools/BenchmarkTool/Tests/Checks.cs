using ProjectF.Benchmark;
using ProjectF.Simulation;
using UnityEngine;

// Scene IO is a boundary double; clocks, benchmark adapters, layout and commands
// are compiled from their production sources by RunChecks.ps1.
namespace UnityEngine
{
    public struct Vector3 { }
    public class Transform { public Vector3 position; }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int RoundToInt(float value) => (int)Math.Round(value);
    }
}
namespace ProjectF.Simulation
{
    public static class SimulationTickWorld
    {
        public const int DefaultSimulationTicksPerSecond = 60;
        public const float FixedSimulationDeltaSeconds = 1f / 60;
    }
}
namespace ProjectF.Benchmark
{
    public static class BenchmarkRuntime
    {
        public static bool ForceWorking = true;
        public static int FallbackItemId = 10, Produced;
        public static float Spill;
        internal static void RecordItems(int count) => Produced += count;
        internal static void RecordSpill(float liters) => Spill += Math.Max(0, liters);
        internal static bool EmitItem(TerrainGenerator terrain, int itemId, Vector3 position) { Produced++; return true; }
    }
}
public sealed class ItemDefinition
{
    public int id;
    public bool Powered = true;
    public float Rate = 60, Duration = 2;
    public static float ResolveUseEnergyRatePerSecond(ItemDefinition item) => item.Rate;
    public static bool IsElectricityItemDefinition(ItemDefinition item) => item?.id == 99;
}
public class TerrainGenerator { public static TerrainGenerator ResolveActive() => new(); }
public static class MapClimate { public const float DefaultCurrentTemperatureCelsius = 20; }
public readonly record struct OutputEntry(ItemDefinition itemDefinition, float ResolvedAmount)
{
    public int ResolvedItemCount => Math.Max(1, (int)Math.Round(ResolvedAmount));
}
public partial class InputOutputModule
{
    public ItemDefinition Definition = new();
    public readonly List<OutputEntry> OutputList = new();
    public Transform transform = new();
    private ProductionProcess production = ProductionProcess.Empty;
    private bool hasActiveCraft => production.Active;
    private bool runtimeSleeping;
    private float lastOperationalEnergySupplyRatio;
    protected int ActiveOutputItemId => production.OutputItemId;
    protected int ActiveRecipeIndex => production.RecipeIndex;
    protected int ActiveOutputCount => production.OutputCount;
    public bool BlockOutput;
    public int OutputCount, OutputItem = -1, VisualDirty;
    public float FluidAcceptedLimit = float.PositiveInfinity;
    public readonly Dictionary<int, float> Fluids = new();
    public void Tick(float delta) => ApplyBenchmarkWork(delta);
    public ProductionProcess State => production;
    protected ItemDefinition ResolveInstalledDefinition() => Definition;
    protected static bool RequiresOperationalEnergy(ItemDefinition definition) => definition.Powered;
    protected virtual float ResolveCompleteEnergy(ItemDefinition definition) => definition.Rate * definition.Duration;
    protected virtual float ResolveInitialCraftDuration(ItemDefinition definition, int itemId = -1) => definition.Duration;
    protected void BeginActiveCraft(int index, int itemId, int count, ItemDefinition definition)
        => production.Begin(index, itemId, count, definition.Powered ? 0 : DeterministicSimulationUnits.SecondsToTicks(definition.Duration));
    protected void ClearActiveCraft() => production.Clear();
    protected void MarkManagedRuntimeVisualsDirty() => VisualDirty++;
    protected void WakeRuntimeUpdate() { }
    protected void SetRuntimeSleeping(bool value) => runtimeSleeping = value;
    protected static ItemDefinition ResolveItemDefinition(int id) => new() { id = id };
    protected static bool IsFluidItemId(int id) => id is >= 20 and < 30;
    protected bool TryEmitOutputItems(int id, int count, Vector3 position)
    { if (BlockOutput) return false; OutputItem = id; OutputCount += count; return true; }
    protected void TryEmitFluidOutputToConnectedStorages(int id, float liters, float temperature, out float accepted)
    { accepted = Math.Min(liters, FluidAcceptedLimit); Fluids[id] = Fluids.GetValueOrDefault(id) + accepted; }
}
public class BoxObject : InputOutputModule { }
public class Pump : InputOutputModule { }
public class Sprinkler : InputOutputModule { }
public class SeedPlanter : InputOutputModule { }
public class ProductionMachine : InputOutputModule
{
    public int Selected = -1;
    public bool IsProductionTargetSelected(int id) => id == Selected;
}
public class SteamGenerator : InputOutputModule
{
    public bool Generating;
    public void SetBenchmarkGeneration(bool enabled) => Generating = enabled;
}
public class CrudeOilRefinery : InputOutputModule { }
public partial class LoggingMachine
{
    public object activeTree;
    private long consumedWorkEnergyUnits;
    private bool hasElectricDemand, electricPowerBlocked;
    public readonly ItemDefinition Definition = new();
    public readonly Transform transform = new();
    public bool Working;
    public int Turns;
    public void Tick(float delta) => ApplyBenchmarkWork(delta);
    private void SetWorking(bool value) => Working = value;
    private void WakeRuntimeTick() { }
    private void UpdateHingeRotation(float delta) { }
    private void AdvanceDirection() => Turns++;
    private ItemDefinition ResolveLoggingDefinition() => Definition;
    private float ResolveRequiredWorkEnergy() => Definition.Duration * Definition.Rate;
}

internal static class Checks
{
    private static int assertions;
    private static void Require(bool pass, string label)
    { assertions++; if (!pass) throw new Exception(label); }
    private static void Near(float actual, float expected, string label) => Require(Math.Abs(actual - expected) < .0001f, label);
    private static BenchmarkCommand Parse(string text)
    { Require(BenchmarkCommand.TryParse(text.Split(' '), out var command, out _), "parse " + text); return command; }
    public static void Main()
    {
        Require(BenchmarkLayout.BeltCount(1) == 8, "first ring");
        Require(BenchmarkLayout.BeltCount(10) == 440, "10 rings");
        Require(BenchmarkLayout.BeltCount(1000) == 4004000, "maximum rings");
        var cells = new HashSet<(int, int)>();
        var variants = new HashSet<(int, int, int, int)>();
        for (int ring = 1; ring <= 64; ring++)
        {
            int turns = 0, length = BenchmarkLayout.RingLength(ring);
            for (int i = 0; i < length; i++)
            {
                var cell = BenchmarkLayout.RingCell(ring, i);
                var previous = BenchmarkLayout.RingCell(ring, (i + length - 1) % length);
                var next = BenchmarkLayout.RingCell(ring, (i + 1) % length);
                Require(cells.Add((cell.X, cell.Y)), "ring cells never overlap");
                Require(Math.Max(Math.Abs(cell.X), Math.Abs(cell.Y)) == ring, "touching concentric square");
                Require(Math.Abs(cell.X - next.X) + Math.Abs(cell.Y - next.Y) == 1, "closed directed adjacent loop");
                int ix = previous.X - cell.X, iy = previous.Y - cell.Y;
                int ox = next.X - cell.X, oy = next.Y - cell.Y;
                Require(ix != ox || iy != oy, "no reversal");
                if (ix * ox + iy * oy == 0) turns++;
                variants.Add((ix, iy, ox, oy));
            }
            Require(turns == 4, "four right angle turns");
            Require(cells.Count == BenchmarkLayout.BeltCount(ring), "preview equals actual placements");
        }
        Require(variants.Count == 8, "four straight and four corner variants");
        for (int y = -64; y <= 64; y++)
            for (int x = -64; x <= 64; x++)
                Require(cells.Contains((x, y)) == (x != 0 || y != 0), "no gaps between belt rings");
        foreach (double percent in new[] { 0, .1, 12.5, 33.3, 50, 99.9, 100 })
        {
            long slots = 0, filled = 0;
            for (int i = 0; i < 997; i++)
            {
                int capacity = i % 5 + 1;
                int count = BenchmarkLayout.FillForBelt(slots, capacity, percent);
                Require(count >= 0 && count <= capacity, "slot fill bounded");
                slots += capacity; filled += count;
            }
            Require(filled == (long)Math.Floor(slots * percent / 100), "global fill exact including fractional percent");
        }
        foreach (int count in new[] { 1, 2, 9, 10, 100, 1000000 })
        {
            int columns = BenchmarkLayout.GridColumns(count);
            Require(columns * columns >= count && (columns - 1) * (columns - 1) < count, "grid dimensions cover N");
        }
        foreach (string action in new[] { "map", "clearitems", "clearobjects", "cancel", "catalog", "status" }) Parse("benchmark " + action);
        Require(Parse("benchmark belts auto 1000").Count == 1000, "auto belt");
        Require(Parse("benchmark spawn 123 1000000").ItemId == 123, "item spawn");
        Require(Parse("benchmark fill 10 12.5").Percent == 12.5, "decimal percent");
        var randomFill = Parse("benchmark fill random 12.5");
        Require(randomFill.Action == BenchmarkAction.FillRandom && randomFill.ItemId == -1 && randomFill.Percent == 12.5, "random mixed fill decimal percent");
        Require(Parse("benchmark fill RANDOM 100").Action == BenchmarkAction.FillRandom, "random fill case insensitive");
        Require(Parse("benchmark force 1 10").Enabled && !Parse("benchmark force 0 10").Enabled, "force toggle");
        Require(!Parse("benchmark force 0 -1").Enabled, "disable force without current item selection");
        foreach (string invalid in new[] { "", "benchmark", "benchmark map extra", "benchmark foo", "benchmark belts auto 0", "benchmark belts auto 1001",
            "benchmark spawn -1 1", "benchmark spawn 1 1000001", "benchmark force 2 1", "benchmark force 1 -1", "benchmark fill 1 NaN",
            "benchmark fill 1 Infinity", "benchmark fill 1 -1", "benchmark fill 1 101", "benchmark fill 1 1,5",
            "benchmark fill random NaN", "benchmark fill random Infinity", "benchmark fill random -1", "benchmark fill random 101",
            "benchmark fill random 1,5", "benchmark fill random", "benchmark fill random 20 extra", "benchmark spawn random 10" })
            Require(!BenchmarkCommand.TryParse(invalid.Split(' '), out _, out _), "reject invalid " + invalid);
        CheckProduction();

        Console.WriteLine($"PASS: {assertions} benchmark layout, protocol and forced production checks");
    }
    private static void Step(InputOutputModule module, int ticks)
    { for (int i = 0; i < ticks; i++) module.Tick(1f / 60); }
    private static void CheckProduction()
    {
        BenchmarkRuntime.Produced = 0; BenchmarkRuntime.Spill = 0;
        var powered = new ProductionMachine();
        powered.OutputList.Add(new(new() { id = 11 }, 2));
        Step(powered, 119);
        Require(powered.OutputCount == 0 && powered.State.Active, "powered machine waits full craft duration");
        Step(powered, 1); Require(powered.OutputCount == 2, "full power completes at 120 ticks");
        Step(powered, 120); Require(powered.OutputCount == 4, "subsequent cycle restarts");
        powered.ResetBenchmarkWork(); Require(powered.State.ConsumedEnergyUnits == 0, "toggle resets partial work");
        var selected = new ProductionMachine { Selected = 12 };
        selected.OutputList.Add(new(new() { id = 11 }, 1)); selected.OutputList.Add(new(new() { id = 12 }, 3));
        Step(selected, 120); Require(selected.OutputItem == 12 && selected.OutputCount == 3, "selected recipe respected");
        selected.Selected = 11;
        Step(selected, 120); Step(selected, 120);
        Require(selected.OutputItem == 11, "recipe selection change does not use stale benchmark cache");
        var timed = new InputOutputModule { Definition = new() { Powered = false, Duration = .5f } };
        Step(timed, 29); Require(timed.OutputCount == 0, "unpowered duration");
        Step(timed, 1); Require(timed.OutputCount == 1 && timed.OutputItem == 10, "virtual resource fallback");
        int old = BenchmarkRuntime.Produced;
        var blocked = new InputOutputModule { BlockOutput = true };
        blocked.OutputList.Add(new(new() { id = 11 }, 2));
        Step(blocked, 120); Require(BenchmarkRuntime.Produced == old + 2 && !blocked.State.WaitingForOutput, "blocked output spills real floor items and keeps working");
        var fluid = new ProductionMachine { FluidAcceptedLimit = 1 };
        fluid.OutputList.Add(new(new() { id = 20 }, .5f));
        Step(fluid, 120); Near(fluid.Fluids[20], 1, "L/s times duration"); Near(BenchmarkRuntime.Spill, 0, "fractional fluid no overproduction");
        var refinery = new CrudeOilRefinery { FluidAcceptedLimit = 1 };
        refinery.Definition.Duration = 5;
        refinery.OutputList.Add(new(new() { id = 20 }, 1)); refinery.OutputList.Add(new(new() { id = 21 }, 1)); refinery.OutputList.Add(new(new() { id = 22 }, 2));
        Step(refinery, 300); Require(refinery.Fluids.Count == 3, "refinery produces all products"); Near(BenchmarkRuntime.Spill, 17, "spill reported separately from delivered fluid");
        var generator = new SteamGenerator(); generator.OutputList.Add(new(new() { id = 99 }, 1));
        Step(generator, 120); Require(generator.Generating && generator.OutputCount == 0, "generator activates electricity without floor item");
        generator.ResetBenchmarkWork(); Require(!generator.Generating, "force off stops benchmark generation");
        Require(!new BoxObject().IsBenchmarkWorking, "passive storage excluded");
        BenchmarkRuntime.ForceWorking = false; Require(!powered.IsBenchmarkWorking, "normal mode restored"); BenchmarkRuntime.ForceWorking = true;
        var zero = new InputOutputModule(); zero.Tick(0); Require(!zero.State.Active, "zero time no work");
        var logger = new LoggingMachine(); old = BenchmarkRuntime.Produced;
        for (int i = 0; i < 119; i++) logger.Tick(1f / 60);
        Require(BenchmarkRuntime.Produced == old && logger.Working, "logger assumes tree but waits work budget");
        logger.Tick(1f / 60); Require(BenchmarkRuntime.Produced == old + 1 && logger.Turns == 1, "logger harvests and rotates");
        logger.ResetBenchmarkWork(); Require(!logger.Working, "logger force state cleared");
        old = BenchmarkRuntime.Produced;
        foreach (var consumer in new InputOutputModule[] { new Pump(), new Sprinkler(), new SeedPlanter() })
        { Step(consumer, 120); Require(consumer.State.Active, "service machine stays active"); }
        Require(BenchmarkRuntime.Produced == old, "service machines never invent manufactured items");
    }
}
