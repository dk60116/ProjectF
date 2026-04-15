using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class ItemDataEditorWindow : EditorWindow
{
    private const float SidebarWidth = 260f;

    private Vector2 listScroll;
    private Vector2 detailScroll;
    private int selectedItemId = -1;
    private string itemSearchText = string.Empty;

    [Serializable]
    private class ItemDataJsonFile
    {
        public List<ItemDataJsonEntry> items = new List<ItemDataJsonEntry>();
        public List<ItemDataJsonEntry> definitions = new List<ItemDataJsonEntry>();
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
        public int size;
        public string energyType;
        public int energyAmount;
        public string useEnergyType;
        public int useEnergyAmount;
        public int mapSizeX = -1;
        public int mapSizeY = -1;
        public string mapFilter;
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
        EnsureSelection();
    }

    private void OnFocus()
    {
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

        List<ItemDefinition> definitions = GetDefinitions(itemManager);
        if (definitions.Count == 0)
        {
            EditorGUILayout.HelpBox("ItemDefinitions가 비어있습니다.", MessageType.Warning);
            GUILayout.EndArea();
            return;
        }

        List<ItemDefinition> visibleDefinitions = FilterDefinitions(definitions);
        EnsureSelection(definitions, visibleDefinitions);

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
            bool pressed = GUI.Toggle(rowRect, isSelected, content, "Button");
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
        if (GUILayout.Button("Save", GUILayout.Width(70f)))
        {
            SaveItemData();
        }

        if (GUILayout.Button("Load", GUILayout.Width(70f)))
        {
            LoadItemData();
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

        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        DrawSelectedItemFields(selectedDefinition);
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
        string assetPath = AssetDatabase.GetAssetPath(definition);
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

    private void DrawSelectedItemFields(ItemDefinition definition)
    {
        SerializedObject serializedObject = new SerializedObject(definition);
        serializedObject.Update();

        SerializedProperty itemNameProperty = serializedObject.FindProperty("itemName");
        SerializedProperty idProperty = serializedObject.FindProperty("id");
        SerializedProperty mapObjectProperty = serializedObject.FindProperty("mapObject");
        SerializedProperty portableMeshProperty = serializedObject.FindProperty("portableMesh");
        SerializedProperty portableMatProperty = serializedObject.FindProperty("portableMat");
        SerializedProperty iconProperty = serializedObject.FindProperty("icon");
        SerializedProperty sizeProperty = serializedObject.FindProperty("size");
        SerializedProperty energyTypeProperty = serializedObject.FindProperty("energyType");
        SerializedProperty energyAmountProperty = serializedObject.FindProperty("energyAmount");
        SerializedProperty useEnergyTypeProperty = serializedObject.FindProperty("useEnergyType");
        SerializedProperty useEnergyAmountProperty = serializedObject.FindProperty("useEnergyAmount");

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
        DrawMapObjectFields(mapObjectProperty.objectReferenceValue as MapObject);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(sizeProperty, new GUIContent("Size"));

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
                    useEnergyAmountProperty.longValue = 0;
                }
            }
            else if (useEnergyAmountProperty != null)
            {
                EditorGUILayout.PropertyField(useEnergyAmountProperty, new GUIContent("Use Energy Amount"));
            }
        }

        if (serializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(definition);
            Repaint();
        }
    }

    private void DrawMapObjectFields(MapObject mapObject)
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

        if (mapObject is InstallationObject)
        {
            SerializedProperty mapFilterProperty = mapObjectSerializedObject.FindProperty("mapFilter");
            if (mapFilterProperty != null)
            {
                InstallationMapFilter currentFilter = (InstallationMapFilter)mapFilterProperty.intValue;
                if (currentFilter == InstallationMapFilter.None)
                {
                    currentFilter = InstallationMapFilter.Ground;
                }

                EditorGUI.BeginChangeCheck();
                InstallationMapFilter nextFilter = (InstallationMapFilter)EditorGUILayout.EnumFlagsField("Map Filter", currentFilter);
                if (nextFilter == InstallationMapFilter.None)
                {
                    nextFilter = InstallationMapFilter.Ground;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    mapFilterProperty.intValue = (int)nextFilter;
                }
            }
        }

        if (mapObjectSerializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(mapObject);
            GameObject owner = mapObject.gameObject;
            if (owner != null)
            {
                EditorUtility.SetDirty(owner);
            }
            Repaint();
        }
    }

    private void SaveItemData()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
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
        EnsureSelection(GetDefinitions(itemManager));
        Repaint();
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

            file.items.Add(BuildJsonEntry(definition));
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
            ItemDefinition definition = ResolveDefinitionForJsonEntry(definitions, entries[i]);
            if (definition == null)
            {
                continue;
            }

            ApplyJsonEntry(definition, entries[i]);
            appliedCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
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
            energyType = definition.energyType.ToString(),
            energyAmount = Mathf.Max(0, definition.energyAmount),
            useEnergyType = definition.useEnergyType.ToString(),
            useEnergyAmount = Mathf.Max(0, definition.useEnergyAmount)
        };

        if (definition.mapObject != null)
        {
            GameObject prefabRoot = definition.mapObject.transform.root != null
                ? definition.mapObject.transform.root.gameObject
                : definition.mapObject.gameObject;
            entry.mapObjectAssetPath = AssetDatabase.GetAssetPath(prefabRoot);
            entry.mapSizeX = definition.mapObject.Status.mapSizeX;
            entry.mapSizeY = definition.mapObject.Status.mapSizeY;

            if (definition.mapObject is InstallationObject installationObject)
            {
                entry.mapFilter = installationObject.MapFilter.ToString();
            }
        }

        return entry;
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

        if (file.definitions != null && file.definitions.Count > 0)
        {
            return file.definitions;
        }

        return new List<ItemDataJsonEntry>();
    }

    private static ItemDefinition ResolveDefinitionForJsonEntry(List<ItemDefinition> definitions, ItemDataJsonEntry entry)
    {
        if (definitions == null || entry == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(entry.definitionAssetPath))
        {
            ItemDefinition assetMatch = AssetDatabase.LoadAssetAtPath<ItemDefinition>(entry.definitionAssetPath);
            if (assetMatch != null)
            {
                return assetMatch;
            }
        }

        if (entry.id >= 0)
        {
            ItemDefinition idMatch = FindDefinitionById(definitions, entry.id);
            if (idMatch != null)
            {
                return idMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.itemName))
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition candidate = definitions[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(GetDefinitionDisplayName(candidate), entry.itemName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.itemName, entry.itemName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.name, entry.itemName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void ApplyJsonEntry(ItemDefinition definition, ItemDataJsonEntry entry)
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
        definition.energyType = ParseEnergyType(entry.energyType, definition.energyType);
        definition.energyAmount = definition.energyType == ItemDefinition.EnergyType.None ? 0 : Mathf.Max(0, entry.energyAmount);
        definition.useEnergyType = ParseEnergyType(entry.useEnergyType, definition.useEnergyType);
        definition.useEnergyAmount = definition.useEnergyType == ItemDefinition.EnergyType.None ? 0 : Mathf.Max(0, entry.useEnergyAmount);

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

        MapObject mapObject = LoadMapObject(entry.mapObjectAssetPath);
        if (mapObject != null)
        {
            definition.mapObject = mapObject;
        }

        ApplyMapObjectJson(definition.mapObject, entry, definition);
        EditorUtility.SetDirty(definition);
    }

    private static void ApplyMapObjectJson(MapObject mapObject, ItemDataJsonEntry entry, ItemDefinition definition)
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
        }

        if (mapObject is InstallationObject)
        {
            SerializedProperty mapFilterProperty = serializedMapObject.FindProperty("mapFilter");
            if (mapFilterProperty != null && !string.IsNullOrWhiteSpace(entry.mapFilter))
            {
                if (Enum.TryParse(entry.mapFilter, true, out InstallationMapFilter parsedFilter))
                {
                    mapFilterProperty.intValue = (int)(parsedFilter == InstallationMapFilter.None ? InstallationMapFilter.Ground : parsedFilter);
                }
            }
        }

        if (serializedMapObject.ApplyModifiedPropertiesWithoutUndo())
        {
            EditorUtility.SetDirty(mapObject);
            if (mapObject.gameObject != null)
            {
                EditorUtility.SetDirty(mapObject.gameObject);
            }
        }
    }

    private static ItemDefinition.EnergyType ParseEnergyType(string rawValue, ItemDefinition.EnergyType fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        return Enum.TryParse(rawValue, true, out ItemDefinition.EnergyType parsedType)
            ? parsedType
            : fallback;
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

        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
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

    private static List<ItemDefinition> GetDefinitions(ItemManager itemManager)
    {
        List<ItemDefinition> results = new List<ItemDefinition>();
        if (itemManager == null || itemManager.ItemDefinitions == null)
        {
            return results;
        }

        for (int i = 0; i < itemManager.ItemDefinitions.Count; i++)
        {
            ItemDefinition definition = itemManager.ItemDefinitions[i];
            if (definition != null)
            {
                results.Add(definition);
            }
        }

        results.Sort((left, right) => left.id.CompareTo(right.id));
        return results;
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
}
