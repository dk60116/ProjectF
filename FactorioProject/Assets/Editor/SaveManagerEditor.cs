using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SaveManager))]
public class SaveManagerEditor : Editor
{
    private SerializedProperty selectedSlotIndex;
    private SerializedProperty loadRecentSlotOnStart;
    private SerializedProperty randomizeEmptySlotMap;

    private void OnEnable()
    {
        selectedSlotIndex = serializedObject.FindProperty("selectedSlotIndex");
        loadRecentSlotOnStart = serializedObject.FindProperty("loadRecentSlotOnStart");
        randomizeEmptySlotMap = serializedObject.FindProperty("randomizeEmptySlotMap");
    }

    public override void OnInspectorGUI()
    {
        SaveManager saveManager = (SaveManager)target;

        serializedObject.Update();

        EditorGUILayout.LabelField("Save / Load", EditorStyles.boldLabel);
        string[] slotLabels = saveManager.BuildSlotLabels();
        int selectedSlot = Mathf.Clamp(selectedSlotIndex.intValue, 0, SaveManager.SlotCount - 1);
        selectedSlotIndex.intValue = EditorGUILayout.Popup("Slot", selectedSlot, slotLabels);

        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Save"))
            {
                serializedObject.ApplyModifiedProperties();
                saveManager.SaveSelectedSlot();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("Load"))
            {
                serializedObject.ApplyModifiedProperties();
                saveManager.LoadSelectedSlot();
                GUIUtility.ExitGUI();
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Save/Load 버튼은 Play Mode에서 동작합니다.", MessageType.Info);
        }

        int slot = Mathf.Clamp(selectedSlotIndex.intValue, 0, SaveManager.SlotCount - 1);
        EditorGUILayout.LabelField("Path", saveManager.GetSlotPath(slot));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(loadRecentSlotOnStart);
        EditorGUILayout.PropertyField(randomizeEmptySlotMap);

        serializedObject.ApplyModifiedProperties();
    }
}
