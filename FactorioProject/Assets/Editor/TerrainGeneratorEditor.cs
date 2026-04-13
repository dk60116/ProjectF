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
    }
}
