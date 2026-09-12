using System;

public static class Checks
{
    private static int passed;
    private static long Duration => DeterministicSimulationUnits.FromFloat(5f);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        passed++;
        Console.WriteLine("PASS " + label);
    }
    private static SeedPlanter Restore(long progress, float supply = 1f, bool loaded = true, long transfer = 0)
    {
        TerrainGenerator.Active = new TerrainGenerator();
        var planter = new SeedPlanter { SupplyRatio = supply };
        planter.ApplyPersistentState(new InputOutputModule.PersistentState
        {
            seedPlanterPlantElapsedUnits = progress,
            seedPlanterHasLoadedSeed = loaded,
            seedPlanterLoadedSeedItemId = loaded ? 1 : -1,
            seedPlanterTransferRemainingUnits = transfer
        });
        return planter;
    }
    private static void CheckCompleted(SeedPlanter planter, string label)
    {
        Check(TerrainGenerator.Active.PlantCalls == 1 && TerrainGenerator.Active.DropAnimations == 1
              && !planter.CapturePersistentState().seedPlanterHasLoadedSeed
              && planter.CurrentOperatingState == SeedPlanter.OperatingState.TargetOccupied, label);
    }
    public static void Main()
    {
        foreach (float supply in new[] { 1f, 0f })
        {
            var planter = Restore(Duration, supply);
            planter.ApplyManagedUpdateTick();
            CheckCompleted(planter, $"completed save commits with supply {supply}");
            Check(planter.EnergyCalls == 0 && planter.ConsumedSeeds == 0, "completed save needs no extra energy or seed");
        }
        foreach (long remaining in new[] { 1L, 5999L, 6000L })
        {
            var planter = Restore(Duration - remaining);
            planter.ApplyManagedUpdateTick();
            CheckCompleted(planter, $"completion with {remaining} integer units remaining");
        }
        var partial = Restore(Duration - DeterministicSimulationUnits.FromFloat(0.01f), 0.5f);
        partial.ApplyManagedUpdateTick();
        CheckCompleted(partial, "partial power completes final 0.01 seconds in one 0.1 second update");

        var outage = Restore(Duration - 1, 0f);
        outage.ApplyManagedUpdateTick();
        Check(outage.CurrentOperatingState == SeedPlanter.OperatingState.NoPower
              && outage.CapturePersistentState().seedPlanterPlantElapsedUnits == Duration - 1
              && outage.CapturePersistentState().seedPlanterHasLoadedSeed, "real outage preserves unfinished work and seed");
        outage.SupplyRatio = 1f;
        outage.ApplyManagedUpdateTick();
        CheckCompleted(outage, "power restoration finishes stranded save");
        outage.ApplyManagedUpdateTick();
        Check(TerrainGenerator.Active.PlantCalls == 1, "subsequent updates do not plant a duplicate");

        var transfer = Restore(Duration, transfer: DeterministicSimulationUnits.FromFloat(PortableObject.MoveToDuration));
        transfer.ApplyManagedUpdateTick();
        Check(transfer.CurrentOperatingState == SeedPlanter.OperatingState.LoadingSeed
              && TerrainGenerator.Active.PlantCalls == 0, "completed progress still waits for seed transfer");
        for (int i = 0; i < 3 && TerrainGenerator.Active.PlantCalls == 0; i++) transfer.ApplyManagedUpdateTick();
        CheckCompleted(transfer, "completion follows seed arrival");

        var legacy = Restore(Duration, loaded: false);
        legacy.InputSeeds = 1;
        legacy.ApplyManagedUpdateTick();
        Check(legacy.ConsumedSeeds == 1 && legacy.InputSeeds == 0
              && legacy.CurrentOperatingState == SeedPlanter.OperatingState.LoadingSeed
              && TerrainGenerator.Active.PlantCalls == 0, "legacy save loads one seed before planting");
        for (int i = 0; i < 4 && TerrainGenerator.Active.PlantCalls == 0; i++) legacy.ApplyManagedUpdateTick();
        CheckCompleted(legacy, "legacy completed progress plants after transfer");

        var cycle = Restore(0, 0.5f, loaded: false);
        cycle.InputSeeds = 1;
        for (int i = 0; i < 110 && TerrainGenerator.Active.PlantCalls == 0; i++)
        {
            cycle.ApplyManagedUpdateTick();
            if (cycle.CurrentOperatingState == SeedPlanter.OperatingState.NoPower)
                throw new Exception("spurious NoPower under continuous half supply");
        }
        CheckCompleted(cycle, "full planting cycle completes under continuous half power");
        Check(cycle.ConsumedSeeds == 1 && cycle.InputSeeds == 0, "full cycle consumes exactly one seed");

        var rejected = Restore(Duration);
        TerrainGenerator.Active.FailPlant = true;
        rejected.ApplyManagedUpdateTick();
        Check(rejected.InputSeeds == 1 && !rejected.CapturePersistentState().seedPlanterHasLoadedSeed
              && TerrainGenerator.Active.DropAnimations == 0, "failed placement returns seed without dropping a duplicate");
        Console.WriteLine($"{passed} seed planter checks passed.");
    }
}
