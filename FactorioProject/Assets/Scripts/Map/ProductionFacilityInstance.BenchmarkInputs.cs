using ProjectF.Benchmark;

public sealed partial class ProductionFacilityInstance
{
    private int benchmarkInputVersion = -1;
    private ProductionRenderTemplate.Recipe benchmarkInputRecipe;
    private void RefreshBenchmarkInputs()
    {
        if (!BenchmarkRuntime.ForceWorking) return;
        var recipe = SelectedRecipe ?? (Template.Recipes.Length > 0 ? Template.Recipes[0] : null);
        if (benchmarkInputVersion == BenchmarkInputSupply.Version && ReferenceEquals(benchmarkInputRecipe, recipe)) return;
        benchmarkInputVersion = BenchmarkInputSupply.Version; benchmarkInputRecipe = recipe;
        BenchmarkInputSupply.Remove(this);
        if (recipe != null)
            for (int i = 0; i < recipe.Inputs.Count; i++)
            {
                var ingredient = recipe.Inputs[i];
                for (int j = 0; j < Io.inputItemAreas.Count; j++)
                {
                    var area = Io.inputItemAreas[j];
                    if (area.itemId >= 0 && area.itemId != ingredient.itemId) continue;
                    BenchmarkInputSupply.Add(this, World.Terrain, World.Store, area.coordinate, ingredient.itemId, ingredient.count, Prototype.RuntimeAreaMaxObjects);
                }
            }
        BenchmarkInputSupply.AddEnergy(this, World.Terrain, World.Store, Io.inputEnergyCoordinates, Template.Definition, Prototype.RuntimeAreaMaxObjects);
    }
    internal void SampleBenchmarkEnergyVisuals() => BenchmarkInputSupply.SampleEnergy(this, ConsumeWorldPosition, IsWorking);
}
