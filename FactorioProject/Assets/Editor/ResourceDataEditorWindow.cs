using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class ResourceDataEditorWindow : EditorWindow
{
    private const string AssetFolder = "Assets/Data/MapObject";
    private const float SidebarWidth = 270f;
    private const float ListRowHeight = 32f;
    private const float ListIconSize = 24f;

    private static readonly string[] CategoryFilterLabels =
    {
        "All",
        "Ore",
        "Oil",
        "Tree"
    };

    private readonly List<ResourceDefinition> definitions = new List<ResourceDefinition>();
    private readonly List<ResourceDefinition> visibleDefinitions = new List<ResourceDefinition>();
    private readonly ItemDefinitionDropdownGUI dropItemDropdown =
        new ItemDefinitionDropdownGUI();
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private string searchText = string.Empty;
    private string cachedSearchText = string.Empty;
    private int categoryFilter = -1;
    private int cachedCategoryFilter = int.MinValue;
    private ResourceDefinition selectedDefinition;
    private SerializedObject selectedDefinitionSerializedObject;
    private Resource cachedPrefab;
    private SerializedObject cachedPrefabSerializedObject;
    private bool catalogDirty = true;
    private bool visibleDefinitionsDirty = true;
    private string pendingSelectionPath;
    private static GUIStyle centeredMiniLabelStyle;

    [MenuItem("Window/ProjectF/Resource Data")]
    public static void ShowWindow()
    {
        ResourceDataEditorWindow window = GetWindow<ResourceDataEditorWindow>("Resource Data");
        window.minSize = new Vector2(760f, 480f);
        window.Show();
    }

    public static void ShowWindowAndSelect(ResourceDefinition definition)
    {
        ResourceDataEditorWindow window = GetWindow<ResourceDataEditorWindow>("Resource Data");
        window.minSize = new Vector2(760f, 480f);
        window.pendingSelectionPath = definition != null
            ? AssetDatabase.GetAssetPath(definition)
            : null;
        window.catalogDirty = true;
        window.Show();
        window.EnsureCatalog();
        window.Repaint();
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += HandleUndoRedo;
        ItemDataEditorWindow.DefinitionCatalog.Changed +=
            HandleItemDefinitionCatalogChanged;
        dropItemDropdown.Refresh();
        catalogDirty = true;
        EnsureCatalog();
        AdoptProjectSelection();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
        ItemDataEditorWindow.DefinitionCatalog.Changed -=
            HandleItemDefinitionCatalogChanged;
        CommitCurrentEdits();
        ClearSerializedObjectCaches();
    }

    private void OnFocus()
    {
        dropItemDropdown.Refresh(false);
        AdoptProjectSelection();
        Repaint();
    }

    private void OnSelectionChange()
    {
        AdoptProjectSelection();
        Repaint();
    }

    private void OnProjectChange()
    {
        pendingSelectionPath = GetSelectedDefinitionPath();
        dropItemDropdown.Refresh(false);
        catalogDirty = true;
        visibleDefinitionsDirty = true;
        Repaint();
    }

    private void HandleUndoRedo()
    {
        ClearSerializedObjectCaches();
        catalogDirty = true;
        visibleDefinitionsDirty = true;
        Repaint();
    }

    private void HandleItemDefinitionCatalogChanged()
    {
        if (dropItemDropdown.Refresh(false))
        {
            Repaint();
        }
    }

    private void OnGUI()
    {
        EnsureCatalog();
        DrawBackground();
        DrawSidebar();
        DrawDetailPanel();
    }

    private void DrawBackground()
    {
        EditorGUI.DrawRect(
            new Rect(0f, 0f, position.width, position.height),
            new Color(0.15f, 0.15f, 0.15f));
    }

    private void DrawSidebar()
    {
        Rect sidebarRect = new Rect(0f, 0f, SidebarWidth, position.height);
        EditorGUI.DrawRect(sidebarRect, new Color(0.12f, 0.12f, 0.12f));

        GUILayout.BeginArea(sidebarRect);
        GUILayout.Space(8f);
        DrawSidebarToolbar();
        DrawSearchAndCategoryFilter();
        DrawResourceList();
        GUILayout.EndArea();
    }

    private void DrawSidebarToolbar()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"Resources ({visibleDefinitions.Count})", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Save", EditorStyles.miniButtonLeft, GUILayout.Width(44f)))
        {
            SaveCurrentAssets();
        }

        if (GUILayout.Button("New", EditorStyles.miniButtonMid, GUILayout.Width(42f)))
        {
            CreateDefinition();
            GUIUtility.ExitGUI();
        }

        using (new EditorGUI.DisabledScope(selectedDefinition == null))
        {
            if (GUILayout.Button("Copy", EditorStyles.miniButtonMid, GUILayout.Width(42f)))
            {
                DuplicateSelectedDefinition();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("-", EditorStyles.miniButtonRight, GUILayout.Width(24f)))
            {
                DeleteSelectedDefinition();
                GUIUtility.ExitGUI();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSearchAndCategoryFilter()
    {
        EditorGUI.BeginChangeCheck();
        searchText = EditorGUILayout.TextField("Search", searchText ?? string.Empty);
        int nextFilterIndex = EditorGUILayout.Popup(
            "Category",
            Mathf.Clamp(categoryFilter + 1, 0, CategoryFilterLabels.Length - 1),
            CategoryFilterLabels);
        if (EditorGUI.EndChangeCheck())
        {
            categoryFilter = nextFilterIndex - 1;
            visibleDefinitionsDirty = true;
        }

        EnsureVisibleDefinitions();
    }

    private void DrawResourceList()
    {
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        if (visibleDefinitions.Count == 0)
        {
            EditorGUILayout.HelpBox("검색 결과가 없습니다.", MessageType.Info);
        }

        for (int i = 0; i < visibleDefinitions.Count; i++)
        {
            DrawDefinitionRow(visibleDefinitions[i]);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawDefinitionRow(ResourceDefinition definition)
    {
        if (definition == null)
        {
            return;
        }

        Rect rowRect = GUILayoutUtility.GetRect(
            1f,
            ListRowHeight,
            GUILayout.ExpandWidth(true));
        bool selected = definition == selectedDefinition;
        bool hovered = rowRect.Contains(Event.current.mousePosition);
        if (selected || hovered)
        {
            EditorGUI.DrawRect(
                rowRect,
                selected
                    ? new Color(0.22f, 0.46f, 0.72f, 0.8f)
                    : new Color(1f, 1f, 1f, 0.06f));
        }

        Rect iconRect = new Rect(
            rowRect.x + 4f,
            rowRect.y + (ListRowHeight - ListIconSize) * 0.5f,
            ListIconSize,
            ListIconSize);
        DrawResourceIcon(iconRect, definition);

        string displayName = GetDisplayName(definition);
        Rect nameRect = new Rect(
            iconRect.xMax + 6f,
            rowRect.y + 2f,
            rowRect.width - iconRect.width - 68f,
            17f);
        GUI.Label(nameRect, displayName, EditorStyles.label);

        Rect assetRect = new Rect(
            nameRect.x,
            rowRect.y + 17f,
            nameRect.width,
            13f);
        GUI.Label(assetRect, definition.name, EditorStyles.miniLabel);

        Rect categoryRect = new Rect(
            rowRect.xMax - 48f,
            rowRect.y + 7f,
            44f,
            18f);
        EditorGUI.DrawRect(categoryRect, GetCategoryColor(definition.placementCategory));
        GUI.Label(categoryRect, definition.placementCategory.ToString(), GetCenteredMiniLabelStyle());

        if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
        {
            SelectDefinition(definition);
        }
    }

    private void DrawDetailPanel()
    {
        Rect detailRect = new Rect(
            SidebarWidth + 1f,
            0f,
            Mathf.Max(0f, position.width - SidebarWidth - 1f),
            position.height);
        GUILayout.BeginArea(detailRect);
        GUILayout.Space(10f);

        if (selectedDefinition == null)
        {
            EditorGUILayout.HelpBox("왼쪽에서 ResourceDefinition을 선택하세요.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        DrawDetailHeader();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        DrawDefinitionSection();
        DrawValidationSection();
        DrawPrefabSection();
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawDetailHeader()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(GetDisplayName(selectedDefinition), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(GetSelectedDefinitionPath(), EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        if (GUILayout.Button("Select Asset", GUILayout.Width(88f), GUILayout.Height(24f)))
        {
            CommitCurrentEdits();
            Selection.activeObject = selectedDefinition;
            EditorGUIUtility.PingObject(selectedDefinition);
        }

        EditorGUILayout.EndHorizontal();
        GUILayout.Space(8f);
    }

    private void DrawDefinitionSection()
    {
        SerializedObject serializedDefinition = GetSelectedDefinitionSerializedObject();
        if (serializedDefinition == null)
        {
            return;
        }

        Resource previousPrefab = selectedDefinition.prefab;
        serializedDefinition.UpdateIfRequiredOrScript();

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Resource Definition", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();

        DrawProperty(serializedDefinition, "resourceName", "Resource Name");
        DrawProperty(serializedDefinition, "resourceIcon", "Resource Icon");
        DrawProperty(serializedDefinition, "prefab", "Resource Prefab");
        DrawProperty(serializedDefinition, "harvestMode", "Harvest Mode");
        DrawProperty(serializedDefinition, "placementCategory", "Placement Category");

        SerializedProperty placementCategory = serializedDefinition.FindProperty(
            "placementCategory");
        bool isPlantResource = placementCategory != null
                               && placementCategory.enumValueIndex
                               == (int)ResourceDefinition.PlacementCategory.Tree;
        if (isPlantResource)
        {
            GUILayout.Space(8f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Plant Growth Settings", EditorStyles.boldLabel);
            SerializedProperty minimumGrowth = serializedDefinition.FindProperty("minimumGrowth");
            SerializedProperty maximumGrowth = serializedDefinition.FindProperty("maximumGrowth");
            EditorGUILayout.PropertyField(
                minimumGrowth,
                new GUIContent("Minimum Spawn Growth"));
            EditorGUILayout.PropertyField(
                maximumGrowth,
                new GUIContent("Maximum Spawn Growth"));
            minimumGrowth.intValue = Mathf.Clamp(
                minimumGrowth.intValue,
                ResourceDefinition.MinGrowth,
                ResourceDefinition.MaxGrowth);
            maximumGrowth.intValue = Mathf.Clamp(
                maximumGrowth.intValue,
                minimumGrowth.intValue,
                ResourceDefinition.MaxGrowth);

            GUILayout.Space(4f);
            DrawPlantGrowthRequirements(serializedDefinition);
            EditorGUILayout.EndVertical();
        }

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("Default Harvest State", EditorStyles.miniBoldLabel);
        SerializedProperty resourceCount = serializedDefinition.FindProperty("defaultResourceCount");
        SerializedProperty getCount = serializedDefinition.FindProperty("defaultGetCount");
        SerializedProperty maxGauge = serializedDefinition.FindProperty("defaultMaxGauge");
        SerializedProperty currentGauge = serializedDefinition.FindProperty("defaultCurrentGauge");
        EditorGUILayout.PropertyField(resourceCount, new GUIContent("Resource Count"));
        EditorGUILayout.PropertyField(getCount, new GUIContent("Output Count"));
        EditorGUILayout.PropertyField(maxGauge, new GUIContent("Max Gauge"));
        EditorGUILayout.PropertyField(currentGauge, new GUIContent("Current Gauge"));

        resourceCount.intValue = Mathf.Max(1, resourceCount.intValue);
        getCount.intValue = Mathf.Max(1, getCount.intValue);
        maxGauge.intValue = Mathf.Max(1, maxGauge.intValue);
        currentGauge.intValue = Mathf.Clamp(currentGauge.intValue, 0, maxGauge.intValue);

        GUILayout.Space(8f);
        DrawFarmingDropItemsSection(serializedDefinition);

        bool changed = EditorGUI.EndChangeCheck();
        serializedDefinition.ApplyModifiedProperties();
        if (changed)
        {
            EditorUtility.SetDirty(selectedDefinition);
            visibleDefinitionsDirty = true;
            if (previousPrefab != selectedDefinition.prefab)
            {
                ClearPrefabSerializedObjectCache();
            }
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(6f);
    }

    private static void DrawPlantGrowthRequirements(SerializedObject serializedDefinition)
    {
        SerializedProperty totalWater = serializedDefinition.FindProperty(
            "totalGrowthWaterLiters");
        SerializedProperty totalFertilizer = serializedDefinition.FindProperty(
            "totalGrowthFertilizerAmount");
        SerializedProperty durationPerLevel = serializedDefinition.FindProperty(
            "growthDurationPerLevelSeconds");

        EditorGUILayout.LabelField("Growth Requirements", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(totalWater, new GUIContent("Total Water (L)"));
        EditorGUILayout.PropertyField(totalFertilizer, new GUIContent("Total Fertilizer"));
        EditorGUILayout.PropertyField(
            durationPerLevel,
            new GUIContent("Growth Time per Level (sec)"));

        totalWater.floatValue = Mathf.Max(0f, totalWater.floatValue);
        totalFertilizer.floatValue = Mathf.Max(0f, totalFertilizer.floatValue);
        durationPerLevel.floatValue = Mathf.Max(0f, durationPerLevel.floatValue);

        EditorGUILayout.HelpBox(
            $"Each Growth level takes {durationPerLevel.floatValue:0.###} seconds after its requirements are met.\n"
            + "Water and fertilizer are distributed automatically using increasing 1:2:...:10 weights. "
            + "Fertilizer input is not implemented yet; only its data and growth requirement are active.",
            MessageType.Info);
        if (durationPerLevel.floatValue <= 0f
            && (totalWater.floatValue > 0f || totalFertilizer.floatValue > 0f))
        {
            EditorGUILayout.HelpBox(
                "A plant cannot grow while Growth Time per Level is 0.",
                MessageType.Warning);
        }

        EditorGUI.indentLevel++;
        for (int targetGrowth = ResourceDefinition.MinGrowth + 1;
             targetGrowth <= ResourceDefinition.MaxGrowth;
             targetGrowth++)
        {
            float water = ResourceDefinition.CalculateGrowthRequirement(
                totalWater.floatValue,
                targetGrowth);
            float fertilizer = ResourceDefinition.CalculateGrowthRequirement(
                totalFertilizer.floatValue,
                targetGrowth);
            EditorGUILayout.LabelField(
                $"Growth {targetGrowth - 1} → {targetGrowth}",
                $"Water {water:0.###} L / Fertilizer {fertilizer:0.###}");
        }

        EditorGUI.indentLevel--;
    }

    private void DrawFarmingDropItemsSection(SerializedObject serializedDefinition)
    {
        SerializedProperty dropItems = serializedDefinition.FindProperty("dropItems");
        if (dropItems == null)
        {
            return;
        }

        EditorGUILayout.LabelField("Farming Drop Items", EditorStyles.miniBoldLabel);
        int removeIndex = -1;
        int moveFromIndex = -1;
        int moveToIndex = -1;

        for (int i = 0; i < dropItems.arraySize; i++)
        {
            SerializedProperty entry = dropItems.GetArrayElementAtIndex(i);
            SerializedProperty itemDefinition = entry.FindPropertyRelative("itemDefinition");
            SerializedProperty amount = entry.FindPropertyRelative("amount");
            SerializedProperty minimumGrowth = entry.FindPropertyRelative("minimumGrowth");
            SerializedProperty maximumGrowth = entry.FindPropertyRelative("maximumGrowth");
            SerializedProperty dropChance = entry.FindPropertyRelative("dropChance");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Entry {i + 1}", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(i <= 0))
            {
                if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(28f)))
                {
                    moveFromIndex = i;
                    moveToIndex = i - 1;
                }
            }

            using (new EditorGUI.DisabledScope(i >= dropItems.arraySize - 1))
            {
                if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(28f)))
                {
                    moveFromIndex = i;
                    moveToIndex = i + 1;
                }
            }

            if (GUILayout.Button("Remove", EditorStyles.miniButtonRight, GUILayout.Width(64f)))
            {
                removeIndex = i;
            }

            EditorGUILayout.EndHorizontal();

            ItemDefinition currentItem =
                itemDefinition.objectReferenceValue as ItemDefinition;
            int entryIndex = i;
            dropItemDropdown.Draw(
                "Item",
                currentItem,
                selectedItem => ApplyDropItemSelection(entryIndex, selectedItem));

            amount.intValue = Mathf.Max(
                0,
                EditorGUILayout.IntField("Amount", amount.intValue));
            minimumGrowth.intValue = EditorGUILayout.IntSlider(
                "Minimum Growth",
                minimumGrowth.intValue,
                ResourceDefinition.MinGrowth,
                ResourceDefinition.MaxGrowth);
            maximumGrowth.intValue = EditorGUILayout.IntSlider(
                "Maximum Growth",
                maximumGrowth.intValue,
                minimumGrowth.intValue,
                ResourceDefinition.MaxGrowth);
            float chancePercent = EditorGUILayout.Slider(
                "Drop Chance (%)",
                Mathf.Clamp01(dropChance.floatValue) * 100f,
                0f,
                100f);
            dropChance.floatValue = chancePercent * 0.01f;

            if (currentItem != null)
            {
                Rect itemNameRect = EditorGUILayout.GetControlRect();
                GUI.Label(
                    itemNameRect,
                    $"Item ID {currentItem.id} · {currentItem.itemName}",
                    EditorStyles.whiteLabel);
            }

            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
        {
            dropItems.DeleteArrayElementAtIndex(removeIndex);
        }
        else if (moveFromIndex >= 0 && moveToIndex >= 0)
        {
            dropItems.MoveArrayElement(moveFromIndex, moveToIndex);
        }

        if (GUILayout.Button("Add Drop Item"))
        {
            int newIndex = dropItems.arraySize;
            dropItems.InsertArrayElementAtIndex(newIndex);
            SerializedProperty entry = dropItems.GetArrayElementAtIndex(newIndex);
            entry.FindPropertyRelative("itemDefinition").objectReferenceValue = null;
            entry.FindPropertyRelative("amount").intValue = 1;
            entry.FindPropertyRelative("minimumGrowth").intValue =
                ResourceDefinition.MinGrowth;
            entry.FindPropertyRelative("maximumGrowth").intValue =
                ResourceDefinition.MaxGrowth;
            entry.FindPropertyRelative("dropChance").floatValue = 1f;
        }

        string growthDescription = selectedDefinition != null
                                   && selectedDefinition.placementCategory
                                   == ResourceDefinition.PlacementCategory.Tree
            ? "Tree Growth 조건을 만족한 항목만"
            : "Tree가 아닌 리소스는 Growth 10으로 판정하며, 조건을 만족한 항목만";
        EditorGUILayout.HelpBox(
            $"{growthDescription} 목록 순서대로 확률 판정 후 고정 수량으로 지급됩니다. "
            + "목록이 비어 있으면 기존 에셋의 단일 출력 데이터를 사용합니다. "
            + "광산·시추 기계의 단일 출력은 기존 Output 설정을 계속 사용합니다.",
            MessageType.Info);
    }

    private void ApplyDropItemSelection(int entryIndex, ItemDefinition selectedItem)
    {
        if (selectedDefinition == null)
        {
            return;
        }

        SerializedObject serializedDefinition = new SerializedObject(selectedDefinition);
        serializedDefinition.Update();
        SerializedProperty dropItems = serializedDefinition.FindProperty("dropItems");
        if (dropItems == null || entryIndex < 0 || entryIndex >= dropItems.arraySize)
        {
            return;
        }

        SerializedProperty itemDefinition = dropItems
            .GetArrayElementAtIndex(entryIndex)
            .FindPropertyRelative("itemDefinition");
        if (itemDefinition.objectReferenceValue == selectedItem)
        {
            return;
        }

        Undo.RecordObject(selectedDefinition, "Set Resource Drop Item");
        itemDefinition.objectReferenceValue = selectedItem;
        serializedDefinition.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedDefinition);
        selectedDefinitionSerializedObject = serializedDefinition;
        Repaint();
    }

    private void DrawValidationSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

        int issueCount = 0;
        if (string.IsNullOrWhiteSpace(selectedDefinition.resourceName))
        {
            issueCount++;
            EditorGUILayout.HelpBox("Resource Name이 비어 있습니다.", MessageType.Error);
        }

        Resource prefab = selectedDefinition.prefab;
        if (prefab == null)
        {
            issueCount++;
            EditorGUILayout.HelpBox("Resource Prefab이 지정되지 않았습니다.", MessageType.Error);
        }
        else
        {
            SerializedObject serializedPrefab = GetPrefabSerializedObject(prefab);
            SerializedProperty linkedDefinition = serializedPrefab?.FindProperty("definition");
            if (linkedDefinition != null && linkedDefinition.objectReferenceValue != selectedDefinition)
            {
                issueCount++;
                EditorGUILayout.HelpBox(
                    "프리팹의 Definition 참조가 현재 ResourceDefinition과 다릅니다.",
                    MessageType.Warning);
            }

            if (IsNamedTree(selectedDefinition)
                && !(prefab is ProjectF.MapObjects.Tree))
            {
                issueCount++;
                EditorGUILayout.HelpBox(
                    "Tree 이름을 가진 프리팹에 Tree 컴포넌트가 부착되지 않았습니다.",
                    MessageType.Warning);
            }
        }

        if (HasDuplicateResourceName(selectedDefinition))
        {
            issueCount++;
            EditorGUILayout.HelpBox(
                "동일한 Resource Name을 사용하는 ResourceDefinition이 있습니다.",
                MessageType.Warning);
        }

        if (issueCount == 0)
        {
            EditorGUILayout.HelpBox("문제를 찾지 못했습니다.", MessageType.Info);
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(6f);
    }

    private void DrawPrefabSection()
    {
        Resource prefab = selectedDefinition.prefab;
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Resource Prefab", EditorStyles.boldLabel);
        if (prefab == null)
        {
            EditorGUILayout.HelpBox("Definition에 프리팹을 먼저 지정하세요.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Prefab", prefab, typeof(Resource), false);
        }
        if (GUILayout.Button("Select", GUILayout.Width(58f)))
        {
            CommitCurrentEdits();
            Selection.activeObject = prefab.gameObject;
            EditorGUIUtility.PingObject(prefab.gameObject);
        }

        if (GUILayout.Button("Open", GUILayout.Width(52f)))
        {
            CommitCurrentEdits();
            AssetDatabase.OpenAsset(prefab.gameObject);
        }
        EditorGUILayout.EndHorizontal();

        SerializedObject serializedPrefab = GetPrefabSerializedObject(prefab);
        if (serializedPrefab == null)
        {
            EditorGUILayout.EndVertical();
            return;
        }

        serializedPrefab.UpdateIfRequiredOrScript();
        EditorGUI.BeginChangeCheck();

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("Map Object", EditorStyles.miniBoldLabel);
        DrawProperty(serializedPrefab, "objectName", "Object Name");
        DrawProperty(serializedPrefab, "mapStatus", "Map Size");
        DrawProperty(serializedPrefab, "multiFocusMode", "Multi Focus Mode");

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("Harvest Presentation", EditorStyles.miniBoldLabel);
        DrawProperty(serializedPrefab, "workPerGaugeDot", "Work Per Gauge Dot");
        DrawProperty(serializedPrefab, "portableMoveInterval", "Portable Move Interval");
        DrawProperty(serializedPrefab, "focusOffset", "Focus Offset");
        if (prefab is ProjectF.MapObjects.Tree)
        {
            DrawProperty(serializedPrefab, "growth", "Prefab Growth");
            DrawProperty(serializedPrefab, "minimumBodyScaleRatio", "Growth 0 Scale");
            DrawProperty(serializedPrefab, "maximumBodyScaleRatio", "Growth 10 Scale");
        }
        else
        {
            DrawProperty(serializedPrefab, "minimumBodyScaleRatio", "Minimum Body Scale");
            DrawProperty(serializedPrefab, "maximumBodyScaleRatio", "Maximum Body Scale");
            DrawProperty(serializedPrefab, "dynamicScaleMaxResourceCount", "Max Count For Scale");
        }

        ClampPrefabProperties(serializedPrefab);
        bool prefabChanged = EditorGUI.EndChangeCheck();
        serializedPrefab.ApplyModifiedProperties();
        if (prefabChanged)
        {
            EditorUtility.SetDirty(prefab);
            EditorUtility.SetDirty(prefab.gameObject);
        }

        GUILayout.Space(6f);
        DrawDefinitionControlledPrefabValues(serializedPrefab);

        if (GUILayout.Button("Apply Definition Defaults to Prefab", GUILayout.Height(25f)))
        {
            ApplyDefinitionDefaultsToPrefab();
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(8f);
    }

    private void DrawDefinitionControlledPrefabValues(SerializedObject serializedPrefab)
    {
        EditorGUILayout.LabelField("Definition-controlled Prefab Values", EditorStyles.miniBoldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            DrawProperty(serializedPrefab, "definition", "Definition");
            DrawProperty(serializedPrefab, "harvestMode", "Harvest Mode");
            SerializedProperty status = serializedPrefab.FindProperty("resourceStatus");
            if (status != null)
            {
                EditorGUILayout.PropertyField(
                    status.FindPropertyRelative("resourceCount"),
                    new GUIContent("Resource Count"));
                EditorGUILayout.PropertyField(
                    status.FindPropertyRelative("getCount"),
                    new GUIContent("Output Count"));
                EditorGUILayout.PropertyField(
                    status.FindPropertyRelative("maxGauge"),
                    new GUIContent("Max Gauge"));
                EditorGUILayout.PropertyField(
                    status.FindPropertyRelative("currentGague"),
                    new GUIContent("Current Gauge"));
            }
        }
    }

    private void ApplyDefinitionDefaultsToPrefab()
    {
        Resource prefab = selectedDefinition != null ? selectedDefinition.prefab : null;
        if (prefab == null)
        {
            return;
        }

        CommitCurrentEdits();
        Undo.RecordObject(prefab, "Apply Resource Definition Defaults");
        SerializedObject serializedPrefab = new SerializedObject(prefab);
        serializedPrefab.Update();
        serializedPrefab.FindProperty("definition").objectReferenceValue = selectedDefinition;
        serializedPrefab.FindProperty("harvestMode").enumValueIndex = (int)selectedDefinition.harvestMode;

        SerializedProperty status = serializedPrefab.FindProperty("resourceStatus");
        status.FindPropertyRelative("resourceCount").intValue =
            Mathf.Max(1, selectedDefinition.defaultResourceCount);
        status.FindPropertyRelative("getCount").intValue =
            Mathf.Max(1, selectedDefinition.defaultGetCount);
        status.FindPropertyRelative("maxGauge").intValue =
            Mathf.Max(1, selectedDefinition.defaultMaxGauge);
        status.FindPropertyRelative("currentGague").intValue = Mathf.Clamp(
            selectedDefinition.defaultCurrentGauge,
            0,
            Mathf.Max(1, selectedDefinition.defaultMaxGauge));
        serializedPrefab.ApplyModifiedProperties();
        EditorUtility.SetDirty(prefab);
        EditorUtility.SetDirty(prefab.gameObject);
        AssetDatabase.SaveAssets();
        ClearPrefabSerializedObjectCache();
        Repaint();
    }

    private static void ClampPrefabProperties(SerializedObject serializedPrefab)
    {
        SerializedProperty workPerGaugeDot = serializedPrefab.FindProperty("workPerGaugeDot");
        SerializedProperty moveInterval = serializedPrefab.FindProperty("portableMoveInterval");
        SerializedProperty minimumScale = serializedPrefab.FindProperty("minimumBodyScaleRatio");
        SerializedProperty maximumScale = serializedPrefab.FindProperty("maximumBodyScaleRatio");
        SerializedProperty maxCount = serializedPrefab.FindProperty("dynamicScaleMaxResourceCount");
        SerializedProperty growth = serializedPrefab.FindProperty("growth");

        if (workPerGaugeDot != null)
        {
            workPerGaugeDot.floatValue = Mathf.Max(0.01f, workPerGaugeDot.floatValue);
        }

        if (moveInterval != null)
        {
            moveInterval.floatValue = Mathf.Max(0f, moveInterval.floatValue);
        }

        if (minimumScale != null)
        {
            minimumScale.floatValue = Mathf.Clamp01(minimumScale.floatValue);
        }

        if (maximumScale != null)
        {
            maximumScale.floatValue = Mathf.Max(
                minimumScale != null ? minimumScale.floatValue : 0f,
                maximumScale.floatValue);
        }

        if (maxCount != null)
        {
            maxCount.intValue = Mathf.Max(1, maxCount.intValue);
        }

        if (growth != null)
        {
            growth.floatValue = Mathf.Clamp(
                growth.floatValue,
                ResourceDefinition.MinGrowth,
                ResourceDefinition.MaxGrowth);
        }
    }

    private static void DrawProperty(SerializedObject serializedObject, string propertyPath, string label)
    {
        SerializedProperty property = serializedObject?.FindProperty(propertyPath);
        if (property != null)
        {
            EditorGUILayout.PropertyField(property, new GUIContent(label), true);
        }
    }

    private void SaveCurrentAssets()
    {
        CommitCurrentEdits();
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent("Resource Data saved"));
    }

    private void CommitCurrentEdits()
    {
        if (Event.current != null)
        {
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
        }

        if (selectedDefinitionSerializedObject != null
            && selectedDefinitionSerializedObject.targetObject != null)
        {
            selectedDefinitionSerializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(selectedDefinitionSerializedObject.targetObject);
        }

        if (cachedPrefabSerializedObject != null
            && cachedPrefabSerializedObject.targetObject != null)
        {
            cachedPrefabSerializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(cachedPrefabSerializedObject.targetObject);
        }
    }

    private void SelectDefinition(ResourceDefinition definition)
    {
        if (definition == selectedDefinition)
        {
            return;
        }

        CommitCurrentEdits();
        selectedDefinition = definition;
        detailScroll = Vector2.zero;
        ClearSerializedObjectCaches();
        Repaint();
    }

    private void AdoptProjectSelection()
    {
        ResourceDefinition selectedAsset = Selection.activeObject as ResourceDefinition;
        if (selectedAsset == null || selectedAsset == selectedDefinition)
        {
            return;
        }

        EnsureCatalog();
        if (definitions.Contains(selectedAsset))
        {
            SelectDefinition(selectedAsset);
        }
    }

    private void CreateDefinition()
    {
        CommitCurrentEdits();
        EnsureAssetFolder();
        ResourceDefinition definition = ScriptableObject.CreateInstance<ResourceDefinition>();
        definition.resourceName = "New resource";
        definition.harvestMode = Resource.HarvestMode.Auto;
        definition.placementCategory = ResourceDefinition.PlacementCategory.Ore;
        definition.defaultResourceCount = 1;
        definition.defaultGetCount = 1;
        definition.defaultMaxGauge = 10;
        definition.defaultCurrentGauge = 10;

        string path = AssetDatabase.GenerateUniqueAssetPath(
            $"{AssetFolder}/Resource_New resource.asset");
        AssetDatabase.CreateAsset(definition, path);
        AssetDatabase.SaveAssets();
        pendingSelectionPath = path;
        catalogDirty = true;
        EnsureCatalog();
        Selection.activeObject = selectedDefinition;
        EditorGUIUtility.PingObject(selectedDefinition);
    }

    private void DuplicateSelectedDefinition()
    {
        if (selectedDefinition == null)
        {
            return;
        }

        CommitCurrentEdits();
        EnsureAssetFolder();
        ResourceDefinition copy = Instantiate(selectedDefinition);
        copy.name = selectedDefinition.name + " Copy";
        copy.resourceName = GetDisplayName(selectedDefinition) + " Copy";

        string fileName = SanitizeFileName($"Resource_{copy.resourceName}.asset");
        string path = AssetDatabase.GenerateUniqueAssetPath($"{AssetFolder}/{fileName}");
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();
        pendingSelectionPath = path;
        catalogDirty = true;
        EnsureCatalog();
        Selection.activeObject = selectedDefinition;
        EditorGUIUtility.PingObject(selectedDefinition);
    }

    private void DeleteSelectedDefinition()
    {
        if (selectedDefinition == null)
        {
            return;
        }

        string path = GetSelectedDefinitionPath();
        string displayName = GetDisplayName(selectedDefinition);
        if (!EditorUtility.DisplayDialog(
                "Delete Resource Definition",
                $"'{displayName}' ResourceDefinition을 삭제하시겠습니까?\n{path}",
                "Delete",
                "Cancel"))
        {
            return;
        }

        int selectedIndex = definitions.IndexOf(selectedDefinition);
        CommitCurrentEdits();
        selectedDefinition = null;
        ClearSerializedObjectCaches();
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();

        pendingSelectionPath = null;
        catalogDirty = true;
        EnsureCatalog();
        if (definitions.Count > 0)
        {
            SelectDefinition(definitions[Mathf.Clamp(selectedIndex, 0, definitions.Count - 1)]);
        }
    }

    private void EnsureCatalog()
    {
        if (!catalogDirty)
        {
            EnsureVisibleDefinitions();
            return;
        }

        string preferredPath = !string.IsNullOrEmpty(pendingSelectionPath)
            ? pendingSelectionPath
            : GetSelectedDefinitionPath();
        pendingSelectionPath = null;
        definitions.Clear();

        string[] guids = AssetDatabase.FindAssets("t:ResourceDefinition", new[] { AssetFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            ResourceDefinition definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(path);
            if (definition != null && !definitions.Contains(definition))
            {
                definitions.Add(definition);
            }
        }

        definitions.Sort(CompareDefinitions);
        ResourceDefinition nextSelection = FindDefinitionByPath(preferredPath);
        if (nextSelection == null && selectedDefinition != null && definitions.Contains(selectedDefinition))
        {
            nextSelection = selectedDefinition;
        }

        if (nextSelection == null && definitions.Count > 0)
        {
            nextSelection = definitions[0];
        }

        if (selectedDefinition != nextSelection)
        {
            CommitCurrentEdits();
            selectedDefinition = nextSelection;
            ClearSerializedObjectCaches();
        }

        catalogDirty = false;
        visibleDefinitionsDirty = true;
        EnsureVisibleDefinitions();
    }

    private void EnsureVisibleDefinitions()
    {
        string normalizedSearch = (searchText ?? string.Empty).Trim();
        if (!visibleDefinitionsDirty
            && string.Equals(normalizedSearch, cachedSearchText, StringComparison.Ordinal)
            && categoryFilter == cachedCategoryFilter)
        {
            return;
        }

        visibleDefinitions.Clear();
        for (int i = 0; i < definitions.Count; i++)
        {
            ResourceDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            if (categoryFilter >= 0
                && (int)definition.placementCategory != categoryFilter)
            {
                continue;
            }

            if (!MatchesSearch(definition, normalizedSearch))
            {
                continue;
            }

            visibleDefinitions.Add(definition);
        }

        cachedSearchText = normalizedSearch;
        cachedCategoryFilter = categoryFilter;
        visibleDefinitionsDirty = false;
    }

    private static bool MatchesSearch(ResourceDefinition definition, string normalizedSearch)
    {
        if (string.IsNullOrEmpty(normalizedSearch))
        {
            return true;
        }

        return GetDisplayName(definition).IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0
               || definition.name.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0
               || definition.placementCategory.ToString().IndexOf(
                   normalizedSearch,
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private ResourceDefinition FindDefinitionByPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            if (string.Equals(
                    AssetDatabase.GetAssetPath(definitions[i]),
                    path,
                    StringComparison.OrdinalIgnoreCase))
            {
                return definitions[i];
            }
        }

        return null;
    }

    private SerializedObject GetSelectedDefinitionSerializedObject()
    {
        if (selectedDefinition == null)
        {
            return null;
        }

        if (selectedDefinitionSerializedObject == null
            || selectedDefinitionSerializedObject.targetObject != selectedDefinition)
        {
            selectedDefinitionSerializedObject = new SerializedObject(selectedDefinition);
        }

        return selectedDefinitionSerializedObject;
    }

    private SerializedObject GetPrefabSerializedObject(Resource prefab)
    {
        if (prefab == null)
        {
            return null;
        }

        if (cachedPrefabSerializedObject == null || cachedPrefab != prefab)
        {
            cachedPrefab = prefab;
            cachedPrefabSerializedObject = new SerializedObject(prefab);
        }

        return cachedPrefabSerializedObject;
    }

    private void ClearSerializedObjectCaches()
    {
        selectedDefinitionSerializedObject = null;
        ClearPrefabSerializedObjectCache();
    }

    private void ClearPrefabSerializedObjectCache()
    {
        cachedPrefab = null;
        cachedPrefabSerializedObject = null;
    }

    private bool HasDuplicateResourceName(ResourceDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.resourceName))
        {
            return false;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ResourceDefinition candidate = definitions[i];
            if (candidate != null
                && candidate != definition
                && string.Equals(
                    candidate.resourceName?.Trim(),
                    definition.resourceName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNamedTree(ResourceDefinition definition)
    {
        return definition != null
               && !string.IsNullOrWhiteSpace(definition.resourceName)
               && definition.resourceName.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int CompareDefinitions(ResourceDefinition left, ResourceDefinition right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int categoryComparison = left.placementCategory.CompareTo(right.placementCategory);
        return categoryComparison != 0
            ? categoryComparison
            : string.Compare(GetDisplayName(left), GetDisplayName(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDisplayName(ResourceDefinition definition)
    {
        if (definition == null)
        {
            return "(Missing)";
        }

        return !string.IsNullOrWhiteSpace(definition.resourceName)
            ? definition.resourceName.Trim()
            : definition.name;
    }

    private string GetSelectedDefinitionPath()
    {
        return selectedDefinition != null
            ? AssetDatabase.GetAssetPath(selectedDefinition)
            : string.Empty;
    }

    private static void DrawResourceIcon(Rect rect, ResourceDefinition definition)
    {
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.08f));
        if (definition == null)
        {
            return;
        }

        Sprite resourceIcon = definition.ResourceIcon;
        if (resourceIcon != null && resourceIcon.texture != null)
        {
            Texture2D texture = resourceIcon.texture;
            Rect textureRect = resourceIcon.textureRect;
            Rect textureCoordinates = new Rect(
                textureRect.x / texture.width,
                textureRect.y / texture.height,
                textureRect.width / texture.width,
                textureRect.height / texture.height);
            Color previousColor = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(
                GetAspectFitRect(rect, textureRect.width, textureRect.height),
                texture,
                textureCoordinates,
                true);
            GUI.color = previousColor;
            return;
        }

        if (definition.prefab == null)
        {
            return;
        }

        Texture icon = AssetPreview.GetMiniThumbnail(definition.prefab.gameObject);
        if (icon != null)
        {
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
        }
    }

    private static Rect GetAspectFitRect(Rect targetRect, float sourceWidth, float sourceHeight)
    {
        if (targetRect.width <= 0f
            || targetRect.height <= 0f
            || sourceWidth <= 0f
            || sourceHeight <= 0f)
        {
            return targetRect;
        }

        float sourceAspect = sourceWidth / sourceHeight;
        float targetAspect = targetRect.width / targetRect.height;
        if (sourceAspect > targetAspect)
        {
            float fittedHeight = targetRect.width / sourceAspect;
            return new Rect(
                targetRect.x,
                targetRect.y + (targetRect.height - fittedHeight) * 0.5f,
                targetRect.width,
                fittedHeight);
        }

        float fittedWidth = targetRect.height * sourceAspect;
        return new Rect(
            targetRect.x + (targetRect.width - fittedWidth) * 0.5f,
            targetRect.y,
            fittedWidth,
            targetRect.height);
    }

    private static Color GetCategoryColor(ResourceDefinition.PlacementCategory category)
    {
        switch (category)
        {
            case ResourceDefinition.PlacementCategory.Oil:
                return new Color(0.34f, 0.25f, 0.42f, 0.9f);
            case ResourceDefinition.PlacementCategory.Tree:
                return new Color(0.22f, 0.42f, 0.24f, 0.9f);
            default:
                return new Color(0.38f, 0.34f, 0.27f, 0.9f);
        }
    }

    private static GUIStyle GetCenteredMiniLabelStyle()
    {
        if (centeredMiniLabelStyle == null)
        {
            centeredMiniLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            centeredMiniLabelStyle.normal.textColor = Color.white;
        }

        return centeredMiniLabelStyle;
    }

    private static string SanitizeFileName(string fileName)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidCharacters.Length; i++)
        {
            fileName = fileName.Replace(invalidCharacters[i], '_');
        }

        return fileName;
    }

    private static void EnsureAssetFolder()
    {
        if (AssetDatabase.IsValidFolder(AssetFolder))
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Data"))
        {
            AssetDatabase.CreateFolder("Assets", "Data");
        }

        AssetDatabase.CreateFolder("Assets/Data", "MapObject");
    }
}
