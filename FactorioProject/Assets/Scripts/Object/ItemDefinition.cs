using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ProjectF/Item Definition", fileName = "ItemDef_")]
public class ItemDefinition : ScriptableObject
{
    private const float DefaultCraftingDurationSeconds = 5f;
    private const float KilowattsToWatts = 1000f;

    public enum EnergyType { None, Burn, Electricity }

    public string itemName;
    public int id;
    public MapObject mapObject;
    public Mesh portableMesh;
    public Material portableMat;
    public Sprite icon;
    public List<Sprite> interactionButtonList = new List<Sprite>();
    public uint size;
    public bool itemFilter;
    [Min(1)]
    public int capacity = 10;
    public bool storesFluid;
    [Min(0f)]
    public float fluidStorageLiters = 0f;
    public Color fluidDisplayColor = Color.white;
    public EnergyType energyType = EnergyType.None;
    [Min(0)]
    public int energyAmount = 0;
    public EnergyType useEnergyType = EnergyType.None;
    [Min(0f)]
    public float useEnergyAmount = 0f;
    [Min(0f)]
    public float completeEnergy = 0f;
    [Min(0)]
    public int utilityPoleConnectionRadius = 6;
    [Min(0)]
    public int utilityPoleSupplyRadius = 3;
    [SerializeField, Min(0.01f)]
    private float craftingDurationSeconds = DefaultCraftingDurationSeconds;

    public float CraftingDurationSeconds => craftingDurationSeconds > 0f ? craftingDurationSeconds : DefaultCraftingDurationSeconds;
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
    }
#endif
}

public static class ItemDefinitionLookup
{
    private const int LegacyConveyorBelt2FItemId = 26;
    private const string ConveyorBelt2FItemName = "Conveyor belt 2F";

    public static ItemDefinition ResolveById(IReadOnlyList<ItemDefinition> definitions, int itemId)
    {
        if (definitions == null || itemId < 0)
        {
            return null;
        }

        ItemDefinition exactDefinition = FindByExactId(definitions, itemId);
        return exactDefinition != null
            ? exactDefinition
            : ResolveLegacyDefinition(definitions, itemId);
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

    private static bool IsInstallationDefinition(ItemDefinition definition)
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
