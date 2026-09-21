using System;
using System.Collections.Generic;

internal static class Checks
{
    private static int passed;

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        passed++;
    }

    public static int Main()
    {
        var definition = new ItemDefinition();
        definition.useEnergyType = ItemDefinition.EnergyType.Burn;
        definition.useEnergyAmount = 10f;
        Require(definition.UseEnergyRequirementCount == 1, "legacy requirement remains readable");
        Require(ItemDefinition.ResolveUseEnergyRatePerSecond(definition) == 10f,
            "legacy rate remains unchanged");

        definition.ReplaceUseEnergyRequirements(new[]
        {
            new ItemDefinition.EnergyUseRequirement(ItemDefinition.EnergyType.Burn, 10f, 30f),
            new ItemDefinition.EnergyUseRequirement(ItemDefinition.EnergyType.Electricity, 2f, 8f),
            new ItemDefinition.EnergyUseRequirement(ItemDefinition.EnergyType.Diesel, 4f, 20f)
        });
        Require(definition.UseEnergyRequirementCount == 3, "three requirements are retained");
        Require(definition.UsesEnergyType(ItemDefinition.EnergyType.Burn), "burn is required");
        Require(definition.UsesEnergyType(ItemDefinition.EnergyType.Electricity), "electricity is required");
        Require(definition.UsesEnergyType(ItemDefinition.EnergyType.Diesel), "diesel is required");
        Require(ItemDefinition.ResolveElectricUseWatts(definition) == 2000f,
            "electricity kW converts to watts independently");
        Require(ItemDefinition.ResolveCompleteEnergyAmount(definition) == 30f,
            "first requirement retains the task completion energy");
        Require(definition.TryGetUseEnergyRequirement(
                    1,
                    out ItemDefinition.EnergyUseRequirement secondaryRequirement)
                && secondaryRequirement.completeEnergy == 0f,
            "secondary requirement has no independent completion energy");

        var machine = new InputOutputModule(definition);
        machine.HasElectricity = true;
        machine.AvailableTypes.Add(ItemDefinition.EnergyType.Burn);
        machine.AvailableTypes.Add(ItemDefinition.EnergyType.Diesel);
        Require(machine.HasAllEnergy(), "all configured energy types permit operation");
        Require(machine.Consume(0.5f), "all configured energy types are consumed together");
        Require(machine.BurnConsumeCalls == 2 && machine.ElectricConsumeCalls == 1,
            "one tick consumes burn, diesel, and electricity");

        machine.HasElectricity = false;
        Require(!machine.HasAllEnergy(), "missing electricity blocks operation");
        machine.HasElectricity = true;
        machine.AvailableTypes.Clear();
        Require(!machine.HasAllEnergy(), "missing burn energy blocks operation");
        Require(!machine.Consume(0.5f), "preflight prevents partial consumption when one type is absent");

        machine.AvailableTypes.Add(ItemDefinition.EnergyType.Burn);
        Require(!machine.HasAllEnergy(), "missing diesel blocks operation");

        Console.WriteLine($"PASS {passed} multi-energy requirement checks (production methods extracted; no Unity launched)");
        return 0;
    }
}
