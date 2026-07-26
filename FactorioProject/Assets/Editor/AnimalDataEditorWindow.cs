using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class AnimalDataEditorWindow : EditorWindow
{
    private const float SidebarWidth = 280f;
    private const float PreviewHeight = 320f;
    private const float ListRowHeight = 28f;

    private static readonly int[] AnimationStates =
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15, 16, 98, 99
    };

    private static readonly string[] AnimationStateLabels =
    {
        "0 - Idle", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12",
        "14", "15", "16", "98", "99"
    };

    [Serializable]
    private sealed class AnimalJsonFile
    {
        public string format = "ProjectF.AnimalData";
        public int version = 3;
        public List<AnimalJsonEntry> animals = new List<AnimalJsonEntry>();
    }

    [Serializable]
    private sealed class AnimalJsonEntry
    {
        public int id = -1;
        public string animalName = string.Empty;
        public int spawnAge = AnimalDefinition.DefaultSpawnAge;
        public int minHerdSize = AnimalDefinition.DefaultMinHerdSize;
        public int maxHerdSize = AnimalDefinition.DefaultMaxHerdSize;
        public int spawnWeight = AnimalDefinition.DefaultSpawnWeight;
        public string hierarchyPath = string.Empty;
        public string definitionAssetPath = string.Empty;
        public string prefabAssetPath = string.Empty;
        public string adultIconAssetPath = string.Empty;
        public string childIconAssetPath = string.Empty;
    }

    private sealed class AnimalDraft
    {
        public readonly AnimalDefinition definition;
        public int id;
        public string animalName;
        public int spawnAge;
        public int minHerdSize;
        public int maxHerdSize;
        public int spawnWeight;
        public GameObject prefab;
        public Sprite adultIcon;
        public Sprite childIcon;
        public bool dirty;

        public AnimalDraft(AnimalDefinition definition)
        {
            this.definition = definition;
            Reload();
        }

        public void Reload()
        {
            id = definition != null ? definition.Id : -1;
            animalName = definition != null ? definition.AnimalName : string.Empty;
            spawnAge = definition != null ? definition.SpawnAgeWeight : AnimalDefinition.DefaultSpawnAge;
            minHerdSize = definition != null ? definition.MinHerdSize : AnimalDefinition.DefaultMinHerdSize;
            maxHerdSize = definition != null ? definition.MaxHerdSize : AnimalDefinition.DefaultMaxHerdSize;
            spawnWeight = definition != null ? definition.SpawnWeight : AnimalDefinition.DefaultSpawnWeight;
            prefab = definition != null ? definition.AnimalPrefab : null;
            adultIcon = definition != null ? definition.AdultIcon : null;
            childIcon = definition != null ? definition.ChildIcon : null;
            dirty = false;
        }
    }

    private sealed class HierarchyNode
    {
        public readonly string name;
        public readonly string path;
        public readonly List<HierarchyNode> children = new List<HierarchyNode>();
        public readonly List<AnimalDefinition> definitions = new List<AnimalDefinition>();

        public HierarchyNode(string name, string path)
        {
            this.name = name;
            this.path = path;
        }
    }

    private readonly List<AnimalDefinition> definitions = new List<AnimalDefinition>();
    private readonly Dictionary<int, AnimalDraft> drafts = new Dictionary<int, AnimalDraft>();
    private readonly HashSet<string> expandedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<AnimalValidationIssue> selectedIssues = new List<AnimalValidationIssue>();
    private readonly List<AnimalDefinition> selectedObjectDefinitions = new List<AnimalDefinition>();

    private HierarchyNode hierarchyRoot = new HierarchyNode("Animals", string.Empty);
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private int selectedDefinitionInstanceId;
    private string selectedObjectPath = string.Empty;
    private string searchText = string.Empty;

    private PreviewRenderUtility previewRenderer;
    private GameObject previewInstance;
    private GameObject previewPrefab;
    private Animal previewAnimal;
    private Animator previewAnimator;
    private Vector2 previewOrbit = new Vector2(145f, 12f);
    private float previewZoom = 1f;
    private float previewAge = 10f;
    private int previewAnimationIndex;
    private bool previewPlaying;
    private double lastPreviewUpdateTime;

    [MenuItem("Window/ProjectF/Animal Data")]
    public static void ShowWindow()
    {
        AnimalDataEditorWindow window = GetWindow<AnimalDataEditorWindow>("Animal Data");
        window.minSize = new Vector2(820f, 520f);
        window.Show();
    }

    public static void ShowWindowAndSelect(AnimalDefinition definition)
    {
        ShowWindow();
        AnimalDataEditorWindow window = GetWindow<AnimalDataEditorWindow>();
        window.ReloadDefinitions(false);
        if (definition != null)
        {
            window.selectedDefinitionInstanceId = definition.GetInstanceID();
            window.selectedObjectPath = string.Empty;
            window.ResetPreview();
        }

        window.Focus();
        window.Repaint();
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += HandleUndoRedo;
        EditorApplication.update += HandleEditorUpdate;
        ReloadDefinitions(false);
        lastPreviewUpdateTime = EditorApplication.timeSinceStartup;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
        EditorApplication.update -= HandleEditorUpdate;
        DisposePreview();
    }

    private void OnProjectChange()
    {
        ReloadDefinitions(true);
    }

    private void HandleUndoRedo()
    {
        ReloadDefinitions(true);
    }

    private void HandleEditorUpdate()
    {
        double currentTime = EditorApplication.timeSinceStartup;
        float deltaTime = (float)Math.Min(0.1d, Math.Max(0d, currentTime - lastPreviewUpdateTime));
        lastPreviewUpdateTime = currentTime;

        if (!previewPlaying || previewAnimator == null || !previewAnimator.isInitialized)
        {
            return;
        }

        previewAnimator.Update(deltaTime);
        Repaint();
    }

    private void OnGUI()
    {
        DrawBackground();
        DrawSidebar();
        DrawDetailPanel();
    }

    private void DrawBackground()
    {
        EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), new Color(0.15f, 0.15f, 0.15f));
    }

    private void DrawSidebar()
    {
        Rect sidebarRect = new Rect(0f, 0f, SidebarWidth, position.height);
        EditorGUI.DrawRect(sidebarRect, new Color(0.12f, 0.12f, 0.12f));

        GUILayout.BeginArea(sidebarRect);
        GUILayout.Space(8f);
        DrawToolbar();
        DrawSearchField();
        EditorGUILayout.LabelField($"Animals ({definitions.Count})", EditorStyles.boldLabel);

        if (definitions.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Assets/Animals에서 AnimalDefinition을 찾지 못했습니다. Rebuild를 눌러 아이콘 기준으로 생성하세요.",
                MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        DrawHierarchyNode(hierarchyRoot, 0);
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", GUILayout.Width(70f)))
        {
            SaveDrafts();
        }

        if (GUILayout.Button("Load", GUILayout.Width(70f)))
        {
            ReloadDefinitionsWithConfirmation();
        }

        if (GUILayout.Button("Rebuild", GUILayout.Width(80f)))
        {
            RebuildDefinitions();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Export JSON", GUILayout.Width(105f)))
        {
            ExportJsonWithDialog();
        }

        if (GUILayout.Button("Load JSON", GUILayout.Width(95f)))
        {
            LoadJsonWithDialog();
        }

        if (GUILayout.Button("Validate", GUILayout.Width(72f)))
        {
            AnimalDataEditorUtility.LogValidation(definitions);
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(4f);
    }

    private void DrawSearchField()
    {
        EditorGUILayout.BeginHorizontal();
        string nextSearch = EditorGUILayout.TextField("Search", searchText);
        if (!string.Equals(nextSearch, searchText, StringComparison.Ordinal))
        {
            searchText = nextSearch;
            listScroll = Vector2.zero;
        }

        EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(searchText));
        if (GUILayout.Button("X", GUILayout.Width(24f)))
        {
            searchText = string.Empty;
            GUI.FocusControl(null);
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private bool DrawHierarchyNode(HierarchyNode node, int depth)
    {
        bool hasVisibleContent = NodeMatchesSearch(node);
        if (!hasVisibleContent)
        {
            return false;
        }

        bool isRoot = node == hierarchyRoot;
        bool searching = !string.IsNullOrWhiteSpace(searchText);
        bool isGenderNode = IsGenderNode(node);
        bool isObjectNode = !isRoot && depth == 0 && !isGenderNode;
        bool expanded = isRoot || isGenderNode || searching || expandedFolders.Contains(node.path);
        if (!isRoot)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, 22f);
            rowRect.xMin += depth * 14f;
            if (isGenderNode)
            {
                EditorGUI.LabelField(rowRect, node.name, EditorStyles.miniBoldLabel);
            }
            else if (isObjectNode)
            {
                Rect foldoutRect = new Rect(rowRect.x, rowRect.y, 18f, rowRect.height);
                bool nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                if (nextExpanded != expanded)
                {
                    SetFolderExpanded(node.path, nextExpanded);
                }

                expanded = nextExpanded || searching;
                Rect selectRect = new Rect(
                    foldoutRect.xMax,
                    rowRect.y,
                    Mathf.Max(0f, rowRect.xMax - foldoutRect.xMax),
                    rowRect.height);
                bool selected = string.Equals(
                    selectedObjectPath,
                    node.path,
                    StringComparison.OrdinalIgnoreCase);
                string dirtyMarker = HasDirtyDefinitions(node) ? "* " : string.Empty;
                if (GUI.Toggle(selectRect, selected, dirtyMarker + node.name, "Button") && !selected)
                {
                    SelectObjectNode(node);
                }
            }
            else
            {
                bool nextExpanded = EditorGUI.Foldout(rowRect, expanded, node.name, true);
                if (nextExpanded != expanded)
                {
                    SetFolderExpanded(node.path, nextExpanded);
                }

                expanded = nextExpanded || searching;
            }
        }

        if (!expanded)
        {
            return true;
        }

        for (int i = 0; i < node.children.Count; i++)
        {
            DrawHierarchyNode(node.children[i], isRoot ? depth : depth + 1);
        }

        for (int i = 0; i < node.definitions.Count; i++)
        {
            AnimalDefinition definition = node.definitions[i];
            if (DefinitionMatchesSearch(definition))
            {
                DrawDefinitionRow(definition, isRoot ? depth : depth + 1);
            }
        }

        return true;
    }

    private static bool IsGenderNode(HierarchyNode node)
    {
        return node != null
               && (string.Equals(node.name, "Female", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(node.name, "Male", StringComparison.OrdinalIgnoreCase));
    }

    private void SetFolderExpanded(string path, bool expanded)
    {
        if (expanded)
        {
            expandedFolders.Add(path);
        }
        else
        {
            expandedFolders.Remove(path);
        }
    }

    private void SelectObjectNode(HierarchyNode node)
    {
        if (node == null)
        {
            return;
        }

        selectedObjectPath = node.path;
        selectedDefinitionInstanceId = 0;
        detailScroll = Vector2.zero;
        ResetPreview();
    }

    private bool HasDirtyDefinitions(HierarchyNode node)
    {
        if (node == null)
        {
            return false;
        }

        for (int i = 0; i < node.definitions.Count; i++)
        {
            AnimalDefinition definition = node.definitions[i];
            if (definition != null && GetDraft(definition).dirty)
            {
                return true;
            }
        }

        for (int i = 0; i < node.children.Count; i++)
        {
            if (HasDirtyDefinitions(node.children[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void DrawDefinitionRow(AnimalDefinition definition, int depth)
    {
        AnimalDraft draft = GetDraft(definition);
        bool selected = definition.GetInstanceID() == selectedDefinitionInstanceId;
        Rect rowRect = GUILayoutUtility.GetRect(1f, ListRowHeight, GUILayout.ExpandWidth(true));
        rowRect.xMin += depth * 14f;

        if (GUI.Toggle(rowRect, selected, GUIContent.none, "Button"))
        {
            if (!selected)
            {
                selectedDefinitionInstanceId = definition.GetInstanceID();
                selectedObjectPath = string.Empty;
                detailScroll = Vector2.zero;
                ResetPreview();
            }
        }

        Sprite icon = draft.adultIcon != null ? draft.adultIcon : draft.childIcon;
        Rect iconRect = new Rect(rowRect.x + 4f, rowRect.y + 4f, 20f, 20f);
        if (icon != null)
        {
            GUI.DrawTexture(iconRect, AssetPreview.GetAssetPreview(icon) ?? icon.texture, ScaleMode.ScaleToFit, true);
        }

        string dirtyMarker = draft.dirty ? "* " : string.Empty;
        string displayName = string.IsNullOrWhiteSpace(draft.animalName) ? definition.name : draft.animalName;
        Rect labelRect = new Rect(iconRect.xMax + 4f, rowRect.y, rowRect.xMax - iconRect.xMax - 8f, rowRect.height);
        GUI.Label(labelRect, $"{dirtyMarker}[{draft.id}] {displayName}", EditorStyles.miniLabel);
    }

    private void DrawDetailPanel()
    {
        Rect detailRect = new Rect(SidebarWidth, 0f, position.width - SidebarWidth, position.height);
        GUILayout.BeginArea(detailRect);
        GUILayout.Space(10f);

        HierarchyNode selectedObject = GetSelectedObjectNode();
        if (selectedObject != null)
        {
            DrawObjectDetailPanel(selectedObject);
            GUILayout.EndArea();
            return;
        }

        AnimalDefinition selectedDefinition = GetSelectedDefinition();
        if (selectedDefinition == null)
        {
            EditorGUILayout.HelpBox("왼쪽 계층에서 동물을 선택하세요.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        AnimalDraft draft = GetDraft(selectedDefinition);
        DrawSelectedHeader(selectedDefinition, draft);
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        DrawBasicFields(draft);
        GUILayout.Space(8f);
        DrawPreview(draft);
        GUILayout.Space(8f);
        DrawReadOnlyReferences(draft);
        GUILayout.Space(8f);
        DrawValidation(selectedDefinition, draft);
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawObjectDetailPanel(HierarchyNode objectNode)
    {
        selectedObjectDefinitions.Clear();
        CollectDefinitions(objectNode, selectedObjectDefinitions);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(objectNode.name, EditorStyles.largeLabel);
        EditorGUILayout.LabelField(
            $"Animal Object · {selectedObjectDefinitions.Count} variants",
            EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            AnimalDataEditorUtility.DefinitionRoot + "/" + objectNode.path,
            EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        string folderPath = AnimalDataEditorUtility.DefinitionRoot + "/" + objectNode.path;
        UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
        EditorGUI.BeginDisabledGroup(folder == null);
        if (GUILayout.Button("Ping Object Folder", GUILayout.Width(125f), GUILayout.Height(24f)))
        {
            EditorGUIUtility.PingObject(folder);
            Selection.activeObject = folder;
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "이 화면은 객체에 속한 성별 변형을 한곳에서 편집합니다. 각 변형은 기존 AnimalDefinition Asset에 독립적으로 저장됩니다.",
            MessageType.Info);

        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        DrawObjectSpawnSettings();
        GUILayout.Space(8f);
        for (int i = 0; i < selectedObjectDefinitions.Count; i++)
        {
            AnimalDefinition definition = selectedObjectDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            DrawObjectVariantEditor(objectNode, definition, GetDraft(definition));
            GUILayout.Space(8f);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawObjectVariantEditor(
        HierarchyNode objectNode,
        AnimalDefinition definition,
        AnimalDraft draft)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();

        Sprite icon = draft.adultIcon != null ? draft.adultIcon : draft.childIcon;
        Rect iconRect = GUILayoutUtility.GetRect(52f, 52f, GUILayout.Width(52f), GUILayout.Height(52f));
        EditorGUI.DrawRect(iconRect, new Color(0.1f, 0.1f, 0.1f));
        if (icon != null)
        {
            GUI.DrawTexture(iconRect, AssetPreview.GetAssetPreview(icon) ?? icon.texture, ScaleMode.ScaleToFit, true);
        }

        EditorGUILayout.BeginVertical();
        string variantName = GetObjectVariantName(objectNode, definition);
        string dirtyMarker = draft.dirty ? "* " : string.Empty;
        EditorGUILayout.LabelField($"{dirtyMarker}{variantName}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(definition), EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        if (GUILayout.Button("Full Details", GUILayout.Width(90f), GUILayout.Height(24f)))
        {
            selectedObjectPath = string.Empty;
            selectedDefinitionInstanceId = definition.GetInstanceID();
            detailScroll = Vector2.zero;
            ResetPreview();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawObjectSpawnSettings()
    {
        if (selectedObjectDefinitions.Count == 0)
        {
            return;
        }

        AnimalDraft firstDraft = GetDraft(selectedObjectDefinitions[0]);
        int currentAge = firstDraft.spawnAge;
        int currentMinHerdSize = firstDraft.minHerdSize;
        int currentMaxHerdSize = firstDraft.maxHerdSize;
        int currentSpawnWeight = firstDraft.spawnWeight;
        bool hasMixedSettings = false;
        for (int i = 1; i < selectedObjectDefinitions.Count; i++)
        {
            AnimalDraft draft = GetDraft(selectedObjectDefinitions[i]);
            if (draft.spawnAge != currentAge
                || draft.minHerdSize != currentMinHerdSize
                || draft.maxHerdSize != currentMaxHerdSize
                || draft.spawnWeight != currentSpawnWeight)
            {
                hasMixedSettings = true;
                break;
            }
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Object Spawn Settings", EditorStyles.boldLabel);
        EditorGUI.showMixedValue = hasMixedSettings;
        EditorGUI.BeginChangeCheck();
        int nextAge = EditorGUILayout.IntSlider(
            new GUIContent("Age Weight", "소환 나이 정규분포의 중심입니다. 이 나이가 가장 자주 생성됩니다."),
            currentAge,
            AnimalDefinition.MinSpawnAge,
            AnimalDefinition.MaxSpawnAge);
        int nextMinHerdSize = Mathf.Max(
            1,
            EditorGUILayout.IntField("Minimum Herd Size", currentMinHerdSize));
        int nextMaxHerdSize = Mathf.Max(
            nextMinHerdSize,
            EditorGUILayout.IntField("Maximum Herd Size", currentMaxHerdSize));
        int nextSpawnWeight = EditorGUILayout.IntSlider(
                new GUIContent("Spawn Weight", "무리 내 개체 수의 선호값입니다. 가까운 크기의 무리가 더 자주 생성됩니다."),
                Mathf.Clamp(currentSpawnWeight, nextMinHerdSize, nextMaxHerdSize),
                nextMinHerdSize,
                nextMaxHerdSize);
        bool settingsChanged = EditorGUI.EndChangeCheck();
        EditorGUI.showMixedValue = false;

        if (settingsChanged)
        {
            for (int i = 0; i < selectedObjectDefinitions.Count; i++)
            {
                AnimalDraft draft = GetDraft(selectedObjectDefinitions[i]);
                draft.spawnAge = nextAge;
                draft.minHerdSize = nextMinHerdSize;
                draft.maxHerdSize = nextMaxHerdSize;
                draft.spawnWeight = nextSpawnWeight;
                draft.dirty = true;
            }
        }

        DrawAgeDistributionGraph(nextAge);

        EditorGUILayout.HelpBox(
            "Age Weight를 중심으로 표준편차 2.0의 정규분포를 적용합니다. 나이와 무리 설정은 Female/Male에 함께 적용됩니다.",
            MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private static void DrawAgeDistributionGraph(int preferredAge)
    {
        const float graphHeight = 170f;
        const float leftPadding = 8f;
        const float rightPadding = 8f;
        const float topPadding = 26f;
        const float bottomPadding = 24f;

        EditorGUILayout.Space(5f);
        Rect graphRect = GUILayoutUtility.GetRect(10f, graphHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(graphRect, new Color(0.09f, 0.09f, 0.09f, 1f));
        GUI.Label(
            new Rect(graphRect.x + 7f, graphRect.y + 4f, graphRect.width - 14f, 18f),
            $"Spawn Age Distribution  (mean {preferredAge}, σ {AnimalDefinition.SpawnAgeStandardDeviation:0.0})",
            EditorStyles.miniBoldLabel);

        Rect plotRect = new Rect(
            graphRect.x + leftPadding,
            graphRect.y + topPadding,
            Mathf.Max(1f, graphRect.width - leftPadding - rightPadding),
            Mathf.Max(1f, graphRect.height - topPadding - bottomPadding));
        EditorGUI.DrawRect(new Rect(plotRect.x, plotRect.yMax - 1f, plotRect.width, 1f), new Color(0.5f, 0.5f, 0.5f));

        int ageCount = AnimalDefinition.MaxSpawnAge - AnimalDefinition.MinSpawnAge + 1;
        float columnWidth = plotRect.width / ageCount;
        float maxProbability = 0f;
        for (int age = AnimalDefinition.MinSpawnAge; age <= AnimalDefinition.MaxSpawnAge; age++)
        {
            maxProbability = Mathf.Max(
                maxProbability,
                AnimalDefinition.GetSpawnAgeProbability(age, preferredAge));
        }

        for (int age = AnimalDefinition.MinSpawnAge; age <= AnimalDefinition.MaxSpawnAge; age++)
        {
            int index = age - AnimalDefinition.MinSpawnAge;
            float probability = AnimalDefinition.GetSpawnAgeProbability(age, preferredAge);
            float normalizedHeight = maxProbability > 0f ? probability / maxProbability : 0f;
            float barWidth = Mathf.Max(2f, columnWidth - 5f);
            float barHeight = Mathf.Max(1f, normalizedHeight * (plotRect.height - 18f));
            Rect barRect = new Rect(
                plotRect.x + index * columnWidth + (columnWidth - barWidth) * 0.5f,
                plotRect.yMax - barHeight,
                barWidth,
                barHeight);
            Color barColor = age == preferredAge
                ? new Color(1f, 0.62f, 0.16f, 1f)
                : new Color(0.3f, 0.7f, 0.95f, 1f);
            EditorGUI.DrawRect(barRect, barColor);

            GUI.Label(
                new Rect(plotRect.x + index * columnWidth, plotRect.yMax + 2f, columnWidth, 18f),
                age.ToString(),
                EditorStyles.centeredGreyMiniLabel);
            GUI.Label(
                new Rect(plotRect.x + index * columnWidth, Mathf.Max(plotRect.y, barRect.y - 16f), columnWidth, 16f),
                $"{probability * 100f:0.#}%",
                EditorStyles.centeredGreyMiniLabel);
        }
    }

    private void DrawSelectedHeader(AnimalDefinition definition, AnimalDraft draft)
    {
        EditorGUILayout.BeginHorizontal();
        Rect iconRect = GUILayoutUtility.GetRect(72f, 72f, GUILayout.Width(72f));
        EditorGUI.DrawRect(iconRect, new Color(0.1f, 0.1f, 0.1f));
        if (draft.adultIcon != null)
        {
            GUI.DrawTexture(iconRect, AssetPreview.GetAssetPreview(draft.adultIcon) ?? draft.adultIcon.texture, ScaleMode.ScaleToFit, true);
        }

        EditorGUILayout.BeginVertical();
        string displayName = string.IsNullOrWhiteSpace(draft.animalName) ? definition.name : draft.animalName;
        EditorGUILayout.LabelField($"[{draft.id}] {displayName}", EditorStyles.largeLabel);
        EditorGUILayout.LabelField(AnimalDataEditorUtility.GetHierarchyPath(definition), EditorStyles.miniLabel);
        EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(definition), EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Ping Definition", GUILayout.Width(110f)))
        {
            EditorGUIUtility.PingObject(definition);
            Selection.activeObject = definition;
        }

        EditorGUI.BeginDisabledGroup(draft.prefab == null);
        if (GUILayout.Button("Ping Prefab", GUILayout.Width(100f)))
        {
            EditorGUIUtility.PingObject(draft.prefab);
            Selection.activeObject = draft.prefab;
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBasicFields(AnimalDraft draft)
    {
        EditorGUILayout.LabelField("Gender Variant", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        int nextId = EditorGUILayout.IntField("Animal ID", draft.id);
        string nextName = EditorGUILayout.TextField("Animal Name", draft.animalName ?? string.Empty);
        GameObject nextPrefab = (GameObject)EditorGUILayout.ObjectField("Animal Prefab", draft.prefab, typeof(GameObject), false);
        Sprite nextAdultIcon = (Sprite)EditorGUILayout.ObjectField("Adult Icon", draft.adultIcon, typeof(Sprite), false);
        Sprite nextChildIcon = (Sprite)EditorGUILayout.ObjectField("Child Icon", draft.childIcon, typeof(Sprite), false);
        if (EditorGUI.EndChangeCheck())
        {
            bool prefabChanged = nextPrefab != draft.prefab;
            draft.id = Mathf.Max(-1, nextId);
            draft.animalName = nextName;
            draft.prefab = nextPrefab;
            draft.adultIcon = nextAdultIcon;
            draft.childIcon = nextChildIcon;
            draft.dirty = true;
            if (prefabChanged)
            {
                ResetPreview();
            }
        }

        if (draft.dirty)
        {
            EditorGUILayout.HelpBox("저장되지 않은 변경 사항입니다. Save를 누르면 Asset과 기본 JSON에 반영됩니다.", MessageType.Info);
        }
    }

    private void DrawPreview(AnimalDraft draft)
    {
        EditorGUILayout.LabelField("3D Preview", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        float nextAge = EditorGUILayout.Slider("Age", previewAge, 0f, 10f);
        if (!Mathf.Approximately(nextAge, previewAge))
        {
            previewAge = nextAge;
            ApplyPreviewAge();
        }

        if (GUILayout.Button("Reset View", GUILayout.Width(90f)))
        {
            previewOrbit = new Vector2(145f, 12f);
            previewZoom = 1f;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        int nextAnimationIndex = EditorGUILayout.Popup("Animation", previewAnimationIndex, AnimationStateLabels);
        if (nextAnimationIndex != previewAnimationIndex)
        {
            previewAnimationIndex = nextAnimationIndex;
            ApplyPreviewAnimation();
        }

        bool nextPlaying = GUILayout.Toggle(previewPlaying, previewPlaying ? "Pause" : "Play", "Button", GUILayout.Width(70f));
        if (nextPlaying != previewPlaying)
        {
            previewPlaying = nextPlaying;
            lastPreviewUpdateTime = EditorApplication.timeSinceStartup;
        }
        EditorGUILayout.EndHorizontal();

        Rect previewRect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(previewRect, new Color(0.08f, 0.08f, 0.08f));
        HandlePreviewInput(previewRect);
        DrawPreviewContents(previewRect, draft.prefab);
        GUI.Label(new Rect(previewRect.x + 8f, previewRect.yMax - 22f, previewRect.width - 16f, 18f),
            "Drag: Rotate  |  Wheel: Zoom", EditorStyles.miniLabel);
    }

    private void DrawPreviewContents(Rect previewRect, GameObject prefab)
    {
        EnsurePreview(prefab);
        if (previewRenderer == null || previewInstance == null)
        {
            EditorGUI.DropShadowLabel(previewRect, prefab == null ? "Assign an Animal Prefab" : "Preview unavailable");
            return;
        }

        Bounds bounds = CalculatePreviewBounds(previewInstance);
        Vector3 center = bounds.center;
        float radius = Mathf.Max(0.25f, bounds.extents.magnitude);
        float distance = radius * 2.6f * previewZoom;
        Quaternion rotation = Quaternion.Euler(previewOrbit.y, previewOrbit.x, 0f);

        Camera camera = previewRenderer.camera;
        camera.transform.position = center + rotation * (Vector3.back * distance);
        camera.transform.LookAt(center);
        camera.nearClipPlane = Mathf.Max(0.001f, distance - radius * 2f);
        camera.farClipPlane = Mathf.Max(100f, distance + radius * 5f);

        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        previewRenderer.BeginPreview(previewRect, GUIStyle.none);
        previewRenderer.Render(true, true);
        Texture texture = previewRenderer.EndPreview();
        GUI.DrawTexture(previewRect, texture, ScaleMode.StretchToFill, false);
    }

    private void HandlePreviewInput(Rect previewRect)
    {
        Event current = Event.current;
        if (!previewRect.Contains(current.mousePosition))
        {
            return;
        }

        if (current.type == EventType.ScrollWheel)
        {
            previewZoom = Mathf.Clamp(previewZoom + current.delta.y * 0.05f, 0.35f, 4f);
            current.Use();
            Repaint();
        }
        else if (current.type == EventType.MouseDrag && (current.button == 0 || current.button == 1))
        {
            previewOrbit.x += current.delta.x;
            previewOrbit.y = Mathf.Clamp(previewOrbit.y - current.delta.y, -89f, 89f);
            current.Use();
            Repaint();
        }
    }

    private void EnsurePreview(GameObject prefab)
    {
        if (prefab == previewPrefab && previewRenderer != null && previewInstance != null)
        {
            return;
        }

        DisposePreview();
        previewPrefab = prefab;
        if (prefab == null)
        {
            return;
        }

        try
        {
            previewRenderer = new PreviewRenderUtility(true);
            previewRenderer.cameraFieldOfView = 30f;
            previewRenderer.ambientColor = new Color(0.32f, 0.32f, 0.32f, 1f);
            previewRenderer.lights[0].intensity = 1.15f;
            previewRenderer.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
            previewRenderer.lights[1].intensity = 0.65f;
            previewRenderer.lights[1].transform.rotation = Quaternion.Euler(340f, 215f, 0f);
            previewInstance = previewRenderer.InstantiatePrefabInScene(prefab);
            if (previewInstance == null)
            {
                DisposePreview();
                return;
            }

            previewInstance.transform.position = Vector3.zero;
            previewInstance.transform.rotation = Quaternion.identity;
            previewAnimal = previewInstance.GetComponentInChildren<Animal>(true);
            previewAnimator = previewInstance.GetComponentInChildren<Animator>(true);
            ApplyPreviewAge();
            ApplyPreviewAnimation();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            DisposePreview();
        }
    }

    private void ApplyPreviewAge()
    {
        if (previewAnimal != null)
        {
            previewAnimal.SetAge(previewAge);
        }
    }

    private void ApplyPreviewAnimation()
    {
        if (previewAnimator == null || AnimationStates.Length == 0)
        {
            return;
        }

        previewAnimationIndex = Mathf.Clamp(previewAnimationIndex, 0, AnimationStates.Length - 1);
        previewAnimator.Rebind();
        previewAnimator.SetInteger("State", AnimationStates[previewAnimationIndex]);
        previewAnimator.Update(0f);
    }

    private void DrawReadOnlyReferences(AnimalDraft draft)
    {
        EditorGUILayout.LabelField("Auto-filled References (Read Only)", EditorStyles.boldLabel);
        Animal animal = AnimalDataEditorUtility.FindAnimal(draft.prefab);
        AnimalDataEditorUtility.DrawReadOnlyAnimalReferences(animal);
    }

    private void DrawValidation(AnimalDefinition definition, AnimalDraft draft)
    {
        EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
        if (draft.dirty)
        {
            EditorGUILayout.HelpBox("기본 필드 검증은 Save 후 확정됩니다.", MessageType.Info);
        }

        selectedIssues.Clear();
        selectedIssues.AddRange(AnimalDataEditorUtility.ValidateDefinition(definition, definitions));
        if (selectedIssues.Count == 0)
        {
            EditorGUILayout.HelpBox("문제를 찾지 못했습니다.", MessageType.Info);
            return;
        }

        for (int i = 0; i < selectedIssues.Count; i++)
        {
            AnimalValidationIssue issue = selectedIssues[i];
            MessageType messageType = issue.severity == AnimalValidationSeverity.Error
                ? MessageType.Error
                : issue.severity == AnimalValidationSeverity.Warning
                    ? MessageType.Warning
                    : MessageType.Info;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(issue.message, messageType);
            EditorGUI.BeginDisabledGroup(issue.context == null);
            if (GUILayout.Button("Ping", GUILayout.Width(45f), GUILayout.Height(38f)))
            {
                EditorGUIUtility.PingObject(issue.context);
                Selection.activeObject = issue.context;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }
    }

    private void SaveDrafts()
    {
        int savedCount = 0;
        foreach (KeyValuePair<int, AnimalDraft> pair in drafts)
        {
            AnimalDraft draft = pair.Value;
            if (draft == null || draft.definition == null || !draft.dirty)
            {
                continue;
            }

            AnimalDataEditorUtility.ApplyDefinition(
                draft.definition,
                draft.id,
                draft.animalName,
                draft.prefab,
                draft.adultIcon,
                draft.childIcon,
                draft.spawnAge,
                draft.minHerdSize,
                draft.maxHerdSize,
                draft.spawnWeight,
                "Save Animal Data");
            draft.dirty = false;
            savedCount++;
        }

        AssetDatabase.SaveAssets();
        WriteJson(AnimalDataEditorUtility.DefaultJsonPath, definitions);
        AssetDatabase.Refresh();
        ReloadDefinitions(false);
        AnimalDataEditorUtility.LogValidation(definitions);
        Debug.Log($"Animal Data: saved {savedCount} changed definitions and exported "
                  + AnimalDataEditorUtility.DefaultJsonPath + ".");
    }

    private void ReloadDefinitionsWithConfirmation()
    {
        if (HasDirtyDrafts()
            && !EditorUtility.DisplayDialog(
                "Load Animal Data",
                "저장하지 않은 변경 사항을 버리고 Asset에서 다시 불러오시겠습니까?",
                "Load",
                "Cancel"))
        {
            return;
        }

        ReloadDefinitions(false);
    }

    private void ReloadDefinitions(bool preserveDirtyDrafts)
    {
        Dictionary<string, AnimalDraft> oldDraftsByPath = new Dictionary<string, AnimalDraft>(StringComparer.OrdinalIgnoreCase);
        if (preserveDirtyDrafts)
        {
            foreach (AnimalDraft draft in drafts.Values)
            {
                if (draft != null && draft.definition != null && draft.dirty)
                {
                    oldDraftsByPath[AssetDatabase.GetAssetPath(draft.definition)] = draft;
                }
            }
        }

        definitions.Clear();
        definitions.AddRange(AnimalDataEditorUtility.LoadDefinitions());
        drafts.Clear();
        for (int i = 0; i < definitions.Count; i++)
        {
            AnimalDefinition definition = definitions[i];
            string path = AssetDatabase.GetAssetPath(definition);
            AnimalDraft draft;
            if (!oldDraftsByPath.TryGetValue(path, out draft))
            {
                draft = new AnimalDraft(definition);
            }

            drafts[definition.GetInstanceID()] = draft;
        }

        BuildHierarchy();
        EnsureSelection();
        ResetPreview();
        Repaint();
    }

    private void RebuildDefinitions()
    {
        if (!AssetDatabase.IsValidFolder(AnimalDataEditorUtility.DefinitionRoot))
        {
            Debug.LogError("Animal Data: missing folder " + AnimalDataEditorUtility.DefinitionRoot);
            return;
        }

        List<AnimalDefinition> currentDefinitions = AnimalDataEditorUtility.LoadDefinitions();
        int nextId = GetNextAvailableId(currentDefinitions);
        int createdCount = 0;
        int filledCount = 0;
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { AnimalDataEditorUtility.DefinitionRoot });
        Dictionary<string, List<string>> texturePathsByFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < textureGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
            string folder = NormalizePath(Path.GetDirectoryName(path));
            if (!texturePathsByFolder.TryGetValue(folder, out List<string> paths))
            {
                paths = new List<string>();
                texturePathsByFolder.Add(folder, paths);
            }

            paths.Add(path);
        }

        foreach (KeyValuePair<string, List<string>> pair in texturePathsByFolder)
        {
            List<string> adultIconPaths = GetAdultIconPaths(pair.Value);
            for (int iconIndex = 0; iconIndex < adultIconPaths.Count; iconIndex++)
            {
                string adultIconPath = adultIconPaths[iconIndex];
                string baseName = GetIconBaseName(adultIconPath);
                string childIconPath = FindChildIconPath(pair.Value, baseName);
                Sprite adultIcon = AssetDatabase.LoadAssetAtPath<Sprite>(adultIconPath);
                Sprite childIcon = !string.IsNullOrEmpty(childIconPath)
                    ? AssetDatabase.LoadAssetAtPath<Sprite>(childIconPath)
                    : null;
                AnimalDefinition definition = FindDefinitionForIcon(currentDefinitions, pair.Key, adultIcon);
                GameObject prefab = FindBestPrefab(pair.Key, baseName);
                string displayName = baseName.Replace('_', ' ').Trim();

                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<AnimalDefinition>();
                    string assetName = $"Animal_{nextId:D3}_{SanitizeFileName(baseName)}.asset";
                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(NormalizePath(Path.Combine(pair.Key, assetName)));
                    AssetDatabase.CreateAsset(definition, assetPath);
                    AnimalDataEditorUtility.ApplyDefinition(
                        definition,
                        nextId++,
                        displayName,
                        prefab,
                        adultIcon,
                        childIcon,
                        AnimalDefinition.DefaultSpawnAge,
                        AnimalDefinition.DefaultMinHerdSize,
                        AnimalDefinition.DefaultMaxHerdSize,
                        AnimalDefinition.DefaultSpawnWeight,
                        "Create Animal Definition");
                    currentDefinitions.Add(definition);
                    createdCount++;
                    continue;
                }

                bool prefabChanged = prefab != null && definition.AnimalPrefab != prefab;
                bool needsRebuild = definition.Id < 0
                                    || string.IsNullOrWhiteSpace(definition.AnimalName)
                                    || definition.AnimalPrefab == null
                                    || definition.AdultIcon == null
                                    || definition.ChildIcon == null
                                    || prefabChanged;
                if (!needsRebuild)
                {
                    AnimalDataEditorUtility.EnsureDefinitionLink(definition);
                    continue;
                }

                AnimalDataEditorUtility.ApplyDefinition(
                    definition,
                    definition.Id >= 0 ? definition.Id : nextId++,
                    string.IsNullOrWhiteSpace(definition.AnimalName) ? displayName : definition.AnimalName,
                    prefab != null ? prefab : definition.AnimalPrefab,
                    definition.AdultIcon != null ? definition.AdultIcon : adultIcon,
                    definition.ChildIcon != null ? definition.ChildIcon : childIcon,
                    definition.SpawnAgeWeight,
                    definition.MinHerdSize,
                    definition.MaxHerdSize,
                    definition.SpawnWeight,
                    "Rebuild Animal Definition");
                filledCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ReloadDefinitions(false);
        WriteJson(AnimalDataEditorUtility.DefaultJsonPath, definitions);
        AssetDatabase.Refresh();
        AnimalDataEditorUtility.LogValidation(definitions);
        Debug.Log($"Animal Data rebuild complete: {createdCount} created, {filledCount} completed.");
    }

    private void ExportJsonWithDialog()
    {
        string absolutePath = EditorUtility.SaveFilePanel(
            "Export Animal Data JSON",
            Application.dataPath,
            "animal_data",
            "json");
        if (string.IsNullOrEmpty(absolutePath))
        {
            return;
        }

        WriteJson(absolutePath, definitions);
        AssetDatabase.Refresh();
    }

    private void LoadJsonWithDialog()
    {
        string absolutePath = EditorUtility.OpenFilePanel("Load Animal Data JSON", Application.dataPath, "json");
        if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
        {
            return;
        }

        AnimalJsonFile file;
        try
        {
            file = JsonUtility.FromJson<AnimalJsonFile>(File.ReadAllText(absolutePath));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return;
        }

        if (file == null || !string.Equals(file.format, "ProjectF.AnimalData", StringComparison.Ordinal))
        {
            Debug.LogError("Animal Data: unsupported JSON format.");
            return;
        }

        int loadedCount = 0;
        for (int i = 0; file.animals != null && i < file.animals.Count; i++)
        {
            AnimalJsonEntry entry = file.animals[i];
            AnimalDefinition definition = ResolveJsonDefinition(entry);
            if (definition == null)
            {
                Debug.LogWarning($"Animal Data: definition not found for JSON entry '{entry.animalName}'.");
                continue;
            }

            AnimalDraft draft = GetDraft(definition);
            draft.id = entry.id;
            draft.animalName = entry.animalName;
            draft.spawnAge = entry.spawnAge >= AnimalDefinition.MinSpawnAge
                ? Mathf.Clamp(entry.spawnAge, AnimalDefinition.MinSpawnAge, AnimalDefinition.MaxSpawnAge)
                : AnimalDefinition.DefaultSpawnAge;
            draft.minHerdSize = entry.minHerdSize > 0
                ? entry.minHerdSize
                : AnimalDefinition.DefaultMinHerdSize;
            draft.maxHerdSize = entry.maxHerdSize >= draft.minHerdSize
                ? entry.maxHerdSize
                : Mathf.Max(draft.minHerdSize, AnimalDefinition.DefaultMaxHerdSize);
            draft.spawnWeight = file.version >= 3
                ? Mathf.Clamp(entry.spawnWeight, draft.minHerdSize, draft.maxHerdSize)
                : Mathf.Clamp(AnimalDefinition.DefaultSpawnWeight, draft.minHerdSize, draft.maxHerdSize);
            draft.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabAssetPath);
            draft.adultIcon = AssetDatabase.LoadAssetAtPath<Sprite>(entry.adultIconAssetPath);
            draft.childIcon = AssetDatabase.LoadAssetAtPath<Sprite>(entry.childIconAssetPath);
            draft.dirty = true;
            loadedCount++;
        }

        ResetPreview();
        Repaint();
        Debug.Log($"Animal Data: loaded {loadedCount} JSON entries into drafts. Press Save to apply.");
    }

    private static void WriteJson(string path, IReadOnlyList<AnimalDefinition> sourceDefinitions)
    {
        AnimalJsonFile file = new AnimalJsonFile();
        for (int i = 0; sourceDefinitions != null && i < sourceDefinitions.Count; i++)
        {
            AnimalDefinition definition = sourceDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            file.animals.Add(new AnimalJsonEntry
            {
                id = definition.Id,
                animalName = definition.AnimalName,
                spawnAge = definition.SpawnAgeWeight,
                minHerdSize = definition.MinHerdSize,
                maxHerdSize = definition.MaxHerdSize,
                spawnWeight = definition.SpawnWeight,
                hierarchyPath = AnimalDataEditorUtility.GetHierarchyPath(definition),
                definitionAssetPath = AssetDatabase.GetAssetPath(definition),
                prefabAssetPath = AssetDatabase.GetAssetPath(definition.AnimalPrefab),
                adultIconAssetPath = AssetDatabase.GetAssetPath(definition.AdultIcon),
                childIconAssetPath = AssetDatabase.GetAssetPath(definition.ChildIcon)
            });
        }

        string absolutePath = ToAbsolutePath(path);
        string directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(absolutePath, JsonUtility.ToJson(file, true));
        Debug.Log("Animal Data JSON exported: " + absolutePath);
    }

    private AnimalDefinition ResolveJsonDefinition(AnimalJsonEntry entry)
    {
        if (entry == null)
        {
            return null;
        }

        AnimalDefinition byPath = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(entry.definitionAssetPath);
        if (byPath != null)
        {
            return byPath;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            AnimalDefinition definition = definitions[i];
            if (definition != null && definition.Id == entry.id)
            {
                return definition;
            }
        }

        return null;
    }

    private void BuildHierarchy()
    {
        hierarchyRoot = new HierarchyNode("Animals", string.Empty);
        expandedFolders.Clear();
        for (int i = 0; i < definitions.Count; i++)
        {
            AnimalDefinition definition = definitions[i];
            string hierarchyPath = AnimalDataEditorUtility.GetHierarchyPath(definition);
            HierarchyNode node = hierarchyRoot;
            if (!string.IsNullOrWhiteSpace(hierarchyPath))
            {
                string[] segments = hierarchyPath.Split('/');
                string currentPath = string.Empty;
                for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
                {
                    string segment = segments[segmentIndex];
                    currentPath = string.IsNullOrEmpty(currentPath) ? segment : currentPath + "/" + segment;
                    node = GetOrCreateChild(node, segment, currentPath);
                    expandedFolders.Add(currentPath);
                }
            }

            node.definitions.Add(definition);
        }

        SortHierarchy(hierarchyRoot);
    }

    private static void CollectDefinitions(HierarchyNode node, List<AnimalDefinition> results)
    {
        if (node == null || results == null)
        {
            return;
        }

        for (int i = 0; i < node.definitions.Count; i++)
        {
            AnimalDefinition definition = node.definitions[i];
            if (definition != null)
            {
                results.Add(definition);
            }
        }

        for (int i = 0; i < node.children.Count; i++)
        {
            CollectDefinitions(node.children[i], results);
        }
    }

    private string GetObjectVariantName(HierarchyNode objectNode, AnimalDefinition definition)
    {
        string hierarchyPath = AnimalDataEditorUtility.GetHierarchyPath(definition);
        string prefix = objectNode.path + "/";
        string variantPath = hierarchyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? hierarchyPath.Substring(prefix.Length)
            : hierarchyPath;
        AnimalDraft draft = GetDraft(definition);
        string displayName = string.IsNullOrWhiteSpace(draft.animalName)
            ? definition.name
            : draft.animalName;
        return string.IsNullOrWhiteSpace(variantPath)
            ? $"[{draft.id}] {displayName}"
            : $"{variantPath} · [{draft.id}] {displayName}";
    }

    private static HierarchyNode GetOrCreateChild(HierarchyNode parent, string name, string path)
    {
        for (int i = 0; i < parent.children.Count; i++)
        {
            if (string.Equals(parent.children[i].name, name, StringComparison.OrdinalIgnoreCase))
            {
                return parent.children[i];
            }
        }

        HierarchyNode child = new HierarchyNode(name, path);
        parent.children.Add(child);
        return child;
    }

    private static void SortHierarchy(HierarchyNode node)
    {
        node.children.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
        node.definitions.Sort(AnimalDataEditorUtility.CompareDefinitions);
        for (int i = 0; i < node.children.Count; i++)
        {
            SortHierarchy(node.children[i]);
        }
    }

    private bool NodeMatchesSearch(HierarchyNode node)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        if (node.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        for (int i = 0; i < node.definitions.Count; i++)
        {
            if (DefinitionMatchesSearch(node.definitions[i]))
            {
                return true;
            }
        }

        for (int i = 0; i < node.children.Count; i++)
        {
            if (NodeMatchesSearch(node.children[i]))
            {
                return true;
            }
        }

        return false;
    }

    private bool DefinitionMatchesSearch(AnimalDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(searchText))
        {
            return definition != null;
        }

        AnimalDraft draft = GetDraft(definition);
        return draft.id.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
               || (draft.animalName?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
               || definition.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private AnimalDraft GetDraft(AnimalDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        int instanceId = definition.GetInstanceID();
        if (!drafts.TryGetValue(instanceId, out AnimalDraft draft))
        {
            draft = new AnimalDraft(definition);
            drafts.Add(instanceId, draft);
        }

        return draft;
    }

    private AnimalDefinition GetSelectedDefinition()
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i] != null && definitions[i].GetInstanceID() == selectedDefinitionInstanceId)
            {
                return definitions[i];
            }
        }

        return null;
    }

    private HierarchyNode GetSelectedObjectNode()
    {
        return string.IsNullOrWhiteSpace(selectedObjectPath)
            ? null
            : FindHierarchyNode(hierarchyRoot, selectedObjectPath);
    }

    private static HierarchyNode FindHierarchyNode(HierarchyNode node, string path)
    {
        if (node == null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (string.Equals(node.path, path, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        for (int i = 0; i < node.children.Count; i++)
        {
            HierarchyNode match = FindHierarchyNode(node.children[i], path);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private void EnsureSelection()
    {
        if (GetSelectedObjectNode() != null)
        {
            selectedDefinitionInstanceId = 0;
            return;
        }

        if (GetSelectedDefinition() != null)
        {
            selectedObjectPath = string.Empty;
            return;
        }

        if (hierarchyRoot.children.Count > 0)
        {
            SelectObjectNode(hierarchyRoot.children[0]);
            return;
        }

        selectedObjectPath = string.Empty;
        selectedDefinitionInstanceId = definitions.Count > 0 && definitions[0] != null
            ? definitions[0].GetInstanceID()
            : 0;
    }

    private bool HasDirtyDrafts()
    {
        foreach (AnimalDraft draft in drafts.Values)
        {
            if (draft != null && draft.dirty)
            {
                return true;
            }
        }

        return false;
    }

    private void ResetPreview()
    {
        DisposePreview();
        previewPrefab = null;
        previewPlaying = false;
        previewAge = 10f;
        previewAnimationIndex = 0;
    }

    private void DisposePreview()
    {
        previewInstance = null;
        previewAnimal = null;
        previewAnimator = null;
        if (previewRenderer != null)
        {
            previewRenderer.Cleanup();
            previewRenderer = null;
        }
    }

    private static Bounds CalculatePreviewBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(instance.transform.position, Vector3.one);
        bool initialized = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

    private static List<string> GetAdultIconPaths(List<string> paths)
    {
        List<string> results = new List<string>();
        for (int i = 0; paths != null && i < paths.Count; i++)
        {
            string fileName = Path.GetFileNameWithoutExtension(paths[i]);
            if (fileName.EndsWith("_Icon", StringComparison.OrdinalIgnoreCase)
                && fileName.IndexOf("Child", StringComparison.OrdinalIgnoreCase) < 0)
            {
                results.Add(paths[i]);
            }
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    private static string GetIconBaseName(string adultIconPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(adultIconPath);
        return fileName.EndsWith("_Icon", StringComparison.OrdinalIgnoreCase)
            ? fileName.Substring(0, fileName.Length - "_Icon".Length)
            : fileName;
    }

    private static string FindChildIconPath(List<string> paths, string baseName)
    {
        string expectedName = baseName + "_Child_Icon";
        for (int i = 0; paths != null && i < paths.Count; i++)
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(paths[i]), expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return paths[i];
            }
        }

        return string.Empty;
    }

    private static AnimalDefinition FindDefinitionForIcon(
        List<AnimalDefinition> currentDefinitions,
        string folder,
        Sprite adultIcon)
    {
        for (int i = 0; i < currentDefinitions.Count; i++)
        {
            AnimalDefinition definition = currentDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            string definitionFolder = NormalizePath(Path.GetDirectoryName(AssetDatabase.GetAssetPath(definition)));
            if (string.Equals(definitionFolder, folder, StringComparison.OrdinalIgnoreCase)
                && (definition.AdultIcon == adultIcon || definition.AdultIcon == null))
            {
                return definition;
            }
        }

        return null;
    }

    private static GameObject FindBestPrefab(string definitionFolder, string baseName)
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { AnimalDataEditorUtility.PrefabRoot });
        GameObject bestPrefab = null;
        int bestScore = int.MinValue;
        string relativeFolder = definitionFolder.StartsWith(AnimalDataEditorUtility.DefinitionRoot + "/", StringComparison.OrdinalIgnoreCase)
            ? definitionFolder.Substring(AnimalDataEditorUtility.DefinitionRoot.Length + 1)
            : definitionFolder;
        string[] hierarchySegments = relativeFolder.Split('/');
        string[] nameTokens = baseName.Split('_');

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
            int score = 0;
            if (string.Equals(prefabName, baseName, StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }

            for (int segmentIndex = 0; segmentIndex < hierarchySegments.Length; segmentIndex++)
            {
                if (prefabPath.IndexOf("/" + hierarchySegments[segmentIndex] + "/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += segmentIndex == 0 ? 100 : 20;
                }
            }

            for (int tokenIndex = 0; tokenIndex < nameTokens.Length; tokenIndex++)
            {
                if (prefabName.IndexOf(nameTokens[tokenIndex], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += tokenIndex == 0 ? 50 : 10;
                }
            }

            if (score <= bestScore)
            {
                continue;
            }

            GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (candidate == null)
            {
                continue;
            }

            bestPrefab = candidate;
            bestScore = score;
        }

        return bestScore > 0 ? bestPrefab : null;
    }

    private static int GetNextAvailableId(List<AnimalDefinition> sourceDefinitions)
    {
        HashSet<int> usedIds = new HashSet<int>();
        for (int i = 0; sourceDefinitions != null && i < sourceDefinitions.Count; i++)
        {
            if (sourceDefinitions[i] != null && sourceDefinitions[i].Id >= 0)
            {
                usedIds.Add(sourceDefinitions[i].Id);
            }
        }

        int id = 0;
        while (usedIds.Contains(id))
        {
            id++;
        }

        return id;
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidCharacters.Length; i++)
        {
            value = value.Replace(invalidCharacters[i], '_');
        }

        return value.Replace(' ', '_');
    }

    private static string ToAbsolutePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.GetFullPath(Path.Combine(projectRoot, path));
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }
}
