using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AnimalDefinition))]
public sealed class AnimalDefinitionEditor : Editor
{
    private int draftId;
    private string draftName;
    private GameObject draftPrefab;
    private Sprite draftAdultIcon;
    private Sprite draftChildIcon;
    private bool dirty;

    private void OnEnable()
    {
        ReloadDraft();
    }

    public override void OnInspectorGUI()
    {
        AnimalDefinition definition = (AnimalDefinition)target;

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Script", MonoScript.FromScriptableObject(definition), typeof(MonoScript), false);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(4f);
        EditorGUI.BeginChangeCheck();
        int nextId = EditorGUILayout.IntField("Animal ID", draftId);
        string nextName = EditorGUILayout.TextField("Animal Name", draftName ?? string.Empty);
        GameObject nextPrefab = (GameObject)EditorGUILayout.ObjectField("Animal Prefab", draftPrefab, typeof(GameObject), false);
        Sprite nextAdultIcon = (Sprite)EditorGUILayout.ObjectField("Adult Icon", draftAdultIcon, typeof(Sprite), false);
        Sprite nextChildIcon = (Sprite)EditorGUILayout.ObjectField("Child Icon", draftChildIcon, typeof(Sprite), false);
        if (EditorGUI.EndChangeCheck())
        {
            draftId = Mathf.Max(-1, nextId);
            draftName = nextName;
            draftPrefab = nextPrefab;
            draftAdultIcon = nextAdultIcon;
            draftChildIcon = nextChildIcon;
            dirty = true;
        }

        if (dirty)
        {
            EditorGUILayout.HelpBox("Save를 눌러야 변경 사항이 Asset에 반영됩니다.", MessageType.Info);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save"))
        {
            AnimalDataEditorUtility.ApplyDefinition(
                definition,
                draftId,
                draftName,
                draftPrefab,
                draftAdultIcon,
                draftChildIcon,
                definition.SpawnAgeWeight,
                definition.MinHerdSize,
                definition.MaxHerdSize,
                definition.SpawnWeight,
                "Save Animal Definition");
            AssetDatabase.SaveAssets();
            ReloadDraft();
        }

        EditorGUI.BeginDisabledGroup(!dirty);
        if (GUILayout.Button("Revert"))
        {
            ReloadDraft();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Open Animal Data Editor"))
        {
            AnimalDataEditorWindow.ShowWindowAndSelect(definition);
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
        List<AnimalDefinition> definitions = AnimalDataEditorUtility.LoadDefinitions();
        List<AnimalValidationIssue> issues = AnimalDataEditorUtility.ValidateDefinition(definition, definitions);
        if (issues.Count == 0)
        {
            EditorGUILayout.HelpBox("문제를 찾지 못했습니다.", MessageType.Info);
        }
        else
        {
            for (int i = 0; i < issues.Count; i++)
            {
                MessageType type = issues[i].severity == AnimalValidationSeverity.Error
                    ? MessageType.Error
                    : issues[i].severity == AnimalValidationSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;
                EditorGUILayout.HelpBox(issues[i].message, type);
            }
        }
    }

    private void ReloadDraft()
    {
        AnimalDefinition definition = target as AnimalDefinition;
        if (definition == null)
        {
            return;
        }

        draftId = definition.Id;
        draftName = definition.AnimalName;
        draftPrefab = definition.AnimalPrefab;
        draftAdultIcon = definition.AdultIcon;
        draftChildIcon = definition.ChildIcon;
        dirty = false;
        Repaint();
    }
}
