using ProjectF.Benchmark;
using ProjectF.Simulation;

public partial class LoggingMachine
{
    internal void ResetBenchmarkWork()
    {
        activeTree = null;
        consumedWorkEnergyUnits = 0;
        hasElectricDemand = BenchmarkRuntime.ForceWorking;
        electricPowerBlocked = false;
        SetWorking(false);
        WakeRuntimeTick();
    }

    private void ApplyBenchmarkWork(float deltaTime)
    {
        hasElectricDemand = true;
        electricPowerBlocked = false;
        UpdateHingeRotation(deltaTime);
        SetWorking(true);
        float rate = ItemDefinition.ResolveUseEnergyRatePerSecond(ResolveLoggingDefinition());
        consumedWorkEnergyUnits += DeterministicSimulationUnits.FromFloat(rate * deltaTime);
        if (consumedWorkEnergyUnits < DeterministicSimulationUnits.FromFloat(ResolveRequiredWorkEnergy())) return;
        BenchmarkRuntime.EmitItem(TerrainGenerator.ResolveActive(), BenchmarkRuntime.FallbackItemId, transform.position);
        consumedWorkEnergyUnits = 0;
        AdvanceDirection();
    }
}
