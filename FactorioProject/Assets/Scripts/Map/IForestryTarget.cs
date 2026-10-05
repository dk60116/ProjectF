using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.MapObjects
{
    public interface ILoggingTarget : IMapObjectTarget
    {
        int MinimumGrowth { get; }
        int MaximumGrowth { get; }
        bool IsTreeFilterInitialized { get; }
        bool TryGetElectricPowerRequirement(out float watts);
        bool IsTreeTypeEnabled(ResourceDefinition definition);
        void SetTreeTypeEnabled(ResourceDefinition definition, IReadOnlyList<ResourceDefinition> available, bool enabled);
        void SetAllTreeTypes(IReadOnlyList<ResourceDefinition> available, bool enabled);
        void SetGrowthRange(int minimum, int maximum);
        List<string> CaptureEnabledTreeDefinitionKeys();
        void GetObjectInfoStatus(out string text, out bool working, out bool warning);
    }

    public interface ISeedPlanterTarget : IMapObjectTarget
    {
        bool IsErrorState { get; }
        int CurrentSeedItemId { get; }
        int CurrentSeedCount { get; }
        int RuntimeAreaMaxObjects { get; }
        float PlantDurationSeconds { get; }
        float PlantElapsedSeconds { get; }
        float PlantProgress01 { get; }
        void GetObjectInfoStatus(out string text, out bool working, out bool warning);
    }
}
