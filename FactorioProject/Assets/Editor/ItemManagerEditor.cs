using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ItemManager))]
public class ItemManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Rebuild"))
        {
            ItemManager itemManager = (ItemManager)target;
            Undo.RecordObject(itemManager, "Rebuild Item Data");
            itemManager.RebuildItemDefinitionsFromAssets();
            itemManager.ApplyItemIdsToPrefabs();
            EditorUtility.SetDirty(itemManager);
        }

        if (GUILayout.Button("Open Item Data UI"))
        {
            ItemDataEditorWindow.ShowWindow();
        }

        if (GUILayout.Button("Open Crafting Tree UI"))
        {
            CraftingTreeEditorWindow.ShowWindow();
        }
    }
}

internal static class ItemDefinitionDragAndDropUtility
{
    private const string DragDataKey = "ProjectF.ItemDefinition";
    private const float DragStartDistance = 6f;
    private static readonly Color DropFillColor = new Color(0.35f, 0.65f, 1f, 0.16f);
    private static readonly Color DropOutlineColor = new Color(0.35f, 0.65f, 1f, 0.95f);
    private static ItemDefinition pendingDefinition;
    private static string pendingDisplayName;
    private static EditorWindow pendingOwner;
    private static Vector2 pendingMouseDownPosition;

    public static void HandleListItemDrag(Rect rect, ItemDefinition definition, string displayName, EditorWindow owner)
    {
        if (definition == null)
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
                    pendingDefinition = definition;
                    pendingDisplayName = displayName;
                    pendingOwner = owner;
                    pendingMouseDownPosition = current.mousePosition;
                }
                break;

            case EventType.MouseDrag:
                if (pendingDefinition != definition || pendingOwner != owner)
                {
                    return;
                }

                if ((current.mousePosition - pendingMouseDownPosition).sqrMagnitude < DragStartDistance * DragStartDistance)
                {
                    return;
                }

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new UnityEngine.Object[] { definition };
                DragAndDrop.SetGenericData(DragDataKey, definition);
                DragAndDrop.StartDrag(string.IsNullOrWhiteSpace(pendingDisplayName) ? definition.name : pendingDisplayName);
                ClearPendingDrag();
                owner?.Repaint();
                current.Use();
                break;

            case EventType.MouseUp:
            case EventType.DragExited:
            case EventType.Ignore:
                if (pendingOwner == owner)
                {
                    ClearPendingDrag();
                }
                break;
        }
    }

    public static bool HandleDropTarget(Rect rect, EditorWindow owner, out ItemDefinition droppedDefinition)
    {
        droppedDefinition = GetDraggedDefinition();
        if (droppedDefinition == null)
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
                owner?.Repaint();
                current.Use();
                return true;

            case EventType.Repaint:
                DrawDropHighlight(rect);
                break;
        }

        return false;
    }

    public static bool TryGetDraggedDefinition(out ItemDefinition draggedDefinition)
    {
        draggedDefinition = GetDraggedDefinition();
        return draggedDefinition != null;
    }

    private static ItemDefinition GetDraggedDefinition()
    {
        object draggedData = DragAndDrop.GetGenericData(DragDataKey);
        if (draggedData is ItemDefinition draggedDefinition)
        {
            return draggedDefinition;
        }

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

    private static void DrawDropHighlight(Rect rect)
    {
        EditorGUI.DrawRect(rect, DropFillColor);
        DrawOutline(rect, DropOutlineColor);
    }

    private static void ClearPendingDrag()
    {
        pendingDefinition = null;
        pendingDisplayName = null;
        pendingOwner = null;
        pendingMouseDownPosition = Vector2.zero;
    }

    private static void DrawOutline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), color);
    }
}

internal sealed class InputOutputRectGridBlockDragPayload
{
    public InputOutputModule.RectGridBlockType blockType;
    public bool hasSourceCell;
    public int sourceX;
    public int sourceY;

    public Vector2Int SourceCell => new Vector2Int(sourceX, sourceY);
}

internal static class InputOutputRectGridBlockDragAndDropUtility
{
    private const string DragDataKey = "ProjectF.InputOutputRectGridBlock";
    private const float DragStartDistance = 6f;
    private static InputOutputModule.RectGridBlockType pendingBlockType;
    private static string pendingDisplayName;
    private static EditorWindow pendingOwner;
    private static Vector2 pendingMouseDownPosition;
    private static bool hasPendingBlock;
    private static bool pendingHasSourceCell;
    private static Vector2Int pendingSourceCell;

    public static void HandlePaletteBlockDrag(Rect rect, InputOutputModule.RectGridBlockType blockType, string displayName, EditorWindow owner)
    {
        HandleBlockDrag(rect, blockType, displayName, owner, false, default);
    }

    public static void HandlePlacedBlockDrag(
        Rect rect,
        InputOutputModule.RectGridBlockType blockType,
        Vector2Int sourceCell,
        string displayName,
        EditorWindow owner)
    {
        HandleBlockDrag(rect, blockType, displayName, owner, true, sourceCell);
    }

    private static void HandleBlockDrag(
        Rect rect,
        InputOutputModule.RectGridBlockType blockType,
        string displayName,
        EditorWindow owner,
        bool hasSourceCell,
        Vector2Int sourceCell)
    {
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
                    pendingBlockType = blockType;
                    pendingDisplayName = displayName;
                    pendingOwner = owner;
                    pendingMouseDownPosition = current.mousePosition;
                    pendingHasSourceCell = hasSourceCell;
                    pendingSourceCell = sourceCell;
                    hasPendingBlock = true;
                }
                break;

            case EventType.MouseDrag:
                if (!hasPendingBlock || pendingBlockType != blockType || pendingOwner != owner)
                {
                    return;
                }

                if ((current.mousePosition - pendingMouseDownPosition).sqrMagnitude < DragStartDistance * DragStartDistance)
                {
                    return;
                }

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = System.Array.Empty<UnityEngine.Object>();
                DragAndDrop.SetGenericData(DragDataKey, new InputOutputRectGridBlockDragPayload
                {
                    blockType = blockType,
                    hasSourceCell = pendingHasSourceCell,
                    sourceX = pendingSourceCell.x,
                    sourceY = pendingSourceCell.y
                });
                DragAndDrop.StartDrag(string.IsNullOrWhiteSpace(pendingDisplayName) ? blockType.ToString() : pendingDisplayName);
                ClearPendingDrag();
                owner?.Repaint();
                current.Use();
                break;

            case EventType.MouseUp:
            case EventType.DragExited:
            case EventType.Ignore:
                if (pendingOwner == owner)
                {
                    ClearPendingDrag();
                }
                break;
        }
    }

    public static bool TryGetDraggedBlockPayload(out InputOutputRectGridBlockDragPayload payload)
    {
        object draggedData = DragAndDrop.GetGenericData(DragDataKey);
        if (draggedData is InputOutputRectGridBlockDragPayload draggedPayload)
        {
            payload = draggedPayload;
            return true;
        }

        payload = null;
        return false;
    }

    private static void ClearPendingDrag()
    {
        hasPendingBlock = false;
        pendingBlockType = default;
        pendingDisplayName = null;
        pendingOwner = null;
        pendingMouseDownPosition = Vector2.zero;
        pendingHasSourceCell = false;
        pendingSourceCell = default;
    }
}
