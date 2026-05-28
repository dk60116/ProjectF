using ProjectF.Attributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PropObj : BaseObject
{
    [SerializeField, ReadOnly]
    protected int objId;

    [SerializeField]
    private ItemDefinition itemDefinition;

    [SerializeField, ReadOnly]
    protected PortableObject portableObj;

    protected void Awake()
    {
        portableObj = GetComponentInChildren<PortableObject>(true);

        if (portableObj != null)
        {
            portableObj.SetItem(ResolveItemId());
        }
    }

    public int ID => ResolveItemId();

    public int ResolvedItemId => ResolveItemId();

    public ItemDefinition BoundItemDefinition => itemDefinition;

    public int ResolveItemId()
    {
        if (itemDefinition != null)
        {
            return itemDefinition.id;
        }

        if (this is InstallationObject installationObject
            && TryResolveInstallationItemDefinition(installationObject, out ItemDefinition resolvedDefinition))
        {
            return resolvedDefinition.id;
        }

        return objId;
    }

    private static bool TryResolveInstallationItemDefinition(
        InstallationObject installationObject,
        out ItemDefinition resolvedDefinition)
    {
        resolvedDefinition = null;
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        if (installationObject == null || definitions == null || definitions.Count <= 0)
        {
            return false;
        }

        Type objectType = installationObject.GetType();
        string objectName = NormalizeLookupName(installationObject.objectName);
        string gameObjectName = NormalizeLookupName(installationObject.gameObject != null
            ? installationObject.gameObject.name
            : null);

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            MapObject definitionMapObject = definition != null ? definition.mapObject : null;
            if (definition == null || definition.id < 0 || definitionMapObject == null)
            {
                continue;
            }

            Type definitionType = definitionMapObject.GetType();
            if (definitionType != objectType
                && !definitionType.IsAssignableFrom(objectType)
                && !objectType.IsAssignableFrom(definitionType))
            {
                continue;
            }

            if (!MatchesLookupName(objectName, definition)
                && !MatchesLookupName(gameObjectName, definition))
            {
                continue;
            }

            resolvedDefinition = definition;
            return true;
        }

        return false;
    }

    private static bool MatchesLookupName(string lookupName, ItemDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(lookupName) || definition == null)
        {
            return false;
        }

        return lookupName == NormalizeLookupName(definition.itemName)
               || lookupName == NormalizeLookupName(definition.name)
               || (definition.mapObject != null && lookupName == NormalizeLookupName(definition.mapObject.name));
    }

    private static string NormalizeLookupName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace("(Clone)", string.Empty)
            .Replace("_Blueprint", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Trim()
            .ToLowerInvariant();
    }
}
