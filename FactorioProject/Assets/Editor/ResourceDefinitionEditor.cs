using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ResourceDefinition))]
[CanEditMultipleObjects]
public sealed class ResourceDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(targets.Length != 1))
        {
            if (GUILayout.Button("Open Resource Data UI"))
            {
                ResourceDataEditorWindow.ShowWindowAndSelect(target as ResourceDefinition);
            }
        }
    }
}
