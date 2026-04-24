using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class TerrainDataEditorWindow : EditorWindow
{
    private const float SidebarWidth = 280f;

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

    private static readonly string[] SurfacePropertyPaths =
    {
        "terrainSurfaceSubdivisions",
        "terrainBlendJitter",
        "terrainSurfaceVertexJitter",
        "enableGeneratedSurfaceTextureBlend",
        "generatedSurfaceBlendTextureTiling",
        "generatedSurfaceBlendNoiseScale",
        "generatedSurfaceBlendNoiseStrength",
        "generatedSurfaceBlendShader",
        "generatedSurfaceBlendWaterTexture",
        "generatedSurfaceBlendSandTexture",
        "generatedSurfaceBlendDirtTexture",
        "generatedSurfaceBlendGrassTexture",
        "generatedSurfaceBlendForestTexture",
        "generatedSurfaceBlendNoiseTexture",
        "generatedSurfaceYOffset"
    };

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
        DrawPropertySection(serializedGenerator, "Surface Blend", SurfacePropertyPaths);
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
