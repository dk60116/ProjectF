using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ProjectF/Item Definition", fileName = "ItemDef_")]
public class ItemDefinition : ScriptableObject
{
    private const float DefaultCraftingDurationSeconds = 5f;
    private const float DefaultBucketFillDurationSeconds = 10f;
    private const int DefaultUndergroundPipeMaxDistance = 5;
    private const float KilowattsToWatts = 1000f;
    private const float DefaultItemLightRange = 6f;

    public enum EnergyType
    {
        None = 0,
        Burn = 1,
        Electricity = 2,
        CarnivoreFood = 3,
        HerbivoreFood = 4,
        Fertilizer = 5
    }

    public enum ItemLightMode
    {
        None,
        Always,
        Toggle,
        NightOnly,
        Working,
        [InspectorName("Hand Toggle")]
        HandToggle
    }

    public string itemName;
    public int id;
    public MapObject mapObject;
    public Mesh portableMesh;
    public Material portableMat;
    public Sprite icon;
    [Tooltip("체크하면 가방·손·바닥·설치물·차량을 포함한 모든 아이템 스택의 용량이 1개로 제한됩니다. 서로 다른 스택에는 같은 아이템을 보관할 수 있습니다.")]
    public bool oneItem;
    public List<Sprite> interactionButtonList = new List<Sprite>();
    public ItemLightMode lightMode = ItemLightMode.None;
    [Min(0.1f)]
    public float lightRange = DefaultItemLightRange;
    [Min(0.01f)]
    public float lightIntensityMultiplier = 1f;
    public uint size;
    public bool itemFilter;
    [Tooltip("체크하면 이 아이템을 박스의 아이템 필터 목록에 표시하지 않습니다.")]
    public bool ignoreFilter;
    [Tooltip("제작법 설명서 등 Manual 용도로 사용하는 아이템인지 여부입니다.")]
    public bool isManual;
    [Tooltip("이 Manual이 설명하는 대상 아이템입니다.")]
    public ItemDefinition manualTargetItem;
    [Tooltip("부모 I/O 모듈이 설치되어 있을 때 이 아이템으로 업그레이드할 수 있는지 여부입니다.")]
    public bool upgradeable = true;
    [Min(1)]
    public int capacity = 10;
    public bool storesFluid;
    [Min(0f)]
    public float fluidStorageLiters = 0f;
    public Color fluidDisplayColor = Color.white;
    [Min(0f)]
    [Tooltip("Water Pump, Oil drilling machine 등 유체 생산 설치물의 초당 출력량(L/s)입니다.")]
    public float fluidOutputLitersPerSecond = 1f;
    [Min(0.1f)]
    [Tooltip("빈 Bucket을 물이 나오는 Pipe 출구에 설치했을 때 Water Bucket이 될 때까지의 시간(초)입니다.")]
    public float bucketFillDurationSeconds = DefaultBucketFillDurationSeconds;
    [Min(2)]
    [Tooltip("Underground Pipe의 입구와 출구를 포함한 최대 설치 거리입니다.")]
    public int undergroundPipeMaxDistance = DefaultUndergroundPipeMaxDistance;
    public EnergyType energyType = EnergyType.None;
    [Min(0)]
    public int energyAmount = 0;
    [Tooltip("이 음식을 먹었을 때 확률적으로 획득하는 아이템입니다.")]
    public ItemDefinition eatRewardItem;
    [Range(0f, 100f)]
    [Tooltip("음식 1개를 먹었을 때 Eat Reward Item을 획득할 확률(%)입니다.")]
    public float eatRewardChancePercent;
    [Tooltip("체크하면 이 아이템을 밭에 심을 수 있는 씨앗으로 사용합니다.")]
    public bool isSeed;
    [Tooltip("씨앗을 밭에 심었을 때 생성할 리소스입니다.")]
    public ResourceDefinition seedTargetResource;
    public EnergyType useEnergyType = EnergyType.None;
    [Min(0f)]
    public float useEnergyAmount = 0f;
    [Min(0f)]
    public float completeEnergy = 0f;
    [Min(0)]
    public int utilityPoleConnectionRadius = 6;
    [Min(0)]
    public int utilityPoleSupplyRadius = 3;
    [Header("Sprinkler")]
    [Min(0)]
    [Tooltip("Sprinkler가 물을 분사하는 반경(칸)입니다.")]
    public int sprinklerRangeRadius = 3;
    [Min(0.001f)]
    [Tooltip("Sprinkler가 한 번 분사할 때 범위의 각 칸마다 소비하는 물(L)입니다.")]
    public float sprinklerWaterLitersPerCell = 0.25f;
    [Min(0.1f)]
    [Tooltip("Sprinkler의 분사 주기(초)입니다.")]
    public float sprinklerSprayIntervalSeconds = 2f;
    [Min(0f)]
    [Tooltip("Sprinkler 작동 중 노즐의 초당 회전 각도입니다.")]
    public float sprinklerNozzleRotationDegreesPerSecond = 180f;
    [Header("Seed Planter")]
    [Min(0.1f)]
    [Tooltip("Seed Planter가 씨앗 하나를 심는 데 걸리는 시간(초)입니다.")]
    public float seedPlanterPlantDurationSeconds = 2f;
    [SerializeField, Min(0.01f)]
    private float craftingDurationSeconds = DefaultCraftingDurationSeconds;

