using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using ProjectF.EditorTools;

public class ItemDataEditorWindow : EditorWindow
{
    internal static class DefinitionCatalog
    {
        internal const string AssetFolder = "Assets/Data/Items";

        internal static event Action Changed;

        internal static List<ItemDefinition> LoadCurrent()
        {
            List<ItemDefinition> definitions = new List<ItemDefinition>();
            Fill(definitions, FindItemManagerUncached());
            return definitions;
        }

        internal static void Fill(
            List<ItemDefinition> definitions,
            ItemManager itemManager)
        {
            definitions.Clear();
            if (itemManager != null && itemManager.ItemDefinitions != null)
            {
                for (int i = 0; i < itemManager.ItemDefinitions.Count; i++)
                {
                    AppendUniqueDefinition(
                        definitions,
                        itemManager.ItemDefinitions[i]);
                }
            }

            if (!EditorApplication.isPlaying)
            {
                AppendMissingItemDefinitionAssets(definitions);
            }

            SortDefinitionsById(definitions);
        }

        internal static int ComputeSignature(
            IReadOnlyList<ItemDefinition> definitions)
        {
            unchecked
            {
                int signature = definitions != null ? definitions.Count : 0;
                for (int i = 0; definitions != null && i < definitions.Count; i++)
                {
                    ItemDefinition definition = definitions[i];
                    signature = signature * 31
                        + (definition != null ? definition.GetHashCode() : 0);
                    if (definition == null)
                    {
                        continue;
                    }

                    signature = signature * 31 + definition.id;
                    signature = signature * 31
                        + (definition.itemName != null
                            ? definition.itemName.GetHashCode()
                            : 0);
                    signature = signature * 31
                        + (definition.name != null
                            ? definition.name.GetHashCode()
                            : 0);
                    signature = signature * 31
                        + (definition.icon != null
                            ? definition.icon.GetHashCode()
                            : 0);
                }

                return signature;
            }
        }

        internal static void NotifyChanged()
        {
            Changed?.Invoke();
        }
    }

    private const float SidebarWidth = 260f;
    private const float GiveButtonWidth = 46f;
    private const float ItemListRowHeight = 28f;
    private const float ItemFolderIndent = 14f;
    private const float ItemFolderDeleteButtonWidth = 22f;
    private const float ItemFolderDragHandleWidth = 16f;
    private const float ItemFolderDragStartDistance = 6f;
    private const string ItemFolderDragDataKey = "ProjectF.ItemDataFolder";
    private const int ItemListOverscanRows = 3;
    private const int LargeInputOutputPairAutoCollapseThreshold = 8;
    private const float RectGridCellSize = 34f;
    private const float RectGridCellSpacing = 5f;
    private const float RectGridPaletteBlockWidth = 78f;
    private const float PlacementCenterGridCellSize = 30f;
    private const float PlacementCenterGridCellSpacing = 4f;
    private const string ItemDefinitionAssetFolder = DefinitionCatalog.AssetFolder;
    private const string UiIconAtlasFolder = "Assets/Image/UI/Item";
    private const string ResourceUiIconFolder = "Assets/Image/UI/Resource";
    private const string UiIconAtlasPath = UiIconAtlasFolder + "/ItemUIIcons.spriteatlas";
    private const string ItemRebuildProgressTitle = "Item Data Rebuild";
    private const string TrainStationItemGuid = "2cbd885291664af429fdc0ef3784d40d";
    private static readonly string[] CompactTrainItemGuids =
    {
        "ad919f4ddfe2a924194a2ddac61bf5af",
        "228fcd45b59e4994d8b5f8ee23dc4595",
        "5e8eb859a7abfe04b919401e00125622",
        "1944e65739557ca4485c86a1309a15c6",
        "9524753a56ea05540be3c118174151dd",
        "4d68593c4bba3a34487b0f60cff9fd9e"
    };
    private static readonly string[] ItemLightModeLabels =
    {
        "None",
        "Always",
        "Toggle",
        "Night Only",
        "Working"
    };
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
    private readonly HashSet<ItemDefinition> selectedItemDefinitions = new HashSet<ItemDefinition>();
    private readonly List<ItemDefinition> selectedItemDefinitionsInOrder = new List<ItemDefinition>();
    private readonly List<ItemDefinition> draggedItemDefinitions = new List<ItemDefinition>();
    private readonly HashSet<ItemDefinition> availableItemDefinitions = new HashSet<ItemDefinition>();
    private readonly List<ItemDefinition> invalidSelectedItemDefinitions = new List<ItemDefinition>();
    private ItemDefinition rangeSelectionAnchor;
    private ItemDefinition pendingPlainSelectionDefinition;
    private Vector2 pendingPlainSelectionMouseDownPosition;
    private string itemSearchText = string.Empty;
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
    private readonly List<ItemListRow> cachedItemListRows = new List<ItemListRow>();
    private readonly List<ItemDefinition> cachedFolderOrderedDefinitions = new List<ItemDefinition>();
    private readonly List<ItemDefinition> pendingFolderOrderedDefinitions = new List<ItemDefinition>();
    private readonly Dictionary<ItemDefinition, string> cachedItemFolderIds =
        new Dictionary<ItemDefinition, string>();
    private string cachedVisibleDefinitionsSearchText = string.Empty;
    private int cachedVisibleDefinitionsVersion = -1;
    private string cachedItemListRowsSearchText = string.Empty;
    private int cachedItemListRowsDefinitionsVersion = -1;
    private int cachedItemListRowsFolderRevision = -1;
    private string pendingFolderDragId;
    private Vector2 pendingFolderDragMouseDownPosition;
    private bool folderItemIdNormalizationScheduled;
    private string editingFolderId;
    private string editingFolderName;
    private bool folderNameFocusPending;
    private ItemDefinition[] cachedInputOutputDefinitionOptions = Array.Empty<ItemDefinition>();
    private GUIContent[] cachedInputOutputDefinitionOptionContents = Array.Empty<GUIContent>();
    private readonly Dictionary<int, int> cachedInputOutputDefinitionOptionIndexes = new Dictionary<int, int>();
    private int cachedInputOutputDefinitionOptionsVersion = -1;
    private ItemDefinition[] cachedParentInputOutputModuleItemOptions = Array.Empty<ItemDefinition>();
    private GUIContent[] cachedParentInputOutputModuleItemOptionContents = Array.Empty<GUIContent>();
    private readonly Dictionary<int, int> cachedParentInputOutputModuleItemOptionIndexes = new Dictionary<int, int>();
    private int cachedParentInputOutputModuleItemOptionsVersion = -1;
    private ResourceDefinition[] cachedSeedTargetResourceOptions = Array.Empty<ResourceDefinition>();
    private GUIContent[] cachedSeedTargetResourceOptionContents = Array.Empty<GUIContent>();
    private readonly Dictionary<int, int> cachedSeedTargetResourceOptionIndexes = new Dictionary<int, int>();
    private int cachedSeedTargetResourceOptionsVersion = -1;
    private int cachedCraftingTreeIngredientSummaryVersion = -1;
    private readonly Dictionary<int, string> inputOutputTargetKeyCache = new Dictionary<int, string>();
    private readonly Dictionary<string, SerializedProperty> cachedSelectedDefinitionProperties =
        new Dictionary<string, SerializedProperty>(StringComparer.Ordinal);
    private readonly Dictionary<string, SerializedProperty> cachedMultiSelectedDefinitionProperties =
        new Dictionary<string, SerializedProperty>(StringComparer.Ordinal);
    private readonly Dictionary<string, SerializedProperty> cachedSelectedMapObjectProperties =
        new Dictionary<string, SerializedProperty>(StringComparer.Ordinal);
    private ItemDefinition cachedSerializedDefinitionTarget;
    private SerializedObject cachedSerializedDefinition;
    private ItemDefinition[] cachedMultiSerializedDefinitionTargets = Array.Empty<ItemDefinition>();
    private SerializedObject cachedMultiSerializedDefinition;
    private MapObject cachedSerializedMapObjectTarget;
    private SerializedObject cachedSerializedMapObject;
    private ConveyorBelt cachedSerializedConveyorTarget;
    private SerializedObject cachedSerializedConveyor;
    private static GUIStyle placementCenterLabelStyle;
    private static GUIStyle rectGridPaletteLabelStyle;
    private static GUIStyle rectGridBlockLabelStyle;
    private GUIStyle manualTargetPopupWithIconStyle;

    internal readonly struct ItemListRow
    {
        public readonly ItemDataFolderSettings.FolderEntry Folder;
        public readonly ItemDefinition Definition;
        public readonly ItemDefinition FolderIconDefinition;
        public readonly int ItemCount;

        private ItemListRow(
            ItemDataFolderSettings.FolderEntry folder,
            ItemDefinition definition,
            ItemDefinition folderIconDefinition,
            int itemCount)
        {
            Folder = folder;
            Definition = definition;
            FolderIconDefinition = folderIconDefinition;
            ItemCount = itemCount;
        }

        public bool IsFolder => Definition == null;

        public static ItemListRow CreateFolder(
            ItemDataFolderSettings.FolderEntry folder,
            ItemDefinition folderIconDefinition,
            int itemCount)
        {
            return new ItemListRow(folder, null, folderIconDefinition, itemCount);
        }

        public static ItemListRow CreateItem(ItemDefinition definition)
        {
            return new ItemListRow(null, definition, null, 0);
        }
    }

    private readonly struct ItemLayoutElement
    {
        public readonly ItemDataFolderSettings.FolderEntry Folder;
        public readonly ItemDefinition Definition;
        public readonly int DefinitionIndex;
        public readonly int FolderOrder;

        private ItemLayoutElement(
            ItemDataFolderSettings.FolderEntry folder,
            ItemDefinition definition,
            int definitionIndex,
            int folderOrder)
        {
            Folder = folder;
            Definition = definition;
            DefinitionIndex = definitionIndex;
            FolderOrder = folderOrder;
        }

        public static ItemLayoutElement CreateFolder(
            ItemDataFolderSettings.FolderEntry folder,
            int definitionIndex,
            int folderOrder)
        {
            return new ItemLayoutElement(folder, null, definitionIndex, folderOrder);
        }

        public static ItemLayoutElement CreateItem(ItemDefinition definition, int definitionIndex)
        {
            return new ItemLayoutElement(null, definition, definitionIndex, int.MaxValue);
        }
    }

    private sealed class ManualTargetItemPopupContent : PopupWindowContent
    {
        private const float PopupWidth = 340f;
        private const float RowHeight = 28f;
        private const float MaximumPopupHeight = 420f;
        private readonly ItemDefinition[] definitions;
        private readonly ItemDefinition selectedDefinition;
        private readonly Action<ItemDefinition> selectionCallback;
        private Vector2 scrollPosition;

        public ManualTargetItemPopupContent(
            ItemDefinition[] definitions,
            ItemDefinition selectedDefinition,
            Action<ItemDefinition> selectionCallback)
        {
            this.definitions = definitions ?? Array.Empty<ItemDefinition>();
            this.selectedDefinition = selectedDefinition;
            this.selectionCallback = selectionCallback;
        }

        public override Vector2 GetWindowSize()
        {
            float contentHeight = Mathf.Max(RowHeight, definitions.Length * RowHeight + 8f);
            return new Vector2(PopupWidth, Mathf.Min(MaximumPopupHeight, contentHeight));
        }

