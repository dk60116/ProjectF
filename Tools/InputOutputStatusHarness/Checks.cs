using System;
using System.Collections.Generic;

public sealed class ItemDefinition { }
public class InstallationObject { }
public partial class InputOutputModule : InstallationObject
{
    public sealed class InputOutputPair { }
    protected bool hasActiveCraft, waitingForOutput;
    protected readonly List<int> runtimeOutputCoordinates = new() { 1 };
    public bool RecipeValid = true, TargetSelected = true, InputsReady;
    public bool InputAreaPresent = true, EnergyAvailable = true, MachinePresent = true;
    public int EnergyQueries;
    protected bool IsBenchmarkWorking => false;
    public float UseAmount = 100000f;
    public bool HasRuntimeOutputCoordinates => runtimeOutputCoordinates.Count > 0;
    protected ItemDefinition ResolveInstalledDefinition() => MachinePresent ? new() : null;
    protected bool HasOperationalEnergyAvailable(ItemDefinition definition)
    { EnergyQueries++; return EnergyAvailable; }
    public bool TryGetElectricPowerRequirement(out float watts)
    { watts = MachinePresent ? UseAmount : 0; return watts > 0; }
    protected int GetEffectiveRecipeCount() => RecipeValid ? 1 : 0;
    protected bool TryGetRecipePair(int index, out int input, out int count, out int output, out int amount)
    { input = 1; count = 1; output = 2; amount = 1; return RecipeValid; }
    protected bool IsRecipeOutputAvailable(int id) => TargetSelected;
    protected bool TryGetInputOutputPair(int index, out InputOutputPair pair)
    { pair = new(); return RecipeValid; }
    protected bool HasAllRecipeInputs(InputOutputPair pair, out bool missingArea)
    { missingArea = !InputAreaPresent; return InputsReady && InputAreaPresent; }
    public void SetCraft(bool active, bool outputWaiting = false)
    { hasActiveCraft = active; waitingForOutput = outputWaiting; }
    public void RemoveOutputArea() => runtimeOutputCoordinates.Clear();
}
public static class CraftingTreeRuntime
{
    public readonly record struct IngredientEntry(int itemId);
}
public partial class ProductionMachine : InputOutputModule
{
    public bool FluidInput, FluidOutput, Draining;
    protected bool IsActiveCraftRunning => hasActiveCraft;
    protected bool IsWaitingForOutput => waitingForOutput;
    protected int ActiveOutputItemId => FluidOutput ? 2 : 3;
    private readonly List<CraftingTreeRuntime.IngredientEntry> resolvedProductionIngredients = new();
    private bool ShouldKeepRuntimeUpdateTickActive() => Draining;
    private bool IsFluidItemId(int id) => id == 2 || id == 1 && FluidInput;
    private int ResolveSelectedProductionTargetItemId() => TargetSelected ? 2 : -1;
    private bool TryResolveSelectedProductionRecipe(List<CraftingTreeRuntime.IngredientEntry> list,
        out int pair, out int item, out int count)
    { list.Clear(); list.Add(new(1)); pair = 0; item = 2; count = 1; return TargetSelected && RecipeValid; }
    private long GetProductionFluidUnits(int id) => InputsReady ? 100 : 0;
    private long GetRequiredProductionFluidUnits(int output, CraftingTreeRuntime.IngredientEntry input) => 100;
    private bool HasProductionFluidInputPort() => InputAreaPresent;
    private bool HasRuntimeInputItemArea(int id) => InputAreaPresent;
    private bool TryResolveProductionIngredientBlocks(int output, List<CraftingTreeRuntime.IngredientEntry> list)
        => InputsReady && InputAreaPresent;
}
public static class Mathf
{
    public static float Max(float a, float b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static float Clamp01(float v) => Math.Clamp(v, 0, 1);
}
public static partial class UtilityPole
{
    private const float EnergyEpsilon = .0001f;
    public sealed class ElectricNetwork
    {
        public bool HasPowerSource = true;
        public float ProductionWatts = 100000f, RequiredWatts = 100000f, SupplyRatio = 1f;
    }
    public static ElectricNetwork Network = new();
    public static bool FreeEnergy;
    private static bool TryGetElectricPowerRequirement(InstallationObject consumer, out float watts)
    { watts = 0; return consumer is InputOutputModule module && module.TryGetElectricPowerRequirement(out watts); }
    private static bool IsFreeElectroEnergyEnabled() => FreeEnergy;
    private static void EnsureNetworksEvaluated() { }
    private static ElectricNetwork ResolveBestNetworkForConsumer(InstallationObject consumer) => Network;
}
public readonly record struct Color(string Name);
public partial class ItemInfoDescription
{
    private const int defaultStatusLineIndex = 0;
    private static readonly Color ProducingSignColor = new("Green");
    private static readonly Color WarningSignColor = new("Yellow");
    private static readonly Color StoppedSignColor = new("Red");
    public Color Sign;
    public string Text;
    private void SetDefaultText(int line, string text, bool visible) => Text = text;
    private void SetDefaultSign(int line, bool visible, Color color) => Sign = color;
    public void Show(InputOutputModule module)
    { module.GetObjectInfoStatus(out string text, out bool working); SetDefaultStatus(text, working); }
    public void Show(string text, bool working, bool warning) => SetDefaultStatus(text, working, warning);
}
public static class Checks
{
    private static int checks;
    private static void Equal<T>(T actual, T expected, string message)
    { if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new Exception($"{message}: {actual} != {expected}"); checks++; }
    private static void Verify(InputOutputModule machine, string text, string color, float demand)
    {
        var panel = new ItemInfoDescription();
        panel.Show(machine);
        Equal(panel.Text, text, "status text");
        Equal(panel.Sign.Name, color, "status light");
        int queries = machine.EnergyQueries;
        Equal(machine.TryGetElectricPowerDemand(out float watts) ? watts : 0, demand, "power demand");
        Equal(machine.EnergyQueries, queries, "demand cannot query electric supply recursively");
        UtilityPole.TryGetElectricPowerInfo(machine, out float supplied, out float required);
        Equal(required, demand, "panel demand matches simulation demand");
        Equal(supplied, Math.Min(demand, UtilityPole.Network.ProductionWatts), "actual supply");
    }
    public static void Main()
    {
        foreach (InputOutputModule machine in new InputOutputModule[] { new(), new ProductionMachine() })
        {
            Verify(machine, "Waiting for input item", "Yellow", machine.UseAmount);
            machine.InputsReady = true;
            Verify(machine, "Working", "Green", machine.UseAmount);
            machine.SetCraft(true, true);
            Verify(machine, "Waiting for output", "Yellow", machine.UseAmount);
            machine.EnergyAvailable = false;
            UtilityPole.Network.ProductionWatts = 0;
            Verify(machine, "No energy", "Red", machine.UseAmount);
            machine.SetCraft(false);
            machine.InputsReady = false;
            Verify(machine, "No energy", "Red", machine.UseAmount);
            machine.TargetSelected = false;
            Verify(machine, "No target", "Red", 0);
            machine.EnergyAvailable = true;
            UtilityPole.Network.ProductionWatts = 100000;
            Verify(machine, "No target", "Red", 0);
            machine.TargetSelected = true;
            Verify(machine, "Waiting for input item", "Yellow", machine.UseAmount);
        }
        var fluid = new ProductionMachine { FluidInput = true, FluidOutput = true };
        Verify(fluid, "Waiting for input fluid", "Yellow", fluid.UseAmount);
        fluid.SetCraft(true, true);
        fluid.Draining = true;
        Verify(fluid, "Outputting", "Green", fluid.UseAmount);
        fluid.TargetSelected = false;
        Verify(fluid, "Outputting", "Green", fluid.UseAmount); // locked batch still owns its target
        fluid.SetCraft(false);
        Verify(fluid, "No target", "Red", 0);
        var invalid = new ProductionMachine { RecipeValid = false };
        Verify(invalid, "No recipe", "Red", 0);
        var unplaced = new ProductionMachine();
        unplaced.RemoveOutputArea();
        Verify(unplaced, "No output area", "Red", 0);
        var panel = new ItemInfoDescription();
        panel.Show("No power", false, true);
        Equal(panel.Sign.Name, "Red", "legacy warning cannot make missing power yellow");
        panel.Show("Waiting for water", false, false);
        Equal(panel.Sign.Name, "Yellow", "fluid input waits are yellow");
        UtilityPole.FreeEnergy = true;
        UtilityPole.TryGetElectricPowerInfo(fluid, out float freeSupplied, out _);
        Equal(freeSupplied, 0f, "free electricity cannot consume without a target");
        Console.WriteLine($"IOModule status/power: {checks} checks passed.");
    }
}
