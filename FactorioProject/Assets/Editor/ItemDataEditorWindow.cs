using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

public class ItemDataEditorWindow : EditorWindow
{
    private const float SidebarWidth = 260f;
    private const float GiveButtonWidth = 46f;
    private const float ItemListRowHeight = 28f;
    private const int ItemListOverscanRows = 3;
    private const int LargeInputOutputPairAutoCollapseThreshold = 8;
    private const float RectGridCellSize = 34f;
    private const float RectGridCellSpacing = 5f;
    private const float RectGridPaletteBlockWidth = 78f;
    private const float PlacementCenterGridCellSize = 30f;
    private const float PlacementCenterGridCellSpacing = 4f;
    private const string ItemDefinitionAssetFolder = "Assets/Data/Items";
    private const string UiIconAtlasFolder = "Assets/Image/UI/Item";
    private const string UiIconAtlasPath = UiIconAtlasFolder + "/ItemUIIcons.spriteatlas";
    private static readonly RectGridPaletteEntry[] RectGridPaletteEntries =
    {
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.Object, "Object", "Object", new Color(0.35f, 0.45f, 0.62f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.InputEnergy, "Input Energy", "Input\nEnergy", new Color(0.55f, 0.44f, 0.18f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.InputItem, "Input Item", "Input\nItem", new Color(0.23f, 0.48f, 0.32f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.Output, "Output", "Output", new Color(0.48f, 0.28f, 0.28f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.PipeInputEnergy, "Pipe Input Energy", "Pipe\nEnergy", new Color(0.35f, 0.5f, 0.72f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.PipeInputItem, "Pipe Input Item", "Pipe\nInput", new Color(0.18f, 0.52f, 0.5f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.PipeOutputItem, "Pipe Output Item", "Pipe\nOutput", new Color(0.54f, 0.34f, 0.55f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.PipeInput, "Pipe Input", "Pipe\nPass", new Color(0.22f, 0.58f, 0.68f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.DoubleEnergy, "Double Energy", "Double\nEnergy", new Color(0.68f, 0.56f, 0.2f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.DoubleInputItem, "Double Input Item", "Double\nInput", new Color(0.28f, 0.62f, 0.38f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.DoublePipeOutputItem, "Double Pipe Output Item", "Double\nOutput", new Color(0.62f, 0.36f, 0.36f, 1f))
    };

    private Vector2 listScroll;
    private Vector2 detailScroll;
    private int selectedItemId = -1;
    private string itemSearchText = string.Empty;
    private bool showPlayModeDetailFields;
    private ItemDefinition pendingReorderSelection;
    private readonly HashSet<string> collapsedInputOutputPairSectionKeys = new HashSet<string>();
    private readonly HashSet<string> collapsedInputOutputPairKeys = new HashSet<string>();
    private readonly HashSet<string> collapsedInputOutputSlotLayoutSectionKeys = new HashSet<string>();
    private readonly HashSet<string> initializedInputOutputPairSectionKeys = new HashSet<string>();
    private readonly HashSet<string> initializedInputOutputPairKeys = new HashSet<string>();
    private readonly HashSet<string> initializedInputOutputSlotLayoutSectionKeys = new HashSet<string>();
    private readonly Dictionary<int, string> cachedCraftingTreeIngredientSummaries = new Dictionary<int, string>();
    private readonly List<CraftingTreeRuntime.IngredientEntry> craftingTreeIngredientBuffer =
        new List<CraftingTreeRuntime.IngredientEntry>();
    private readonly List<string> craftingTreeIngredientSummaryParts = new List<string>();
    private ItemManager cachedItemManager;
    private bool itemManagerCacheDirty = true;
    private ItemManager cachedDefinitionsItemManager;
    private int cachedDefinitionsItemManagerCount = -1;
    private bool definitionsCacheDirty = true;
    private int definitionsCacheVersion;
    private readonly List<ItemDefinition> cachedDefinitions = new List<ItemDefinition>();
    private readonly List<ItemDefinition> cachedVisibleDefinitions = new List<ItemDefinition>();
    private string cachedVisibleDefinitionsSearchText = string.Empty;
    private int cachedVisibleDefinitionsVersion = -1;
    private ItemDefinition[] cachedInputOutputDefinitionOptions = Array.Empty<ItemDefinition>();
    private GUIContent[] cachedInputOutputDefinitionOptionContents = Array.Empty<GUIContent>();
    private readonly Dictionary<int, int> cachedInputOutputDefinitionOptionIndexes = new Dictionary<int, int>();
    private int cachedInputOutputDefinitionOptionsVersion = -1;
    private int cachedCraftingTreeIngredientSummaryVersion = -1;
    private readonly Dictionary<int, string> inputOutputTargetKeyCache = new Dictionary<int, string>();
    private static GUIStyle placementCenterLabelStyle;
    private static GUIStyle rectGridPaletteLabelStyle;
    private static GUIStyle rectGridBlockLabelStyle;

    private readonly struct RectGridPaletteEntry
    {
        public readonly InputOutputModule.RectGridBlockType blockType;
        public readonly string label;
        public readonly string displayLabel;
        public readonly Color color;

        public RectGridPaletteEntry(InputOutputModule.RectGridBlockType blockType, string label, string displayLabel, Color color)
        {
            this.blockType = blockType;
            this.label = label;
            this.displayLabel = displayLabel;
            this.color = color;
        }
    }

    [Serializable]
    private class ItemDataJsonFile
    {
        public string format = "ProjectF.ItemData";
        public int version = 2;
        public List<ItemDataJsonEntry> items = new List<ItemDataJsonEntry>();
    }

    [Serializable]
    private class ItemDataJsonEntry
    {
        public int id = -1;
        public string itemName;
        public string definitionAssetPath;
        public string mapObjectAssetPath;
        public string portableMeshAssetPath;
        public string portableMaterialAssetPath;
        public string iconAssetPath;
        public List<string> interactionButtonAssetPaths;
        public int size;
        public bool itemFilter;
        public int capacity = -1;
        public bool storesFluid;
        public float fluidStorageLiters;
        public bool hasFluidDisplayColor;
        public Color fluidDisplayColor = Color.white;
        public float craftingDurationSeconds = -1f;
        public string energyType;
        public int energyTypeValue = -1;
        public int energyAmount;
        public string useEnergyType;
        public int useEnergyTypeValue = -1;
        public float useEnergyAmount;
        public float completeEnergy;
        public int utilityPoleConnectionRadius = -1;
        public int utilityPoleSupplyRadius = -1;
        public int mapSizeX = -1;
        public int mapSizeY = -1;
        public int placementCenterX = -1;
        public int placementCenterY = -1;
        public float focusRadius = -1f;
        public int workableRangeCells = -1;
        public float conveyorSpeed = -1f;
        public float vehicleAccelerationPerSecond = -1f;
        public float vehicleMaxSpeed = -1f;
        public float vehicleStopInertiaSeconds = -1f;
        public float waterLitersPerSecond = -1f;
        public string multiFocusMode;
        public int multiFocusModeValue = -1;
        public string mapFilter;
        public int mapFilterValue = -1;
        public string inputOutputLayoutType;
        public int rectGridWidth;
        public int rectGridHeight;
        public List<RectGridBlockPlacementJsonEntry> rectGridBlocks = new List<RectGridBlockPlacementJsonEntry>();
        public List<InputOutputPairJsonEntry> ioPairs = new List<InputOutputPairJsonEntry>();
    }

    [Serializable]
    private class InputOutputJsonEntry
    {
        public int id = -1;
        public string itemName;
        public string definitionAssetPath;
        public int count = 1;
    }

    [Serializable]
    private class InputOutputPairJsonEntry
    {
        public InputOutputJsonEntry input;
        public InputOutputJsonEntry output;
    }

    [Serializable]
    private class RectGridBlockPlacementJsonEntry
    {
        public int x;
        public int y;
        public string blockType;
    }

    [MenuItem("Window/ProjectF/Item Data")]
    public static void ShowWindow()
    {
        ItemDataEditorWindow window = GetWindow<ItemDataEditorWindow>("Item Data");
        window.minSize = new Vector2(720f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += HandleUndoRedoPerformed;
        InvalidateAllCaches();
        EnsureSelection();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedoPerformed;
    }

    private void OnFocus()
    {
        Repaint();
    }

    private void OnHierarchyChange()
    {
        if (IsCachedItemManagerValid(cachedItemManager))
        {
            return;
        }

        itemManagerCacheDirty = true;
        Repaint();
    }

    private void OnProjectChange()
    {
        InvalidateDefinitionCache();
        inputOutputTargetKeyCache.Clear();
        Repaint();
    }

    private void HandleUndoRedoPerformed()
    {
        InvalidateDefinitionPresentationCache();
        inputOutputTargetKeyCache.Clear();
        Repaint();
    }

    private void OnGUI()
    {
        DrawBackground();
        DrawItemList();
        DrawDetailPanel();
    }

    private void InvalidateAllCaches()
    {
        itemManagerCacheDirty = true;
        InvalidateDefinitionCache();
        inputOutputTargetKeyCache.Clear();
    }

    private void InvalidateDefinitionCache()
    {
        definitionsCacheDirty = true;
        cachedDefinitionsItemManager = null;
        cachedDefinitionsItemManagerCount = -1;
        cachedDefinitions.Clear();
        definitionsCacheVersion++;
        InvalidateDefinitionPresentationCache();
    }

    private void InvalidateDefinitionPresentationCache()
    {
        cachedVisibleDefinitions.Clear();
        cachedVisibleDefinitionsSearchText = string.Empty;
        cachedVisibleDefinitionsVersion = -1;
        cachedInputOutputDefinitionOptions = Array.Empty<ItemDefinition>();
        cachedInputOutputDefinitionOptionContents = Array.Empty<GUIContent>();
        cachedInputOutputDefinitionOptionIndexes.Clear();
        cachedInputOutputDefinitionOptionsVersion = -1;
        cachedCraftingTreeIngredientSummaries.Clear();
        cachedCraftingTreeIngredientSummaryVersion = -1;
    }

    private void DrawBackground()
    {
        EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), new Color(0.15f, 0.15f, 0.15f));
    }

    private void DrawItemList()
    {
        Rect sidebarRect = new Rect(0f, 0f, SidebarWidth, position.height);
        EditorGUI.DrawRect(sidebarRect, new Color(0.12f, 0.12f, 0.12f));

        GUILayout.BeginArea(sidebarRect);
        GUILayout.Space(10f);
        DrawToolbar();
        DrawSearchField();
        EditorGUILayout.LabelField("Items", EditorStyles.boldLabel);

        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EditorGUILayout.HelpBox("씬에서 ItemManager를 찾을 수 없습니다.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        List<ItemDefinition> definitions = GetDefinitions(itemManager);
        if (definitions.Count == 0)
        {
            EditorGUILayout.HelpBox("ItemDefinitions가 비어있습니다.", MessageType.Warning);
            GUILayout.EndArea();
            return;
        }

        List<ItemDefinition> visibleDefinitions = FilterDefinitions(definitions);
        EnsureSelection(definitions, visibleDefinitions);
        bool allowReorder = string.IsNullOrWhiteSpace(itemSearchText);

        if (visibleDefinitions.Count == 0)
        {
            EditorGUILayout.HelpBox("검색 결과가 없습니다.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        int firstVisibleIndex = GetFirstVisibleItemListIndex(visibleDefinitions.Count);
        int lastVisibleIndex = GetLastVisibleItemListIndex(firstVisibleIndex, visibleDefinitions.Count);
        if (firstVisibleIndex > 0)
        {
            GUILayout.Space(firstVisibleIndex * ItemListRowHeight);
        }

        for (int i = firstVisibleIndex; i <= lastVisibleIndex; i++)
        {
            ItemDefinition definition = visibleDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            string displayName = GetDefinitionDisplayName(definition);
            bool isSelected = definition.id == selectedItemId;
            Rect rowRect = GUILayoutUtility.GetRect(1f, ItemListRowHeight, GUILayout.ExpandWidth(true));
            Rect selectRect = new Rect(rowRect.x, rowRect.y, Mathf.Max(1f, rowRect.width - GiveButtonWidth - 4f), rowRect.height);
            Rect giveRect = new Rect(selectRect.xMax + 4f, rowRect.y, GiveButtonWidth, rowRect.height);
            GUIContent content = new GUIContent($"[{definition.id}] {displayName}");
            ItemDefinitionDragAndDropUtility.HandleListItemDrag(selectRect, definition, content.text, this);
            if (allowReorder)
            {
                HandleDefinitionReorderDropTarget(rowRect, itemManager, definitions, visibleDefinitions, i);
            }

            bool pressed = GUI.Toggle(selectRect, isSelected, GUIContent.none, "Button");
            if (pressed)
            {
                selectedItemId = definition.id;
            }

            Rect iconRect = new Rect(selectRect.x + 4f, selectRect.y + 4f, 20f, 20f);
            Rect labelRect = new Rect(iconRect.xMax + 4f, selectRect.y, Mathf.Max(1f, selectRect.xMax - iconRect.xMax - 8f), selectRect.height);
            DrawItemIcon(iconRect, definition);
            GUI.Label(labelRect, content, EditorStyles.miniLabel);

            EditorGUI.BeginDisabledGroup(!EditorApplication.isPlaying);
            if (GUI.Button(giveRect, "Give"))
            {
                TryGiveItemToPlayer(definition);
            }
            EditorGUI.EndDisabledGroup();
        }

        int hiddenTrailingRowCount = visibleDefinitions.Count - lastVisibleIndex - 1;
        if (hiddenTrailingRowCount > 0)
        {
            GUILayout.Space(hiddenTrailingRowCount * ItemListRowHeight);
        }

        if (allowReorder)
        {
            Rect endDropRect = GUILayoutUtility.GetRect(1f, 16f, GUILayout.ExpandWidth(true));
            HandleDefinitionReorderDropTarget(endDropRect, itemManager, definitions, visibleDefinitions, visibleDefinitions.Count);
        }

        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private int GetFirstVisibleItemListIndex(int itemCount)
    {
        if (itemCount <= 0)
        {
            return 0;
        }

        int firstIndex = Mathf.FloorToInt(Mathf.Max(0f, listScroll.y) / ItemListRowHeight) - ItemListOverscanRows;
        return Mathf.Clamp(firstIndex, 0, itemCount - 1);
    }

    private int GetLastVisibleItemListIndex(int firstVisibleIndex, int itemCount)
    {
        if (itemCount <= 0)
        {
            return -1;
        }

        int visibleRowCount = Mathf.CeilToInt(Mathf.Max(ItemListRowHeight, position.height) / ItemListRowHeight)
            + ItemListOverscanRows * 2;
        return Mathf.Clamp(firstVisibleIndex + visibleRowCount, firstVisibleIndex, itemCount - 1);
    }

    private void HandleDefinitionReorderDropTarget(
        Rect rect,
        ItemManager itemManager,
        List<ItemDefinition> definitions,
        List<ItemDefinition> visibleDefinitions,
        int visibleInsertIndex)
    {
        ItemDefinition draggedDefinition = GetDraggedItemDefinition();
        if (draggedDefinition == null || definitions == null || visibleDefinitions == null || itemManager == null)
        {
            return;
        }

        Event current = Event.current;
        if (current == null || !rect.Contains(current.mousePosition))
        {
            return;
        }

        bool insertAfter = visibleInsertIndex < visibleDefinitions.Count && current.mousePosition.y > rect.center.y;
        bool isEndTarget = visibleInsertIndex >= visibleDefinitions.Count;
        if (isEndTarget)
        {
            insertAfter = false;
        }

        switch (current.type)
        {
            case EventType.DragUpdated:
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                current.Use();
                break;

            case EventType.DragPerform:
                DragAndDrop.AcceptDrag();
                ReorderDefinitions(itemManager, definitions, visibleDefinitions, draggedDefinition, visibleInsertIndex, insertAfter);
                GUI.changed = true;
                current.Use();
                break;

            case EventType.Repaint:
                DrawDefinitionReorderHighlight(rect, insertAfter, isEndTarget);
                break;
        }
    }

    private void ReorderDefinitions(
        ItemManager itemManager,
        List<ItemDefinition> definitions,
        List<ItemDefinition> visibleDefinitions,
        ItemDefinition draggedDefinition,
        int visibleInsertIndex,
        bool insertAfter)
    {
        if (itemManager == null || definitions == null || draggedDefinition == null)
        {
            return;
        }

        int sourceIndex = definitions.IndexOf(draggedDefinition);
        if (sourceIndex < 0)
        {
            return;
        }

        int targetIndex;
        if (visibleInsertIndex >= visibleDefinitions.Count)
        {
            targetIndex = definitions.Count;
        }
        else
        {
            ItemDefinition targetDefinition = visibleDefinitions[visibleInsertIndex];
            targetIndex = definitions.IndexOf(targetDefinition);
            if (targetIndex < 0)
            {
                return;
            }

            if (insertAfter)
            {
                targetIndex++;
            }
        }

        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        if (targetIndex < 0)
        {
            targetIndex = 0;
        }

        if (targetIndex > definitions.Count - 1)
        {
            targetIndex = definitions.Count - 1;
        }

        if (sourceIndex == targetIndex)
        {
            return;
        }

        ItemDefinition selectedDefinition = FindDefinitionById(definitions, selectedItemId);
        pendingReorderSelection = selectedDefinition != null ? selectedDefinition : draggedDefinition;
        CraftingTreeItemIdRemapper.CapturedCraftingTree craftingTreeSnapshot =
            CraftingTreeItemIdRemapper.CapturePersistedCraftingTree(definitions);

        Undo.RegisterCompleteObjectUndo(itemManager, "Reorder Item Definitions");
        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i] != null)
            {
                Undo.RecordObject(definitions[i], "Reorder Item Definitions");
            }
        }

        definitions.RemoveAt(sourceIndex);
        definitions.Insert(targetIndex, draggedDefinition);

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            definition.id = i;
            EditorUtility.SetDirty(definition);
        }

        ApplyDefinitionOrderToItemManager(itemManager, definitions);
        SyncItemManagerItemSets(itemManager, definitions);
        itemManager.ApplyItemIdsToPrefabs();
        CraftingTreeItemIdRemapper.RewritePersistedCraftingTree(craftingTreeSnapshot, definitions);
        CraftingTreeEditorWindow.ReloadOpenWindows();
        EditorUtility.SetDirty(itemManager);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        InvalidateDefinitionCache();

        if (pendingReorderSelection != null)
        {
            selectedItemId = pendingReorderSelection.id;
            pendingReorderSelection = null;
        }

        Repaint();
    }

    private static void ApplyDefinitionOrderToItemManager(ItemManager itemManager, List<ItemDefinition> orderedDefinitions)
    {
        if (itemManager == null || orderedDefinitions == null)
        {
            return;
        }

        SerializedObject serializedManager = new SerializedObject(itemManager);
        serializedManager.Update();
        SerializedProperty definitionsProperty = serializedManager.FindProperty("itemDefinitions");
        if (definitionsProperty != null && definitionsProperty.isArray)
        {
            definitionsProperty.arraySize = 0;
            int writeIndex = 0;
            for (int i = 0; i < orderedDefinitions.Count; i++)
            {
                ItemDefinition definition = orderedDefinitions[i];
                if (definition == null)
                {
                    continue;
                }

                definitionsProperty.InsertArrayElementAtIndex(writeIndex);
                definitionsProperty.GetArrayElementAtIndex(writeIndex).objectReferenceValue = definition;
                writeIndex++;
            }

            serializedManager.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(itemManager);
            return;
        }

        List<ItemDefinition> targetDefinitions = itemManager.ItemDefinitions;
        if (targetDefinitions == null)
        {
            return;
        }

        targetDefinitions.Clear();
        for (int i = 0; i < orderedDefinitions.Count; i++)
        {
            if (orderedDefinitions[i] != null)
            {
                targetDefinitions.Add(orderedDefinitions[i]);
            }
        }

        EditorUtility.SetDirty(itemManager);
    }

    private static void SyncItemManagerItemSets(ItemManager itemManager, List<ItemDefinition> definitions)
    {
        if (itemManager == null)
        {
            return;
        }

        List<ItemManager.ItemSet> itemSets = itemManager.ItemSets;
        if (itemSets == null)
        {
            return;
        }

        Dictionary<string, int> idByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            string key = GetDefinitionDisplayName(definition);
            if (!string.IsNullOrWhiteSpace(key))
            {
                idByName[key] = definition.id;
            }
        }

        for (int i = 0; i < itemSets.Count; i++)
        {
            ItemManager.ItemSet itemSet = itemSets[i];
            if (string.IsNullOrWhiteSpace(itemSet.name))
            {
                continue;
            }

            if (idByName.TryGetValue(itemSet.name, out int reorderedId))
            {
                itemSet.id = reorderedId;
                itemSets[i] = itemSet;
            }
        }

        itemSets.Sort((left, right) =>
        {
            int idCompare = left.id.CompareTo(right.id);
            if (idCompare != 0)
            {
                return idCompare;
            }

            return string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static ItemDefinition GetDraggedItemDefinition()
    {
        UnityEngine.Object[] objectReferences = DragAndDrop.objectReferences;
        if (objectReferences == null)
        {
            return null;
        }

        for (int i = 0; i < objectReferences.Length; i++)
        {
            if (objectReferences[i] is ItemDefinition definition)
            {
                return definition;
            }
        }

        return null;
    }

    private static void DrawDefinitionReorderHighlight(Rect rect, bool insertAfter, bool isEndTarget)
    {
        Color color = new Color(0.35f, 0.65f, 1f, 0.95f);
        float y = isEndTarget || insertAfter ? rect.yMax - 1f : rect.yMin;
        EditorGUI.DrawRect(new Rect(rect.xMin, y, rect.width, 2f), color);
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", GUILayout.Width(70f)))
        {
            SaveItemData();
        }

        if (GUILayout.Button("Load", GUILayout.Width(70f)))
        {
            LoadItemData();
        }

        if (GUILayout.Button("Rebuild", GUILayout.Width(80f)))
        {
            RebuildItemData();
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Export JSON", GUILayout.Width(100f)))
        {
            ExportJson();
        }

        if (GUILayout.Button("Load JSON", GUILayout.Width(100f)))
        {
            LoadJson();
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Create UI Icon Atlas", GUILayout.Width(150f)))
        {
            CreateUiIconAtlas();
        }

        if (GUILayout.Button("Open UI Icon Atlas", GUILayout.Width(140f)))
        {
            OpenUiIconAtlas();
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(6f);
    }

    private void DrawSearchField()
    {
        EditorGUILayout.BeginHorizontal();
        string nextSearchText = EditorGUILayout.TextField("Search", itemSearchText);
        if (!string.Equals(nextSearchText, itemSearchText))
        {
            itemSearchText = nextSearchText;
            listScroll = Vector2.zero;
        }

        EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(itemSearchText));
        if (GUILayout.Button("X", GUILayout.Width(24f)))
        {
            itemSearchText = string.Empty;
            listScroll = Vector2.zero;
            GUI.FocusControl(null);
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(4f);
    }

    private void DrawDetailPanel()
    {
        Rect detailRect = new Rect(SidebarWidth, 0f, position.width - SidebarWidth, position.height);
        EditorGUI.DrawRect(detailRect, new Color(0.16f, 0.16f, 0.16f));

        GUILayout.BeginArea(detailRect);
        GUILayout.Space(10f);
        EditorGUILayout.LabelField("Item Detail", EditorStyles.boldLabel);

        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EditorGUILayout.HelpBox("씬에서 ItemManager를 찾을 수 없습니다.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        List<ItemDefinition> definitions = GetDefinitions(itemManager);
        if (definitions.Count == 0)
        {
            EditorGUILayout.HelpBox("ItemDefinitions가 비어있습니다.", MessageType.Warning);
            GUILayout.EndArea();
            return;
        }

        EnsureSelection(definitions);

        ItemDefinition selectedDefinition = FindDefinitionById(definitions, selectedItemId);
        if (selectedDefinition == null)
        {
            EditorGUILayout.HelpBox("왼쪽 목록에서 아이템을 선택하세요.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        DrawSelectedItemHeader(selectedDefinition);
        GUILayout.Space(8f);

        if (EditorApplication.isPlaying)
        {
            showPlayModeDetailFields = EditorGUILayout.ToggleLeft("Show Detail Fields", showPlayModeDetailFields);
            if (!showPlayModeDetailFields)
            {
                EditorGUILayout.HelpBox("Play Mode에서는 프레임 드랍을 줄이기 위해 상세 편집 필드를 그리지 않습니다.", MessageType.Info);
                GUILayout.EndArea();
                return;
            }
        }

        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        DrawSelectedItemFields(selectedDefinition, definitions);
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawSelectedItemHeader(ItemDefinition definition)
    {
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(96f));
        Rect iconRect = GUILayoutUtility.GetRect(80f, 80f, GUILayout.ExpandWidth(false));
        DrawIconBackground(iconRect);
        DrawItemIcon(iconRect, definition);
        GUILayout.EndVertical();

        GUILayout.BeginVertical();
        EditorGUILayout.LabelField($"[{definition.id}] {GetDefinitionDisplayName(definition)}", EditorStyles.largeLabel);
        string assetPath = EditorApplication.isPlaying
            ? "(Asset Path skipped in Play Mode)"
            : AssetDatabase.GetAssetPath(definition);
        EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(assetPath) ? "(No Asset Path)" : assetPath, EditorStyles.miniLabel);
        GUILayout.Space(6f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Ping ItemDefinition", GUILayout.Width(140f)))
        {
            EditorGUIUtility.PingObject(definition);
            Selection.activeObject = definition;
        }

        if (definition.mapObject != null && GUILayout.Button("Ping MapObject", GUILayout.Width(120f)))
        {
            EditorGUIUtility.PingObject(definition.mapObject.gameObject);
            Selection.activeObject = definition.mapObject.gameObject;
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void DrawSelectedItemFields(ItemDefinition definition, List<ItemDefinition> definitions)
    {
        SerializedObject serializedObject = new SerializedObject(definition);
        serializedObject.Update();

        SerializedProperty itemNameProperty = serializedObject.FindProperty("itemName");
        SerializedProperty idProperty = serializedObject.FindProperty("id");
        SerializedProperty mapObjectProperty = serializedObject.FindProperty("mapObject");
        SerializedProperty portableMeshProperty = serializedObject.FindProperty("portableMesh");
        SerializedProperty portableMatProperty = serializedObject.FindProperty("portableMat");
        SerializedProperty iconProperty = serializedObject.FindProperty("icon");
        SerializedProperty interactionButtonListProperty = serializedObject.FindProperty("interactionButtonList");
        SerializedProperty sizeProperty = serializedObject.FindProperty("size");
        SerializedProperty itemFilterProperty = serializedObject.FindProperty("itemFilter");
        SerializedProperty capacityProperty = serializedObject.FindProperty("capacity");
        SerializedProperty storesFluidProperty = serializedObject.FindProperty("storesFluid");
        SerializedProperty fluidStorageLitersProperty = serializedObject.FindProperty("fluidStorageLiters");
        SerializedProperty fluidDisplayColorProperty = serializedObject.FindProperty("fluidDisplayColor");
        SerializedProperty energyTypeProperty = serializedObject.FindProperty("energyType");
        SerializedProperty energyAmountProperty = serializedObject.FindProperty("energyAmount");
        SerializedProperty useEnergyTypeProperty = serializedObject.FindProperty("useEnergyType");
        SerializedProperty useEnergyAmountProperty = serializedObject.FindProperty("useEnergyAmount");
        SerializedProperty completeEnergyProperty = serializedObject.FindProperty("completeEnergy");
        SerializedProperty utilityPoleConnectionRadiusProperty = serializedObject.FindProperty("utilityPoleConnectionRadius");
        SerializedProperty utilityPoleSupplyRadiusProperty = serializedObject.FindProperty("utilityPoleSupplyRadius");
        SerializedProperty craftingDurationSecondsProperty = serializedObject.FindProperty("craftingDurationSeconds");

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Basic", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.PropertyField(itemNameProperty, new GUIContent("Item Name"));
        EditorGUILayout.PropertyField(idProperty, new GUIContent("Id"));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.PropertyField(mapObjectProperty, new GUIContent("Map Object"));
        EditorGUILayout.PropertyField(portableMeshProperty, new GUIContent("Portable Mesh"));
        EditorGUILayout.PropertyField(portableMatProperty, new GUIContent("Portable Material"));
        EditorGUILayout.PropertyField(iconProperty, new GUIContent("Icon"));
        EditorGUI.EndDisabledGroup();
        DrawMapObjectFields(mapObjectProperty.objectReferenceValue as MapObject, definitions);

        if (interactionButtonListProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Interaction", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(interactionButtonListProperty, new GUIContent("Interaction Button List"), true);
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(sizeProperty, new GUIContent("Size"));
        if (itemFilterProperty != null)
        {
            EditorGUILayout.PropertyField(itemFilterProperty, new GUIContent("Item Filter"));
        }
        if (ShouldShowCapacity(definition) && capacityProperty != null)
        {
            if (capacityProperty.intValue <= 0)
            {
                capacityProperty.intValue = 10;
            }

            EditorGUILayout.PropertyField(capacityProperty, new GUIContent("Capacity"));
        }
        if (storesFluidProperty != null || fluidDisplayColorProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Fluid", EditorStyles.boldLabel);
            bool isFluidItem = InputOutputModule.IsFluidItemDefinition(definition);
            if (fluidDisplayColorProperty != null && isFluidItem)
            {
                EditorGUILayout.PropertyField(fluidDisplayColorProperty, new GUIContent("Pipe DP Color"));
            }

            if (storesFluidProperty != null)
            {
                EditorGUILayout.PropertyField(storesFluidProperty, new GUIContent("Store Fluid"));
                if (storesFluidProperty.boolValue)
                {
                    if (fluidStorageLitersProperty != null)
                    {
                        fluidStorageLitersProperty.floatValue = Mathf.Max(0f, fluidStorageLitersProperty.floatValue);
                        EditorGUILayout.PropertyField(fluidStorageLitersProperty, new GUIContent("Fluid Storage Liters"));
                    }
                }
                else if (fluidStorageLitersProperty != null)
                {
                    fluidStorageLitersProperty.floatValue = 0f;
                }
            }
        }
        if (craftingDurationSecondsProperty != null)
        {
            if (craftingDurationSecondsProperty.floatValue <= 0f)
            {
                craftingDurationSecondsProperty.floatValue = 5f;
            }

            craftingDurationSecondsProperty.floatValue = Mathf.Max(0.01f, craftingDurationSecondsProperty.floatValue);
            EditorGUILayout.PropertyField(craftingDurationSecondsProperty, new GUIContent("Crafting Time (sec)"));
        }

        if (energyTypeProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Energy", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(energyTypeProperty, new GUIContent("Energy Type"));

            ItemDefinition.EnergyType energyType = (ItemDefinition.EnergyType)energyTypeProperty.enumValueIndex;
            if (energyType == ItemDefinition.EnergyType.None)
            {
                if (energyAmountProperty != null)
                {
                    energyAmountProperty.longValue = 0;
                }
            }
            else if (energyAmountProperty != null)
            {
                EditorGUILayout.PropertyField(energyAmountProperty, new GUIContent("Energy Amount"));
            }
        }

        if (useEnergyTypeProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Use Energy", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useEnergyTypeProperty, new GUIContent("Use Energy Type"));

            ItemDefinition.EnergyType useEnergyType = (ItemDefinition.EnergyType)useEnergyTypeProperty.enumValueIndex;
            if (useEnergyType == ItemDefinition.EnergyType.None)
            {
                if (useEnergyAmountProperty != null)
                {
                    useEnergyAmountProperty.floatValue = 0f;
                }

                if (completeEnergyProperty != null)
                {
                    completeEnergyProperty.floatValue = 0f;
                }
            }
            else
            {
                if (useEnergyAmountProperty != null)
                {
                    string useEnergyAmountLabel = useEnergyType == ItemDefinition.EnergyType.Electricity
                        ? "Use Energy Amount (kW)"
                        : "Use Energy Amount / Sec";
                    EditorGUILayout.PropertyField(useEnergyAmountProperty, new GUIContent(useEnergyAmountLabel));
                }

                if (completeEnergyProperty != null)
                {
                    EditorGUILayout.PropertyField(completeEnergyProperty, new GUIContent("Complete Energy"));
                }
            }
        }

        if (definition.mapObject is UtilityPole
            && (utilityPoleConnectionRadiusProperty != null || utilityPoleSupplyRadiusProperty != null))
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Utility Pole", EditorStyles.boldLabel);
            if (utilityPoleConnectionRadiusProperty != null)
            {
                utilityPoleConnectionRadiusProperty.intValue = Mathf.Max(0, utilityPoleConnectionRadiusProperty.intValue);
                EditorGUILayout.PropertyField(
                    utilityPoleConnectionRadiusProperty,
                    new GUIContent("Connection Radius"));
            }

            if (utilityPoleSupplyRadiusProperty != null)
            {
                utilityPoleSupplyRadiusProperty.intValue = Mathf.Max(0, utilityPoleSupplyRadiusProperty.intValue);
                EditorGUILayout.PropertyField(
                    utilityPoleSupplyRadiusProperty,
                    new GUIContent("Supply Radius"));
            }
        }

        if (serializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(definition);
            InvalidateDefinitionPresentationCache();
            Repaint();
        }
    }

    private static bool ShouldShowCapacity(ItemDefinition definition)
    {
        if (definition == null || !(definition.mapObject is InstallationObject installationObject))
        {
            return false;
        }

        return (installationObject.MapFilter & InstallationMapFilter.ItemArea) != 0;
    }

    private static InstallationMapFilter NormalizeInstallationMapFilter(InstallationMapFilter filter)
    {
        return filter == InstallationMapFilter.None ? InstallationObject.DefaultMapFilter : filter;
    }

    private static bool TryParseInstallationMapFilter(string mapFilter, out InstallationMapFilter parsedFilter)
    {
        parsedFilter = InstallationMapFilter.None;
        if (string.IsNullOrWhiteSpace(mapFilter))
        {
            return false;
        }

        if (Enum.TryParse(mapFilter, true, out parsedFilter))
        {
            parsedFilter = NormalizeInstallationMapFilter(parsedFilter);
            return true;
        }

        string[] tokens = mapFilter.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        InstallationMapFilter combinedFilter = InstallationMapFilter.None;
        bool parsedAny = false;
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].Trim();
            if (token.Length <= 0)
            {
                continue;
            }

            if (string.Equals(token, "Resource", StringComparison.OrdinalIgnoreCase))
            {
                combinedFilter |= InstallationMapFilter.Ore;
                parsedAny = true;
                continue;
            }

            if (string.Equals(token, "WaterOutlne", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "WaterLOutline", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "WaterLandOutline", StringComparison.OrdinalIgnoreCase))
            {
                combinedFilter |= InstallationMapFilter.WaterOutline;
                parsedAny = true;
                continue;
            }

            if (!Enum.TryParse(token, true, out InstallationMapFilter tokenFilter))
            {
                parsedFilter = InstallationMapFilter.None;
                return false;
            }

            combinedFilter |= tokenFilter;
            parsedAny = true;
        }

        if (!parsedAny)
        {
            return false;
        }

        parsedFilter = NormalizeInstallationMapFilter(combinedFilter);
        return true;
    }

    private void DrawMapObjectFields(MapObject mapObject, List<ItemDefinition> definitions)
    {
        if (mapObject == null)
        {
            return;
        }

        SerializedObject mapObjectSerializedObject = new SerializedObject(mapObject);
        mapObjectSerializedObject.Update();

        SerializedProperty mapStatusProperty = mapObjectSerializedObject.FindProperty("mapStatus");
        if (mapStatusProperty == null)
        {
            return;
        }

        SerializedProperty mapSizeXProperty = mapStatusProperty.FindPropertyRelative("mapSizeX");
        SerializedProperty mapSizeYProperty = mapStatusProperty.FindPropertyRelative("mapSizeY");
        if (mapSizeXProperty == null || mapSizeYProperty == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Map Object", EditorStyles.boldLabel);
        Rect rowRect = EditorGUILayout.GetControlRect();
        Rect labelRect = EditorGUI.PrefixLabel(rowRect, new GUIContent("mapSize"));
        float spacing = 4f;
        float fieldWidth = 54f;
        Rect xRect = new Rect(labelRect.x, labelRect.y, fieldWidth, labelRect.height);
        Rect yRect = new Rect(labelRect.x + fieldWidth + spacing, labelRect.y, fieldWidth, labelRect.height);
        EditorGUI.PropertyField(xRect, mapSizeXProperty, GUIContent.none);
        EditorGUI.PropertyField(yRect, mapSizeYProperty, GUIContent.none);
        DrawPlacementCenterGridFields(mapObjectSerializedObject, mapStatusProperty, mapSizeXProperty, mapSizeYProperty, mapObject);

        SerializedProperty multiFocusModeProperty = mapObjectSerializedObject.FindProperty("multiFocusMode");
        if (multiFocusModeProperty != null)
        {
            DrawMultiFocusModeField(multiFocusModeProperty);
        }

        if (mapObject is WorkableObject)
        {
            DrawWorkableRangeCellsField(mapObjectSerializedObject);
        }
        else
        {
            SerializedProperty focusActivationRadiusProperty = GetMapObjectFocusRadiusProperty(mapObjectSerializedObject, mapObject);
            if (focusActivationRadiusProperty != null)
            {
                focusActivationRadiusProperty.floatValue = Mathf.Max(0f, focusActivationRadiusProperty.floatValue);
                EditorGUILayout.PropertyField(focusActivationRadiusProperty, new GUIContent("Focus Radius"));
            }
        }

        bool shouldSyncConveyorVariantSpeed = false;
        ConveyorBelt conveyorBeltForSpeed = ResolveConveyorBelt(mapObject);
        bool usesSeparateConveyorSerializedObject = conveyorBeltForSpeed != null && conveyorBeltForSpeed != mapObject;
        SerializedObject conveyorSerializedObject = usesSeparateConveyorSerializedObject
            ? new SerializedObject(conveyorBeltForSpeed)
            : mapObjectSerializedObject;
        if (conveyorBeltForSpeed != null)
        {
            if (usesSeparateConveyorSerializedObject)
            {
                conveyorSerializedObject.Update();
            }

            SerializedProperty conveyorSpeedProperty = FindSerializedProperty(conveyorSerializedObject, "conveyorSpeed");
            if (conveyorSpeedProperty != null)
            {
                conveyorSpeedProperty.floatValue = Mathf.Max(0f, conveyorSpeedProperty.floatValue);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(conveyorSpeedProperty, new GUIContent("Conveyor Speed"));
                shouldSyncConveyorVariantSpeed = EditorGUI.EndChangeCheck();
            }
        }

        if (mapObject is Pump)
        {
            DrawPumpFields(mapObjectSerializedObject);
        }

        if (ShouldExposeVehicleStats(mapObject))
        {
            DrawVehicleFields(mapObjectSerializedObject);
        }

        if (mapObject is InstallationObject)
        {
            SerializedProperty mapFilterProperty = mapObjectSerializedObject.FindProperty("mapFilter");
            if (mapFilterProperty != null)
            {
                InstallationMapFilter currentFilter = NormalizeInstallationMapFilter(
                    (InstallationMapFilter)mapFilterProperty.intValue);

                EditorGUI.BeginChangeCheck();
                InstallationMapFilter nextFilter = (InstallationMapFilter)EditorGUILayout.EnumFlagsField("Map Filter", currentFilter);
                nextFilter = NormalizeInstallationMapFilter(nextFilter);

                if (EditorGUI.EndChangeCheck())
                {
                    mapFilterProperty.intValue = (int)nextFilter;
                }
            }
        }

        if (mapObject is InputOutputModule)
        {
            DrawInputOutputModuleFields(mapObjectSerializedObject, definitions);
        }

        bool mapObjectApplied = mapObjectSerializedObject.ApplyModifiedProperties();
        bool conveyorObjectApplied = usesSeparateConveyorSerializedObject
            && conveyorSerializedObject.ApplyModifiedProperties();
        if (mapObjectApplied || conveyorObjectApplied)
        {
            if (shouldSyncConveyorVariantSpeed)
            {
                SyncConveyorVariantSpeed(conveyorBeltForSpeed);
            }

            if (mapObject is Wall fence)
            {
                SyncFenceVariantMultiFocusMode(fence);
            }

            EditorUtility.SetDirty(mapObject);
            GameObject owner = mapObject.gameObject;
            if (owner != null)
            {
                EditorUtility.SetDirty(owner);
            }

            if (conveyorObjectApplied && conveyorBeltForSpeed != null)
            {
                EditorUtility.SetDirty(conveyorBeltForSpeed);
                if (conveyorBeltForSpeed.gameObject != null)
                {
                    EditorUtility.SetDirty(conveyorBeltForSpeed.gameObject);
                }
            }

            Repaint();
        }
    }

    private static void DrawPumpFields(SerializedObject mapObjectSerializedObject)
    {
        if (mapObjectSerializedObject == null)
        {
            return;
        }

        SerializedProperty waterLitersPerSecondProperty = mapObjectSerializedObject.FindProperty("waterLitersPerSecond");
        if (waterLitersPerSecondProperty == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Pump", EditorStyles.miniBoldLabel);
        waterLitersPerSecondProperty.floatValue = Mathf.Max(0f, waterLitersPerSecondProperty.floatValue);
        EditorGUILayout.PropertyField(waterLitersPerSecondProperty, new GUIContent("Water Liters / s"));
    }

    private static void DrawVehicleFields(SerializedObject mapObjectSerializedObject)
    {
        if (mapObjectSerializedObject == null)
        {
            return;
        }

        SerializedProperty accelerationProperty = FindSerializedProperty(mapObjectSerializedObject, "vehicleAccelerationPerSecond");
        SerializedProperty maxSpeedProperty = FindSerializedProperty(mapObjectSerializedObject, "vehicleMaxSpeed");
        SerializedProperty stopInertiaProperty = FindSerializedProperty(mapObjectSerializedObject, "vehicleStopInertiaSeconds");
        if (accelerationProperty == null && maxSpeedProperty == null && stopInertiaProperty == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Vehicle", EditorStyles.miniBoldLabel);
        if (accelerationProperty != null)
        {
            accelerationProperty.floatValue = Mathf.Max(0.01f, accelerationProperty.floatValue);
            EditorGUILayout.PropertyField(accelerationProperty, new GUIContent("Acceleration / s"));
        }

        if (maxSpeedProperty != null)
        {
            maxSpeedProperty.floatValue = Mathf.Max(0.01f, maxSpeedProperty.floatValue);
            EditorGUILayout.PropertyField(maxSpeedProperty, new GUIContent("Max Speed"));
        }

        if (stopInertiaProperty != null)
        {
            stopInertiaProperty.floatValue = Mathf.Max(0f, stopInertiaProperty.floatValue);
            EditorGUILayout.PropertyField(stopInertiaProperty, new GUIContent("Stop Inertia (sec)"));
        }
    }

    private static bool ShouldExposeVehicleStats(MapObject mapObject)
    {
        return mapObject is Vehicle && !(mapObject is FreightCar);
    }

    private void DrawPlacementCenterGridFields(
        SerializedObject mapObjectSerializedObject,
        SerializedProperty mapStatusProperty,
        SerializedProperty mapSizeXProperty,
        SerializedProperty mapSizeYProperty,
        MapObject mapObject)
    {
        if (mapObjectSerializedObject == null
            || mapStatusProperty == null
            || mapSizeXProperty == null
            || mapSizeYProperty == null)
        {
            return;
        }

        SerializedProperty centerXProperty = mapStatusProperty.FindPropertyRelative("centerCellX");
        SerializedProperty centerYProperty = mapStatusProperty.FindPropertyRelative("centerCellY");
        if (centerXProperty == null || centerYProperty == null)
        {
            return;
        }

        int width = Mathf.Clamp(mapSizeXProperty.intValue, 1, byte.MaxValue);
        int height = Mathf.Clamp(mapSizeYProperty.intValue, 1, byte.MaxValue);
        mapSizeXProperty.intValue = width;
        mapSizeYProperty.intValue = height;

        int centerX = Mathf.Clamp(centerXProperty.intValue, 0, width - 1);
        int centerY = Mathf.Clamp(centerYProperty.intValue, 0, height - 1);
        centerXProperty.intValue = centerX;
        centerYProperty.intValue = centerY;

        if (width * height <= 1)
        {
            return;
        }

        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField($"Center Cell ({centerX}, {centerY})", EditorStyles.miniBoldLabel);
        DrawPlacementCenterGrid(mapObjectSerializedObject, mapObject, centerXProperty, centerYProperty, width, height);
    }

    private void DrawPlacementCenterGrid(
        SerializedObject mapObjectSerializedObject,
        MapObject mapObject,
        SerializedProperty centerXProperty,
        SerializedProperty centerYProperty,
        int width,
        int height)
    {
        float cellSize = PlacementCenterGridCellSize;
        float spacing = PlacementCenterGridCellSpacing;
        float previewWidth = width * cellSize + Mathf.Max(0, width - 1) * spacing;
        float previewHeight = height * cellSize + Mathf.Max(0, height - 1) * spacing;

        Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.ExpandWidth(false));
        EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f, 0.85f));

        Event current = Event.current;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float cellX = previewRect.x + x * (cellSize + spacing);
                float cellY = previewRect.y + y * (cellSize + spacing);
                Rect cellRect = new Rect(cellX, cellY, cellSize, cellSize);
                Vector2Int cell = new Vector2Int(x, height - 1 - y);
                bool isCenter = cell.x == centerXProperty.intValue && cell.y == centerYProperty.intValue;

                DrawPlacementCenterGridCell(cellRect, isCenter);
                if (isCenter)
                {
                    GUIStyle labelStyle = GetPlacementCenterLabelStyle();
                    labelStyle.fontSize = cellSize >= 30f ? 9 : 8;
                    GUI.Label(cellRect, cellSize >= 30f ? "Center" : "C", labelStyle);
                }

                if (current != null
                    && current.type == EventType.MouseDown
                    && current.button == 0
                    && cellRect.Contains(current.mousePosition))
                {
                    Undo.RecordObject(mapObject, "Set Placement Center");
                    centerXProperty.intValue = cell.x;
                    centerYProperty.intValue = cell.y;
                    MarkMapObjectDirty(mapObjectSerializedObject, mapObject);
                    current.Use();
                }
            }
        }
    }

    private static void DrawPlacementCenterGridCell(Rect rect, bool isCenter)
    {
        Color fillColor = isCenter
            ? new Color(0.22f, 0.58f, 0.94f, 1f)
            : new Color(0.28f, 0.28f, 0.28f, 1f);
        Color borderColor = isCenter
            ? new Color(0.75f, 0.92f, 1f, 1f)
            : new Color(0.55f, 0.55f, 0.55f, 1f);

        EditorGUI.DrawRect(rect, fillColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), borderColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), borderColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), borderColor);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), borderColor);
    }

    private void MarkMapObjectDirty(SerializedObject mapObjectSerializedObject, MapObject mapObject)
    {
        mapObjectSerializedObject?.ApplyModifiedProperties();
        EditorUtility.SetDirty(mapObject);
        if (mapObject != null && mapObject.gameObject != null)
        {
            EditorUtility.SetDirty(mapObject.gameObject);
        }

        Repaint();
    }

    private static ConveyorBelt ResolveConveyorBelt(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return null;
        }

        if (mapObject is ConveyorBelt conveyorBelt)
        {
            return conveyorBelt;
        }

        conveyorBelt = mapObject.GetComponent<ConveyorBelt>();
        if (conveyorBelt != null)
        {
            return conveyorBelt;
        }

        return mapObject.GetComponentInParent<ConveyorBelt>(true);
    }

    private static SerializedProperty FindSerializedProperty(SerializedObject serializedObject, string propertyName)
    {
        if (serializedObject == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        SerializedProperty directProperty = serializedObject.FindProperty(propertyName);
        if (directProperty != null)
        {
            return directProperty;
        }

        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.Next(enterChildren))
        {
            enterChildren = false;
            if (iterator.name == propertyName
                || string.Equals(iterator.propertyPath, propertyName, StringComparison.Ordinal)
                || iterator.propertyPath.EndsWith("." + propertyName, StringComparison.Ordinal))
            {
                return iterator.Copy();
            }
        }

        return null;
    }

    private static void SyncConveyorVariantSpeed(ConveyorBelt sourceConveyor)
    {
        if (sourceConveyor == null)
        {
            return;
        }

        float speed = sourceConveyor.ConveyorSpeed;
        HashSet<ConveyorBelt> visited = new HashSet<ConveyorBelt>();
        Stack<ConveyorBelt> pending = new Stack<ConveyorBelt>();
        pending.Push(sourceConveyor);

        while (pending.Count > 0)
        {
            ConveyorBelt conveyorBelt = pending.Pop();
            if (conveyorBelt == null || !visited.Add(conveyorBelt))
            {
                continue;
            }

            SetConveyorSpeed(conveyorBelt, speed, conveyorBelt != sourceConveyor);

            AddConveyorVariant(conveyorBelt.StraightVariantPrefab, pending, visited);
            AddConveyorVariant(conveyorBelt.CornerVariantPrefab, pending, visited);
            AddConveyorVariant(conveyorBelt.ReverseCornerVariantPrefab, pending, visited);
        }
    }

    private static void AddConveyorVariant(ConveyorBelt conveyorBelt, Stack<ConveyorBelt> pending, HashSet<ConveyorBelt> visited)
    {
        if (conveyorBelt != null && !visited.Contains(conveyorBelt))
        {
            pending.Push(conveyorBelt);
        }
    }

    private static void SetConveyorSpeed(ConveyorBelt conveyorBelt, float speed, bool recordUndo)
    {
        if (conveyorBelt == null)
        {
            return;
        }

        if (recordUndo)
        {
            Undo.RecordObject(conveyorBelt, "Sync Conveyor Variant Speed");
        }

        SerializedObject serializedConveyor = new SerializedObject(conveyorBelt);
        serializedConveyor.Update();
        SerializedProperty conveyorSpeedProperty = serializedConveyor.FindProperty("conveyorSpeed");
        if (conveyorSpeedProperty == null)
        {
            return;
        }

        conveyorSpeedProperty.floatValue = Mathf.Max(0f, speed);
        bool applied = recordUndo
            ? serializedConveyor.ApplyModifiedProperties()
            : serializedConveyor.ApplyModifiedPropertiesWithoutUndo();
        if (applied)
        {
            EditorUtility.SetDirty(conveyorBelt);
            if (conveyorBelt.gameObject != null)
            {
                EditorUtility.SetDirty(conveyorBelt.gameObject);
            }
        }
    }

    private void DrawInputOutputModuleFields(SerializedObject mapObjectSerializedObject, List<ItemDefinition> definitions)
    {
        if (mapObjectSerializedObject == null)
        {
            return;
        }

        SerializedProperty inputListProperty = mapObjectSerializedObject.FindProperty("inputList");
        SerializedProperty outputListProperty = mapObjectSerializedObject.FindProperty("outputList");
        SerializedProperty legacyOutputProperty = mapObjectSerializedObject.FindProperty("output");
        if (inputListProperty == null || outputListProperty == null)
        {
            return;
        }

        EnsureInputOutputPairArraySizes(inputListProperty, outputListProperty, legacyOutputProperty);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Input Output Module", EditorStyles.boldLabel);
        UnityEngine.Object targetObject = mapObjectSerializedObject.targetObject;
        int pairCount = inputListProperty.arraySize;
        string sectionFoldoutKey = GetInputOutputPairSectionFoldoutKey(targetObject);
        InitializeInputOutputPairFoldoutStates(sectionFoldoutKey, targetObject, pairCount);
        bool isSectionExpanded = string.IsNullOrEmpty(sectionFoldoutKey)
            || !collapsedInputOutputPairSectionKeys.Contains(sectionFoldoutKey);
        EditorGUILayout.BeginHorizontal();
        bool nextSectionExpanded = EditorGUILayout.Foldout(
            isSectionExpanded,
            $"Input / Output Pairs ({pairCount})",
            true,
            EditorStyles.foldout);
        if (nextSectionExpanded != isSectionExpanded)
        {
            SetInputOutputPairSectionCollapsedState(sectionFoldoutKey, !nextSectionExpanded);
            isSectionExpanded = nextSectionExpanded;
        }

        if (GUILayout.Button("Expand All", GUILayout.Width(76f)))
        {
            SetInputOutputPairSectionCollapsedState(sectionFoldoutKey, false);
            SetInputOutputPairCollapsedState(targetObject, pairCount, false);
            isSectionExpanded = true;
        }

        if (GUILayout.Button("Collapse All", GUILayout.Width(82f)))
        {
            SetInputOutputPairCollapsedState(targetObject, pairCount, true);
            SetInputOutputPairSectionCollapsedState(sectionFoldoutKey, true);
            isSectionExpanded = false;
        }

        EditorGUILayout.EndHorizontal();

        if (isSectionExpanded)
        {
            for (int i = 0; i < pairCount; i++)
            {
                SerializedProperty inputEntryProperty = inputListProperty.GetArrayElementAtIndex(i);
                SerializedProperty outputEntryProperty = outputListProperty.GetArrayElementAtIndex(i);
                DrawInputOutputPairRow(
                    inputEntryProperty,
                    outputEntryProperty,
                    definitions,
                    targetObject is ProductionMachine,
                    GetInputOutputPairFoldoutKey(targetObject, i),
                    i,
                    () =>
                {
                    inputListProperty.DeleteArrayElementAtIndex(i);
                    outputListProperty.DeleteArrayElementAtIndex(i);
                });
            }

            if (GUILayout.Button("Add Pair", GUILayout.Width(96f)))
            {
                int insertIndex = inputListProperty.arraySize;
                inputListProperty.InsertArrayElementAtIndex(insertIndex);
                ResetInputOutputEntry(inputListProperty.GetArrayElementAtIndex(insertIndex));

                outputListProperty.InsertArrayElementAtIndex(insertIndex);
                ResetInputOutputEntry(outputListProperty.GetArrayElementAtIndex(insertIndex));
            }
        }

        GUILayout.Space(8f);
        DrawInputOutputRectGridFields(mapObjectSerializedObject, pairCount);
    }

    private string GetInputOutputPairSectionFoldoutKey(UnityEngine.Object targetObject)
    {
        return $"{GetInputOutputTargetKey(targetObject)}/Pairs";
    }

    private string GetInputOutputPairFoldoutKey(UnityEngine.Object targetObject, int pairIndex)
    {
        return $"{GetInputOutputTargetKey(targetObject)}/Pair/{Mathf.Max(0, pairIndex)}";
    }

    private void InitializeInputOutputPairFoldoutStates(string sectionFoldoutKey, UnityEngine.Object targetObject, int pairCount)
    {
        bool shouldAutoCollapse = pairCount >= LargeInputOutputPairAutoCollapseThreshold;
        if (!string.IsNullOrEmpty(sectionFoldoutKey)
            && initializedInputOutputPairSectionKeys.Add(sectionFoldoutKey)
            && shouldAutoCollapse)
        {
            collapsedInputOutputPairSectionKeys.Add(sectionFoldoutKey);
        }

        if (!shouldAutoCollapse || targetObject == null)
        {
            return;
        }

        for (int i = 0; i < pairCount; i++)
        {
            string pairKey = GetInputOutputPairFoldoutKey(targetObject, i);
            if (!string.IsNullOrEmpty(pairKey) && initializedInputOutputPairKeys.Add(pairKey))
            {
                collapsedInputOutputPairKeys.Add(pairKey);
            }
        }
    }

    private string GetInputOutputTargetKey(UnityEngine.Object targetObject)
    {
        if (targetObject == null)
        {
            return "UnknownInputOutputModule";
        }

        int instanceId = targetObject.GetInstanceID();
        if (inputOutputTargetKeyCache.TryGetValue(instanceId, out string cachedKey)
            && !string.IsNullOrEmpty(cachedKey))
        {
            return cachedKey;
        }

        string key = GlobalObjectId.GetGlobalObjectIdSlow(targetObject).ToString();
        inputOutputTargetKeyCache[instanceId] = key;
        return key;
    }

    private void SetInputOutputPairSectionCollapsedState(string sectionFoldoutKey, bool collapsed)
    {
        if (string.IsNullOrEmpty(sectionFoldoutKey))
        {
            return;
        }

        if (collapsed)
        {
            collapsedInputOutputPairSectionKeys.Add(sectionFoldoutKey);
        }
        else
        {
            collapsedInputOutputPairSectionKeys.Remove(sectionFoldoutKey);
        }
    }

    private void SetInputOutputPairCollapsedState(UnityEngine.Object targetObject, int pairCount, bool collapsed)
    {
        for (int i = 0; i < pairCount; i++)
        {
            string key = GetInputOutputPairFoldoutKey(targetObject, i);
            if (collapsed)
            {
                collapsedInputOutputPairKeys.Add(key);
            }
            else
            {
                collapsedInputOutputPairKeys.Remove(key);
            }
        }
    }

    private string GetInputOutputSlotLayoutSectionFoldoutKey(UnityEngine.Object targetObject)
    {
        return $"{GetInputOutputTargetKey(targetObject)}/SlotLayout";
    }

    private void InitializeInputOutputSlotLayoutFoldoutState(string sectionFoldoutKey, int pairCount)
    {
        bool shouldAutoCollapse = pairCount >= LargeInputOutputPairAutoCollapseThreshold;
        if (!string.IsNullOrEmpty(sectionFoldoutKey)
            && initializedInputOutputSlotLayoutSectionKeys.Add(sectionFoldoutKey)
            && shouldAutoCollapse)
        {
            collapsedInputOutputSlotLayoutSectionKeys.Add(sectionFoldoutKey);
        }
    }

    private void SetInputOutputSlotLayoutSectionCollapsedState(string sectionFoldoutKey, bool collapsed)
    {
        if (string.IsNullOrEmpty(sectionFoldoutKey))
        {
            return;
        }

        if (collapsed)
        {
            collapsedInputOutputSlotLayoutSectionKeys.Add(sectionFoldoutKey);
        }
        else
        {
            collapsedInputOutputSlotLayoutSectionKeys.Remove(sectionFoldoutKey);
        }
    }

    private void DrawInputOutputRectGridFields(SerializedObject mapObjectSerializedObject, int pairCount)
    {
        if (mapObjectSerializedObject == null)
        {
            return;
        }

        InputOutputModule inputOutputModule = mapObjectSerializedObject.targetObject as InputOutputModule;
        if (inputOutputModule == null)
        {
            return;
        }

        string sectionFoldoutKey = GetInputOutputSlotLayoutSectionFoldoutKey(mapObjectSerializedObject.targetObject);
        InitializeInputOutputSlotLayoutFoldoutState(sectionFoldoutKey, pairCount);
        bool isSectionExpanded = string.IsNullOrEmpty(sectionFoldoutKey)
            || !collapsedInputOutputSlotLayoutSectionKeys.Contains(sectionFoldoutKey);
        bool nextSectionExpanded = EditorGUILayout.Foldout(
            isSectionExpanded,
            "Slot Layout",
            true,
            EditorStyles.foldout);
        if (nextSectionExpanded != isSectionExpanded)
        {
            SetInputOutputSlotLayoutSectionCollapsedState(sectionFoldoutKey, !nextSectionExpanded);
            isSectionExpanded = nextSectionExpanded;
        }

        if (!isSectionExpanded)
        {
            return;
        }

        SerializedProperty slotLayoutTypeProperty = mapObjectSerializedObject.FindProperty("slotLayoutType");
        SerializedProperty rectGridWidthProperty = mapObjectSerializedObject.FindProperty("rectGridWidth");
        SerializedProperty rectGridHeightProperty = mapObjectSerializedObject.FindProperty("rectGridHeight");
        SerializedProperty rectGridCellsProperty = mapObjectSerializedObject.FindProperty("rectGridCells");
        if (slotLayoutTypeProperty == null || rectGridWidthProperty == null || rectGridHeightProperty == null || rectGridCellsProperty == null)
        {
            return;
        }

        EditorGUILayout.PropertyField(slotLayoutTypeProperty, new GUIContent("Layout"));

        InputOutputModule.SlotLayoutType layoutType = (InputOutputModule.SlotLayoutType)slotLayoutTypeProperty.enumValueIndex;
        if (layoutType != InputOutputModule.SlotLayoutType.RectGrid)
        {
            return;
        }

        Rect rowRect = EditorGUILayout.GetControlRect();
        Rect fieldRect = EditorGUI.PrefixLabel(rowRect, new GUIContent("RectGrid"));
        float spacing = 4f;
        float fieldWidth = 44f;
        Rect widthRect = new Rect(fieldRect.x, fieldRect.y, fieldWidth, fieldRect.height);
        Rect multiplyRect = new Rect(widthRect.xMax + spacing, fieldRect.y, 16f, fieldRect.height);
        Rect heightRect = new Rect(multiplyRect.xMax + spacing, fieldRect.y, fieldWidth, fieldRect.height);
        rectGridWidthProperty.intValue = Mathf.Max(1, EditorGUI.IntField(widthRect, rectGridWidthProperty.intValue));
        EditorGUI.LabelField(multiplyRect, "x");
        rectGridHeightProperty.intValue = Mathf.Max(1, EditorGUI.IntField(heightRect, rectGridHeightProperty.intValue));

        if (GUILayout.Button("Rebuild RectGrid", GUILayout.Width(124f)))
        {
            mapObjectSerializedObject.ApplyModifiedProperties();
            inputOutputModule.ConfigureRectGrid(rectGridWidthProperty.intValue, rectGridHeightProperty.intValue);
            EditorUtility.SetDirty(inputOutputModule);
            if (inputOutputModule.gameObject != null)
            {
                EditorUtility.SetDirty(inputOutputModule.gameObject);
            }

            mapObjectSerializedObject.Update();
        }

        if (mapObjectSerializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(inputOutputModule);
            if (inputOutputModule.gameObject != null)
            {
                EditorUtility.SetDirty(inputOutputModule.gameObject);
            }
        }

        mapObjectSerializedObject.Update();
        rectGridCellsProperty = mapObjectSerializedObject.FindProperty("rectGridCells");
        EditorGUILayout.LabelField($"Cells: {rectGridCellsProperty.arraySize}", EditorStyles.miniLabel);
        DrawRectGridPreview(mapObjectSerializedObject, inputOutputModule, rectGridWidthProperty.intValue, rectGridHeightProperty.intValue);
    }

    private static SerializedProperty GetMapObjectFocusRadiusProperty(SerializedObject serializedMapObject, MapObject mapObject)
    {
        if (serializedMapObject == null || mapObject == null)
        {
            return null;
        }

        if (mapObject is WorkableObject)
        {
            return null;
        }

        if (mapObject is BoxObject)
        {
            return serializedMapObject.FindProperty("focusActivationRadius");
        }

        if (mapObject is InstallationObject)
        {
            return serializedMapObject.FindProperty("installationFocusRadius");
        }

        return null;
    }

    private static void ApplyVehicleJson(SerializedObject serializedMapObject, ItemDataJsonEntry entry)
    {
        if (serializedMapObject == null || entry == null)
        {
            return;
        }

        SerializedProperty accelerationProperty = FindSerializedProperty(serializedMapObject, "vehicleAccelerationPerSecond");
        if (accelerationProperty != null && entry.vehicleAccelerationPerSecond > 0f)
        {
            accelerationProperty.floatValue = Mathf.Max(0.01f, entry.vehicleAccelerationPerSecond);
        }

        SerializedProperty maxSpeedProperty = FindSerializedProperty(serializedMapObject, "vehicleMaxSpeed");
        if (maxSpeedProperty != null && entry.vehicleMaxSpeed > 0f)
        {
            maxSpeedProperty.floatValue = Mathf.Max(0.01f, entry.vehicleMaxSpeed);
        }

        SerializedProperty stopInertiaProperty = FindSerializedProperty(serializedMapObject, "vehicleStopInertiaSeconds");
        if (stopInertiaProperty != null && entry.vehicleStopInertiaSeconds >= 0f)
        {
            stopInertiaProperty.floatValue = Mathf.Max(0f, entry.vehicleStopInertiaSeconds);
        }
    }

    private static void DrawWorkableRangeCellsField(SerializedObject serializedMapObject)
    {
        if (serializedMapObject == null)
        {
            return;
        }

        SerializedProperty workableRangeCellsProperty = serializedMapObject.FindProperty("workableRangeCells");
        if (workableRangeCellsProperty == null)
        {
            return;
        }

        int currentRangeCells = Mathf.Max(0, workableRangeCellsProperty.intValue);
        EditorGUI.BeginChangeCheck();
        int nextRangeCells = Mathf.Max(0, EditorGUILayout.IntField("Workable Range Cells", currentRangeCells));
        if (EditorGUI.EndChangeCheck())
        {
            workableRangeCellsProperty.intValue = nextRangeCells;
        }
    }

    private void DrawRectGridPreview(SerializedObject mapObjectSerializedObject, InputOutputModule inputOutputModule, int width, int height)
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);

        float cellSize = RectGridCellSize;
        float spacing = RectGridCellSpacing;
        float previewWidth = width * cellSize + Mathf.Max(0, width - 1) * spacing;
        float previewHeight = height * cellSize + Mathf.Max(0, height - 1) * spacing;

        EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);
        Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.ExpandWidth(false));
        EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f, 0.85f));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float cellX = previewRect.x + x * (cellSize + spacing);
                float cellY = previewRect.y + y * (cellSize + spacing);
                Rect cellRect = new Rect(cellX, cellY, cellSize, cellSize);
                Vector2Int cell = new Vector2Int(x, height - 1 - y);
                InputOutputModule.RectGridBlockType blockType = inputOutputModule != null
                    ? inputOutputModule.GetRectGridBlockAt(cell.x, cell.y)
                    : InputOutputModule.RectGridBlockType.None;
                EditorGUI.DrawRect(cellRect, new Color(0.28f, 0.28f, 0.28f, 1f));
                EditorGUI.DrawRect(new Rect(cellRect.x, cellRect.y, cellRect.width, 1f), new Color(0.55f, 0.55f, 0.55f, 1f));
                EditorGUI.DrawRect(new Rect(cellRect.x, cellRect.yMax - 1f, cellRect.width, 1f), new Color(0.55f, 0.55f, 0.55f, 1f));
                EditorGUI.DrawRect(new Rect(cellRect.x, cellRect.y, 1f, cellRect.height), new Color(0.55f, 0.55f, 0.55f, 1f));
                EditorGUI.DrawRect(new Rect(cellRect.xMax - 1f, cellRect.y, 1f, cellRect.height), new Color(0.55f, 0.55f, 0.55f, 1f));

                if (blockType != InputOutputModule.RectGridBlockType.None)
                {
                    DrawPlacedRectGridBlock(cellRect, inputOutputModule, blockType, cell);
                    InputOutputRectGridBlockDragAndDropUtility.HandlePlacedBlockDrag(
                        cellRect,
                        blockType,
                        cell,
                        GetRectGridBlockLabel(blockType),
                        this);
                }

                HandleRectGridCellDrop(mapObjectSerializedObject, inputOutputModule, cellRect, cell);
            }
        }

        HandleRectGridRemoveDrop(mapObjectSerializedObject, inputOutputModule, previewRect);

        GUILayout.Space(8f);
        EditorGUILayout.LabelField("Blocks", EditorStyles.miniBoldLabel);
        for (int i = 0; i < RectGridPaletteEntries.Length; i++)
        {
            if (i % 4 == 0)
            {
                if (i > 0)
                {
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(spacing);
                }

                EditorGUILayout.BeginHorizontal();
            }

            DrawRectGridPaletteBlock(RectGridPaletteEntries[i], RectGridPaletteBlockWidth, cellSize);
            if (i % 4 < 3 && i < RectGridPaletteEntries.Length - 1)
            {
                GUILayout.Space(spacing);
            }
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRectGridPaletteBlock(RectGridPaletteEntry entry, float blockWidth, float blockHeight)
    {
        Rect blockRect = GUILayoutUtility.GetRect(blockWidth, blockHeight, GUILayout.Width(blockWidth), GUILayout.Height(blockHeight));
        EditorGUI.DrawRect(blockRect, entry.color);
        EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.y, blockRect.width, 1f), new Color(1f, 1f, 1f, 0.35f));
        EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.yMax - 1f, blockRect.width, 1f), new Color(0f, 0f, 0f, 0.35f));
        EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.y, 1f, blockRect.height), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(blockRect.xMax - 1f, blockRect.y, 1f, blockRect.height), new Color(0f, 0f, 0f, 0.35f));

        GUI.Label(blockRect, entry.displayLabel, GetRectGridPaletteLabelStyle());
        InputOutputRectGridBlockDragAndDropUtility.HandlePaletteBlockDrag(blockRect, entry.blockType, entry.label, this);
    }

    private void DrawPlacedRectGridBlock(
        Rect rect,
        InputOutputModule inputOutputModule,
        InputOutputModule.RectGridBlockType blockType,
        Vector2Int cell)
    {
        Color fillColor = GetRectGridBlockColor(blockType);
        Rect insetRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);
        EditorGUI.DrawRect(insetRect, fillColor);
        EditorGUI.DrawRect(new Rect(insetRect.x, insetRect.y, insetRect.width, 1f), new Color(1f, 1f, 1f, 0.35f));
        EditorGUI.DrawRect(new Rect(insetRect.x, insetRect.yMax - 1f, insetRect.width, 1f), new Color(0f, 0f, 0f, 0.35f));
        EditorGUI.DrawRect(new Rect(insetRect.x, insetRect.y, 1f, insetRect.height), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(insetRect.xMax - 1f, insetRect.y, 1f, insetRect.height), new Color(0f, 0f, 0f, 0.35f));

        GUI.Label(insetRect, GetRectGridBlockDisplayLabel(inputOutputModule, blockType, cell), GetRectGridBlockLabelStyle());
    }

    private void HandleRectGridCellDrop(
        SerializedObject mapObjectSerializedObject,
        InputOutputModule inputOutputModule,
        Rect cellRect,
        Vector2Int cell)
    {
        if (mapObjectSerializedObject == null || inputOutputModule == null
            || !InputOutputRectGridBlockDragAndDropUtility.TryGetDraggedBlockPayload(out InputOutputRectGridBlockDragPayload payload)
            || payload == null)
        {
            return;
        }

        Event current = Event.current;
        if (current == null || !cellRect.Contains(current.mousePosition))
        {
            return;
        }

        switch (current.type)
        {
            case EventType.DragUpdated:
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                current.Use();
                break;

            case EventType.DragPerform:
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                DragAndDrop.AcceptDrag();
                Undo.RecordObject(inputOutputModule, "Edit RectGrid Block");
                if (payload.hasSourceCell)
                {
                    inputOutputModule.MoveOrSwapRectGridBlock(payload.SourceCell, cell);
                }
                else
                {
                    inputOutputModule.SetRectGridBlock(cell.x, cell.y, payload.blockType);
                }

                MarkRectGridObjectDirty(mapObjectSerializedObject, inputOutputModule);
                current.Use();
                break;

            case EventType.Repaint:
                DrawRectGridDropHighlight(cellRect);
                break;
        }
    }

    private void HandleRectGridRemoveDrop(SerializedObject mapObjectSerializedObject, InputOutputModule inputOutputModule, Rect previewRect)
    {
        if (mapObjectSerializedObject == null || inputOutputModule == null
            || !InputOutputRectGridBlockDragAndDropUtility.TryGetDraggedBlockPayload(out InputOutputRectGridBlockDragPayload payload)
            || payload == null
            || !payload.hasSourceCell)
        {
            return;
        }

        Event current = Event.current;
        if (current == null || previewRect.Contains(current.mousePosition))
        {
            return;
        }

        switch (current.type)
        {
            case EventType.DragUpdated:
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                current.Use();
                break;

            case EventType.DragPerform:
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                DragAndDrop.AcceptDrag();
                Undo.RecordObject(inputOutputModule, "Remove RectGrid Block");
                inputOutputModule.RemoveRectGridBlockAt(payload.SourceCell.x, payload.SourceCell.y);
                MarkRectGridObjectDirty(mapObjectSerializedObject, inputOutputModule);
                current.Use();
                break;
        }
    }

    private void MarkRectGridObjectDirty(SerializedObject mapObjectSerializedObject, InputOutputModule inputOutputModule)
    {
        if (inputOutputModule == null)
        {
            return;
        }

        EditorUtility.SetDirty(inputOutputModule);
        if (inputOutputModule.gameObject != null)
        {
            EditorUtility.SetDirty(inputOutputModule.gameObject);
        }

        mapObjectSerializedObject?.Update();
        Repaint();
    }

    private static string GetRectGridBlockLabel(InputOutputModule.RectGridBlockType blockType)
    {
        RectGridPaletteEntry entry = GetRectGridPaletteEntry(blockType);
        return string.IsNullOrWhiteSpace(entry.label) ? blockType.ToString() : entry.label;
    }

    private static string GetRectGridBlockDisplayLabel(
        InputOutputModule inputOutputModule,
        InputOutputModule.RectGridBlockType blockType,
        Vector2Int cell)
    {
        if (InputOutputModule.IsInputItemBlockType(blockType))
        {
            int numberedIndex = GetInputItemBlockIndex(inputOutputModule, cell);
            return numberedIndex > 0
                ? $"Input\n{numberedIndex}"
                : "Input";
        }

        RectGridPaletteEntry entry = GetRectGridPaletteEntry(blockType);
        return string.IsNullOrWhiteSpace(entry.displayLabel) ? blockType.ToString() : entry.displayLabel;
    }

    private static int GetInputItemBlockIndex(InputOutputModule inputOutputModule, Vector2Int cell)
    {
        if (inputOutputModule == null)
        {
            return -1;
        }

        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements = inputOutputModule.RectGridPlacements;
        int index = 1;
        bool found = false;
        for (int i = 0; i < placements.Count; i++)
        {
            InputOutputModule.RectGridBlockPlacement placement = placements[i];
            if (!InputOutputModule.IsInputItemBlockType(placement.blockType))
            {
                continue;
            }

            if (placement.x == cell.x && placement.y == cell.y)
            {
                found = true;
                continue;
            }

            if (placement.y > cell.y || (placement.y == cell.y && placement.x < cell.x))
            {
                index++;
            }
        }

        return found ? index : -1;
    }

    private static Color GetRectGridBlockColor(InputOutputModule.RectGridBlockType blockType)
    {
        RectGridPaletteEntry entry = GetRectGridPaletteEntry(blockType);
        return entry.color.a > 0f ? entry.color : new Color(0.35f, 0.35f, 0.35f, 1f);
    }

    private static RectGridPaletteEntry GetRectGridPaletteEntry(InputOutputModule.RectGridBlockType blockType)
    {
        for (int i = 0; i < RectGridPaletteEntries.Length; i++)
        {
            if (RectGridPaletteEntries[i].blockType == blockType)
            {
                return RectGridPaletteEntries[i];
            }
        }

        return default;
    }

    private static void DrawRectGridDropHighlight(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.35f, 0.65f, 1f, 0.16f));
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), new Color(0.35f, 0.65f, 1f, 0.95f));
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), new Color(0.35f, 0.65f, 1f, 0.95f));
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), new Color(0.35f, 0.65f, 1f, 0.95f));
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), new Color(0.35f, 0.65f, 1f, 0.95f));
    }

    private static GUIStyle GetPlacementCenterLabelStyle()
    {
        if (placementCenterLabelStyle == null)
        {
            placementCenterLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
        }

        return placementCenterLabelStyle;
    }

    private static GUIStyle GetRectGridPaletteLabelStyle()
    {
        if (rectGridPaletteLabelStyle == null)
        {
            rectGridPaletteLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 9,
                padding = new RectOffset(2, 2, 1, 1),
                normal = { textColor = Color.white }
            };
        }

        return rectGridPaletteLabelStyle;
    }

    private static GUIStyle GetRectGridBlockLabelStyle()
    {
        if (rectGridBlockLabelStyle == null)
        {
            rectGridBlockLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 8,
                padding = new RectOffset(1, 1, 1, 1),
                normal = { textColor = Color.white }
            };
        }

        return rectGridBlockLabelStyle;
    }

    private void DrawInputOutputPairRow(
        SerializedProperty inputEntryProperty,
        SerializedProperty outputEntryProperty,
        List<ItemDefinition> definitions,
        bool preferCraftingTreeIngredients,
        string foldoutKey,
        int pairIndex,
        Action removeAction)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        bool isExpanded = string.IsNullOrEmpty(foldoutKey) || !collapsedInputOutputPairKeys.Contains(foldoutKey);
        string header = GetInputOutputPairHeader(
            inputEntryProperty,
            outputEntryProperty,
            definitions,
            preferCraftingTreeIngredients,
            pairIndex);
        bool nextExpanded = EditorGUILayout.Foldout(isExpanded, header, true, EditorStyles.foldout);
        if (nextExpanded != isExpanded && !string.IsNullOrEmpty(foldoutKey))
        {
            if (nextExpanded)
            {
                collapsedInputOutputPairKeys.Remove(foldoutKey);
            }
            else
            {
                collapsedInputOutputPairKeys.Add(foldoutKey);
            }

            isExpanded = nextExpanded;
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("X", GUILayout.Width(24f)) && removeAction != null)
        {
            removeAction.Invoke();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.EndHorizontal();
        if (isExpanded)
        {
            if (preferCraftingTreeIngredients
                && TryGetCraftingTreeIngredientSummary(outputEntryProperty, definitions, out string ingredientSummary))
            {
                DrawReadOnlyInputOutputSummary("Ingredients", ingredientSummary);
            }
            else
            {
                DrawInputOutputEntryFields(inputEntryProperty, definitions, "Input");
            }

            GUILayout.Space(4f);
            DrawInputOutputEntryFields(outputEntryProperty, definitions, "Output");
        }

        EditorGUILayout.EndVertical();
    }

    private string GetInputOutputPairHeader(
        SerializedProperty inputEntryProperty,
        SerializedProperty outputEntryProperty,
        List<ItemDefinition> definitions,
        bool preferCraftingTreeIngredients,
        int pairIndex)
    {
        string inputSummary = GetInputOutputEntrySummary(inputEntryProperty);
        if (preferCraftingTreeIngredients
            && TryGetCraftingTreeIngredientSummary(outputEntryProperty, definitions, out string ingredientSummary))
        {
            inputSummary = ingredientSummary;
        }

        return $"Pair {pairIndex + 1}: {inputSummary} -> {GetInputOutputEntrySummary(outputEntryProperty)}";
    }

    private static string GetInputOutputEntrySummary(SerializedProperty entryProperty)
    {
        if (entryProperty == null)
        {
            return "None";
        }

        SerializedProperty itemDefinitionProperty = entryProperty.FindPropertyRelative("itemDefinition");
        SerializedProperty countProperty = entryProperty.FindPropertyRelative("count");
        ItemDefinition definition = itemDefinitionProperty != null
            ? itemDefinitionProperty.objectReferenceValue as ItemDefinition
            : null;
        string itemName = definition != null ? GetDefinitionDisplayName(definition) : "None";
        int count = countProperty != null ? Mathf.Max(1, countProperty.intValue) : 1;
        return $"{itemName} x{count}";
    }

    private static void DrawReadOnlyInputOutputSummary(string label, string summary)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);
        EditorGUILayout.SelectableLabel(
            string.IsNullOrWhiteSpace(summary) ? "None" : summary,
            EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private bool TryGetCraftingTreeIngredientSummary(
        SerializedProperty outputEntryProperty,
        List<ItemDefinition> definitions,
        out string summary)
    {
        summary = string.Empty;
        if (outputEntryProperty == null)
        {
            return false;
        }

        SerializedProperty itemDefinitionProperty = outputEntryProperty.FindPropertyRelative("itemDefinition");
        ItemDefinition outputDefinition = itemDefinitionProperty != null
            ? itemDefinitionProperty.objectReferenceValue as ItemDefinition
            : null;
        if (outputDefinition == null || outputDefinition.id < 0)
        {
            return false;
        }

        EnsureCraftingTreeIngredientSummaryCacheVersion();
        if (cachedCraftingTreeIngredientSummaries.TryGetValue(outputDefinition.id, out summary))
        {
            return !string.IsNullOrWhiteSpace(summary);
        }

        if (!CraftingTreeRuntime.TryGetIngredients(outputDefinition.id, craftingTreeIngredientBuffer)
            || craftingTreeIngredientBuffer.Count <= 0)
        {
            cachedCraftingTreeIngredientSummaries[outputDefinition.id] = string.Empty;
            return false;
        }

        craftingTreeIngredientSummaryParts.Clear();
        for (int i = 0; i < craftingTreeIngredientBuffer.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = craftingTreeIngredientBuffer[i];
            ItemDefinition ingredientDefinition = FindDefinitionById(definitions, ingredient.itemId);
            string itemName = ingredientDefinition != null
                ? GetDefinitionDisplayName(ingredientDefinition)
                : $"Item {ingredient.itemId}";
            craftingTreeIngredientSummaryParts.Add($"{itemName} x{Mathf.Max(1, ingredient.count)}");
        }

        summary = string.Join(" + ", craftingTreeIngredientSummaryParts);
        cachedCraftingTreeIngredientSummaries[outputDefinition.id] = summary;
        return !string.IsNullOrWhiteSpace(summary);
    }

    private void EnsureCraftingTreeIngredientSummaryCacheVersion()
    {
        if (cachedCraftingTreeIngredientSummaryVersion == definitionsCacheVersion)
        {
            return;
        }

        cachedCraftingTreeIngredientSummaries.Clear();
        cachedCraftingTreeIngredientSummaryVersion = definitionsCacheVersion;
    }

    private void DrawInputOutputEntryFields(
        SerializedProperty entryProperty,
        List<ItemDefinition> definitions,
        string label)
    {
        if (entryProperty == null)
        {
            return;
        }

        SerializedProperty itemDefinitionProperty = entryProperty.FindPropertyRelative("itemDefinition");
        SerializedProperty countProperty = entryProperty.FindPropertyRelative("count");
        if (itemDefinitionProperty == null || countProperty == null)
        {
            return;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);

        ItemDefinition currentDefinition = itemDefinitionProperty.objectReferenceValue as ItemDefinition;
        ItemDefinition[] dropdownDefinitions = GetInputOutputDefinitionOptions(definitions);
        GUIContent[] dropdownOptions = GetInputOutputDefinitionOptionContents(definitions);
        int currentIndex = GetInputOutputDefinitionOptionIndex(currentDefinition);
        Rect popupRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup, GUILayout.ExpandWidth(true));
        int nextIndex = EditorGUI.Popup(popupRect, currentIndex, dropdownOptions);
        ItemDefinition nextDefinition = nextIndex > 0 && nextIndex < dropdownDefinitions.Length
            ? dropdownDefinitions[nextIndex]
            : null;
        if (ItemDefinitionDragAndDropUtility.HandleDropTarget(popupRect, this, out ItemDefinition droppedDefinition))
        {
            nextDefinition = droppedDefinition;
        }

        if (nextDefinition != currentDefinition)
        {
            itemDefinitionProperty.objectReferenceValue = nextDefinition;
            currentDefinition = nextDefinition;
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUIUtility.labelWidth);
        string countLabel = ItemDefinition.IsElectricityItemDefinition(currentDefinition)
            ? "Count (kW)"
            : "Count";
        int nextCount = EditorGUILayout.IntField(countLabel, Mathf.Max(1, countProperty.intValue));
        countProperty.intValue = Mathf.Max(1, nextCount);
        EditorGUILayout.EndHorizontal();

        if (currentDefinition != null)
        {
            DrawReferencedItemPreview(currentDefinition);
        }
    }

    private void DrawReferencedItemPreview(ItemDefinition definition)
    {
        if (definition == null)
        {
            return;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUIUtility.labelWidth);

        Rect iconRect = GUILayoutUtility.GetRect(24f, 24f, GUILayout.Width(24f), GUILayout.Height(24f));
        DrawIconBackground(iconRect);
        DrawItemIcon(iconRect, definition);

        EditorGUILayout.LabelField($"[{definition.id}] {GetDefinitionDisplayName(definition)}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private static void ResetInputOutputEntry(SerializedProperty entryProperty)
    {
        if (entryProperty == null)
        {
            return;
        }

        SerializedProperty itemDefinitionProperty = entryProperty.FindPropertyRelative("itemDefinition");
        SerializedProperty countProperty = entryProperty.FindPropertyRelative("count");
        if (itemDefinitionProperty != null)
        {
            itemDefinitionProperty.objectReferenceValue = null;
        }

        if (countProperty != null)
        {
            countProperty.intValue = 1;
        }
    }

    private static void EnsureInputOutputPairArraySizes(
        SerializedProperty inputListProperty,
        SerializedProperty outputListProperty,
        SerializedProperty legacyOutputProperty)
    {
        if (inputListProperty == null || outputListProperty == null)
        {
            return;
        }

        bool shouldMigrateLegacyOutput = outputListProperty.arraySize == 0
            && inputListProperty.arraySize > 0
            && legacyOutputProperty != null;

        while (outputListProperty.arraySize < inputListProperty.arraySize)
        {
            int insertIndex = outputListProperty.arraySize;
            outputListProperty.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty insertedProperty = outputListProperty.GetArrayElementAtIndex(insertIndex);

            if (shouldMigrateLegacyOutput)
            {
                CopyInputOutputEntry(legacyOutputProperty, insertedProperty);
            }
            else
            {
                ResetInputOutputEntry(insertedProperty);
            }
        }

        while (outputListProperty.arraySize > inputListProperty.arraySize)
        {
            outputListProperty.DeleteArrayElementAtIndex(outputListProperty.arraySize - 1);
        }

        if (shouldMigrateLegacyOutput)
        {
            ResetInputOutputEntry(legacyOutputProperty);
        }
    }

    private static void CopyInputOutputEntry(SerializedProperty sourceProperty, SerializedProperty targetProperty)
    {
        if (sourceProperty == null || targetProperty == null)
        {
            return;
        }

        SerializedProperty sourceDefinitionProperty = sourceProperty.FindPropertyRelative("itemDefinition");
        SerializedProperty sourceCountProperty = sourceProperty.FindPropertyRelative("count");
        SerializedProperty targetDefinitionProperty = targetProperty.FindPropertyRelative("itemDefinition");
        SerializedProperty targetCountProperty = targetProperty.FindPropertyRelative("count");
        if (sourceDefinitionProperty == null || sourceCountProperty == null || targetDefinitionProperty == null || targetCountProperty == null)
        {
            return;
        }

        targetDefinitionProperty.objectReferenceValue = sourceDefinitionProperty.objectReferenceValue;
        targetCountProperty.intValue = Mathf.Max(1, sourceCountProperty.intValue);
    }

    private ItemDefinition[] GetInputOutputDefinitionOptions(List<ItemDefinition> definitions)
    {
        EnsureInputOutputDefinitionOptionCache(definitions);
        return cachedInputOutputDefinitionOptions;
    }

    private GUIContent[] GetInputOutputDefinitionOptionContents(List<ItemDefinition> definitions)
    {
        EnsureInputOutputDefinitionOptionCache(definitions);
        return cachedInputOutputDefinitionOptionContents;
    }

    private void EnsureInputOutputDefinitionOptionCache(List<ItemDefinition> definitions)
    {
        if (cachedInputOutputDefinitionOptionsVersion == definitionsCacheVersion)
        {
            return;
        }

        cachedInputOutputDefinitionOptions = BuildInputOutputDefinitionOptions(definitions);
        cachedInputOutputDefinitionOptionContents =
            BuildInputOutputDefinitionOptionContents(cachedInputOutputDefinitionOptions);
        BuildInputOutputDefinitionOptionIndexes(
            cachedInputOutputDefinitionOptions,
            cachedInputOutputDefinitionOptionIndexes);
        cachedInputOutputDefinitionOptionsVersion = definitionsCacheVersion;
    }

    private static ItemDefinition[] BuildInputOutputDefinitionOptions(List<ItemDefinition> definitions)
    {
        int optionCount = definitions != null ? definitions.Count : 0;
        ItemDefinition[] results = new ItemDefinition[optionCount + 1];
        results[0] = null;

        if (definitions == null)
        {
            return results;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            results[i + 1] = definitions[i];
        }

        return results;
    }

    private static GUIContent[] BuildInputOutputDefinitionOptionContents(ItemDefinition[] definitions)
    {
        if (definitions == null || definitions.Length == 0)
        {
            return new[] { new GUIContent("(None)") };
        }

        GUIContent[] contents = new GUIContent[definitions.Length];
        contents[0] = new GUIContent("(None)");

        for (int i = 1; i < definitions.Length; i++)
        {
            ItemDefinition definition = definitions[i];
            string label = definition != null
                ? $"[{definition.id}] {GetDefinitionDisplayName(definition)}"
                : "(None)";
            contents[i] = new GUIContent(label);
        }

        return contents;
    }

    private static void BuildInputOutputDefinitionOptionIndexes(
        ItemDefinition[] options,
        Dictionary<int, int> optionIndexes)
    {
        optionIndexes.Clear();
        if (options == null)
        {
            return;
        }

        for (int i = 1; i < options.Length; i++)
        {
            ItemDefinition definition = options[i];
            if (definition != null)
            {
                optionIndexes[definition.GetInstanceID()] = i;
            }
        }
    }

    private int GetInputOutputDefinitionOptionIndex(ItemDefinition currentDefinition)
    {
        if (currentDefinition == null)
        {
            return 0;
        }

        return cachedInputOutputDefinitionOptionIndexes.TryGetValue(currentDefinition.GetInstanceID(), out int optionIndex)
            ? optionIndex
            : 0;
    }

    private void SaveItemData()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        InvalidateDefinitionCache();
        EnsureSelection();
        Repaint();
    }

    private void LoadItemData()
    {
        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EnsureSelection();
            Repaint();
            return;
        }

        List<ItemDefinition> definitions = GetDefinitions(itemManager);
        ItemDefinition selectedDefinition = FindDefinitionById(definitions, selectedItemId);
        ReloadAsset(selectedDefinition);

        if (selectedDefinition != null)
        {
            ReloadAsset(selectedDefinition.mapObject);
        }

        AssetDatabase.Refresh();
        InvalidateDefinitionCache();
        EnsureSelection(GetDefinitions(itemManager));
        Repaint();
    }

    private void RebuildItemData()
    {
        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EditorUtility.DisplayDialog("Item Data", "씬에서 ItemManager를 찾을 수 없습니다.", "OK");
            EnsureSelection();
            Repaint();
            return;
        }

        Undo.RecordObject(itemManager, "Rebuild Item Data");
        itemManager.RebuildItemDefinitionsFromAssets();
        itemManager.ApplyItemIdsToPrefabs();
        int productionMachineRecipeCount = ProductionMachineRecipeAutoFill.SyncProductionMachines(itemManager);
        EditorUtility.SetDirty(itemManager);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        InvalidateDefinitionCache();
        EnsureSelection(GetDefinitions(itemManager));
        ShowNotification(new GUIContent($"Item Data rebuilt. Production recipes: {productionMachineRecipeCount}"));
        Repaint();
    }

    private void CreateUiIconAtlas()
    {
        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EditorUtility.DisplayDialog("Item Data", "씬에서 ItemManager를 찾을 수 없습니다.", "OK");
            return;
        }

        List<ItemDefinition> definitions = GetDefinitions(itemManager);
        List<UnityEngine.Object> iconSprites = CollectUiIconSprites(definitions);
        if (iconSprites.Count == 0)
        {
            EditorUtility.DisplayDialog("Item Data", "Atlas에 넣을 UI 아이콘 Sprite가 없습니다.", "OK");
            return;
        }

        EnsureAssetFolder(UiIconAtlasFolder);

        SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(UiIconAtlasPath);
        bool created = false;
        if (atlas == null)
        {
            atlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(atlas, UiIconAtlasPath);
            created = true;
        }

        SyncSpriteAtlasPackables(atlas, iconSprites);
        ApplyUiIconAtlasSettings(atlas);

        EditorUtility.SetDirty(atlas);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(UiIconAtlasPath, ImportAssetOptions.ForceUpdate);
        SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = atlas;
        EditorGUIUtility.PingObject(atlas);
        ShowNotification(new GUIContent($"{(created ? "Created" : "Updated")} UI Icon Atlas ({iconSprites.Count})"));
        Repaint();
    }

    private void OpenUiIconAtlas()
    {
        SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(UiIconAtlasPath);
        if (atlas == null)
        {
            EditorUtility.DisplayDialog("Item Data", "UI Icon Atlas가 없습니다. Create UI Icon Atlas 버튼으로 먼저 생성하세요.", "OK");
            UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(UiIconAtlasFolder);
            if (folder != null)
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = folder;
                EditorGUIUtility.PingObject(folder);
            }

            return;
        }

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = atlas;
        EditorGUIUtility.PingObject(atlas);
        ShowNotification(new GUIContent("UI Icon Atlas selected."));
    }

    private static List<UnityEngine.Object> CollectUiIconSprites(List<ItemDefinition> definitions)
    {
        List<UnityEngine.Object> sprites = new List<UnityEngine.Object>();
        HashSet<Sprite> visitedSprites = new HashSet<Sprite>();
        if (definitions == null)
        {
            return sprites;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            AddUiIconSprite(definition.icon, sprites, visitedSprites);

            List<Sprite> interactionSprites = definition.interactionButtonList;
            if (interactionSprites == null)
            {
                continue;
            }

            for (int spriteIndex = 0; spriteIndex < interactionSprites.Count; spriteIndex++)
            {
                AddUiIconSprite(interactionSprites[spriteIndex], sprites, visitedSprites);
            }
        }

        return sprites;
    }

    private static void AddUiIconSprite(Sprite sprite, List<UnityEngine.Object> sprites, HashSet<Sprite> visitedSprites)
    {
        if (sprite == null || sprites == null || visitedSprites == null || !visitedSprites.Add(sprite))
        {
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(sprite);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        sprites.Add(sprite);
    }

    private static void SyncSpriteAtlasPackables(SpriteAtlas atlas, List<UnityEngine.Object> sprites)
    {
        if (atlas == null)
        {
            return;
        }

        UnityEngine.Object[] currentPackables = SpriteAtlasExtensions.GetPackables(atlas);
        if (currentPackables != null && currentPackables.Length > 0)
        {
            SpriteAtlasExtensions.Remove(atlas, currentPackables);
        }

        if (sprites != null && sprites.Count > 0)
        {
            SpriteAtlasExtensions.Add(atlas, sprites.ToArray());
        }
    }

    private static void ApplyUiIconAtlasSettings(SpriteAtlas atlas)
    {
        if (atlas == null)
        {
            return;
        }

        SpriteAtlasPackingSettings packingSettings = new SpriteAtlasPackingSettings
        {
            enableRotation = false,
            enableTightPacking = false,
            padding = 4
        };
        SpriteAtlasExtensions.SetPackingSettings(atlas, packingSettings);

        SpriteAtlasTextureSettings textureSettings = new SpriteAtlasTextureSettings
        {
            readable = false,
            generateMipMaps = false,
            sRGB = true,
            filterMode = FilterMode.Bilinear
        };
        SpriteAtlasExtensions.SetTextureSettings(atlas, textureSettings);
    }

    private static void EnsureAssetFolder(string assetFolder)
    {
        if (string.IsNullOrWhiteSpace(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
        {
            return;
        }

        string[] parts = assetFolder.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
        {
            return;
        }

        string currentPath = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = currentPath + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, parts[i]);
            }

            currentPath = nextPath;
        }
    }

    private void ExportJson()
    {
        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EditorUtility.DisplayDialog("Item Data", "씬에서 ItemManager를 찾을 수 없습니다.", "OK");
            return;
        }

        List<ItemDefinition> definitions = GetDefinitions(itemManager);
        if (definitions.Count == 0)
        {
            EditorUtility.DisplayDialog("Item Data", "내보낼 ItemDefinition이 없습니다.", "OK");
            return;
        }

        string defaultPath = Path.Combine(Application.dataPath, "Data", "Items", "item_data.json");
        string exportPath = EditorUtility.SaveFilePanel("Export Item Data JSON", Path.GetDirectoryName(defaultPath), Path.GetFileNameWithoutExtension(defaultPath), "json");
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            return;
        }

        ItemDataJsonFile file = new ItemDataJsonFile();
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            ItemDataJsonEntry entry = BuildJsonEntry(definition);
            file.items.Add(entry);
        }

        File.WriteAllText(exportPath, JsonUtility.ToJson(file, true));
        AssetDatabase.Refresh();
    }

    private void LoadJson()
    {
        string importPath = EditorUtility.OpenFilePanel("Load Item Data JSON", Application.dataPath, "json");
        if (string.IsNullOrWhiteSpace(importPath) || !File.Exists(importPath))
        {
            return;
        }

        string json = File.ReadAllText(importPath);
        if (string.IsNullOrWhiteSpace(json))
        {
            EditorUtility.DisplayDialog("Item Data", "JSON 파일이 비어 있습니다.", "OK");
            return;
        }

        ItemDataJsonFile file = JsonUtility.FromJson<ItemDataJsonFile>(json);
        List<ItemDataJsonEntry> entries = GetJsonEntries(file);
        if (entries.Count == 0)
        {
            EditorUtility.DisplayDialog("Item Data", "불러올 아이템 데이터가 없습니다.", "OK");
            return;
        }

        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EditorUtility.DisplayDialog("Item Data", "씬에서 ItemManager를 찾을 수 없습니다.", "OK");
            return;
        }

        List<ItemDefinition> definitions = GetDefinitions(itemManager);
        int appliedCount = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ItemDefinition definition = ResolveDefinitionReference(definitions, entries[i]);
            if (definition == null)
            {
                continue;
            }

            ApplyJsonEntry(definition, entries[i], definitions);
            appliedCount++;
        }

        if (appliedCount > 0)
        {
            SortDefinitionsById(definitions);
            ApplyDefinitionOrderToItemManager(itemManager, definitions);
            SyncItemManagerItemSets(itemManager, definitions);
            itemManager.ApplyItemIdsToPrefabs();
            EditorUtility.SetDirty(itemManager);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        InvalidateDefinitionCache();
        EnsureSelection(GetDefinitions(itemManager));
        Repaint();

        EditorUtility.DisplayDialog("Item Data", $"{appliedCount}개 아이템 데이터를 불러왔습니다.", "OK");
    }

    private static void ReloadAsset(UnityEngine.Object targetObject)
    {
        if (targetObject == null)
        {
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(targetObject);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
    }

    private static ItemDataJsonEntry BuildJsonEntry(ItemDefinition definition)
    {
        ItemDataJsonEntry entry = new ItemDataJsonEntry
        {
            id = definition.id,
            itemName = definition.itemName,
            definitionAssetPath = AssetDatabase.GetAssetPath(definition),
            portableMeshAssetPath = AssetDatabase.GetAssetPath(definition.portableMesh),
            portableMaterialAssetPath = AssetDatabase.GetAssetPath(definition.portableMat),
            iconAssetPath = AssetDatabase.GetAssetPath(definition.icon),
            size = Mathf.Max(0, (int)definition.size),
            itemFilter = definition.itemFilter,
            capacity = definition.capacity > 0 ? definition.capacity : 10,
            storesFluid = definition.storesFluid,
            fluidStorageLiters = definition.storesFluid ? Mathf.Max(0f, definition.fluidStorageLiters) : 0f,
            hasFluidDisplayColor = InputOutputModule.IsFluidItemDefinition(definition),
            fluidDisplayColor = definition.fluidDisplayColor,
            craftingDurationSeconds = definition.CraftingDurationSeconds,
            energyType = definition.energyType.ToString(),
            energyTypeValue = (int)definition.energyType,
            energyAmount = Mathf.Max(0, definition.energyAmount),
            useEnergyType = definition.useEnergyType.ToString(),
            useEnergyTypeValue = (int)definition.useEnergyType,
            useEnergyAmount = Mathf.Max(0f, definition.useEnergyAmount),
            completeEnergy = Mathf.Max(0f, definition.completeEnergy),
            utilityPoleConnectionRadius = definition.mapObject is UtilityPole
                ? Mathf.Max(0, definition.utilityPoleConnectionRadius)
                : -1,
            utilityPoleSupplyRadius = definition.mapObject is UtilityPole
                ? Mathf.Max(0, definition.utilityPoleSupplyRadius)
                : -1
        };

        if (definition.interactionButtonList != null && definition.interactionButtonList.Count > 0)
        {
            entry.interactionButtonAssetPaths = new List<string>(definition.interactionButtonList.Count);
            for (int i = 0; i < definition.interactionButtonList.Count; i++)
            {
                entry.interactionButtonAssetPaths.Add(AssetDatabase.GetAssetPath(definition.interactionButtonList[i]));
            }
        }

        if (definition.mapObject != null)
        {
            GameObject prefabRoot = definition.mapObject.transform.root != null
                ? definition.mapObject.transform.root.gameObject
                : definition.mapObject.gameObject;
            entry.mapObjectAssetPath = AssetDatabase.GetAssetPath(prefabRoot);
            entry.mapSizeX = definition.mapObject.Status.mapSizeX;
            entry.mapSizeY = definition.mapObject.Status.mapSizeY;
            Vector2Int placementCenterCell = definition.mapObject.PlacementCenterCell;
            entry.placementCenterX = placementCenterCell.x;
            entry.placementCenterY = placementCenterCell.y;
            entry.multiFocusMode = definition.mapObject.FocusMode.ToString();
            entry.multiFocusModeValue = (int)definition.mapObject.FocusMode;
            if (definition.mapObject is WorkableObject workableObject)
            {
                entry.focusRadius = workableObject.FocusActivationRadius;
                entry.workableRangeCells = (int)workableObject.WorkableRangeCells;
            }
            else if (definition.mapObject is BoxObject boxObject)
            {
                entry.focusRadius = boxObject.FocusActivationRadius;
            }
            else if (definition.mapObject is InstallationObject installationObjectWithFocus)
            {
                entry.focusRadius = installationObjectWithFocus.FocusActivationRadius;
            }

            ConveyorBelt conveyorBelt = ResolveConveyorBelt(definition.mapObject);
            if (conveyorBelt != null)
            {
                entry.conveyorSpeed = conveyorBelt.ConveyorSpeed;
            }

            if (definition.mapObject is Pump pump)
            {
                entry.waterLitersPerSecond = pump.WaterLitersPerSecond;
            }

            if (ShouldExposeVehicleStats(definition.mapObject) && definition.mapObject is Vehicle vehicle)
            {
                entry.vehicleAccelerationPerSecond = vehicle.VehicleAccelerationPerSecond;
                entry.vehicleMaxSpeed = vehicle.VehicleMaxSpeed;
                entry.vehicleStopInertiaSeconds = vehicle.VehicleStopInertiaSeconds;
            }

            if (definition.mapObject is InstallationObject installationObject)
            {
                entry.mapFilter = installationObject.MapFilter.ToString();
                entry.mapFilterValue = (int)installationObject.MapFilter;
            }

            if (definition.mapObject is InputOutputModule inputOutputModule)
            {
                entry.inputOutputLayoutType = inputOutputModule.LayoutType.ToString();
                entry.rectGridWidth = inputOutputModule.RectGridWidth;
                entry.rectGridHeight = inputOutputModule.RectGridHeight;

                IReadOnlyList<InputOutputModule.RectGridBlockPlacement> rectGridPlacements = inputOutputModule.RectGridPlacements;
                for (int i = 0; i < rectGridPlacements.Count; i++)
                {
                    entry.rectGridBlocks.Add(BuildRectGridBlockPlacementJsonEntry(rectGridPlacements[i]));
                }

                IReadOnlyList<InputOutputModule.ItemIoEntry> inputs = inputOutputModule.InputList;
                IReadOnlyList<InputOutputModule.ItemIoEntry> outputs = inputOutputModule.OutputList;
                int pairCount = Mathf.Min(inputs.Count, outputs.Count);
                for (int i = 0; i < pairCount; i++)
                {
                    InputOutputJsonEntry inputJsonEntry = BuildInputOutputJsonEntry(inputs[i]);
                    InputOutputJsonEntry outputJsonEntry = BuildInputOutputJsonEntry(outputs[i]);
                    entry.ioPairs.Add(new InputOutputPairJsonEntry
                    {
                        input = inputJsonEntry,
                        output = outputJsonEntry
                    });
                }
            }
        }

        return entry;
    }

    private static RectGridBlockPlacementJsonEntry BuildRectGridBlockPlacementJsonEntry(InputOutputModule.RectGridBlockPlacement placement)
    {
        return new RectGridBlockPlacementJsonEntry
        {
            x = placement.x,
            y = placement.y,
            blockType = placement.blockType.ToString()
        };
    }

    private static InputOutputJsonEntry BuildInputOutputJsonEntry(InputOutputModule.ItemIoEntry entry)
    {
        ItemDefinition definition = entry.itemDefinition;
        return new InputOutputJsonEntry
        {
            id = definition != null ? definition.id : -1,
            itemName = definition != null ? definition.itemName : string.Empty,
            definitionAssetPath = definition != null ? AssetDatabase.GetAssetPath(definition) : string.Empty,
            count = Mathf.Max(1, entry.count)
        };
    }

    private static List<ItemDataJsonEntry> GetJsonEntries(ItemDataJsonFile file)
    {
        if (file == null)
        {
            return new List<ItemDataJsonEntry>();
        }

        if (file.items != null && file.items.Count > 0)
        {
            return file.items;
        }

        return new List<ItemDataJsonEntry>();
    }

    private static void SortDefinitionsById(List<ItemDefinition> definitions)
    {
        if (definitions == null || definitions.Count <= 1)
        {
            return;
        }

        definitions.Sort((left, right) =>
        {
            if (left == null && right == null)
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

            int idCompare = left.id.CompareTo(right.id);
            if (idCompare != 0)
            {
                return idCompare;
            }

            return string.Compare(GetDefinitionDisplayName(left), GetDefinitionDisplayName(right), StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void ApplyJsonEntry(ItemDefinition definition, ItemDataJsonEntry entry, List<ItemDefinition> definitions)
    {
        if (definition == null || entry == null)
        {
            return;
        }

        Undo.RecordObject(definition, "Load Item Data JSON");
        if (!string.IsNullOrWhiteSpace(entry.itemName))
        {
            definition.itemName = entry.itemName;
        }

        if (entry.id >= 0)
        {
            definition.id = entry.id;
        }

        definition.size = (uint)Mathf.Max(0, entry.size);
        definition.itemFilter = entry.itemFilter;
        if (entry.capacity > 0)
        {
            definition.capacity = Mathf.Max(1, entry.capacity);
        }
        definition.storesFluid = entry.storesFluid;
        definition.fluidStorageLiters = entry.storesFluid ? Mathf.Max(0f, entry.fluidStorageLiters) : 0f;
        if (entry.hasFluidDisplayColor)
        {
            definition.fluidDisplayColor = entry.fluidDisplayColor;
        }
        if (entry.craftingDurationSeconds > 0f)
        {
            definition.SetCraftingDurationSeconds(entry.craftingDurationSeconds);
        }
        definition.energyType = ParseEnergyType(entry.energyType, entry.energyTypeValue, definition.energyType);
        definition.energyAmount = definition.energyType == ItemDefinition.EnergyType.None ? 0 : Mathf.Max(0, entry.energyAmount);
        definition.useEnergyType = ParseEnergyType(entry.useEnergyType, entry.useEnergyTypeValue, definition.useEnergyType);
        definition.useEnergyAmount = definition.useEnergyType == ItemDefinition.EnergyType.None ? 0f : Mathf.Max(0f, entry.useEnergyAmount);
        definition.completeEnergy = definition.useEnergyType == ItemDefinition.EnergyType.None ? 0f : Mathf.Max(0f, entry.completeEnergy);
        if (entry.utilityPoleConnectionRadius >= 0)
        {
            definition.utilityPoleConnectionRadius = Mathf.Max(0, entry.utilityPoleConnectionRadius);
        }

        if (entry.utilityPoleSupplyRadius >= 0)
        {
            definition.utilityPoleSupplyRadius = Mathf.Max(0, entry.utilityPoleSupplyRadius);
        }

        Mesh portableMesh = LoadAssetAtPath<Mesh>(entry.portableMeshAssetPath);
        if (portableMesh != null)
        {
            definition.portableMesh = portableMesh;
        }

        Material portableMaterial = LoadAssetAtPath<Material>(entry.portableMaterialAssetPath);
        if (portableMaterial != null)
        {
            definition.portableMat = portableMaterial;
        }

        Sprite icon = LoadAssetAtPath<Sprite>(entry.iconAssetPath);
        if (icon != null)
        {
            definition.icon = icon;
        }

        if (entry.interactionButtonAssetPaths != null)
        {
            if (definition.interactionButtonList == null)
            {
                definition.interactionButtonList = new List<Sprite>();
            }

            definition.interactionButtonList.Clear();
            for (int i = 0; i < entry.interactionButtonAssetPaths.Count; i++)
            {
                definition.interactionButtonList.Add(LoadAssetAtPath<Sprite>(entry.interactionButtonAssetPaths[i]));
            }
        }

        MapObject mapObject = LoadMapObject(entry.mapObjectAssetPath);
        if (mapObject != null)
        {
            definition.mapObject = mapObject;
        }

        ApplyMapObjectJson(definition.mapObject, entry, definition, definitions);
        EditorUtility.SetDirty(definition);
    }

    private static void ApplyMapObjectJson(MapObject mapObject, ItemDataJsonEntry entry, ItemDefinition definition, List<ItemDefinition> definitions)
    {
        if (mapObject == null || entry == null)
        {
            return;
        }

        SerializedObject serializedMapObject = new SerializedObject(mapObject);
        serializedMapObject.Update();

        SerializedProperty objIdProperty = serializedMapObject.FindProperty("objId");
        if (objIdProperty != null && definition != null)
        {
            objIdProperty.intValue = definition.id;
        }

        SerializedProperty itemDefinitionProperty = serializedMapObject.FindProperty("itemDefinition");
        if (itemDefinitionProperty != null && definition != null)
        {
            itemDefinitionProperty.objectReferenceValue = definition;
        }

        SerializedProperty mapStatusProperty = serializedMapObject.FindProperty("mapStatus");
        if (mapStatusProperty != null)
        {
            SerializedProperty mapSizeXProperty = mapStatusProperty.FindPropertyRelative("mapSizeX");
            SerializedProperty mapSizeYProperty = mapStatusProperty.FindPropertyRelative("mapSizeY");
            if (mapSizeXProperty != null && entry.mapSizeX > 0)
            {
                mapSizeXProperty.intValue = Mathf.Clamp(entry.mapSizeX, 1, byte.MaxValue);
            }

            if (mapSizeYProperty != null && entry.mapSizeY > 0)
            {
                mapSizeYProperty.intValue = Mathf.Clamp(entry.mapSizeY, 1, byte.MaxValue);
            }

            SerializedProperty centerXProperty = mapStatusProperty.FindPropertyRelative("centerCellX");
            SerializedProperty centerYProperty = mapStatusProperty.FindPropertyRelative("centerCellY");
            int mapSizeX = mapSizeXProperty != null ? Mathf.Max(1, mapSizeXProperty.intValue) : Mathf.Max(1, entry.mapSizeX);
            int mapSizeY = mapSizeYProperty != null ? Mathf.Max(1, mapSizeYProperty.intValue) : Mathf.Max(1, entry.mapSizeY);
            if (centerXProperty != null && entry.placementCenterX >= 0)
            {
                centerXProperty.intValue = Mathf.Clamp(entry.placementCenterX, 0, mapSizeX - 1);
            }

            if (centerYProperty != null && entry.placementCenterY >= 0)
            {
                centerYProperty.intValue = Mathf.Clamp(entry.placementCenterY, 0, mapSizeY - 1);
            }
        }

        SerializedProperty multiFocusModeProperty = serializedMapObject.FindProperty("multiFocusMode");
        if (multiFocusModeProperty != null)
        {
            if (!string.IsNullOrWhiteSpace(entry.multiFocusMode)
                && Enum.TryParse(entry.multiFocusMode, true, out MapObject.MultiFocusMode parsedMultiFocusMode))
            {
                multiFocusModeProperty.intValue = (int)parsedMultiFocusMode;
            }
            else if (entry.multiFocusModeValue >= 0)
            {
                multiFocusModeProperty.intValue = entry.multiFocusModeValue;
            }
        }

        if (mapObject is InstallationObject)
        {
            SerializedProperty mapFilterProperty = serializedMapObject.FindProperty("mapFilter");
            if (mapFilterProperty != null)
            {
                if (!string.IsNullOrWhiteSpace(entry.mapFilter)
                    && TryParseInstallationMapFilter(entry.mapFilter, out InstallationMapFilter parsedFilter))
                {
                    mapFilterProperty.intValue = (int)parsedFilter;
                }
                else if (entry.mapFilterValue >= 0)
                {
                    InstallationMapFilter parsedFilterValue = (InstallationMapFilter)entry.mapFilterValue;
                    mapFilterProperty.intValue = (int)NormalizeInstallationMapFilter(parsedFilterValue);
                }
            }
        }

        if (mapObject is WorkableObject)
        {
            SerializedProperty workableRangeCellsProperty = serializedMapObject.FindProperty("workableRangeCells");
            if (workableRangeCellsProperty != null)
            {
                if (entry.workableRangeCells >= 0)
                {
                    workableRangeCellsProperty.intValue = Mathf.Max(0, entry.workableRangeCells);
                }
            }
        }
        else
        {
            if (entry.focusRadius >= 0f)
            {
                SerializedProperty focusActivationRadiusProperty = GetMapObjectFocusRadiusProperty(serializedMapObject, mapObject);
                if (focusActivationRadiusProperty != null)
                {
                    focusActivationRadiusProperty.floatValue = Mathf.Max(0f, entry.focusRadius);
                }
            }
        }

        bool shouldSyncConveyorVariantSpeed = false;
        ConveyorBelt conveyorBelt = ResolveConveyorBelt(mapObject);
        bool usesSeparateConveyorSerializedObject = conveyorBelt != null && conveyorBelt != mapObject;
        SerializedObject serializedConveyor = usesSeparateConveyorSerializedObject
            ? new SerializedObject(conveyorBelt)
            : serializedMapObject;
        if (entry.conveyorSpeed >= 0f && conveyorBelt != null)
        {
            if (usesSeparateConveyorSerializedObject)
            {
                serializedConveyor.Update();
            }

            SerializedProperty conveyorSpeedProperty = FindSerializedProperty(serializedConveyor, "conveyorSpeed");
            if (conveyorSpeedProperty != null)
            {
                conveyorSpeedProperty.floatValue = Mathf.Max(0f, entry.conveyorSpeed);
                shouldSyncConveyorVariantSpeed = true;
            }
        }

        if (entry.waterLitersPerSecond >= 0f && mapObject is Pump)
        {
            SerializedProperty waterLitersPerSecondProperty = serializedMapObject.FindProperty("waterLitersPerSecond");
            if (waterLitersPerSecondProperty != null)
            {
                waterLitersPerSecondProperty.floatValue = Mathf.Max(0f, entry.waterLitersPerSecond);
            }
        }

        if (ShouldExposeVehicleStats(mapObject))
        {
            ApplyVehicleJson(serializedMapObject, entry);
        }

        if (mapObject is InputOutputModule)
        {
            ApplyInputOutputModuleJson(serializedMapObject, entry, definitions);
        }

        bool applied = serializedMapObject.ApplyModifiedPropertiesWithoutUndo();
        if (usesSeparateConveyorSerializedObject)
        {
            applied |= serializedConveyor.ApplyModifiedPropertiesWithoutUndo();
        }

        if (shouldSyncConveyorVariantSpeed)
        {
            SyncConveyorVariantSpeed(conveyorBelt);
        }

        if (applied)
        {
            EditorUtility.SetDirty(mapObject);
            if (mapObject.gameObject != null)
            {
                EditorUtility.SetDirty(mapObject.gameObject);
            }

            if (usesSeparateConveyorSerializedObject && conveyorBelt != null)
            {
                EditorUtility.SetDirty(conveyorBelt);
                if (conveyorBelt.gameObject != null)
                {
                    EditorUtility.SetDirty(conveyorBelt.gameObject);
                }
            }
        }
    }

    private static ItemDefinition.EnergyType ParseEnergyType(string rawValue, int rawEnumValue, ItemDefinition.EnergyType fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            if (rawEnumValue >= 0 && Enum.IsDefined(typeof(ItemDefinition.EnergyType), rawEnumValue))
            {
                return (ItemDefinition.EnergyType)rawEnumValue;
            }

            return fallback;
        }

        return Enum.TryParse(rawValue, true, out ItemDefinition.EnergyType parsedType)
            ? parsedType
            : (rawEnumValue >= 0 && Enum.IsDefined(typeof(ItemDefinition.EnergyType), rawEnumValue)
                ? (ItemDefinition.EnergyType)rawEnumValue
                : fallback);
    }

    private static void ApplyInputOutputModuleJson(SerializedObject serializedMapObject, ItemDataJsonEntry entry, List<ItemDefinition> definitions)
    {
        if (serializedMapObject == null || entry == null)
        {
            return;
        }

        SerializedProperty slotLayoutTypeProperty = serializedMapObject.FindProperty("slotLayoutType");
        SerializedProperty rectGridWidthProperty = serializedMapObject.FindProperty("rectGridWidth");
        SerializedProperty rectGridHeightProperty = serializedMapObject.FindProperty("rectGridHeight");
        if (slotLayoutTypeProperty != null && !string.IsNullOrWhiteSpace(entry.inputOutputLayoutType)
            && Enum.TryParse(entry.inputOutputLayoutType, true, out InputOutputModule.SlotLayoutType parsedLayoutType))
        {
            slotLayoutTypeProperty.enumValueIndex = (int)parsedLayoutType;
        }

        if (rectGridWidthProperty != null && entry.rectGridWidth > 0)
        {
            rectGridWidthProperty.intValue = Mathf.Max(1, entry.rectGridWidth);
        }

        if (rectGridHeightProperty != null && entry.rectGridHeight > 0)
        {
            rectGridHeightProperty.intValue = Mathf.Max(1, entry.rectGridHeight);
        }

        SerializedProperty inputListProperty = serializedMapObject.FindProperty("inputList");
        SerializedProperty outputListProperty = serializedMapObject.FindProperty("outputList");
        SerializedProperty legacyOutputProperty = serializedMapObject.FindProperty("output");
        SerializedProperty rectGridPlacementsProperty = serializedMapObject.FindProperty("rectGridPlacements");
        if (inputListProperty != null)
        {
            inputListProperty.ClearArray();
        }

        if (outputListProperty != null)
        {
            outputListProperty.ClearArray();
        }

        if (entry.ioPairs != null)
        {
            for (int i = 0; i < entry.ioPairs.Count; i++)
            {
                ApplyInputOutputPairJson(inputListProperty, outputListProperty, entry.ioPairs[i], definitions);
            }
        }

        if (legacyOutputProperty != null)
        {
            ResetInputOutputEntry(legacyOutputProperty);
        }

        if (rectGridPlacementsProperty != null)
        {
            rectGridPlacementsProperty.ClearArray();
            if (entry.rectGridBlocks != null)
            {
                for (int i = 0; i < entry.rectGridBlocks.Count; i++)
                {
                    ApplyRectGridBlockPlacementJson(rectGridPlacementsProperty, entry.rectGridBlocks[i]);
                }
            }
        }
    }

    private static void DrawMultiFocusModeField(SerializedProperty multiFocusModeProperty)
    {
        if (multiFocusModeProperty == null)
        {
            return;
        }

        MapObject.MultiFocusMode currentMode = Enum.IsDefined(
            typeof(MapObject.MultiFocusMode),
            multiFocusModeProperty.intValue)
                ? (MapObject.MultiFocusMode)multiFocusModeProperty.intValue
                : MapObject.MultiFocusMode.NearOne;

        EditorGUI.BeginChangeCheck();
        currentMode = (MapObject.MultiFocusMode)EditorGUILayout.EnumPopup("Multi Focus", currentMode);
        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        multiFocusModeProperty.intValue = (int)currentMode;
    }

    private static void SyncFenceVariantMultiFocusMode(Wall fence)
    {
        if (fence == null)
        {
            return;
        }

        MapObject.MultiFocusMode mode = fence.FocusMode;
        SyncFenceVariantMultiFocusMode(fence.StraightVariantPrefab, mode, fence);
        SyncFenceVariantMultiFocusMode(fence.CornerVariantPrefab, mode, fence);
        SyncFenceVariantMultiFocusMode(fence.TriCornerVariantPrefab, mode, fence);
        SyncFenceVariantMultiFocusMode(fence.CrossVariantPrefab, mode, fence);
    }

    private static void SyncFenceVariantMultiFocusMode(Wall variantPrefab, MapObject.MultiFocusMode mode, Wall sourceFence)
    {
        if (variantPrefab == null || variantPrefab == sourceFence)
        {
            return;
        }

        SerializedObject serializedVariant = new SerializedObject(variantPrefab);
        serializedVariant.Update();
        SerializedProperty multiFocusModeProperty = serializedVariant.FindProperty("multiFocusMode");
        if (multiFocusModeProperty == null || multiFocusModeProperty.intValue == (int)mode)
        {
            return;
        }

        multiFocusModeProperty.intValue = (int)mode;
        serializedVariant.ApplyModifiedProperties();
        EditorUtility.SetDirty(variantPrefab);
        if (variantPrefab.gameObject != null)
        {
            EditorUtility.SetDirty(variantPrefab.gameObject);
        }
    }

    private static void ApplyRectGridBlockPlacementJson(SerializedProperty rectGridPlacementsProperty, RectGridBlockPlacementJsonEntry entry)
    {
        if (rectGridPlacementsProperty == null || entry == null || string.IsNullOrWhiteSpace(entry.blockType)
            || !Enum.TryParse(entry.blockType, true, out InputOutputModule.RectGridBlockType parsedBlockType)
            || parsedBlockType == InputOutputModule.RectGridBlockType.None)
        {
            return;
        }

        int insertIndex = rectGridPlacementsProperty.arraySize;
        rectGridPlacementsProperty.InsertArrayElementAtIndex(insertIndex);
        SerializedProperty placementProperty = rectGridPlacementsProperty.GetArrayElementAtIndex(insertIndex);
        SerializedProperty xProperty = placementProperty.FindPropertyRelative("x");
        SerializedProperty yProperty = placementProperty.FindPropertyRelative("y");
        SerializedProperty blockTypeProperty = placementProperty.FindPropertyRelative("blockType");
        if (xProperty != null)
        {
            xProperty.intValue = Mathf.Max(0, entry.x);
        }

        if (yProperty != null)
        {
            yProperty.intValue = Mathf.Max(0, entry.y);
        }

        if (blockTypeProperty != null)
        {
            blockTypeProperty.enumValueIndex = (int)parsedBlockType;
        }
    }

    private static void ApplyInputOutputPairJson(
        SerializedProperty inputListProperty,
        SerializedProperty outputListProperty,
        InputOutputPairJsonEntry pairEntry,
        List<ItemDefinition> definitions)
    {
        if (pairEntry == null)
        {
            return;
        }

        if (inputListProperty != null)
        {
            int inputIndex = inputListProperty.arraySize;
            inputListProperty.InsertArrayElementAtIndex(inputIndex);
            ApplyInputOutputEntryJson(inputListProperty.GetArrayElementAtIndex(inputIndex), pairEntry.input, definitions);
        }

        if (outputListProperty != null)
        {
            int outputIndex = outputListProperty.arraySize;
            outputListProperty.InsertArrayElementAtIndex(outputIndex);
            ApplyInputOutputEntryJson(outputListProperty.GetArrayElementAtIndex(outputIndex), pairEntry.output, definitions);
        }
    }

    private static void ApplyInputOutputEntryJson(SerializedProperty entryProperty, InputOutputJsonEntry entry, List<ItemDefinition> definitions)
    {
        if (entryProperty == null)
        {
            return;
        }

        SerializedProperty itemDefinitionProperty = entryProperty.FindPropertyRelative("itemDefinition");
        SerializedProperty countProperty = entryProperty.FindPropertyRelative("count");
        if (itemDefinitionProperty == null || countProperty == null)
        {
            return;
        }

        if (entry == null)
        {
            itemDefinitionProperty.objectReferenceValue = null;
            countProperty.intValue = 1;
            return;
        }

        itemDefinitionProperty.objectReferenceValue = ResolveDefinitionReference(definitions, entry);
        countProperty.intValue = Mathf.Max(1, entry.count);
    }

    private static ItemDefinition ResolveDefinitionReference(List<ItemDefinition> definitions, ItemDataJsonEntry entry)
    {
        return entry == null
            ? null
            : ResolveDefinitionReference(definitions, entry.definitionAssetPath, entry.id, entry.itemName);
    }

    private static ItemDefinition ResolveDefinitionReference(List<ItemDefinition> definitions, InputOutputJsonEntry entry)
    {
        return entry == null
            ? null
            : ResolveDefinitionReference(definitions, entry.definitionAssetPath, entry.id, entry.itemName);
    }

    private static ItemDefinition ResolveDefinitionReference(List<ItemDefinition> definitions, string definitionAssetPath, int id, string itemName)
    {
        if (!string.IsNullOrWhiteSpace(definitionAssetPath))
        {
            ItemDefinition assetMatch = AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionAssetPath);
            if (assetMatch != null)
            {
                return assetMatch;
            }
        }

        if (definitions != null && id >= 0)
        {
            ItemDefinition idMatch = FindDefinitionById(definitions, id);
            if (idMatch != null)
            {
                return idMatch;
            }
        }

        if (definitions != null && !string.IsNullOrWhiteSpace(itemName))
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition candidate = definitions[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.itemName, itemName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.name, itemName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(GetDefinitionDisplayName(candidate), itemName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<T>(assetPath);
    }

    private static MapObject LoadMapObject(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        MapObject directMapObject = AssetDatabase.LoadAssetAtPath<MapObject>(assetPath);
        if (directMapObject != null)
        {
            return directMapObject;
        }

        InstallationObject directInstallationObject = AssetDatabase.LoadAssetAtPath<InstallationObject>(assetPath);
        if (directInstallationObject != null)
        {
            return directInstallationObject;
        }

        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefabRoot == null)
        {
            return LoadMapObjectFromAllAssets(assetPath);
        }

        MapObject mapObject = prefabRoot.GetComponent<MapObject>();
        if (mapObject == null)
        {
            mapObject = prefabRoot.GetComponentInChildren<MapObject>(true);
        }

        return mapObject != null ? mapObject : LoadMapObjectFromAllAssets(assetPath);
    }

    private static MapObject LoadMapObjectFromAllAssets(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                switch (assets[i])
                {
                    case InstallationObject installationObject:
                        return installationObject;
                    case MapObject mapObject:
                        return mapObject;
                }
            }
        }

        GameObject prefabContentsRoot = null;
        try
        {
            prefabContentsRoot = PrefabUtility.LoadPrefabContents(assetPath);
            if (prefabContentsRoot == null)
            {
                return null;
            }

            MapObject instanceMatch = prefabContentsRoot.GetComponent<MapObject>();
            if (instanceMatch == null)
            {
                instanceMatch = prefabContentsRoot.GetComponentInChildren<MapObject>(true);
            }

            MapObject sourceMatch = instanceMatch != null ? PrefabUtility.GetCorrespondingObjectFromSource(instanceMatch) : null;
            return sourceMatch != null ? sourceMatch : instanceMatch;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (prefabContentsRoot != null)
            {
                PrefabUtility.UnloadPrefabContents(prefabContentsRoot);
            }
        }
    }

    private void EnsureSelection()
    {
        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            selectedItemId = -1;
            return;
        }

        EnsureSelection(GetDefinitions(itemManager));
    }

    private void EnsureSelection(List<ItemDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0)
        {
            selectedItemId = -1;
            return;
        }

        if (FindDefinitionById(definitions, selectedItemId) != null)
        {
            return;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i] != null)
            {
                selectedItemId = definitions[i].id;
                return;
            }
        }

        selectedItemId = -1;
    }

    private void EnsureSelection(List<ItemDefinition> definitions, List<ItemDefinition> visibleDefinitions)
    {
        if (visibleDefinitions != null && visibleDefinitions.Count > 0)
        {
            if (FindDefinitionById(visibleDefinitions, selectedItemId) == null)
            {
                selectedItemId = visibleDefinitions[0].id;
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(itemSearchText))
        {
            EnsureSelection(definitions);
            return;
        }

        if (definitions == null || FindDefinitionById(definitions, selectedItemId) == null)
        {
            selectedItemId = -1;
        }
    }

    private List<ItemDefinition> FilterDefinitions(List<ItemDefinition> definitions)
    {
        if (definitions == null)
        {
            cachedVisibleDefinitions.Clear();
            cachedVisibleDefinitionsVersion = -1;
            return cachedVisibleDefinitions;
        }

        string searchText = string.IsNullOrWhiteSpace(itemSearchText) ? string.Empty : itemSearchText.Trim();
        if (cachedVisibleDefinitionsVersion == definitionsCacheVersion
            && string.Equals(cachedVisibleDefinitionsSearchText, searchText, StringComparison.Ordinal))
        {
            return cachedVisibleDefinitions;
        }

        cachedVisibleDefinitions.Clear();
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(searchText) || MatchesDefinitionSearch(definition, searchText))
            {
                cachedVisibleDefinitions.Add(definition);
            }
        }

        cachedVisibleDefinitionsSearchText = searchText;
        cachedVisibleDefinitionsVersion = definitionsCacheVersion;
        return cachedVisibleDefinitions;
    }

    private List<ItemDefinition> GetDefinitions(ItemManager itemManager)
    {
        int itemManagerDefinitionCount = itemManager != null && itemManager.ItemDefinitions != null
            ? itemManager.ItemDefinitions.Count
            : -1;
        if (!definitionsCacheDirty
            && cachedDefinitionsItemManager == itemManager
            && cachedDefinitionsItemManagerCount == itemManagerDefinitionCount)
        {
            return cachedDefinitions;
        }

        cachedDefinitions.Clear();
        if (itemManager != null && itemManager.ItemDefinitions != null)
        {
            for (int i = 0; i < itemManager.ItemDefinitions.Count; i++)
            {
                ItemDefinition definition = itemManager.ItemDefinitions[i];
                if (definition != null)
                {
                    cachedDefinitions.Add(definition);
                }
            }
        }

        if (!EditorApplication.isPlaying)
        {
            AppendMissingItemDefinitionAssets(cachedDefinitions);
        }
        cachedDefinitionsItemManager = itemManager;
        cachedDefinitionsItemManagerCount = itemManagerDefinitionCount;
        definitionsCacheDirty = false;
        definitionsCacheVersion++;
        InvalidateDefinitionPresentationCache();
        return cachedDefinitions;
    }

    private static void AppendMissingItemDefinitionAssets(List<ItemDefinition> definitions)
    {
        if (definitions == null || !AssetDatabase.IsValidFolder(ItemDefinitionAssetFolder))
        {
            return;
        }

        HashSet<string> knownAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<int> knownIds = new HashSet<int>();
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition existingDefinition = definitions[i];
            if (existingDefinition == null)
            {
                continue;
            }

            string existingAssetPath = AssetDatabase.GetAssetPath(existingDefinition);
            if (!string.IsNullOrWhiteSpace(existingAssetPath))
            {
                knownAssetPaths.Add(existingAssetPath);
            }

            if (existingDefinition.id >= 0)
            {
                knownIds.Add(existingDefinition.id);
            }
        }

        string[] definitionGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemDefinitionAssetFolder });
        if (definitionGuids == null || definitionGuids.Length == 0)
        {
            return;
        }

        List<ItemDefinition> missingDefinitions = new List<ItemDefinition>();
        for (int i = 0; i < definitionGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
            if (!string.IsNullOrWhiteSpace(assetPath) && knownAssetPaths.Contains(assetPath))
            {
                continue;
            }

            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
            if (definition == null || (definition.id >= 0 && knownIds.Contains(definition.id)))
            {
                continue;
            }

            missingDefinitions.Add(definition);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                knownAssetPaths.Add(assetPath);
            }

            if (definition.id >= 0)
            {
                knownIds.Add(definition.id);
            }
        }

        SortDefinitionsById(missingDefinitions);
        definitions.AddRange(missingDefinitions);
    }

    private static ItemDefinition FindDefinitionById(List<ItemDefinition> definitions, int id)
    {
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == id)
            {
                return definition;
            }
        }

        return null;
    }

    private static string GetDefinitionDisplayName(ItemDefinition definition)
    {
        if (definition == null)
        {
            return "(Missing)";
        }

        return string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName;
    }

    private static bool MatchesDefinitionSearch(ItemDefinition definition, string searchText)
    {
        if (definition == null || string.IsNullOrEmpty(searchText))
        {
            return false;
        }

        if (definition.id.ToString().IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        string displayName = GetDefinitionDisplayName(definition);
        if (!string.IsNullOrWhiteSpace(displayName) &&
            displayName.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(definition.name) &&
               definition.name.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void TryGiveItemToPlayer(ItemDefinition definition)
    {
        if (definition == null || definition.id < 0)
        {
            ShowNotification(new GUIContent("지급할 아이템이 없습니다."));
            return;
        }

        if (!EditorApplication.isPlaying)
        {
            ShowNotification(new GUIContent("플레이 중일 때만 지급할 수 있습니다."));
            return;
        }

        Player player = FindRuntimePlayer();
        if (player == null)
        {
            ShowNotification(new GUIContent("플레이어를 찾을 수 없습니다."));
            return;
        }

        if (player.TryAddToBag(definition.id, out _) || player.TryAddToHand(definition.id, out _))
        {
            ShowNotification(new GUIContent($"{GetDefinitionDisplayName(definition)} 지급"));
            Repaint();
            return;
        }

        TerrainGenerator terrain = FindObjectOfType<TerrainGenerator>();
        Vector3 playerPosition = player.transform.position;
        if (terrain != null
            && (terrain.TryAddDroppedItemAnimated(playerPosition, definition.id, playerPosition, out _)
                || terrain.TryAddDroppedItemAtPlayerBlock(playerPosition, definition.id, out _)
                || terrain.TryAddDroppedItemNear(playerPosition, definition.id, out _)))
        {
            ShowNotification(new GUIContent($"{GetDefinitionDisplayName(definition)} 바닥 지급"));
            Repaint();
            return;
        }

        ShowNotification(new GUIContent("가방과 손이 모두 가득 찼습니다."));
    }

    private static void DrawIconBackground(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
    }

    private static void DrawItemIcon(Rect rect, ItemDefinition definition)
    {
        if (!IsRepaintEvent())
        {
            return;
        }

        Sprite sprite = definition != null ? definition.icon : null;
        if (!TryGetSpriteTextureCoords(sprite, out Texture texture, out Rect textureCoords))
        {
            return;
        }

        Color previousColor = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTextureWithTexCoords(rect, texture, textureCoords);
        GUI.color = previousColor;
    }

    private static bool TryGetSpriteTextureCoords(Sprite sprite, out Texture texture, out Rect textureCoords)
    {
        texture = null;
        textureCoords = default;
        if (sprite == null || sprite.texture == null)
        {
            return false;
        }

        Rect textureRect;
        try
        {
            textureRect = sprite.textureRect;
        }
        catch (Exception)
        {
            textureRect = sprite.rect;
        }

        texture = sprite.texture;
        float textureWidth = Mathf.Max(1f, texture.width);
        float textureHeight = Mathf.Max(1f, texture.height);
        textureCoords = new Rect(
            textureRect.x / textureWidth,
            textureRect.y / textureHeight,
            textureRect.width / textureWidth,
            textureRect.height / textureHeight);
        return textureCoords.width > 0f && textureCoords.height > 0f;
    }

    private static bool IsRepaintEvent()
    {
        Event current = Event.current;
        return current != null && current.type == EventType.Repaint;
    }

    private ItemManager FindItemManager()
    {
        if (!itemManagerCacheDirty && IsCachedItemManagerValid(cachedItemManager))
        {
            return cachedItemManager;
        }

        cachedItemManager = FindItemManagerUncached();
        itemManagerCacheDirty = false;
        return cachedItemManager;
    }

    private static bool IsCachedItemManagerValid(ItemManager itemManager)
    {
        if (itemManager == null)
        {
            return false;
        }

        GameObject owner = itemManager.gameObject;
        return owner != null
               && (EditorUtility.IsPersistent(itemManager)
                   || (owner.scene.IsValid() && owner.scene.isLoaded));
    }

    private static ItemManager FindItemManagerUncached()
    {
        ItemManager[] managers = Resources.FindObjectsOfTypeAll<ItemManager>();
        if (managers == null || managers.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < managers.Length; i++)
        {
            ItemManager manager = managers[i];
            if (manager == null)
            {
                continue;
            }

            if (!EditorUtility.IsPersistent(manager) && manager.gameObject.scene.IsValid() && manager.gameObject.scene.isLoaded)
            {
                return manager;
            }
        }

        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] != null)
            {
                return managers[i];
            }
        }

        return null;
    }

    private static Player FindRuntimePlayer()
    {
        Player[] players = Resources.FindObjectsOfTypeAll<Player>();
        if (players == null || players.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < players.Length; i++)
        {
            Player player = players[i];
            if (player == null)
            {
                continue;
            }

            GameObject playerObject = player.gameObject;
            if (!EditorUtility.IsPersistent(player)
                && playerObject.scene.IsValid()
                && playerObject.scene.isLoaded
                && playerObject.activeInHierarchy)
            {
                return player;
            }
        }

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
            {
                return players[i];
            }
        }

        return null;
    }
}
