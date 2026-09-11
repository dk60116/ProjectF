using System;
using UnityEngine;

public class Resource : MapObject
{
    public enum HarvestMode
    {
        Auto,
        Mining,
        Logging,
        Cut,
        Cultivating
    }

    [Serializable]
    public struct ResourceStatus
    {
        [HideInInspector]
        public int outputId;
        [Tooltip("Harvest output ItemDefinition name. Leave blank to use the ResourceDefinition resource name.")]
        public string outputItemName;
        public int resourceCount;
        public int getCount;
        public int maxGauge;
        public int currentGague;
    }

    [Serializable]
    public struct ResourceSaveState
    {
        public int resourceCount;
        public int maxGauge;
        public int currentGauge;
        public int initialResourceCount;
        public bool hasBodyYawStep;
        public int bodyYawStep;
        public bool hasGrowth;
        public float growth;
        public bool hasPlantGrowthState;
        public float growthWaterLiters;
        public float growthFertilizerAmount;
        public float growthElapsedSeconds;
    }

    [SerializeField]
    private HarvestMode harvestMode = HarvestMode.Auto;

    [SerializeField]
    private ResourceDefinition definition;

    [SerializeField]
    private ResourceStatus resourceStatus;

    [SerializeField]
    private float workPerGaugeDot = 1f;

    [SerializeField, Min(0f)]
    private float portableMoveInterval = 0.1f;

    [SerializeField]
    private Vector3 focusOffset = new Vector3(0f, 0.5f, 0f);

    [SerializeField, Range(0f, 1f)]
    private float minimumBodyScaleRatio = 0.5f;

    [SerializeField, Min(0.01f)]
    private float maximumBodyScaleRatio = 1f;

    [SerializeField, Min(1)]
    private int dynamicScaleMaxResourceCount = 1000;


    public ResourceDefinition Definition => definition;
    public ResourceDefinition.PlacementCategory PlacementCategory => definition != null
        ? definition.placementCategory : ResourceDefinition.PlacementCategory.Ore;
    public HarvestMode ResolvedHarvestMode
    {
        get
        {
            HarvestMode mode = definition != null ? definition.harvestMode : harvestMode;
            if (mode != HarvestMode.Auto) return mode;
            string sourceName = ObjectName + " " + name;
            if (sourceName.IndexOf("reed", StringComparison.OrdinalIgnoreCase) >= 0) return HarvestMode.Cut;
            return sourceName.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ? HarvestMode.Logging : HarvestMode.Mining;
        }
    }
    internal ResourceStatus InitialStatus => resourceStatus;
    public int ResourceCount => CaptureState().resourceCount;
    public int GetCount => Mathf.Max(1, definition != null ? definition.defaultGetCount : resourceStatus.getCount);
    public int MaxGauge => CaptureState().maxGauge;
    public int CurrentGauge => CaptureState().currentGauge;
    internal float WorkPerGaugeDot => workPerGaugeDot;
    internal float PortableMoveInterval => portableMoveInterval;
    internal Vector3 FocusOffset => focusOffset;
    internal float MinimumScale => minimumBodyScaleRatio;
    internal float MaximumScale => maximumBodyScaleRatio;
    internal int ScaleMaximumCount => dynamicScaleMaxResourceCount;
    public override bool AllowsAnimalTraversal => ResolvedHarvestMode == HarvestMode.Mining || ResolvedHarvestMode == HarvestMode.Cut;

    // Authoring defaults only; live quantities are owned by ResourceTypeWorld.
    public virtual ResourceSaveState CaptureState()
    {
        int count = resourceStatus.resourceCount > 0 ? resourceStatus.resourceCount : definition != null ? definition.defaultResourceCount : 1;
        int gauge = resourceStatus.maxGauge > 0 ? resourceStatus.maxGauge : definition != null ? definition.defaultMaxGauge : 10;
        return new ResourceSaveState { resourceCount = Mathf.Max(1, count), initialResourceCount = Mathf.Max(1, count),
            maxGauge = Mathf.Max(1, gauge), currentGauge = resourceStatus.currentGague > 0 ? Mathf.Min(resourceStatus.currentGague, Mathf.Max(1,gauge)) : Mathf.Max(1,gauge) };
    }
}
