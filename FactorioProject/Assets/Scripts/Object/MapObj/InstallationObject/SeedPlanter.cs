using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;

// Installed simulation lives in SeedPlanterInstance; this component supplies prefab/edit data.
public class SeedPlanter : InputOutputModule
{
    private const float DefaultPlantDurationSeconds = 2f;
    [SerializeField] private Sprite outputAreaMarkerIcon;
    [SerializeField] private Renderer warningLightRenderer;
    [SerializeField, Min(0.1f)] private float workAnimationCycleSeconds = 2.5f;
    private PersistentState plantingState;
    public Sprite OutputAreaMarkerIcon => outputAreaMarkerIcon;
    internal Renderer DataWarningLightRenderer
    {
        get
        {
            if (warningLightRenderer != null) return warningLightRenderer;
            var transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (transforms[i].name == "Line" && transforms[i].TryGetComponent(out Renderer renderer)) return renderer;
            return null;
        }
    }
    internal float WorkAnimationCycleSeconds => workAnimationCycleSeconds;
    public override float ManagedUpdateTickIntervalSeconds => 0.1f;
    public override void ApplyManagedUpdateTick() { }
    protected override bool ShouldKeepRuntimeUpdateTickActive() => false;
    protected override bool ShouldPlayWorkAnimation() => false;
    public override PersistentState CapturePersistentState()
    {
        var state = base.CapturePersistentState();
        if (plantingState != null)
        {
            state.seedPlanterPlantElapsedSeconds = plantingState.seedPlanterPlantElapsedSeconds;
            state.seedPlanterPlantElapsedUnits = plantingState.seedPlanterPlantElapsedUnits;
            state.seedPlanterHasLoadedSeed = plantingState.seedPlanterHasLoadedSeed;
            state.seedPlanterLoadedSeedItemId = plantingState.seedPlanterLoadedSeedItemId;
            state.seedPlanterLoadedSeedInputCoordinate = plantingState.seedPlanterLoadedSeedInputCoordinate;
            state.seedPlanterTransferRemainingUnits = plantingState.seedPlanterTransferRemainingUnits;
        }
        return state;
    }
    public override void ApplyPersistentState(PersistentState state) { base.ApplyPersistentState(state); plantingState = state; }
    protected override void OnPlacementRuntimeCleared() { plantingState = null; base.OnPlacementRuntimeCleared(); }
    public static float ResolvePlantDuration(ItemDefinition definition)
        => definition != null && definition.seedPlanterPlantDurationSeconds > 0
            ? Mathf.Max(0.1f, definition.seedPlanterPlantDurationSeconds) : DefaultPlantDurationSeconds;
    public bool TryCollectPlantableSeedItemIds(ICollection<int> itemIds)
    {
        if (itemIds == null || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return false;
        }

        bool foundAny = false;
        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (!ItemDefinition.IsPlantableSeedDefinition(definition) || itemIds.Contains(definition.id))
            {
                continue;
            }

            itemIds.Add(definition.id);
            foundAny = true;
        }

        return foundAny;
    }
    protected override bool TryCollectAdditionalRuntimeInputItemIds(ICollection<int> itemIds)
    {
        return TryCollectPlantableSeedItemIds(itemIds);
    }
    protected override bool AppendAcceptedRuntimeInputItemIdsAtCoordinate(
        Vector2Int coordinate,
        ISet<int> inputItemIds)
    {
        if (inputItemIds == null)
        {
            return false;
        }

        bool foundAny = false;
        List<ItemDefinition> definitions = GameManager.Instance != null && GameManager.Instance.ItemManger != null
            ? GameManager.Instance.ItemManger.ItemDefinitions
            : null;
        if (definitions == null)
        {
            return false;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (!ItemDefinition.IsPlantableSeedDefinition(definition)
                || !ContainsRuntimeInputItemArea(coordinate, definition.id))
            {
                continue;
            }

            inputItemIds.Add(definition.id);
            foundAny = true;
        }

        return foundAny;
    }
}
