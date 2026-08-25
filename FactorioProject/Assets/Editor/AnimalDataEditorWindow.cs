using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
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

    private static readonly string[] DefaultAnimationStateLabels =
    {
        "Idle", "Attack", "Get Hit 1", "Jump", "Roll", "Kick Front", "Death", "Get Hit 2",
        "Gallop", "Prancing", "Swim", "Eating", "Walk", "Look Around", "Run", "Rest (Sleep)",
        "Enter Water", "Exit Water"
    };

    [Serializable]
    private sealed class AnimalJsonFile
    {
        public string format = "ProjectF.AnimalData";
        public int version = 11;
        public List<AnimalJsonEntry> animals = new List<AnimalJsonEntry>();
    }

    [Serializable]
    private sealed class AnimalDropJsonEntry
    {
        public int itemId = -1;
        public string itemName = string.Empty;
        public string itemAssetPath = string.Empty;
        public int minAmount = 1;
        public int maxAmount = 1;
        public float dropChance = 1f;
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
        public float maxHealth = AnimalDefinition.DefaultMaxHealth;
        public bool canRiding = true;
        public float riderHeight = AnimalDefinition.DefaultRiderHeight;
        public float strength = AnimalDefinition.DefaultStrength;
        public AnimalAISettings aiSettings = new AnimalAISettings();
        public List<AnimalDropJsonEntry> dropItems = new List<AnimalDropJsonEntry>();
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
        public float maxHealth;
        public bool canRiding;
        public float riderHeight;
        public float strength;
        public AnimalAISettings aiSettings;
        public List<AnimalDropEntry> dropItems;
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
            maxHealth = definition != null ? definition.MaxHealth : AnimalDefinition.DefaultMaxHealth;
            canRiding = definition == null || definition.CanBeRidden;
            riderHeight = definition != null ? definition.RiderHeight : AnimalDefinition.DefaultRiderHeight;
            strength = definition != null ? definition.Strength : AnimalDefinition.DefaultStrength;
            aiSettings = definition != null && definition.AISettings != null
                ? definition.AISettings.Clone()
                : new AnimalAISettings();
            dropItems = definition != null
                ? AnimalDropEntry.CloneList(definition.DropItems)
                : new List<AnimalDropEntry>();
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

    private sealed class DropItemPopupContent : PopupWindowContent
    {
        private const float PopupWidth = 340f;
        private const float MaximumPopupHeight = 420f;
        private const float RowHeight = 24f;
        private const float IconSize = 18f;

        private readonly GUIContent[] options;
        private readonly IReadOnlyList<ItemDefinition> itemDefinitions;
        private readonly int selectedOptionIndex;
        private readonly Action<int> selectionCallback;
        private Vector2 scrollPosition;

        public DropItemPopupContent(
            GUIContent[] options,
            IReadOnlyList<ItemDefinition> itemDefinitions,
            int selectedOptionIndex,
            Action<int> selectionCallback)
        {
            this.options = options;
            this.itemDefinitions = itemDefinitions;
            this.selectedOptionIndex = selectedOptionIndex;
            this.selectionCallback = selectionCallback;
        }

        public override Vector2 GetWindowSize()
        {
            float contentHeight = options.Length * RowHeight + 4f;
            return new Vector2(
                PopupWidth,
                Mathf.Min(MaximumPopupHeight, contentHeight));
        }

        public override void OnGUI(Rect rect)
        {
            Event currentEvent = Event.current;
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < options.Length; i++)
            {
                Rect rowRect = GUILayoutUtility.GetRect(
                    0f,
                    RowHeight,
                    GUILayout.ExpandWidth(true));
                bool isSelected = i == selectedOptionIndex;
                bool isHovered = rowRect.Contains(currentEvent.mousePosition);
                if (Event.current.type == EventType.Repaint
                    && (isSelected || isHovered))
                {
                    Color backgroundColor = isSelected
                        ? new Color(0.24f, 0.49f, 0.90f, 0.45f)
                        : new Color(0.5f, 0.5f, 0.5f, 0.20f);
                    EditorGUI.DrawRect(rowRect, backgroundColor);
                }

                Sprite icon = ResolveOptionIcon(i);
                float textOffset = 7f;
                if (icon != null)
                {
                    Rect iconRect = new Rect(
                        rowRect.x + 5f,
                        rowRect.y + (rowRect.height - IconSize) * 0.5f,
                        IconSize,
                        IconSize);
                    DrawSprite(iconRect, icon);
                    textOffset = IconSize + 10f;
                }

                Rect textRect = new Rect(
                    rowRect.x + textOffset,
                    rowRect.y,
                    rowRect.width - textOffset - 4f,
                    rowRect.height);
                EditorGUI.LabelField(textRect, options[i].text);

                if (currentEvent.type == EventType.MouseDown
                    && currentEvent.button == 0
                    && rowRect.Contains(currentEvent.mousePosition))
                {
                    selectionCallback?.Invoke(i);
                    editorWindow.Close();
                    currentEvent.Use();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private Sprite ResolveOptionIcon(int optionIndex)
        {
            return optionIndex > 0 && optionIndex <= itemDefinitions.Count
                ? itemDefinitions[optionIndex - 1]?.icon
                : null;
        }
    }

    private readonly List<AnimalDefinition> definitions = new List<AnimalDefinition>();
    private readonly Dictionary<int, AnimalDraft> drafts = new Dictionary<int, AnimalDraft>();
    private readonly HashSet<string> expandedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<AnimalValidationIssue> selectedIssues = new List<AnimalValidationIssue>();
    private readonly List<AnimalDefinition> selectedObjectDefinitions = new List<AnimalDefinition>();
    private readonly List<ItemDefinition> dropItemDefinitions = new List<ItemDefinition>();
    private readonly Dictionary<ItemDefinition, int> dropItemOptionIndices =
        new Dictionary<ItemDefinition, int>();
    private readonly Dictionary<AnimalDefinition, string> definitionAssetPaths =
        new Dictionary<AnimalDefinition, string>();
    private readonly Dictionary<string, UnityEngine.Object> folderAssetCache =
        new Dictionary<string, UnityEngine.Object>(StringComparer.OrdinalIgnoreCase);

    private HierarchyNode hierarchyRoot = new HierarchyNode("Animals", string.Empty);
    private GUIContent[] dropItemOptions = { new GUIContent("None") };
    private GUIStyle dropItemPopupWithIconStyle;
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
    private string[] previewAnimationStateLabels = DefaultAnimationStateLabels;
    private bool previewPlaying;
    private bool projectReloadQueued;
    private double lastPreviewUpdateTime;
    private int dropItemCatalogSignature = int.MinValue;

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
            ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
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
        ItemDataEditorWindow.DefinitionCatalog.Changed +=
            HandleItemDefinitionCatalogChanged;
        ReloadDefinitions(false);
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
        EditorApplication.update -= HandleEditorUpdate;
        EditorApplication.delayCall -= HandleDelayedProjectChange;
        projectReloadQueued = false;
        ItemDataEditorWindow.DefinitionCatalog.Changed -=
            HandleItemDefinitionCatalogChanged;
        DisposePreview();
    }

    private void OnFocus()
    {
        if (RefreshDropItemOptions(false))
        {
            Repaint();
        }
    }

    private void OnProjectChange()
    {
        if (projectReloadQueued)
        {
            return;
        }

        projectReloadQueued = true;
        EditorApplication.delayCall += HandleDelayedProjectChange;
    }

    private void HandleDelayedProjectChange()
    {
        EditorApplication.delayCall -= HandleDelayedProjectChange;
        projectReloadQueued = false;
        if (this == null)
        {
            return;
        }

        ReloadDefinitions(true);
    }

    private void HandleUndoRedo()
    {
        ReloadDefinitions(true);
    }

    private void HandleItemDefinitionCatalogChanged()
    {
        if (RefreshDropItemOptions(false))
        {
            Repaint();
        }
    }

    private void HandleEditorUpdate()
    {
        if (!previewPlaying || previewAnimator == null || !previewAnimator.isInitialized)
        {
            SetPreviewPlaying(false);
            return;
        }

        double currentTime = EditorApplication.timeSinceStartup;
        float deltaTime = (float)Math.Min(0.1d, Math.Max(0d, currentTime - lastPreviewUpdateTime));
        lastPreviewUpdateTime = currentTime;

        previewAnimator.Update(deltaTime);
        Repaint();
    }

    private void SetPreviewPlaying(bool shouldPlay)
    {
        previewPlaying = shouldPlay;
        EditorApplication.update -= HandleEditorUpdate;
        if (!previewPlaying)
        {
            return;
        }

        lastPreviewUpdateTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += HandleEditorUpdate;
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

        ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
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
                ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
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
            DrawSprite(iconRect, icon);
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
        EditorGUILayout.HelpBox(
            "드롭 아이템은 성별 공통 설정입니다. 왼쪽 계층에서 동물 객체를 선택해 편집하세요.",
            MessageType.Info);
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
        UnityEngine.Object folder = GetFolderAsset(folderPath);
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
        DrawObjectAISettings();
        GUILayout.Space(8f);
        DrawObjectDropItems();
        GUILayout.Space(8f);
        DrawObjectRidingSettings();
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
            DrawSprite(iconRect, icon);
        }

        EditorGUILayout.BeginVertical();
        string variantName = GetObjectVariantName(objectNode, definition);
        string dirtyMarker = draft.dirty ? "* " : string.Empty;
        EditorGUILayout.LabelField($"{dirtyMarker}{variantName}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(GetDefinitionAssetPath(definition), EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        if (GUILayout.Button("Full Details", GUILayout.Width(90f), GUILayout.Height(24f)))
        {
            ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
            selectedObjectPath = string.Empty;
            selectedDefinitionInstanceId = definition.GetInstanceID();
            detailScroll = Vector2.zero;
            ResetPreview();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        float nextRiderHeight = Mathf.Max(
            0f,
            EditorGUILayout.FloatField(
                new GUIContent(
                    "Rider Height",
                    "이 성별 변형의 Age 10 동물 루트 기준 탑승 높이입니다. 실제 높이는 성장 배율에 맞춰 적용됩니다."),
                draft.riderHeight));
        float nextStrength = EditorGUILayout.Slider(
            new GUIContent(
                "Strength",
                "이 성별 변형의 수레 Mass 감속 저항입니다. -100은 감속 2배, 100은 감속 무시입니다."),
            draft.strength,
            AnimalDefinition.MinStrength,
            AnimalDefinition.MaxStrength);
        if (EditorGUI.EndChangeCheck())
        {
            draft.riderHeight = nextRiderHeight;
            draft.strength = nextStrength;
            draft.dirty = true;
        }

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
        float currentMaxHealth = firstDraft.maxHealth;
        bool hasMixedSettings = false;
        for (int i = 1; i < selectedObjectDefinitions.Count; i++)
        {
            AnimalDraft draft = GetDraft(selectedObjectDefinitions[i]);
            if (draft.spawnAge != currentAge
                || draft.minHerdSize != currentMinHerdSize
                || draft.maxHerdSize != currentMaxHerdSize
                || draft.spawnWeight != currentSpawnWeight
                || !Mathf.Approximately(draft.maxHealth, currentMaxHealth))
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
        float nextMaxHealth = Mathf.Max(
            1f,
            EditorGUILayout.FloatField("Max Health", currentMaxHealth));
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
                draft.maxHealth = nextMaxHealth;
                draft.dirty = true;
            }
        }

        DrawAgeDistributionGraph(nextAge);

        EditorGUILayout.HelpBox(
            "Age Weight를 중심으로 표준편차 2.0의 정규분포를 적용합니다. 나이와 무리 설정은 Female/Male에 함께 적용됩니다.",
            MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawObjectRidingSettings()
    {
        if (selectedObjectDefinitions.Count == 0)
        {
            return;
        }

        bool currentCanRiding = GetDraft(selectedObjectDefinitions[0]).canRiding;
        bool hasMixedSettings = false;
        for (int i = 1; i < selectedObjectDefinitions.Count; i++)
        {
            if (GetDraft(selectedObjectDefinitions[i]).canRiding != currentCanRiding)
            {
                hasMixedSettings = true;
                break;
            }
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Riding Settings", EditorStyles.boldLabel);
        EditorGUI.showMixedValue = hasMixedSettings;
        EditorGUI.BeginChangeCheck();
        bool nextCanRiding = EditorGUILayout.Toggle(
            new GUIContent(
                "Can Riding",
                "이 종의 Female/Male 모두에게 안장 장착과 탑승을 허용합니다."),
            currentCanRiding);
        bool settingsChanged = EditorGUI.EndChangeCheck();
        EditorGUI.showMixedValue = false;

        if (settingsChanged)
        {
            for (int i = 0; i < selectedObjectDefinitions.Count; i++)
            {
                AnimalDraft draft = GetDraft(selectedObjectDefinitions[i]);
                draft.canRiding = nextCanRiding;
                draft.dirty = true;
            }
        }

        EditorGUILayout.LabelField(
            "Female/Male 변형에 공통으로 저장됩니다.",
            EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawObjectAISettings()
    {
        if (selectedObjectDefinitions.Count == 0)
        {
            return;
        }

        AnimalDraft firstDraft = GetDraft(selectedObjectDefinitions[0]);
        firstDraft.aiSettings ??= new AnimalAISettings();
        AnimalAISettings current = firstDraft.aiSettings;
        bool hasMixedSettings = false;
        for (int i = 1; i < selectedObjectDefinitions.Count; i++)
        {
            AnimalAISettings other = GetDraft(selectedObjectDefinitions[i]).aiSettings;
            if (!AreAISettingsEqual(current, other))
            {
                hasMixedSettings = true;
                break;
            }
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Species AI Settings", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "이 객체의 Female/Male 변형에 함께 저장되는 종별 설정입니다.",
            EditorStyles.miniLabel);
        EditorGUI.showMixedValue = hasMixedSettings;
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Herd Area", EditorStyles.miniBoldLabel);
        float herdAreaRadius = Mathf.Max(
            1f,
            EditorGUILayout.FloatField(
                new GUIContent("Behavior Radius", "무리가 벗어나지 않는 행동 영역의 반경(블록)입니다."),
                current.HerdAreaRadius));
        float separationRadius = Mathf.Max(
            0.1f,
            EditorGUILayout.FloatField("Separation Radius", current.SeparationRadius));
        float separationWeight = Mathf.Max(
            0f,
            EditorGUILayout.FloatField("Separation Weight", current.SeparationWeight));
        float cohesionWeight = Mathf.Max(
            0f,
            EditorGUILayout.FloatField("Cohesion Weight", current.CohesionWeight));

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Movement", EditorStyles.miniBoldLabel);
        float moveSpeed = Mathf.Max(0f, EditorGUILayout.FloatField("Move Speed", current.MoveSpeed));
        float turnSpeed = Mathf.Max(0f, EditorGUILayout.FloatField("Turn Speed", current.TurnSpeed));
        float obstacleProbeDistance = Mathf.Max(
            0.1f,
            EditorGUILayout.FloatField("Obstacle Probe", current.ObstacleProbeDistance));
        float arrivalDistance = Mathf.Max(
            0.05f,
            EditorGUILayout.FloatField("Arrival Distance", current.ArrivalDistance));

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Threat Response", EditorStyles.miniBoldLabel);
        float fleeSafeDistance = Mathf.Max(
            1f,
            EditorGUILayout.FloatField("Safe Distance", current.FleeSafeDistance));
        float nearbyThreatRadius = Mathf.Max(
            0f,
            EditorGUILayout.FloatField("Nearby Reaction Radius", current.NearbyThreatRadius));
        float fleeSpeedMultiplier = Mathf.Max(
            0.1f,
            EditorGUILayout.FloatField("Flee Speed Multiplier", current.FleeSpeedMultiplier));

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Age / Gender Multipliers", EditorStyles.miniBoldLabel);
        float youngSpeedMultiplier =
            EditorGUILayout.Slider("Young Speed", current.YoungSpeedMultiplier, 0.1f, 2f);
        float maleSpeedMultiplier =
            EditorGUILayout.Slider("Male Speed", current.MaleSpeedMultiplier, 0.1f, 2f);
        float femaleSpeedMultiplier =
            EditorGUILayout.Slider("Female Speed", current.FemaleSpeedMultiplier, 0.1f, 2f);
        float youngWanderWeightMultiplier =
            EditorGUILayout.Slider("Young Wander Weight", current.YoungWanderWeightMultiplier, 0.1f, 3f);
        float youngRestWeightMultiplier =
            EditorGUILayout.Slider("Young Rest Weight", current.YoungRestWeightMultiplier, 0.1f, 3f);

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Behavior Weights", EditorStyles.miniBoldLabel);
        float idleWeight = Mathf.Max(0f, EditorGUILayout.FloatField("Idle", current.IdleWeight));
        float lookAroundWeight = Mathf.Max(
            0f,
            EditorGUILayout.FloatField("Look Around", current.LookAroundWeight));
        float wanderWeight = Mathf.Max(0f, EditorGUILayout.FloatField("Wander", current.WanderWeight));
        float grazeWeight = Mathf.Max(0f, EditorGUILayout.FloatField("Graze", current.GrazeWeight));
        float drinkWeight = Mathf.Max(0f, EditorGUILayout.FloatField("Drink", current.DrinkWeight));
        float restWeight = Mathf.Max(0f, EditorGUILayout.FloatField("Rest", current.RestWeight));

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Behavior Duration (Min / Max Seconds)", EditorStyles.miniBoldLabel);
        Vector2 idleDuration = EditorGUILayout.Vector2Field("Idle", current.IdleDuration);
        Vector2 lookAroundDuration =
            EditorGUILayout.Vector2Field("Look Around", current.LookAroundDuration);
        Vector2 wanderDuration = EditorGUILayout.Vector2Field("Wander", current.WanderDuration);
        Vector2 grazeDuration = EditorGUILayout.Vector2Field("Graze", current.GrazeDuration);
        Vector2 drinkDuration = EditorGUILayout.Vector2Field("Drink", current.DrinkDuration);
        Vector2 restDuration = EditorGUILayout.Vector2Field("Rest", current.RestDuration);

        bool settingsChanged = EditorGUI.EndChangeCheck();
        EditorGUI.showMixedValue = false;
        if (settingsChanged)
        {
            AnimalAISettings next = current.Clone();
            next.HerdAreaRadius = herdAreaRadius;
            next.SeparationRadius = separationRadius;
            next.SeparationWeight = separationWeight;
            next.CohesionWeight = cohesionWeight;
            next.MoveSpeed = moveSpeed;
            next.TurnSpeed = turnSpeed;
            next.ObstacleProbeDistance = obstacleProbeDistance;
            next.ArrivalDistance = arrivalDistance;
            next.FleeSafeDistance = fleeSafeDistance;
            next.NearbyThreatRadius = nearbyThreatRadius;
            next.FleeSpeedMultiplier = fleeSpeedMultiplier;
            next.YoungSpeedMultiplier = youngSpeedMultiplier;
            next.MaleSpeedMultiplier = maleSpeedMultiplier;
            next.FemaleSpeedMultiplier = femaleSpeedMultiplier;
            next.YoungWanderWeightMultiplier = youngWanderWeightMultiplier;
            next.YoungRestWeightMultiplier = youngRestWeightMultiplier;
            next.IdleWeight = idleWeight;
            next.LookAroundWeight = lookAroundWeight;
            next.WanderWeight = wanderWeight;
            next.GrazeWeight = grazeWeight;
            next.DrinkWeight = drinkWeight;
            next.RestWeight = restWeight;
            next.IdleDuration = idleDuration;
            next.LookAroundDuration = lookAroundDuration;
            next.WanderDuration = wanderDuration;
            next.GrazeDuration = grazeDuration;
            next.DrinkDuration = drinkDuration;
            next.RestDuration = restDuration;
            next.Normalize();

            for (int i = 0; i < selectedObjectDefinitions.Count; i++)
            {
                AnimalDraft draft = GetDraft(selectedObjectDefinitions[i]);
                draft.aiSettings = next.Clone();
                draft.dirty = true;
            }
        }

        EditorGUILayout.HelpBox(
            "실제 행동은 플레이어 기준 GameManager의 Animal AI Active Radius 안에서만 수행됩니다.",
            MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private static bool AreAISettingsEqual(AnimalAISettings left, AnimalAISettings right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        return string.Equals(
            JsonUtility.ToJson(left),
            JsonUtility.ToJson(right),
            StringComparison.Ordinal);
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
            DrawSprite(iconRect, draft.adultIcon);
        }

        EditorGUILayout.BeginVertical();
        string displayName = string.IsNullOrWhiteSpace(draft.animalName) ? definition.name : draft.animalName;
        EditorGUILayout.LabelField($"[{draft.id}] {displayName}", EditorStyles.largeLabel);
        EditorGUILayout.LabelField(AnimalDataEditorUtility.GetHierarchyPath(definition), EditorStyles.miniLabel);
        EditorGUILayout.LabelField(GetDefinitionAssetPath(definition), EditorStyles.miniLabel);
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
        float nextRiderHeight = Mathf.Max(
            0f,
            EditorGUILayout.FloatField(
                new GUIContent(
                    "Rider Height",
                    "Age 10 동물 루트 기준 탑승 높이입니다. 실제 높이는 성장 배율에 맞춰 적용됩니다."),
                draft.riderHeight));
        float nextStrength = EditorGUILayout.Slider(
            new GUIContent(
                "Strength",
                "수레 Mass 감속 효과를 줄이는 비율입니다. -100은 감속 2배, 100은 감속 무시입니다."),
            draft.strength,
            AnimalDefinition.MinStrength,
            AnimalDefinition.MaxStrength);
        if (EditorGUI.EndChangeCheck())
        {
            bool prefabChanged = nextPrefab != draft.prefab;
            draft.id = Mathf.Max(-1, nextId);
            draft.animalName = nextName;
            draft.prefab = nextPrefab;
            draft.adultIcon = nextAdultIcon;
            draft.childIcon = nextChildIcon;
            draft.riderHeight = nextRiderHeight;
            draft.strength = nextStrength;
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

    private void DrawObjectDropItems()
    {
        if (selectedObjectDefinitions.Count == 0)
        {
            return;
        }

        AnimalDraft firstDraft = GetDraft(selectedObjectDefinitions[0]);
        firstDraft.dropItems ??= new List<AnimalDropEntry>();
        bool hasMixedDropItems = false;
        for (int i = 1; i < selectedObjectDefinitions.Count; i++)
        {
            AnimalDraft otherDraft = GetDraft(selectedObjectDefinitions[i]);
            if (!AreDropItemsEqual(firstDraft.dropItems, otherDraft.dropItems))
            {
                hasMixedDropItems = true;
                break;
            }
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Gender Common Drop Items", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "이 객체의 Female/Male 변형에 동일하게 저장됩니다.",
            EditorStyles.miniLabel);
        if (hasMixedDropItems)
        {
            EditorGUILayout.HelpBox(
                "현재 성별 자산의 드롭 설정이 서로 다릅니다. 아래 값을 편집하거나 동기화하면 첫 번째 변형의 값으로 통일됩니다.",
                MessageType.Warning);
        }

        int removeIndex = -1;
        bool changed = false;
        for (int i = 0; i < firstDraft.dropItems.Count; i++)
        {
            AnimalDropEntry entry =
                firstDraft.dropItems[i] ??= new AnimalDropEntry();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Entry {i + 1}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("Remove", GUILayout.Width(65f)))
            {
                removeIndex = i;
            }

            EditorGUILayout.EndHorizontal();

            DrawDropItemPopup(i, entry);

            EditorGUI.BeginChangeCheck();
            int minAmount = Mathf.Max(
                0,
                EditorGUILayout.IntField("Minimum Amount", entry.MinAmount));
            int maxAmount = Mathf.Max(
                minAmount,
                EditorGUILayout.IntField("Maximum Amount", entry.MaxAmount));
            float chancePercent = EditorGUILayout.Slider(
                "Drop Chance (%)",
                entry.DropChance * 100f,
                0f,
                100f);
            if (EditorGUI.EndChangeCheck())
            {
                entry.MinAmount = minAmount;
                entry.MaxAmount = maxAmount;
                entry.DropChance = chancePercent * 0.01f;
                changed = true;
            }

            if (entry.ItemDefinition != null)
            {
                EditorGUILayout.LabelField(
                    $"Item ID {entry.ItemDefinition.id} · {entry.ItemDefinition.itemName}",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
        {
            firstDraft.dropItems.RemoveAt(removeIndex);
            changed = true;
        }

        if (GUILayout.Button("Add Drop Item"))
        {
            firstDraft.dropItems.Add(new AnimalDropEntry());
            changed = true;
        }

        if (hasMixedDropItems
            && GUILayout.Button("Synchronize Female / Male"))
        {
            changed = true;
        }

        if (changed)
        {
            SynchronizeCommonDropItems(firstDraft);
        }

        EditorGUILayout.HelpBox(
            "동물 시체를 채집하면 이 목록 순서대로 아이템이 지급됩니다.",
            MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawDropItemPopup(int entryIndex, AnimalDropEntry entry)
    {
        Rect rowRect = EditorGUILayout.GetControlRect();
        Rect popupRect = EditorGUI.PrefixLabel(rowRect, new GUIContent("Item"));
        int currentOptionIndex = FindDropItemIndex(entry.ItemDefinition);
        GUIContent selectedContent = currentOptionIndex >= 0
            && currentOptionIndex < dropItemOptions.Length
            ? dropItemOptions[currentOptionIndex]
            : dropItemOptions[0];
        Sprite selectedIcon = currentOptionIndex > 0
            && currentOptionIndex <= dropItemDefinitions.Count
            ? dropItemDefinitions[currentOptionIndex - 1]?.icon
            : null;

        bool hasIcon = selectedIcon != null;
        GUIStyle popupStyle = hasIcon
            ? GetDropItemPopupWithIconStyle()
            : EditorStyles.popup;
        bool openDropdown = EditorGUI.DropdownButton(
                popupRect,
                new GUIContent(selectedContent.text),
                FocusType.Keyboard,
                popupStyle);
        if (hasIcon)
        {
            const float iconSize = 16f;
            Rect iconRect = new Rect(
                popupRect.x + 3f,
                popupRect.y + (popupRect.height - iconSize) * 0.5f,
                iconSize,
                iconSize);
            DrawSprite(iconRect, selectedIcon);
        }

        if (openDropdown)
        {
            PopupWindow.Show(
                popupRect,
                new DropItemPopupContent(
                    dropItemOptions,
                    dropItemDefinitions,
                    currentOptionIndex,
                    optionIndex => ApplyDropItemSelection(entryIndex, optionIndex)));
        }
    }

    private GUIStyle GetDropItemPopupWithIconStyle()
    {
        if (dropItemPopupWithIconStyle == null)
        {
            dropItemPopupWithIconStyle = new GUIStyle(EditorStyles.popup);
            dropItemPopupWithIconStyle.padding.left = 23;
        }

        return dropItemPopupWithIconStyle;
    }

    private void ApplyDropItemSelection(int entryIndex, int optionIndex)
    {
        if (selectedObjectDefinitions.Count == 0)
        {
            return;
        }

        AnimalDraft firstDraft = GetDraft(selectedObjectDefinitions[0]);
        firstDraft.dropItems ??= new List<AnimalDropEntry>();
        if (entryIndex < 0 || entryIndex >= firstDraft.dropItems.Count)
        {
            return;
        }

        ItemDefinition selectedDefinition = optionIndex > 0
            && optionIndex <= dropItemDefinitions.Count
            ? dropItemDefinitions[optionIndex - 1]
            : null;
        AnimalDropEntry entry =
            firstDraft.dropItems[entryIndex] ??= new AnimalDropEntry();
        if (entry.ItemDefinition == selectedDefinition)
        {
            return;
        }

        entry.ItemDefinition = selectedDefinition;
        SynchronizeCommonDropItems(firstDraft);
        Repaint();
    }

    private void SynchronizeCommonDropItems(AnimalDraft sourceDraft)
    {
        List<AnimalDropEntry> commonDropItems =
            AnimalDropEntry.CloneList(sourceDraft.dropItems);
        for (int i = 0; i < selectedObjectDefinitions.Count; i++)
        {
            AnimalDraft draft = GetDraft(selectedObjectDefinitions[i]);
            draft.dropItems = AnimalDropEntry.CloneList(commonDropItems);
            draft.dirty = true;
        }
    }

    private static bool AreDropItemsEqual(
        IReadOnlyList<AnimalDropEntry> left,
        IReadOnlyList<AnimalDropEntry> right)
    {
        int leftCount = left != null ? left.Count : 0;
        int rightCount = right != null ? right.Count : 0;
        if (leftCount != rightCount)
        {
            return false;
        }

        for (int i = 0; i < leftCount; i++)
        {
            AnimalDropEntry leftEntry = left[i];
            AnimalDropEntry rightEntry = right[i];
            if (ReferenceEquals(leftEntry, rightEntry))
            {
                continue;
            }

            if (leftEntry == null
                || rightEntry == null
                || leftEntry.ItemDefinition != rightEntry.ItemDefinition
                || leftEntry.MinAmount != rightEntry.MinAmount
                || leftEntry.MaxAmount != rightEntry.MaxAmount
                || !Mathf.Approximately(
                    leftEntry.DropChance,
                    rightEntry.DropChance))
            {
                return false;
            }
        }

        return true;
    }

    private bool RefreshDropItemOptions(bool force = true)
    {
        List<ItemDefinition> latestDefinitions = LoadAllItemDefinitions();
        int latestSignature =
            ItemDataEditorWindow.DefinitionCatalog.ComputeSignature(
                latestDefinitions);
        if (!force && latestSignature == dropItemCatalogSignature)
        {
            return false;
        }

        dropItemCatalogSignature = latestSignature;
        dropItemDefinitions.Clear();
        dropItemDefinitions.AddRange(latestDefinitions);
        dropItemOptionIndices.Clear();

        dropItemOptions = new GUIContent[dropItemDefinitions.Count + 1];
        dropItemOptions[0] = new GUIContent("None");
        for (int i = 0; i < dropItemDefinitions.Count; i++)
        {
            ItemDefinition definition = dropItemDefinitions[i];
            string itemName = !string.IsNullOrWhiteSpace(definition.itemName)
                ? definition.itemName
                : definition.name;
            dropItemOptions[i + 1] = new GUIContent(
                $"[{definition.id}] {itemName}");
            dropItemOptionIndices[definition] = i + 1;
        }

        return true;
    }

    private static void DrawSprite(Rect targetRect, Sprite sprite)
    {
        if (sprite == null || sprite.texture == null)
        {
            return;
        }

        Rect textureRect = sprite.textureRect;
        Texture2D texture = sprite.texture;
        Rect textureCoordinates = new Rect(
            textureRect.x / texture.width,
            textureRect.y / texture.height,
            textureRect.width / texture.width,
            textureRect.height / texture.height);
        GUI.DrawTextureWithTexCoords(
            targetRect,
            texture,
            textureCoordinates,
            true);
    }

    private int FindDropItemIndex(ItemDefinition definition)
    {
        return definition != null
            && dropItemOptionIndices.TryGetValue(definition, out int optionIndex)
                ? optionIndex
                : 0;
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
        int nextAnimationIndex = EditorGUILayout.Popup(
            "Animation",
            previewAnimationIndex,
            previewAnimationStateLabels);
        if (nextAnimationIndex != previewAnimationIndex)
        {
            previewAnimationIndex = nextAnimationIndex;
            ApplyPreviewAnimation();
        }

        bool nextPlaying = GUILayout.Toggle(previewPlaying, previewPlaying ? "Pause" : "Play", "Button", GUILayout.Width(70f));
        if (nextPlaying != previewPlaying)
        {
            SetPreviewPlaying(nextPlaying);
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
            RefreshPreviewAnimationStateLabels();
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

    private void RefreshPreviewAnimationStateLabels()
    {
        previewAnimationStateLabels = (string[])DefaultAnimationStateLabels.Clone();
        AnimatorController controller = ResolveAnimatorController(
            previewAnimator != null ? previewAnimator.runtimeAnimatorController : null);
        if (controller == null || controller.layers.Length == 0)
        {
            return;
        }

        Dictionary<int, string> stateNames = new Dictionary<int, string>();
        CollectAnimationStateNames(controller.layers[0].stateMachine, stateNames);
        for (int i = 0; i < AnimationStates.Length; i++)
        {
            if (stateNames.TryGetValue(AnimationStates[i], out string stateName)
                && !string.IsNullOrWhiteSpace(stateName))
            {
                previewAnimationStateLabels[i] = GetAnimationStateDisplayName(stateName);
            }
        }
    }

    private static string GetAnimationStateDisplayName(string stateName)
    {
        switch (stateName)
        {
            case "Galop":
                return "Gallop";
            case "Sweem":
                return "Swim";
            case "IdlleToLay":
                return "Rest (Sleep)";
            default:
                return ObjectNames.NicifyVariableName(stateName);
        }
    }

    private static AnimatorController ResolveAnimatorController(RuntimeAnimatorController runtimeController)
    {
        while (runtimeController is AnimatorOverrideController overrideController)
        {
            runtimeController = overrideController.runtimeAnimatorController;
        }

        return runtimeController as AnimatorController;
    }

    private static void CollectAnimationStateNames(
        AnimatorStateMachine stateMachine,
        Dictionary<int, string> stateNames)
    {
        if (stateMachine == null)
        {
            return;
        }

        if (stateMachine.defaultState != null)
        {
            stateNames[0] = stateMachine.defaultState.name;
        }

        ChildAnimatorState[] childStates = stateMachine.states;
        for (int i = 0; i < childStates.Length; i++)
        {
            AnimatorState state = childStates[i].state;
            if (state == null)
            {
                continue;
            }

            AnimatorStateTransition[] transitions = state.transitions;
            for (int transitionIndex = 0; transitionIndex < transitions.Length; transitionIndex++)
            {
                AnimatorStateTransition transition = transitions[transitionIndex];
                if (transition == null || transition.destinationState == null)
                {
                    continue;
                }

                AnimatorCondition[] conditions = transition.conditions;
                for (int conditionIndex = 0; conditionIndex < conditions.Length; conditionIndex++)
                {
                    AnimatorCondition condition = conditions[conditionIndex];
                    if (condition.parameter == "State"
                        && condition.mode == AnimatorConditionMode.Equals)
                    {
                        stateNames[Mathf.RoundToInt(condition.threshold)] = transition.destinationState.name;
                    }
                }
            }
        }

        ChildAnimatorStateMachine[] childStateMachines = stateMachine.stateMachines;
        for (int i = 0; i < childStateMachines.Length; i++)
        {
            CollectAnimationStateNames(childStateMachines[i].stateMachine, stateNames);
        }
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
                draft.maxHealth,
                draft.canRiding,
                draft.riderHeight,
                draft.strength,
                draft.aiSettings,
                draft.dropItems,
                "Save Animal Data");
            draft.dirty = false;
            savedCount++;
        }

        AssetDatabase.SaveAssets();
        WriteJson(AnimalDataEditorUtility.DefaultJsonPath, definitions);
        ImportWrittenAsset(AnimalDataEditorUtility.DefaultJsonPath);
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
        RefreshDropItemOptions();
        drafts.Clear();
        definitionAssetPaths.Clear();
        folderAssetCache.Clear();
        for (int i = 0; i < definitions.Count; i++)
        {
            AnimalDefinition definition = definitions[i];
            string path = AssetDatabase.GetAssetPath(definition);
            definitionAssetPaths[definition] = path;
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
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { AnimalDataEditorUtility.PrefabRoot });
        string[] prefabPaths = new string[prefabGuids.Length];
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            prefabPaths[i] = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
        }

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
                GameObject prefab = FindBestPrefab(pair.Key, baseName, prefabPaths);
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
                        AnimalDefinition.DefaultMaxHealth,
                        true,
                        AnimalDefinition.DefaultRiderHeight,
                        AnimalDefinition.DefaultStrength,
                        new AnimalAISettings(),
                        new List<AnimalDropEntry>(),
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
                    definition.MaxHealth,
                    definition.CanBeRidden,
                    definition.RiderHeight,
                    definition.Strength,
                    definition.AISettings,
                    definition.DropItems,
                    "Rebuild Animal Definition");
                filledCount++;
            }
        }

        AssetDatabase.SaveAssets();
        ReloadDefinitions(false);
        WriteJson(AnimalDataEditorUtility.DefaultJsonPath, definitions);
        ImportWrittenAsset(AnimalDataEditorUtility.DefaultJsonPath);
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
        ImportWrittenAsset(absolutePath);
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

        List<ItemDefinition> itemDefinitions = file.version >= 8
            ? LoadAllItemDefinitions()
            : null;
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
            draft.maxHealth = file.version >= 5
                ? Mathf.Max(1f, entry.maxHealth)
                : AnimalDefinition.DefaultMaxHealth;
            draft.canRiding = file.version >= 10
                ? entry.canRiding
                : true;
            draft.riderHeight = file.version >= 9
                ? Mathf.Max(0f, entry.riderHeight)
                : AnimalDefinition.DefaultRiderHeight;
            draft.strength = file.version >= 11
                ? Mathf.Clamp(
                    entry.strength,
                    AnimalDefinition.MinStrength,
                    AnimalDefinition.MaxStrength)
                : AnimalDefinition.DefaultStrength;
            if (file.version >= 4 && entry.aiSettings != null)
            {
                draft.aiSettings = entry.aiSettings.Clone();
                if (file.version < 6)
                {
                    draft.aiSettings.LookAroundWeight =
                        AnimalAISettings.DefaultLookAroundWeight;
                    draft.aiSettings.LookAroundDuration =
                        AnimalAISettings.DefaultLookAroundDuration;
                }

                if (file.version < 7)
                {
                    draft.aiSettings.FleeSafeDistance =
                        AnimalAISettings.DefaultFleeSafeDistance;
                    draft.aiSettings.NearbyThreatRadius =
                        AnimalAISettings.DefaultNearbyThreatRadius;
                    draft.aiSettings.FleeSpeedMultiplier =
                        AnimalAISettings.DefaultFleeSpeedMultiplier;
                }

                draft.aiSettings.Normalize();
            }
            if (file.version >= 8)
            {
                draft.dropItems = ImportDropItems(
                    entry.dropItems,
                    itemDefinitions);
            }

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
                maxHealth = definition.MaxHealth,
                canRiding = definition.CanBeRidden,
                riderHeight = definition.RiderHeight,
                strength = definition.Strength,
                aiSettings = definition.AISettings != null
                    ? definition.AISettings.Clone()
                    : new AnimalAISettings(),
                dropItems = ExportDropItems(definition.DropItems),
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

    private static void ImportWrittenAsset(string path)
    {
        string absolutePath = ToAbsolutePath(path);
        string assetsPath = Path.GetFullPath(Application.dataPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string assetsPrefix = assetsPath + Path.DirectorySeparatorChar;
        if (!absolutePath.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string relativePath = "Assets/" + absolutePath.Substring(assetsPrefix.Length)
            .Replace('\\', '/');
        AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
    }

    private static List<AnimalDropJsonEntry> ExportDropItems(
        IReadOnlyList<AnimalDropEntry> source)
    {
        List<AnimalDropJsonEntry> result = new List<AnimalDropJsonEntry>(
            source != null ? source.Count : 0);
        for (int i = 0; source != null && i < source.Count; i++)
        {
            AnimalDropEntry entry = source[i] ?? new AnimalDropEntry();
            ItemDefinition itemDefinition = entry.ItemDefinition;
            result.Add(new AnimalDropJsonEntry
            {
                itemId = itemDefinition != null ? itemDefinition.id : -1,
                itemName = itemDefinition != null
                    ? itemDefinition.itemName ?? string.Empty
                    : string.Empty,
                itemAssetPath = AssetDatabase.GetAssetPath(itemDefinition),
                minAmount = entry.MinAmount,
                maxAmount = entry.MaxAmount,
                dropChance = entry.DropChance
            });
        }

        return result;
    }

    private static List<AnimalDropEntry> ImportDropItems(
        IReadOnlyList<AnimalDropJsonEntry> source,
        IReadOnlyList<ItemDefinition> itemDefinitions)
    {
        List<AnimalDropEntry> result = new List<AnimalDropEntry>(
            source != null ? source.Count : 0);
        for (int i = 0; source != null && i < source.Count; i++)
        {
            AnimalDropJsonEntry jsonEntry = source[i];
            if (jsonEntry == null)
            {
                result.Add(new AnimalDropEntry());
                continue;
            }

            ItemDefinition itemDefinition =
                AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    jsonEntry.itemAssetPath);
            if (itemDefinition == null)
            {
                itemDefinition = ResolveDropItemDefinition(
                    itemDefinitions,
                    jsonEntry.itemId,
                    jsonEntry.itemName);
            }

            AnimalDropEntry entry = new AnimalDropEntry
            {
                ItemDefinition = itemDefinition,
                MinAmount = jsonEntry.minAmount,
                MaxAmount = jsonEntry.maxAmount,
                DropChance = jsonEntry.dropChance
            };
            result.Add(entry);
        }

        return result;
    }

    private static List<ItemDefinition> LoadAllItemDefinitions()
    {
        return ItemDataEditorWindow.DefinitionCatalog.LoadCurrent();
    }

    private static ItemDefinition ResolveDropItemDefinition(
        IReadOnlyList<ItemDefinition> definitions,
        int itemId,
        string itemName)
    {
        ItemDefinition idMatch =
            ItemDefinitionLookup.ResolveById(definitions, itemId);
        if (idMatch != null)
        {
            return idMatch;
        }

        return ItemDefinitionLookup.ResolveByStableName(
            definitions,
            itemName);
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
        SetPreviewPlaying(false);
        DisposePreview();
        previewPrefab = null;
        previewAge = 10f;
        previewAnimationIndex = 0;
    }

    private void DisposePreview()
    {
        previewInstance = null;
        previewAnimal = null;
        previewAnimator = null;
        previewAnimationStateLabels = DefaultAnimationStateLabels;
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

    private static GameObject FindBestPrefab(
        string definitionFolder,
        string baseName,
        IReadOnlyList<string> prefabPaths)
    {
        string bestPrefabPath = null;
        int bestScore = int.MinValue;
        string relativeFolder = definitionFolder.StartsWith(AnimalDataEditorUtility.DefinitionRoot + "/", StringComparison.OrdinalIgnoreCase)
            ? definitionFolder.Substring(AnimalDataEditorUtility.DefinitionRoot.Length + 1)
            : definitionFolder;
        string[] hierarchySegments = relativeFolder.Split('/');
        string[] nameTokens = baseName.Split('_');

        for (int i = 0; prefabPaths != null && i < prefabPaths.Count; i++)
        {
            string prefabPath = prefabPaths[i];
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

            bestPrefabPath = prefabPath;
            bestScore = score;
        }

        return bestScore > 0 && !string.IsNullOrEmpty(bestPrefabPath)
            ? AssetDatabase.LoadAssetAtPath<GameObject>(bestPrefabPath)
            : null;
    }

    private string GetDefinitionAssetPath(AnimalDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        if (!definitionAssetPaths.TryGetValue(definition, out string assetPath))
        {
            assetPath = AssetDatabase.GetAssetPath(definition);
            definitionAssetPaths[definition] = assetPath;
        }

        return assetPath;
    }

    private UnityEngine.Object GetFolderAsset(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            return null;
        }

        if (!folderAssetCache.TryGetValue(folderPath, out UnityEngine.Object folder))
        {
            folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
            folderAssetCache[folderPath] = folder;
        }

        return folder;
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
