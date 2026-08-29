using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal sealed class ItemDefinitionDropdownGUI
{
    private sealed class PopupContent : PopupWindowContent
    {
        private const float PopupWidth = 340f;
        private const float MaximumPopupHeight = 420f;
        private const float RowHeight = 24f;
        private const float IconSize = 18f;

        private readonly GUIContent[] options;
        private readonly IReadOnlyList<ItemDefinition> definitions;
        private readonly int selectedIndex;
        private readonly Action<int> selectionCallback;
        private Vector2 scrollPosition;

        public PopupContent(
            GUIContent[] options,
            IReadOnlyList<ItemDefinition> definitions,
            int selectedIndex,
            Action<int> selectionCallback)
        {
            this.options = options;
            this.definitions = definitions;
            this.selectedIndex = selectedIndex;
            this.selectionCallback = selectionCallback;
        }

        public override Vector2 GetWindowSize()
        {
            float contentHeight = options.Length * RowHeight + 4f;
            return new Vector2(PopupWidth, Mathf.Min(MaximumPopupHeight, contentHeight));
        }

        public override void OnGUI(Rect rect)
        {
            Event currentEvent = Event.current;
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < options.Length; i++)
            {
                Rect rowRect = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
                bool isSelected = i == selectedIndex;
                bool isHovered = rowRect.Contains(currentEvent.mousePosition);
                if (currentEvent.type == EventType.Repaint && (isSelected || isHovered))
                {
                    EditorGUI.DrawRect(
                        rowRect,
                        isSelected
                            ? new Color(0.24f, 0.49f, 0.90f, 0.45f)
                            : new Color(0.5f, 0.5f, 0.5f, 0.20f));
                }

                Sprite icon = ResolveIcon(i);
                float textOffset = 7f;
                if (icon != null)
                {
                    Rect iconRect = new Rect(
                        rowRect.x + 5f,
                        rowRect.y + (rowRect.height - IconSize) * 0.5f,
                        IconSize,
                        IconSize);
                    ProjectFEditorGUIUtility.DrawSprite(iconRect, icon);
                    textOffset = IconSize + 10f;
                }

                GUI.Label(
                    new Rect(
                        rowRect.x + textOffset,
                        rowRect.y,
                        rowRect.width - textOffset - 4f,
                        rowRect.height),
                    options[i].text);

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

        private Sprite ResolveIcon(int optionIndex)
        {
            return optionIndex > 0 && optionIndex <= definitions.Count
                ? definitions[optionIndex - 1]?.icon
                : null;
        }
    }

    private readonly List<ItemDefinition> definitions = new List<ItemDefinition>();
    private readonly Dictionary<ItemDefinition, int> optionIndices =
        new Dictionary<ItemDefinition, int>();
    private GUIContent[] options = { new GUIContent("None") };
    private GUIStyle popupWithIconStyle;
    private int catalogSignature = int.MinValue;

    public bool Refresh(bool force = true)
    {
        List<ItemDefinition> latest = ItemDataEditorWindow.DefinitionCatalog.LoadCurrent();
        int signature = ItemDataEditorWindow.DefinitionCatalog.ComputeSignature(latest);
        if (!force && signature == catalogSignature)
        {
            return false;
        }

        catalogSignature = signature;
        definitions.Clear();
        definitions.AddRange(latest);
        optionIndices.Clear();
        options = new GUIContent[definitions.Count + 1];
        options[0] = new GUIContent("None");
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                options[i + 1] = new GUIContent("(Missing ItemDefinition)");
                continue;
            }

            string itemName = !string.IsNullOrWhiteSpace(definition.itemName)
                ? definition.itemName
                : definition.name;
            options[i + 1] = new GUIContent($"[{definition.id}] {itemName}");
            optionIndices[definition] = i + 1;
        }

        return true;
    }

    public void Draw(
        string label,
        ItemDefinition selectedDefinition,
        Action<ItemDefinition> selectionCallback)
    {
        if (catalogSignature == int.MinValue)
        {
            Refresh();
        }

        Rect rowRect = EditorGUILayout.GetControlRect();
        Rect popupRect = EditorGUI.PrefixLabel(rowRect, new GUIContent(label));
        int selectedIndex = selectedDefinition != null
            && optionIndices.TryGetValue(selectedDefinition, out int resolvedIndex)
                ? resolvedIndex
                : 0;
        GUIContent selectedContent = selectedIndex >= 0 && selectedIndex < options.Length
            ? options[selectedIndex]
            : options[0];
        Sprite selectedIcon = selectedIndex > 0 && selectedIndex <= definitions.Count
            ? definitions[selectedIndex - 1]?.icon
            : null;
        bool open = EditorGUI.DropdownButton(
            popupRect,
            selectedContent,
            FocusType.Keyboard,
            selectedIcon != null ? GetPopupWithIconStyle() : EditorStyles.popup);

        if (selectedIcon != null)
        {
            const float iconSize = 16f;
            ProjectFEditorGUIUtility.DrawSprite(
                new Rect(
                    popupRect.x + 3f,
                    popupRect.y + (popupRect.height - iconSize) * 0.5f,
                    iconSize,
                    iconSize),
                selectedIcon);
        }

        if (open)
        {
            PopupWindow.Show(
                popupRect,
                new PopupContent(
                    options,
                    definitions,
                    selectedIndex,
                    optionIndex => selectionCallback?.Invoke(
                        optionIndex > 0 && optionIndex <= definitions.Count
                            ? definitions[optionIndex - 1]
                            : null)));
        }
    }

    private GUIStyle GetPopupWithIconStyle()
    {
        if (popupWithIconStyle == null)
        {
            RectOffset sourcePadding = EditorStyles.popup.padding;
            popupWithIconStyle = new GUIStyle(EditorStyles.popup)
            {
                padding = new RectOffset(
                    sourcePadding.left + 20,
                    sourcePadding.right,
                    sourcePadding.top,
                    sourcePadding.bottom)
            };
        }

        return popupWithIconStyle;
    }

}
