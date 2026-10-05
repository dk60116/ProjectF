// Pipe pressure queries must not require a scene component or a crafting recipe.
public interface IDataFluidProducer
{
    bool IsRuntimeActive { get; }
    int OutputItemId { get; }
    float GetFluidPressure(int fluidItemId);
}
