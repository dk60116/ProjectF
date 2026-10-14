using ProjectF.Monsters;
using UnityEditor;
using UnityEngine;

namespace ProjectF.Editor.Monsters
{
    [CustomEditor(typeof(MonsterDefinition))]
    public sealed class MonsterDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Open Monster Data"))
                MonsterDataEditorWindow.ShowWindowAndSelect((MonsterDefinition)target);
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
        }
    }
}
