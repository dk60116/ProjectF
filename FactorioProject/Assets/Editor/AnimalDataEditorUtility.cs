using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class ProjectFEditorGUIUtility
{
    public static void CommitAndReleaseKeyboardFocus()
    {
        GUI.FocusControl(null);
        GUIUtility.keyboardControl = 0;
        EditorGUIUtility.editingTextField = false;
    }
}

internal enum AnimalValidationSeverity
{
    Info,
    Warning,
    Error
}

internal readonly struct AnimalValidationIssue
{
    public readonly AnimalValidationSeverity severity;
    public readonly string message;
    public readonly UnityEngine.Object context;

    public AnimalValidationIssue(
        AnimalValidationSeverity severity,
        string message,
        UnityEngine.Object context)
    {
        this.severity = severity;
        this.message = message;
        this.context = context;
    }
}

internal static class AnimalDataEditorUtility
{
    public const string DefinitionRoot = "Assets/Animals";
    public const string PrefabRoot = "Assets/AnimalObject";
    public const string DefaultJsonPath = DefinitionRoot + "/animal_data.json";

    private static readonly string[] RequiredAnimalReferencePropertyNames =
    {
        "dinoRenderer",
        "Eye",
        "headBone",
        "dinoTransform",
        "youngDinoLeftEye",
        "youngDinoRightEye",
        "oldDinoLeftEye",
        "oldDinoRightEye"
    };

    private static readonly string[] RequiredAnimalReferenceLabels =
    {
        "Growth Renderer",
        "Eye Prefab",
        "Head Bone",
        "Dino Transform",
        "Young Left Eye",
        "Young Right Eye",
        "Adult Left Eye",
        "Adult Right Eye"
    };

    public static List<AnimalDefinition> LoadDefinitions()
    {
        List<AnimalDefinition> definitions = new List<AnimalDefinition>();
        string[] guids = AssetDatabase.FindAssets("t:AnimalDefinition", new[] { DefinitionRoot });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AnimalDefinition definition = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(path);
            if (definition != null)
            {
                definitions.Add(definition);
            }
        }

