using UnityEngine;
namespace ProjectF.MapObjects
{
    // Prefab authoring only. TreeInstance contains no Unity component.
    [AddComponentMenu("ProjectF/Map Object/Tree")]
    public class Tree : Resource
    {
        [SerializeField, Range(ResourceDefinition.MinGrowth, ResourceDefinition.MaxGrowth)]
        private float growth = ResourceDefinition.DefaultGrowth;
        [SerializeField, Min(0f)] private float growthWaterLiters;
        [SerializeField, Min(0f)] private float growthFertilizerAmount;
        [SerializeField, Min(0f)] private float growthElapsedSeconds;
        public float Growth => Mathf.Clamp(growth, ResourceDefinition.MinGrowth, ResourceDefinition.MaxGrowth);
        public override ResourceSaveState CaptureState()
        {
            ResourceSaveState state = base.CaptureState();
            state.hasGrowth = true; state.growth = Growth; state.hasPlantGrowthState = true;
            state.growthWaterLiters = growthWaterLiters; state.growthFertilizerAmount = growthFertilizerAmount;
            state.growthElapsedSeconds = growthElapsedSeconds;
            return state;
        }
    }
}
