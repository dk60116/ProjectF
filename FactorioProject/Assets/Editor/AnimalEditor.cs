using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Animal), true)]
public sealed class AnimalEditor : Editor
{
    private AnimalDefinition linkedDefinition;

    private void OnEnable()
    {
        linkedDefinition = AnimalDataEditorUtility.FindDefinitionForAnimal(target as Animal);
    }

    public override void OnInspectorGUI()
    {
        Animal animal = (Animal)target;
        AnimalDefinition currentDefinition = AnimalDataEditorUtility.FindDefinitionForAnimal(animal);
        if (linkedDefinition != currentDefinition)
        {
            linkedDefinition = currentDefinition;
        }

        serializedObject.Update();

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(animal), typeof(MonoScript), false);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Components", EditorStyles.boldLabel);
        DrawEditableProperty("anim", "Animator");
        DrawEditableProperty("capsuleCollider", "Capsule Collider");
        DrawEditableProperty("saddleObject", "Saddle Object");

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
        DrawEditableProperty("animalGender", "Gender");

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Growth Preview", EditorStyles.boldLabel);
        DrawEditableProperty("DinoAge", "Growth");
        DrawUniformScaleProperty("adultScale", "Adult Scale");
        DrawEditableProperty("BaseScale", "Base Scale");
        DrawEditableProperty("BabyScale", "Baby Scale");
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Animal Definition", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Linked Definition", linkedDefinition, typeof(AnimalDefinition), false);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh Link"))
        {
            linkedDefinition = AnimalDataEditorUtility.FindDefinitionForAnimal(animal);
        }

        if (GUILayout.Button("Open Animal Data"))
        {
            AnimalDataEditorWindow.ShowWindowAndSelect(linkedDefinition);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Auto-filled References (Read Only)", EditorStyles.boldLabel);
        AnimalDataEditorUtility.DrawReadOnlyAnimalReferences(animal);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
        List<AnimalValidationIssue> issues = AnimalDataEditorUtility.ValidateAnimal(animal);
        if (linkedDefinition == null)
        {
            issues.Insert(0, new AnimalValidationIssue(
                AnimalValidationSeverity.Warning,
                "이 프리팹에 연결된 AnimalDefinition을 찾지 못했습니다.",
                animal));
        }

        if (issues.Count == 0)
        {
            EditorGUILayout.HelpBox("문제를 찾지 못했습니다.", MessageType.Info);
            return;
        }

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

    private void DrawEditableProperty(string propertyName, string label)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
    }

    private void DrawUniformScaleProperty(string propertyName, string label)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            return;
        }

        EditorGUI.BeginChangeCheck();
        float scale = EditorGUILayout.FloatField(
            new GUIContent(label, "Growth 10 기준으로 X, Y, Z에 동일하게 적용되는 모델 크기입니다."),
            property.vector3Value.x);
        if (EditorGUI.EndChangeCheck())
        {
            scale = Mathf.Max(0f, scale);
            property.vector3Value = Vector3.one * scale;
        }
    }
}