        public override void OnGUI(Rect rect)
        {
            Event currentEvent = Event.current;
            if (currentEvent != null && currentEvent.type == EventType.MouseMove)
            {
                editorWindow.Repaint();
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < definitions.Length; i++)
            {
                ItemDefinition definition = definitions[i];
                Rect rowRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
                bool isSelected = definition == selectedDefinition;
                bool isHovered = currentEvent != null && rowRect.Contains(currentEvent.mousePosition);
                if (isSelected || isHovered)
                {
                    EditorGUI.DrawRect(
                        rowRect,
                        isSelected
                            ? new Color(0.24f, 0.49f, 0.78f, 0.75f)
                            : new Color(1f, 1f, 1f, 0.08f));
                }

                Rect iconRect = new Rect(rowRect.x + 5f, rowRect.y + 3f, 22f, 22f);
                DrawIconBackground(iconRect);
                DrawItemIcon(iconRect, definition);

                string label = definition != null
                    ? $"[{definition.id}] {GetDefinitionDisplayName(definition)}"
                    : "(None)";
                Rect labelRect = new Rect(
                    iconRect.xMax + 7f,
                    rowRect.y,
                    rowRect.width - 38f,
                    RowHeight);
                GUI.Label(labelRect, label);

                if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
                {
                    selectionCallback?.Invoke(definition);
                    editorWindow.Close();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }

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
        public int version = 13;
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
        public string lightMode;
        public int lightModeValue = -1;
        public float lightRange = -1f;
        public float lightIntensityMultiplier = -1f;
        public int size;
        public bool itemFilter;
        public bool hasIgnoreFilter;
        public bool ignoreFilter;
        public bool hasOneItem;
        public bool oneItem;
        public bool hasManual;
        public bool isManual;
        public InputOutputJsonEntry manualTargetItem;
        public bool hasUpgradeable;
        public bool upgradeable = true;
        public int capacity = -1;
        public bool storesFluid;
        public float fluidStorageLiters;
        public float fluidOutputLitersPerSecond = -1f;
        public int undergroundPipeMaxDistance = -1;
        public bool hasFluidDisplayColor;
        public Color fluidDisplayColor = Color.white;
        public float bucketFillDurationSeconds = -1f;
        public float craftingDurationSeconds = -1f;
        public string energyType;
        public int energyTypeValue = -1;
        public int energyAmount;
        public bool hasEatReward;
        public InputOutputJsonEntry eatRewardItem;
        public float eatRewardChancePercent;
        public bool hasSeedSettings;
        public bool isSeed;
        public string seedTargetResourceAssetPath;
        public string useEnergyType;
        public int useEnergyTypeValue = -1;
        public float useEnergyAmount;
        public float completeEnergy;
        public int utilityPoleConnectionRadius = -1;
        public int utilityPoleSupplyRadius = -1;
        public int sprinklerRangeRadius = -1;
        public float sprinklerWaterLitersPerCell = -1f;
        public float sprinklerSprayIntervalSeconds = -1f;
        public float sprinklerNozzleRotationDegreesPerSecond = -1f;
        public float seedPlanterPlantDurationSeconds = -1f;
        public int mapSizeX = -1;
        public int mapSizeY = -1;
        public int placementCenterX = -1;
        public int placementCenterY = -1;
        public float focusRadius = -1f;
        public int workableRangeCells = -1;
        public float conveyorSpeed = -1f;
        public float vehicleAccelerationPerSecond = -1f;
        public float vehicleDecelerationPerSecond = -1f;
        public float vehicleMaxSpeed = -1f;
        public float vehicleMass = -1f;
        public string multiFocusMode;
        public int multiFocusModeValue = -1;
        public string mapFilter;
        public int mapFilterValue = -1;
        public string rotationFilter;
        public int rotationFilterValue = -1;
        public string inputOutputLayoutType;
        public InputOutputJsonEntry parentInputOutputModuleItem;
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

    [MenuItem("Tools/ProjectF/Normalize Item IDs")]
    public static void NormalizeItemIdsAndExportData()
    {
        List<ItemDefinition> definitions = LoadItemDefinitionsFromAssets();
        List<ItemDefinition> orderedDefinitions = BuildCompactItemDefinitionOrder(definitions);
        if (orderedDefinitions.Count == 0)
        {
            Debug.LogError("ItemDataEditorWindow: no ItemDefinitions found while normalizing item IDs.");
            return;
        }

        CraftingTreeItemIdRemapper.CapturedCraftingTree craftingTreeSnapshot =
            CraftingTreeItemIdRemapper.CapturePersistedCraftingTree(definitions);
        for (int i = 0; i < orderedDefinitions.Count; i++)
        {
            ItemDefinition definition = orderedDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            definition.id = i;
            EditorUtility.SetDirty(definition);
        }

        RenameItemDefinitionAssets(orderedDefinitions);

        ItemManager itemManager = FindItemManagerUncached();
        if (itemManager != null)
        {
            ApplyDefinitionOrderToItemManager(itemManager, orderedDefinitions);
            SyncItemManagerItemSets(itemManager, orderedDefinitions);
            itemManager.ApplyItemIdsToPrefabs();
            itemManager.MarkEditorDirty();
        }

        CraftingTreeItemIdRemapper.RewritePersistedCraftingTree(craftingTreeSnapshot, orderedDefinitions);
        WriteItemDataJson(GetDefaultItemDataJsonPath(), orderedDefinitions);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.SaveOpenScenes();
        CraftingTreeRuntime.ForceReload();
        CraftingTreeEditorWindow.ReloadOpenWindows();
        DefinitionCatalog.NotifyChanged();
        Debug.Log($"ItemDataEditorWindow: normalized {orderedDefinitions.Count} item IDs and exported item data.");
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
        if (folderItemIdNormalizationScheduled)
        {
            EditorApplication.delayCall -= NormalizeFolderItemIds;
            folderItemIdNormalizationScheduled = false;
        }

        pendingFolderOrderedDefinitions.Clear();
        CancelFolderNameEditing();
        ClearSelectedSerializedObjectCaches();
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
        DefinitionCatalog.NotifyChanged();
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
        ClearSelectedSerializedObjectCaches();
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
        InvalidateItemFolderPresentationCache();
        cachedInputOutputDefinitionOptions = Array.Empty<ItemDefinition>();
        cachedInputOutputDefinitionOptionContents = Array.Empty<GUIContent>();
        cachedInputOutputDefinitionOptionIndexes.Clear();
        cachedInputOutputDefinitionOptionsVersion = -1;
        cachedParentInputOutputModuleItemOptions = Array.Empty<ItemDefinition>();
        cachedParentInputOutputModuleItemOptionContents = Array.Empty<GUIContent>();
        cachedParentInputOutputModuleItemOptionIndexes.Clear();
        cachedParentInputOutputModuleItemOptionsVersion = -1;
        cachedSeedTargetResourceOptions = Array.Empty<ResourceDefinition>();
        cachedSeedTargetResourceOptionContents = Array.Empty<GUIContent>();
        cachedSeedTargetResourceOptionIndexes.Clear();
        cachedSeedTargetResourceOptionsVersion = -1;
        cachedCraftingTreeIngredientSummaries.Clear();
        cachedCraftingTreeIngredientSummaryVersion = -1;
    }

    private void InvalidateItemFolderPresentationCache()
    {
        cachedItemListRows.Clear();
        cachedFolderOrderedDefinitions.Clear();
        cachedItemFolderIds.Clear();
        cachedItemListRowsSearchText = string.Empty;
        cachedItemListRowsDefinitionsVersion = -1;
        cachedItemListRowsFolderRevision = -1;
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

        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            DrawItemFolderToolbar(0);
            EditorGUILayout.HelpBox("씬에서 ItemManager를 찾을 수 없습니다.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        List<ItemDefinition> definitions = GetDefinitions(itemManager);
        List<ItemDefinition> visibleDefinitions = FilterDefinitions(definitions);
        DrawItemFolderToolbar(visibleDefinitions.Count);
        if (definitions.Count == 0)
        {
            EditorGUILayout.HelpBox("ItemDefinitions가 비어있습니다.", MessageType.Warning);
            GUILayout.EndArea();
            return;
        }

        EnsureSelection(definitions, visibleDefinitions);
        EnsureMultiSelection(definitions);

        ItemDataFolderSettings folderSettings = ItemDataFolderSettings.instance;
        List<ItemListRow> itemListRows = BuildItemListRows(definitions, visibleDefinitions, folderSettings);
        ScheduleFolderItemIdNormalization(itemManager, definitions, folderSettings);

        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        if (visibleDefinitions.Count == 0)
        {
            EditorGUILayout.HelpBox("검색 결과가 없습니다.", MessageType.Info);
        }

        int firstVisibleIndex = GetFirstVisibleItemListIndex(itemListRows.Count);
        int lastVisibleIndex = GetLastVisibleItemListIndex(firstVisibleIndex, itemListRows.Count);
        if (firstVisibleIndex > 0)
        {
            GUILayout.Space(firstVisibleIndex * ItemListRowHeight);
        }

        for (int i = firstVisibleIndex;
             i <= lastVisibleIndex && i < itemListRows.Count;
             i++)
        {
            ItemListRow row = itemListRows[i];
            if (row.IsFolder)
            {
                DrawItemFolderRow(row, folderSettings, itemManager, definitions);
                continue;
            }

            DrawItemDefinitionRow(
                row,
                itemManager,
                definitions,
                visibleDefinitions,
                folderSettings);
        }

        int hiddenTrailingRowCount = itemListRows.Count - lastVisibleIndex - 1;
        if (hiddenTrailingRowCount > 0)
        {
            GUILayout.Space(hiddenTrailingRowCount * ItemListRowHeight);
        }

        Rect endDropRect = GUILayoutUtility.GetRect(1f, 16f, GUILayout.ExpandWidth(true));
        if (folderSettings.Folders.Count > 0)
        {
            HandleItemLayoutDropTarget(
                endDropRect,
                default,
                true,
                itemManager,
                definitions,
                folderSettings);
        }
        else
        {
            HandleDefinitionReorderDropTarget(
                endDropRect,
                itemManager,
                definitions,
                visibleDefinitions,
                visibleDefinitions.Count);
        }

        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawItemFolderToolbar(int visibleItemCount)
    {
        EditorGUILayout.BeginHorizontal();
        GUIContent itemCountContent = new GUIContent($"Items ({Mathf.Max(0, visibleItemCount)})");
        float itemCountWidth = EditorStyles.boldLabel.CalcSize(itemCountContent).x;
        GUILayout.Label(
            itemCountContent,
            EditorStyles.boldLabel,
            GUILayout.Width(itemCountWidth));
        if (selectedItemDefinitions.Count > 1)
        {
            string selectionCountText = $"{selectedItemDefinitions.Count} selected";
            float selectionCountWidth = EditorStyles.miniLabel.CalcSize(
                new GUIContent(selectionCountText)).x;
            GUILayout.Label(
                selectionCountText,
                EditorStyles.miniLabel,
                GUILayout.Width(selectionCountWidth));
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button(new GUIContent("+ Folder", "에디터 목록 정리용 폴더를 추가합니다."), GUILayout.Width(72f)))
        {
            ItemDataFolderSettings folderSettings = ItemDataFolderSettings.instance;
            ItemDataFolderSettings.FolderEntry folder = folderSettings.AddFolder();
            ItemManager itemManager = FindItemManager();
            List<ItemDefinition> definitions = itemManager != null
                ? GetDefinitions(itemManager)
                : null;
            ItemDefinition anchorDefinition = definitions != null
                ? FindDefinitionById(definitions, selectedItemId)
                : null;
            if (anchorDefinition == null && definitions != null && definitions.Count > 0)
            {
                anchorDefinition = definitions[0];
            }

            folderSettings.SetFolderPlacement(folder.Id, anchorDefinition);
            InvalidateItemFolderPresentationCache();
            GUI.FocusControl(null);
            Repaint();
        }

        GUILayout.Space(4f);
        EditorGUILayout.EndHorizontal();
    }

    private List<ItemListRow> BuildItemListRows(
        List<ItemDefinition> definitions,
        List<ItemDefinition> visibleDefinitions,
        ItemDataFolderSettings folderSettings)
    {
        string searchText = string.IsNullOrWhiteSpace(itemSearchText) ? string.Empty : itemSearchText.Trim();
        int folderRevision = folderSettings != null ? folderSettings.Revision : -1;
        if (cachedItemListRowsDefinitionsVersion == definitionsCacheVersion
            && cachedItemListRowsFolderRevision == folderRevision
            && string.Equals(cachedItemListRowsSearchText, searchText, StringComparison.Ordinal))
        {
            return cachedItemListRows;
        }

        BuildFolderLayoutRows(
            definitions,
            visibleDefinitions,
            folderSettings,
            !string.IsNullOrEmpty(searchText),
            cachedItemListRows,
            cachedFolderOrderedDefinitions,
            cachedItemFolderIds);

        CacheItemListRowState(searchText, folderRevision);
        return cachedItemListRows;
    }

    internal static void BuildFolderLayoutRows(
        List<ItemDefinition> definitions,
        List<ItemDefinition> visibleDefinitions,
        ItemDataFolderSettings folderSettings,
        bool forceExpandedForSearch,
        List<ItemListRow> itemListRows,
        List<ItemDefinition> folderOrderedDefinitions,
        Dictionary<ItemDefinition, string> itemFolderIds)
    {
        itemListRows?.Clear();
        folderOrderedDefinitions?.Clear();
        itemFolderIds?.Clear();
        if (definitions == null
            || visibleDefinitions == null
            || itemListRows == null
            || folderOrderedDefinitions == null
            || itemFolderIds == null)
        {
            return;
        }

        IReadOnlyList<ItemDataFolderSettings.FolderEntry> folders = folderSettings != null
            ? folderSettings.Folders
            : null;
        if (folders == null || folders.Count == 0)
        {
            folderOrderedDefinitions.AddRange(definitions);
            for (int i = 0; i < visibleDefinitions.Count; i++)
            {
                ItemDefinition definition = visibleDefinitions[i];
                if (definition != null)
                {
                    itemListRows.Add(ItemListRow.CreateItem(definition));
                }
            }

            return;
        }

        HashSet<ItemDefinition> visibleDefinitionSet = new HashSet<ItemDefinition>(visibleDefinitions);
        Dictionary<string, List<ItemDefinition>> membersByFolder =
            new Dictionary<string, List<ItemDefinition>>(StringComparer.Ordinal);
        Dictionary<string, int> firstMemberIndexByFolder =
            new Dictionary<string, int>(StringComparer.Ordinal);
        List<ItemLayoutElement> layoutElements = new List<ItemLayoutElement>();

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            string folderId = folderSettings.GetItemFolderId(definition);
            itemFolderIds[definition] = folderId;
            if (string.IsNullOrEmpty(folderId))
            {
                layoutElements.Add(ItemLayoutElement.CreateItem(definition, i));
                continue;
            }

            if (!membersByFolder.TryGetValue(folderId, out List<ItemDefinition> members))
            {
                members = new List<ItemDefinition>();
                membersByFolder.Add(folderId, members);
                firstMemberIndexByFolder.Add(folderId, i);
            }

            members.Add(definition);
        }

        for (int folderIndex = 0; folderIndex < folders.Count; folderIndex++)
        {
            ItemDataFolderSettings.FolderEntry folder = folders[folderIndex];
            if (folder == null)
            {
                continue;
            }

            int definitionIndex = firstMemberIndexByFolder.TryGetValue(folder.Id, out int firstMemberIndex)
                ? firstMemberIndex
                : ResolveFolderPlacementIndex(definitions, folder, folderSettings);
            layoutElements.Add(ItemLayoutElement.CreateFolder(folder, definitionIndex, folderIndex));
        }

        layoutElements.Sort(CompareItemLayoutElements);
        for (int i = 0; i < layoutElements.Count; i++)
        {
            ItemLayoutElement element = layoutElements[i];
            if (element.Folder == null)
            {
                folderOrderedDefinitions.Add(element.Definition);
                if (visibleDefinitionSet.Contains(element.Definition))
                {
                    itemListRows.Add(ItemListRow.CreateItem(element.Definition));
                }

                continue;
            }

            membersByFolder.TryGetValue(element.Folder.Id, out List<ItemDefinition> members);
            if (members != null)
            {
                folderOrderedDefinitions.AddRange(members);
            }

            int visibleMemberCount = CountVisibleFolderMembers(members, visibleDefinitionSet);
            if (forceExpandedForSearch && visibleMemberCount == 0)
            {
                continue;
            }

            ItemDefinition folderIconDefinition = members != null && members.Count > 0
                ? members[0]
                : null;
            itemListRows.Add(ItemListRow.CreateFolder(
                element.Folder,
                folderIconDefinition,
                visibleMemberCount));
            if (!element.Folder.Expanded && !forceExpandedForSearch)
            {
                continue;
            }

            AppendVisibleFolderMembers(members, visibleDefinitionSet, itemListRows);
        }
    }

    private void ScheduleFolderItemIdNormalization(
        ItemManager itemManager,
        List<ItemDefinition> definitions,
        ItemDataFolderSettings folderSettings)
    {
        if (folderItemIdNormalizationScheduled
            || itemManager == null
            || definitions == null
            || folderSettings == null
            || folderSettings.Folders.Count == 0
            || cachedFolderOrderedDefinitions.Count != definitions.Count
            || HaveSameDefinitionOrder(definitions, cachedFolderOrderedDefinitions))
        {
            return;
        }

        pendingFolderOrderedDefinitions.Clear();
        pendingFolderOrderedDefinitions.AddRange(cachedFolderOrderedDefinitions);
        folderItemIdNormalizationScheduled = true;
        EditorApplication.delayCall += NormalizeFolderItemIds;
    }

    private void NormalizeFolderItemIds()
    {
        EditorApplication.delayCall -= NormalizeFolderItemIds;
        folderItemIdNormalizationScheduled = false;
        if (this == null || pendingFolderOrderedDefinitions.Count == 0)
        {
            pendingFolderOrderedDefinitions.Clear();
            return;
        }

        ItemManager itemManager = FindItemManager();
        List<ItemDefinition> currentDefinitions = itemManager != null
            ? GetDefinitions(itemManager)
            : null;
        if (!HaveSameDefinitions(currentDefinitions, pendingFolderOrderedDefinitions))
        {
            pendingFolderOrderedDefinitions.Clear();
            InvalidateItemFolderPresentationCache();
            Repaint();
            return;
        }

        if (HaveSameDefinitionOrder(currentDefinitions, pendingFolderOrderedDefinitions))
        {
            pendingFolderOrderedDefinitions.Clear();
            return;
        }

        ItemDefinition selection = FindDefinitionById(currentDefinitions, selectedItemId);
        RegisterDefinitionOrderUndo(itemManager, currentDefinitions, "Normalize Folder Item IDs");
        CommitDefinitionOrderChange(
            itemManager,
            pendingFolderOrderedDefinitions,
            selection);
        pendingFolderOrderedDefinitions.Clear();
    }

    private static bool HaveSameDefinitionOrder(
        IReadOnlyList<ItemDefinition> left,
        IReadOnlyList<ItemDefinition> right)
    {
        if (left == null || right == null || left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool HaveSameDefinitions(
        IReadOnlyList<ItemDefinition> left,
        IReadOnlyList<ItemDefinition> right)
    {
        if (left == null || right == null || left.Count != right.Count)
        {
            return false;
        }

        HashSet<ItemDefinition> definitions = new HashSet<ItemDefinition>();
        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] == null || !definitions.Add(left[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < right.Count; i++)
        {
            if (right[i] == null || !definitions.Remove(right[i]))
            {
                return false;
            }
        }

        return definitions.Count == 0;
    }

    private void CacheItemListRowState(string searchText, int folderRevision)
    {
        cachedItemListRowsSearchText = searchText;
        cachedItemListRowsDefinitionsVersion = definitionsCacheVersion;
        cachedItemListRowsFolderRevision = folderRevision;
    }

    private static int CompareItemLayoutElements(ItemLayoutElement left, ItemLayoutElement right)
    {
        int positionCompare = left.DefinitionIndex.CompareTo(right.DefinitionIndex);
        if (positionCompare != 0)
        {
            return positionCompare;
        }

        if (left.Folder != null && right.Folder != null)
        {
            return left.FolderOrder.CompareTo(right.FolderOrder);
        }

        return left.Folder != null ? -1 : right.Folder != null ? 1 : 0;
    }

    private static int CountVisibleFolderMembers(
        List<ItemDefinition> members,
        HashSet<ItemDefinition> visibleDefinitions)
    {
        if (members == null || visibleDefinitions == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null && visibleDefinitions.Contains(members[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static void AppendVisibleFolderMembers(
        List<ItemDefinition> members,
        HashSet<ItemDefinition> visibleDefinitions,
        List<ItemListRow> itemListRows)
    {
        if (members == null || visibleDefinitions == null || itemListRows == null)
        {
            return;
        }

        for (int i = 0; i < members.Count; i++)
        {
            ItemDefinition definition = members[i];
            if (definition != null && visibleDefinitions.Contains(definition))
            {
                itemListRows.Add(ItemListRow.CreateItem(definition));
            }
        }
    }

    private void DrawItemFolderRow(
        ItemListRow row,
        ItemDataFolderSettings folderSettings,
        ItemManager itemManager,
        List<ItemDefinition> definitions)
    {
        Rect rowRect = GUILayoutUtility.GetRect(1f, ItemListRowHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rowRect, new Color(0.18f, 0.18f, 0.18f, 1f));

        bool expanded = row.Folder != null && row.Folder.Expanded;
        Rect foldoutRect = new Rect(rowRect.x + 2f, rowRect.y, 16f, rowRect.height);
        bool nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
        if (nextExpanded != expanded)
        {
            if (row.Folder != null)
            {
                folderSettings.SetFolderExpanded(row.Folder.Id, nextExpanded);
            }

            InvalidateItemFolderPresentationCache();
        }

        Rect dragHandleRect = new Rect(foldoutRect.xMax, rowRect.y, ItemFolderDragHandleWidth, rowRect.height);
        HandleItemFolderDrag(dragHandleRect, row.Folder);
        EditorGUIUtility.AddCursorRect(dragHandleRect, MouseCursor.Pan);
        GUI.Label(dragHandleRect, new GUIContent("≡", "폴더와 내부 아이템을 함께 이동합니다."), EditorStyles.miniLabel);

        float nameStartX = dragHandleRect.xMax + 2f;
        if (row.FolderIconDefinition != null)
        {
            Rect iconRect = new Rect(nameStartX, rowRect.y + 4f, 20f, 20f);
            DrawItemIcon(iconRect, row.FolderIconDefinition);
            nameStartX = iconRect.xMax + 4f;
        }

        Rect countRect = new Rect(
            rowRect.xMax - ItemFolderDeleteButtonWidth - 34f,
            rowRect.y,
            30f,
            rowRect.height);
        Rect nameRect = new Rect(
            nameStartX,
            rowRect.y + 3f,
            Mathf.Max(20f, countRect.xMin - nameStartX - 4f),
            rowRect.height - 6f);

        if (row.Folder != null)
        {
            DrawEditableFolderName(nameRect, row.Folder, folderSettings);

            Rect deleteRect = new Rect(
                rowRect.xMax - ItemFolderDeleteButtonWidth,
                rowRect.y + 3f,
                ItemFolderDeleteButtonWidth,
                rowRect.height - 6f);
            if (GUI.Button(deleteRect, new GUIContent("×", "폴더만 제거하고 아이템은 Unfiled로 이동합니다.")))
            {
                string folderName = row.Folder.DisplayName;
                if (EditorUtility.DisplayDialog(
                        "Remove Item Folder",
                        $"'{folderName}' 폴더를 제거하시겠습니까?\n아이템 데이터와 순서는 변경되지 않습니다.",
                        "Remove",
                        "Cancel")
                    && folderSettings.RemoveFolder(row.Folder.Id))
                {
                    if (string.Equals(editingFolderId, row.Folder.Id, StringComparison.Ordinal))
                    {
                        CancelFolderNameEditing();
                    }

                    InvalidateItemFolderPresentationCache();
                    GUI.FocusControl(null);
                }
            }
        }

        GUI.Label(countRect, row.ItemCount.ToString(), EditorStyles.miniLabel);
        HandleItemLayoutDropTarget(
            rowRect,
            row,
            false,
            itemManager,
            definitions,
            folderSettings);
    }

    private void DrawEditableFolderName(
        Rect nameRect,
        ItemDataFolderSettings.FolderEntry folder,
        ItemDataFolderSettings folderSettings)
    {
        if (folder == null || folderSettings == null)
        {
            return;
        }

        bool isEditing = string.Equals(editingFolderId, folder.Id, StringComparison.Ordinal);
        Event current = Event.current;
        if (!isEditing)
        {
            GUI.Label(
                nameRect,
                new GUIContent(folder.DisplayName, "더블클릭하여 폴더명을 변경합니다."),
                EditorStyles.boldLabel);
            if (current != null
                && current.type == EventType.MouseDown
                && current.button == 0
                && current.clickCount >= 2
                && nameRect.Contains(current.mousePosition))
            {
                BeginFolderNameEditing(folder);
                current.Use();
                Repaint();
            }

            return;
        }

        string controlName = $"ProjectF.ItemFolderName.{folder.Id}";
        GUI.SetNextControlName(controlName);
        editingFolderName = EditorGUI.TextField(
            nameRect,
            editingFolderName ?? folder.DisplayName,
            EditorStyles.textField);

        if (folderNameFocusPending)
        {
            EditorGUI.FocusTextInControl(controlName);
            folderNameFocusPending = false;
        }

        bool hasFolderNameFocus = string.Equals(
            GUI.GetNameOfFocusedControl(),
            controlName,
            StringComparison.Ordinal);
        if (current != null
            && current.type == EventType.KeyDown
            && hasFolderNameFocus)
        {
            if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
            {
                CommitFolderNameEditing(folderSettings);
                current.Use();
                return;
            }

            if (current.keyCode == KeyCode.Escape)
            {
                CancelFolderNameEditing();
                GUI.FocusControl(null);
                current.Use();
                Repaint();
                return;
            }
        }

        if (current != null
            && current.type == EventType.MouseDown
            && current.button == 0
            && !nameRect.Contains(current.mousePosition))
        {
            CommitFolderNameEditing(folderSettings);
            return;
        }

        if (current != null
            && current.type == EventType.Repaint
            && !hasFolderNameFocus)
        {
            CommitFolderNameEditing(folderSettings);
        }
    }

    private void BeginFolderNameEditing(ItemDataFolderSettings.FolderEntry folder)
    {
        editingFolderId = folder != null ? folder.Id : null;
        editingFolderName = folder != null ? folder.DisplayName : null;
        folderNameFocusPending = folder != null;
    }

    private void CommitFolderNameEditing(ItemDataFolderSettings folderSettings)
    {
        string folderId = editingFolderId;
        string folderName = editingFolderName;
        CancelFolderNameEditing();
        GUI.FocusControl(null);
        if (!string.IsNullOrEmpty(folderId)
            && folderSettings != null
            && folderSettings.RenameFolder(folderId, folderName))
        {
            InvalidateItemFolderPresentationCache();
        }

        Repaint();
    }

    private void CancelFolderNameEditing()
    {
        editingFolderId = null;
        editingFolderName = null;
        folderNameFocusPending = false;
    }

    private void DrawItemDefinitionRow(
        ItemListRow row,
        ItemManager itemManager,
        List<ItemDefinition> definitions,
        List<ItemDefinition> visibleDefinitions,
        ItemDataFolderSettings folderSettings)
    {
        ItemDefinition definition = row.Definition;
        if (definition == null)
        {
            return;
        }

        string displayName = GetDefinitionDisplayName(definition);
        Rect rowRect = GUILayoutUtility.GetRect(1f, ItemListRowHeight, GUILayout.ExpandWidth(true));
        bool hasFolders = folderSettings != null && folderSettings.Folders.Count > 0;
        bool isFolderMember = hasFolders
            && cachedItemFolderIds.TryGetValue(definition, out string itemFolderId)
            && !string.IsNullOrEmpty(itemFolderId);
        float indent = isFolderMember ? ItemFolderIndent : 0f;
        Rect selectRect = new Rect(
            rowRect.x + indent,
            rowRect.y,
            Mathf.Max(1f, rowRect.width - indent - GiveButtonWidth - 4f),
            rowRect.height);
        Rect giveRect = new Rect(selectRect.xMax + 4f, rowRect.y, GiveButtonWidth, rowRect.height);
        GUIContent content = new GUIContent($"[{definition.id}] {displayName}");
        HandleItemSelectionInput(selectRect, definition, definitions);
        bool isSelected = selectedItemDefinitions.Contains(definition);
        IReadOnlyList<ItemDefinition> dragSelection = isSelected && selectedItemDefinitionsInOrder.Count > 1
            ? selectedItemDefinitionsInOrder
            : null;
        string dragDisplayName = dragSelection != null
            ? $"{selectedItemDefinitionsInOrder.Count} items"
            : content.text;
        ItemDefinitionDragAndDropUtility.HandleListItemDrag(
            selectRect,
            definition,
            dragSelection,
            dragDisplayName,
            this);
        if (hasFolders)
        {
            HandleItemLayoutDropTarget(
                rowRect,
                row,
                false,
                itemManager,
                definitions,
                folderSettings);
        }
        else
        {
            int visibleDefinitionIndex = visibleDefinitions.IndexOf(definition);
            HandleDefinitionReorderDropTarget(
                rowRect,
                itemManager,
                definitions,
                visibleDefinitions,
                visibleDefinitionIndex);
        }

        GUI.Toggle(selectRect, isSelected, GUIContent.none, "Button");

        Rect iconRect = new Rect(selectRect.x + 4f, selectRect.y + 4f, 20f, 20f);
        Rect labelRect = new Rect(
            iconRect.xMax + 4f,
            selectRect.y,
            Mathf.Max(1f, selectRect.xMax - iconRect.xMax - 8f),
            selectRect.height);
        DrawItemIcon(iconRect, definition);
        GUI.Label(labelRect, content, EditorStyles.miniLabel);

        EditorGUI.BeginDisabledGroup(!EditorApplication.isPlaying);
        if (GUI.Button(giveRect, "Give"))
        {
            TryGiveItemToPlayer(definition);
        }
        EditorGUI.EndDisabledGroup();

        HandleItemFolderContextMenu(
            rowRect,
            definition,
            folderSettings,
            itemManager,
            definitions);
    }

    private void HandleItemSelectionInput(
        Rect selectRect,
        ItemDefinition definition,
        List<ItemDefinition> definitions)
    {
        Event current = Event.current;
        if (current == null || definition == null || current.button != 0)
        {
            return;
        }

        switch (current.type)
        {
            case EventType.MouseDown:
                if (!selectRect.Contains(current.mousePosition))
                {
                    return;
                }

                ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
                bool additive = current.control || current.command;
                if (current.shift)
                {
                    ClearPendingPlainSelection();
                    SelectItemRange(definition, additive, definitions);
                    return;
                }

                if (additive)
                {
                    ClearPendingPlainSelection();
                    ToggleItemSelection(definition, definitions);
                    return;
                }

                pendingPlainSelectionDefinition = definition;
                pendingPlainSelectionMouseDownPosition = current.mousePosition;
                break;

            case EventType.MouseDrag:
                if (pendingPlainSelectionDefinition != null
                    && ItemDefinitionDragAndDropUtility.HasExceededDragStartDistance(
                        pendingPlainSelectionMouseDownPosition,
                        current.mousePosition))
                {
                    ClearPendingPlainSelection();
                }

                break;

            case EventType.MouseUp:
                if (pendingPlainSelectionDefinition != definition)
                {
                    break;
                }

                if (selectRect.Contains(current.mousePosition))
                {
                    SetSingleItemSelection(definition, definitions);
                }

                ClearPendingPlainSelection();
                break;

            case EventType.DragExited:
            case EventType.Ignore:
                ClearPendingPlainSelection();
                break;
        }
    }

    private void SetSingleItemSelection(
        ItemDefinition definition,
        List<ItemDefinition> definitions)
    {
        ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
        selectedItemDefinitions.Clear();
        if (definition != null)
        {
            selectedItemDefinitions.Add(definition);
            selectedItemId = definition.id;
        }

        rangeSelectionAnchor = definition;
        RebuildSelectedItemOrder(definitions);
        Repaint();
    }

    private void ToggleItemSelection(
        ItemDefinition definition,
        List<ItemDefinition> definitions)
    {
        ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
        if (selectedItemDefinitions.Contains(definition))
        {
            if (selectedItemDefinitions.Count <= 1)
            {
                return;
            }

            selectedItemDefinitions.Remove(definition);
            if (definition.id == selectedItemId)
            {
                ItemDefinition nextActiveDefinition = FindFirstSelectedDefinition(definitions);
                selectedItemId = nextActiveDefinition != null ? nextActiveDefinition.id : -1;
            }
        }
        else
        {
            selectedItemDefinitions.Add(definition);
            selectedItemId = definition.id;
        }

        rangeSelectionAnchor = definition;
        RebuildSelectedItemOrder(definitions);
        Repaint();
    }

    private void SelectItemRange(
        ItemDefinition definition,
        bool additive,
        List<ItemDefinition> definitions)
    {
        ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
        ItemDefinition anchor = rangeSelectionAnchor;
        if (anchor == null)
        {
            anchor = FindDefinitionById(definitions, selectedItemId) ?? definition;
        }

        int anchorRowIndex = FindItemListRowIndex(anchor);
        int targetRowIndex = FindItemListRowIndex(definition);
        if (!additive)
        {
            selectedItemDefinitions.Clear();
        }

        if (anchorRowIndex < 0 || targetRowIndex < 0)
        {
            selectedItemDefinitions.Add(definition);
            rangeSelectionAnchor = definition;
        }
        else
        {
            int firstRowIndex = Mathf.Min(anchorRowIndex, targetRowIndex);
            int lastRowIndex = Mathf.Max(anchorRowIndex, targetRowIndex);
            for (int i = firstRowIndex; i <= lastRowIndex; i++)
            {
                ItemDefinition rangeDefinition = cachedItemListRows[i].Definition;
                if (rangeDefinition != null)
                {
                    selectedItemDefinitions.Add(rangeDefinition);
                }
            }

            rangeSelectionAnchor = anchor;
        }

        selectedItemId = definition.id;
        RebuildSelectedItemOrder(definitions);
        Repaint();
    }

    private int FindItemListRowIndex(ItemDefinition definition)
    {
        if (definition == null)
        {
            return -1;
        }

        for (int i = 0; i < cachedItemListRows.Count; i++)
        {
            if (cachedItemListRows[i].Definition == definition)
            {
                return i;
            }
        }

        return -1;
    }

    private ItemDefinition FindFirstSelectedDefinition(List<ItemDefinition> definitions)
    {
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && selectedItemDefinitions.Contains(definition))
            {
                return definition;
            }
        }

        return null;
    }

    private void RebuildSelectedItemOrder(List<ItemDefinition> definitions)
    {
        selectedItemDefinitionsInOrder.Clear();
        if (definitions == null)
        {
            return;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && selectedItemDefinitions.Contains(definition))
            {
                selectedItemDefinitionsInOrder.Add(definition);
            }
        }
    }

    private void ClearPendingPlainSelection()
    {
        pendingPlainSelectionDefinition = null;
        pendingPlainSelectionMouseDownPosition = Vector2.zero;
    }

    private void HandleItemFolderDrag(
        Rect rect,
        ItemDataFolderSettings.FolderEntry folder)
    {
        if (folder == null)
        {
            return;
        }

        Event current = Event.current;
        if (current == null || current.button != 0)
        {
            return;
        }

        switch (current.type)
        {
            case EventType.MouseDown:
                if (rect.Contains(current.mousePosition))
                {
                    pendingFolderDragId = folder.Id;
                    pendingFolderDragMouseDownPosition = current.mousePosition;
                }
                break;

            case EventType.MouseDrag:
                if (!string.Equals(pendingFolderDragId, folder.Id, StringComparison.Ordinal)
                    || (current.mousePosition - pendingFolderDragMouseDownPosition).sqrMagnitude
                    < ItemFolderDragStartDistance * ItemFolderDragStartDistance)
                {
                    return;
                }

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                DragAndDrop.SetGenericData(ItemFolderDragDataKey, folder.Id);
                DragAndDrop.StartDrag(folder.DisplayName);
                ClearPendingFolderDrag();
                Repaint();
                current.Use();
                break;

            case EventType.MouseUp:
            case EventType.DragExited:
            case EventType.Ignore:
                ClearPendingFolderDrag();
                break;
        }
    }

    private void ClearPendingFolderDrag()
    {
        pendingFolderDragId = null;
        pendingFolderDragMouseDownPosition = Vector2.zero;
    }

    private static bool TryGetDraggedFolderId(out string folderId)
    {
        folderId = DragAndDrop.GetGenericData(ItemFolderDragDataKey) as string;
        return !string.IsNullOrEmpty(folderId);
    }

    private void HandleItemLayoutDropTarget(
        Rect rect,
        ItemListRow targetRow,
        bool isEndTarget,
        ItemManager itemManager,
        List<ItemDefinition> definitions,
        ItemDataFolderSettings folderSettings)
    {
        bool hasDraggedFolder = TryGetDraggedFolderId(out string draggedFolderId);
        bool hasDraggedItem = !hasDraggedFolder
            && ItemDefinitionDragAndDropUtility.TryGetDraggedDefinitions(draggedItemDefinitions);
        if ((!hasDraggedFolder && !hasDraggedItem)
            || folderSettings == null
            || itemManager == null
            || definitions == null)
        {
            return;
        }

        if (hasDraggedItem
            && !isEndTarget
            && targetRow.Definition != null
            && draggedItemDefinitions.Contains(targetRow.Definition))
        {
            return;
        }

        if (hasDraggedFolder && !CanDropFolderOnRow(draggedFolderId, targetRow, isEndTarget, folderSettings))
        {
            return;
        }

        Event current = Event.current;
        if (current == null || !rect.Contains(current.mousePosition))
        {
            return;
        }

        bool insertAfter = !isEndTarget && current.mousePosition.y > rect.center.y;
        bool itemIntoFolder = hasDraggedItem && !isEndTarget && targetRow.IsFolder;
        switch (current.type)
        {
            case EventType.DragUpdated:
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                Repaint();
                current.Use();
                break;

            case EventType.DragPerform:
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                DragAndDrop.AcceptDrag();
                if (hasDraggedFolder)
                {
                    MoveFolderForLayoutDrop(
                        itemManager,
                        definitions,
                        folderSettings,
                        draggedFolderId,
                        targetRow,
                        isEndTarget,
                        insertAfter);
                }
                else
                {
                    MoveItemsForLayoutDrop(
                        itemManager,
                        definitions,
                        folderSettings,
                        draggedItemDefinitions,
                        targetRow,
                        isEndTarget,
                        insertAfter);
                }

                GUI.changed = true;
                current.Use();
                break;

            case EventType.Repaint:
                if (itemIntoFolder)
                {
                    Color fillColor = new Color(0.35f, 0.65f, 1f, 0.16f);
                    Color outlineColor = new Color(0.35f, 0.65f, 1f, 0.95f);
                    EditorGUI.DrawRect(rect, fillColor);
                    DrawRectOutline(rect, outlineColor);
                }
                else
                {
                    DrawDefinitionReorderHighlight(rect, insertAfter, isEndTarget);
                }

                break;
        }
    }

    private static bool CanDropFolderOnRow(
        string draggedFolderId,
        ItemListRow targetRow,
        bool isEndTarget,
        ItemDataFolderSettings folderSettings)
    {
        if (isEndTarget)
        {
            return true;
        }

        if (targetRow.Folder != null)
        {
            return !string.Equals(draggedFolderId, targetRow.Folder.Id, StringComparison.Ordinal);
        }

        return targetRow.Definition == null
            || !string.Equals(
                draggedFolderId,
                folderSettings.GetItemFolderId(targetRow.Definition),
                StringComparison.Ordinal);
    }

    private void HandleItemFolderContextMenu(
        Rect rect,
        ItemDefinition definition,
        ItemDataFolderSettings folderSettings,
        ItemManager itemManager,
        List<ItemDefinition> definitions)
    {
        Event current = Event.current;
        if (current == null
            || current.type != EventType.ContextClick
            || !rect.Contains(current.mousePosition)
            || definition == null
            || folderSettings == null
            || folderSettings.Folders.Count == 0)
        {
            return;
        }

        string currentFolderId = GetCommonActionFolderId(
            definition,
            folderSettings,
            out bool allActionItemsShareFolder);
        GenericMenu menu = new GenericMenu();
        menu.AddItem(
            new GUIContent("Move to Folder/Unfiled"),
            allActionItemsShareFolder && string.IsNullOrEmpty(currentFolderId),
            () => MoveItemToFolder(
                definition,
                string.Empty,
                folderSettings,
                itemManager,
                definitions));

        IReadOnlyList<ItemDataFolderSettings.FolderEntry> folders = folderSettings.Folders;
        for (int i = 0; i < folders.Count; i++)
        {
            ItemDataFolderSettings.FolderEntry folder = folders[i];
            if (folder == null)
            {
                continue;
            }

            string targetFolderId = folder.Id;
            string menuName = folder.DisplayName.Replace("/", "∕");
            menu.AddItem(
                new GUIContent($"Move to Folder/{menuName}"),
                allActionItemsShareFolder
                && string.Equals(currentFolderId, targetFolderId, StringComparison.Ordinal),
                () => MoveItemToFolder(
                    definition,
                    targetFolderId,
                    folderSettings,
                    itemManager,
                    definitions));
        }

        menu.ShowAsContext();
        current.Use();
    }

    private void MoveItemToFolder(
        ItemDefinition definition,
        string folderId,
        ItemDataFolderSettings folderSettings,
        ItemManager itemManager,
        List<ItemDefinition> definitions)
    {
        List<ItemDefinition> actionDefinitions = BuildFolderActionDefinitions(definition);
        string currentFolderId = actionDefinitions.Count > 0
            ? folderSettings.GetItemFolderId(actionDefinitions[0])
            : string.Empty;
        bool allActionItemsShareFolder = true;
        for (int i = 1; i < actionDefinitions.Count; i++)
        {
            if (!string.Equals(
                    currentFolderId,
                    folderSettings.GetItemFolderId(actionDefinitions[i]),
                    StringComparison.Ordinal))
            {
                allActionItemsShareFolder = false;
                break;
            }
        }

        int insertBoundary;
        if (string.IsNullOrEmpty(folderId))
        {
            insertBoundary = allActionItemsShareFolder && !string.IsNullOrEmpty(currentFolderId)
                ? GetFolderBlockEndBoundary(definitions, currentFolderId, folderSettings)
                : actionDefinitions.Count == 1
                    ? Mathf.Max(0, definitions.IndexOf(definition))
                : definitions.Count;
        }
        else
        {
            insertBoundary = GetFolderBlockEndBoundary(definitions, folderId, folderSettings);
        }

        MoveItemsToFolderAtBoundary(
            itemManager,
            definitions,
            folderSettings,
            actionDefinitions,
            folderId,
            insertBoundary);
    }

    private string GetCommonActionFolderId(
        ItemDefinition clickedDefinition,
        ItemDataFolderSettings folderSettings,
        out bool allItemsShareFolder)
    {
        List<ItemDefinition> actionDefinitions = BuildFolderActionDefinitions(clickedDefinition);
        string commonFolderId = actionDefinitions.Count > 0
            ? folderSettings.GetItemFolderId(actionDefinitions[0])
            : string.Empty;
        allItemsShareFolder = actionDefinitions.Count > 0;
        for (int i = 1; i < actionDefinitions.Count; i++)
        {
            if (!string.Equals(
                    commonFolderId,
                    folderSettings.GetItemFolderId(actionDefinitions[i]),
                    StringComparison.Ordinal))
            {
                allItemsShareFolder = false;
                break;
            }
        }

        return commonFolderId;
    }

    private List<ItemDefinition> BuildFolderActionDefinitions(ItemDefinition clickedDefinition)
    {
        if (clickedDefinition != null
            && selectedItemDefinitions.Contains(clickedDefinition)
            && selectedItemDefinitionsInOrder.Count > 1)
        {
            return new List<ItemDefinition>(selectedItemDefinitionsInOrder);
        }

        return clickedDefinition != null
            ? new List<ItemDefinition>(1) { clickedDefinition }
            : new List<ItemDefinition>();
    }

    private void MoveItemsForLayoutDrop(
        ItemManager itemManager,
        List<ItemDefinition> definitions,
        ItemDataFolderSettings folderSettings,
        IReadOnlyList<ItemDefinition> draggedDefinitions,
        ItemListRow targetRow,
        bool isEndTarget,
        bool insertAfter)
    {
        if (draggedDefinitions == null || draggedDefinitions.Count == 0)
        {
            return;
        }

        string targetFolderId;
        int insertBoundary;
        if (isEndTarget)
        {
            targetFolderId = string.Empty;
            insertBoundary = definitions.Count;
        }
        else if (targetRow.Folder != null)
        {
            targetFolderId = targetRow.Folder.Id;
            insertBoundary = GetFolderBlockEndBoundary(definitions, targetFolderId, folderSettings);
        }
        else
        {
            ItemDefinition targetDefinition = targetRow.Definition;
            if (targetDefinition == null)
            {
                return;
            }

            targetFolderId = folderSettings.GetItemFolderId(targetDefinition);
            int targetIndex = definitions.IndexOf(targetDefinition);
            if (targetIndex < 0)
            {
                return;
            }

            insertBoundary = targetIndex + (insertAfter ? 1 : 0);
        }

        MoveItemsToFolderAtBoundary(
            itemManager,
            definitions,
            folderSettings,
            draggedDefinitions,
            targetFolderId,
            insertBoundary);
    }

    private void MoveItemsToFolderAtBoundary(
        ItemManager itemManager,
        List<ItemDefinition> definitions,
        ItemDataFolderSettings folderSettings,
        IReadOnlyList<ItemDefinition> requestedDefinitions,
        string targetFolderId,
        int insertBoundary)
    {
        if (itemManager == null
            || definitions == null
            || folderSettings == null
            || requestedDefinitions == null)
        {
            return;
        }

        List<ItemDefinition> movingDefinitions = CollectOrderedDefinitions(
            definitions,
            requestedDefinitions);
        if (movingDefinitions.Count == 0)
        {
            return;
        }

        Dictionary<string, int> movingCountBySourceFolder =
            new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < movingDefinitions.Count; i++)
        {
            string sourceFolderId = folderSettings.GetItemFolderId(movingDefinitions[i]);
            if (string.IsNullOrEmpty(sourceFolderId)
                || string.Equals(sourceFolderId, targetFolderId, StringComparison.Ordinal))
            {
                continue;
            }

            movingCountBySourceFolder.TryGetValue(sourceFolderId, out int count);
            movingCountBySourceFolder[sourceFolderId] = count + 1;
        }

        Dictionary<string, ItemDefinition> emptiedSourceFolderAnchors =
            new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> sourceFolder in movingCountBySourceFolder)
        {
            if (CountFolderMembers(definitions, sourceFolder.Key, folderSettings) != sourceFolder.Value)
            {
                continue;
            }

            int sourceEndBoundary = GetFolderBlockEndBoundary(
                definitions,
                sourceFolder.Key,
                folderSettings);
            ItemDefinition emptyAnchor = string.IsNullOrEmpty(targetFolderId)
                                         && movingCountBySourceFolder.Count == 1
                                         && insertBoundary == sourceEndBoundary
                ? FindFirstDefinitionInFolder(
                    movingDefinitions,
                    sourceFolder.Key,
                    folderSettings)
                : FindDefinitionAfterFolderBlock(definitions, sourceFolder.Key, folderSettings);
            emptiedSourceFolderAnchors.Add(sourceFolder.Key, emptyAnchor);
        }

        bool membershipChanged = folderSettings.SetItemsFolder(movingDefinitions, targetFolderId);
        ItemDefinition selection = FindDefinitionById(definitions, selectedItemId);
        RegisterDefinitionOrderUndo(itemManager, definitions, "Move Item In Folder Layout");
        bool orderChanged = MoveDefinitionBlock(
            definitions,
            movingDefinitions,
            insertBoundary,
            out _);

        bool placementChanged = false;
        foreach (KeyValuePair<string, ItemDefinition> sourceFolder in emptiedSourceFolderAnchors)
        {
            placementChanged |= folderSettings.SetFolderPlacement(
                sourceFolder.Key,
                sourceFolder.Value);
        }

        if (!string.IsNullOrEmpty(targetFolderId))
        {
            ItemDefinition targetAnchor = FindDefinitionAfterFolderBlock(
                definitions,
                targetFolderId,
                folderSettings);
            placementChanged |= folderSettings.SetFolderPlacement(targetFolderId, targetAnchor);
        }

        if (orderChanged)
        {
            CommitDefinitionOrderChange(
                itemManager,
                definitions,
                selection != null ? selection : movingDefinitions[0]);
        }
        else if (membershipChanged || placementChanged)
        {
            InvalidateItemFolderPresentationCache();
            Repaint();
        }
    }

    private static List<ItemDefinition> CollectOrderedDefinitions(
        List<ItemDefinition> definitions,
        IReadOnlyList<ItemDefinition> requestedDefinitions)
    {
        List<ItemDefinition> orderedDefinitions = new List<ItemDefinition>();
        if (definitions == null || requestedDefinitions == null)
        {
            return orderedDefinitions;
        }

        HashSet<ItemDefinition> requestedDefinitionSet =
            new HashSet<ItemDefinition>(requestedDefinitions);
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && requestedDefinitionSet.Contains(definition))
            {
                orderedDefinitions.Add(definition);
            }
        }

        return orderedDefinitions;
    }

    private static ItemDefinition FindFirstDefinitionInFolder(
        List<ItemDefinition> definitions,
        string folderId,
        ItemDataFolderSettings folderSettings)
    {
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null
                && string.Equals(
                    folderSettings.GetItemFolderId(definition),
                    folderId,
                    StringComparison.Ordinal))
            {
                return definition;
            }
        }

        return null;
    }

