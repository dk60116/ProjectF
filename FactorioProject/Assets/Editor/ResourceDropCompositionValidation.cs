using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ResourceDropCompositionValidation
{
    private const string QuartziteDefinitionPath =
        "Assets/Data/MapObject/Resource_Quartzite.asset";

    [MenuItem("Tools/ProjectF/Validation/Resource Drop Composition")]
    public static void ValidateResourceDropComposition()
    {
        ItemDefinition stone = ScriptableObject.CreateInstance<ItemDefinition>();
        ItemDefinition quartz = ScriptableObject.CreateInstance<ItemDefinition>();
        stone.id = 3;
        quartz.id = 117;

        ResourceDropItem stoneItem = new ResourceDropItem
        {
            ItemDefinition = stone,
            Amount = 1,
            DropChance = 0.9f
        };
        ResourceDropItem quartzItem = new ResourceDropItem
        {
            ItemDefinition = quartz,
            Amount = 1,
            DropChance = 0.1f
        };
        ResourceDropEntry entry = new ResourceDropEntry();
        entry.AddItem(stoneItem);
        entry.AddItem(quartzItem);
        ResourceDropEntry[] entries = { entry };

        try
        {
            ValidateDistribution(entries, stone, quartz, 10, 8137, 9, 1);
            ValidateDistribution(entries, stone, quartz, 100, 8137, 90, 10);
            ValidateDistribution(entries, stone, quartz, 1000, 8137, 900, 100);
            ValidateBalancedDistribution(entries, stone, quartz, 1000, 8137);
            ValidateStableSequence(entries, 100, 8137);
            ValidateQuartziteDefinition();
            ValidateNestedResourceAssets();
            Debug.Log(
                "Resource Drop Composition validation passed: 90/10 weights produce exact "
                + "whole-deposit allocations with a deterministic balanced order.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(stone);
            UnityEngine.Object.DestroyImmediate(quartz);
        }
    }

    private static void ValidateNestedResourceAssets()
    {
        string[] guids = AssetDatabase.FindAssets(
            string.Empty,
            new[] { "Assets/Data/MapObject" });
        for (int assetIndex = 0; assetIndex < guids.Length; assetIndex++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[assetIndex]);
            ResourceDefinition definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(path);
            if (definition == null)
            {
                continue;
            }

            SerializedObject serializedDefinition = new SerializedObject(definition);
            SerializedProperty entries = serializedDefinition.FindProperty("dropItems");
            for (int entryIndex = 0;
                 entries != null && entryIndex < entries.arraySize;
                 entryIndex++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(entryIndex);
                SerializedProperty items = entry.FindPropertyRelative("items");
                if (items == null || items.arraySize == 0)
                {
                    throw new InvalidOperationException(
                        $"Resource drop entry was not migrated to nested items: {path}, "
                        + $"entry {entryIndex + 1}.");
                }
            }
        }
    }

    private static void ValidateQuartziteDefinition()
    {
        ResourceDefinition definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(
            QuartziteDefinitionPath);
        if (definition == null || definition.DropItems.Count != 1)
        {
            throw new InvalidOperationException(
                "Quartzite must have exactly one drop entry.");
        }

        Resource prefab = definition.prefab;
        if (prefab == null
            || prefab.Definition != definition
            || !string.Equals(
                prefab.ObjectName,
                definition.resourceName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Quartzite prefab must use the Quartzite definition and resource name.");
        }

        IReadOnlyList<ResourceDropItem> items = definition.DropItems[0].Items;
        if (items.Count != 2)
        {
            throw new InvalidOperationException(
                "Quartzite drop entry must contain exactly two items.");
        }

        ResourceDropItem stone = items[0];
        ResourceDropItem quartz = items[1];
        if (!IsExpectedEntry(stone, "Stone", 0.9f)
            || !IsExpectedEntry(quartz, "Quartz", 0.1f))
        {
            throw new InvalidOperationException(
                "Quartzite composition must be Stone 90% and Quartz 10%.");
        }
    }

    private static bool IsExpectedEntry(
        ResourceDropItem item,
        string itemName,
        float expectedWeight)
    {
        return item?.ItemDefinition != null
               && string.Equals(
                   item.ItemDefinition.itemName,
                   itemName,
                   StringComparison.OrdinalIgnoreCase)
               && Mathf.Approximately(item.DropChance, expectedWeight)
               && item.Amount == 1;
    }

    private static void ValidateDistribution(
        ResourceDropEntry[] entries,
        ItemDefinition stone,
        ItemDefinition quartz,
        int resourceCount,
        int seed,
        int expectedStone,
        int expectedQuartz)
    {
        int stoneCount = 0;
        int quartzCount = 0;
        for (int ordinal = 0; ordinal < resourceCount; ordinal++)
        {
            if (!ResourceDropSequence.TrySelect(
                    entries,
                    ResourceDefinition.MaxGrowth,
                    ordinal,
                    resourceCount,
                    seed,
                    out ResourceDropItem selected))
            {
                throw new InvalidOperationException(
                    $"Resource composition failed to select ordinal {ordinal}.");
            }

            if (selected.ItemDefinition == stone)
            {
                stoneCount++;
            }
            else if (selected.ItemDefinition == quartz)
            {
                quartzCount++;
            }
        }

        if (stoneCount != expectedStone || quartzCount != expectedQuartz)
        {
            throw new InvalidOperationException(
                $"Resource composition expected {expectedStone}/{expectedQuartz}, "
                + $"got {stoneCount}/{quartzCount} for {resourceCount} units.");
        }
    }

    private static void ValidateStableSequence(
        ResourceDropEntry[] entries,
        int resourceCount,
        int seed)
    {
        for (int ordinal = 0; ordinal < resourceCount; ordinal++)
        {
            ResourceDropSequence.TrySelect(
                entries,
                ResourceDefinition.MaxGrowth,
                ordinal,
                resourceCount,
                seed,
                out ResourceDropItem first);
            ResourceDropSequence.TrySelect(
                entries,
                ResourceDefinition.MaxGrowth,
                ordinal,
                resourceCount,
                seed,
                out ResourceDropItem second);
            if (!ReferenceEquals(first, second))
            {
                throw new InvalidOperationException(
                    $"Resource composition changed at ordinal {ordinal} for the same seed.");
            }
        }
    }

    private static void ValidateBalancedDistribution(
        ResourceDropEntry[] entries,
        ItemDefinition stone,
        ItemDefinition quartz,
        int resourceCount,
        int seed)
    {
        int consecutiveStone = 0;
        int maximumConsecutiveStone = 0;
        int consecutiveQuartz = 0;
        int maximumConsecutiveQuartz = 0;
        for (int ordinal = 0; ordinal < resourceCount; ordinal++)
        {
            ResourceDropSequence.TrySelect(
                entries,
                ResourceDefinition.MaxGrowth,
                ordinal,
                resourceCount,
                seed,
                out ResourceDropItem selected);
            if (selected?.ItemDefinition == quartz)
            {
                consecutiveQuartz++;
                consecutiveStone = 0;
            }
            else if (selected?.ItemDefinition == stone)
            {
                consecutiveStone++;
                consecutiveQuartz = 0;
            }

            maximumConsecutiveStone = Math.Max(maximumConsecutiveStone, consecutiveStone);
            maximumConsecutiveQuartz = Math.Max(maximumConsecutiveQuartz, consecutiveQuartz);
        }

        if (maximumConsecutiveStone > 10 || maximumConsecutiveQuartz > 1)
        {
            throw new InvalidOperationException(
                "Quartzite composition clustered instead of staying evenly distributed: "
                + $"Stone run {maximumConsecutiveStone}, Quartz run {maximumConsecutiveQuartz}.");
        }
    }
}
