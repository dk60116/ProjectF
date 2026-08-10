using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class CraftingTreeEditorWindow : EditorWindow
{
    private const float SidebarWidth = 260f;
    private const int CurrentCraftingTreeFileVersion = 5;
    private const int ItemNameCraftingTreeFileVersion = 5;
    private const int ItemIdCraftingTreeFileVersion = 4;
    private const int MultiCraftingMapObjectGuidFileVersion = 3;
    private const int OutputCountCraftingTreeFileVersion = 2;
    private const int LegacyCraftingTreeFileVersion = 1;
    private static readonly Color DropFillColor = new Color(0.35f, 0.65f, 1f, 0.16f);
    private static readonly Color DropOutlineColor = new Color(0.35f, 0.65f, 1f, 0.95f);
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private int selectedItemId = -1;
    private string itemSearchText = string.Empty;
    private readonly Dictionary<int, List<IngredientEntry>> recipeByItemId = new Dictionary<int, List<IngredientEntry>>();
    private readonly Dictionary<int, List<MapObject>> craftingMapObjectsByItemId = new Dictionary<int, List<MapObject>>();
    private readonly Dictionary<int, int> outputCountByItemId = new Dictionary<int, int>();
    private readonly List<ItemDefinition> synchronizedDefinitions = new List<ItemDefinition>();
    private bool definitionCatalogDirty = true;

    [Serializable]
    private class CraftingTreeJsonFile
    {
        public string format = "ProjectF.CraftingTree";
        public int version = 2;
        public List<CraftingTreeJsonEntry> recipes = new List<CraftingTreeJsonEntry>();
        public List<CraftingTreeJsonEntry> items = new List<CraftingTreeJsonEntry>();
        public List<CraftingTreeJsonEntry> entries = new List<CraftingTreeJsonEntry>();
    }

    [Serializable]
    private class CraftingTreeJsonEntry
    {
        public int itemId = -1;
        public string itemName;
        public string definitionAssetPath;
        public int outputCount = 1;
        public List<CraftingIngredientJsonEntry> ingredients = new List<CraftingIngredientJsonEntry>();
        public List<CraftingMapObjectJsonEntry> craftingMapObjects = new List<CraftingMapObjectJsonEntry>();
        public List<CraftingMapObjectJsonEntry> requiredMapObjects = new List<CraftingMapObjectJsonEntry>();
    }

    [Serializable]
    private class CraftingIngredientJsonEntry
    {
        public int itemId = -1;
        public string itemName;
        public string definitionAssetPath;
        public int count = 1;
    }

    [Serializable]
    private class CraftingMapObjectJsonEntry
    {
        public int itemId = -1;
        public string mapObjectName;
        public string assetPath;
    }

    private struct IngredientEntry
    {
        public int itemId;
        public int count;

        public IngredientEntry(int itemId, int count)
        {
            this.itemId = itemId;
            this.count = count;
        }
    }

    private sealed class IconSelectionPopupContent<T> : PopupWindowContent
    {
        private const float PopupWidth = 320f;
        private const float RowHeight = 28f;
        private const float MaximumPopupHeight = 420f;
        private readonly List<T> entries;
        private readonly Func<T, bool> isSelected;
        private readonly Func<T, string> getLabel;
        private readonly Action<Rect, T> drawIcon;
        private readonly Action<T> onSelected;
        private Vector2 scrollPosition;

        public IconSelectionPopupContent(
            IEnumerable<T> entries,
            Func<T, bool> isSelected,
            Func<T, string> getLabel,
            Action<Rect, T> drawIcon,
            Action<T> onSelected)
        {
            this.entries = entries != null ? new List<T>(entries) : new List<T>();
            this.isSelected = isSelected;
            this.getLabel = getLabel;
            this.drawIcon = drawIcon;
            this.onSelected = onSelected;
        }

        public override Vector2 GetWindowSize()
        {
            float contentHeight = Mathf.Max(RowHeight, entries.Count * RowHeight + 8f);
            return new Vector2(PopupWidth, Mathf.Min(MaximumPopupHeight, contentHeight));
        }

        public override void OnGUI(Rect rect)
        {
            Event current = Event.current;
            if (current != null && current.type == EventType.MouseMove)
            {
                editorWindow.Repaint();
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < entries.Count; i++)
            {
                T entry = entries[i];
                Rect rowRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
                bool selected = isSelected != null && isSelected(entry);
                bool isHovered = current != null && rowRect.Contains(current.mousePosition);
                if (selected || isHovered)
                {
                    Color rowColor = selected
                        ? new Color(0.24f, 0.49f, 0.78f, 0.75f)
                        : new Color(1f, 1f, 1f, 0.08f);
                    EditorGUI.DrawRect(rowRect, rowColor);
                }

                Rect iconRect = new Rect(rowRect.x + 5f, rowRect.y + 3f, 22f, 22f);
                DrawIconBackground(iconRect);
                drawIcon?.Invoke(iconRect, entry);

                Rect labelRect = new Rect(iconRect.xMax + 7f, rowRect.y, rowRect.width - 38f, RowHeight);
                GUI.Label(labelRect, getLabel != null ? getLabel(entry) : string.Empty);

                if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
                {
                    onSelected?.Invoke(entry);
                    editorWindow.Close();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }

    [MenuItem("Window/ProjectF/Crafting Tree")]
    public static void ShowWindow()
    {
        CraftingTreeEditorWindow window = GetWindow<CraftingTreeEditorWindow>("Crafting Tree");
        window.minSize = new Vector2(600f, 400f);
        window.Show();
    }

    public static void ReloadOpenWindows()
    {
        CraftingTreeEditorWindow[] windows = Resources.FindObjectsOfTypeAll<CraftingTreeEditorWindow>();
        if (windows == null)
        {
            return;
        }

        for (int i = 0; i < windows.Length; i++)
        {
            CraftingTreeEditorWindow window = windows[i];
            if (window == null)
            {
                continue;
            }

            window.InvalidateDefinitionCatalog();
            window.LoadCraftingTree();
            window.Repaint();
        }
    }

    private void OnEnable()
    {
        ItemDataEditorWindow.DefinitionCatalog.Changed += HandleDefinitionCatalogChanged;
        InvalidateDefinitionCatalog();
        LoadCraftingTree();
    }

    private void OnDisable()
    {
        ItemDataEditorWindow.DefinitionCatalog.Changed -= HandleDefinitionCatalogChanged;
    }

    private void OnProjectChange()
    {
        InvalidateDefinitionCatalog();
        LoadCraftingTree();
        Repaint();
    }

    private void OnHierarchyChange()
    {
        InvalidateDefinitionCatalog();
        LoadCraftingTree();
        Repaint();
    }

    private void HandleDefinitionCatalogChanged()
    {
        InvalidateDefinitionCatalog();
        LoadCraftingTree();
        Repaint();
    }

    private void OnGUI()
    {
        DrawBackground();
        DrawItemList();
        DrawDetailPanel();
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

        List<ItemDefinition> definitions = GetSynchronizedDefinitions(itemManager);
        if (definitions == null || definitions.Count == 0)
        {
            EditorGUILayout.HelpBox("ItemDefinitions가 비어있습니다.", MessageType.Warning);
            GUILayout.EndArea();
            return;
        }

        List<ItemDefinition> visibleDefinitions = FilterDefinitions(definitions);
        EnsureVisibleSelection(definitions, visibleDefinitions);

        if (visibleDefinitions.Count == 0)
        {
            EditorGUILayout.HelpBox("검색 결과가 없습니다.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        for (int i = 0; i < visibleDefinitions.Count; i++)
        {
            ItemDefinition definition = visibleDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            string displayName = GetDefinitionDisplayName(definition);
            bool isSelected = definition.id == selectedItemId;
            Rect rowRect = GUILayoutUtility.GetRect(1f, 28f, GUILayout.ExpandWidth(true));
            GUIContent content = new GUIContent($"[{definition.id}] {displayName}", GetItemIcon(definition));
            ItemDefinitionDragAndDropUtility.HandleListItemDrag(rowRect, definition, content.text, this);
            Color previousContentColor = GUI.contentColor;
            GUI.contentColor = Color.white;
            bool pressed = GUI.Toggle(rowRect, isSelected, content, "Button");
            GUI.contentColor = previousContentColor;
            if (pressed)
            {
                selectedItemId = definition.id;
            }
        }
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", GUILayout.Width(60f)))
        {
            SaveCraftingTree();
        }

        if (GUILayout.Button("Load", GUILayout.Width(60f)))
        {
            LoadCraftingTree();
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
        EditorGUILayout.LabelField("Crafting Detail", EditorStyles.boldLabel);

        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EditorGUILayout.HelpBox("씬에서 ItemManager를 찾을 수 없습니다.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        List<ItemDefinition> definitions = GetSynchronizedDefinitions(itemManager);
        if (definitions == null || definitions.Count == 0)
        {
            EditorGUILayout.HelpBox("ItemDefinitions가 비어있습니다.", MessageType.Warning);
            GUILayout.EndArea();
            return;
        }

        ItemDefinition selectedDefinition = FindDefinitionById(definitions, selectedItemId);
        if (selectedDefinition == null)
        {
            EditorGUILayout.HelpBox("왼쪽 목록에서 아이템을 선택하세요.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        DrawSelectedItemHeader(selectedDefinition);
        GUILayout.Space(8f);

        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        DrawOutputCount(selectedDefinition);
        GUILayout.Space(12f);
        DrawCraftingMapObjectRequirement(selectedDefinition);
        GUILayout.Space(12f);
        DrawIngredientList(selectedDefinition, definitions);
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
        string displayName = GetDefinitionDisplayName(definition);
        EditorGUILayout.LabelField($"[{definition.id}] {displayName}", EditorStyles.largeLabel);
        EditorGUILayout.LabelField("필요 재료를 아래에서 추가/편집하세요.", EditorStyles.miniLabel);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void DrawOutputCount(ItemDefinition targetDefinition)
    {
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Count", GUILayout.Width(80f));
        int currentValue = GetOutputCount(targetDefinition.id);
        int newValue = Mathf.Max(1, EditorGUILayout.IntField(currentValue, GUILayout.Width(60f)));
        if (newValue != currentValue)
        {
            outputCountByItemId[targetDefinition.id] = newValue;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawIngredientList(ItemDefinition targetDefinition, List<ItemDefinition> definitions)
    {
        List<IngredientEntry> recipe = GetOrCreateRecipe(targetDefinition.id);

        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Ingredients", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Add", GUILayout.Width(60f)))
        {
            int fallbackId = definitions.Count > 0 ? definitions[0].id : -1;
            recipe.Add(new IngredientEntry(fallbackId, 1));
        }
        GUILayout.EndHorizontal();

        for (int i = recipe.Count - 1; i >= 0; i--)
        {
            IngredientEntry entry = recipe[i];
            ItemDefinition currentDefinition = FindDefinitionById(definitions, entry.itemId);
            if (currentDefinition == null && definitions.Count > 0)
            {
                currentDefinition = definitions[0];
            }

            GUILayout.BeginHorizontal();
            Rect iconRect = GUILayoutUtility.GetRect(22f, 22f, GUILayout.ExpandWidth(false));
            DrawIconBackground(iconRect);
            DrawItemIcon(iconRect, currentDefinition);
            int ingredientIndex = i;
            ItemDefinition nextDefinition = DrawDefinitionSelector(
                iconRect,
                currentDefinition,
                definitions,
                selectedDefinition =>
                {
                    if (selectedDefinition == null
                        || ingredientIndex < 0
                        || ingredientIndex >= recipe.Count)
                    {
                        return;
                    }

                    IngredientEntry currentEntry = recipe[ingredientIndex];
                    recipe[ingredientIndex] = new IngredientEntry(selectedDefinition.id, currentEntry.count);
                    Repaint();
                });
            int newItemId = nextDefinition != null ? nextDefinition.id : entry.itemId;
            int newCount = Mathf.Max(1, EditorGUILayout.IntField(entry.count, GUILayout.Width(60f)));

            if (GUILayout.Button("X", GUILayout.Width(24f)))
            {
                recipe.RemoveAt(i);
                GUILayout.EndHorizontal();
                continue;
            }

            recipe[i] = new IngredientEntry(newItemId, newCount);
            GUILayout.EndHorizontal();
        }
    }

    private void DrawCraftingMapObjectRequirement(ItemDefinition targetDefinition)
    {
        List<MapObject> mapObjects = CollectMapObjectCandidates();
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Crafting MapObject", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Add", GUILayout.Width(60f)))
        {
            List<MapObject> selectedMapObjects = GetOrCreateCraftingMapObjects(targetDefinition.id);
            AddCraftingMapObjectIfMissing(
                selectedMapObjects,
                GetNextCraftingMapObjectCandidate(mapObjects, selectedMapObjects));
        }
        GUILayout.EndHorizontal();

        if (mapObjects.Count == 0)
        {
            EditorGUILayout.HelpBox("MapObject 프리팹을 찾을 수 없습니다. Assets/MapObject 폴더를 확인하세요.", MessageType.Info);
            return;
        }

        List<MapObject> selectedObjects = GetOrCreateCraftingMapObjects(targetDefinition.id);
        if (selectedObjects.Count <= 0)
        {
            Rect emptyDropRect = GUILayoutUtility.GetRect(1f, 36f, GUILayout.ExpandWidth(true));
            EditorGUI.HelpBox(emptyDropRect, "필요한 Crafting MapObject를 추가하세요.", MessageType.None);
            if (HandleMapObjectDropTarget(emptyDropRect, this, mapObjects, out MapObject droppedMapObject))
            {
                AddCraftingMapObjectIfMissing(selectedObjects, droppedMapObject);
            }
            return;
        }

        for (int i = selectedObjects.Count - 1; i >= 0; i--)
        {
            MapObject currentSelection = selectedObjects[i];

            EditorGUILayout.BeginHorizontal();
            Rect iconRect = GUILayoutUtility.GetRect(22f, 22f, GUILayout.ExpandWidth(false));
            DrawIconBackground(iconRect);
            DrawMapObjectIcon(iconRect, currentSelection);
            int mapObjectIndex = i;
            MapObject nextSelection = DrawMapObjectPopup(
                currentSelection,
                mapObjects,
                iconRect,
                selectedMapObject =>
                {
                    if (mapObjectIndex < 0 || mapObjectIndex >= selectedObjects.Count)
                    {
                        return;
                    }

                    selectedObjects[mapObjectIndex] = selectedMapObject;
                    Repaint();
                });
            if (GUILayout.Button("X", GUILayout.Width(24f)))
            {
                selectedObjects.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                continue;
            }

            EditorGUILayout.EndHorizontal();
            if (nextSelection != currentSelection)
            {
                selectedObjects[i] = nextSelection;
            }
        }
    }

    private void SaveCraftingTree()
    {
        string path = GetCraftingTreeAssetPath();
        string resourcePath = GetCraftingTreeResourcesPath();
        EnsureParentFolder(path);
        EnsureParentFolder(resourcePath);

        List<ItemDefinition> definitions = GetSynchronizedDefinitions(FindItemManager());
        List<int> itemIds = CollectRecipeItemIds(definitions);

        WriteCraftingTree(path, itemIds, definitions);
        WriteCraftingTree(resourcePath, itemIds, definitions);

        AssetDatabase.Refresh();
        CraftingTreeRuntime.ForceReload();
    }

    private void ExportJson()
    {
        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EditorUtility.DisplayDialog("CraftingTree", "씬에서 ItemManager를 찾을 수 없습니다.", "OK");
            return;
        }

        List<ItemDefinition> definitions = GetSynchronizedDefinitions(itemManager);
        string defaultPath = Path.Combine(Application.dataPath, "Data", "CraftingTree", "crafting_tree.json");
        string exportPath = EditorUtility.SaveFilePanel("Export CraftingTree JSON", Path.GetDirectoryName(defaultPath), Path.GetFileNameWithoutExtension(defaultPath), "json");
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            return;
        }

        CraftingTreeJsonFile file = new CraftingTreeJsonFile();
        List<int> itemIds = CollectRecipeItemIds(definitions);
        for (int i = 0; i < itemIds.Count; i++)
        {
            CraftingTreeJsonEntry entry = BuildJsonEntry(itemIds[i], definitions);
            file.recipes.Add(entry);
            file.items.Add(entry);
            file.entries.Add(entry);
        }

        File.WriteAllText(exportPath, JsonUtility.ToJson(file, true));
        AssetDatabase.Refresh();
    }

    private void LoadJson()
    {
        string importPath = EditorUtility.OpenFilePanel("Load CraftingTree JSON", Application.dataPath, "json");
        if (string.IsNullOrWhiteSpace(importPath) || !File.Exists(importPath))
        {
            return;
        }

        string json = File.ReadAllText(importPath);
        if (string.IsNullOrWhiteSpace(json))
        {
            EditorUtility.DisplayDialog("CraftingTree", "JSON 파일이 비어 있습니다.", "OK");
            return;
        }

        CraftingTreeJsonFile file = JsonUtility.FromJson<CraftingTreeJsonFile>(json);
        List<CraftingTreeJsonEntry> entries = GetJsonEntries(file);
        if (entries.Count == 0)
        {
            EditorUtility.DisplayDialog("CraftingTree", "불러올 레시피가 없습니다.", "OK");
            return;
        }

        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EditorUtility.DisplayDialog("CraftingTree", "씬에서 ItemManager를 찾을 수 없습니다.", "OK");
            return;
        }

        List<ItemDefinition> definitions = GetSynchronizedDefinitions(itemManager);
        if (definitions == null || definitions.Count == 0)
        {
            EditorUtility.DisplayDialog("CraftingTree", "ItemDefinitions가 비어 있습니다.", "OK");
            return;
        }

        List<MapObject> mapObjectCandidates = CollectMapObjectCandidates();
        recipeByItemId.Clear();
        craftingMapObjectsByItemId.Clear();
        outputCountByItemId.Clear();

        int appliedCount = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            CraftingTreeJsonEntry entry = entries[i];
            ItemDefinition targetDefinition = ResolveDefinition(definitions, entry.definitionAssetPath, entry.itemName, entry.itemId);
            if (targetDefinition == null)
            {
                continue;
            }

            int targetItemId = targetDefinition.id;
            outputCountByItemId[targetItemId] = Mathf.Max(1, entry.outputCount);

            List<MapObject> resolvedMapObjects = ResolveCraftingMapObjects(entry, mapObjectCandidates);
            if (resolvedMapObjects.Count > 0)
            {
                craftingMapObjectsByItemId[targetItemId] = resolvedMapObjects;
            }

            List<IngredientEntry> recipe = GetOrCreateRecipe(targetItemId);
            recipe.Clear();
            if (entry.ingredients != null)
            {
                for (int ingredientIndex = 0; ingredientIndex < entry.ingredients.Count; ingredientIndex++)
                {
                    CraftingIngredientJsonEntry ingredientEntry = entry.ingredients[ingredientIndex];
                    ItemDefinition ingredientDefinition = ResolveDefinition(definitions, ingredientEntry.definitionAssetPath, ingredientEntry.itemName, ingredientEntry.itemId);
                    if (ingredientDefinition == null)
                    {
                        continue;
                    }

                    recipe.Add(new IngredientEntry(ingredientDefinition.id, Mathf.Max(1, ingredientEntry.count)));
                }
            }

            appliedCount++;
        }

        SaveCraftingTree();
        Repaint();
        EditorUtility.DisplayDialog("CraftingTree", $"{appliedCount}개 레시피를 불러왔습니다.", "OK");
    }

    private void WriteCraftingTree(string path, List<int> itemIds, List<ItemDefinition> definitions)
    {
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(CurrentCraftingTreeFileVersion);
            writer.Write(itemIds.Count);

            for (int i = 0; i < itemIds.Count; i++)
            {
                int itemId = itemIds[i];
                ItemDefinition targetDefinition = FindDefinitionById(definitions, itemId);
                writer.Write(GetPersistedItemName(targetDefinition, definitions));

                List<int> mapObjectIds = GetMapObjectRuntimeIds(GetCraftingMapObjects(itemId));
                writer.Write(mapObjectIds.Count);
                for (int mapObjectIndex = 0; mapObjectIndex < mapObjectIds.Count; mapObjectIndex++)
                {
                    ItemDefinition mapObjectDefinition = FindDefinitionById(definitions, mapObjectIds[mapObjectIndex]);
                    writer.Write(GetPersistedItemName(mapObjectDefinition, definitions));
                }

                writer.Write(GetOutputCount(itemId));

                List<IngredientEntry> recipe = GetOrCreateRecipe(itemId);
                writer.Write(recipe.Count);
                for (int j = 0; j < recipe.Count; j++)
                {
                    ItemDefinition ingredientDefinition = FindDefinitionById(definitions, recipe[j].itemId);
                    writer.Write(GetPersistedItemName(ingredientDefinition, definitions));
                    writer.Write(recipe[j].count);
                }
            }
        }
    }

    private void LoadCraftingTree()
    {
        string path = GetCraftingTreeAssetPath();
        if (!File.Exists(path))
        {
            string resourcePath = GetCraftingTreeResourcesPath();
            if (File.Exists(resourcePath))
            {
                path = resourcePath;
            }
        }

        if (!File.Exists(path))
        {
            EditorUtility.DisplayDialog("CraftingTree", "저장된 CraftingTree 파일이 없습니다.", "OK");
            return;
        }

        recipeByItemId.Clear();
        craftingMapObjectsByItemId.Clear();
        outputCountByItemId.Clear();

        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            int version = reader.ReadInt32();
            if (version < LegacyCraftingTreeFileVersion || version > CurrentCraftingTreeFileVersion)
            {
                EditorUtility.DisplayDialog("CraftingTree", $"버전이 맞지 않습니다. (파일: {version})", "OK");
                return;
            }

            List<ItemDefinition> definitions = GetSynchronizedDefinitions(FindItemManager());
            int itemCount = reader.ReadInt32();
            for (int i = 0; i < itemCount; i++)
            {
                int itemId = ReadItemId(reader, version, definitions);
                List<MapObject> mapObjects = ReadCraftingMapObjects(reader, version, definitions);
                if (itemId >= 0 && mapObjects.Count > 0)
                {
                    craftingMapObjectsByItemId[itemId] = mapObjects;
                }

                int outputCount = version >= OutputCountCraftingTreeFileVersion
                    ? Mathf.Max(1, reader.ReadInt32())
                    : 1;
                if (itemId >= 0)
                {
                    outputCountByItemId[itemId] = outputCount;
                }

                int ingredientCount = reader.ReadInt32();
                List<IngredientEntry> recipe = new List<IngredientEntry>(ingredientCount);
                for (int j = 0; j < ingredientCount; j++)
                {
                    int ingredientId = ReadItemId(reader, version, definitions);
                    int count = reader.ReadInt32();
                    if (ingredientId >= 0)
                    {
                        recipe.Add(new IngredientEntry(ingredientId, count));
                    }
                }

                if (itemId >= 0)
                {
                    recipeByItemId[itemId] = recipe;
                }
            }
        }

        Repaint();
    }

    private static string GetCraftingTreeAssetPath()
    {
        return Path.Combine(Application.dataPath, "Data", "CraftingTree", "crafting_tree.bytes");
    }

    private static string GetCraftingTreeResourcesPath()
    {
        return Path.Combine(Application.dataPath, "Resources", "Data", "CraftingTree", "crafting_tree.bytes");
    }

    private CraftingTreeJsonEntry BuildJsonEntry(int itemId, List<ItemDefinition> definitions)
    {
        CraftingTreeJsonEntry entry = new CraftingTreeJsonEntry();
        ItemDefinition targetDefinition = FindDefinitionById(definitions, itemId);
        entry.itemId = itemId;
        entry.itemName = targetDefinition != null ? GetDefinitionDisplayName(targetDefinition) : string.Empty;
        entry.definitionAssetPath = targetDefinition != null ? AssetDatabase.GetAssetPath(targetDefinition) : string.Empty;
        entry.outputCount = GetOutputCount(itemId);

        List<MapObject> mapObjects = GetCraftingMapObjects(itemId);
        if (mapObjects != null)
        {
            for (int i = 0; i < mapObjects.Count; i++)
            {
                CraftingMapObjectJsonEntry mapObjectEntry = BuildMapObjectJsonEntry(mapObjects[i]);
                if (mapObjectEntry != null)
                {
                    entry.craftingMapObjects.Add(mapObjectEntry);
                    entry.requiredMapObjects.Add(mapObjectEntry);
                }
            }
        }

        List<IngredientEntry> recipe = GetOrCreateRecipe(itemId);
        for (int i = 0; i < recipe.Count; i++)
        {
            IngredientEntry ingredient = recipe[i];
            ItemDefinition ingredientDefinition = FindDefinitionById(definitions, ingredient.itemId);
            entry.ingredients.Add(new CraftingIngredientJsonEntry
            {
                itemId = ingredient.itemId,
                itemName = ingredientDefinition != null ? GetDefinitionDisplayName(ingredientDefinition) : string.Empty,
                definitionAssetPath = ingredientDefinition != null ? AssetDatabase.GetAssetPath(ingredientDefinition) : string.Empty,
                count = Mathf.Max(1, ingredient.count)
            });
        }

        return entry;
    }

    private static CraftingMapObjectJsonEntry BuildMapObjectJsonEntry(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return null;
        }

        GameObject prefabRoot = GetMapObjectPrefabRoot(mapObject);
        return new CraftingMapObjectJsonEntry
        {
            itemId = mapObject.ResolveItemId(),
            mapObjectName = prefabRoot != null ? prefabRoot.name : mapObject.gameObject.name,
            assetPath = AssetDatabase.GetAssetPath(prefabRoot)
        };
    }

    private static List<CraftingTreeJsonEntry> GetJsonEntries(CraftingTreeJsonFile file)
    {
        if (file == null)
        {
            return new List<CraftingTreeJsonEntry>();
        }

        if (file.recipes != null && file.recipes.Count > 0)
        {
            return file.recipes;
        }

        if (file.items != null && file.items.Count > 0)
        {
            return file.items;
        }

        if (file.entries != null && file.entries.Count > 0)
        {
            return file.entries;
        }

        return new List<CraftingTreeJsonEntry>();
    }

    private static ItemDefinition ResolveDefinition(List<ItemDefinition> definitions, string definitionAssetPath, string itemName, int itemId)
    {
        if (definitions == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(definitionAssetPath))
        {
            ItemDefinition assetMatch = AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionAssetPath);
            if (assetMatch != null)
            {
                return assetMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(itemName))
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition candidate = definitions[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(GetDefinitionDisplayName(candidate), itemName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.itemName, itemName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.name, itemName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return itemId >= 0 ? FindDefinitionById(definitions, itemId) : null;
    }

    private static List<MapObject> ResolveCraftingMapObjects(CraftingTreeJsonEntry entry, List<MapObject> candidates)
    {
        List<MapObject> results = new List<MapObject>();
        if (entry == null || candidates == null)
        {
            return results;
        }

        List<CraftingMapObjectJsonEntry> sourceEntries = null;
        if (entry.craftingMapObjects != null && entry.craftingMapObjects.Count > 0)
        {
            sourceEntries = entry.craftingMapObjects;
        }
        else if (entry.requiredMapObjects != null && entry.requiredMapObjects.Count > 0)
        {
            sourceEntries = entry.requiredMapObjects;
        }

        if (sourceEntries == null)
        {
            return results;
        }

        for (int i = 0; i < sourceEntries.Count; i++)
        {
            MapObject resolved = ResolveMapObject(sourceEntries[i], candidates);
            if (resolved != null && !results.Contains(resolved))
            {
                results.Add(resolved);
            }
        }

        return results;
    }

    private static MapObject ResolveMapObject(CraftingMapObjectJsonEntry entry, List<MapObject> candidates)
    {
        if (entry == null || candidates == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(entry.assetPath))
        {
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(entry.assetPath);
            if (prefabRoot != null)
            {
                MapObject directMatch = prefabRoot.GetComponent<MapObject>();
                if (directMatch == null)
                {
                    directMatch = prefabRoot.GetComponentInChildren<MapObject>(true);
                }

                if (directMatch != null)
                {
                    return directMatch;
                }
            }
        }

        if (entry.itemId >= 0)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                MapObject candidate = candidates[i];
                if (candidate != null && candidate.ResolveItemId() == entry.itemId)
                {
                    return candidate;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.mapObjectName))
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                MapObject candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                GameObject prefabRoot = candidate.transform.root != null ? candidate.transform.root.gameObject : candidate.gameObject;
                string candidateName = prefabRoot != null ? prefabRoot.name : candidate.gameObject.name;
                if (string.Equals(candidateName, entry.mapObjectName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.gameObject.name, entry.mapObjectName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void EnsureParentFolder(string path)
    {
        string folderPath = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
    }

    private List<int> CollectRecipeItemIds(List<ItemDefinition> definitions)
    {
        HashSet<int> ids = new HashSet<int>();

        foreach (int key in recipeByItemId.Keys)
        {
            ids.Add(key);
        }

        foreach (int key in craftingMapObjectsByItemId.Keys)
        {
            ids.Add(key);
        }

        foreach (int key in outputCountByItemId.Keys)
        {
            ids.Add(key);
        }

        List<int> results = new List<int>();
        bool hasDefinitions = definitions != null && definitions.Count > 0;
        if (hasDefinitions)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition != null && ids.Remove(definition.id))
                {
                    results.Add(definition.id);
                }
            }
        }

        if (!hasDefinitions && ids.Count > 0)
        {
            List<int> unresolvedIds = new List<int>(ids);
            unresolvedIds.Sort();
            results.AddRange(unresolvedIds);
        }

        return results;
    }

    private List<MapObject> ReadCraftingMapObjects(BinaryReader reader, int version, List<ItemDefinition> definitions)
    {
        List<MapObject> results = new List<MapObject>();

        if (version >= ItemNameCraftingTreeFileVersion)
        {
            int mapObjectCount = Mathf.Max(0, reader.ReadInt32());
            for (int i = 0; i < mapObjectCount; i++)
            {
                ItemDefinition definition = FindDefinitionByPersistedName(definitions, reader.ReadString());
                AppendCraftingMapObject(results, definition != null ? definition.mapObject : null);
            }

            return results;
        }

        if (version >= ItemIdCraftingTreeFileVersion)
        {
            int mapObjectCount = Mathf.Max(0, reader.ReadInt32());
            for (int i = 0; i < mapObjectCount; i++)
            {
                AppendCraftingMapObject(results, ResolveMapObjectFromRuntimeId(reader.ReadInt32()));
            }

            return results;
        }

        if (version >= MultiCraftingMapObjectGuidFileVersion)
        {
            int mapObjectCount = Mathf.Max(0, reader.ReadInt32());
            for (int i = 0; i < mapObjectCount; i++)
            {
                AppendCraftingMapObject(results, ResolveMapObjectFromGuid(reader.ReadString()));
            }

            return results;
        }

        AppendCraftingMapObject(results, ResolveMapObjectFromGuid(reader.ReadString()));
        return results;
    }

    private static int ReadItemId(BinaryReader reader, int version, List<ItemDefinition> definitions)
    {
        if (version < ItemNameCraftingTreeFileVersion)
        {
            return reader.ReadInt32();
        }

        ItemDefinition definition = FindDefinitionByPersistedName(definitions, reader.ReadString());
        return definition != null ? definition.id : -1;
    }

    private static ItemDefinition FindDefinitionByPersistedName(List<ItemDefinition> definitions, string itemName)
    {
        if (definitions == null || string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        string normalizedName = itemName.Trim();
        ItemDefinition definition = ItemDefinitionLookup.ResolveByPersistenceName(definitions, normalizedName);
        if (definition != null)
        {
            return definition;
        }

        Debug.LogWarning($"CraftingTree: ItemDefinition '{normalizedName}'을(를) 찾지 못했습니다.");
        return null;
    }

    private static string GetMapObjectGuid(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return string.Empty;
        }

        string path = AssetDatabase.GetAssetPath(mapObject.gameObject);
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return AssetDatabase.AssetPathToGUID(path);
    }

    private static MapObject ResolveMapObjectFromGuid(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
        {
            return null;
        }

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefabRoot == null)
        {
            return null;
        }

        MapObject mapObject = prefabRoot.GetComponent<MapObject>();
        if (mapObject == null)
        {
            mapObject = prefabRoot.GetComponentInChildren<MapObject>(true);
        }

        return mapObject;
    }

    private static MapObject ResolveMapObjectFromRuntimeId(int runtimeId)
    {
        if (runtimeId < 0)
        {
            return null;
        }

        List<MapObject> candidates = CollectMapObjectCandidates();
        for (int i = 0; i < candidates.Count; i++)
        {
            MapObject candidate = candidates[i];
            if (candidate != null && candidate.ResolveItemId() == runtimeId)
            {
                return candidate;
            }
        }

        return null;
    }

    private List<IngredientEntry> GetOrCreateRecipe(int itemId)
    {
        if (!recipeByItemId.TryGetValue(itemId, out List<IngredientEntry> recipe))
        {
            recipe = new List<IngredientEntry>();
            recipeByItemId[itemId] = recipe;
        }

        return recipe;
    }

    private List<MapObject> GetOrCreateCraftingMapObjects(int itemId)
    {
        if (!craftingMapObjectsByItemId.TryGetValue(itemId, out List<MapObject> values) || values == null)
        {
            values = new List<MapObject>();
            craftingMapObjectsByItemId[itemId] = values;
        }

        return values;
    }

    private List<MapObject> GetCraftingMapObjects(int itemId)
    {
        if (craftingMapObjectsByItemId.TryGetValue(itemId, out List<MapObject> values) && values != null)
        {
            return values;
        }

        return null;
    }

    private int GetOutputCount(int itemId)
    {
        if (outputCountByItemId.TryGetValue(itemId, out int value))
        {
            return Mathf.Max(1, value);
        }

        return 1;
    }

    private static void AppendCraftingMapObject(List<MapObject> results, MapObject mapObject)
    {
        if (results == null || mapObject == null || results.Contains(mapObject))
        {
            return;
        }

        results.Add(mapObject);
    }

    private static List<int> GetMapObjectRuntimeIds(List<MapObject> mapObjects)
    {
        List<int> results = new List<int>();
        if (mapObjects == null)
        {
            return results;
        }

        HashSet<int> seen = new HashSet<int>();
        for (int i = 0; i < mapObjects.Count; i++)
        {
            MapObject mapObject = mapObjects[i];
            if (mapObject == null)
            {
                continue;
            }

            int runtimeId = mapObject.ResolveItemId();
            if (runtimeId < 0 || !seen.Add(runtimeId))
            {
                continue;
            }

            results.Add(runtimeId);
        }

        return results;
    }

    private static MapObject GetNextCraftingMapObjectCandidate(List<MapObject> availableMapObjects, List<MapObject> selectedMapObjects)
    {
        if (availableMapObjects == null || availableMapObjects.Count <= 0)
        {
            return null;
        }

        if (selectedMapObjects == null)
        {
            return availableMapObjects[0];
        }

        for (int i = 0; i < availableMapObjects.Count; i++)
        {
            MapObject candidate = availableMapObjects[i];
            if (candidate != null && !selectedMapObjects.Contains(candidate))
            {
                return candidate;
            }
        }

        return availableMapObjects[0];
    }

    private static List<MapObject> CollectMapObjectCandidates()
    {
        List<MapObject> results = new List<MapObject>();
        HashSet<MapObject> seen = new HashSet<MapObject>();

        List<string> searchFolders = new List<string>();
        if (AssetDatabase.IsValidFolder("Assets/MapObject"))
        {
            searchFolders.Add("Assets/MapObject");
        }
        if (AssetDatabase.IsValidFolder("Assets/MapObjects"))
        {
            searchFolders.Add("Assets/MapObjects");
        }

        if (searchFolders.Count == 0)
        {
            return results;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", searchFolders.ToArray());
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                continue;
            }

            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabRoot == null)
            {
                continue;
            }

            MapObject mapObject = prefabRoot.GetComponent<MapObject>();
            if (mapObject == null)
            {
                mapObject = prefabRoot.GetComponentInChildren<MapObject>(true);
            }

            if (mapObject == null || seen.Contains(mapObject))
            {
                continue;
            }

            seen.Add(mapObject);
            results.Add(mapObject);
        }

        results.Sort((left, right) =>
        {
            string leftName = left != null ? left.gameObject.name : string.Empty;
            string rightName = right != null ? right.gameObject.name : string.Empty;
            return string.Compare(leftName, rightName, System.StringComparison.OrdinalIgnoreCase);
        });

        return results;
    }

    private void EnsureVisibleSelection(List<ItemDefinition> definitions, List<ItemDefinition> visibleDefinitions)
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
            return;
        }

        if (definitions == null || FindDefinitionById(definitions, selectedItemId) == null)
        {
            selectedItemId = -1;
        }
    }

    private List<ItemDefinition> FilterDefinitions(List<ItemDefinition> definitions)
    {
        List<ItemDefinition> results = new List<ItemDefinition>();
        if (definitions == null)
        {
            return results;
        }

        string searchText = string.IsNullOrWhiteSpace(itemSearchText) ? string.Empty : itemSearchText.Trim();
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(searchText) || MatchesDefinitionSearch(definition, searchText))
            {
                results.Add(definition);
            }
        }

        return results;
    }

    private List<ItemDefinition> GetSynchronizedDefinitions(ItemManager itemManager)
    {
        if (!definitionCatalogDirty)
        {
            return synchronizedDefinitions;
        }

        ItemDataEditorWindow.DefinitionCatalog.Fill(synchronizedDefinitions, itemManager);
        definitionCatalogDirty = false;
        return synchronizedDefinitions;
    }

    private void InvalidateDefinitionCatalog()
    {
        definitionCatalogDirty = true;
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

    private ItemDefinition DrawDefinitionSelector(
        Rect iconRect,
        ItemDefinition currentDefinition,
        List<ItemDefinition> definitions,
        Action<ItemDefinition> onSelected)
    {
        if (definitions == null || definitions.Count == 0)
        {
            return currentDefinition;
        }

        Rect popupRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup, GUILayout.ExpandWidth(true));
        int currentItemId = currentDefinition != null ? currentDefinition.id : -1;
        GUIContent selectedContent = new GUIContent(
            $"[{currentItemId}] {GetDefinitionDisplayName(currentDefinition)}",
            GetItemIcon(currentDefinition));
        if (EditorGUI.DropdownButton(popupRect, selectedContent, FocusType.Keyboard, EditorStyles.popup))
        {
            PopupWindow.Show(
                popupRect,
                new IconSelectionPopupContent<ItemDefinition>(
                    definitions,
                    definition => definition != null && definition.id == currentItemId,
                    definition =>
                    {
                        int itemId = definition != null ? definition.id : -1;
                        return $"[{itemId}] {GetDefinitionDisplayName(definition)}";
                    },
                    DrawItemIcon,
                    onSelected));
        }

        ItemDefinition nextDefinition = currentDefinition;

        Rect dropRect = Rect.MinMaxRect(
            iconRect.xMin,
            Mathf.Min(iconRect.yMin, popupRect.yMin),
            popupRect.xMax,
            Mathf.Max(iconRect.yMax, popupRect.yMax));

        if (ItemDefinitionDragAndDropUtility.HandleDropTarget(dropRect, this, out ItemDefinition droppedDefinition))
        {
            nextDefinition = droppedDefinition;
        }

        return nextDefinition;
    }

    private MapObject DrawMapObjectPopup(
        MapObject currentSelection,
        List<MapObject> mapObjects,
        Rect iconRect,
        Action<MapObject> onSelected)
    {
        Rect popupRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup, GUILayout.ExpandWidth(true));
        GUIContent selectedContent = new GUIContent(
            GetMapObjectDisplayName(currentSelection),
            GetMapObjectIcon(currentSelection));
        if (EditorGUI.DropdownButton(popupRect, selectedContent, FocusType.Keyboard, EditorStyles.popup))
        {
            List<MapObject> options = new List<MapObject>(mapObjects.Count + 1) { null };
            options.AddRange(mapObjects);
            PopupWindow.Show(
                popupRect,
                new IconSelectionPopupContent<MapObject>(
                    options,
                    mapObject => mapObject == currentSelection,
                    GetMapObjectDisplayName,
                    DrawMapObjectIcon,
                    onSelected));
        }

        Rect dropRect = Rect.MinMaxRect(
            iconRect.xMin,
            Mathf.Min(iconRect.yMin, popupRect.yMin),
            popupRect.xMax,
            Mathf.Max(iconRect.yMax, popupRect.yMax));
        return HandleMapObjectDropTarget(dropRect, this, mapObjects, out MapObject droppedMapObject)
            ? droppedMapObject
            : currentSelection;
    }

    private static string GetMapObjectDisplayName(MapObject mapObject)
    {
        return mapObject != null ? mapObject.gameObject.name : "(None)";
    }

    private static bool HandleMapObjectDropTarget(
        Rect rect,
        EditorWindow owner,
        List<MapObject> candidates,
        out MapObject droppedMapObject)
    {
        droppedMapObject = ResolveDraggedMapObject(candidates);
        if (droppedMapObject == null)
        {
            return false;
        }

        Event current = Event.current;
        if (current == null || !rect.Contains(current.mousePosition))
        {
            return false;
        }

        switch (current.type)
        {
            case EventType.DragUpdated:
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                owner?.Repaint();
                current.Use();
                break;

            case EventType.DragPerform:
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                DragAndDrop.AcceptDrag();
                GUI.changed = true;
                owner?.Repaint();
                current.Use();
                return true;

            case EventType.Repaint:
                DrawDropHighlight(rect);
                break;
        }

        return false;
    }

    private static MapObject ResolveDraggedMapObject(List<MapObject> candidates)
    {
        MapObject mapObject = ResolveDraggedMapObjectReference();
        if (mapObject != null)
        {
            return FindMatchingMapObjectCandidate(candidates, mapObject) ?? mapObject;
        }

        if (ItemDefinitionDragAndDropUtility.TryGetDraggedDefinition(out ItemDefinition draggedDefinition))
        {
            return FindMapObjectCandidateByItemId(candidates, draggedDefinition != null ? draggedDefinition.id : -1);
        }

        return null;
    }

    private static MapObject ResolveDraggedMapObjectReference()
    {
        UnityEngine.Object[] objectReferences = DragAndDrop.objectReferences;
        if (objectReferences == null)
        {
            return null;
        }

        for (int i = 0; i < objectReferences.Length; i++)
        {
            UnityEngine.Object reference = objectReferences[i];
            if (reference is MapObject mapObject)
            {
                return mapObject;
            }

            if (reference is GameObject gameObject)
            {
                MapObject candidate = gameObject.GetComponent<MapObject>();
                if (candidate == null)
                {
                    candidate = gameObject.GetComponentInChildren<MapObject>(true);
                }

                if (candidate != null)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static MapObject FindMatchingMapObjectCandidate(List<MapObject> candidates, MapObject mapObject)
    {
        if (candidates == null || mapObject == null)
        {
            return null;
        }

        string assetPath = GetMapObjectAssetPath(mapObject);
        int itemId = mapObject.ResolveItemId();
        for (int i = 0; i < candidates.Count; i++)
        {
            MapObject candidate = candidates[i];
            if (candidate == null)
            {
                continue;
            }

            if (candidate == mapObject)
            {
                return candidate;
            }

            if (!string.IsNullOrWhiteSpace(assetPath)
                && string.Equals(GetMapObjectAssetPath(candidate), assetPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            if (itemId >= 0 && candidate.ResolveItemId() == itemId)
            {
                return candidate;
            }
        }

        return null;
    }

    private static MapObject FindMapObjectCandidateByItemId(List<MapObject> candidates, int itemId)
    {
        if (candidates == null || itemId < 0)
        {
            return null;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            MapObject candidate = candidates[i];
            if (candidate != null && candidate.ResolveItemId() == itemId)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string GetMapObjectAssetPath(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return string.Empty;
        }

        return AssetDatabase.GetAssetPath(GetMapObjectPrefabRoot(mapObject));
    }

    private static GameObject GetMapObjectPrefabRoot(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return null;
        }

        return mapObject.transform.root != null
            ? mapObject.transform.root.gameObject
            : mapObject.gameObject;
    }

    private static void AddCraftingMapObjectIfMissing(List<MapObject> selectedObjects, MapObject mapObject)
    {
        if (selectedObjects == null || mapObject == null || selectedObjects.Contains(mapObject))
        {
            return;
        }

        selectedObjects.Add(mapObject);
    }

    private static void DrawDropHighlight(Rect rect)
    {
        EditorGUI.DrawRect(rect, DropFillColor);
        DrawOutline(rect, DropOutlineColor);
    }

    private static void DrawOutline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), color);
    }

    private static Texture GetItemIcon(ItemDefinition definition)
    {
        if (definition == null || definition.icon == null)
        {
            return null;
        }

        Texture preview = AssetPreview.GetAssetPreview(definition.icon);
        if (preview != null)
        {
            return preview;
        }

        Texture mini = AssetPreview.GetMiniThumbnail(definition.icon);
        if (mini != null)
        {
            return mini;
        }

        return definition.icon.texture;
    }

    private static Texture GetMapObjectIcon(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return null;
        }

        GameObject target = mapObject.gameObject;
        Texture preview = AssetPreview.GetAssetPreview(target);
        if (preview != null)
        {
            return preview;
        }

        return AssetPreview.GetMiniThumbnail(target);
    }

    private static void DrawMapObjectIcon(Rect rect, MapObject mapObject)
    {
        Texture icon = GetMapObjectIcon(mapObject);
        if (icon == null)
        {
            return;
        }

        Color previousColor = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
        GUI.color = previousColor;
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

    private static int FindDefinitionIndexById(List<ItemDefinition> definitions, int id)
    {
        if (definitions == null)
        {
            return -1;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static string GetDefinitionDisplayName(ItemDefinition definition)
    {
        if (definition == null)
        {
            return "(Missing)";
        }

        return string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName;
    }

    private static string GetPersistedItemName(ItemDefinition definition, List<ItemDefinition> definitions)
    {
        if (definition == null)
        {
            throw new InvalidDataException("CraftingTree에 저장할 ItemDefinition을 찾지 못했습니다.");
        }

        string itemName = ItemDefinitionLookup.GetPersistenceName(definition, definitions);
        if (string.IsNullOrWhiteSpace(itemName))
        {
            throw new InvalidDataException($"ItemDefinition '{definition.name}'의 아이템 이름이 비어 있습니다.");
        }

        return itemName.Trim();
    }

    private static void DrawIconBackground(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
    }

    private static void DrawItemIcon(Rect rect, ItemDefinition definition)
    {
        Texture icon = GetItemIcon(definition);
        if (icon == null)
        {
            return;
        }

        Color previousColor = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
        GUI.color = previousColor;
    }

    private static ItemManager FindItemManager()
    {
        ItemManager[] managers = Resources.FindObjectsOfTypeAll<ItemManager>();
        if (managers == null || managers.Length == 0)
        {
            return null;
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
}
