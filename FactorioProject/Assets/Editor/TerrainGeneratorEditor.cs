using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

[CustomEditor(typeof(TerrainGenerator))]
public class TerrainGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawSerializedProperties();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "에디터에서 생성한 청크는 미리보기이며 씬에 저장되지 않습니다. 기존에 저장된 청크는 Clear Preview로 즉시 제거하거나, Generate 또는 Reset 후 씬을 저장하면 제거됩니다.",
            MessageType.Info);

        if (GUILayout.Button("Generate"))
        {
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
            serializedObject.ApplyModifiedProperties();
            TerrainGenerator generator = (TerrainGenerator)target;
            Selection.activeGameObject = generator.gameObject;
            Undo.RegisterCompleteObjectUndo(generator, "Generate Terrain");
            generator.Generate();
            EditorUtility.SetDirty(generator);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        if (GUILayout.Button("Reset"))
        {
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
            serializedObject.ApplyModifiedProperties();
            TerrainGenerator generator = (TerrainGenerator)target;
            Selection.activeGameObject = generator.gameObject;
            Undo.RegisterCompleteObjectUndo(generator, "Reset Terrain Chunks");
            generator.ResetChunks();
            EditorUtility.SetDirty(generator);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        if (GUILayout.Button("Clear Preview"))
        {
            TerrainGenerator generator = (TerrainGenerator)target;
            if (EditorUtility.DisplayDialog(
                    "Clear Terrain Preview",
                    "씬의 파생 지형 청크 미리보기를 제거합니다. 지형 시드와 게임 세이브 데이터는 유지됩니다.",
                    "Clear",
                    "Cancel"))
            {
                GUI.FocusControl(null);
                Selection.activeGameObject = generator.gameObject;
                generator.ClearEditorPreviewChunks();
                EditorUtility.SetDirty(generator);
                EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
            }
        }

        if (GUILayout.Button("Random Seed"))
        {
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
            serializedObject.ApplyModifiedProperties();
            TerrainGenerator generator = (TerrainGenerator)target;
            Selection.activeGameObject = generator.gameObject;
            Undo.RegisterCompleteObjectUndo(generator, "Randomize Terrain Seed");
            generator.RandomizeSeed();
            EditorUtility.SetDirty(generator);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Animal Test Harness", EditorStyles.boldLabel);
        if (GUILayout.Button("Sync Animal Definitions"))
        {
            TerrainGenerator generator = (TerrainGenerator)target;
            Undo.RegisterCompleteObjectUndo(generator, "Sync Animal Definitions");
            generator.SyncAnimalDefinitionsFromAssets();
            serializedObject.Update();
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        if (GUILayout.Button("Rebuild Loaded Animals"))
        {
            TerrainGenerator generator = (TerrainGenerator)target;
            generator.RebuildLoadedAnimals();
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        if (GUILayout.Button("Remove Non-Interacted Animals"))
        {
            TerrainGenerator generator = (TerrainGenerator)target;
            int removedCount = generator.RemoveNonInteractedAnimalsFromLoadedChunks();
            Debug.Log($"Removed {removedCount} non-interacted terrain animals.", generator);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        if (GUILayout.Button("Clear Loaded Animal Views"))
        {
            TerrainGenerator generator = (TerrainGenerator)target;
            int removedCount = generator.ClearLoadedAnimalViews();
            Debug.Log($"Cleared {removedCount} loaded terrain animal views.", generator);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        if (GUILayout.Button("Log Animal Spawn Stats"))
        {
            ((TerrainGenerator)target).LogAnimalSpawnStats();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Open Terrain Editor"))
        {
            TerrainDataEditorWindow.ShowWindow();
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
                ApplyPersistedFoldoutState(iterator);
                EditorGUILayout.PropertyField(iterator, true);
                PersistFoldoutState(iterator);
            }
        }
    }

    private void ApplyPersistedFoldoutState(SerializedProperty property)
    {
        if (property == null || !property.hasVisibleChildren)
        {
            return;
        }

        property.isExpanded = SessionState.GetBool(GetFoldoutStateKey(property.propertyPath), property.isExpanded);
        if (!property.isExpanded)
        {
            return;
        }

        VisitChildProperties(property, child =>
        {
            if (!child.hasVisibleChildren)
            {
                return;
            }

            child.isExpanded = SessionState.GetBool(GetFoldoutStateKey(child.propertyPath), child.isExpanded);
        });
    }

    private void PersistFoldoutState(SerializedProperty property)
    {
        if (property == null || !property.hasVisibleChildren)
        {
            return;
        }

        SessionState.SetBool(GetFoldoutStateKey(property.propertyPath), property.isExpanded);

        VisitChildProperties(property, child =>
        {
            if (!child.hasVisibleChildren)
            {
                return;
            }

            SessionState.SetBool(GetFoldoutStateKey(child.propertyPath), child.isExpanded);
        });
    }

    private string GetFoldoutStateKey(string propertyPath)
    {
        int instanceId = target != null ? target.GetInstanceID() : 0;
        return $"TerrainGeneratorEditor.Foldout.{instanceId}.{propertyPath}";
    }

    private static void VisitChildProperties(SerializedProperty rootProperty, System.Action<SerializedProperty> visitor)
    {
        if (rootProperty == null || visitor == null)
        {
            return;
        }

        SerializedProperty iterator = rootProperty.Copy();
        SerializedProperty endProperty = iterator.GetEndProperty();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty))
        {
            visitor(iterator);
            enterChildren = true;
        }
    }
}
