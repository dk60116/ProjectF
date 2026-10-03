using System.Collections.Generic;
using UnityEngine;

// Shared item transport boundary for data facilities. Identity/state belong to their worlds.
internal interface IDataItemProducer
{
    TerrainGenerator Terrain { get; }
    BlockStateStore Store { get; }
    InputOutputModule OutputPrototype { get; }
    BlockStateStore.InstallationSaveState Placement { get; }
    Vector2Int AnchorCoordinate { get; }
    IReadOnlyList<Vector2Int> OutputCoordinates { get; }
}

public interface IProductionTargetSelection
{
    bool CanSelectProductionTarget(int itemId);
    bool TryCollectAllProductionTargetItemIds(ICollection<int> itemIds);
    bool IsProductionTargetSelected(int itemId);
    void SetExclusiveProductionTarget(int itemId);
    void ClearProductionTargetSelection();
}
