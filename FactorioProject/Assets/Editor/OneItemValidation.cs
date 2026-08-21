using UnityEditor;
using UnityEngine;

public static class OneItemValidation
{
    [MenuItem("Tools/ProjectF/Validation/One Item Stack Capacity")]
    public static void Validate()
    {
        ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            SerializedObject serializedDefinition = new SerializedObject(definition);
            Require(
                serializedDefinition.FindProperty("oneItem") != null,
                "ItemDefinition에 One Item 직렬화 필드가 없습니다.");

            definition.oneItem = false;
            Require(
                ItemDefinition.ResolveStackCapacity(definition, 10) == 10,
                "일반 아이템의 스택 용량이 변경됩니다.");

            definition.oneItem = true;
            Require(
                ItemDefinition.ResolveStackCapacity(definition, 10) == 1,
                "One Item의 스택 용량이 1이 아닙니다.");
            Require(
                ItemDefinition.ResolveStackCapacity(definition, 1) == 1,
                "One Item의 스택 용량이 물리 용량을 넘어석니다.");

            Debug.Log("One Item stack capacity validation passed");
        }
        finally
        {
            Object.DestroyImmediate(definition);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
