using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class TerrainDataEditorWindow : EditorWindow
{
    private const float SidebarWidth = 280f;
    private const float TexturePreviewSize = 42f;
    private static readonly int TextureReferenceControlHash = "TerrainTextureReferenceField".GetHashCode();
    private static int activeTexturePickerControlId;
    private static int activeTexturePickerTargetId;
    private static string activeTexturePickerPropertyPath;

    private static readonly string[] CorePropertyPaths =
    {
        "blocks",
        "chunkSize",
        "loadRadius",
        "unloadRadius",
        "expandEditorPreviewRange",
        "chunkGenerationBlocksPerFrame",
        "chunkSurfaceRowsPerFrame",
        "trackingTarget",
        "generateOnStart",
        "seed"
    };

    private static readonly string[] WaterPropertyPaths =
    {
        "waterFillPercent",
        "waterNoiseScale",
        "largeLakeCellSize",
        "largeLakeChance",
        "largeLakeRadiusRange",
        "largeLakeBlobNoiseScale",
        "smallLakeCellSize",
        "smallLakeChance",
        "smallLakeRadiusRange",
        "smallLakeBlobNoiseScale",
        "riverCellSize",
        "riverChance",
        "riverWidth",
        "riverCurveStrength",
        "riverEndpointLakeRadiusRange",
        "sandMinWidth",
        "sandMaxWidth"
    };

    private static readonly string[] SurfaceGeneralPropertyPaths =
    {
        "terrainSurfaceSubdivisions",
        "terrainBlendJitter",
        "terrainSurfaceVertexJitter",
        "enableGeneratedSurfaceTextureBlend",
        "generatedSurfaceBlendTextureTiling",
        "generatedSurfaceBlendNoiseScale",
        "generatedSurfaceBlendNoiseStrength",
        "generatedSurfaceBlendShader",
        "generatedSurfaceWaterMaterial",
        "generatedSurfaceYOffset"
    };

    private static readonly TerrainTextureSet[] SurfaceTextureSets =
    {
        new TerrainTextureSet(
            "Sand",
            "generatedSurfaceBlendSandTexture",
            "sandBiomeColor"),
        new TerrainTextureSet(
            "Dirt",
            "generatedSurfaceBlendDirtTexture",
            "dirtBiomeColor"),
        new TerrainTextureSet(
            "Grass",
            "generatedSurfaceBlendGrassTexture",
            "grassBiomeColor"),
        new TerrainTextureSet(
            "Forest",
            "generatedSurfaceBlendForestTexture",
            "forestBiomeColor")
    };

    private readonly struct TerrainTextureSet
    {
        public readonly string title;
        public readonly string baseTexturePropertyPath;
        public readonly string baseColorPropertyPath;

        public TerrainTextureSet(
            string title,
            string baseTexturePropertyPath,
            string baseColorPropertyPath)
        {
            this.title = title;
            this.baseTexturePropertyPath = baseTexturePropertyPath;
            this.baseColorPropertyPath = baseColorPropertyPath;
        }
    }

    private static readonly string[] LandBiomePropertyPaths =
    {
        "landBiomePrimaryScale",
        "landBiomeDetailScale",
        "dirtWeight",
        "grassWeight",
        "forestWeight",
        "rockWeight",
        "waterBiomeColor",
        "sandBiomeColor",
        "dirtBiomeColor",
        "grassBiomeColor",
        "forestBiomeColor",
        "rockBiomeColor"
    };

    private static readonly string[] ResourceGenerationPropertyPaths =
    {
        "resourcePatchScale",
        "resourceDetailScale",
        "resourceDensityMultiplier",
        "resourcePatchSpacing",
        "resourceClusterSparsity",
        "resourceClusterBreakupScale",
        "resourceClusterLobeSpread",
        "minimumResourcePatchSize",
        "maximumResourcePatchSize",
        "resourcePatchCellSize",
        "generateStarterResourcePatches",
        "starterPatchHalfSize",
        "starterPatchDistanceFromCenter",
        "generateStarterTrees",
        "starterTreeMinCount",
        "starterTreeMaxCount",
        "starterTreeDistanceFromCenter",
        "oreMinimumBodyScaleRatio",
        "oreMaximumBodyScaleRatio",
        "oreScaleAtResourceCount"
    };

    private static readonly string[] StartAreaPropertyPaths =
    {
        "startLakeRadiusRange",
        "startSafeZoneRadius",
        "keepStartSafeZoneClearOfResources",
        "starterWaterExclusionRadius"
    };

    private Vector2 generatorListScroll;
    private Vector2 detailScroll;
    private string searchText = string.Empty;
    private int selectedGeneratorInstanceId;

    [MenuItem("Window/ProjectF/Terrain Editor")]
    public static void ShowWindow()
    {
        TerrainDataEditorWindow window = GetWindow<TerrainDataEditorWindow>("Terrain Editor");
        window.minSize = new Vector2(860f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        EnsureSelection();
    }

    private void OnFocus()
    {
        Repaint();
    }

    private void OnHierarchyChange()
    {
        EnsureSelection();
        Repaint();
    }

    private void OnGUI()
    {
        DrawBackground();
        DrawGeneratorList();
        DrawDetailPanel();
    }

    private void DrawBackground()
    {
        EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), new Color(0.15f, 0.15f, 0.15f));
    }

    private void DrawGeneratorList()
    {
        Rect sidebarRect = new Rect(0f, 0f, SidebarWidth, position.height);
        EditorGUI.DrawRect(sidebarRect, new Color(0.12f, 0.12f, 0.12f));

        GUILayout.BeginArea(sidebarRect);
        GUILayout.Space(10f);
        DrawSidebarToolbar();
        DrawSearchField();
        EditorGUILayout.LabelField("Terrain Generators", EditorStyles.boldLabel);

        List<TerrainGenerator> generators = GetVisibleGenerators();
        EnsureSelection(generators);

        if (generators.Count == 0)
        {
            EditorGUILayout.HelpBox("씬에서 TerrainGenerator를 찾을 수 없습니다.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        generatorListScroll = EditorGUILayout.BeginScrollView(generatorListScroll);
        for (int i = 0; i < generators.Count; i++)
        {
            TerrainGenerator generator = generators[i];
            if (generator == null)
            {
                continue;
            }

            int instanceId = generator.GetInstanceID();
            bool isSelected = selectedGeneratorInstanceId == instanceId;
            string sceneName = generator.gameObject.scene.IsValid() ? generator.gameObject.scene.name : "No Scene";
            GUIContent content = new GUIContent($"{generator.name}  ({sceneName})");
            Rect rowRect = GUILayoutUtility.GetRect(1f, 28f, GUILayout.ExpandWidth(true));
            if (GUI.Toggle(rowRect, isSelected, content, "Button"))
            {
                selectedGeneratorInstanceId = instanceId;
            }
        }

        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawSidebarToolbar()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh", GUILayout.Height(24f)))
        {
            EnsureSelection();
            Repaint();
        }

        TerrainGenerator selectedGenerator = GetSelectedGenerator();
        EditorGUI.BeginDisabledGroup(selectedGenerator == null);
        if (GUILayout.Button("Ping", GUILayout.Height(24f)))
        {
            EditorGUIUtility.PingObject(selectedGenerator);
            Selection.activeObject = selectedGenerator;
        }

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(6f);
    }

    private void DrawSearchField()
    {
        GUI.SetNextControlName("TerrainEditorSearchField");
        searchText = EditorGUILayout.TextField("Search", searchText);
        GUILayout.Space(8f);
    }

    private void DrawDetailPanel()
    {
        Rect detailRect = new Rect(SidebarWidth, 0f, position.width - SidebarWidth, position.height);
        GUILayout.BeginArea(detailRect);
        GUILayout.Space(10f);

        TerrainGenerator generator = GetSelectedGenerator();
        if (generator == null)
        {
            EditorGUILayout.HelpBox("선택된 TerrainGenerator가 없습니다.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        SerializedObject serializedGenerator = new SerializedObject(generator);
        serializedGenerator.Update();

        DrawDetailHeader(generator);

        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        EditorGUI.BeginChangeCheck();

        DrawPropertySection(serializedGenerator, "Core", CorePropertyPaths);
        DrawPropertySection(serializedGenerator, "Water", WaterPropertyPaths);
        DrawSurfaceBlendSection(serializedGenerator);
        DrawPropertySection(serializedGenerator, "Land Biomes", LandBiomePropertyPaths);
        DrawPropertySection(serializedGenerator, "Start Area", StartAreaPropertyPaths);
        DrawPropertySection(serializedGenerator, "Ore Resources", "oreResources");
        DrawPropertySection(serializedGenerator, "Tree Resources", "treeResources");
        DrawPropertySection(serializedGenerator, "Resource Generation", ResourceGenerationPropertyPaths);

        if (EditorGUI.EndChangeCheck())
        {
            serializedGenerator.ApplyModifiedProperties();
            EditorUtility.SetDirty(generator);
            if (generator.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
            }
        }
        else
        {
            serializedGenerator.ApplyModifiedProperties();
        }

        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawDetailHeader(TerrainGenerator generator)
    {
        EditorGUILayout.LabelField(generator.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            generator.gameObject.scene.IsValid() ? generator.gameObject.scene.path : "No Scene",
            EditorStyles.miniLabel);

        GUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate", GUILayout.Height(28f)))
        {
            RunGeneratorAction(generator, "Generate Terrain", terrain => terrain.Generate());
        }

        if (GUILayout.Button("Reset", GUILayout.Height(28f)))
        {
            RunGeneratorAction(generator, "Reset Terrain Chunks", terrain => terrain.ResetChunks());
        }

        if (GUILayout.Button("Random Seed", GUILayout.Height(28f)))
        {
            RunGeneratorAction(generator, "Randomize Terrain Seed", terrain => terrain.RandomizeSeed());
        }

        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10f);
    }

    private void RunGeneratorAction(TerrainGenerator generator, string undoLabel, System.Action<TerrainGenerator> action)
    {
        if (generator == null || action == null)
        {
            return;
        }

        // Commit any focused IMGUI text field before running generation actions.
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;

        SerializedObject serializedGenerator = new SerializedObject(generator);
        serializedGenerator.Update();
        serializedGenerator.ApplyModifiedProperties();

        Undo.RegisterCompleteObjectUndo(generator, undoLabel);
        Selection.activeObject = generator.gameObject;
        action(generator);
        EditorUtility.SetDirty(generator);
        if (generator.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }

    private void DrawPropertySection(SerializedObject serializedObject, string title, params string[] propertyPaths)
    {
        if (serializedObject == null || propertyPaths == null || propertyPaths.Length == 0)
        {
            return;
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        for (int i = 0; i < propertyPaths.Length; i++)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyPaths[i]);
            if (property == null)
            {
                continue;
            }

            ApplyPersistedFoldoutState(serializedObject, property);
            EditorGUILayout.PropertyField(property, true);
            PersistFoldoutState(serializedObject, property);
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(6f);
    }

    private void DrawSurfaceBlendSection(SerializedObject serializedObject)
    {
        if (serializedObject == null)
        {
            return;
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Surface Blend", EditorStyles.boldLabel);
        DrawProperties(serializedObject, SurfaceGeneralPropertyPaths);

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("Texture Sets", EditorStyles.miniBoldLabel);
        for (int i = 0; i < SurfaceTextureSets.Length; i++)
        {
            DrawTerrainTextureSet(serializedObject, SurfaceTextureSets[i]);
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(6f);
    }

    private void DrawProperties(SerializedObject serializedObject, params string[] propertyPaths)
    {
        if (serializedObject == null || propertyPaths == null)
        {
            return;
        }

        for (int i = 0; i < propertyPaths.Length; i++)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyPaths[i]);
            if (property == null)
            {
                continue;
            }

            ApplyPersistedFoldoutState(serializedObject, property);
            EditorGUILayout.PropertyField(property, true);
            PersistFoldoutState(serializedObject, property);
        }
    }

    private void DrawTerrainTextureSet(SerializedObject serializedObject, TerrainTextureSet textureSet)
    {
        string foldoutKey = GetFoldoutStateKey(serializedObject, $"SurfaceTextureSet.{textureSet.title}");
        bool isExpanded = SessionState.GetBool(foldoutKey, false);
        Rect foldoutRect = GUILayoutUtility.GetRect(1f, EditorGUIUtility.singleLineHeight + 4f, GUILayout.ExpandWidth(true));
        foldoutRect.x += 8f;
        foldoutRect.width -= 8f;
        isExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, textureSet.title, true);
        SessionState.SetBool(foldoutKey, isExpanded);

        if (!isExpanded)
        {
            return;
        }

        EditorGUI.indentLevel++;
        SerializedProperty baseTexture = serializedObject.FindProperty(textureSet.baseTexturePropertyPath);
        SerializedProperty baseColor = serializedObject.FindProperty(textureSet.baseColorPropertyPath);
        Color basePreviewTint = baseColor != null ? baseColor.colorValue : Color.white;
        basePreviewTint.a = 1f;
        DrawTextureProperty(baseTexture, "Base Texture", false, basePreviewTint);

        EditorGUI.indentLevel--;
        GUILayout.Space(2f);
    }

    private static void DrawTextureProperty(SerializedProperty property, string label)
    {
        DrawTextureProperty(property, label, false, Color.white);
    }

    private static void DrawTextureProperty(SerializedProperty property, string label, bool useAssetPreview)
    {
        DrawTextureProperty(property, label, useAssetPreview, Color.white);
    }

    private static void DrawTextureProperty(SerializedProperty property, string label, bool useAssetPreview, Color previewTint)
    {
        if (property == null)
        {
            return;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUI.indentLevel * 14f);
        Rect previewRect = GUILayoutUtility.GetRect(
            TexturePreviewSize,
            TexturePreviewSize,
            GUILayout.Width(TexturePreviewSize),
            GUILayout.Height(TexturePreviewSize));
        DrawTexturePreview(previewRect, property.objectReferenceValue as Texture2D, useAssetPreview, previewTint);

        EditorGUILayout.BeginVertical();
        GUILayout.Space((TexturePreviewSize - EditorGUIUtility.singleLineHeight) * 0.5f);
        Rect fieldRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        DrawTextureReferenceField(fieldRect, property, label, useAssetPreview, previewTint);
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawTexturePreview(Rect rect, Texture2D texture, bool useAssetPreview, Color previewTint)
    {
        EditorGUI.DrawRect(rect, new Color(0.09f, 0.09f, 0.09f));
        GUI.Box(rect, GUIContent.none);

        if (texture == null)
        {
            return;
        }

        Texture preview = texture;
        if (useAssetPreview)
        {
            preview = AssetPreview.GetAssetPreview(texture);
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(texture);
            }
        }

        Color previousColor = GUI.color;
        GUI.color = previousColor * previewTint;
        GUI.DrawTexture(rect, preview != null ? preview : texture, ScaleMode.ScaleToFit);
        GUI.color = previousColor;
    }

    private static void DrawTextureReferenceField(
        Rect rect,
        SerializedProperty property,
        string label,
        bool useAssetPreview,
        Color previewTint)
    {
        int controlId = GUIUtility.GetControlID(TextureReferenceControlHash, FocusType.Keyboard, rect);
        HandleTextureObjectPicker(property);

        int previousIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        Rect valueRect = EditorGUI.PrefixLabel(rect, controlId, new GUIContent(label));
        EditorGUI.indentLevel = previousIndent;

        GUIStyle objectFieldStyle = GUI.skin.FindStyle("ObjectField") ?? EditorStyles.textField;
        GUIStyle objectFieldButtonStyle = GUI.skin.FindStyle("ObjectFieldButton") ?? EditorStyles.miniButton;

        Texture2D texture = property.objectReferenceValue as Texture2D;
        GUI.Box(valueRect, GUIContent.none, objectFieldStyle);
        Rect buttonRect = new Rect(valueRect.xMax - 19f, valueRect.y, 19f, valueRect.height);
        Rect iconRect = new Rect(valueRect.x + 3f, valueRect.y + 2f, valueRect.height - 4f, valueRect.height - 4f);
        Rect nameRect = new Rect(
            iconRect.xMax + 4f,
            valueRect.y,
            Mathf.Max(0f, buttonRect.x - iconRect.xMax - 7f),
            valueRect.height);

        DrawTexturePreview(iconRect, texture, useAssetPreview, previewTint);

        string assetPath = texture != null ? AssetDatabase.GetAssetPath(texture) : string.Empty;
        string displayName = texture != null ? texture.name : "None (Texture2D)";
        GUI.Label(nameRect, new GUIContent(displayName, assetPath), EditorStyles.label);

        Event currentEvent = Event.current;
        HandleTextureReferenceDragAndDrop(valueRect, property, currentEvent);
        if (GUI.Button(buttonRect, GUIContent.none, objectFieldButtonStyle))
        {
            BeginTextureObjectPicker(property, controlId, texture);
        }

        Rect clickableValueRect = new Rect(
            valueRect.x,
            valueRect.y,
            Mathf.Max(0f, buttonRect.x - valueRect.x),
            valueRect.height);
        if (texture != null
            && currentEvent.type == EventType.MouseDown
            && currentEvent.clickCount > 1
            && clickableValueRect.Contains(currentEvent.mousePosition))
        {
            EditorGUIUtility.PingObject(texture);
            Selection.activeObject = texture;
            currentEvent.Use();
        }
    }

    private static void BeginTextureObjectPicker(SerializedProperty property, int controlId, Texture2D currentTexture)
    {
        activeTexturePickerControlId = controlId;
        activeTexturePickerTargetId = GetSerializedTargetId(property);
        activeTexturePickerPropertyPath = property.propertyPath;
        EditorGUIUtility.ShowObjectPicker<Texture2D>(currentTexture, false, string.Empty, controlId);
    }

    private static void HandleTextureObjectPicker(SerializedProperty property)
    {
        Event currentEvent = Event.current;
        if (activeTexturePickerControlId == 0
            || currentEvent.type != EventType.ExecuteCommand
            || EditorGUIUtility.GetObjectPickerControlID() != activeTexturePickerControlId
            || !IsActiveTexturePickerProperty(property))
        {
            return;
        }

        if (currentEvent.commandName != "ObjectSelectorUpdated"
            && currentEvent.commandName != "ObjectSelectorClosed")
        {
            return;
        }

        Object selectedObject = EditorGUIUtility.GetObjectPickerObject();
        if (selectedObject == null || selectedObject is Texture2D)
        {
            SetTextureProperty(property, selectedObject as Texture2D);
        }

        if (currentEvent.commandName == "ObjectSelectorClosed")
        {
            activeTexturePickerControlId = 0;
            activeTexturePickerTargetId = 0;
            activeTexturePickerPropertyPath = null;
        }

        currentEvent.Use();
    }

    private static bool IsActiveTexturePickerProperty(SerializedProperty property)
    {
        return property != null
            && activeTexturePickerTargetId == GetSerializedTargetId(property)
            && activeTexturePickerPropertyPath == property.propertyPath;
    }

    private static int GetSerializedTargetId(SerializedProperty property)
    {
        return property != null
            && property.serializedObject != null
            && property.serializedObject.targetObject != null
            ? property.serializedObject.targetObject.GetInstanceID()
            : 0;
    }

    private static void SetTextureProperty(SerializedProperty property, Texture2D texture)
    {
        SerializedProperty defaultsInitialized = property.serializedObject.FindProperty("generatedSurfaceBlendTextureDefaultsInitialized");
        if (defaultsInitialized != null)
        {
            defaultsInitialized.boolValue = true;
        }

        property.objectReferenceValue = texture;
        property.serializedObject.ApplyModifiedProperties();
        Object targetObject = property.serializedObject.targetObject;
        if (targetObject != null)
        {
            EditorUtility.SetDirty(targetObject);
            if (targetObject is Component component && component.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            }
        }

        GUI.changed = true;
    }

    private static void HandleTextureReferenceDragAndDrop(Rect rect, SerializedProperty property, Event currentEvent)
    {
        if (!rect.Contains(currentEvent.mousePosition)
            || (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform))
        {
            return;
        }

        Texture2D draggedTexture = null;
        Object[] draggedObjects = DragAndDrop.objectReferences;
        for (int i = 0; i < draggedObjects.Length; i++)
        {
            if (draggedObjects[i] is Texture2D texture)
            {
                draggedTexture = texture;
                break;
            }
        }

        if (draggedTexture == null)
        {
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (currentEvent.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            SetTextureProperty(property, draggedTexture);
        }

        currentEvent.Use();
    }

    private static void ApplyPersistedFoldoutState(SerializedObject serializedObject, SerializedProperty property)
    {
        if (serializedObject == null || property == null || !property.hasVisibleChildren)
        {
            return;
        }

        string rootKey = GetFoldoutStateKey(serializedObject, property.propertyPath);
        property.isExpanded = SessionState.GetBool(rootKey, property.isExpanded);
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

            string key = GetFoldoutStateKey(serializedObject, child.propertyPath);
            child.isExpanded = SessionState.GetBool(key, child.isExpanded);
        });
    }

    private static void PersistFoldoutState(SerializedObject serializedObject, SerializedProperty property)
    {
        if (serializedObject == null || property == null || !property.hasVisibleChildren)
        {
            return;
        }

        string rootKey = GetFoldoutStateKey(serializedObject, property.propertyPath);
        SessionState.SetBool(rootKey, property.isExpanded);

        VisitChildProperties(property, child =>
        {
            if (!child.hasVisibleChildren)
            {
                return;
            }

            string key = GetFoldoutStateKey(serializedObject, child.propertyPath);
            SessionState.SetBool(key, child.isExpanded);
        });
    }

    private static string GetFoldoutStateKey(SerializedObject serializedObject, string propertyPath)
    {
        int instanceId = serializedObject != null && serializedObject.targetObject != null
            ? serializedObject.targetObject.GetInstanceID()
            : 0;
        return $"TerrainDataEditorWindow.Foldout.{instanceId}.{propertyPath}";
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

    private TerrainGenerator GetSelectedGenerator()
    {
        if (selectedGeneratorInstanceId == 0)
        {
            return null;
        }

        return EditorUtility.InstanceIDToObject(selectedGeneratorInstanceId) as TerrainGenerator;
    }

    private void EnsureSelection()
    {
        EnsureSelection(GetVisibleGenerators());
    }

    private void EnsureSelection(List<TerrainGenerator> generators)
    {
        if (generators == null || generators.Count == 0)
        {
            selectedGeneratorInstanceId = 0;
            return;
        }

        TerrainGenerator selectedGenerator = GetSelectedGenerator();
        if (selectedGenerator != null && generators.Contains(selectedGenerator))
        {
            return;
        }

        selectedGeneratorInstanceId = generators[0].GetInstanceID();
    }

    private List<TerrainGenerator> GetVisibleGenerators()
    {
        TerrainGenerator[] allGenerators = FindObjectsOfType<TerrainGenerator>(true);
        List<TerrainGenerator> visibleGenerators = new List<TerrainGenerator>();
        for (int i = 0; i < allGenerators.Length; i++)
        {
            TerrainGenerator generator = allGenerators[i];
            if (generator == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string lowerSearch = searchText.ToLowerInvariant();
                string generatorName = generator.name != null ? generator.name.ToLowerInvariant() : string.Empty;
                string sceneName = generator.gameObject.scene.IsValid()
                    ? generator.gameObject.scene.name.ToLowerInvariant()
                    : string.Empty;
                if (!generatorName.Contains(lowerSearch) && !sceneName.Contains(lowerSearch))
                {
                    continue;
                }
            }

            visibleGenerators.Add(generator);
        }

        visibleGenerators.Sort((left, right) =>
        {
            string leftScene = left != null && left.gameObject.scene.IsValid() ? left.gameObject.scene.name : string.Empty;
            string rightScene = right != null && right.gameObject.scene.IsValid() ? right.gameObject.scene.name : string.Empty;
            int sceneCompare = string.Compare(leftScene, rightScene, System.StringComparison.OrdinalIgnoreCase);
            if (sceneCompare != 0)
            {
                return sceneCompare;
            }

            string leftName = left != null ? left.name : string.Empty;
            string rightName = right != null ? right.name : string.Empty;
            return string.Compare(leftName, rightName, System.StringComparison.OrdinalIgnoreCase);
        });

        return visibleGenerators;
    }
}
