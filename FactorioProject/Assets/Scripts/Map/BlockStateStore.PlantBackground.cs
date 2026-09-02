using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore
{
    public void CollectSavedResourceCoordinates(List<Vector2Int> coordinates)
    {
        if (coordinates == null)
        {
            return;
        }

        coordinates.Clear();
        foreach (Vector2Int coordinate in savedStates.Keys)
        {
            coordinates.Add(coordinate);
        }
    }

    public void CollectSavedInstallationStorageKeys(List<Vector2Int> storageKeys)
    {
        if (storageKeys == null)
        {
            return;
        }

        storageKeys.Clear();
        foreach (Vector2Int storageKey in savedInstallationStates.Keys)
        {
            storageKeys.Add(storageKey);
        }
    }

    public bool TryGetSavedResourceState(
        Vector2Int coordinate,
        out int itemId,
        out Resource.ResourceSaveState state)
    {
        if (savedStates.TryGetValue(coordinate, out state)
            && savedResourceItemIds.TryGetValue(coordinate, out itemId))
        {
            return true;
        }

        itemId = -1;
        state = default;
        return false;
    }

    public void UpdateSavedResourceState(
        Vector2Int coordinate,
        int itemId,
        Resource.ResourceSaveState state,
        bool refreshVirtualWorld = true)
    {
        savedStates[coordinate] = state;
        if (itemId >= 0)
        {
            savedResourceItemIds[coordinate] = itemId;
            if (refreshVirtualWorld)
            {
                ResolveVirtualObjectWorld()?.UpsertResource(coordinate, itemId, state);
            }
        }
    }

    public bool IsSavedCoordinateEmptyGround(Vector2Int coordinate)
    {
        return !savedStates.ContainsKey(coordinate)
               && !TryGetInstallationAnchorAtCoordinate(coordinate, out _)
               && !HasSavedDroppedFloorObjects(coordinate);
    }
}