    private void MoveFolderForLayoutDrop(
        ItemManager itemManager,
        List<ItemDefinition> definitions,
        ItemDataFolderSettings folderSettings,
        string draggedFolderId,
        ItemListRow targetRow,
        bool isEndTarget,
        bool insertAfter)
    {
        ItemDataFolderSettings.FolderEntry draggedFolder = folderSettings.FindFolder(draggedFolderId);
        if (draggedFolder == null)
        {
            return;
        }

        int insertBoundary;
        string relativeFolderId = null;
        bool insertAfterRelativeFolder = false;
        bool relativeFolderWasEmpty = false;
        if (isEndTarget)
        {
            insertBoundary = definitions.Count;
        }
        else if (targetRow.Folder != null)
        {
            relativeFolderId = targetRow.Folder.Id;
            insertAfterRelativeFolder = insertAfter;
            relativeFolderWasEmpty = CountFolderMembers(
                definitions,
                relativeFolderId,
                folderSettings) == 0;
            insertBoundary = insertAfter
                ? GetFolderBlockEndBoundary(definitions, relativeFolderId, folderSettings)
                : GetFolderBlockStartBoundary(definitions, relativeFolderId, folderSettings);
        }
        else
        {
            ItemDefinition targetDefinition = targetRow.Definition;
            if (targetDefinition == null)
            {
                return;
            }

            string targetFolderId = folderSettings.GetItemFolderId(targetDefinition);
            if (!string.IsNullOrEmpty(targetFolderId))
            {
                relativeFolderId = targetFolderId;
                insertAfterRelativeFolder = insertAfter;
                relativeFolderWasEmpty = CountFolderMembers(
                    definitions,
                    relativeFolderId,
                    folderSettings) == 0;
                insertBoundary = insertAfter
                    ? GetFolderBlockEndBoundary(definitions, targetFolderId, folderSettings)
                    : GetFolderBlockStartBoundary(definitions, targetFolderId, folderSettings);
            }
            else
            {
                int targetIndex = definitions.IndexOf(targetDefinition);
                if (targetIndex < 0)
                {
                    return;
                }

                insertBoundary = targetIndex + (insertAfter ? 1 : 0);
            }
        }

        List<ItemDefinition> movingDefinitions = CollectFolderMembers(
            definitions,
            draggedFolderId,
            folderSettings);
        ItemDefinition selection = FindDefinitionById(definitions, selectedItemId);
        RegisterDefinitionOrderUndo(itemManager, definitions, "Move Item Folder");
        bool orderChanged = MoveDefinitionBlock(
            definitions,
            movingDefinitions,
            insertBoundary,
            out int insertedIndex);
        int anchorIndex = Mathf.Clamp(insertedIndex + movingDefinitions.Count, 0, definitions.Count);
        ItemDefinition anchorDefinition = anchorIndex < definitions.Count
            ? definitions[anchorIndex]
            : null;
        bool placementChanged = folderSettings.SetFolderPlacement(
            draggedFolderId,
            anchorDefinition,
            relativeFolderId,
            insertAfterRelativeFolder);
        if (insertAfterRelativeFolder
            && relativeFolderWasEmpty
            && movingDefinitions.Count > 0
            && insertedIndex >= 0
            && insertedIndex < definitions.Count)
        {
            placementChanged |= folderSettings.SetFolderPlacement(
                relativeFolderId,
                definitions[insertedIndex],
                draggedFolderId,
                false);
        }

        if (orderChanged)
        {
            CommitDefinitionOrderChange(
                itemManager,
                definitions,
                selection);
        }
        else if (placementChanged)
        {
            InvalidateItemFolderPresentationCache();
            Repaint();
        }
    }

