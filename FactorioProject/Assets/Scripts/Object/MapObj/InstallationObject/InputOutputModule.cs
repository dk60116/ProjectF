using System.Collections.Generic;
using UnityEngine;

public class InputOutputModule : InstallationObject
{
    public enum SlotLayoutType
    {
        None = 0,
        RectGrid = 1
    }

    public enum RectGridBlockType
    {
        None = 0,
        Object = 1,
        InputEnergy = 2,
        InputItem = 3,
        Output = 4
    }

    public enum RectGridDirection
    {
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3
    }

    [System.Serializable]
    public struct ItemIoEntry
    {
        public ItemDefinition itemDefinition;
        public int count;

        public ItemIoEntry(ItemDefinition itemDefinition, int count)
        {
            this.itemDefinition = itemDefinition;
            this.count = count;
        }
    }

    [System.Serializable]
    public struct RectGridCell
    {
        public int x;
        public int y;

        public RectGridCell(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [System.Serializable]
    public struct RectGridBlockPlacement
    {
        public int x;
        public int y;
        public RectGridBlockType blockType;

        public RectGridBlockPlacement(int x, int y, RectGridBlockType blockType)
        {
            this.x = x;
            this.y = y;
            this.blockType = blockType;
        }
    }

    [SerializeField]
    private List<ItemIoEntry> inputList = new List<ItemIoEntry>();
    [SerializeField]
    private List<ItemIoEntry> outputList = new List<ItemIoEntry>();
    [SerializeField, HideInInspector]
    private ItemIoEntry output = new ItemIoEntry(null, 1);
    [SerializeField]
    private SlotLayoutType slotLayoutType = SlotLayoutType.None;
    [SerializeField]
    private int rectGridWidth = 1;
    [SerializeField]
    private int rectGridHeight = 1;
    [SerializeField]
    private List<RectGridCell> rectGridCells = new List<RectGridCell>();
    [SerializeField]
    private List<RectGridBlockPlacement> rectGridPlacements = new List<RectGridBlockPlacement>();

    public IReadOnlyList<ItemIoEntry> InputList
    {
        get
        {
            EnsurePairData();
            return inputList;
        }
    }

    public IReadOnlyList<ItemIoEntry> OutputList
    {
        get
        {
            EnsurePairData();
            return outputList;
        }
    }

    public ItemIoEntry Output
    {
        get
        {
            EnsurePairData();
            return outputList.Count > 0 ? outputList[0] : output;
        }
    }

    public SlotLayoutType LayoutType
    {
        get
        {
            EnsureRectGridData();
            return slotLayoutType;
        }
    }

    public int RectGridWidth
    {
        get
        {
            EnsureRectGridData();
            return rectGridWidth;
        }
    }

    public int RectGridHeight
    {
        get
        {
            EnsureRectGridData();
            return rectGridHeight;
        }
    }

    public IReadOnlyList<RectGridCell> RectGridCells
    {
        get
        {
            EnsureRectGridData();
            return rectGridCells;
        }
    }

    public IReadOnlyList<RectGridBlockPlacement> RectGridPlacements
    {
        get
        {
            EnsureRectGridPlacementData();
            return rectGridPlacements;
        }
    }

    private void EnsurePairData()
    {
        if (inputList == null)
        {
            inputList = new List<ItemIoEntry>();
        }

        if (outputList == null)
        {
            outputList = new List<ItemIoEntry>();
        }

        for (int i = 0; i < inputList.Count; i++)
        {
            ItemIoEntry entry = inputList[i];
            entry.count = Mathf.Max(1, entry.count);
            inputList[i] = entry;
        }

        if (outputList.Count == 0 && inputList.Count > 0)
        {
            ItemIoEntry migratedOutput = output;
            migratedOutput.count = Mathf.Max(1, migratedOutput.count);

            for (int i = 0; i < inputList.Count; i++)
            {
                outputList.Add(migratedOutput);
            }

            output = new ItemIoEntry(null, 1);
        }

        while (outputList.Count < inputList.Count)
        {
            outputList.Add(new ItemIoEntry(null, 1));
        }

        while (outputList.Count > inputList.Count)
        {
            outputList.RemoveAt(outputList.Count - 1);
        }

        for (int i = 0; i < outputList.Count; i++)
        {
            ItemIoEntry entry = outputList[i];
            entry.count = Mathf.Max(1, entry.count);
            outputList[i] = entry;
        }
    }

    public void ConfigureRectGrid(int width, int height)
    {
        slotLayoutType = SlotLayoutType.RectGrid;
        rectGridWidth = Mathf.Max(1, width);
        rectGridHeight = Mathf.Max(1, height);
        RebuildRectGridCells();
        EnsureRectGridPlacementData();
    }

    public void ClearRectGrid()
    {
        slotLayoutType = SlotLayoutType.None;
        rectGridCells.Clear();
        rectGridPlacements.Clear();
    }

    public RectGridBlockType GetRectGridBlockAt(int x, int y)
    {
        EnsureRectGridPlacementData();
        int placementIndex = FindRectGridPlacementIndex(x, y);
        return placementIndex >= 0
            ? rectGridPlacements[placementIndex].blockType
            : RectGridBlockType.None;
    }

    public bool TryGetRectGridBlockCell(RectGridBlockType blockType, out Vector2Int cell)
    {
        EnsureRectGridPlacementData();
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.blockType != blockType)
            {
                continue;
            }

            cell = new Vector2Int(placement.x, placement.y);
            return true;
        }

