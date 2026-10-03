public interface IProductionFacilityInfo
{
    bool TryGetObjectInfoProductionIngredientCount(out int count);
    bool TryGetObjectInfoProductionIngredient(int index, out int item, out int required, out int count, out int capacity);
    bool TryGetObjectInfoProductionFluidIngredient(int index, out int item, out float stored, out float required);
    bool TryGetObjectInfoProductionOutput(out int item, out int count, out int capacity);
    bool TryGetObjectInfoProductionFluidOutput(out int item, out float rate, out float stored, out float capacity);
    bool TryGetObjectInfoProductionFluidGauge(int index, out ProductionMachine.FluidGaugeState state);
    float GetStoredFluidTemperatureCelsius(int item);
}