    private static List<ItemDefinition> CollectFolderMembers(
        List<ItemDefinition> definitions,
        string folderId,
        ItemDataFolderSettings folderSettings)
    {
        List<ItemDefinition> members = new List<ItemDefinition>();
        if (definitions == null || string.IsNullOrEmpty(folderId) || folderSettings == null)
        {
            return members;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null
                && string.Equals(
                    folderSettings.GetItemFolderId(definition),
                    folderId,
                    StringComparison.Ordinal))
            {
                members.Add(definition);
            }
        }

        return members;
    }

    private static int CountFolderMembers(
        List<ItemDefinition> definitions,
        string folderId,
        ItemDataFolderSettings folderSettings)
    {
        int count = 0;
        if (definitions == null || string.IsNullOrEmpty(folderId) || folderSettings == null)
        {
            return count;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null
                && string.Equals(
                    folderSettings.GetItemFolderId(definition),
                    folderId,
                    StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static int GetFolderBlockStartBoundary(
        List<ItemDefinition> definitions,
        string folderId,
        ItemDataFolderSettings folderSettings)
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null
                && string.Equals(
                    folderSettings.GetItemFolderId(definition),
                    folderId,
                    StringComparison.Ordinal))
            {
                return i;
            }
        }

        ItemDataFolderSettings.FolderEntry folder = folderSettings.FindFolder(folderId);
        return ResolveFolderPlacementIndex(definitions, folder, folderSettings);
    }

    private static int GetFolderBlockEndBoundary(
        List<ItemDefinition> definitions,
        string folderId,
        ItemDataFolderSettings folderSettings)
    {
        for (int i = definitions.Count - 1; i >= 0; i--)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null
                && string.Equals(
                    folderSettings.GetItemFolderId(definition),
                    folderId,
                    StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        ItemDataFolderSettings.FolderEntry folder = folderSettings.FindFolder(folderId);
        return ResolveFolderPlacementIndex(definitions, folder, folderSettings);
    }

    private static ItemDefinition FindDefinitionAfterFolderBlock(
        List<ItemDefinition> definitions,
        string folderId,
        ItemDataFolderSettings folderSettings)
    {
        int endBoundary = GetFolderBlockEndBoundary(definitions, folderId, folderSettings);
        return endBoundary >= 0 && endBoundary < definitions.Count
            ? definitions[endBoundary]
            : null;
    }

    private static int ResolveFolderPlacementIndex(
        List<ItemDefinition> definitions,
        ItemDataFolderSettings.FolderEntry folder,
        ItemDataFolderSettings folderSettings)
    {
        if (definitions == null || folder == null || folderSettings == null)
        {
            return definitions != null ? definitions.Count : 0;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            if (string.Equals(
                    folderSettings.GetItemFolderId(definition),
                    folder.Id,
                    StringComparison.Ordinal))
            {
                return i;
            }

            string assetPath = AssetDatabase.GetAssetPath(definition);
            string definitionGuid = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);
            if (!string.IsNullOrEmpty(definitionGuid)
                && string.Equals(definitionGuid, folder.AnchorItemGuid, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return definitions.Count;
    }

    private static bool MoveDefinitionBlock(
        List<ItemDefinition> definitions,
        List<ItemDefinition> movingDefinitions,
        int insertBoundary,
        out int insertedIndex)
    {
        insertedIndex = 0;
        if (definitions == null || movingDefinitions == null)
        {
            return false;
        }

        HashSet<ItemDefinition> movingSet = new HashSet<ItemDefinition>();
        List<ItemDefinition> orderedMovingDefinitions = new List<ItemDefinition>();
        for (int i = 0; i < movingDefinitions.Count; i++)
        {
            ItemDefinition definition = movingDefinitions[i];
            if (definition != null
                && definitions.Contains(definition)
                && movingSet.Add(definition))
            {
                orderedMovingDefinitions.Add(definition);
            }
        }

        insertBoundary = Mathf.Clamp(insertBoundary, 0, definitions.Count);
        int removedBeforeBoundary = 0;
        List<ItemDefinition> remainingDefinitions = new List<ItemDefinition>(definitions.Count);
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (movingSet.Contains(definition))
            {
                if (i < insertBoundary)
                {
                    removedBeforeBoundary++;
                }

                continue;
            }

            remainingDefinitions.Add(definition);
        }

        insertedIndex = Mathf.Clamp(
            insertBoundary - removedBeforeBoundary,
            0,
            remainingDefinitions.Count);
        if (orderedMovingDefinitions.Count == 0)
        {
            return false;
        }

        List<ItemDefinition> reorderedDefinitions = new List<ItemDefinition>(definitions.Count);
        for (int i = 0; i < insertedIndex; i++)
        {
            reorderedDefinitions.Add(remainingDefinitions[i]);
        }

        reorderedDefinitions.AddRange(orderedMovingDefinitions);
        for (int i = insertedIndex; i < remainingDefinitions.Count; i++)
        {
            reorderedDefinitions.Add(remainingDefinitions[i]);
        }

        bool changed = reorderedDefinitions.Count != definitions.Count;
        if (!changed)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != reorderedDefinitions[i])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            return false;
        }

        definitions.Clear();
        definitions.AddRange(reorderedDefinitions);
        return true;
    }

