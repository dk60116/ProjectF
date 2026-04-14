using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class CraftingTreeEditorWindow : EditorWindow
{
    private const float SidebarWidth = 260f;
    private const int CraftingTreeFileVersion = 1;
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private int selectedItemId = -1;
    private readonly Dictionary<int, List<IngredientEntry>> recipeByItemId = new Dictionary<int, List<IngredientEntry>>();
    private readonly Dictionary<int, MapObject> craftingMapObjectByItemId = new Dictionary<int, MapObject>();

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

        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            string displayName = string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName;
            bool isSelected = definition.id == selectedItemId;
            Rect rowRect = GUILayoutUtility.GetRect(1f, 28f, GUILayout.ExpandWidth(true));
            GUIContent content = new GUIContent($"[{definition.id}] {displayName}", GetItemIcon(definition));
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
        GUILayout.Space(6f);
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
            int currentIndex = FindDefinitionIndexById(definitions, entry.itemId);
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            GUILayout.BeginHorizontal();
            Rect iconRect = GUILayoutUtility.GetRect(22f, 22f, GUILayout.ExpandWidth(false));
            DrawIconBackground(iconRect);
            ItemDefinition iconDefinition = definitions.Count > 0 ? definitions[currentIndex] : null;
            DrawItemIcon(iconRect, iconDefinition);
            int newIndex = DrawDefinitionPopup(currentIndex, definitions);
            int newItemId = (definitions.Count > 0 && newIndex >= 0 && newIndex < definitions.Count)
                ? definitions[newIndex].id
                : entry.itemId;
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
        EditorGUILayout.LabelField("Crafting MapObject", EditorStyles.boldLabel);

        if (mapObjects.Count == 0)
        {
            EditorGUILayout.HelpBox("MapObject 프리팹을 찾을 수 없습니다. Assets/MapObject 폴더를 확인하세요.", MessageType.Info);
            return;
        }

        MapObject currentSelection = GetCraftingMapObject(targetDefinition.id);
        int currentIndex = FindMapObjectIndex(mapObjects, currentSelection);

        EditorGUI.BeginChangeCheck();
        int newIndex;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("MapObject", GUILayout.Width(80f));
        newIndex = DrawMapObjectPopup(currentIndex, mapObjects);
        EditorGUILayout.EndHorizontal();
        if (EditorGUI.EndChangeCheck())
        {
            MapObject newSelection = newIndex > 0 ? mapObjects[newIndex - 1] : null;
            craftingMapObjectByItemId[targetDefinition.id] = newSelection;
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
    }

    private void WriteCraftingTree(string path, List<int> itemIds)
    {
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(CraftingTreeFileVersion);
            writer.Write(itemIds.Count);

            for (int i = 0; i < itemIds.Count; i++)
            {
                int itemId = itemIds[i];
                writer.Write(itemId);

                string mapObjectGuid = GetMapObjectGuid(GetCraftingMapObject(itemId));
                writer.Write(mapObjectGuid ?? string.Empty);

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
        craftingMapObjectByItemId.Clear();

        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            int version = reader.ReadInt32();
            if (version != CraftingTreeFileVersion)
            {
                EditorUtility.DisplayDialog("CraftingTree", $"버전이 맞지 않습니다. (파일: {version})", "OK");
                return;
            }

            int itemCount = reader.ReadInt32();
            for (int i = 0; i < itemCount; i++)
            {
                int itemId = reader.ReadInt32();
                string mapGuid = reader.ReadString();
                MapObject mapObject = ResolveMapObjectFromGuid(mapGuid);
                craftingMapObjectByItemId[itemId] = mapObject;

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

        foreach (int key in craftingMapObjectByItemId.Keys)
        {
            ids.Add(key);
        }

        List<int> results = new List<int>(ids);
        results.Sort();
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

    private List<IngredientEntry> GetOrCreateRecipe(int itemId)
    {
        if (!recipeByItemId.TryGetValue(itemId, out List<IngredientEntry> recipe))
        {
            recipe = new List<IngredientEntry>();
            recipeByItemId[itemId] = recipe;
        }

        return recipe;
    }

    private MapObject GetCraftingMapObject(int itemId)
    {
        if (craftingMapObjectByItemId.TryGetValue(itemId, out MapObject value))
        {
            return value;
        }

        return null;
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

    private int DrawDefinitionPopup(int currentIndex, List<ItemDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0)
        {
            return -1;
        }

        List<GUIContent> options = BuildDefinitionContents(definitions);
        int safeIndex = Mathf.Clamp(currentIndex, 0, options.Count - 1);
        return EditorGUILayout.Popup(safeIndex, options.ToArray());
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
