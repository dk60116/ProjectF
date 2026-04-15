using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ItemManager))]
public class ItemManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Rebuild"))
        {
            ItemManager itemManager = (ItemManager)target;
            Undo.RecordObject(itemManager, "Rebuild Item Data");
            itemManager.RebuildItemDefinitionsFromAssets();
            itemManager.ApplyItemIdsToPrefabs();
            EditorUtility.SetDirty(itemManager);
        }

        if (GUILayout.Button("Open Item Data UI"))
        {
            ItemDataEditorWindow.ShowWindow();
        }

        if (GUILayout.Button("Open Crafting Tree UI"))
        {
            CraftingTreeEditorWindow.ShowWindow();
        }
    }
}
