using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y);
    public readonly record struct Vector3(float x, float y, float z);
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    }
}

public static class DeterministicSimulationUnits
{
    public static long FromFloat(float value) => (long)Math.Round(value * 1000f);
    public static float ToFloat(long value) => value / 1000f;
}

public static class CraftingTreeRuntime
{
    public readonly record struct IngredientEntry(int itemId, float amount)
    { public int count => Math.Max(1, (int)Math.Ceiling(amount)); }
}

public sealed class ItemDefinition { public int id; }

public class InputOutputModule
{
    protected virtual void TryStartNextCraft() { }
    protected static void NotifyFluidOutputCapacityIncreased(InputOutputModule source) { }
    public readonly record struct ItemIoEntry(ItemDefinition itemDefinition, float ResolvedAmount);
    public sealed class InputOutputPair { public List<ItemIoEntry> inputs; }
    public int FluidInputNotifications;
    protected static void NotifyFluidInputAvailabilityIncreased(InputOutputModule source) =>
        source.FluidInputNotifications++;
    public enum RectGridBlockType { None, InputItem, PipeInputItem, DoubleInputItem, PipeInput }
    public readonly record struct RectGridBlockPlacement(int x, int y, RectGridBlockType blockType);
    protected readonly List<RectGridBlockPlacement> placements = new();
    public IReadOnlyList<RectGridBlockPlacement> RectGridPlacements => placements;
    protected virtual void AppendDedicatedFluidStorageRuntimeCoordinates(List<UnityEngine.Vector2Int> coordinates) { }
    internal virtual bool UsesDedicatedFluidStorageAtRuntimeCoordinate(UnityEngine.Vector2Int coordinate) => false;
    internal virtual float GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(UnityEngine.Vector2Int coordinate) => 0f;
    internal virtual float GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(UnityEngine.Vector2Int coordinate, int itemId) => 0f;
    internal virtual float GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(UnityEngine.Vector2Int coordinate) => 0f;
    internal virtual float GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(UnityEngine.Vector2Int coordinate, int itemId) => 0f;
    internal virtual bool CanAcceptDedicatedFluidAtRuntimeCoordinate(UnityEngine.Vector2Int coordinate, int itemId, float liters) => false;
    internal virtual bool TryAddDedicatedFluidAtRuntimeCoordinate(UnityEngine.Vector2Int coordinate, int itemId,
        float liters, float temperature, out float accepted) { accepted = 0f; return false; }
    protected bool TryGetPlacementRuntime(out UnityEngine.Vector2Int anchor, out int quarterTurns)
    {
        anchor = default;
        quarterTurns = 0;
        return true;
    }
    protected static bool IsInputItemBlockType(RectGridBlockType blockType) =>
        blockType is RectGridBlockType.InputItem or RectGridBlockType.PipeInputItem or RectGridBlockType.DoubleInputItem;
    protected static bool AllowsPipeAreaInteraction(RectGridBlockType blockType) =>
        blockType is RectGridBlockType.PipeInputItem or RectGridBlockType.DoubleInputItem or RectGridBlockType.PipeInput;
    protected bool TryGetRectGridPlacementCoordinate(InputOutputModule source, UnityEngine.Vector2Int anchor,
        int turns, RectGridBlockPlacement placement, out UnityEngine.Vector2Int coordinate)
    {
        coordinate = new UnityEngine.Vector2Int(placement.x, placement.y);
        return true;
    }
    protected bool TryGetRectGridBlockTypeAtCoordinate(InputOutputModule source, UnityEngine.Vector2Int anchor,
        int turns, UnityEngine.Vector2Int coordinate, out RectGridBlockType blockType)
    {
        foreach (var placement in placements)
        {
            if (placement.x == coordinate.x && placement.y == coordinate.y)
            {
                blockType = placement.blockType;
                return true;
            }
        }
        blockType = RectGridBlockType.None;
        return false;
    }
    protected bool TryGetRuntimePipeAreaExternalDirection(UnityEngine.Vector2Int coordinate,
        out UnityEngine.Vector2Int direction)
    {
        direction = new UnityEngine.Vector2Int(1, 0);
        return TryGetRectGridBlockTypeAtCoordinate(this, default, 0, coordinate, out var blockType)
               && AllowsPipeAreaInteraction(blockType);
    }
}

