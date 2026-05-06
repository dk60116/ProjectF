using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class ItemDataEditorWindow : EditorWindow
{
    private const float SidebarWidth = 260f;
    private const float GiveButtonWidth = 46f;
    private const float RectGridCellSize = 34f;
    private const float RectGridCellSpacing = 5f;
    private const float RectGridPaletteBlockWidth = 78f;
    private static readonly RectGridPaletteEntry[] RectGridPaletteEntries =
    {
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.Object, "Object", "Object", new Color(0.35f, 0.45f, 0.62f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.InputEnergy, "Input Energy", "Input\nEnergy", new Color(0.55f, 0.44f, 0.18f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.InputItem, "Input Item", "Input\nItem", new Color(0.23f, 0.48f, 0.32f, 1f)),
        new RectGridPaletteEntry(InputOutputModule.RectGridBlockType.Output, "Output", "Output", new Color(0.48f, 0.28f, 0.28f, 1f))
    };

    private Vector2 listScroll;
    private Vector2 detailScroll;
    private int selectedItemId = -1;
    private string itemSearchText = string.Empty;
    private ItemDefinition pendingReorderSelection;

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
        public List<ItemDataJsonEntry> definitions = new List<ItemDataJsonEntry>();
        public List<ItemDataJsonEntry> entries = new List<ItemDataJsonEntry>();
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
        public string energyType;
        public int energyTypeValue = -1;
        public int energyAmount;
        public string useEnergyType;
        public int useEnergyTypeValue = -1;
        public float useEnergyAmount;
        public float completeEnergy;
        public int mapSizeX = -1;
        public int mapSizeY = -1;
        public float focusRadius = -1f;
        public float workableFocusRadius = -1f;
        public float conveyorSpeed = -1f;
        public string multiFocusMode;
        public int multiFocusModeValue = -1;
        public string mapFilter;
        public int mapFilterValue = -1;
        public string mapObjectName;
        public string mapObjectType;
        public string inputOutputLayoutType;
        public int rectGridWidth;
        public int rectGridHeight;
        public List<RectGridCellJsonEntry> rectGridCells = new List<RectGridCellJsonEntry>();
        public List<RectGridBlockPlacementJsonEntry> rectGridBlocks = new List<RectGridBlockPlacementJsonEntry>();
        public List<InputOutputPairJsonEntry> ioPairs = new List<InputOutputPairJsonEntry>();
        public List<InputOutputJsonEntry> inputList = new List<InputOutputJsonEntry>();
        public List<InputOutputJsonEntry> outputList = new List<InputOutputJsonEntry>();
        public InputOutputJsonEntry output = null;
    }

    [Serializable]
    private class RectGridCellJsonEntry
    {
        public int x;
        public int y;
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
        bool allowReorder = string.IsNullOrWhiteSpace(itemSearchText);

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
            Rect selectRect = new Rect(rowRect.x, rowRect.y, Mathf.Max(1f, rowRect.width - GiveButtonWidth - 4f), rowRect.height);
            Rect giveRect = new Rect(selectRect.xMax + 4f, rowRect.y, GiveButtonWidth, rowRect.height);
            GUIContent content = new GUIContent($"[{definition.id}] {displayName}", GetItemIcon(definition));
            ItemDefinitionDragAndDropUtility.HandleListItemDrag(selectRect, definition, content.text, this);
            if (allowReorder)
            {
                HandleDefinitionReorderDropTarget(rowRect, itemManager, definitions, visibleDefinitions, i);
            }

            bool pressed = GUI.Toggle(selectRect, isSelected, content, "Button");
            if (pressed)
            {
                selectedItemId = definition.id;
            }

            EditorGUI.BeginDisabledGroup(!EditorApplication.isPlaying);
            if (GUI.Button(giveRect, "Give"))
            {
                TryGiveItemToPlayer(definition);
            }
            EditorGUI.EndDisabledGroup();
        }

        if (allowReorder)
        {
            Rect endDropRect = GUILayoutUtility.GetRect(1f, 16f, GUILayout.ExpandWidth(true));
            HandleDefinitionReorderDropTarget(endDropRect, itemManager, definitions, visibleDefinitions, visibleDefinitions.Count);
        }

        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
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
        EditorUtility.SetDirty(itemManager);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

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
        SerializedProperty energyTypeProperty = serializedObject.FindProperty("energyType");
        SerializedProperty energyAmountProperty = serializedObject.FindProperty("energyAmount");
        SerializedProperty useEnergyTypeProperty = serializedObject.FindProperty("useEnergyType");
        SerializedProperty useEnergyAmountProperty = serializedObject.FindProperty("useEnergyAmount");
        SerializedProperty completeEnergyProperty = serializedObject.FindProperty("completeEnergy");

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

        if (mapObjectProperty.objectReferenceValue is InstallationObject && interactionButtonListProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Interaction", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(interactionButtonListProperty, new GUIContent("Interaction Button List"), true);
            EditorGUI.EndDisabledGroup();
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
                    EditorGUILayout.PropertyField(useEnergyAmountProperty, new GUIContent("Use Energy Amount / Sec"));
                }

                if (completeEnergyProperty != null)
                {
                    EditorGUILayout.PropertyField(completeEnergyProperty, new GUIContent("Complete Energy"));
                }
            }
        }

        if (serializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(definition);
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
        return filter == InstallationMapFilter.None ? InstallationMapFilter.Ground : filter;
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

        SerializedProperty multiFocusModeProperty = mapObjectSerializedObject.FindProperty("multiFocusMode");
        if (multiFocusModeProperty != null)
        {
            DrawMultiFocusModeField(multiFocusModeProperty);
        }

        SerializedProperty focusActivationRadiusProperty = GetMapObjectFocusRadiusProperty(mapObjectSerializedObject, mapObject);
        if (focusActivationRadiusProperty != null)
        {
            focusActivationRadiusProperty.floatValue = Mathf.Max(0f, focusActivationRadiusProperty.floatValue);
            EditorGUILayout.PropertyField(focusActivationRadiusProperty, new GUIContent("Focus Radius"));
        }

        if (mapObject is ConveyorBelt)
        {
            SerializedProperty conveyorSpeedProperty = mapObjectSerializedObject.FindProperty("conveyorSpeed");
            if (conveyorSpeedProperty != null)
            {
                conveyorSpeedProperty.floatValue = Mathf.Max(0f, conveyorSpeedProperty.floatValue);
                EditorGUILayout.PropertyField(conveyorSpeedProperty, new GUIContent("Conveyor Speed"));
            }
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
        EditorGUILayout.LabelField("Input / Output Pairs", EditorStyles.miniBoldLabel);

        for (int i = 0; i < inputListProperty.arraySize; i++)
        {
            SerializedProperty inputEntryProperty = inputListProperty.GetArrayElementAtIndex(i);
            SerializedProperty outputEntryProperty = outputListProperty.GetArrayElementAtIndex(i);
            DrawInputOutputPairRow(inputEntryProperty, outputEntryProperty, definitions, i, () =>
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

        GUILayout.Space(8f);
        DrawInputOutputRectGridFields(mapObjectSerializedObject);
    }

    private void DrawInputOutputRectGridFields(SerializedObject mapObjectSerializedObject)
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

        SerializedProperty slotLayoutTypeProperty = mapObjectSerializedObject.FindProperty("slotLayoutType");
        SerializedProperty rectGridWidthProperty = mapObjectSerializedObject.FindProperty("rectGridWidth");
        SerializedProperty rectGridHeightProperty = mapObjectSerializedObject.FindProperty("rectGridHeight");
        SerializedProperty rectGridCellsProperty = mapObjectSerializedObject.FindProperty("rectGridCells");
        if (slotLayoutTypeProperty == null || rectGridWidthProperty == null || rectGridHeightProperty == null || rectGridCellsProperty == null)
        {
            return;
        }

        EditorGUILayout.LabelField("Slot Layout", EditorStyles.miniBoldLabel);
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

        if (mapObject is WorkableObject || mapObject is BoxObject)
        {
            return serializedMapObject.FindProperty("focusActivationRadius");
        }

        if (mapObject is InstallationObject)
        {
            return serializedMapObject.FindProperty("installationFocusRadius");
        }

        return null;
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
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < RectGridPaletteEntries.Length; i++)
        {
            DrawRectGridPaletteBlock(RectGridPaletteEntries[i], RectGridPaletteBlockWidth, cellSize);
            if (i < RectGridPaletteEntries.Length - 1)
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

        GUIStyle labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = 9,
            padding = new RectOffset(2, 2, 1, 1),
            normal = { textColor = Color.white }
        };
        GUI.Label(blockRect, entry.displayLabel, labelStyle);
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

        GUIStyle labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = 8,
            padding = new RectOffset(1, 1, 1, 1),
            normal = { textColor = Color.white }
        };
        GUI.Label(insetRect, GetRectGridBlockDisplayLabel(inputOutputModule, blockType, cell), labelStyle);
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
        if (blockType == InputOutputModule.RectGridBlockType.InputItem)
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
            if (placement.blockType != InputOutputModule.RectGridBlockType.InputItem)
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

    private void DrawInputOutputPairRow(
        SerializedProperty inputEntryProperty,
        SerializedProperty outputEntryProperty,
        List<ItemDefinition> definitions,
        int pairIndex,
        Action removeAction)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Pair {pairIndex + 1}", EditorStyles.miniBoldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("X", GUILayout.Width(24f)) && removeAction != null)
        {
            removeAction.Invoke();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.EndHorizontal();
        DrawInputOutputEntryFields(inputEntryProperty, definitions, "Input");
        GUILayout.Space(4f);
        DrawInputOutputEntryFields(outputEntryProperty, definitions, "Output");
        EditorGUILayout.EndVertical();
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
        ItemDefinition[] dropdownDefinitions = BuildInputOutputDefinitionOptions(definitions);
        GUIContent[] dropdownOptions = BuildInputOutputDefinitionOptionContents(dropdownDefinitions);
        int currentIndex = GetInputOutputDefinitionOptionIndex(currentDefinition, dropdownDefinitions);
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
        int nextCount = EditorGUILayout.IntField("Count", Mathf.Max(1, countProperty.intValue));
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

        Texture icon = GetItemIcon(definition);
        Rect iconRect = GUILayoutUtility.GetRect(24f, 24f, GUILayout.Width(24f), GUILayout.Height(24f));
        DrawIconBackground(iconRect);
        if (icon != null)
        {
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
        }

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
            contents[i] = new GUIContent(label, GetItemIcon(definition));
        }

        return contents;
    }

    private static int GetInputOutputDefinitionOptionIndex(ItemDefinition currentDefinition, ItemDefinition[] options)
    {
        if (currentDefinition == null || options == null)
        {
            return 0;
        }

        for (int i = 1; i < options.Length; i++)
        {
            if (options[i] == currentDefinition)
            {
                return i;
            }
        }

        return 0;
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
        EditorUtility.SetDirty(itemManager);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EnsureSelection(GetDefinitions(itemManager));
        ShowNotification(new GUIContent("Item Data rebuilt."));
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

            ItemDataJsonEntry entry = BuildJsonEntry(definition);
            file.items.Add(entry);
            file.definitions.Add(entry);
            file.entries.Add(entry);
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
            energyType = definition.energyType.ToString(),
            energyTypeValue = (int)definition.energyType,
            energyAmount = Mathf.Max(0, definition.energyAmount),
              useEnergyType = definition.useEnergyType.ToString(),
              useEnergyTypeValue = (int)definition.useEnergyType,
              useEnergyAmount = Mathf.Max(0f, definition.useEnergyAmount),
              completeEnergy = Mathf.Max(0f, definition.completeEnergy)
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
            entry.mapObjectName = prefabRoot != null ? prefabRoot.name : definition.mapObject.name;
            entry.mapObjectType = definition.mapObject.GetType().FullName;
            entry.mapSizeX = definition.mapObject.Status.mapSizeX;
            entry.mapSizeY = definition.mapObject.Status.mapSizeY;
            entry.multiFocusMode = definition.mapObject.FocusMode.ToString();
            entry.multiFocusModeValue = (int)definition.mapObject.FocusMode;
            if (definition.mapObject is WorkableObject workableObject)
            {
                entry.focusRadius = workableObject.FocusActivationRadius;
                entry.workableFocusRadius = workableObject.FocusActivationRadius;
            }
            else if (definition.mapObject is BoxObject boxObject)
            {
                entry.focusRadius = boxObject.FocusActivationRadius;
            }
            else if (definition.mapObject is InstallationObject installationObjectWithFocus)
            {
                entry.focusRadius = installationObjectWithFocus.FocusActivationRadius;
            }

            if (definition.mapObject is ConveyorBelt conveyorBelt)
            {
                entry.conveyorSpeed = conveyorBelt.ConveyorSpeed;
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
                IReadOnlyList<InputOutputModule.RectGridCell> rectGridCells = inputOutputModule.RectGridCells;
                for (int i = 0; i < rectGridCells.Count; i++)
                {
                    entry.rectGridCells.Add(new RectGridCellJsonEntry
                    {
                        x = rectGridCells[i].x,
                        y = rectGridCells[i].y
                    });
                }

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
                    entry.inputList.Add(inputJsonEntry);
                    entry.outputList.Add(outputJsonEntry);
                    entry.ioPairs.Add(new InputOutputPairJsonEntry
                    {
                        input = inputJsonEntry,
                        output = outputJsonEntry
                    });
                }

                if (entry.outputList.Count > 0)
                {
                    entry.output = entry.outputList[0];
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

        if (file.definitions != null && file.definitions.Count > 0)
        {
            return file.definitions;
        }

        if (file.entries != null && file.entries.Count > 0)
        {
            return file.entries;
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
        definition.energyType = ParseEnergyType(entry.energyType, entry.energyTypeValue, definition.energyType);
        definition.energyAmount = definition.energyType == ItemDefinition.EnergyType.None ? 0 : Mathf.Max(0, entry.energyAmount);
        definition.useEnergyType = ParseEnergyType(entry.useEnergyType, entry.useEnergyTypeValue, definition.useEnergyType);
        definition.useEnergyAmount = definition.useEnergyType == ItemDefinition.EnergyType.None ? 0f : Mathf.Max(0f, entry.useEnergyAmount);
        definition.completeEnergy = definition.useEnergyType == ItemDefinition.EnergyType.None ? 0f : Mathf.Max(0f, entry.completeEnergy);

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

        float savedFocusRadius = entry.focusRadius >= 0f ? entry.focusRadius : entry.workableFocusRadius;
        if (savedFocusRadius >= 0f)
        {
            SerializedProperty focusActivationRadiusProperty = GetMapObjectFocusRadiusProperty(serializedMapObject, mapObject);
            if (focusActivationRadiusProperty != null)
            {
                focusActivationRadiusProperty.floatValue = Mathf.Max(0f, savedFocusRadius);
            }
        }

        if (entry.conveyorSpeed >= 0f)
        {
            SerializedProperty conveyorSpeedProperty = serializedMapObject.FindProperty("conveyorSpeed");
            if (conveyorSpeedProperty != null)
            {
                conveyorSpeedProperty.floatValue = Mathf.Max(0f, entry.conveyorSpeed);
            }
        }

        if (mapObject is InputOutputModule)
        {
            ApplyInputOutputModuleJson(serializedMapObject, entry, definitions);
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

        if (entry.ioPairs != null && entry.ioPairs.Count > 0)
        {
            for (int i = 0; i < entry.ioPairs.Count; i++)
            {
                ApplyInputOutputPairJson(inputListProperty, outputListProperty, entry.ioPairs[i], definitions);
            }
        }
        else if (entry.inputList != null)
        {
            for (int i = 0; i < entry.inputList.Count; i++)
            {
                InputOutputJsonEntry inputEntry = entry.inputList[i];
                InputOutputJsonEntry outputEntry = GetLegacyOutputEntry(entry, i);

                if (inputListProperty != null)
                {
                    int inputIndex = inputListProperty.arraySize;
                    inputListProperty.InsertArrayElementAtIndex(inputIndex);
                    ApplyInputOutputEntryJson(inputListProperty.GetArrayElementAtIndex(inputIndex), inputEntry, definitions);
                }

                if (outputListProperty != null)
                {
                    int outputIndex = outputListProperty.arraySize;
                    outputListProperty.InsertArrayElementAtIndex(outputIndex);
                    ApplyInputOutputEntryJson(outputListProperty.GetArrayElementAtIndex(outputIndex), outputEntry, definitions);
                }
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

        MapObject.MultiFocusMode currentMode = (MapObject.MultiFocusMode)multiFocusModeProperty.intValue;
        int selectedIndex = currentMode == MapObject.MultiFocusMode.All ? 1 : 0;

        EditorGUI.BeginChangeCheck();
        selectedIndex = EditorGUILayout.Popup("Multi Focus", selectedIndex, new[] { "NearOne", "All" });
        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        multiFocusModeProperty.intValue = selectedIndex == 0
            ? (int)MapObject.MultiFocusMode.NearOne
            : (int)MapObject.MultiFocusMode.All;
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

    private static InputOutputJsonEntry GetLegacyOutputEntry(ItemDataJsonEntry entry, int index)
    {
        if (entry == null)
        {
            return null;
        }

        if (entry.outputList != null && index >= 0 && index < entry.outputList.Count)
        {
            return entry.outputList[index];
        }

        return entry.output;
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

        itemDefinitionProperty.objectReferenceValue = ResolveDefinitionReference(definitions, entry.definitionAssetPath, entry.id, entry.itemName);
        countProperty.intValue = Mathf.Max(1, entry.count);
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

        SortDefinitionsById(results);
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