    public float CraftingDurationSeconds => craftingDurationSeconds > 0f ? craftingDurationSeconds : DefaultCraftingDurationSeconds;
    public float LightRange => Mathf.Max(0.1f, lightRange);
    public float LightIntensityMultiplier => lightIntensityMultiplier > 0f ? lightIntensityMultiplier : 1f;
    public float BucketFillDurationSeconds => bucketFillDurationSeconds > 0f
        ? bucketFillDurationSeconds
        : DefaultBucketFillDurationSeconds;
    public float FluidOutputLitersPerSecond => Mathf.Max(0f, fluidOutputLitersPerSecond);
    public int UndergroundPipeMaxDistance => Mathf.Max(2, undergroundPipeMaxDistance);
    public ItemDefinition ManualTargetItem => isManual ? manualTargetItem : null;
    public float UseEnergyRatePerSecond => ResolveUseEnergyRatePerSecond(this);
    public float ElectricUseWatts => ResolveElectricUseWatts(this);

    public static float ResolveUseEnergyRatePerSecond(ItemDefinition definition)
    {
        if (definition == null || definition.useEnergyType == EnergyType.None)
        {
            return 0f;
        }

        float amount = Mathf.Max(0f, definition.useEnergyAmount);
        return definition.useEnergyType == EnergyType.Electricity
            ? amount * KilowattsToWatts
            : amount;
    }

    public static int ResolveStackCapacity(ItemDefinition definition, int defaultCapacity)
    {
        return definition != null && definition.oneItem
            ? 1
            : Mathf.Max(1, defaultCapacity);
    }

    public static int ResolveStackCapacity(ItemManager itemManager, int itemId, int defaultCapacity)
    {
        ItemDefinition definition = null;
        if (itemManager != null && itemId >= 0)
        {
            itemManager.TryGetItemDefinitionById(itemId, out definition);
        }

        return ResolveStackCapacity(definition, defaultCapacity);
    }

    public static bool IsFoodEnergyType(EnergyType energyType)
    {
        return energyType == EnergyType.CarnivoreFood
               || energyType == EnergyType.HerbivoreFood;
    }

    public static bool IsFoodEnergyItemDefinition(ItemDefinition definition)
    {
        return definition != null && IsFoodEnergyType(definition.energyType);
    }

    public static bool IsFertilizerEnergyItemDefinition(ItemDefinition definition)
    {
        return definition != null
               && definition.energyType == EnergyType.Fertilizer
               && definition.energyAmount > 0;
    }

    public static bool IsPlantableSeedDefinition(ItemDefinition definition)
    {
        return definition != null
               && definition.isSeed
               && definition.seedTargetResource != null
               && definition.seedTargetResource.prefab != null;
    }

    public static bool IsHandToggleLightDefinition(ItemDefinition definition)
    {
        return definition != null && definition.lightMode == ItemLightMode.HandToggle;
    }

    public static bool IsToggleLightMode(ItemLightMode mode)
    {
        return mode == ItemLightMode.Toggle || mode == ItemLightMode.HandToggle;
    }

    public bool TryGetEatReward(out ItemDefinition rewardDefinition, out float chancePercent)
    {
        rewardDefinition = eatRewardItem;
        chancePercent = Mathf.Clamp(eatRewardChancePercent, 0f, 100f);
        return IsFoodEnergyItemDefinition(this)
               && rewardDefinition != null
               && rewardDefinition != this
               && rewardDefinition.id >= 0
               && chancePercent > 0f;
    }

    public static float ResolveCompleteEnergyAmount(ItemDefinition definition)
    {
        if (definition == null || definition.useEnergyType == EnergyType.None)
        {
            return 0f;
        }

        float amount = Mathf.Max(0f, definition.completeEnergy);
        return definition.useEnergyType == EnergyType.Electricity
            ? amount * KilowattsToWatts
            : amount;
    }

    public static float ResolveElectricUseWatts(ItemDefinition definition)
    {
        return definition != null && definition.useEnergyType == EnergyType.Electricity
            ? ResolveUseEnergyRatePerSecond(definition)
            : 0f;
    }

    public static bool IsElectricityItemDefinition(ItemDefinition definition)
    {
        return definition != null
               && string.Equals(definition.itemName, "Electricity", StringComparison.OrdinalIgnoreCase);
    }

