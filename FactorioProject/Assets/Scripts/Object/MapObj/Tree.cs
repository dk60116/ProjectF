using UnityEngine;

namespace ProjectF.MapObjects
{
    [DisallowMultipleComponent]
    [AddComponentMenu("ProjectF/Map Object/Tree")]
    public class Tree : Resource
    {
        [SerializeField, Range(ResourceDefinition.MinGrowth, ResourceDefinition.MaxGrowth)]
        private float growth = ResourceDefinition.DefaultGrowth;

        public float Growth => Mathf.Clamp(
            growth,
            ResourceDefinition.MinGrowth,
            ResourceDefinition.MaxGrowth);

        public void SetGrowth(float value)
        {
            growth = Mathf.Clamp(
                value,
                ResourceDefinition.MinGrowth,
                ResourceDefinition.MaxGrowth);
            RefreshBodyScale();
        }

        protected override float GetAdditionalBodyScaleRatio()
        {
            float normalizedGrowth = Mathf.InverseLerp(
                ResourceDefinition.MinGrowth,
                ResourceDefinition.MaxGrowth,
                Growth);
            return Mathf.Lerp(
                MinimumBodyScaleRatio,
                MaximumBodyScaleRatio,
                normalizedGrowth);
        }

        protected override void CaptureAdditionalSaveState(ref ResourceSaveState state)
        {
            state.hasGrowth = true;
            state.growth = Growth;
        }

        protected override void ApplyAdditionalSavedState(ResourceSaveState state)
        {
            if (state.hasGrowth)
            {
                growth = Mathf.Clamp(
                    state.growth,
                    ResourceDefinition.MinGrowth,
                    ResourceDefinition.MaxGrowth);
            }
        }
    }
}
