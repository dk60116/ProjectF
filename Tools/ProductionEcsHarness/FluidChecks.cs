using System;
using UnityEngine;

static class FluidChecks
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    static long Units(float liters) => DeterministicSimulationUnits.FromFloat(liters);
    static ProductionFacilityInstance Create(ProductionWorld world, Vector2Int port, bool output, Vector2Int direction)
    {
        var prototype = new ProductionMachine();
        prototype.PortTypes[port] = output ? InputOutputModule.RectGridBlockType.PipeOutputItem : InputOutputModule.RectGridBlockType.PipeInputItem;
        prototype.ExternalDirections[port] = direction;
        var recipe = new ProductionRenderTemplate.Recipe { OutputId = 2, OutputCount = 1, OutputRate = 1, Duration = 2 }; recipe.Inputs.Add(new CraftingTreeRuntime.IngredientEntry(2, 1));
        var template = new ProductionRenderTemplate { Recipes = new[] { recipe } };
        var state = new BlockStateStore.InstallationSaveState { itemFilterMaskInitialized = true };
        state.inputOutputState.gridCoordinates.Add(port);
        if (output) state.inputOutputState.outputCoordinates.Add(port);
        return world.Add(prototype, state, template);
    }
    static void Main()
    {
        InputOutputModule.Items[2] = new ItemDefinition { id = 2, isFluid = true };
        var world = new ProductionWorld(); var source = Create(world, new Vector2Int(1, 0), true, Vector2Int.right);
        var io = source.Placement.inputOutputState; io.productionOutputFluidUnits = Units(2);
        PipeWorld.Current.Pipes[new Vector2Int(1, 0)] = new(); PipeWorld.Current.Pipes[new Vector2Int(2, 0)] = new();
        Pipe.Graph[new Vector2Int(1, 0)] = 0; Pipe.Graph[new Vector2Int(2, 0)] = 1;
        var tank = new Fluidtank(); tank.RuntimeOccupiedCoordinates.Add(new Vector2Int(2, 0));
        InstallationObject.Bodies[new Vector2Int(2, 0)] = tank;
        world.TransferOutputFluid(source, source.Template.Recipes[0]);
        Check(tank.Units > 0 && io.productionOutputFluidUnits < Units(2), "Output reaches a fixed tank body without an area registry");
        tank.Capacity = tank.Units; long reserve = io.productionOutputFluidUnits;
        world.TransferOutputFluid(source, source.Template.Recipes[0]); Check(io.productionOutputFluidUnits == reserve, "Full tank debits no source fluid");
        tank.Capacity = long.MaxValue; Pipe.FluidId = 9;
        world.TransferOutputFluid(source, source.Template.Recipes[0]); Check(io.productionOutputFluidUnits == reserve, "Different network fluid rejects transfer");
        Pipe.FluidId = -1; InstallationObject.Bodies.Clear();
        InputOutputModule.FluidTopologyVersion++;
        var dedicated = new InputOutputModule { Dedicated = true, RejectDedicated = true };
        InputOutputModule.Storages[new Vector2Int(2, 0)] = dedicated;
        world.TransferOutputFluid(source, source.Template.Recipes[0]);
        Check(dedicated.GenericAdds == 0 && io.productionOutputFluidUnits == reserve, "Rejected dedicated storage never falls back to generic storage");
        InputOutputModule.Storages.Clear();
        InputOutputModule.FluidTopologyVersion++;
        var receiver = Create(world, new Vector2Int(2, 1), false, Vector2Int.down);
        long sourceRevision = source.FluidStorageStateRevision;
        world.TransferOutputFluid(source, source.Template.Recipes[0]);
        Check(receiver.FluidUnits(2) == 0, "Parallel side pipe cannot feed data receiver without a connector");
        PipeWorld.Current.Pipes[new Vector2Int(2, 0)].Directions.Add(Vector2Int.up);
        InputOutputModule.FluidTopologyVersion++;
        world.TransferOutputFluid(source, source.Template.Recipes[0]);
        Check(receiver.FluidUnits(2) > 0, "Facing connector feeds data receiver");
        Check(receiver.FluidStorageStateRevision > 0, "Fluid receipt publishes receiver-local revision");
        Check(source.FluidStorageStateRevision == sourceRevision, "Receipt does not invalidate unrelated storage revision");
        long receiverRevision = receiver.FluidStorageStateRevision;
        world.WakeNativeProducers(receiver);
        Check(receiver.FluidStorageStateRevision > receiverRevision, "Receiver capacity wake publishes local revision");
        Check(source.FluidStorageStateRevision == sourceRevision, "Receiver wake leaves unrelated revision unchanged");
        receiver.World.GetState(receiver.Index, receiver.Generation).Production.Begin(0, 2, 1, 120);
        long received = receiver.FluidUnits(2); world.TransferOutputFluid(source, source.Template.Recipes[0]);
        Check(receiver.FluidUnits(2) == received, "Crafting receiver blocks new intake");
        receiver.World.GetState(receiver.Index, receiver.Generation).Production.Clear();
        io.productionOutputFluidUnits = 1; world.TransferOutputFluid(source, source.Template.Recipes[0]);
        Check(io.productionOutputFluidUnits == 0 && receiver.FluidUnits(2) == received + 1, "Final single fluid unit drains despite pipe retention");
        Pipe.Graph.Clear(); Pipe.Graph[new Vector2Int(1, 0)] = 0;
        InputOutputModule.FluidTopologyVersion++;
        InputOutputModule.Storages[new Vector2Int(1, 0)] = tank; tank.Units = 0; tank.Fluid = -1;
        io.productionOutputFluidUnits = Units(2);
        for (long tick = 0; io.productionOutputFluidUnits > 0 && tick < 400; tick++)
        { MapObjectTickManager.CurrentSimulationTick = tick; world.TransferOutputFluid(source, source.Template.Recipes[0]); }
        Check(io.productionOutputFluidUnits == 0, "Entire output reserve eventually drains");
        Check(Math.Abs(tank.Units - Units(2)) < 60, "Float native transport conserves batch within one micro-liter");
        Pipe.RoutePump = new Pump { Rate = .5f }; io.productionOutputFluidUnits = Units(2); tank.Units = 0;
        InputOutputModule.FluidTopologyVersion++;
        MapObjectTickManager.CurrentSimulationTick = 1000; world.TransferOutputFluid(source, source.Template.Recipes[0]);
        world.TransferOutputFluid(source, source.Template.Recipes[0]);
        Check(tank.StoredFluidLiters <= .5f / 60 + .000001f, "Shared pump budget caps combined transfer in one tick");
        Pipe.RoutePump = null; tank.Fluid = 2; tank.Units = Units(10); Pipe.FluidId = 2;
        var intake = Create(world, new Vector2Int(1, 0), false, Vector2Int.right);
        world.TransferInputFluids(intake, intake.Template.Recipes[0]);
        Check(intake.FluidUnits(2) == Units(1) && tank.Units == Units(9), "Input removes actual storage fluid");
        world.TransferInputFluids(intake, intake.Template.Recipes[0]); world.TransferInputFluids(intake, intake.Template.Recipes[0]);
        Check(intake.FluidUnits(2) == Units(2) && tank.Units == Units(8), "Input stops at complete duration-scaled batch");
        Console.WriteLine($"PASS: {checks} real production fluid bridge checks (reachable pipe graph/native stores doubled).");
    }
}