public partial class ProductionMachine
{
    private readonly Dictionary<int, long> productionFluidUnits = new();
    private readonly List<CraftingTreeRuntime.IngredientEntry> resolvedProductionIngredients = new();
    private readonly List<UnityEngine.Vector2Int> resolvedProductionInputCoordinates = new();
    private readonly HashSet<UnityEngine.Vector2Int> resolvedProductionInputCoordinateSet = new();
    private readonly List<UnityEngine.Vector2Int> productionFluidInputCoordinates = new();
    public bool HasRecipe = true;
    public bool IncludeSecondFluid;
    public bool FluidOutput;
    public float FirstInputAmount = 1f, SecondInputAmount = 2f;
    public int PersistenceMarks;
    public int WakeCalls;
    public int CraftStarts, StartEnergyChecks;
    public bool StartEnergyAvailable = true;
    public bool CraftActive;
    private bool IsActiveCraftRunning => CraftActive;
    public int PressureQueries, SourceTransfers;
    public void Pull(float dt) => PullProductionFluidIngredients(dt);
    private void CollectProductionFluidInputCoordinates()
    {
        productionFluidInputCoordinates.Clear();
        foreach(var placement in placements)
            productionFluidInputCoordinates.Add(new(placement.x,placement.y));
    }
    private bool TryGetRuntimeFluidInputPressure(UnityEngine.Vector2Int coordinate, int id, out float rate)
    { PressureQueries++; rate=100; return true; }
    private bool TryConsumeConnectedFluidInputAtCoordinate(UnityEngine.Vector2Int coordinate, int id, float requested,
        out float consumed, out float temperature) { SourceTransfers++; consumed=requested; temperature=20; return true; }
    private bool HasRuntimeOutputCoordinates => true;
    private float InputConsumeMoveInterval => .1f;
    private bool TryEnsureCraftStartEnergy(ItemDefinition definition) { StartEnergyChecks++; return StartEnergyAvailable; }
    private UnityEngine.Vector3 ResolveConsumeTargetWorldPosition() => default;
    private int ConsumeRuntimeInputAreaCenterObjects(UnityEngine.Vector2Int coordinate, int itemId, int count,
        UnityEngine.Vector3 position, float interval) => count;
    private bool TryResolveRuntimeInputItemBlock(int itemId, int count, ISet<UnityEngine.Vector2Int> excluded,
        out object block, out UnityEngine.Vector2Int coordinate) { block=null; coordinate=default; return false; }
    private void BeginActiveCraft(int index, int itemId, int count, ItemDefinition definition)
    { CraftStarts++; CraftActive=true; }
    public void TryStart() => TryStartNextCraft();
    public void AddPort(UnityEngine.Vector2Int coordinate, RectGridBlockType blockType = RectGridBlockType.DoubleInputItem) =>
        placements.Add(new RectGridBlockPlacement(coordinate.x, coordinate.y, blockType));
    public List<UnityEngine.Vector2Int> CollectPorts()
    {
        var coordinates = new List<UnityEngine.Vector2Int>();
        AppendDedicatedFluidStorageRuntimeCoordinates(coordinates);
        return coordinates;
    }
    public long Stored(int itemId) => GetProductionFluidUnits(itemId);

    private bool TryResolveSelectedProductionRecipe(
        List<CraftingTreeRuntime.IngredientEntry> ingredients,
        out int recipeIndex, out int outputItemId, out int outputCount)
    {
        ingredients.Clear();
        ingredients.Add(new CraftingTreeRuntime.IngredientEntry(1, FirstInputAmount));
        if (IncludeSecondFluid)
            ingredients.Add(new CraftingTreeRuntime.IngredientEntry(2, SecondInputAmount));
        recipeIndex = 0;
        outputItemId = FluidOutput ? 3 : 100;
        outputCount = 1;
        return HasRecipe;
    }
    private static bool IsFluidItemId(int itemId) => itemId == 1 || itemId == 2 || itemId == 3;
    private int ResolveProductionTargetPairIndex(int itemId) => 0;
    private bool TryGetInputOutputPair(int index, out InputOutputPair pair) { pair=null; return false; }
    private ItemDefinition ResolveInstalledDefinition() => new();
    private float ResolveInitialCraftDuration(ItemDefinition definition) => 36;
    private long GetProductionFluidUnits(int fluidItemId) =>
        productionFluidUnits.TryGetValue(fluidItemId, out long units) ? units : 0L;
    private void MarkPersistenceStateDirty() => PersistenceMarks++;
    private void WakeRuntimeUpdate() => WakeCalls++;
}

