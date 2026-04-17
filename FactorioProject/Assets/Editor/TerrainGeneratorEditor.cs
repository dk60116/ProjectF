using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

[CustomEditor(typeof(TerrainGenerator))]
public class TerrainGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate"))
        {
            TerrainGenerator generator = (TerrainGenerator)target;
            Selection.activeGameObject = generator.gameObject;
            Undo.RegisterCompleteObjectUndo(generator, "Generate Terrain");
            generator.Generate();
            EditorUtility.SetDirty(generator);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        if (GUILayout.Button("Reset"))
        {
            TerrainGenerator generator = (TerrainGenerator)target;
            Selection.activeGameObject = generator.gameObject;
            Undo.RegisterCompleteObjectUndo(generator, "Reset Terrain Chunks");
            generator.ResetChunks();
            EditorUtility.SetDirty(generator);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        if (GUILayout.Button("Random Seed"))
        {
            TerrainGenerator generator = (TerrainGenerator)target;
            Selection.activeGameObject = generator.gameObject;
            Undo.RegisterCompleteObjectUndo(generator, "Randomize Terrain Seed");
            generator.RandomizeSeed();
            EditorUtility.SetDirty(generator);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }
}
