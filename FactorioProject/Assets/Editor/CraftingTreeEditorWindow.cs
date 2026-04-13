using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class CraftingTreeEditorWindow : EditorWindow
{
    private const float SidebarWidth = 260f;
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private int selectedItemId = -1;
    private readonly Dictionary<int, List<IngredientEntry>> recipeByItemId = new Dictionary<int, List<IngredientEntry>>();

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
            if (GUILayout.Toggle(isSelected, $"[{definition.id}] {displayName}", "Button"))
            {
                selectedItemId = definition.id;
            }
        }
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
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
        string[] definitionNames = BuildDefinitionNames(definitions);

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
            int newIndex = EditorGUILayout.Popup(currentIndex, definitionNames);
            int newItemId = definitions.Count > 0 ? definitions[newIndex].id : entry.itemId;
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

    private List<IngredientEntry> GetOrCreateRecipe(int itemId)
    {
        if (!recipeByItemId.TryGetValue(itemId, out List<IngredientEntry> recipe))
        {
            recipe = new List<IngredientEntry>();
            recipeByItemId[itemId] = recipe;
        }

        return recipe;
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
        if (definition == null || definition.icon == null)
        {
            return;
        }

        Texture icon = definition.icon.texture;
        if (icon != null)
        {
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
        }
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
