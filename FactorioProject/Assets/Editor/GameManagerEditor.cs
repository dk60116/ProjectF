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

            if (GUILayout.Button(gameManager.FreeElectroEnergy
                    ? "Disable Free Electro Energy"
                    : "Enable Free Electro Energy"))
            {
                ToggleRuntimeBool(
                    gameManager,
                    gameManager.FreeElectroEnergy,
                    value => gameManager.SetFreeElectroEnergy(value),
                    "Toggle Free Electro Energy");
            }

            if (GUILayout.Button(gameManager.FreeBucket
                    ? "Disable Free Bucket"
                    : "Enable Free Bucket"))
            {
                ToggleRuntimeBool(
                    gameManager,
                    gameManager.FreeBucket,
                    value => gameManager.SetFreeBucket(value),
                    "Toggle Free Bucket");
            }

            DrawWorldTimeControls(gameManager);
        }
    }

    private static void DrawWorldTimeControls(GameManager gameManager)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("World Time", EditorStyles.boldLabel);

        WorldTimeService worldTime = gameManager.WorldTime;
        EditorGUILayout.LabelField(
            worldTime != null ? worldTime.ClockText : "WorldTimeService unavailable");
        if (worldTime == null)
        {
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(worldTime.Paused ? "Resume" : "Pause"))
            {
                gameManager.SetWorldTimePaused(!worldTime.Paused);
            }

            if (GUILayout.Button("1x"))
            {
                gameManager.SetWorldTimeScale(1f);
                gameManager.SetWorldTimePaused(false);
            }

            if (GUILayout.Button("10x"))
            {
                gameManager.SetWorldTimeScale(10f);
                gameManager.SetWorldTimePaused(false);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("06:00"))
            {
                gameManager.TrySetWorldTime(6, 0);
            }

            if (GUILayout.Button("08:00"))
            {
                gameManager.TrySetWorldTime(8, 0);
            }

            if (GUILayout.Button("18:00"))
            {
                gameManager.TrySetWorldTime(18, 0);
            }
        }

        if (GUILayout.Button("Next Sunrise"))
        {
            gameManager.AdvanceWorldTimeToNextSunrise();
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
