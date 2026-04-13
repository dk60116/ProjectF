using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ItemManager))]
public class ItemManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Rebuild Items From Assets"))
        {
            ItemManager itemManager = (ItemManager)target;
            Undo.RecordObject(itemManager, "Rebuild Items From Assets");
            itemManager.RebuildItemsFromAssets();
            EditorUtility.SetDirty(itemManager);
        }

        if (GUILayout.Button("Apply IDs To Prefabs"))
        {
            ItemManager itemManager = (ItemManager)target;
            itemManager.ApplyItemIdsToPrefabs();
            EditorUtility.SetDirty(itemManager);
        }
    }
}
