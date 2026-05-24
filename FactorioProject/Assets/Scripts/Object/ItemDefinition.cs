using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ProjectF/Item Definition", fileName = "ItemDef_")]
public class ItemDefinition : ScriptableObject
{
    private const float DefaultCraftingDurationSeconds = 5f;

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
    public EnergyType energyType = EnergyType.None;
    [Min(0)]
    public int energyAmount = 0;
    public EnergyType useEnergyType = EnergyType.None;
    [Min(0f)]
    public float useEnergyAmount = 0f;
    [Min(0f)]
    public float completeEnergy = 0f;
    [SerializeField, Min(0.01f)]
    private float craftingDurationSeconds = DefaultCraftingDurationSeconds;

    public float CraftingDurationSeconds => craftingDurationSeconds > 0f ? craftingDurationSeconds : DefaultCraftingDurationSeconds;

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

        ItemDefinition exactDefinition = FindByExactId(definitions, itemId);
        if (exactDefinition != null)
        {
            return IsInstallationDefinition(exactDefinition) ? exactDefinition : null;
        }

        ItemDefinition legacyDefinition = ResolveLegacyDefinition(definitions, itemId);
        return IsInstallationDefinition(legacyDefinition) ? legacyDefinition : null;
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

    private static bool NameMatches(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && string.Equals(value.Trim(), ConveyorBelt2FItemName, StringComparison.OrdinalIgnoreCase);
    }
}
