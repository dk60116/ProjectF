using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TerrainDataEditorWindow : EditorWindow
{
    private const float TexturePreviewSize = 42f;
    private static readonly int TextureReferenceControlHash = "TerrainTextureReferenceField".GetHashCode();
    private static int activeTexturePickerControlId;
    private static Object activeTexturePickerTarget;
    private static string activeTexturePickerPropertyPath;

    private static readonly string[] CorePropertyPaths =
    {
        "mapSize",
        "blocks",
        "chunkSize",
        "loadRadius",
        "unloadRadius",
        "expandEditorPreviewRange",
        "chunkGenerationBlocksPerFrame",
        "chunkSurfaceRowsPerFrame",
        "chunkInstallationRestoresPerFrame",
        "chunkRestoreBackgroundSimulationIterations",
        "chunkUnloadsPerFrame",
        "virtualizeDistantFloorObjects",
        "floorObjectLiveRadius",
        "floorObjectVirtualizationInterval",
        "floorObjectVirtualizationConversionsPerTick",
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
            "Water",
            null,
            "waterBiomeColor"),
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
            "forestBiomeColor"),
        new TerrainTextureSet(
            "Rock",
            null,
            "rockBiomeColor")
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
        "rockWeight"
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
        "reedWaterSearchRadius",
        "reedDensityMultiplier",
        "oreMinimumBodyScaleRatio",
        "oreMaximumBodyScaleRatio",
        "oreScaleAtResourceCount"
    };

    private static readonly string[] AnimalGenerationPropertyPaths =
    {
        "generateAnimals",
        "animalDensity",
        "animalHerdSpreadRadius",
        "showAnimalSpawnGizmos"
    };

    private static readonly string[] StartAreaPropertyPaths =
    {
        "startLakeRadiusRange",
        "startSafeZoneRadius",
        "keepStartSafeZoneClearOfResources",
        "starterWaterExclusionRadius"
    };

    private Vector2 detailScroll;
    private TerrainGenerator selectedGenerator;
    private readonly List<TerrainGenerator> visibleGenerators = new List<TerrainGenerator>();
    private readonly Dictionary<string, SerializedProperty> serializedPropertyCache =
        new Dictionary<string, SerializedProperty>(System.StringComparer.Ordinal);
    private readonly HashSet<string> appliedFoldoutStateKeys =
        new HashSet<string>(System.StringComparer.Ordinal);
    private bool visibleGeneratorsDirty = true;
    private SerializedObject selectedGeneratorSerializedObject;

    [MenuItem("Window/ProjectF/Terrain Editor")]
    public static void ShowWindow()
    {
        TerrainDataEditorWindow window = GetWindow<TerrainDataEditorWindow>("Terrain Editor");
        window.minSize = new Vector2(620f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        InvalidateVisibleGenerators();
        EnsureSelection();
        EditorApplication.delayCall -= RefreshGeneratorSelection;
        EditorApplication.delayCall += RefreshGeneratorSelection;
    }

    private void OnDisable()
    {
        EditorApplication.delayCall -= RefreshGeneratorSelection;
        ClearSelectedGeneratorCache();
        visibleGenerators.Clear();
        visibleGeneratorsDirty = true;
    }

    private void OnFocus()
    {
        RefreshGeneratorSelection();
    }

    private void RefreshGeneratorSelection()
    {
        if (this == null)
        {
            return;
        }

        InvalidateVisibleGenerators();
        EnsureSelection();
        Repaint();
    }

    private void OnHierarchyChange()
    {
        InvalidateVisibleGenerators();
        EnsureSelection();
        Repaint();
    }

    private void OnSelectionChange()
    {
        EnsureSelection();
        Repaint();
    }

    private void OnGUI()
    {
        DrawBackground();
        EnsureSelection();
        DrawDetailPanel();
    }

    private void DrawBackground()
    {
        EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), new Color(0.15f, 0.15f, 0.15f));
    }

    private void DrawDetailPanel()
    {
        Rect detailRect = new Rect(0f, 0f, position.width, position.height);
        GUILayout.BeginArea(detailRect);
        GUILayout.Space(10f);

        TerrainGenerator generator = ResolveGeneratorForDrawing();
        if (generator == null)
        {
            EditorGUILayout.HelpBox("선택된 TerrainGenerator가 없습니다.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        SerializedObject serializedGenerator = GetSelectedGeneratorSerializedObject(generator);
        if (serializedGenerator == null)
        {
            GUILayout.EndArea();
            return;
        }

        serializedGenerator.UpdateIfRequiredOrScript();

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
        DrawPropertySection(serializedGenerator, "Reed Resources", "reedResources");
        DrawPropertySection(serializedGenerator, "Oil Resources", "oilResources");
        DrawPropertySection(serializedGenerator, "Resource Generation", ResourceGenerationPropertyPaths);
        DrawPropertySection(serializedGenerator, "Animal Generation", AnimalGenerationPropertyPaths);
        EditorGUILayout.LabelField(
            "Effective Animal Density",
            generator.EffectiveAnimalDensity.ToString("0.########"));
        if (GUILayout.Button("Apply Animal Settings to Loaded Chunks", GUILayout.Height(24f)))
        {
            serializedGenerator.ApplyModifiedProperties();
            RunGeneratorAction(
                generator,
                "Rebuild Loaded Terrain Animals",
                terrain => terrain.RebuildLoadedAnimals());
        }

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
            SerializedProperty property = GetCachedProperty(serializedObject, propertyPaths[i]);
            if (property == null)
            {
                continue;
            }

            ApplyPersistedFoldoutState(serializedObject, property);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(property, true);
            if (EditorGUI.EndChangeCheck())
            {
                PersistFoldoutState(serializedObject, property);
            }
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
            SerializedProperty property = GetCachedProperty(serializedObject, propertyPaths[i]);
            if (property == null)
            {
                continue;
            }

            ApplyPersistedFoldoutState(serializedObject, property);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(property, true);
            if (EditorGUI.EndChangeCheck())
            {
                PersistFoldoutState(serializedObject, property);
            }
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
        SerializedProperty baseTexture = !string.IsNullOrEmpty(textureSet.baseTexturePropertyPath)
            ? GetCachedProperty(serializedObject, textureSet.baseTexturePropertyPath)
            : null;
        SerializedProperty baseColor = !string.IsNullOrEmpty(textureSet.baseColorPropertyPath)
            ? GetCachedProperty(serializedObject, textureSet.baseColorPropertyPath)
            : null;
        if (baseTexture != null)
        {
            DrawTextureProperty(baseTexture, "Base Texture", false, Color.white);
        }

        if (baseColor != null)
        {
            EditorGUILayout.PropertyField(baseColor, new GUIContent("Minimap Color"));
        }

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
        activeTexturePickerTarget = property != null
            && property.serializedObject != null
                ? property.serializedObject.targetObject
                : null;
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
            activeTexturePickerTarget = null;
            activeTexturePickerPropertyPath = null;
        }

        currentEvent.Use();
    }

    private static bool IsActiveTexturePickerProperty(SerializedProperty property)
    {
        return property != null
            && property.serializedObject != null
            && activeTexturePickerTarget == property.serializedObject.targetObject
            && activeTexturePickerPropertyPath == property.propertyPath;
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

    private void ApplyPersistedFoldoutState(SerializedObject serializedObject, SerializedProperty property)
    {
        if (serializedObject == null || property == null || !property.hasVisibleChildren)
        {
            return;
        }

        string rootKey = GetFoldoutStateKey(serializedObject, property.propertyPath);
        if (!appliedFoldoutStateKeys.Add(rootKey))
        {
            return;
        }

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
        int targetHash = serializedObject != null && serializedObject.targetObject != null
            ? serializedObject.targetObject.GetHashCode()
            : 0;
        return $"TerrainDataEditorWindow.Foldout.{targetHash}.{propertyPath}";
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
        return selectedGenerator;
    }

    private TerrainGenerator ResolveGeneratorForDrawing()
    {
        TerrainGenerator generator = GetSelectedGenerator();
        if (IsLoadedSceneGenerator(generator))
        {
            return generator;
        }

        generator = GetTerrainGeneratorFromUnitySelection();
        if (IsLoadedSceneGenerator(generator))
        {
            SetSelectedGenerator(generator);
            return generator;
        }

        List<TerrainGenerator> generators = GetVisibleGenerators();
        if (generators.Count <= 0)
        {
            return null;
        }

        generator = generators[0];
        SetSelectedGenerator(generator);
        return generator;
    }

    private void EnsureSelection()
    {
        EnsureSelection(GetVisibleGenerators());
    }

    private void EnsureSelection(List<TerrainGenerator> generators)
    {
        if (generators == null || generators.Count == 0)
        {
            SetSelectedGenerator(null);
            return;
        }

        TerrainGenerator activeSelection = GetTerrainGeneratorFromUnitySelection();
        if (IsLoadedSceneGenerator(activeSelection))
        {
            SetSelectedGenerator(activeSelection);
            return;
        }

        TerrainGenerator selectedGenerator = GetSelectedGenerator();
        if (IsLoadedSceneGenerator(selectedGenerator))
        {
            return;
        }

        SetSelectedGenerator(generators[0]);
    }

    private List<TerrainGenerator> GetVisibleGenerators()
    {
        if (!visibleGeneratorsDirty)
        {
            for (int i = 0; i < visibleGenerators.Count; i++)
            {
                if (visibleGenerators[i] == null)
                {
                    visibleGeneratorsDirty = true;
                    break;
                }
            }
        }

        if (!visibleGeneratorsDirty)
        {
            return visibleGenerators;
        }

        visibleGenerators.Clear();
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                continue;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                TerrainGenerator[] generators = roots[rootIndex].GetComponentsInChildren<TerrainGenerator>(true);
                for (int generatorIndex = 0; generatorIndex < generators.Length; generatorIndex++)
                {
                    TerrainGenerator generator = generators[generatorIndex];
                    if (generator != null)
                    {
                        visibleGenerators.Add(generator);
                    }
                }
            }
        }

        visibleGenerators.Sort(CompareTerrainGenerators);
        // A domain reload can enable this window before scene objects are restored.
        // Keep an empty result dirty so the next GUI pass can recover automatically.
        visibleGeneratorsDirty = visibleGenerators.Count == 0;

        return visibleGenerators;
    }

    private static bool IsLoadedSceneGenerator(TerrainGenerator generator)
    {
        return generator != null
               && !EditorUtility.IsPersistent(generator)
               && generator.gameObject.scene.IsValid()
               && generator.gameObject.scene.isLoaded;
    }

    private void InvalidateVisibleGenerators()
    {
        visibleGeneratorsDirty = true;
    }

    private void SetSelectedGenerator(TerrainGenerator generator)
    {
        if (selectedGenerator == generator)
        {
            return;
        }

        selectedGenerator = generator;
        ClearSelectedGeneratorCache();
    }

    private SerializedObject GetSelectedGeneratorSerializedObject(TerrainGenerator generator)
    {
        if (generator == null)
        {
            ClearSelectedGeneratorCache();
            return null;
        }

        if (selectedGeneratorSerializedObject != null
            && selectedGeneratorSerializedObject.targetObject == generator)
        {
            return selectedGeneratorSerializedObject;
        }

        ClearSelectedGeneratorCache();
        selectedGeneratorSerializedObject = new SerializedObject(generator);
        return selectedGeneratorSerializedObject;
    }

    private SerializedProperty GetCachedProperty(SerializedObject serializedObject, string propertyPath)
    {
        if (serializedObject == null || string.IsNullOrEmpty(propertyPath))
        {
            return null;
        }

        if (serializedObject == selectedGeneratorSerializedObject
            && serializedPropertyCache.TryGetValue(propertyPath, out SerializedProperty cachedProperty)
            && cachedProperty != null)
        {
            return cachedProperty;
        }

        SerializedProperty property = serializedObject.FindProperty(propertyPath);
        if (serializedObject == selectedGeneratorSerializedObject)
        {
            serializedPropertyCache[propertyPath] = property;
        }

        return property;
    }

    private void ClearSelectedGeneratorCache()
    {
        selectedGeneratorSerializedObject = null;
        serializedPropertyCache.Clear();
        appliedFoldoutStateKeys.Clear();
    }

    private static int CompareTerrainGenerators(TerrainGenerator left, TerrainGenerator right)
    {
        string leftScene = left != null && left.gameObject.scene.IsValid()
            ? left.gameObject.scene.name
            : string.Empty;
        string rightScene = right != null && right.gameObject.scene.IsValid()
            ? right.gameObject.scene.name
            : string.Empty;
        int sceneCompare = string.Compare(
            leftScene,
            rightScene,
            System.StringComparison.OrdinalIgnoreCase);
        if (sceneCompare != 0)
        {
            return sceneCompare;
        }

        string leftName = left != null ? left.name : string.Empty;
        string rightName = right != null ? right.name : string.Empty;
        return string.Compare(leftName, rightName, System.StringComparison.OrdinalIgnoreCase);
    }

    private static TerrainGenerator GetTerrainGeneratorFromUnitySelection()
    {
        if (Selection.activeObject is TerrainGenerator selectedGenerator)
        {
            return selectedGenerator;
        }

        GameObject selectedGameObject = Selection.activeGameObject;
        return selectedGameObject != null
            ? selectedGameObject.GetComponentInParent<TerrainGenerator>(true)
            : null;
    }
}
