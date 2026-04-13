using System.Collections.Generic;
using UnityEngine;

public class BlockStateStore : MonoBehaviour
{
    private readonly Dictionary<Vector2Int, Resource.ResourceSaveState> savedStates = new Dictionary<Vector2Int, Resource.ResourceSaveState>();
    private readonly Dictionary<Vector2Int, List<int>> savedFloorObjectStates = new Dictionary<Vector2Int, List<int>>();

    public void Save(Vector2Int worldCoordinate, Resource resource)
    {
        if (resource == null)
        {
            return;
        }

        savedStates[worldCoordinate] = resource.CaptureState();
    }

    public void SaveFloorObjects(Vector2Int worldCoordinate, Block block)
    {
        if (block == null)
        {
            return;
        }

        List<int> itemIds = block.CaptureFloorObjectState();
        if (itemIds == null || itemIds.Count == 0)
        {
            savedFloorObjectStates.Remove(worldCoordinate);
            return;
        }

        savedFloorObjectStates[worldCoordinate] = itemIds;
    }

    public bool TryGet(Vector2Int worldCoordinate, out Resource.ResourceSaveState state)
    {
        return savedStates.TryGetValue(worldCoordinate, out state);
    }

    public bool IsDepleted(Vector2Int worldCoordinate)
    {
        return savedStates.TryGetValue(worldCoordinate, out Resource.ResourceSaveState state)
               && state.resourceCount <= 0;
    }

    public bool TryGetFloorObjects(Vector2Int worldCoordinate, out List<int> itemIds)
    {
        return savedFloorObjectStates.TryGetValue(worldCoordinate, out itemIds);
    }

    public void ClearStates()
    {
        savedStates.Clear();
        savedFloorObjectStates.Clear();
    }
}