    public static float ResolveElectricOutputWatts(ItemDefinition outputDefinition, float outputKilowatts)
    {
        float amount = Mathf.Max(0f, outputKilowatts);
        return IsElectricityItemDefinition(outputDefinition)
            ? amount * KilowattsToWatts
            : amount;
    }

    public void SetCraftingDurationSeconds(float seconds)
    {
        craftingDurationSeconds = Mathf.Max(0.01f, seconds);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!isManual || manualTargetItem == this)
        {
            manualTargetItem = null;
        }

        if (eatRewardItem == this)
        {
            eatRewardItem = null;
        }

        if (!isSeed)
        {
            seedTargetResource = null;
        }

        eatRewardChancePercent = Mathf.Clamp(eatRewardChancePercent, 0f, 100f);

        if (craftingDurationSeconds <= 0f)
        {
            craftingDurationSeconds = DefaultCraftingDurationSeconds;
        }

        if (!storesFluid)
        {
            fluidStorageLiters = 0f;
        }
        else
        {
            fluidStorageLiters = Mathf.Max(0f, fluidStorageLiters);
        }

        utilityPoleConnectionRadius = Mathf.Max(0, utilityPoleConnectionRadius);
        utilityPoleSupplyRadius = Mathf.Max(0, utilityPoleSupplyRadius);
        sprinklerRangeRadius = Mathf.Max(0, sprinklerRangeRadius);
        sprinklerWaterLitersPerCell = Mathf.Max(0.001f, sprinklerWaterLitersPerCell);
        sprinklerSprayIntervalSeconds = Mathf.Max(0.1f, sprinklerSprayIntervalSeconds);
        sprinklerNozzleRotationDegreesPerSecond = Mathf.Max(0f, sprinklerNozzleRotationDegreesPerSecond);
        undergroundPipeMaxDistance = Mathf.Max(2, undergroundPipeMaxDistance);
        lightRange = Mathf.Max(0.1f, lightRange);
        lightIntensityMultiplier = Mathf.Max(0.01f, lightIntensityMultiplier);
        bucketFillDurationSeconds = Mathf.Max(0.1f, bucketFillDurationSeconds);
        fluidOutputLitersPerSecond = Mathf.Max(0f, fluidOutputLitersPerSecond);
    }
#endif
}

public static class ItemDefinitionLookup
{
    private const int LegacyConveyorBelt2FItemId = 26;
    private const string ConveyorBelt2FItemName = "Conveyor belt 2F";
    private const char PersistenceNameSeparator = '\u001f';