        definitions.Sort(CompareDefinitions);
        return definitions;
    }

    public static int CompareDefinitions(AnimalDefinition left, AnimalDefinition right)
    {
        string leftPath = left != null ? AssetDatabase.GetAssetPath(left) : string.Empty;
        string rightPath = right != null ? AssetDatabase.GetAssetPath(right) : string.Empty;
        int pathCompare = string.Compare(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
        if (pathCompare != 0)
        {
            return pathCompare;
        }

        int leftId = left != null ? left.Id : int.MaxValue;
        int rightId = right != null ? right.Id : int.MaxValue;
        return leftId.CompareTo(rightId);
    }

    public static string GetHierarchyPath(AnimalDefinition definition)
    {
        string assetPath = definition != null ? AssetDatabase.GetAssetPath(definition) : string.Empty;
        string directory = NormalizePath(Path.GetDirectoryName(assetPath));
        if (string.IsNullOrEmpty(directory)
            || string.Equals(directory, DefinitionRoot, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string prefix = DefinitionRoot + "/";
        return directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? directory.Substring(prefix.Length)
            : directory;
    }

    public static Animal FindAnimal(GameObject prefab)
    {
        return prefab != null ? prefab.GetComponentInChildren<Animal>(true) : null;
    }

    public static AnimalDefinition FindDefinitionForAnimal(Animal animal)
    {
        if (animal == null)
        {
            return null;
        }

        if (animal.Definition != null)
        {
            return animal.Definition;
        }

        string animalPrefabPath = NormalizePath(
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(animal.gameObject));
        if (string.IsNullOrEmpty(animalPrefabPath))
        {
            animalPrefabPath = NormalizePath(AssetDatabase.GetAssetPath(animal));
        }

        Animal sourceAnimal = PrefabUtility.GetCorrespondingObjectFromSource(animal);

        List<AnimalDefinition> definitions = LoadDefinitions();
        for (int i = 0; i < definitions.Count; i++)
        {
            AnimalDefinition definition = definitions[i];
            if (definition == null || definition.AnimalPrefab == null)
            {
                continue;
            }

            string definitionPrefabPath = NormalizePath(AssetDatabase.GetAssetPath(definition.AnimalPrefab));
            if ((!string.IsNullOrEmpty(animalPrefabPath)
                 && string.Equals(animalPrefabPath, definitionPrefabPath, StringComparison.OrdinalIgnoreCase))
                || (sourceAnimal != null && FindAnimal(definition.AnimalPrefab) == sourceAnimal))
            {
                return definition;
            }
        }

        return null;
    }

    public static void ApplyDefinition(
        AnimalDefinition definition,
        int id,
        string animalName,
        GameObject animalPrefab,
        Sprite adultIcon,
        Sprite childIcon,
        int spawnAge,
        int minHerdSize,
        int maxHerdSize,
        int spawnWeight,
        float maxHealth,
        bool canRiding,
        float riderHeight,
        float strength,
        AnimalAISettings aiSettings,
        IReadOnlyList<AnimalDropEntry> dropItems,
        string undoName)
    {
        if (definition == null)
        {
            return;
        }

        Undo.RecordObject(definition, undoName);
        SerializedObject serializedDefinition = new SerializedObject(definition);
        serializedDefinition.Update();
        serializedDefinition.FindProperty("id").intValue = Mathf.Max(-1, id);
        serializedDefinition.FindProperty("animalName").stringValue = animalName?.Trim() ?? string.Empty;
        serializedDefinition.FindProperty("animalPrefab").objectReferenceValue = animalPrefab;
        serializedDefinition.FindProperty("adultIcon").objectReferenceValue = adultIcon;
        serializedDefinition.FindProperty("childIcon").objectReferenceValue = childIcon;
        serializedDefinition.FindProperty("spawnAge").intValue = Mathf.Clamp(
            spawnAge,
            AnimalDefinition.MinSpawnAge,
            AnimalDefinition.MaxSpawnAge);
        int normalizedMinHerdSize = Mathf.Max(1, minHerdSize);
        int normalizedMaxHerdSize = Mathf.Max(normalizedMinHerdSize, maxHerdSize);
        serializedDefinition.FindProperty("minHerdSize").intValue = normalizedMinHerdSize;
        serializedDefinition.FindProperty("maxHerdSize").intValue = normalizedMaxHerdSize;
        serializedDefinition.FindProperty("spawnWeight").intValue = Mathf.Clamp(
            spawnWeight,
            normalizedMinHerdSize,
            normalizedMaxHerdSize);
        serializedDefinition.FindProperty("maxHealth").floatValue = Mathf.Max(1f, maxHealth);
        serializedDefinition.FindProperty("canRiding").boolValue = canRiding;
        serializedDefinition.FindProperty("riderHeight").floatValue = Mathf.Max(0f, riderHeight);
        serializedDefinition.FindProperty("strength").floatValue = Mathf.Clamp(
            strength,
            AnimalDefinition.MinStrength,
            AnimalDefinition.MaxStrength);
        SerializedProperty dropItemsProperty = serializedDefinition.FindProperty("dropItems");
        if (dropItemsProperty != null)
        {
            int dropCount = dropItems != null ? dropItems.Count : 0;
            dropItemsProperty.arraySize = dropCount;
            for (int i = 0; i < dropCount; i++)
            {
                AnimalDropEntry entry = dropItems[i] ?? new AnimalDropEntry();
                entry.Normalize();
                SerializedProperty element = dropItemsProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("itemDefinition").objectReferenceValue =
                    entry.ItemDefinition;
                element.FindPropertyRelative("minAmount").intValue = entry.MinAmount;
                element.FindPropertyRelative("maxAmount").intValue = entry.MaxAmount;
                element.FindPropertyRelative("dropChance").floatValue = entry.DropChance;
            }
        }

        serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

        AnimalAISettings normalizedAISettings =
            (aiSettings ?? new AnimalAISettings()).Clone();
        normalizedAISettings.Normalize();
        JsonUtility.FromJsonOverwrite(
            JsonUtility.ToJson(normalizedAISettings),
            definition.AISettings);
        EditorUtility.SetDirty(definition);
        EnsureDefinitionLink(definition);
    }

    public static bool EnsureDefinitionLink(AnimalDefinition definition)
    {
        if (definition == null || definition.AnimalPrefab == null)
        {
            return false;
        }

        Animal animal = FindAnimal(definition.AnimalPrefab);
        if (animal == null || animal.Definition == definition)
        {
            return false;
        }

        SerializedObject serializedAnimal = new SerializedObject(animal);
        serializedAnimal.Update();
        SerializedProperty definitionProperty = serializedAnimal.FindProperty("animalDefinition");
        if (definitionProperty == null)
        {
            return false;
        }

        definitionProperty.objectReferenceValue = definition;
        serializedAnimal.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(animal);
        PrefabUtility.SavePrefabAsset(definition.AnimalPrefab);
        return true;
    }

    public static List<AnimalValidationIssue> ValidateDefinition(
        AnimalDefinition definition,
        IReadOnlyList<AnimalDefinition> allDefinitions)
    {
        List<AnimalValidationIssue> issues = new List<AnimalValidationIssue>();
        if (definition == null)
        {
            issues.Add(new AnimalValidationIssue(
                AnimalValidationSeverity.Error,
                "AnimalDefinition이 없습니다.",
                null));
            return issues;
        }

        if (definition.Id < 0)
        {
            issues.Add(new AnimalValidationIssue(
                AnimalValidationSeverity.Error,
                "Animal ID가 지정되지 않았습니다.",
                definition));
        }
        else if (CountDefinitionsWithId(allDefinitions, definition.Id) > 1)
        {
            issues.Add(new AnimalValidationIssue(
                AnimalValidationSeverity.Error,
                $"Animal ID {definition.Id}가 중복되었습니다.",
                definition));
        }

        if (string.IsNullOrWhiteSpace(definition.AnimalName))
        {
            issues.Add(new AnimalValidationIssue(
                AnimalValidationSeverity.Error,
                "동물 이름이 비어 있습니다.",
                definition));
        }

        ValidateAssetReference(definition.AnimalPrefab, "동물 프리팹", PrefabRoot, definition, issues);
        ValidateAssetReference(definition.AdultIcon, "성체 아이콘", DefinitionRoot, definition, issues);
        ValidateAssetReference(definition.ChildIcon, "새끼 아이콘", DefinitionRoot, definition, issues);

        Animal animal = FindAnimal(definition.AnimalPrefab);
        if (definition.AnimalPrefab != null && animal == null)
        {
            issues.Add(new AnimalValidationIssue(
                AnimalValidationSeverity.Error,
                "프리팹에서 Animal 또는 Animal 파생 컴포넌트를 찾지 못했습니다.",
                definition.AnimalPrefab));
        }
        else if (animal != null)
        {
            ValidateAnimalReferences(animal, issues);
            if (definition.TryGetDeclaredGender(out Animal.AnimalGender expectedGender)
                && animal.Gender != expectedGender)
            {
                issues.Add(new AnimalValidationIssue(
                    AnimalValidationSeverity.Error,
                    $"동물 이름의 성별({expectedGender})과 프리팹 성별({animal.Gender})이 다릅니다.",
                    definition.AnimalPrefab));
            }
        }

        return issues;
    }

    public static List<AnimalValidationIssue> ValidateAnimal(Animal animal)
    {
        List<AnimalValidationIssue> issues = new List<AnimalValidationIssue>();
        if (animal == null)
        {
            issues.Add(new AnimalValidationIssue(
                AnimalValidationSeverity.Error,
                "Animal 컴포넌트가 없습니다.",
                null));
            return issues;
        }

        ValidateAnimalReferences(animal, issues);
        return issues;
    }

    public static void DrawReadOnlyAnimalReferences(Animal animal)
    {
        if (animal == null)
        {
            EditorGUILayout.HelpBox("Animal 컴포넌트가 없습니다.", MessageType.Error);
            return;
        }

        SerializedObject serializedAnimal = new SerializedObject(animal);
        serializedAnimal.Update();
        EditorGUI.BeginDisabledGroup(true);
        for (int i = 0; i < RequiredAnimalReferencePropertyNames.Length; i++)
        {
            SerializedProperty property = serializedAnimal.FindProperty(RequiredAnimalReferencePropertyNames[i]);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(RequiredAnimalReferenceLabels[i]));
            }
        }
        EditorGUI.EndDisabledGroup();
    }

    public static int CountErrors(IReadOnlyList<AnimalValidationIssue> issues)
    {
        int count = 0;
        for (int i = 0; issues != null && i < issues.Count; i++)
        {
            if (issues[i].severity == AnimalValidationSeverity.Error)
            {
                count++;
            }
        }

        return count;
    }

    public static int CountWarnings(IReadOnlyList<AnimalValidationIssue> issues)
    {
        int count = 0;
        for (int i = 0; issues != null && i < issues.Count; i++)
        {
            if (issues[i].severity == AnimalValidationSeverity.Warning)
            {
                count++;
            }
        }

        return count;
    }

    public static void LogValidation(IReadOnlyList<AnimalDefinition> definitions)
    {
        int errorCount = 0;
        int warningCount = 0;
        for (int i = 0; definitions != null && i < definitions.Count; i++)
        {
            AnimalDefinition definition = definitions[i];
            List<AnimalValidationIssue> issues = ValidateDefinition(definition, definitions);
            for (int issueIndex = 0; issueIndex < issues.Count; issueIndex++)
            {
                AnimalValidationIssue issue = issues[issueIndex];
                string prefix = definition != null ? $"[{definition.Id}] {definition.AnimalName}: " : string.Empty;
                if (issue.severity == AnimalValidationSeverity.Error)
                {
                    errorCount++;
                    Debug.LogError(prefix + issue.message, issue.context);
                }
                else if (issue.severity == AnimalValidationSeverity.Warning)
                {
                    warningCount++;
                    Debug.LogWarning(prefix + issue.message, issue.context);
                }
            }
        }

        Debug.Log($"Animal validation complete: {definitions?.Count ?? 0} definitions, "
                  + $"{errorCount} errors, {warningCount} warnings.");
    }

    private static int CountDefinitionsWithId(IReadOnlyList<AnimalDefinition> definitions, int id)
    {
        int count = 0;
        for (int i = 0; definitions != null && i < definitions.Count; i++)
        {
            if (definitions[i] != null && definitions[i].Id == id)
            {
                count++;
            }
        }

        return count;
    }

    private static void ValidateAssetReference(
        UnityEngine.Object value,
        string label,
        string expectedRoot,
        UnityEngine.Object context,
        List<AnimalValidationIssue> issues)
    {
        if (value == null)
        {
            issues.Add(new AnimalValidationIssue(
                AnimalValidationSeverity.Error,
                $"{label}이(가) 지정되지 않았습니다.",
                context));
            return;
        }

        string path = AssetDatabase.GetAssetPath(value);
        if (!IsPathUnderRoot(path, expectedRoot))
        {
            issues.Add(new AnimalValidationIssue(
                AnimalValidationSeverity.Warning,
                $"{label} 경로가 {expectedRoot} 밖에 있습니다: {path}",
                value));
        }
    }

    private static void ValidateAnimalReferences(
        Animal animal,
        List<AnimalValidationIssue> issues)
    {
        SerializedObject serializedAnimal = new SerializedObject(animal);
        serializedAnimal.Update();
        for (int i = 0; i < RequiredAnimalReferencePropertyNames.Length; i++)
        {
            SerializedProperty property = serializedAnimal.FindProperty(RequiredAnimalReferencePropertyNames[i]);
            if (property == null || property.objectReferenceValue != null)
            {
                continue;
            }

            issues.Add(new AnimalValidationIssue(
                AnimalValidationSeverity.Error,
                $"{RequiredAnimalReferenceLabels[i]} 참조가 비어 있습니다.",
                animal));
        }

        SerializedProperty rendererProperty = serializedAnimal.FindProperty("dinoRenderer");
        SkinnedMeshRenderer renderer = rendererProperty?.objectReferenceValue as SkinnedMeshRenderer;
        if (renderer != null
            && (renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount <= 0))
        {
            issues.Add(new AnimalValidationIssue(
                AnimalValidationSeverity.Error,
                "Growth Renderer에 성장용 BlendShape가 없습니다.",
                renderer));
        }
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string normalizedPath = NormalizePath(path);
        string normalizedRoot = NormalizePath(root).TrimEnd('/');
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
               || normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }
}
