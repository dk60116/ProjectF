using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class CraftingTreeEditorWindow : EditorWindow
{
    private const float SidebarWidth = 260f;
    private const int CurrentCraftingTreeFileVersion = 4;
    private const int MultiCraftingMapObjectGuidFileVersion = 3;
    private const int OutputCountCraftingTreeFileVersion = 2;
    private const int LegacyCraftingTreeFileVersion = 1;
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private int selectedItemId = -1;
    private string itemSearchText = string.Empty;
    private readonly Dictionary<int, List<IngredientEntry>> recipeByItemId = new Dictionary<int, List<IngredientEntry>>();
    private readonly Dictionary<int, List<MapObject>> craftingMapObjectsByItemId = new Dictionary<int, List<MapObject>>();
    private readonly Dictionary<int, int> outputCountByItemId = new Dictionary<int, int>();

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

            window.LoadCraftingTree();
            window.Repaint();
        }
    }

    private void OnEnable()
    {
        LoadCraftingTree();
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

        List<ItemDefinition> definitions = itemManager.ItemDefinitions;
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

            string displayName = string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName;
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

        List<ItemDefinition> definitions = itemManager.ItemDefinitions;
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
        string displayName = string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName;
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
            ItemDefinition nextDefinition = DrawDefinitionSelector(iconRect, currentDefinition, definitions);
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
            selectedMapObjects.Add(GetNextCraftingMapObjectCandidate(mapObjects, selectedMapObjects));
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
            EditorGUILayout.HelpBox("필요한 Crafting MapObject를 추가하세요.", MessageType.None);
            return;
        }

        for (int i = selectedObjects.Count - 1; i >= 0; i--)
        {
            MapObject currentSelection = selectedObjects[i];
            int currentIndex = FindMapObjectIndex(mapObjects, currentSelection);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            Rect iconRect = GUILayoutUtility.GetRect(22f, 22f, GUILayout.ExpandWidth(false));
            DrawIconBackground(iconRect);
            DrawMapObjectIcon(iconRect, currentSelection);
            int newIndex = DrawMapObjectPopup(currentIndex, mapObjects);
            if (GUILayout.Button("X", GUILayout.Width(24f)))
            {
                selectedObjects.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                continue;
            }

            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                selectedObjects[i] = newIndex > 0 ? mapObjects[newIndex - 1] : null;
            }
        }
    }

    private void SaveCraftingTree()
    {
        string path = GetCraftingTreeAssetPath();
        string resourcePath = GetCraftingTreeResourcesPath();
        EnsureParentFolder(path);
        EnsureParentFolder(resourcePath);

        List<int> itemIds = CollectRecipeItemIds();

        WriteCraftingTree(path, itemIds);
        WriteCraftingTree(resourcePath, itemIds);

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

        List<ItemDefinition> definitions = itemManager.ItemDefinitions;
        string defaultPath = Path.Combine(Application.dataPath, "Data", "CraftingTree", "crafting_tree.json");
        string exportPath = EditorUtility.SaveFilePanel("Export CraftingTree JSON", Path.GetDirectoryName(defaultPath), Path.GetFileNameWithoutExtension(defaultPath), "json");
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            return;
        }

        CraftingTreeJsonFile file = new CraftingTreeJsonFile();
        List<int> itemIds = CollectRecipeItemIds();
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

        List<ItemDefinition> definitions = itemManager.ItemDefinitions;
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

    private void WriteCraftingTree(string path, List<int> itemIds)
    {
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(CurrentCraftingTreeFileVersion);
            writer.Write(itemIds.Count);

            for (int i = 0; i < itemIds.Count; i++)
            {
                int itemId = itemIds[i];
                writer.Write(itemId);

                List<int> mapObjectIds = GetMapObjectRuntimeIds(GetCraftingMapObjects(itemId));
                writer.Write(mapObjectIds.Count);
                for (int mapObjectIndex = 0; mapObjectIndex < mapObjectIds.Count; mapObjectIndex++)
                {
                    writer.Write(mapObjectIds[mapObjectIndex]);
                }

                writer.Write(GetOutputCount(itemId));

                List<IngredientEntry> recipe = GetOrCreateRecipe(itemId);
                writer.Write(recipe.Count);
                for (int j = 0; j < recipe.Count; j++)
                {
                    writer.Write(recipe[j].itemId);
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
            if (version != LegacyCraftingTreeFileVersion && version != CurrentCraftingTreeFileVersion)
            {
                EditorUtility.DisplayDialog("CraftingTree", $"버전이 맞지 않습니다. (파일: {version})", "OK");
                return;
            }

            int itemCount = reader.ReadInt32();
            for (int i = 0; i < itemCount; i++)
            {
                int itemId = reader.ReadInt32();
                List<MapObject> mapObjects = ReadCraftingMapObjects(reader, version);
                if (mapObjects.Count > 0)
                {
                    craftingMapObjectsByItemId[itemId] = mapObjects;
                }

                outputCountByItemId[itemId] = version >= OutputCountCraftingTreeFileVersion
                    ? Mathf.Max(1, reader.ReadInt32())
                    : 1;

                int ingredientCount = reader.ReadInt32();
                List<IngredientEntry> recipe = new List<IngredientEntry>(ingredientCount);
                for (int j = 0; j < ingredientCount; j++)
                {
                    int ingredientId = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    recipe.Add(new IngredientEntry(ingredientId, count));
                }

                recipeByItemId[itemId] = recipe;
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

        GameObject prefabRoot = mapObject.transform.root != null ? mapObject.transform.root.gameObject : mapObject.gameObject;
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

    private List<int> CollectRecipeItemIds()
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

        List<int> results = new List<int>(ids);
        results.Sort();
        return results;
    }

    private List<MapObject> ReadCraftingMapObjects(BinaryReader reader, int version)
    {
        List<MapObject> results = new List<MapObject>();

        if (version >= CurrentCraftingTreeFileVersion)
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

    private static string[] BuildMapObjectNames(List<MapObject> mapObjects)
    {
        string[] names = new string[mapObjects.Count + 1];
        names[0] = "(None)";

        for (int i = 0; i < mapObjects.Count; i++)
        {
            MapObject mapObject = mapObjects[i];
            string displayName = mapObject != null ? mapObject.gameObject.name : "(Missing)";
            names[i + 1] = displayName;
        }

        return names;
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

        results.Sort((left, right) =>
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

            int idCompare = left.id.CompareTo(right.id);
            if (idCompare != 0)
            {
                return idCompare;
            }

            string leftName = string.IsNullOrWhiteSpace(left.itemName) ? left.name : left.itemName;
            string rightName = string.IsNullOrWhiteSpace(right.itemName) ? right.name : right.itemName;
            return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        });

        return results;
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

        string displayName = string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName;
        if (!string.IsNullOrWhiteSpace(displayName) &&
            displayName.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(definition.name) &&
               definition.name.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private ItemDefinition DrawDefinitionSelector(Rect iconRect, ItemDefinition currentDefinition, List<ItemDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0)
        {
            return currentDefinition;
        }

        List<GUIContent> options = BuildDefinitionContents(definitions);
        int currentIndex = FindDefinitionIndexById(definitions, currentDefinition != null ? currentDefinition.id : -1);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        int safeIndex = Mathf.Clamp(currentIndex, 0, options.Count - 1);
        Rect popupRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup, GUILayout.ExpandWidth(true));
        int nextIndex = EditorGUI.Popup(popupRect, safeIndex, options.ToArray());
        ItemDefinition nextDefinition = nextIndex >= 0 && nextIndex < definitions.Count
            ? definitions[nextIndex]
            : currentDefinition;

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

    private static List<GUIContent> BuildDefinitionContents(List<ItemDefinition> definitions)
    {
        List<GUIContent> options = new List<GUIContent>(definitions.Count);
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            string displayName = definition == null
                ? "(Missing)"
                : (string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName);
            int id = definition != null ? definition.id : -1;
            GUIContent content = new GUIContent($"[{id}] {displayName}", GetItemIcon(definition));
            options.Add(content);
        }

        return options;
    }

    private int DrawMapObjectPopup(int currentIndex, List<MapObject> mapObjects)
    {
        List<GUIContent> options = BuildMapObjectContents(mapObjects);
        int safeIndex = Mathf.Clamp(currentIndex, 0, options.Count - 1);
        return EditorGUILayout.Popup(safeIndex, options.ToArray());
    }

    private static List<GUIContent> BuildMapObjectContents(List<MapObject> mapObjects)
    {
        List<GUIContent> options = new List<GUIContent>(mapObjects.Count + 1);
        options.Add(new GUIContent("(None)"));

        for (int i = 0; i < mapObjects.Count; i++)
        {
            MapObject mapObject = mapObjects[i];
            string displayName = mapObject != null ? mapObject.gameObject.name : "(Missing)";
            GUIContent content = new GUIContent(displayName, GetMapObjectIcon(mapObject));
            options.Add(content);
        }

        return options;
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

    private static int FindMapObjectIndex(List<MapObject> mapObjects, MapObject selected)
    {
        if (selected == null)
        {
            return 0;
        }

        for (int i = 0; i < mapObjects.Count; i++)
        {
            if (mapObjects[i] == selected)
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static string[] BuildDefinitionNames(List<ItemDefinition> definitions)
    {
        string[] names = new string[definitions.Count];
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            string displayName = definition == null ? "(Missing)" : (string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName);
            int id = definition != null ? definition.id : -1;
            names[i] = $"[{id}] {displayName}";
        }

        return names;
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