        cell = default;
        return false;
    }

    public bool TryGetPrimaryObjectCell(out Vector2Int cell)
    {
        EnsureRectGridPlacementData();
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.blockType != RectGridBlockType.Object)
            {
                continue;
            }

            cell = new Vector2Int(placement.x, placement.y);
            return true;
        }

        cell = default;
        return false;
    }

    public bool TryGetInitialOutputDirection(out RectGridDirection direction)
    {
        EnsureRectGridPlacementData();
        direction = RectGridDirection.Right;
        if (!TryGetPrimaryObjectCell(out Vector2Int objectCell)
            || !TryGetRectGridBlockCell(RectGridBlockType.Output, out Vector2Int outputCell))
        {
            return false;
        }

        Vector2Int delta = outputCell - objectCell;
        return TryConvertOffsetToDirection(delta, out direction);
    }

    public static RectGridDirection RotateDirection(RectGridDirection direction, int quarterTurns)
    {
        int normalizedTurns = ((quarterTurns % 4) + 4) % 4;
        return (RectGridDirection)(((int)direction + normalizedTurns) % 4);
    }

    public bool TryGetOutputDirection(int quarterTurns, out RectGridDirection direction)
    {
        EnsureRectGridPlacementData();
        direction = RectGridDirection.Right;
        if (!TryGetPrimaryObjectCell(out Vector2Int objectCell)
            || !TryGetRectGridBlockCell(RectGridBlockType.Output, out Vector2Int outputCell))
        {
            return false;
        }

        Vector2Int delta = outputCell - objectCell;
        delta = RotateCellOffset(delta, quarterTurns);
        return TryConvertOffsetToDirection(delta, out direction);
    }

    private static bool TryConvertOffsetToDirection(Vector2Int delta, out RectGridDirection direction)
    {
        direction = RectGridDirection.Right;
        if (delta == Vector2Int.zero)
        {
            return false;
        }

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
        {
            direction = delta.x >= 0 ? RectGridDirection.Right : RectGridDirection.Left;
            return true;
        }

        direction = delta.y >= 0 ? RectGridDirection.Up : RectGridDirection.Down;
        return true;
    }

    private static Vector2Int RotateCellOffset(Vector2Int offset, int quarterTurns)
    {
        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        return normalizedQuarterTurns switch
        {
            1 => new Vector2Int(offset.y, -offset.x),
            2 => new Vector2Int(-offset.x, -offset.y),
            3 => new Vector2Int(-offset.y, offset.x),
            _ => offset
        };
    }

    public void SetRectGridBlock(int x, int y, RectGridBlockType blockType)
    {
        EnsureRectGridData();
        EnsureRectGridPlacementData();
        if (!IsValidRectGridCell(x, y))
        {
            return;
        }

        RemoveRectGridBlockAt(x, y);
        if (blockType == RectGridBlockType.None)
        {
            return;
        }

        if (IsUniqueRectGridBlock(blockType))
        {
            RemoveRectGridBlock(blockType);
        }

        if (blockType == RectGridBlockType.Object && GetRectGridObjectCount() >= GetMaxObjectBlockCount())
        {
            return;
        }

        rectGridPlacements.Add(new RectGridBlockPlacement(x, y, blockType));
    }

    public void MoveOrSwapRectGridBlock(Vector2Int sourceCell, Vector2Int targetCell)
    {
        EnsureRectGridData();
        EnsureRectGridPlacementData();
        if (!IsValidRectGridCell(sourceCell.x, sourceCell.y) || !IsValidRectGridCell(targetCell.x, targetCell.y))
        {
            return;
        }

        if (sourceCell == targetCell)
        {
            return;
        }

        RectGridBlockType sourceBlockType = GetRectGridBlockAt(sourceCell.x, sourceCell.y);
        if (sourceBlockType == RectGridBlockType.None)
        {
            return;
        }

        RectGridBlockType targetBlockType = GetRectGridBlockAt(targetCell.x, targetCell.y);
        SetRectGridBlockInternal(targetCell.x, targetCell.y, sourceBlockType);
        SetRectGridBlockInternal(sourceCell.x, sourceCell.y, targetBlockType);
        EnsureRectGridPlacementData();
    }

    public void RemoveRectGridBlockAt(int x, int y)
    {
        EnsureRectGridPlacementData();
        int placementIndex = FindRectGridPlacementIndex(x, y);
        if (placementIndex >= 0)
        {
            rectGridPlacements.RemoveAt(placementIndex);
        }
    }

    private void EnsureRectGridData()
    {
        rectGridWidth = Mathf.Max(1, rectGridWidth);
        rectGridHeight = Mathf.Max(1, rectGridHeight);

        if (rectGridCells == null)
        {
            rectGridCells = new List<RectGridCell>();
        }

        if (slotLayoutType != SlotLayoutType.RectGrid)
        {
            if (rectGridCells.Count > 0)
            {
                rectGridCells.Clear();
            }

            return;
        }

        int expectedCount = Mathf.Max(1, rectGridWidth) * Mathf.Max(1, rectGridHeight);
        bool requiresRebuild = rectGridCells.Count != expectedCount;

        if (!requiresRebuild)
        {
            int index = 0;
            for (int y = rectGridHeight - 1; y >= 0 && !requiresRebuild; y--)
            {
                for (int x = 0; x < rectGridWidth; x++)
                {
                    RectGridCell cell = rectGridCells[index++];
                    if (cell.x != x || cell.y != y)
                    {
                        requiresRebuild = true;
                        break;
                    }
                }
            }
        }

        if (requiresRebuild)
        {
            RebuildRectGridCells();
        }
    }

    private void EnsureRectGridPlacementData()
    {
        if (rectGridPlacements == null)
        {
            rectGridPlacements = new List<RectGridBlockPlacement>();
        }

        if (slotLayoutType != SlotLayoutType.RectGrid)
        {
            if (rectGridPlacements.Count > 0)
            {
                rectGridPlacements.Clear();
            }

            return;
        }

        List<RectGridBlockPlacement> normalizedPlacements = new List<RectGridBlockPlacement>();
        HashSet<int> occupiedCells = new HashSet<int>();
        int objectCount = 0;
        bool hasInputEnergy = false;
        bool hasOutput = false;
        int maxObjectCount = GetMaxObjectBlockCount();

        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.blockType == RectGridBlockType.None || !IsValidRectGridCell(placement.x, placement.y))
            {
                continue;
            }

            int cellKey = placement.y * rectGridWidth + placement.x;
            if (occupiedCells.Contains(cellKey))
            {
                continue;
            }

            if (placement.blockType == RectGridBlockType.Object)
            {
                if (objectCount >= maxObjectCount)
                {
                    continue;
                }

                objectCount++;
            }
            else if (placement.blockType == RectGridBlockType.InputEnergy)
            {
                if (hasInputEnergy)
                {
                    continue;
                }

                hasInputEnergy = true;
            }
            else if (placement.blockType == RectGridBlockType.Output)
            {
                if (hasOutput)
                {
                    continue;
                }

                hasOutput = true;
            }

            occupiedCells.Add(cellKey);
            normalizedPlacements.Add(placement);
        }

        rectGridPlacements = normalizedPlacements;
    }

    private void RebuildRectGridCells()
    {
        if (rectGridCells == null)
        {
            rectGridCells = new List<RectGridCell>();
        }

        rectGridCells.Clear();
        if (slotLayoutType != SlotLayoutType.RectGrid)
        {
            return;
        }

        for (int y = rectGridHeight - 1; y >= 0; y--)
        {
            for (int x = 0; x < rectGridWidth; x++)
            {
                rectGridCells.Add(new RectGridCell(x, y));
            }
        }
    }

    private bool IsValidRectGridCell(int x, int y)
    {
        return x >= 0 && x < rectGridWidth && y >= 0 && y < rectGridHeight;
    }

    private int FindRectGridPlacementIndex(int x, int y)
    {
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            RectGridBlockPlacement placement = rectGridPlacements[i];
            if (placement.x == x && placement.y == y)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveRectGridBlock(RectGridBlockType blockType)
    {
        for (int i = rectGridPlacements.Count - 1; i >= 0; i--)
        {
            if (rectGridPlacements[i].blockType == blockType)
            {
                rectGridPlacements.RemoveAt(i);
            }
        }
    }

    private void SetRectGridBlockInternal(int x, int y, RectGridBlockType blockType)
    {
        int placementIndex = FindRectGridPlacementIndex(x, y);
        if (blockType == RectGridBlockType.None)
        {
            if (placementIndex >= 0)
            {
                rectGridPlacements.RemoveAt(placementIndex);
            }

            return;
        }

        RectGridBlockPlacement placement = new RectGridBlockPlacement(x, y, blockType);
        if (placementIndex >= 0)
        {
            rectGridPlacements[placementIndex] = placement;
            return;
        }

        rectGridPlacements.Add(placement);
    }

    private static bool IsUniqueRectGridBlock(RectGridBlockType blockType)
    {
        return blockType == RectGridBlockType.InputEnergy
            || blockType == RectGridBlockType.Output;
    }

    private int GetRectGridObjectCount()
    {
        int count = 0;
        for (int i = 0; i < rectGridPlacements.Count; i++)
        {
            if (rectGridPlacements[i].blockType == RectGridBlockType.Object)
            {
                count++;
            }
        }

        return count;
    }

    private int GetMaxObjectBlockCount()
    {
        int mapSizeX = Mathf.Max(1, Status.mapSizeX);
        int mapSizeY = Mathf.Max(1, Status.mapSizeY);
        return mapSizeX * mapSizeY;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsurePairData();
        EnsureRectGridData();
        EnsureRectGridPlacementData();
    }
#endif
}
