using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y);
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
    public readonly record struct IngredientEntry(int itemId, int count);
}

public class InputOutputModule
{
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
    public bool HasRecipe = true;
    public bool IncludeSecondFluid;
    public int PersistenceMarks;
    public int WakeCalls;
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
        ingredients.Add(new CraftingTreeRuntime.IngredientEntry(1, 1));
        if (IncludeSecondFluid)
            ingredients.Add(new CraftingTreeRuntime.IngredientEntry(2, 2));
        recipeIndex = 0;
        outputItemId = 100;
        outputCount = 1;
        return HasRecipe;
    }
    private static bool IsFluidItemId(int itemId) => itemId == 1 || itemId == 2;
    private static long GetRequiredProductionFluidUnits(
        int outputItemId, CraftingTreeRuntime.IngredientEntry ingredient) =>
        DeterministicSimulationUnits.FromFloat(ingredient.count);
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
        Require(machine.PersistenceMarks == 2 && machine.WakeCalls == 2,
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
        Console.WriteLine($"Production fluid receiver checks passed: {checks}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}