    public static string GetPersistenceName(
        ItemDefinition definition,
        IReadOnlyList<ItemDefinition> definitions)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        string itemName = GetDisplayName(definition);
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return string.Empty;
        }

        int matchingNameCount = 0;
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition candidate = definitions[i];
                if (candidate != null
                    && string.Equals(GetDisplayName(candidate), itemName, StringComparison.OrdinalIgnoreCase))
                {
                    matchingNameCount++;
                }
            }
        }

        if (matchingNameCount <= 1)
        {
            return itemName;
        }

        string definitionName = definition.name != null ? definition.name.Trim() : string.Empty;
        return string.IsNullOrWhiteSpace(definitionName)
            ? itemName
            : string.Concat(itemName, PersistenceNameSeparator, definitionName);
    }

    public static ItemDefinition ResolveByPersistenceName(
        IReadOnlyList<ItemDefinition> definitions,
        string persistenceName)
    {
        if (definitions == null || string.IsNullOrWhiteSpace(persistenceName))
        {
            return null;
        }

        string normalizedName = persistenceName.Trim();
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null
                && string.Equals(
                    GetPersistenceName(definition, definitions),
                    normalizedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        return ResolveByStableName(definitions, normalizedName);
    }

    public static string GetDisplayName(ItemDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        string itemName = string.IsNullOrWhiteSpace(definition.itemName)
            ? definition.name
            : definition.itemName;
        return itemName != null ? itemName.Trim() : string.Empty;
    }

    public static ItemDefinition ResolveById(IReadOnlyList<ItemDefinition> definitions, int itemId)
    {
        if (definitions == null || itemId < 0)
        {
            return null;
        }

        ItemDefinition exactDefinition = ResolveExactById(definitions, itemId);
        return exactDefinition != null
            ? exactDefinition
            : ResolveLegacyDefinition(definitions, itemId);
    }

    public static ItemDefinition ResolveExactById(IReadOnlyList<ItemDefinition> definitions, int itemId)
    {
        return definitions != null && itemId >= 0
            ? FindByExactId(definitions, itemId)
            : null;
    }

    public static ItemDefinition ResolveInstallationById(IReadOnlyList<ItemDefinition> definitions, int itemId)
    {
        if (definitions == null || itemId < 0)
        {
            return null;
        }

        ItemDefinition exactDefinition = FindByExactInstallationId(definitions, itemId);
        if (exactDefinition != null)
        {
            return exactDefinition;
        }

        ItemDefinition legacyDefinition = ResolveLegacyDefinition(definitions, itemId);
        return IsInstallationDefinition(legacyDefinition) ? legacyDefinition : null;
    }

    public static ItemDefinition ResolveByStableName(IReadOnlyList<ItemDefinition> definitions, string itemName)
    {
        return ResolveByStableName(definitions, itemName, false);
    }

    public static ItemDefinition ResolveInstallationByStableName(IReadOnlyList<ItemDefinition> definitions, string itemName)
    {
        return ResolveByStableName(definitions, itemName, true);
    }

    public static string NormalizeStableName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = StripGeneratedItemNamePrefix(StripUnityCloneSuffix(value.Trim()));

        char[] buffer = new char[value.Length];
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!char.IsLetterOrDigit(c))
            {
                continue;
            }

            buffer[count++] = char.ToLowerInvariant(c);
        }

        return count > 0 ? new string(buffer, 0, count) : string.Empty;
    }

    public static bool IsConveyorBelt2FDefinition(ItemDefinition definition)
    {
        if (definition == null)
        {
            return false;
        }

        if (definition.mapObject is ConvayorBelt2F)
        {
            return true;
        }

        return NameMatches(definition.itemName)
               || NameMatches(definition.name)
               || (definition.mapObject != null && NameMatches(definition.mapObject.name));
    }

    public static bool LooksLikeLegacyConveyorBelt2FState(
        int itemId,
        ItemDefinition resolvedDefinition,
        IReadOnlyList<Vector2Int> occupiedCoordinates)
    {
        return itemId == LegacyConveyorBelt2FItemId
               && !IsConveyorBelt2FDefinition(resolvedDefinition)
               && occupiedCoordinates != null
               && occupiedCoordinates.Count > 1;
    }

    public static ItemDefinition ResolveConveyorBelt2F(IReadOnlyList<ItemDefinition> definitions)
    {
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (IsConveyorBelt2FDefinition(definition))
            {
                return definition;
            }
        }

        return null;
    }

    private static ItemDefinition FindByExactInstallationId(IReadOnlyList<ItemDefinition> definitions, int itemId)
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId && IsInstallationDefinition(definition))
            {
                return definition;
            }
        }

        return null;
    }

    private static ItemDefinition FindByExactId(IReadOnlyList<ItemDefinition> definitions, int itemId)
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    private static ItemDefinition ResolveByStableName(
        IReadOnlyList<ItemDefinition> definitions,
        string itemName,
        bool requireInstallation)
    {
        if (definitions == null)
        {
            return null;
        }

        string normalizedName = NormalizeStableName(itemName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        ItemDefinition fallback = null;
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null
                || definition.id < 0
                || (requireInstallation && !IsInstallationDefinition(definition)))
            {
                continue;
            }

            if (NormalizeStableName(definition.itemName) == normalizedName)
            {
                return definition;
            }

            if (fallback == null
                && (NormalizeStableName(definition.name) == normalizedName
                    || (definition.mapObject != null && NormalizeStableName(definition.mapObject.name) == normalizedName)))
            {
                fallback = definition;
            }
        }

        return fallback;
    }

    private static ItemDefinition ResolveLegacyDefinition(IReadOnlyList<ItemDefinition> definitions, int itemId)
    {
        if (itemId != LegacyConveyorBelt2FItemId)
        {
            return null;
        }

        return ResolveConveyorBelt2F(definitions);
    }

    public static bool IsInstallationDefinition(ItemDefinition definition)
    {
        if (definition == null || definition.mapObject == null)
        {
            return false;
        }

        return definition.mapObject is InstallationObject
               || definition.mapObject.GetComponent<InstallationObject>() != null
               || definition.mapObject.GetComponentInChildren<InstallationObject>(true) != null;
    }

    private static string StripGeneratedItemNamePrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("Item_", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        int index = "Item_".Length;
        while (index < value.Length && char.IsDigit(value[index]))
        {
            index++;
        }

        return index < value.Length && value[index] == '_'
            ? value.Substring(index + 1)
            : value;
    }

    private static string StripUnityCloneSuffix(string value)
    {
        const string CloneSuffix = "(Clone)";
        return !string.IsNullOrWhiteSpace(value) && value.EndsWith(CloneSuffix, StringComparison.Ordinal)
            ? value.Substring(0, value.Length - CloneSuffix.Length)
            : value;
    }

    private static bool NameMatches(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && string.Equals(value.Trim(), ConveyorBelt2FItemName, StringComparison.OrdinalIgnoreCase);
    }
}
