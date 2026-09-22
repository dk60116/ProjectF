using System;
using UnityEditor;
using UnityEngine;

// Run with Unity Pipeline run_script. Uses only transient objects and leaves no assets/scenes dirty.
public static class RectGridBlockDetailChecks
{
    public static string Main()
    {
        GameObject owner = null;
        ItemDefinition inputDefinition = null;
        ItemDefinition outputDefinition = null;
        try
        {
            owner = new GameObject("RectGridBlockDetailChecks");
            owner.hideFlags = HideFlags.HideAndDontSave;
            InputOutputModule module = owner.AddComponent<InputOutputModule>();
            module.ConfigureRectGrid(2, 1);
            module.SetRectGridBlock(0, 0, InputOutputModule.RectGridBlockType.InputItem);
            module.SetRectGridBlock(1, 0, InputOutputModule.RectGridBlockType.Output);

            inputDefinition = ScriptableObject.CreateInstance<ItemDefinition>();
            outputDefinition = ScriptableObject.CreateInstance<ItemDefinition>();
            inputDefinition.name = "Configured Input";
            outputDefinition.name = "Configured Output";

            using (var serialized = new SerializedObject(module))
            {
                SerializedProperty placements = serialized.FindProperty("rectGridPlacements");
                Require(placements != null && placements.arraySize == 2, "RectGrid placements were not serialized.");
                SetConfiguredItem(placements, 0, 0, inputDefinition);
                SetConfiguredItem(placements, 1, 0, outputDefinition);
                Require(serialized.ApplyModifiedPropertiesWithoutUndo(), "Configured item references were not applied.");
            }

            RequireConfiguredItem(module, 0, 0, inputDefinition, InputOutputModule.RectGridBlockType.InputItem);
            RequireConfiguredItem(module, 1, 0, outputDefinition, InputOutputModule.RectGridBlockType.Output);

            module.MoveOrSwapRectGridBlock(new Vector2Int(0, 0), new Vector2Int(1, 0));
            RequireConfiguredItem(module, 1, 0, inputDefinition, InputOutputModule.RectGridBlockType.InputItem);
            RequireConfiguredItem(module, 0, 0, outputDefinition, InputOutputModule.RectGridBlockType.Output);
            return "RectGrid block detail checks passed: serialized references and move/swap preservation.";
        }
        finally
        {
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            if (inputDefinition != null) UnityEngine.Object.DestroyImmediate(inputDefinition);
            if (outputDefinition != null) UnityEngine.Object.DestroyImmediate(outputDefinition);
        }
    }

    private static void SetConfiguredItem(
        SerializedProperty placements,
        int x,
        int y,
        ItemDefinition definition)
    {
        for (int i = 0; i < placements.arraySize; i++)
        {
            SerializedProperty placement = placements.GetArrayElementAtIndex(i);
            if (placement.FindPropertyRelative("x").intValue != x
                || placement.FindPropertyRelative("y").intValue != y)
            {
                continue;
            }

            SerializedProperty itemDefinition = placement.FindPropertyRelative("itemDefinition");
            Require(itemDefinition != null, "RectGrid itemDefinition field is missing.");
            itemDefinition.objectReferenceValue = definition;
            return;
        }

        throw new InvalidOperationException($"RectGrid cell ({x}, {y}) was not found.");
    }

    private static void RequireConfiguredItem(
        InputOutputModule module,
        int x,
        int y,
        ItemDefinition expectedDefinition,
        InputOutputModule.RectGridBlockType expectedType)
    {
        foreach (InputOutputModule.RectGridBlockPlacement placement in module.RectGridPlacements)
        {
            if (placement.x != x || placement.y != y)
            {
                continue;
            }

            Require(placement.blockType == expectedType, $"Unexpected block type at ({x}, {y}).");
            Require(placement.itemDefinition == expectedDefinition, $"Configured item was lost at ({x}, {y}).");
            return;
        }

        throw new InvalidOperationException($"RectGrid cell ({x}, {y}) was not found.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