    private static void DrawRectOutline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), color);
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
        if (!ItemDefinitionDragAndDropUtility.TryGetDraggedDefinitions(draggedItemDefinitions)
            || definitions == null
            || visibleDefinitions == null
            || itemManager == null
            || draggedItemDefinitions.Count == 0)
        {
            return;
        }

        if (visibleInsertIndex >= 0
            && visibleInsertIndex < visibleDefinitions.Count
            && draggedItemDefinitions.Contains(visibleDefinitions[visibleInsertIndex]))
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
                Repaint();
                current.Use();
                break;

            case EventType.DragPerform:
                DragAndDrop.AcceptDrag();
                ReorderDefinitions(
                    itemManager,
                    definitions,
                    visibleDefinitions,
                    draggedItemDefinitions,
                    visibleInsertIndex,
                    insertAfter);
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
        IReadOnlyList<ItemDefinition> draggedDefinitions,
        int visibleInsertIndex,
        bool insertAfter)
    {
        if (itemManager == null
            || definitions == null
            || draggedDefinitions == null
            || draggedDefinitions.Count == 0)
        {
            return;
        }

        int insertBoundary;
        if (visibleInsertIndex >= visibleDefinitions.Count)
        {
            if (visibleDefinitions.Count <= 0)
            {
                return;
            }

            ItemDefinition lastVisibleDefinition = visibleDefinitions[visibleDefinitions.Count - 1];
            insertBoundary = definitions.IndexOf(lastVisibleDefinition);
            if (insertBoundary < 0)
            {
                return;
            }

            insertBoundary++;
        }
        else
        {
            ItemDefinition targetDefinition = visibleDefinitions[visibleInsertIndex];
            insertBoundary = definitions.IndexOf(targetDefinition);
            if (insertBoundary < 0)
            {
                return;
            }

            if (insertAfter)
            {
                insertBoundary++;
            }
        }

        List<ItemDefinition> movingDefinitions = CollectOrderedDefinitions(
            definitions,
            draggedDefinitions);
        ItemDefinition selectedDefinition = FindDefinitionById(definitions, selectedItemId);
        RegisterDefinitionOrderUndo(itemManager, definitions, "Reorder Item Definitions");
        if (!MoveDefinitionBlock(definitions, movingDefinitions, insertBoundary, out _))
        {
            return;
        }

        CommitDefinitionOrderChange(
            itemManager,
            definitions,
            selectedDefinition != null ? selectedDefinition : movingDefinitions[0]);
    }

    private static void RegisterDefinitionOrderUndo(
        ItemManager itemManager,
        List<ItemDefinition> definitions,
        string undoName)
    {
        if (itemManager == null || definitions == null)
        {
            return;
        }

        Undo.RegisterCompleteObjectUndo(itemManager, undoName);
        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i] != null)
            {
                Undo.RecordObject(definitions[i], undoName);
            }
        }
    }

    private void CommitDefinitionOrderChange(
        ItemManager itemManager,
        List<ItemDefinition> definitions,
        ItemDefinition selection)
    {
        if (itemManager == null || definitions == null)
        {
            return;
        }

        pendingReorderSelection = selection;

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
        // Crafting tree v5 stores stable item names, so an ID-only reorder must not
        // rewrite the imported binary asset. Reloading remaps those names to the new IDs.
        CraftingTreeRuntime.ForceReload();
        CraftingTreeEditorWindow.ReloadOpenWindows();
        itemManager.MarkEditorDirty();
        AssetDatabase.SaveAssets();
        InvalidateDefinitionCache();
        DefinitionCatalog.NotifyChanged();

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
            itemManager.MarkEditorDirty();
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

        itemManager.MarkEditorDirty();
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

        RemoveDuplicateItemSets(itemSets);
    }

    private static void RemoveDuplicateItemSets(List<ItemManager.ItemSet> itemSets)
    {
        if (itemSets == null || itemSets.Count <= 1)
        {
            return;
        }

        HashSet<int> usedIds = new HashSet<int>();
        HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<ItemManager.ItemSet> uniqueItemSets = new List<ItemManager.ItemSet>(itemSets.Count);
        for (int i = 0; i < itemSets.Count; i++)
        {
            ItemManager.ItemSet itemSet = itemSets[i];
            bool hasId = itemSet.id >= 0;
            string itemName = itemSet.name?.Trim();
            bool hasName = !string.IsNullOrWhiteSpace(itemName);
            if ((hasId && usedIds.Contains(itemSet.id))
                || (hasName && usedNames.Contains(itemName)))
            {
                continue;
            }

            if (hasId)
            {
                usedIds.Add(itemSet.id);
            }

            if (hasName)
            {
                usedNames.Add(itemName);
            }

            uniqueItemSets.Add(itemSet);
        }

        itemSets.Clear();
        itemSets.AddRange(uniqueItemSets);
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
        if (GUILayout.Button("Save", GUILayout.ExpandWidth(true)))
        {
            SaveItemData();
        }

        if (GUILayout.Button("Load", GUILayout.ExpandWidth(true)))
        {
            LoadItemData();
        }

        if (GUILayout.Button("Rebuild", GUILayout.ExpandWidth(true)))
        {
            RebuildItemData();
        }

        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Export JSON", GUILayout.ExpandWidth(true)))
        {
            ExportJson();
        }

        if (GUILayout.Button("Load JSON", GUILayout.ExpandWidth(true)))
        {
            LoadJson();
        }

        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Create UI Icon Atlas", GUILayout.ExpandWidth(true)))
        {
            CreateUiIconAtlas();
        }

        if (GUILayout.Button("Open UI Icon Atlas", GUILayout.ExpandWidth(true)))
        {
            OpenUiIconAtlas();
        }

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
        EnsureMultiSelection(definitions);

        ItemDefinition selectedDefinition = FindDefinitionById(definitions, selectedItemId);
        if (selectedDefinition == null)
        {
            EditorGUILayout.HelpBox("왼쪽 목록에서 아이템을 선택하세요.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        bool hasMultipleSelectedItems = selectedItemDefinitionsInOrder.Count > 1;
        if (hasMultipleSelectedItems)
        {
            DrawMultiSelectedItemHeader(selectedDefinition);
        }
        else
        {
            DrawSelectedItemHeader(selectedDefinition);
        }
        GUILayout.Space(8f);

        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        if (hasMultipleSelectedItems)
        {
            DrawMultiSelectedItemFields(definitions);
        }
        else
        {
            DrawSelectedItemFields(selectedDefinition, definitions);
        }
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

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            GUILayout.BeginHorizontal();
            GUIContent rebuildContent = new GUIContent(
                "Rebuild Item",
                "선택한 아이템만 Assets/Items의 에셋을 기준으로 다시 연결합니다.");
            if (GUILayout.Button(rebuildContent, GUILayout.Width(110f)))
            {
                RebuildItemData(definition);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUIContent createPaperIconContent = new GUIContent(
                "Create Paper Icon",
                "일반 아이템을 선택하면 원본 ItemDefinition을 변경하지 않고 Assets/Items/Paper 아래의 대응 Paper 아이콘과 P 텍스처를 생성합니다.");
            if (GUILayout.Button(createPaperIconContent, GUILayout.Width(135f)))
            {
                CreatePaperIcon(definition);
            }

            GUIContent createBookIconContent = new GUIContent(
                "Create Book Icon",
                "일반 아이템을 선택하면 원본 ItemDefinition을 변경하지 않고 Assets/Items/Book 아래의 대응 Book 아이콘과 P 텍스처를 생성합니다.");
            if (GUILayout.Button(createBookIconContent, GUILayout.Width(130f)))
            {
                CreateBookIcon(definition);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUIContent createCdIconContent = new GUIContent(
                "Create CD Icon",
                "일반 아이템을 선택하면 원본 ItemDefinition을 변경하지 않고 Assets/Items/CD 아래의 대응 CD 아이콘과 P 텍스처를 생성합니다.");
            if (GUILayout.Button(createCdIconContent, GUILayout.Width(120f)))
            {
                CreateCdIcon(definition);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void DrawMultiSelectedItemHeader(ItemDefinition activeDefinition)
    {
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(96f));
        Rect iconRect = GUILayoutUtility.GetRect(80f, 80f, GUILayout.ExpandWidth(false));
        DrawIconBackground(iconRect);
        DrawItemIcon(iconRect, activeDefinition);
        GUILayout.EndVertical();

        GUILayout.BeginVertical();
        EditorGUILayout.LabelField(
            $"{selectedItemDefinitionsInOrder.Count} Items Selected",
            EditorStyles.largeLabel);
        EditorGUILayout.LabelField(
            $"Active: [{activeDefinition.id}] {GetDefinitionDisplayName(activeDefinition)}",
            EditorStyles.miniLabel);
        GUILayout.Space(6f);

        if (GUILayout.Button("Ping Selected", GUILayout.Width(120f)))
        {
            UnityEngine.Object[] selectedAssets =
                new UnityEngine.Object[selectedItemDefinitionsInOrder.Count];
            for (int i = 0; i < selectedItemDefinitionsInOrder.Count; i++)
            {
                selectedAssets[i] = selectedItemDefinitionsInOrder[i];
            }

            Selection.objects = selectedAssets;
            EditorGUIUtility.PingObject(activeDefinition);
        }

        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void DrawMultiSelectedItemFields(List<ItemDefinition> definitions)
    {
        SerializedObject serializedObject = GetMultiSelectedDefinitionSerializedObject();
        if (serializedObject == null)
        {
            return;
        }

        serializedObject.UpdateIfRequiredOrScript();

        SerializedProperty interactionButtonListProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "interactionButtonList");
        SerializedProperty lightModeProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "lightMode");
        SerializedProperty lightRangeProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "lightRange");
        SerializedProperty lightIntensityMultiplierProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "lightIntensityMultiplier");
        SerializedProperty sizeProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "size");
        SerializedProperty itemFilterProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "itemFilter");
        SerializedProperty ignoreFilterProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "ignoreFilter");
        SerializedProperty oneItemProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "oneItem");
        SerializedProperty isManualProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "isManual");
        SerializedProperty manualTargetItemProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "manualTargetItem");
        SerializedProperty upgradeableProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "upgradeable");
        SerializedProperty capacityProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "capacity");
        SerializedProperty storesFluidProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "storesFluid");
        SerializedProperty fluidStorageLitersProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "fluidStorageLiters");
        SerializedProperty fluidOutputLitersPerSecondProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "fluidOutputLitersPerSecond");
        SerializedProperty fluidDisplayColorProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "fluidDisplayColor");
        SerializedProperty bucketFillDurationSecondsProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "bucketFillDurationSeconds");
        SerializedProperty energyTypeProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "energyType");
        SerializedProperty energyAmountProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "energyAmount");
        SerializedProperty eatRewardItemProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "eatRewardItem");
        SerializedProperty eatRewardChancePercentProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "eatRewardChancePercent");
        SerializedProperty isSeedProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "isSeed");
        SerializedProperty seedTargetResourceProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "seedTargetResource");
        SerializedProperty useEnergyTypeProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "useEnergyType");
        SerializedProperty useEnergyAmountProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "useEnergyAmount");
        SerializedProperty completeEnergyProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "completeEnergy");
        SerializedProperty utilityPoleConnectionRadiusProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "utilityPoleConnectionRadius");
        SerializedProperty utilityPoleSupplyRadiusProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "utilityPoleSupplyRadius");
        SerializedProperty sprinklerRangeRadiusProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "sprinklerRangeRadius");
        SerializedProperty sprinklerWaterLitersPerCellProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "sprinklerWaterLitersPerCell");
        SerializedProperty sprinklerSprayIntervalSecondsProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "sprinklerSprayIntervalSeconds");
        SerializedProperty sprinklerNozzleRotationDegreesPerSecondProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "sprinklerNozzleRotationDegreesPerSecond");
        SerializedProperty seedPlanterPlantDurationSecondsProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "seedPlanterPlantDurationSeconds");
        SerializedProperty craftingDurationSecondsProperty =
            GetMultiSelectedDefinitionProperty(serializedObject, "craftingDurationSeconds");

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Common Item Fields", EditorStyles.boldLabel);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
        DrawMultiPropertyField(
            oneItemProperty,
            new GUIContent(
                "One Item",
                "체크하면 모든 보관 컨텐츠에서 스택 하나당 이 아이템을 하나만 보관할 수 있습니다."));

        if (interactionButtonListProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Interaction", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                interactionButtonListProperty,
                new GUIContent("Interaction Button List"),
                true);
        }

        if (lightModeProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(lightModeProperty, new GUIContent("Light Condition"));
            if (lightModeProperty.hasMultipleDifferentValues
                || lightModeProperty.enumValueIndex != (int)ItemDefinition.ItemLightMode.None)
            {
                DrawMultiClampedFloatProperty(
                    lightRangeProperty,
                    new GUIContent("Light Range"),
                    0.1f);
                DrawMultiClampedFloatProperty(
                    lightIntensityMultiplierProperty,
                    new GUIContent("Light Intensity Multiplier"),
                    0.01f);
            }
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
        DrawMultiPropertyField(sizeProperty, new GUIContent("Size"));
        DrawMultiPropertyField(itemFilterProperty, new GUIContent("Item Filter"));
        DrawMultiPropertyField(
            ignoreFilterProperty,
            new GUIContent("Ignore Filter", "체크하면 박스의 아이템 필터 목록에서 제외합니다."));
        if (isManualProperty != null)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                isManualProperty,
                new GUIContent(
                    "Manual Item",
                    "제작법 설명서 등 Manual 용도로 사용하는 아이템이면 체크합니다."));
            bool manualStateChanged = EditorGUI.EndChangeCheck();
            if (manualStateChanged
                && !isManualProperty.hasMultipleDifferentValues
                && !isManualProperty.boolValue
                && manualTargetItemProperty != null)
            {
                manualTargetItemProperty.objectReferenceValue = null;
            }

            if (!isManualProperty.hasMultipleDifferentValues && isManualProperty.boolValue)
            {
                DrawManualTargetItemField(
                    manualTargetItemProperty,
                    definitions);
            }
        }

        if (upgradeableProperty != null && AllSelectedDefinitionsSupportUpgrade())
        {
            EditorGUILayout.PropertyField(
                upgradeableProperty,
                new GUIContent(
                    "Upgrade able",
                    "체크하면 부모 I/O 모듈을 이 아이템으로 업그레이드할 수 있습니다."));
        }

        if (capacityProperty != null && AllSelectedDefinitionsShowCapacity())
        {
            DrawMultiClampedLongProperty(capacityProperty, new GUIContent("Capacity"), 1L);
        }

        if (isSeedProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Farming", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                isSeedProperty,
                new GUIContent("Seed", "체크하면 밭에 심을 수 있는 씨앗 아이템으로 사용합니다."));
            bool seedStateChanged = EditorGUI.EndChangeCheck();
            if (seedStateChanged
                && !isSeedProperty.hasMultipleDifferentValues
                && !isSeedProperty.boolValue
                && seedTargetResourceProperty != null)
            {
                seedTargetResourceProperty.objectReferenceValue = null;
            }

            if (!isSeedProperty.hasMultipleDifferentValues && isSeedProperty.boolValue)
            {
                DrawSeedTargetResourceField(seedTargetResourceProperty);
            }
        }

        if (storesFluidProperty != null
            || fluidDisplayColorProperty != null
            || (fluidOutputLitersPerSecondProperty != null && AllSelectedDefinitionsAreFluidOutputMachines()))
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Fluid", EditorStyles.boldLabel);

            if (fluidDisplayColorProperty != null && AllSelectedDefinitionsAreFluidItems())
            {
                EditorGUILayout.PropertyField(
                    fluidDisplayColorProperty,
                    new GUIContent("Pipe DP Color"));
            }

            if (storesFluidProperty != null)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(storesFluidProperty, new GUIContent("Store Fluid"));
                bool storesFluidChanged = EditorGUI.EndChangeCheck();
                if (storesFluidChanged
                    && !storesFluidProperty.hasMultipleDifferentValues
                    && !storesFluidProperty.boolValue
                    && fluidStorageLitersProperty != null)
                {
                    fluidStorageLitersProperty.floatValue = 0f;
                }

                if (storesFluidProperty.hasMultipleDifferentValues || storesFluidProperty.boolValue)
                {
                    DrawMultiClampedFloatProperty(
                        fluidStorageLitersProperty,
                        new GUIContent("Fluid Storage Liters"),
                        0f);
                }
            }

            if (bucketFillDurationSecondsProperty != null && AllSelectedDefinitionsAreEmptyBuckets())
            {
                DrawMultiClampedFloatProperty(
                    bucketFillDurationSecondsProperty,
                    new GUIContent(
                        "Fill Duration (sec)",
                        "Pipe 출구에서 빈 Bucket이 Water Bucket으로 완전히 차는 시간입니다."),
                    0.1f);
            }

            if (fluidOutputLitersPerSecondProperty != null && AllSelectedDefinitionsAreFluidOutputMachines())
            {
                DrawMultiClampedFloatProperty(
                    fluidOutputLitersPerSecondProperty,
                    new GUIContent("Output Rate (L/s)", "유체 생산 설비의 초당 출력량입니다."),
                    0f);
            }
        }

        DrawMultiClampedFloatProperty(
            craftingDurationSecondsProperty,
            new GUIContent("Crafting Time (sec)"),
            0.01f);

        if (energyTypeProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Energy", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(energyTypeProperty, new GUIContent("Energy Type"));
            bool energyTypeChanged = EditorGUI.EndChangeCheck();
            if (energyTypeChanged
                && !energyTypeProperty.hasMultipleDifferentValues
                && energyTypeProperty.enumValueIndex == (int)ItemDefinition.EnergyType.None
                && energyAmountProperty != null)
            {
                energyAmountProperty.longValue = 0L;
            }

            if (energyTypeProperty.hasMultipleDifferentValues
                || energyTypeProperty.enumValueIndex != (int)ItemDefinition.EnergyType.None)
            {
                DrawMultiClampedLongProperty(
                    energyAmountProperty,
                    new GUIContent("Energy Amount"),
                    0L);
            }

            if (!energyTypeProperty.hasMultipleDifferentValues
                && ItemDefinition.IsFoodEnergyType(
                    (ItemDefinition.EnergyType)energyTypeProperty.enumValueIndex))
            {
                DrawMultiPropertyField(
                    eatRewardItemProperty,
                    new GUIContent(
                        "Eat Reward Item",
                        "이 음식을 먹었을 때 확률적으로 획득하는 아이템입니다."));
                DrawMultiPropertyField(
                    eatRewardChancePercentProperty,
                    new GUIContent(
                        "Eat Reward Chance (%)",
                        "음식 1개를 먹을 때 보상 아이템을 획득할 확률입니다."));
            }
        }

        if (useEnergyTypeProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Use Energy", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(useEnergyTypeProperty, new GUIContent("Use Energy Type"));
            bool useEnergyTypeChanged = EditorGUI.EndChangeCheck();
            if (useEnergyTypeChanged
                && !useEnergyTypeProperty.hasMultipleDifferentValues
                && useEnergyTypeProperty.enumValueIndex == (int)ItemDefinition.EnergyType.None)
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

            if (useEnergyTypeProperty.hasMultipleDifferentValues
                || useEnergyTypeProperty.enumValueIndex != (int)ItemDefinition.EnergyType.None)
            {
                string useEnergyAmountLabel = !useEnergyTypeProperty.hasMultipleDifferentValues
                    && useEnergyTypeProperty.enumValueIndex == (int)ItemDefinition.EnergyType.Electricity
                        ? "Use Energy Amount (kW)"
                        : "Use Energy Amount / Sec";
                DrawMultiClampedFloatProperty(
                    useEnergyAmountProperty,
                    new GUIContent(useEnergyAmountLabel),
                    0f);
                DrawMultiClampedFloatProperty(
                    completeEnergyProperty,
                    new GUIContent("Complete Energy"),
                    0f);
            }
        }

        if (AllSelectedDefinitionsAreUtilityPoles()
            && (utilityPoleConnectionRadiusProperty != null
                || utilityPoleSupplyRadiusProperty != null))
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Utility Pole", EditorStyles.boldLabel);
            DrawMultiClampedLongProperty(
                utilityPoleConnectionRadiusProperty,
                new GUIContent("Connection Radius"),
                0L);
            DrawMultiClampedLongProperty(
                utilityPoleSupplyRadiusProperty,
                new GUIContent("Supply Radius"),
                0L);
        }

        if (AllSelectedDefinitionsAreSprinklers())
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Sprinkler", EditorStyles.boldLabel);
            DrawMultiClampedLongProperty(
                sprinklerRangeRadiusProperty,
                new GUIContent("Range Radius", "물을 분사하는 반경(칸)입니다."),
                0L);
            DrawMultiClampedFloatProperty(
                sprinklerWaterLitersPerCellProperty,
                new GUIContent("Water Per Cell (L)", "한 번 분사할 때 범위의 각 칸마다 소비하는 물입니다."),
                0.001f);
            DrawMultiClampedFloatProperty(
                sprinklerSprayIntervalSecondsProperty,
                new GUIContent("Spray Interval (sec)"),
                0.1f);
            DrawMultiClampedFloatProperty(
                sprinklerNozzleRotationDegreesPerSecondProperty,
                new GUIContent("Nozzle Rotation (deg/sec)"),
                0f);
        }

        if (AllSelectedDefinitionsAreSeedPlanters())
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Seed Planter", EditorStyles.boldLabel);
            DrawMultiClampedFloatProperty(
                seedPlanterPlantDurationSecondsProperty,
                new GUIContent("Plant Duration (sec)"),
                0.1f);
        }

        if (!serializedObject.ApplyModifiedProperties())
        {
            return;
        }

        for (int i = 0; i < selectedItemDefinitionsInOrder.Count; i++)
        {
            ItemDefinition definition = selectedItemDefinitionsInOrder[i];
            if (definition == null)
            {
                continue;
            }

            EditorUtility.SetDirty(definition);
            if (EditorApplication.isPlaying)
            {
                ItemLightController.RefreshDefinition(definition);
            }
        }

        InvalidateDefinitionPresentationCache();
        DefinitionCatalog.NotifyChanged();
        Repaint();
    }

    private static void DrawMultiPropertyField(SerializedProperty property, GUIContent label)
    {
        if (property != null)
        {
            EditorGUILayout.PropertyField(property, label);
        }
    }

    private static void DrawMultiClampedFloatProperty(
        SerializedProperty property,
        GUIContent label,
        float minimum)
    {
        if (property == null)
        {
            return;
        }

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(property, label);
        if (EditorGUI.EndChangeCheck())
        {
            property.floatValue = Mathf.Max(minimum, property.floatValue);
        }
    }

    private static void DrawMultiClampedLongProperty(
        SerializedProperty property,
        GUIContent label,
        long minimum)
    {
        if (property == null)
        {
            return;
        }

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(property, label);
        if (EditorGUI.EndChangeCheck())
        {
            property.longValue = Math.Max(minimum, property.longValue);
        }
    }

    private bool AllSelectedDefinitionsSupportUpgrade()
    {
        if (selectedItemDefinitionsInOrder.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < selectedItemDefinitionsInOrder.Count; i++)
        {
            if (!(selectedItemDefinitionsInOrder[i].mapObject is InputOutputModule inputOutputModule)
                || inputOutputModule.ParentInputOutputModuleItem == null)
            {
                return false;
            }
        }

        return true;
    }

    private bool AllSelectedDefinitionsShowCapacity()
    {
        if (selectedItemDefinitionsInOrder.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < selectedItemDefinitionsInOrder.Count; i++)
        {
            if (!ShouldShowCapacity(selectedItemDefinitionsInOrder[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool AllSelectedDefinitionsAreFluidItems()
    {
        if (selectedItemDefinitionsInOrder.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < selectedItemDefinitionsInOrder.Count; i++)
        {
            if (!InputOutputModule.IsFluidItemDefinition(selectedItemDefinitionsInOrder[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool AllSelectedDefinitionsAreEmptyBuckets()
    {
        if (selectedItemDefinitionsInOrder.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < selectedItemDefinitionsInOrder.Count; i++)
        {
            if (!Bucket.IsEmptyBucketDefinition(selectedItemDefinitionsInOrder[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool AllSelectedDefinitionsAreUtilityPoles()
    {
        if (selectedItemDefinitionsInOrder.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < selectedItemDefinitionsInOrder.Count; i++)
        {
            if (!(selectedItemDefinitionsInOrder[i].mapObject is UtilityPole))
            {
                return false;
            }
        }

        return true;
    }

    private bool AllSelectedDefinitionsAreFluidOutputMachines()
    {
        if (selectedItemDefinitionsInOrder.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < selectedItemDefinitionsInOrder.Count; i++)
        {
            if (!IsFluidOutputMachine(selectedItemDefinitionsInOrder[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFluidOutputMachine(ItemDefinition definition)
    {
        return definition != null
               && (definition.mapObject is OilDrillingMachine || definition.mapObject is Pump);
    }

    private bool AllSelectedDefinitionsAreSprinklers()
    {
        if (selectedItemDefinitionsInOrder.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < selectedItemDefinitionsInOrder.Count; i++)
        {
            if (!(selectedItemDefinitionsInOrder[i].mapObject is Sprinkler))
            {
                return false;
            }
        }

        return true;
    }

    private bool AllSelectedDefinitionsAreSeedPlanters()
    {
        if (selectedItemDefinitionsInOrder.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < selectedItemDefinitionsInOrder.Count; i++)
        {
            if (!(selectedItemDefinitionsInOrder[i].mapObject is SeedPlanter))
            {
                return false;
            }
        }

        return true;
    }

    private void DrawSelectedItemFields(ItemDefinition definition, List<ItemDefinition> definitions)
    {
        SerializedObject serializedObject = GetSelectedDefinitionSerializedObject(definition);
        if (serializedObject == null)
        {
            return;
        }

        serializedObject.UpdateIfRequiredOrScript();

        SerializedProperty itemNameProperty = GetSelectedDefinitionProperty(serializedObject, "itemName");
        SerializedProperty idProperty = GetSelectedDefinitionProperty(serializedObject, "id");
        SerializedProperty mapObjectProperty = GetSelectedDefinitionProperty(serializedObject, "mapObject");
        SerializedProperty portableMeshProperty = GetSelectedDefinitionProperty(serializedObject, "portableMesh");
        SerializedProperty portableMatProperty = GetSelectedDefinitionProperty(serializedObject, "portableMat");
        SerializedProperty iconProperty = GetSelectedDefinitionProperty(serializedObject, "icon");
        SerializedProperty interactionButtonListProperty = GetSelectedDefinitionProperty(serializedObject, "interactionButtonList");
        SerializedProperty lightModeProperty = GetSelectedDefinitionProperty(serializedObject, "lightMode");
        SerializedProperty lightRangeProperty = GetSelectedDefinitionProperty(serializedObject, "lightRange");
        SerializedProperty lightIntensityMultiplierProperty = GetSelectedDefinitionProperty(serializedObject, "lightIntensityMultiplier");
        SerializedProperty sizeProperty = GetSelectedDefinitionProperty(serializedObject, "size");
        SerializedProperty itemFilterProperty = GetSelectedDefinitionProperty(serializedObject, "itemFilter");
        SerializedProperty ignoreFilterProperty = GetSelectedDefinitionProperty(serializedObject, "ignoreFilter");
        SerializedProperty oneItemProperty = GetSelectedDefinitionProperty(serializedObject, "oneItem");
        SerializedProperty isManualProperty = GetSelectedDefinitionProperty(serializedObject, "isManual");
        SerializedProperty manualTargetItemProperty = GetSelectedDefinitionProperty(serializedObject, "manualTargetItem");
        SerializedProperty upgradeableProperty = GetSelectedDefinitionProperty(serializedObject, "upgradeable");
        SerializedProperty capacityProperty = GetSelectedDefinitionProperty(serializedObject, "capacity");
        SerializedProperty storesFluidProperty = GetSelectedDefinitionProperty(serializedObject, "storesFluid");
        SerializedProperty fluidStorageLitersProperty = GetSelectedDefinitionProperty(serializedObject, "fluidStorageLiters");
        SerializedProperty fluidOutputLitersPerSecondProperty = GetSelectedDefinitionProperty(serializedObject, "fluidOutputLitersPerSecond");
        SerializedProperty fluidDisplayColorProperty = GetSelectedDefinitionProperty(serializedObject, "fluidDisplayColor");
        SerializedProperty bucketFillDurationSecondsProperty = GetSelectedDefinitionProperty(serializedObject, "bucketFillDurationSeconds");
        SerializedProperty undergroundPipeMaxDistanceProperty = GetSelectedDefinitionProperty(serializedObject, "undergroundPipeMaxDistance");
        SerializedProperty energyTypeProperty = GetSelectedDefinitionProperty(serializedObject, "energyType");
        SerializedProperty energyAmountProperty = GetSelectedDefinitionProperty(serializedObject, "energyAmount");
        SerializedProperty eatRewardItemProperty = GetSelectedDefinitionProperty(serializedObject, "eatRewardItem");
        SerializedProperty eatRewardChancePercentProperty = GetSelectedDefinitionProperty(serializedObject, "eatRewardChancePercent");
        SerializedProperty isSeedProperty = GetSelectedDefinitionProperty(serializedObject, "isSeed");
        SerializedProperty seedTargetResourceProperty = GetSelectedDefinitionProperty(serializedObject, "seedTargetResource");
        SerializedProperty useEnergyTypeProperty = GetSelectedDefinitionProperty(serializedObject, "useEnergyType");
        SerializedProperty useEnergyAmountProperty = GetSelectedDefinitionProperty(serializedObject, "useEnergyAmount");
        SerializedProperty completeEnergyProperty = GetSelectedDefinitionProperty(serializedObject, "completeEnergy");
        SerializedProperty utilityPoleConnectionRadiusProperty = GetSelectedDefinitionProperty(serializedObject, "utilityPoleConnectionRadius");
        SerializedProperty utilityPoleSupplyRadiusProperty = GetSelectedDefinitionProperty(serializedObject, "utilityPoleSupplyRadius");
        SerializedProperty sprinklerRangeRadiusProperty = GetSelectedDefinitionProperty(serializedObject, "sprinklerRangeRadius");
        SerializedProperty sprinklerWaterLitersPerCellProperty = GetSelectedDefinitionProperty(serializedObject, "sprinklerWaterLitersPerCell");
        SerializedProperty sprinklerSprayIntervalSecondsProperty = GetSelectedDefinitionProperty(serializedObject, "sprinklerSprayIntervalSeconds");
        SerializedProperty sprinklerNozzleRotationDegreesPerSecondProperty = GetSelectedDefinitionProperty(serializedObject, "sprinklerNozzleRotationDegreesPerSecond");
        SerializedProperty seedPlanterPlantDurationSecondsProperty = GetSelectedDefinitionProperty(serializedObject, "seedPlanterPlantDurationSeconds");
        SerializedProperty craftingDurationSecondsProperty = GetSelectedDefinitionProperty(serializedObject, "craftingDurationSeconds");

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
        if (oneItemProperty != null)
        {
            EditorGUILayout.PropertyField(
                oneItemProperty,
                new GUIContent(
                    "One Item",
                    "체크하면 모든 보관 컨텐츠에서 스택 하나당 이 아이템을 하나만 보관할 수 있습니다."));
        }
        DrawMapObjectFields(mapObjectProperty.objectReferenceValue as MapObject, definitions);

        if (interactionButtonListProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Interaction", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(interactionButtonListProperty, new GUIContent("Interaction Button List"), true);
            DrawInteractionDistanceField(mapObjectProperty.objectReferenceValue as MapObject);
        }

        if (lightModeProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
            int lightModeIndex = Mathf.Clamp(
                lightModeProperty.enumValueIndex,
                0,
                ItemLightModeLabels.Length - 1);
            lightModeProperty.enumValueIndex = EditorGUILayout.Popup(
                "Light Condition",
                lightModeIndex,
                ItemLightModeLabels);
            if (lightModeProperty.enumValueIndex != (int)ItemDefinition.ItemLightMode.None
                && lightRangeProperty != null)
            {
                lightRangeProperty.floatValue = Mathf.Max(0.1f, lightRangeProperty.floatValue);
                EditorGUILayout.PropertyField(lightRangeProperty, new GUIContent("Light Range"));
                if (lightIntensityMultiplierProperty != null)
                {
                    lightIntensityMultiplierProperty.floatValue = Mathf.Max(
                        0.01f,
                        lightIntensityMultiplierProperty.floatValue);
                    EditorGUILayout.PropertyField(
                        lightIntensityMultiplierProperty,
                        new GUIContent("Light Intensity Multiplier"));
                }
            }
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(sizeProperty, new GUIContent("Size"));
        if (itemFilterProperty != null)
        {
            EditorGUILayout.PropertyField(itemFilterProperty, new GUIContent("Item Filter"));
        }
        if (ignoreFilterProperty != null)
        {
            EditorGUILayout.PropertyField(
                ignoreFilterProperty,
                new GUIContent("Ignore Filter", "체크하면 박스의 아이템 필터 목록에서 제외합니다."));
        }
        if (isManualProperty != null)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                isManualProperty,
                new GUIContent("Manual Item", "제작법 설명서 등 Manual 용도로 사용하는 아이템이면 체크합니다."));
            bool manualStateChanged = EditorGUI.EndChangeCheck();
            if (manualStateChanged && !isManualProperty.boolValue && manualTargetItemProperty != null)
            {
                manualTargetItemProperty.objectReferenceValue = null;
            }

            if (isManualProperty.boolValue)
            {
                DrawManualTargetItemField(
                    manualTargetItemProperty,
                    definitions);
            }
        }
        if (upgradeableProperty != null
            && definition.mapObject is InputOutputModule inputOutputModule
            && inputOutputModule.ParentInputOutputModuleItem != null)
        {
            EditorGUILayout.PropertyField(
                upgradeableProperty,
                new GUIContent("Upgrade able", "체크하면 부모 I/O 모듈을 이 아이템으로 업그레이드할 수 있습니다."));
        }
        if (ShouldShowCapacity(definition) && capacityProperty != null)
        {
            if (capacityProperty.intValue <= 0)
            {
                capacityProperty.intValue = 10;
            }

            EditorGUILayout.PropertyField(capacityProperty, new GUIContent("Capacity"));
        }
        if (isSeedProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Farming", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                isSeedProperty,
                new GUIContent("Seed", "체크하면 밭에 심을 수 있는 씨앗 아이템으로 사용합니다."));
            if (isSeedProperty.boolValue)
            {
                DrawSeedTargetResourceField(seedTargetResourceProperty);
            }
            else if (seedTargetResourceProperty != null)
            {
                seedTargetResourceProperty.objectReferenceValue = null;
            }
        }
        if (storesFluidProperty != null
            || fluidDisplayColorProperty != null
            || (fluidOutputLitersPerSecondProperty != null && IsFluidOutputMachine(definition)))
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

            if (bucketFillDurationSecondsProperty != null && Bucket.IsEmptyBucketDefinition(definition))
            {
                bucketFillDurationSecondsProperty.floatValue = Mathf.Max(
                    0.1f,
                    bucketFillDurationSecondsProperty.floatValue);
                EditorGUILayout.PropertyField(
                    bucketFillDurationSecondsProperty,
                    new GUIContent(
                        "Fill Duration (sec)",
                        "Pipe 출구에서 빈 Bucket이 Water Bucket으로 완전히 차는 시간입니다."));
            }

            if (fluidOutputLitersPerSecondProperty != null && IsFluidOutputMachine(definition))
            {
                fluidOutputLitersPerSecondProperty.floatValue = Mathf.Max(
                    0f,
                    fluidOutputLitersPerSecondProperty.floatValue);
                EditorGUILayout.PropertyField(
                    fluidOutputLitersPerSecondProperty,
                    new GUIContent("Output Rate (L/s)", "유체 생산 설비의 초당 출력량입니다."));
            }
        }
        if (definition.mapObject is UndergroundPipe && undergroundPipeMaxDistanceProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Underground Pipe", EditorStyles.boldLabel);
            undergroundPipeMaxDistanceProperty.intValue = Mathf.Max(
                2,
                undergroundPipeMaxDistanceProperty.intValue);
            EditorGUILayout.PropertyField(
                undergroundPipeMaxDistanceProperty,
                new GUIContent(
                    "Max Distance",
                    "입구와 출구를 포함한 최대 설치 거리입니다."));
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

            if (ItemDefinition.IsFoodEnergyType(energyType))
            {
                EditorGUILayout.PropertyField(
                    eatRewardItemProperty,
                    new GUIContent(
                        "Eat Reward Item",
                        "이 음식을 먹었을 때 확률적으로 획득하는 아이템입니다."));
                EditorGUILayout.PropertyField(
                    eatRewardChancePercentProperty,
                    new GUIContent(
                        "Eat Reward Chance (%)",
                        "음식 1개를 먹을 때 보상 아이템을 획득할 확률입니다."));
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

        if (definition.mapObject is Sprinkler)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Sprinkler", EditorStyles.boldLabel);
            sprinklerRangeRadiusProperty.intValue = Mathf.Max(0, sprinklerRangeRadiusProperty.intValue);
            sprinklerWaterLitersPerCellProperty.floatValue = Mathf.Max(
                0.001f,
                sprinklerWaterLitersPerCellProperty.floatValue);
            sprinklerSprayIntervalSecondsProperty.floatValue = Mathf.Max(
                0.1f,
                sprinklerSprayIntervalSecondsProperty.floatValue);
            sprinklerNozzleRotationDegreesPerSecondProperty.floatValue = Mathf.Max(
                0f,
                sprinklerNozzleRotationDegreesPerSecondProperty.floatValue);
            EditorGUILayout.PropertyField(
                sprinklerRangeRadiusProperty,
                new GUIContent("Range Radius", "물을 분사하는 반경(칸)입니다."));
            EditorGUILayout.PropertyField(
                sprinklerWaterLitersPerCellProperty,
                new GUIContent("Water Per Cell (L)", "한 번 분사할 때 범위의 각 칸마다 소비하는 물입니다."));
            EditorGUILayout.PropertyField(
                sprinklerSprayIntervalSecondsProperty,
                new GUIContent("Spray Interval (sec)"));
            EditorGUILayout.PropertyField(
                sprinklerNozzleRotationDegreesPerSecondProperty,
                new GUIContent("Nozzle Rotation (deg/sec)"));
        }

        if (definition.mapObject is SeedPlanter && seedPlanterPlantDurationSecondsProperty != null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Seed Planter", EditorStyles.boldLabel);
            seedPlanterPlantDurationSecondsProperty.floatValue = Mathf.Max(
                0.1f,
                seedPlanterPlantDurationSecondsProperty.floatValue);
            EditorGUILayout.PropertyField(
                seedPlanterPlantDurationSecondsProperty,
                new GUIContent("Plant Duration (sec)"));
        }

        if (serializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(definition);
            if (EditorApplication.isPlaying)
            {
                ItemLightController.RefreshDefinition(definition);
            }
            InvalidateDefinitionPresentationCache();
            DefinitionCatalog.NotifyChanged();
            Repaint();
        }
    }

    private SerializedObject GetMultiSelectedDefinitionSerializedObject()
    {
        int selectedCount = selectedItemDefinitionsInOrder.Count;
        if (selectedCount <= 1)
        {
            ClearMultiSelectedSerializedObjectCache();
            return null;
        }

        bool cacheMatchesSelection = cachedMultiSerializedDefinition != null
                                     && cachedMultiSerializedDefinitionTargets.Length == selectedCount;
        if (cacheMatchesSelection)
        {
            for (int i = 0; i < selectedCount; i++)
            {
                ItemDefinition definition = selectedItemDefinitionsInOrder[i];
                if (definition == null || cachedMultiSerializedDefinitionTargets[i] != definition)
                {
                    cacheMatchesSelection = false;
                    break;
                }
            }

        }

        if (cacheMatchesSelection)
        {
            return cachedMultiSerializedDefinition;
        }

        ClearMultiSelectedSerializedObjectCache();
        cachedMultiSerializedDefinitionTargets = new ItemDefinition[selectedCount];
        for (int i = 0; i < selectedCount; i++)
        {
            cachedMultiSerializedDefinitionTargets[i] = selectedItemDefinitionsInOrder[i];
        }

        cachedMultiSerializedDefinition =
            new SerializedObject(cachedMultiSerializedDefinitionTargets);
        return cachedMultiSerializedDefinition;
    }

    private SerializedProperty GetMultiSelectedDefinitionProperty(
        SerializedObject serializedObject,
        string propertyPath)
    {
        if (serializedObject == null || string.IsNullOrEmpty(propertyPath))
        {
            return null;
        }

        if (cachedMultiSelectedDefinitionProperties.TryGetValue(
                propertyPath,
                out SerializedProperty property))
        {
            return property;
        }

        property = serializedObject.FindProperty(propertyPath);
        cachedMultiSelectedDefinitionProperties[propertyPath] = property;
        return property;
    }

    private SerializedObject GetSelectedDefinitionSerializedObject(ItemDefinition definition)
    {
        if (definition == null)
        {
            ClearSelectedSerializedObjectCaches();
            return null;
        }

        if (cachedSerializedDefinition != null
            && cachedSerializedDefinitionTarget == definition
            && cachedSerializedDefinition.targetObject == definition)
        {
            return cachedSerializedDefinition;
        }

        ClearSelectedSerializedObjectCaches();
        cachedSerializedDefinitionTarget = definition;
        cachedSerializedDefinition = new SerializedObject(definition);
        return cachedSerializedDefinition;
    }

    private SerializedProperty GetSelectedDefinitionProperty(
        SerializedObject serializedObject,
        string propertyPath)
    {
        if (serializedObject == null || string.IsNullOrEmpty(propertyPath))
        {
            return null;
        }

        if (cachedSelectedDefinitionProperties.TryGetValue(
                propertyPath,
                out SerializedProperty property))
        {
            return property;
        }

        property = serializedObject.FindProperty(propertyPath);
        cachedSelectedDefinitionProperties[propertyPath] = property;
        return property;
    }

    private SerializedObject GetSelectedMapObjectSerializedObject(MapObject mapObject)
    {
        if (mapObject == null)
        {
            ClearSelectedMapObjectSerializedObjectCache();
            return null;
        }

        if (cachedSerializedMapObject != null
            && cachedSerializedMapObjectTarget == mapObject
            && cachedSerializedMapObject.targetObject == mapObject)
        {
            return cachedSerializedMapObject;
        }

        ClearSelectedMapObjectSerializedObjectCache();
        cachedSerializedMapObjectTarget = mapObject;
        cachedSerializedMapObject = new SerializedObject(mapObject);
        return cachedSerializedMapObject;
    }

    private SerializedProperty GetSelectedMapObjectProperty(
        SerializedObject serializedObject,
        string propertyPath)
    {
        if (serializedObject == null || string.IsNullOrEmpty(propertyPath))
        {
            return null;
        }

        if (cachedSelectedMapObjectProperties.TryGetValue(
                propertyPath,
                out SerializedProperty property))
        {
            return property;
        }

        property = serializedObject.FindProperty(propertyPath);
        cachedSelectedMapObjectProperties[propertyPath] = property;
        return property;
    }

    private SerializedObject GetSelectedConveyorSerializedObject(ConveyorBelt conveyorBelt)
    {
        if (conveyorBelt == null)
        {
            cachedSerializedConveyorTarget = null;
            cachedSerializedConveyor = null;
            return null;
        }

        if (cachedSerializedConveyor != null
            && cachedSerializedConveyorTarget == conveyorBelt
            && cachedSerializedConveyor.targetObject == conveyorBelt)
        {
            return cachedSerializedConveyor;
        }

        cachedSerializedConveyorTarget = conveyorBelt;
        cachedSerializedConveyor = new SerializedObject(conveyorBelt);
        return cachedSerializedConveyor;
    }

    private void ClearSelectedSerializedObjectCaches()
    {
        cachedSerializedDefinitionTarget = null;
        cachedSerializedDefinition = null;
        cachedSelectedDefinitionProperties.Clear();
        ClearMultiSelectedSerializedObjectCache();
        ClearSelectedMapObjectSerializedObjectCache();
    }

    private void ClearMultiSelectedSerializedObjectCache()
    {
        cachedMultiSerializedDefinition = null;
        cachedMultiSerializedDefinitionTargets = Array.Empty<ItemDefinition>();
        cachedMultiSelectedDefinitionProperties.Clear();
    }

    private void ClearSelectedMapObjectSerializedObjectCache()
    {
        cachedSerializedMapObjectTarget = null;
        cachedSerializedMapObject = null;
        cachedSelectedMapObjectProperties.Clear();
        cachedSerializedConveyorTarget = null;
        cachedSerializedConveyor = null;
    }

    private static bool ShouldShowCapacity(ItemDefinition definition)
    {
        if (definition == null || !(definition.mapObject is InstallationObject installationObject))
        {
            return false;
        }

        return installationObject is BoxObject
               || installationObject is Handcart
               || (installationObject.MapFilter & InstallationMapFilter.ItemArea) != 0;
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
            parsedFilter = InstallationObject.NormalizeMapFilter(parsedFilter);
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

        parsedFilter = InstallationObject.NormalizeMapFilter(combinedFilter);
        return true;
    }

    private void DrawMapObjectFields(MapObject mapObject, List<ItemDefinition> definitions)
    {
        if (mapObject == null)
        {
            return;
        }

        SerializedObject mapObjectSerializedObject = GetSelectedMapObjectSerializedObject(mapObject);
        if (mapObjectSerializedObject == null)
        {
            return;
        }

        mapObjectSerializedObject.UpdateIfRequiredOrScript();

        SerializedProperty mapStatusProperty = GetSelectedMapObjectProperty(mapObjectSerializedObject, "mapStatus");
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

        SerializedProperty multiFocusModeProperty = GetSelectedMapObjectProperty(mapObjectSerializedObject, "multiFocusMode");
        if (multiFocusModeProperty != null)
        {
            DrawMultiFocusModeField(multiFocusModeProperty);
        }

        bool shouldSyncConveyorVariantSpeed = false;
        ConveyorBelt conveyorBeltForSpeed = ResolveConveyorBelt(mapObject);
        bool usesSeparateConveyorSerializedObject = conveyorBeltForSpeed != null && conveyorBeltForSpeed != mapObject;
        SerializedObject conveyorSerializedObject = usesSeparateConveyorSerializedObject
            ? GetSelectedConveyorSerializedObject(conveyorBeltForSpeed)
            : mapObjectSerializedObject;
        if (conveyorBeltForSpeed != null)
        {
            if (usesSeparateConveyorSerializedObject)
            {
                conveyorSerializedObject.UpdateIfRequiredOrScript();
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

        if (mapObject is Vehicle)
        {
            DrawVehicleFields(mapObjectSerializedObject, ShouldExposeVehicleStats(mapObject));
        }

        if (mapObject is InstallationObject)
        {
            SerializedProperty mapFilterProperty = GetSelectedMapObjectProperty(mapObjectSerializedObject, "mapFilter");
            if (mapFilterProperty != null)
            {
                InstallationMapFilter currentFilter = InstallationObject.NormalizeMapFilter(
                    (InstallationMapFilter)mapFilterProperty.intValue);
                mapFilterProperty.intValue = (int)currentFilter;

                EditorGUI.BeginChangeCheck();
                InstallationMapFilter nextFilter = (InstallationMapFilter)EditorGUILayout.EnumFlagsField("Map Filter", currentFilter);
                nextFilter = InstallationObject.NormalizeMapFilter(nextFilter);

                if (EditorGUI.EndChangeCheck())
                {
                    mapFilterProperty.intValue = (int)nextFilter;
                }
            }

            SerializedProperty rotationFilterProperty = GetSelectedMapObjectProperty(mapObjectSerializedObject, "rotationFilter");
            if (rotationFilterProperty != null)
            {
                InstallationRotationFilter currentRotationFilter = InstallationObject.NormalizeRotationFilter(
                    (InstallationRotationFilter)rotationFilterProperty.intValue);
                InstallationRotationFilter nextRotationFilter = (InstallationRotationFilter)EditorGUILayout.EnumPopup(
                    "Rotation Filter",
                    currentRotationFilter);
                rotationFilterProperty.intValue = (int)InstallationObject.NormalizeRotationFilter(nextRotationFilter);
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

    private static void DrawVehicleFields(
        SerializedObject mapObjectSerializedObject,
        bool exposeMovementStats)
    {
        if (mapObjectSerializedObject == null)
        {
            return;
        }

        SerializedProperty accelerationProperty = FindSerializedProperty(mapObjectSerializedObject, "vehicleAccelerationPerSecond");
        SerializedProperty decelerationProperty = FindSerializedProperty(mapObjectSerializedObject, "vehicleDecelerationPerSecond");
        SerializedProperty maxSpeedProperty = FindSerializedProperty(mapObjectSerializedObject, "vehicleMaxSpeed");
        SerializedProperty massProperty = FindSerializedProperty(mapObjectSerializedObject, "vehicleMass");
        if ((!exposeMovementStats
             || (accelerationProperty == null && decelerationProperty == null && maxSpeedProperty == null))
            && massProperty == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Vehicle", EditorStyles.miniBoldLabel);
        if (exposeMovementStats && accelerationProperty != null)
        {
            accelerationProperty.floatValue = Mathf.Max(0.01f, accelerationProperty.floatValue);
            EditorGUILayout.PropertyField(accelerationProperty, new GUIContent("Acceleration / s"));
        }

        if (exposeMovementStats && decelerationProperty != null)
        {
            decelerationProperty.floatValue = Mathf.Max(0.01f, decelerationProperty.floatValue);
            EditorGUILayout.PropertyField(decelerationProperty, new GUIContent("Deceleration / s"));
        }

        if (exposeMovementStats && maxSpeedProperty != null)
        {
            maxSpeedProperty.floatValue = Mathf.Max(0.01f, maxSpeedProperty.floatValue);
            EditorGUILayout.PropertyField(maxSpeedProperty, new GUIContent("Max Speed"));
        }

        if (massProperty != null)
        {
            massProperty.floatValue = Mathf.Max(0.01f, massProperty.floatValue);
            EditorGUILayout.PropertyField(massProperty, new GUIContent("Mass"));
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
        SerializedProperty parentItemProperty = mapObjectSerializedObject.FindProperty("parentInputOutputModuleItem");
        if (inputListProperty == null || outputListProperty == null)
        {
            return;
        }

        EnsureInputOutputPairArraySizes(inputListProperty, outputListProperty, legacyOutputProperty);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Input Output Module", EditorStyles.boldLabel);
        UnityEngine.Object targetObject = mapObjectSerializedObject.targetObject;
        InputOutputModule inputOutputModule = targetObject as InputOutputModule;
        int inheritedPairCount = 0;
        ItemDefinition parentItem = DrawParentInputOutputModuleItemField(
            parentItemProperty,
            definitions,
            inputOutputModule);
        InputOutputModule parentModule = parentItem != null
            ? parentItem.mapObject as InputOutputModule
            : null;
        if (parentItem != null && parentModule == null)
        {
            EditorGUILayout.HelpBox(
                "Parent IOModule Item에는 MapObject가 InputOutputModule인 아이템만 지정할 수 있습니다.",
                MessageType.Error);
        }
        else if (parentModule == inputOutputModule)
        {
            EditorGUILayout.HelpBox("현재 아이템 자신은 부모로 지정할 수 없습니다.", MessageType.Error);
        }

        if (parentModule != null)
        {
            DrawReferencedItemPreview(parentItem);
            IReadOnlyList<InputOutputModule.ItemIoEntry> inheritedInputs = parentModule.InputList;
            IReadOnlyList<InputOutputModule.ItemIoEntry> inheritedOutputs = parentModule.OutputList;
            inheritedPairCount = Mathf.Min(inheritedInputs.Count, inheritedOutputs.Count);
            EditorGUILayout.LabelField($"Inherited Pairs ({inheritedPairCount})", EditorStyles.miniBoldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                for (int i = 0; i < inheritedPairCount; i++)
                {
                    InputOutputModule.ItemIoEntry inheritedInput = inheritedInputs[i];
                    InputOutputModule.ItemIoEntry inheritedOutput = inheritedOutputs[i];
                    string inputName = inheritedInput.itemDefinition != null
                        ? inheritedInput.itemDefinition.itemName
                        : "None";
                    string outputName = inheritedOutput.itemDefinition != null
                        ? inheritedOutput.itemDefinition.itemName
                        : "None";
                    EditorGUILayout.TextField(
                        $"{i + 1}. {inputName} x{Mathf.Max(1, inheritedInput.count)}  →  "
                        + $"{outputName} x{Mathf.Max(1, inheritedOutput.count)}");
                }
            }
        }

        int pairCount = inputListProperty.arraySize;
        string sectionFoldoutKey = GetInputOutputPairSectionFoldoutKey(targetObject);
        InitializeInputOutputPairFoldoutStates(sectionFoldoutKey, targetObject, pairCount);
        bool isSectionExpanded = string.IsNullOrEmpty(sectionFoldoutKey)
            || !collapsedInputOutputPairSectionKeys.Contains(sectionFoldoutKey);
        EditorGUILayout.BeginHorizontal();
        bool nextSectionExpanded = EditorGUILayout.Foldout(
            isSectionExpanded,
            $"Local Input / Output Pairs ({pairCount})",
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
        DrawInputOutputRectGridFields(mapObjectSerializedObject, inheritedPairCount + pairCount);
    }

    private ItemDefinition DrawParentInputOutputModuleItemField(
        SerializedProperty parentItemProperty,
        List<ItemDefinition> definitions,
        InputOutputModule currentModule)
    {
        if (parentItemProperty == null)
        {
            return null;
        }

        EnsureParentInputOutputModuleItemOptionCache(definitions);
        ItemDefinition currentItem = parentItemProperty.objectReferenceValue as ItemDefinition;
        int currentIndex = currentItem != null
                           && cachedParentInputOutputModuleItemOptionIndexes.TryGetValue(
                               currentItem.GetInstanceID(),
                               out int resolvedIndex)
            ? resolvedIndex
            : 0;

        Rect rowRect = EditorGUILayout.GetControlRect();
        Rect popupRect = EditorGUI.PrefixLabel(
            rowRect,
            new GUIContent(
                "Parent IOModule",
                "부모로 사용할 IOModule 아이템입니다. 부모의 Pair 뒤에 현재 아이템의 Local Pair가 추가됩니다."));
        int nextIndex = EditorGUI.Popup(
            popupRect,
            currentIndex,
            cachedParentInputOutputModuleItemOptionContents);
        ItemDefinition nextItem = nextIndex > 0 && nextIndex < cachedParentInputOutputModuleItemOptions.Length
            ? cachedParentInputOutputModuleItemOptions[nextIndex]
            : null;
        if (nextItem != null && nextItem.mapObject == currentModule)
        {
            nextItem = currentItem;
        }

        if (ItemDefinitionDragAndDropUtility.HandleDropTarget(popupRect, this, out ItemDefinition droppedItem))
        {
            nextItem = droppedItem != null
                       && droppedItem.mapObject is InputOutputModule droppedModule
                       && droppedModule != currentModule
                ? droppedItem
                : currentItem;
        }

        if (nextItem != currentItem)
        {
            parentItemProperty.objectReferenceValue = nextItem;
            currentItem = nextItem;
        }

        return currentItem;
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

    private void DrawInteractionDistanceField(MapObject mapObject)
    {
        if (!(mapObject is InstallationObject))
        {
            return;
        }

        SerializedObject serializedMapObject = GetSelectedMapObjectSerializedObject(mapObject);
        if (serializedMapObject == null)
        {
            return;
        }

        serializedMapObject.UpdateIfRequiredOrScript();

        if (mapObject is WorkableObject)
        {
            SerializedProperty workableRangeCellsProperty = serializedMapObject.FindProperty("workableRangeCells");
            if (workableRangeCellsProperty == null)
            {
                return;
            }

            uint currentRangeCells = (uint)Mathf.Max(0, workableRangeCellsProperty.intValue);
            float currentDistance = WorkableObject.ResolveRangeRadius(currentRangeCells);
            float nextDistance = Mathf.Max(
                0f,
                EditorGUILayout.FloatField(
                    new GUIContent(
                        "Interaction Distance",
                        "상호작용 아이콘 표시와 작업이 가능한 월드 거리입니다. 0.5 단위로 저장됩니다."),
                    currentDistance));
            workableRangeCellsProperty.intValue = Mathf.Max(0, Mathf.RoundToInt(nextDistance * 2f));
        }
        else
        {
            SerializedProperty focusRadiusProperty = GetMapObjectFocusRadiusProperty(serializedMapObject, mapObject);
            if (focusRadiusProperty == null)
            {
                return;
            }

            focusRadiusProperty.floatValue = Mathf.Max(0f, focusRadiusProperty.floatValue);
            EditorGUILayout.PropertyField(
                focusRadiusProperty,
                new GUIContent(
                    "Interaction Distance",
                    "상호작용 아이콘 표시와 실행이 가능한 월드 거리입니다."));
        }

        if (!serializedMapObject.ApplyModifiedProperties())
        {
            return;
        }

        EditorUtility.SetDirty(mapObject);
        if (mapObject.gameObject != null)
        {
            EditorUtility.SetDirty(mapObject.gameObject);
        }
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

        SerializedProperty decelerationProperty = FindSerializedProperty(serializedMapObject, "vehicleDecelerationPerSecond");
        if (decelerationProperty != null && entry.vehicleDecelerationPerSecond > 0f)
        {
            decelerationProperty.floatValue = Mathf.Max(0.01f, entry.vehicleDecelerationPerSecond);
        }

        SerializedProperty maxSpeedProperty = FindSerializedProperty(serializedMapObject, "vehicleMaxSpeed");
        if (maxSpeedProperty != null && entry.vehicleMaxSpeed > 0f)
        {
            maxSpeedProperty.floatValue = Mathf.Max(0.01f, entry.vehicleMaxSpeed);
        }

        SerializedProperty massProperty = FindSerializedProperty(serializedMapObject, "vehicleMass");
        if (massProperty != null && entry.vehicleMass > 0f)
        {
            massProperty.floatValue = Mathf.Max(0.01f, entry.vehicleMass);
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

    private void DrawManualTargetItemField(
        SerializedProperty targetItemProperty,
        List<ItemDefinition> definitions)
    {
        if (targetItemProperty == null)
        {
            return;
        }

        ItemDefinition currentDefinition =
            targetItemProperty.objectReferenceValue as ItemDefinition;
        ItemDefinition[] dropdownDefinitions = GetInputOutputDefinitionOptions(definitions);
        UnityEngine.Object[] editedTargets = targetItemProperty.serializedObject.targetObjects;

        Rect rowRect = EditorGUILayout.GetControlRect();
        Rect popupRect = EditorGUI.PrefixLabel(
            rowRect,
            new GUIContent(
                "Target Item",
                "이 Manual이 설명하는 대상 아이템입니다."));

        bool previousMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = targetItemProperty.hasMultipleDifferentValues;
        bool hasIcon = currentDefinition != null && currentDefinition.icon != null;
        GUIStyle popupStyle = hasIcon
            ? GetManualTargetPopupWithIconStyle()
            : EditorStyles.popup;
        string selectedLabel = currentDefinition != null
            ? $"[{currentDefinition.id}] {GetDefinitionDisplayName(currentDefinition)}"
            : "(None)";
        bool openDropdown = EditorGUI.DropdownButton(
            popupRect,
            new GUIContent(selectedLabel),
            FocusType.Keyboard,
            popupStyle);
        EditorGUI.showMixedValue = previousMixedValue;

        if (hasIcon)
        {
            const float iconSize = 16f;
            Rect iconRect = new Rect(
                popupRect.x + 3f,
                popupRect.y + (popupRect.height - iconSize) * 0.5f,
                iconSize,
                iconSize);
            DrawItemIcon(iconRect, currentDefinition);
        }

        if (openDropdown)
        {
            PopupWindow.Show(
                popupRect,
                new ManualTargetItemPopupContent(
                    dropdownDefinitions,
                    currentDefinition,
                    nextDefinition => ApplyManualTargetItemSelection(editedTargets, nextDefinition)));
        }

        if (ItemDefinitionDragAndDropUtility.HandleDropTarget(
                popupRect,
                this,
                out ItemDefinition droppedDefinition))
        {
            if (ApplyManualTargetItemSelection(editedTargets, droppedDefinition))
            {
                currentDefinition = droppedDefinition;
            }
        }

        if (currentDefinition != null && !targetItemProperty.hasMultipleDifferentValues)
        {
            DrawReferencedItemPreview(currentDefinition);
        }
    }

    private bool ApplyManualTargetItemSelection(
        UnityEngine.Object[] editedTargets,
        ItemDefinition nextDefinition)
    {
        if (editedTargets == null || editedTargets.Length == 0)
        {
            return false;
        }

        if (nextDefinition != null)
        {
            for (int i = 0; i < editedTargets.Length; i++)
            {
                if (editedTargets[i] == nextDefinition)
                {
                    ShowNotification(new GUIContent("Manual은 자기 자신을 대상으로 지정할 수 없습니다."));
                    return false;
                }
            }
        }

        SerializedObject editedDefinitions = new SerializedObject(editedTargets);
        editedDefinitions.Update();
        SerializedProperty manualTargetProperty = editedDefinitions.FindProperty(nameof(ItemDefinition.manualTargetItem));
        if (manualTargetProperty == null)
        {
            return false;
        }

        manualTargetProperty.objectReferenceValue = nextDefinition;
        editedDefinitions.ApplyModifiedProperties();
        Repaint();
        return true;
    }

    private GUIStyle GetManualTargetPopupWithIconStyle()
    {
        if (manualTargetPopupWithIconStyle == null)
        {
            manualTargetPopupWithIconStyle = new GUIStyle(EditorStyles.popup);
            manualTargetPopupWithIconStyle.padding.left = 23;
        }

        return manualTargetPopupWithIconStyle;
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

    private void EnsureParentInputOutputModuleItemOptionCache(List<ItemDefinition> definitions)
    {
        if (cachedParentInputOutputModuleItemOptionsVersion == definitionsCacheVersion)
        {
            return;
        }

        List<ItemDefinition> parentItems = new List<ItemDefinition>();
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition != null && definition.mapObject is InputOutputModule)
                {
                    parentItems.Add(definition);
                }
            }
        }

        cachedParentInputOutputModuleItemOptions = BuildInputOutputDefinitionOptions(parentItems);
        cachedParentInputOutputModuleItemOptionContents =
            BuildInputOutputDefinitionOptionContents(cachedParentInputOutputModuleItemOptions);
        BuildInputOutputDefinitionOptionIndexes(
            cachedParentInputOutputModuleItemOptions,
            cachedParentInputOutputModuleItemOptionIndexes);
        cachedParentInputOutputModuleItemOptionsVersion = definitionsCacheVersion;
    }

    private void DrawSeedTargetResourceField(SerializedProperty targetResourceProperty)
    {
        if (targetResourceProperty == null)
        {
            return;
        }

        EnsureSeedTargetResourceOptionCache();
        ResourceDefinition currentDefinition =
            targetResourceProperty.objectReferenceValue as ResourceDefinition;
        int currentIndex = currentDefinition != null
                           && cachedSeedTargetResourceOptionIndexes.TryGetValue(
                               currentDefinition.GetInstanceID(),
                               out int resolvedIndex)
            ? resolvedIndex
            : 0;

        bool previousMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = targetResourceProperty.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int nextIndex = EditorGUILayout.Popup(
            new GUIContent(
                "Target Resource",
                "씨앗을 밭에 심었을 때 생성할 ResourceDefinition입니다."),
            currentIndex,
            cachedSeedTargetResourceOptionContents);
        bool selectionChanged = EditorGUI.EndChangeCheck();
        EditorGUI.showMixedValue = previousMixedValue;

        ResourceDefinition nextDefinition =
            nextIndex > 0 && nextIndex < cachedSeedTargetResourceOptions.Length
                ? cachedSeedTargetResourceOptions[nextIndex]
                : null;
        if (selectionChanged && nextDefinition != currentDefinition)
        {
            targetResourceProperty.objectReferenceValue = nextDefinition;
            currentDefinition = nextDefinition;
        }

        if (currentDefinition != null && currentDefinition.prefab == null)
        {
            EditorGUILayout.HelpBox(
                "선택한 ResourceDefinition에 Prefab이 없어 게임에서 심을 수 없습니다.",
                MessageType.Warning);
        }
    }

    private void EnsureSeedTargetResourceOptionCache()
    {
        if (cachedSeedTargetResourceOptionsVersion == definitionsCacheVersion)
        {
            return;
        }

        string[] resourceGuids = AssetDatabase.FindAssets("t:ResourceDefinition");
        List<ResourceDefinition> definitions = new List<ResourceDefinition>(resourceGuids.Length);
        for (int i = 0; i < resourceGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(resourceGuids[i]);
            ResourceDefinition definition =
                AssetDatabase.LoadAssetAtPath<ResourceDefinition>(assetPath);
            if (definition != null)
            {
                definitions.Add(definition);
            }
        }

        definitions.Sort((left, right) => string.Compare(
            GetResourceDefinitionDisplayName(left),
            GetResourceDefinitionDisplayName(right),
            StringComparison.OrdinalIgnoreCase));

        cachedSeedTargetResourceOptions = new ResourceDefinition[definitions.Count + 1];
        cachedSeedTargetResourceOptionContents = new GUIContent[definitions.Count + 1];
        cachedSeedTargetResourceOptionContents[0] = new GUIContent("(None)");
        cachedSeedTargetResourceOptionIndexes.Clear();
        for (int i = 0; i < definitions.Count; i++)
        {
            ResourceDefinition definition = definitions[i];
            int optionIndex = i + 1;
            cachedSeedTargetResourceOptions[optionIndex] = definition;
            cachedSeedTargetResourceOptionContents[optionIndex] = new GUIContent(
                GetResourceDefinitionDisplayName(definition),
                AssetDatabase.GetAssetPath(definition));
            cachedSeedTargetResourceOptionIndexes[definition.GetInstanceID()] = optionIndex;
        }

        cachedSeedTargetResourceOptionsVersion = definitionsCacheVersion;
    }

    private static string GetResourceDefinitionDisplayName(ResourceDefinition definition)
    {
        if (definition == null)
        {
            return "(None)";
        }

        return string.IsNullOrWhiteSpace(definition.resourceName)
            ? definition.name
            : definition.resourceName.Trim();
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
        DefinitionCatalog.NotifyChanged();
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
        DefinitionCatalog.NotifyChanged();
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

        Exception rebuildException = null;
        int productionMachineRecipeCount = 0;
        try
        {
            DisplayItemRebuildProgress("리빌드 준비 중...", 0.01f);
            Undo.RecordObject(itemManager, "Rebuild Item Data");
            itemManager.RebuildItemDefinitionsFromAssets((message, progress) =>
                DisplayItemRebuildProgress(message, Mathf.Lerp(0.02f, 0.72f, progress)));
            itemManager.ApplyItemIdsToPrefabs((message, progress) =>
                DisplayItemRebuildProgress(message, Mathf.Lerp(0.72f, 0.88f, progress)));

            DisplayItemRebuildProgress("생산 기계 레시피 동기화 중...", 0.9f);
            productionMachineRecipeCount = ProductionMachineRecipeAutoFill.SyncProductionMachines(itemManager);
            itemManager.MarkEditorDirty();

            DisplayItemRebuildProgress("변경된 에셋 저장 중...", 0.94f);
            AssetDatabase.SaveAssets();
            DisplayItemRebuildProgress("에셋 데이터베이스 새로고침 중...", 0.97f);
            AssetDatabase.Refresh();

            DisplayItemRebuildProgress("아이템 UI 갱신 중...", 0.99f);
            InvalidateDefinitionCache();
            EnsureSelection(GetDefinitions(itemManager));
            DefinitionCatalog.NotifyChanged();
            DisplayItemRebuildProgress("아이템 리빌드 완료", 1f);
        }
        catch (Exception exception)
        {
            rebuildException = exception;
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }

        if (rebuildException != null)
        {
            EditorUtility.DisplayDialog(
                "Item Data",
                $"아이템 리빌드 중 오류가 발생했습니다.\n{rebuildException.Message}",
                "OK");
            return;
        }

        ShowNotification(new GUIContent($"Item Data rebuilt. Production recipes: {productionMachineRecipeCount}"));
    }

    private void RebuildItemData(ItemDefinition definition)
    {
        ItemManager itemManager = FindItemManager();
        if (itemManager == null)
        {
            EditorUtility.DisplayDialog("Item Data", "씬에서 ItemManager를 찾을 수 없습니다.", "OK");
            EnsureSelection();
            Repaint();
            return;
        }

        if (definition == null)
        {
            EditorUtility.DisplayDialog("Item Data", "리빌드할 아이템을 선택하세요.", "OK");
            return;
        }

        string displayName = GetDefinitionDisplayName(definition);
        string errorMessage = string.Empty;
        Exception rebuildException = null;
        bool rebuilt = false;
        try
        {
            DisplayItemRebuildProgress($"[{definition.id}] {displayName} 리빌드 준비 중...", 0.05f);
            Undo.RecordObjects(
                new UnityEngine.Object[] { itemManager, definition },
                "Rebuild Selected Item Data");
            DisplayItemRebuildProgress($"[{definition.id}] {displayName} 에셋 연결 중...", 0.2f);
            rebuilt = itemManager.RebuildItemDefinitionFromAssets(definition, out errorMessage);
            if (rebuilt)
            {
                DisplayItemRebuildProgress("변경된 에셋 저장 중...", 0.7f);
                AssetDatabase.SaveAssets();
                DisplayItemRebuildProgress("에셋 데이터베이스 새로고침 중...", 0.85f);
                AssetDatabase.Refresh();
                DisplayItemRebuildProgress("아이템 UI 갱신 중...", 0.96f);
                InvalidateDefinitionCache();
                selectedItemId = definition.id;
                EnsureSelection(GetDefinitions(itemManager));
                DefinitionCatalog.NotifyChanged();
                DisplayItemRebuildProgress("선택 아이템 리빌드 완료", 1f);
            }
        }
        catch (Exception exception)
        {
            rebuildException = exception;
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }

        if (rebuildException != null)
        {
            EditorUtility.DisplayDialog(
                "Item Data",
                $"선택 아이템 리빌드 중 오류가 발생했습니다.\n{rebuildException.Message}",
                "OK");
            return;
        }

        if (!rebuilt)
        {
            EditorUtility.DisplayDialog("Item Data", errorMessage, "OK");
            return;
        }

        ShowNotification(new GUIContent($"[{definition.id}] {displayName} rebuilt."));
    }

    private delegate bool TryCreateDocumentAssets(
        ItemDefinition selectedDefinition,
        IReadOnlyList<ItemDefinition> definitions,
        out BookItemAssetGenerator.Result result,
        out string errorMessage);

    private void CreateBookIcon(ItemDefinition definition)
    {
        CreateDocumentIcon(definition, "Book", BookItemAssetGenerator.TryCreate);
    }

    private void CreatePaperIcon(ItemDefinition definition)
    {
        CreateDocumentIcon(definition, "Paper", PaperItemAssetGenerator.TryCreate);
    }

    private void CreateCdIcon(ItemDefinition definition)
    {
        CreateDocumentIcon(definition, "CD", CdItemAssetGenerator.TryCreate);
    }

    private void CreateDocumentIcon(
        ItemDefinition definition,
        string documentType,
        TryCreateDocumentAssets tryCreateAssets)
    {
        string dialogTitle = $"Create {documentType} Icon";
        if (definition == null)
        {
            EditorUtility.DisplayDialog("Item Data", $"{documentType} 에셋을 생성할 아이템을 선택하세요.", "OK");
            return;
        }

        ItemManager itemManager = FindItemManager();
        List<ItemDefinition> definitions = itemManager != null
            ? GetDefinitions(itemManager)
            : DefinitionCatalog.LoadCurrent();
        BookItemAssetGenerator.Result result = null;
        string errorMessage = string.Empty;
        bool created = false;
        try
        {
            EditorUtility.DisplayProgressBar(
                dialogTitle,
                $"원본 아이템과 {documentType} 템플릿 확인 중...",
                0.1f);
            created = tryCreateAssets(
                definition,
                definitions,
                out result,
                out errorMessage);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (!created || result == null)
        {
            EditorUtility.DisplayDialog(
                dialogTitle,
                string.IsNullOrWhiteSpace(errorMessage)
                    ? $"{documentType} 에셋을 생성하지 못했습니다."
                    : errorMessage,
                "OK");
            return;
        }

        AssetDatabase.SaveAssets();
        InvalidateDefinitionCache();
        ItemDefinition selectionDefinition = result.TargetDefinition != null
            ? result.TargetDefinition
            : definition;
        selectedItemId = selectionDefinition.id;
        EnsureSelection(itemManager != null ? GetDefinitions(itemManager) : DefinitionCatalog.LoadCurrent());
        DefinitionCatalog.NotifyChanged();
        Selection.activeObject = result.Icon;
        EditorGUIUtility.PingObject(result.Icon);
        string targetStatus = result.TargetDefinition != null
            ? $"{documentType} item '{GetDefinitionDisplayName(result.TargetDefinition)}' updated"
            : $"{documentType} assets '{result.TargetItemName}' created (Rebuild Item Data to register)";
        ShowNotification(new GUIContent(
            $"{targetStatus} from {GetDefinitionDisplayName(result.SourceDefinition)}."));
        Repaint();
    }

    private static void DisplayItemRebuildProgress(string message, float progress)
    {
        EditorUtility.DisplayProgressBar(
            ItemRebuildProgressTitle,
            message,
            Mathf.Clamp01(progress));
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
        List<UnityEngine.Object> iconPackables = CollectUiIconPackables(definitions);
        if (iconPackables.Count == 0)
        {
            EditorUtility.DisplayDialog("Item Data", "Atlas에 넣을 UI 아이콘 Packable이 없습니다.", "OK");
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

        SyncSpriteAtlasPackables(atlas, iconPackables);
        ApplyUiIconAtlasSettings(atlas);

        EditorUtility.SetDirty(atlas);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(UiIconAtlasPath, ImportAssetOptions.ForceUpdate);
        SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = atlas;
        EditorGUIUtility.PingObject(atlas);
        ShowNotification(new GUIContent($"{(created ? "Created" : "Updated")} UI Icon Atlas ({iconPackables.Count} packables)"));
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

    private static List<UnityEngine.Object> CollectUiIconPackables(List<ItemDefinition> definitions)
    {
        List<UnityEngine.Object> packables = new List<UnityEngine.Object>();
        HashSet<UnityEngine.Object> visitedPackables = new HashSet<UnityEngine.Object>();
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                AddUiIconSprite(definition.icon, packables, visitedPackables);

                List<Sprite> interactionSprites = definition.interactionButtonList;
                if (interactionSprites == null)
                {
                    continue;
                }

                for (int spriteIndex = 0; spriteIndex < interactionSprites.Count; spriteIndex++)
                {
                    AddUiIconSprite(interactionSprites[spriteIndex], packables, visitedPackables);
                }
            }
        }

        AddUiIconFolderPackable(UiIconAtlasFolder, packables, visitedPackables);
        AddUiIconFolderPackable(ResourceUiIconFolder, packables, visitedPackables);
        return packables;
    }

    private static void AddUiIconSprite(
        Sprite sprite,
        List<UnityEngine.Object> packables,
        HashSet<UnityEngine.Object> visitedPackables)
    {
        if (sprite == null || packables == null || visitedPackables == null)
        {
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(sprite);
        if (string.IsNullOrWhiteSpace(assetPath) || IsCoveredByUiIconFolder(assetPath))
        {
            return;
        }

        if (visitedPackables.Add(sprite))
        {
            packables.Add(sprite);
        }
    }

    private static void AddUiIconFolderPackable(
        string folderPath,
        List<UnityEngine.Object> packables,
        HashSet<UnityEngine.Object> visitedPackables)
    {
        if (packables == null
            || visitedPackables == null
            || string.IsNullOrWhiteSpace(folderPath)
            || !AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        DefaultAsset folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
        if (folder != null && visitedPackables.Add(folder))
        {
            packables.Add(folder);
        }
    }

    private static bool IsCoveredByUiIconFolder(string assetPath)
    {
        return IsAssetInsideFolder(assetPath, UiIconAtlasFolder)
            || IsAssetInsideFolder(assetPath, ResourceUiIconFolder);
    }

    private static bool IsAssetInsideFolder(string assetPath, string folderPath)
    {
        return !string.IsNullOrWhiteSpace(assetPath)
            && !string.IsNullOrWhiteSpace(folderPath)
            && assetPath.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static void SyncSpriteAtlasPackables(SpriteAtlas atlas, List<UnityEngine.Object> packables)
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

        if (packables != null && packables.Count > 0)
        {
            SpriteAtlasExtensions.Add(atlas, packables.ToArray());
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

    private sealed class ItemDefinitionRenameEntry
    {
        public ItemDefinition definition;
        public string finalPath;
    }

    private static List<ItemDefinition> LoadItemDefinitionsFromAssets()
    {
        List<ItemDefinition> definitions = new List<ItemDefinition>();
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemDefinitionAssetFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
            if (definition != null)
            {
                definitions.Add(definition);
            }
        }

        SortDefinitionsById(definitions);
        return definitions;
    }

    private static List<ItemDefinition> BuildCompactItemDefinitionOrder(List<ItemDefinition> definitions)
    {
        List<ItemDefinition> orderedDefinitions = new List<ItemDefinition>();
        HashSet<ItemDefinition> addedDefinitions = new HashSet<ItemDefinition>();

        for (int id = 0; id <= 40; id++)
        {
            AddDefinitionIfFound(orderedDefinitions, addedDefinitions, FindDefinitionById(definitions, id));
        }

        AddDefinitionIfFound(orderedDefinitions, addedDefinitions, FindDefinitionByGuid(TrainStationItemGuid));
        for (int i = 0; i < CompactTrainItemGuids.Length; i++)
        {
            AddDefinitionIfFound(orderedDefinitions, addedDefinitions, FindDefinitionByGuid(CompactTrainItemGuids[i]));
        }

        return orderedDefinitions;
    }

    private static void RenameItemDefinitionAssets(List<ItemDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0)
        {
            return;
        }

        List<ItemDefinitionRenameEntry> renameEntries = new List<ItemDefinitionRenameEntry>();
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            string currentPath = AssetDatabase.GetAssetPath(definition);
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                continue;
            }

            string finalPath = GetExpectedItemDefinitionAssetPath(definition);
            string finalName = Path.GetFileNameWithoutExtension(finalPath);
            definition.name = finalName;
            EditorUtility.SetDirty(definition);
            if (!string.Equals(currentPath, finalPath, StringComparison.OrdinalIgnoreCase))
            {
                renameEntries.Add(new ItemDefinitionRenameEntry
                {
                    definition = definition,
                    finalPath = finalPath
                });
            }
        }

        for (int i = 0; i < renameEntries.Count; i++)
        {
            ItemDefinition definition = renameEntries[i].definition;
            string currentPath = AssetDatabase.GetAssetPath(definition);
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                continue;
            }

            string tempName = $"__ItemIdNormalize_{AssetDatabase.AssetPathToGUID(currentPath)}";
            string error = AssetDatabase.RenameAsset(currentPath, tempName);
            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning($"ItemDataEditorWindow: failed temp rename for '{currentPath}'. {error}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        for (int i = 0; i < renameEntries.Count; i++)
        {
            ItemDefinition definition = renameEntries[i].definition;
            if (definition == null)
            {
                continue;
            }

            string currentPath = AssetDatabase.GetAssetPath(definition);
            string finalName = Path.GetFileNameWithoutExtension(renameEntries[i].finalPath);
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                continue;
            }

            string error = AssetDatabase.RenameAsset(currentPath, finalName);
            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning($"ItemDataEditorWindow: failed final rename for '{currentPath}'. {error}");
            }

            definition.name = finalName;
            EditorUtility.SetDirty(definition);
        }
    }

    private static void WriteItemDataJson(string path, List<ItemDefinition> definitions)
    {
        ItemDataJsonFile file = new ItemDataJsonFile();
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition != null)
                {
                    file.items.Add(BuildJsonEntry(definition));
                }
            }
        }

        EnsureParentFolder(path);
        File.WriteAllText(path, JsonUtility.ToJson(file, true));
    }

    private static void AddDefinitionIfFound(
        List<ItemDefinition> definitions,
        HashSet<ItemDefinition> addedDefinitions,
        ItemDefinition definition)
    {
        if (definition != null && addedDefinitions.Add(definition))
        {
            definitions.Add(definition);
        }
    }

    private static ItemDefinition FindDefinitionByGuid(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
        {
            return null;
        }

        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrWhiteSpace(assetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
    }

    private static string GetExpectedItemDefinitionAssetPath(ItemDefinition definition)
    {
        string displayName = GetDefinitionDisplayName(definition);
        string safeName = SanitizeAssetFileName(string.IsNullOrWhiteSpace(displayName)
            ? $"Item_{definition.id}"
            : displayName);
        return $"{ItemDefinitionAssetFolder}/Item_{definition.id}_{safeName}.asset";
    }

    private static string SanitizeAssetFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Item";
        }

        string sanitized = value.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
        {
            sanitized = sanitized.Replace(invalidChars[i].ToString(), string.Empty);
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "Item" : sanitized;
    }

    private static string GetDefaultItemDataJsonPath()
    {
        return Path.Combine(Application.dataPath, "Data", "Items", "item_data.json");
    }

    private static void EnsureParentFolder(string path)
    {
        string folderPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folderPath) && !Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
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

        WriteItemDataJson(exportPath, definitions);
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
            itemManager.MarkEditorDirty();
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
            lightMode = definition.lightMode.ToString(),
            lightModeValue = (int)definition.lightMode,
            lightRange = definition.LightRange,
            lightIntensityMultiplier = definition.LightIntensityMultiplier,
            size = Mathf.Max(0, (int)definition.size),
            itemFilter = definition.itemFilter,
            hasIgnoreFilter = true,
            ignoreFilter = definition.ignoreFilter,
            hasOneItem = true,
            oneItem = definition.oneItem,
            hasManual = true,
            isManual = definition.isManual,
            manualTargetItem = definition.isManual
                ? BuildDefinitionReferenceJsonEntry(definition.manualTargetItem)
                : null,
            hasUpgradeable = true,
            upgradeable = definition.upgradeable,
            capacity = definition.capacity > 0 ? definition.capacity : 10,
            storesFluid = definition.storesFluid,
            fluidStorageLiters = definition.storesFluid ? Mathf.Max(0f, definition.fluidStorageLiters) : 0f,
            fluidOutputLitersPerSecond = IsFluidOutputMachine(definition)
                ? definition.FluidOutputLitersPerSecond
                : -1f,
            undergroundPipeMaxDistance = definition.mapObject is UndergroundPipe
                ? definition.UndergroundPipeMaxDistance
                : -1,
            hasFluidDisplayColor = InputOutputModule.IsFluidItemDefinition(definition),
            fluidDisplayColor = definition.fluidDisplayColor,
            bucketFillDurationSeconds = Bucket.IsEmptyBucketDefinition(definition)
                ? definition.BucketFillDurationSeconds
                : -1f,
            craftingDurationSeconds = definition.CraftingDurationSeconds,
            energyType = definition.energyType.ToString(),
            energyTypeValue = (int)definition.energyType,
            energyAmount = Mathf.Max(0, definition.energyAmount),
            hasEatReward = true,
            eatRewardItem = BuildDefinitionReferenceJsonEntry(definition.eatRewardItem),
            eatRewardChancePercent = Mathf.Clamp(definition.eatRewardChancePercent, 0f, 100f),
            hasSeedSettings = true,
            isSeed = definition.isSeed,
            seedTargetResourceAssetPath = definition.isSeed && definition.seedTargetResource != null
                ? AssetDatabase.GetAssetPath(definition.seedTargetResource)
                : string.Empty,
            useEnergyType = definition.useEnergyType.ToString(),
            useEnergyTypeValue = (int)definition.useEnergyType,
            useEnergyAmount = Mathf.Max(0f, definition.useEnergyAmount),
            completeEnergy = Mathf.Max(0f, definition.completeEnergy),
            utilityPoleConnectionRadius = definition.mapObject is UtilityPole
                ? Mathf.Max(0, definition.utilityPoleConnectionRadius)
                : -1,
            utilityPoleSupplyRadius = definition.mapObject is UtilityPole
                ? Mathf.Max(0, definition.utilityPoleSupplyRadius)
                : -1,
            sprinklerRangeRadius = definition.mapObject is Sprinkler
                ? Mathf.Max(0, definition.sprinklerRangeRadius)
                : -1,
            sprinklerWaterLitersPerCell = definition.mapObject is Sprinkler
                ? Mathf.Max(0.001f, definition.sprinklerWaterLitersPerCell)
                : -1f,
            sprinklerSprayIntervalSeconds = definition.mapObject is Sprinkler
                ? Mathf.Max(0.1f, definition.sprinklerSprayIntervalSeconds)
                : -1f,
            sprinklerNozzleRotationDegreesPerSecond = definition.mapObject is Sprinkler
                ? Mathf.Max(0f, definition.sprinklerNozzleRotationDegreesPerSecond)
                : -1f,
            seedPlanterPlantDurationSeconds = definition.mapObject is SeedPlanter
                ? SeedPlanter.ResolvePlantDuration(definition)
                : -1f
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

            if (ShouldExposeVehicleStats(definition.mapObject) && definition.mapObject is Vehicle vehicle)
            {
                entry.vehicleAccelerationPerSecond = vehicle.VehicleAccelerationPerSecond;
                entry.vehicleDecelerationPerSecond = vehicle.VehicleDecelerationPerSecond;
                entry.vehicleMaxSpeed = vehicle.VehicleMaxSpeed;
            }

            if (definition.mapObject is Vehicle massVehicle)
            {
                entry.vehicleMass = massVehicle.VehicleMass;
            }

            if (definition.mapObject is InstallationObject installationObject)
            {
                entry.mapFilter = installationObject.MapFilter.ToString();
                entry.mapFilterValue = (int)installationObject.MapFilter;
                entry.rotationFilter = installationObject.RotationFilter.ToString();
                entry.rotationFilterValue = (int)installationObject.RotationFilter;
            }

            if (definition.mapObject is InputOutputModule inputOutputModule)
            {
                entry.inputOutputLayoutType = inputOutputModule.LayoutType.ToString();
                entry.parentInputOutputModuleItem = BuildDefinitionReferenceJsonEntry(
                    inputOutputModule.ParentInputOutputModuleItem);
                entry.rectGridWidth = inputOutputModule.RectGridWidth;
                entry.rectGridHeight = inputOutputModule.RectGridHeight;

                IReadOnlyList<InputOutputModule.RectGridBlockPlacement> rectGridPlacements = inputOutputModule.RectGridPlacements;
                for (int i = 0; i < rectGridPlacements.Count; i++)
                {
                    entry.rectGridBlocks.Add(BuildRectGridBlockPlacementJsonEntry(rectGridPlacements[i]));
                }

                IReadOnlyList<InputOutputModule.ItemIoEntry> inputs = inputOutputModule.LocalInputList;
                IReadOnlyList<InputOutputModule.ItemIoEntry> outputs = inputOutputModule.LocalOutputList;
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
        InputOutputJsonEntry jsonEntry = BuildDefinitionReferenceJsonEntry(entry.itemDefinition)
                                         ?? new InputOutputJsonEntry();
        jsonEntry.count = Mathf.Max(1, entry.count);
        return jsonEntry;
    }

    private static InputOutputJsonEntry BuildDefinitionReferenceJsonEntry(ItemDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        return new InputOutputJsonEntry
        {
            id = definition.id,
            itemName = definition.itemName,
            definitionAssetPath = AssetDatabase.GetAssetPath(definition),
            count = 1
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
        definition.lightMode = ParseItemLightMode(
            entry.lightMode,
            entry.lightModeValue,
            definition.lightMode);
        if (entry.lightRange > 0f)
        {
            definition.lightRange = Mathf.Max(0.1f, entry.lightRange);
        }
        if (entry.lightIntensityMultiplier > 0f)
        {
            definition.lightIntensityMultiplier = Mathf.Max(0.01f, entry.lightIntensityMultiplier);
        }
        definition.itemFilter = entry.itemFilter;
        if (entry.hasIgnoreFilter)
        {
            definition.ignoreFilter = entry.ignoreFilter;
        }
        if (entry.hasOneItem)
        {
            definition.oneItem = entry.oneItem;
        }
        if (entry.hasManual)
        {
            definition.isManual = entry.isManual;
            definition.manualTargetItem = entry.isManual
                ? ResolveDefinitionReference(definitions, entry.manualTargetItem)
                : null;
        }
        if (entry.hasUpgradeable)
        {
            definition.upgradeable = entry.upgradeable;
        }
        if (entry.capacity > 0)
        {
            definition.capacity = Mathf.Max(1, entry.capacity);
        }
        definition.storesFluid = entry.storesFluid;
        definition.fluidStorageLiters = entry.storesFluid ? Mathf.Max(0f, entry.fluidStorageLiters) : 0f;
        if (entry.fluidOutputLitersPerSecond >= 0f)
        {
            definition.fluidOutputLitersPerSecond = Mathf.Max(0f, entry.fluidOutputLitersPerSecond);
        }
        if (entry.undergroundPipeMaxDistance >= 2)
        {
            definition.undergroundPipeMaxDistance = Mathf.Max(2, entry.undergroundPipeMaxDistance);
        }
        if (entry.hasFluidDisplayColor)
        {
            definition.fluidDisplayColor = entry.fluidDisplayColor;
        }
        if (entry.bucketFillDurationSeconds > 0f)
        {
            definition.bucketFillDurationSeconds = Mathf.Max(0.1f, entry.bucketFillDurationSeconds);
        }
        if (entry.craftingDurationSeconds > 0f)
        {
            definition.SetCraftingDurationSeconds(entry.craftingDurationSeconds);
        }
        definition.energyType = ParseEnergyType(entry.energyType, entry.energyTypeValue, definition.energyType);
        definition.energyAmount = definition.energyType == ItemDefinition.EnergyType.None ? 0 : Mathf.Max(0, entry.energyAmount);
        if (entry.hasEatReward)
        {
            definition.eatRewardItem = ResolveDefinitionReference(definitions, entry.eatRewardItem);
            definition.eatRewardChancePercent = Mathf.Clamp(entry.eatRewardChancePercent, 0f, 100f);
        }
        if (entry.hasSeedSettings)
        {
            definition.isSeed = entry.isSeed;
            definition.seedTargetResource = entry.isSeed
                ? LoadAssetAtPath<ResourceDefinition>(entry.seedTargetResourceAssetPath)
                : null;
        }
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

        if (entry.sprinklerRangeRadius >= 0)
        {
            definition.sprinklerRangeRadius = Mathf.Max(0, entry.sprinklerRangeRadius);
        }

        if (entry.sprinklerWaterLitersPerCell > 0f)
        {
            definition.sprinklerWaterLitersPerCell = Mathf.Max(0.001f, entry.sprinklerWaterLitersPerCell);
        }

        if (entry.sprinklerSprayIntervalSeconds > 0f)
        {
            definition.sprinklerSprayIntervalSeconds = Mathf.Max(0.1f, entry.sprinklerSprayIntervalSeconds);
        }

        if (entry.sprinklerNozzleRotationDegreesPerSecond >= 0f)
        {
            definition.sprinklerNozzleRotationDegreesPerSecond = Mathf.Max(
                0f,
                entry.sprinklerNozzleRotationDegreesPerSecond);
        }

        if (entry.seedPlanterPlantDurationSeconds > 0f)
        {
            definition.seedPlanterPlantDurationSeconds = Mathf.Max(
                0.1f,
                entry.seedPlanterPlantDurationSeconds);
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
        if (EditorApplication.isPlaying)
        {
            ItemLightController.RefreshDefinition(definition);
        }
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
                    mapFilterProperty.intValue = (int)InstallationObject.NormalizeMapFilter(parsedFilterValue);
                }
            }

            SerializedProperty rotationFilterProperty = serializedMapObject.FindProperty("rotationFilter");
            if (rotationFilterProperty != null)
            {
                if (!string.IsNullOrWhiteSpace(entry.rotationFilter)
                    && Enum.TryParse(
                        entry.rotationFilter,
                        true,
                        out InstallationRotationFilter parsedRotationFilter))
                {
                    rotationFilterProperty.intValue = (int)InstallationObject.NormalizeRotationFilter(
                        parsedRotationFilter);
                }
                else if (entry.rotationFilterValue >= 0)
                {
                    rotationFilterProperty.intValue = (int)InstallationObject.NormalizeRotationFilter(
                        (InstallationRotationFilter)entry.rotationFilterValue);
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

        if (mapObject is Vehicle)
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
        SerializedProperty parentItemProperty = serializedMapObject.FindProperty("parentInputOutputModuleItem");
        SerializedProperty rectGridWidthProperty = serializedMapObject.FindProperty("rectGridWidth");
        SerializedProperty rectGridHeightProperty = serializedMapObject.FindProperty("rectGridHeight");
        if (slotLayoutTypeProperty != null && !string.IsNullOrWhiteSpace(entry.inputOutputLayoutType)
            && Enum.TryParse(entry.inputOutputLayoutType, true, out InputOutputModule.SlotLayoutType parsedLayoutType))
        {
            slotLayoutTypeProperty.enumValueIndex = (int)parsedLayoutType;
        }

        if (parentItemProperty != null)
        {
            ItemDefinition parentItem = ResolveDefinitionReference(
                definitions,
                entry.parentInputOutputModuleItem);
            parentItemProperty.objectReferenceValue = parentItem != null
                                                      && parentItem.mapObject is InputOutputModule
                ? parentItem
                : null;
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

    private void EnsureMultiSelection(List<ItemDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0)
        {
            selectedItemDefinitions.Clear();
            selectedItemDefinitionsInOrder.Clear();
            rangeSelectionAnchor = null;
            ClearPendingPlainSelection();
            return;
        }

        availableItemDefinitions.Clear();
        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i] != null)
            {
                availableItemDefinitions.Add(definitions[i]);
            }
        }

        invalidSelectedItemDefinitions.Clear();
        foreach (ItemDefinition definition in selectedItemDefinitions)
        {
            if (definition == null || !availableItemDefinitions.Contains(definition))
            {
                invalidSelectedItemDefinitions.Add(definition);
            }
        }

        for (int i = 0; i < invalidSelectedItemDefinitions.Count; i++)
        {
            selectedItemDefinitions.Remove(invalidSelectedItemDefinitions[i]);
        }

        invalidSelectedItemDefinitions.Clear();
        ItemDefinition activeDefinition = FindDefinitionById(definitions, selectedItemId);
        if (selectedItemDefinitions.Count == 0 && activeDefinition != null)
        {
            selectedItemDefinitions.Add(activeDefinition);
        }
        else if (activeDefinition != null && !selectedItemDefinitions.Contains(activeDefinition))
        {
            selectedItemDefinitions.Add(activeDefinition);
        }

        if (rangeSelectionAnchor == null || !availableItemDefinitions.Contains(rangeSelectionAnchor))
        {
            rangeSelectionAnchor = activeDefinition;
        }

        RebuildSelectedItemOrder(definitions);
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

        DefinitionCatalog.Fill(cachedDefinitions, itemManager);
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
            if (definition == null)
            {
                continue;
            }

            missingDefinitions.Add(definition);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                knownAssetPaths.Add(assetPath);
            }
        }

        SortDefinitionsById(missingDefinitions);
        definitions.AddRange(missingDefinitions);
    }

    private static ItemDefinition.ItemLightMode ParseItemLightMode(
        string rawValue,
        int rawEnumValue,
        ItemDefinition.ItemLightMode fallback)
    {
        if (!string.IsNullOrWhiteSpace(rawValue)
            && Enum.TryParse(
                rawValue,
                true,
                out ItemDefinition.ItemLightMode parsedMode))
        {
            return parsedMode;
        }

        return rawEnumValue >= 0
               && Enum.IsDefined(typeof(ItemDefinition.ItemLightMode), rawEnumValue)
            ? (ItemDefinition.ItemLightMode)rawEnumValue
            : fallback;
    }

    private static void AppendUniqueDefinition(List<ItemDefinition> definitions, ItemDefinition definition)
    {
        if (definitions == null || definition == null || ContainsDefinitionIdentity(definitions, definition))
        {
            return;
        }

        definitions.Add(definition);
    }

    private static bool ContainsDefinitionIdentity(List<ItemDefinition> definitions, ItemDefinition definition)
    {
        if (definitions == null || definition == null)
        {
            return false;
        }

        string assetPath = AssetDatabase.GetAssetPath(definition);
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition existingDefinition = definitions[i];
            if (existingDefinition == null)
            {
                continue;
            }

            if (existingDefinition == definition)
            {
                return true;
            }

            string existingAssetPath = AssetDatabase.GetAssetPath(existingDefinition);
            if (!string.IsNullOrWhiteSpace(assetPath)
                && !string.IsNullOrWhiteSpace(existingAssetPath)
                && string.Equals(assetPath, existingAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

        TerrainGenerator terrain = FindAnyObjectByType<TerrainGenerator>();
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

    internal static void DrawItemIcon(Rect rect, ItemDefinition definition)
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
        GUI.DrawTextureWithTexCoords(GetAspectFitRect(rect, texture, textureCoords), texture, textureCoords);
        GUI.color = previousColor;
    }

    private static Rect GetAspectFitRect(Rect targetRect, Texture texture, Rect textureCoords)
    {
        if (texture == null || targetRect.width <= 0f || targetRect.height <= 0f)
        {
            return targetRect;
        }

        float sourceWidth = Mathf.Max(1f, textureCoords.width * texture.width);
        float sourceHeight = Mathf.Max(1f, textureCoords.height * texture.height);
        float sourceAspect = sourceWidth / sourceHeight;
        float targetAspect = targetRect.width / targetRect.height;

        if (sourceAspect > targetAspect)
        {
            float height = targetRect.width / sourceAspect;
            return new Rect(targetRect.x, targetRect.y + (targetRect.height - height) * 0.5f, targetRect.width, height);
        }

        float width = targetRect.height * sourceAspect;
        return new Rect(targetRect.x + (targetRect.width - width) * 0.5f, targetRect.y, width, targetRect.height);
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
