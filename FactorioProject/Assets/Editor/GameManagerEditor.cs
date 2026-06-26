using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(GameManager))]
public class GameManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawSerializedProperties();
        serializedObject.ApplyModifiedProperties();

        GameManager gameManager = target as GameManager;
        if (gameManager == null)
        {
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Toggles", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button(gameManager.FreeTrain ? "Disable Free Train" : "Enable Free Train"))
            {
                ToggleRuntimeBool(gameManager, gameManager.FreeTrain, value => gameManager.SetFreeTrain(value), "Toggle Free Train");
            }

            if (GUILayout.Button(gameManager.FreeCamera ? "Disable Free Camera" : "Enable Free Camera"))
            {
                ToggleRuntimeBool(gameManager, gameManager.FreeCamera, value => gameManager.SetFreeCamera(value), "Toggle Free Camera");
            }
        }
    }

    private void DrawSerializedProperties()
    {
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            using (new EditorGUI.DisabledScope(iterator.propertyPath == "m_Script"))
            {
                EditorGUILayout.PropertyField(iterator, true);
            }
        }
    }

    private static void ToggleRuntimeBool(GameManager gameManager, bool currentValue, System.Action<bool> setter, string undoLabel)
    {
        if (gameManager == null || setter == null)
        {
            return;
        }

        Undo.RegisterCompleteObjectUndo(gameManager, undoLabel);
        setter(!currentValue);
        EditorUtility.SetDirty(gameManager);
        if (!Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(gameManager.gameObject.scene);
        }
    }
}