public static class ReceiverChecks
{
    private static int checks;
    public static void Main()
    {
        var machine = new ProductionMachine();
        var port = new UnityEngine.Vector2Int(2, 3);
        var other = new UnityEngine.Vector2Int(9, 9);
        machine.AddPort(port);
        var pass = new UnityEngine.Vector2Int(3, 3);
        machine.AddPort(pass, InputOutputModule.RectGridBlockType.PipeInput);
        machine.AddPort(new UnityEngine.Vector2Int(4, 3), InputOutputModule.RectGridBlockType.InputItem);
        var registeredPorts = machine.CollectPorts();
        Require(registeredPorts.Count == 2 && registeredPorts.Contains(port) && registeredPorts.Contains(pass),
            "DoubleInput and PipeInput must register as fluid receivers, while a direct item input must not");
        Require(machine.CanAcceptDedicatedFluidAtRuntimeCoordinate(port, 1, 0.35f),
            "a selected fluid ingredient must be accepted at DoubleInput");
        Require(!machine.CanAcceptDedicatedFluidAtRuntimeCoordinate(other, 1, 0.35f),
            "an unrelated cell must not accept fluid");
        Require(!machine.CanAcceptDedicatedFluidAtRuntimeCoordinate(port, 2, 0.35f),
            "a fluid absent from the selected recipe must be rejected");
        Require(machine.TryAddDedicatedFluidAtRuntimeCoordinate(port, 1, 0.35f, 20f, out float accepted)
                && Math.Abs(accepted - 0.35f) < 0.0001f && machine.Stored(1) == 350L,
            "accepted fluid must enter the ingredient buffer");
        Require(Math.Abs(machine.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port) - 0.65f) < 0.0001f,
            "the receiver must expose only the remaining recipe quota");
        Require(Math.Abs(machine.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port, 1) - 0.65f) < 0.0001f
                && Math.Abs(machine.GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(port, 1) - 0.35f) < 0.0001f,
            "source routing must see this ingredient's own capacity and fill ratio");
        Require(machine.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port, 2) == 0f,
            "a different fluid must expose no receiver capacity");
        Require(machine.TryAddDedicatedFluidAtRuntimeCoordinate(port, 1, 0.9f, 20f, out accepted)
                && Math.Abs(accepted - 0.65f) < 0.0001f && machine.Stored(1) == 1000L,
            "incoming fluid must stop at the recipe quota");
        Require(!machine.CanAcceptDedicatedFluidAtRuntimeCoordinate(port, 1, 0.001f)
                && machine.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port) == 0f,
            "a full ingredient buffer must block further input");
        Require(machine.PersistenceMarks == 2 && machine.WakeCalls == 2 && machine.FluidInputNotifications == 2,
            "both transfers must persist and wake the machine");
        machine.IncludeSecondFluid = true;
        Require(machine.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port, 1) == 0f
                && machine.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port, 2) == 2f,
            "one full ingredient must not hide another fluid ingredient's space");
        Require(machine.TryAddDedicatedFluidAtRuntimeCoordinate(port, 2, 0.5f, 20f, out accepted)
                && accepted == 0.5f && machine.Stored(2) == 500L,
            "each fluid ingredient must keep a separate buffer");
        machine.HasRecipe = false;
        Require(!machine.CanAcceptDedicatedFluidAtRuntimeCoordinate(port, 1, 0.001f),
            "without a selected recipe, the receiver must remain closed");
        var fluidMaker = new ProductionMachine { FluidOutput=true, IncludeSecondFluid=true };
        fluidMaker.AddPort(port);
        Require(fluidMaker.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port,2)==72,
            "a 36 second fluid recipe accepts its full 2 L/s times 36 second input batch");
        Require(fluidMaker.TryAddDedicatedFluidAtRuntimeCoordinate(port,2,18,20,out accepted)
                && accepted==18 && fluidMaker.GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(port,2)==.25f,
            "fluid receiver fill uses the same 72 L denominator as the gauge");
        Require(fluidMaker.TryAddDedicatedFluidAtRuntimeCoordinate(port,2,100,20,out accepted)
                && accepted==54 && fluidMaker.Stored(2)==72000,
            "fluid intake caps at the exact whole-batch requirement");
        Require(!fluidMaker.CanAcceptDedicatedFluidAtRuntimeCoordinate(port,2,.001f),
            "complete fluid input batch blocks additional intake");
        var gate = new ProductionMachine { FluidOutput=true, IncludeSecondFluid=true };
        gate.AddPort(port);
        gate.TryStart();
        Require(gate.CraftStarts==0 && gate.StartEnergyChecks==0,"empty fluids cannot start or reserve start energy");
        gate.TryAddDedicatedFluidAtRuntimeCoordinate(port,1,36,20,out _);
        gate.TryAddDedicatedFluidAtRuntimeCoordinate(port,2,71.999f,20,out _);
        gate.TryStart();
        Require(gate.CraftStarts==0 && gate.Stored(1)==36000 && gate.Stored(2)==71999,
            "all fluid ingredients must be full; 71.999 of 72 L cannot start or consume other inputs");
        gate.TryAddDedicatedFluidAtRuntimeCoordinate(port,2,.001f,20,out _);
        gate.StartEnergyAvailable=false; gate.TryStart();
        Require(gate.CraftStarts==0 && gate.Stored(2)==72000,"full input remains stored when start energy is unavailable");
        gate.StartEnergyAvailable=true; gate.TryStart();
        Require(gate.CraftStarts==1 && gate.Stored(1)==0 && gate.Stored(2)==0,
            "exactly full fluids start once and are entirely committed at the start");
        gate.TryStart();
        Require(gate.CraftStarts==1,"consumed empty buffers cannot begin a second craft");
        Require(gate.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port)==0
                && gate.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port,2)==0
                && !gate.CanAcceptDedicatedFluidAtRuntimeCoordinate(port,2,1),
            "an active craft advertises no input capacity to sending pipes");
        Require(!gate.TryAddDedicatedFluidAtRuntimeCoordinate(port,2,1,20,out accepted) && accepted==0,
            "pipe-driven input rejects fluid throughout crafting and output waiting");
        gate.Pull(.1f);
        Require(gate.PressureQueries==0 && gate.SourceTransfers==0 && gate.Stored(2)==0,
            "active production cannot pull the next batch from its upstream source");
        gate.CraftActive=false; gate.Pull(.1f);
        Require(gate.SourceTransfers==2 && gate.Stored(1)==10000 && gate.Stored(2)==10000,
            "after output completion the next receiving phase can pull both fluids again");
        var fractional = new ProductionMachine { FluidOutput=true, IncludeSecondFluid=true, FirstInputAmount=.25f, SecondInputAmount=.5f };
        fractional.AddPort(port);
        Require(fractional.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port,1)==9f
            && fractional.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(port,2)==18f,
            "fractional input rates keep exact 36 second batch capacities");
        fractional.TryAddDedicatedFluidAtRuntimeCoordinate(port,1,9f,20,out _);
        fractional.TryAddDedicatedFluidAtRuntimeCoordinate(port,2,17.999f,20,out _);
        fractional.TryStart();
        Require(fractional.CraftStarts==0, "fractional batch cannot start below full quota");
        fractional.TryAddDedicatedFluidAtRuntimeCoordinate(port,2,.001f,20,out _);
        fractional.TryStart();
        Require(fractional.CraftStarts==1 && fractional.Stored(1)==0 && fractional.Stored(2)==0,
            "full fractional batch consumes exactly 9 and 18 L");
        Console.WriteLine($"Production fluid receiver checks passed: {checks}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}
