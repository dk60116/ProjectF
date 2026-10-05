using System.Collections.Generic;
using UnityEngine;
namespace ProjectF.MapObjects
{
internal static class LoggingHarvest
{
    private const int NearbyAppleDropSearchRadius = 2;
    private static readonly Vector2Int[] LocalHarvestDirections =
    { Vector2Int.down, Vector2Int.left, Vector2Int.up, Vector2Int.right };
    internal static bool CompleteTreeHarvest(ResourceInstance tree, List<KeyValuePair<int, int>> harvestedSeedDrops)
    {
        harvestedSeedDrops.Clear();
        bool harvested = false;

        if (tree != null && tree.OwningBlock != null)
        {
            Block treeBlock = tree.OwningBlock;
            Vector3 startWorldPosition = tree.FocusPoint;
            int appleItemId = -1;
            int appleCount = 0;
            bool hasAppleDrop = tree is ProjectF.MapObjects.TreeInstance harvestedTree
                                && harvestedTree.TryGetMachineAppleDrop(
                                    out appleItemId,
                                    out appleCount);
            if (tree is ProjectF.MapObjects.TreeInstance seedTree)
                seedTree.CollectMachineSeedDrops(harvestedSeedDrops);

            if (tree.TryHarvestForMachine(out int outputItemId, out int outputCount)
                && outputItemId >= 0
                && outputCount > 0)
            {
                harvested = true;
                // Logging output is authoritative at the harvested tree coordinate.
                for (int i = 0; i < outputCount; i++)
                {
                    if (!treeBlock.TryAddHarvestedFloorObjectAnimated(
                            outputItemId,
                            startWorldPosition,
                            out _,
                            tree))
                    {
                        Debug.LogError($"{nameof(LoggingMachine)} could not place harvested logs at {treeBlock.Coordinate}.");
                        break;
                    }
                }

                if (hasAppleDrop)
                {
                    DropApplesIntoNearbyEmptyBlocks(
                        treeBlock,
                        startWorldPosition,
                        appleItemId,
                        appleCount);
                }

                RecoverHarvestedSeeds(treeBlock, startWorldPosition, harvestedSeedDrops);
            }
        }

        harvestedSeedDrops.Clear();
        return harvested;
    }

    private static void RecoverHarvestedSeeds(Block treeBlock, Vector3 position, List<KeyValuePair<int, int>> seeds)
    {
        for (int i = 0; i < seeds.Count; i++)
        {
            var reward = seeds[i];
            int accepted = ForestryWorld.Current != null
                ? ForestryWorld.Current.RecoverSeeds(treeBlock.Coordinate, reward.Key, reward.Value, position) : 0;
            if (accepted < reward.Value)
                DropHarvestedSeedsNearTree(treeBlock, position, reward.Key, reward.Value - accepted);
        }
    }

    private static void DropHarvestedSeedsNearTree(
        Block treeBlock,
        Vector3 startWorldPosition,
        int itemId,
        int count)
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null
            || treeBlock == null
            || !TryResolveNearbyEmptyDropBlock(
                terrain,
                treeBlock.Coordinate,
                itemId,
                count,
                out Block targetBlock))
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (!targetBlock.TryAddFloorObjectAnimated(
                    itemId,
                    startWorldPosition,
                    0f,
                    out _))
            {
                return;
            }
        }
    }

    private static void DropApplesIntoNearbyEmptyBlocks(
        Block treeBlock,
        Vector3 startWorldPosition,
        int itemId,
        int count)
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null || treeBlock == null || itemId < 0 || count <= 0)
        {
            return;
        }

        if (TryResolveNearbyAppleBox(
                terrain,
                treeBlock.Coordinate,
                itemId,
                count,
                out BoxObject targetBox))
        {
            for (int itemIndex = 0; itemIndex < count; itemIndex++)
            {
                if (!targetBox.TryPutOneContainedObject(
                        itemId,
                        startWorldPosition,
                        0f,
                        out _))
                {
                    return;
                }
            }

            return;
        }

        if (!TryResolveNearbyEmptyDropBlock(
                terrain,
                treeBlock.Coordinate,
                itemId,
                count,
                out Block targetBlock))
        {
            return;
        }

        for (int itemIndex = 0; itemIndex < count; itemIndex++)
        {
            if (!targetBlock.TryAddFloorObjectAnimated(
                    itemId,
                    startWorldPosition,
                    0f,
                    out _))
            {
                return;
            }
        }
    }

    private static bool TryResolveNearbyAppleBox(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int itemId,
        int itemCount,
        out BoxObject targetBox)
    {
        targetBox = null;
        return TryResolveCardinalBox(
                   terrain,
                   centerCoordinate,
                   1,
                   itemId,
                   itemCount,
                   out targetBox)
               || TryResolveNonCardinalBoxRing(
                   terrain,
                   centerCoordinate,
                   1,
                   itemId,
                   itemCount,
                   out targetBox)
               || TryResolveCardinalBox(
                   terrain,
                   centerCoordinate,
                   NearbyAppleDropSearchRadius,
                   itemId,
                   itemCount,
                   out targetBox)
               || TryResolveNonCardinalBoxRing(
                   terrain,
                   centerCoordinate,
                   NearbyAppleDropSearchRadius,
                   itemId,
                   itemCount,
                   out targetBox);
    }

    private static bool TryResolveCardinalBox(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int radius,
        int itemId,
        int itemCount,
        out BoxObject targetBox)
    {
        targetBox = null;
        for (int directionIndex = 0;
             directionIndex < LocalHarvestDirections.Length;
             directionIndex++)
        {
            Vector2Int coordinate = centerCoordinate
                                    + LocalHarvestDirections[directionIndex] * radius;
            if (TryResolveBoxCandidate(
                    terrain,
                    coordinate,
                    itemId,
                    itemCount,
                    out targetBox))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveNonCardinalBoxRing(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int radius,
        int itemId,
        int itemCount,
        out BoxObject targetBox)
    {
        targetBox = null;
        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                if ((Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                    || offsetX == 0
                    || offsetY == 0)
                {
                    continue;
                }

                Vector2Int coordinate = centerCoordinate
                                        + new Vector2Int(offsetX, offsetY);
                if (TryResolveBoxCandidate(
                        terrain,
                        coordinate,
                        itemId,
                        itemCount,
                        out targetBox))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveBoxCandidate(
        TerrainGenerator terrain,
        Vector2Int coordinate,
        int itemId,
        int itemCount,
        out BoxObject targetBox)
    {
        targetBox = null;
        if (!terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || block.MapObject == null)
        {
            return false;
        }

        targetBox = block.MapObject as BoxObject;
        if (targetBox == null)
        {
            block.MapObject.TryGetComponent(out targetBox);
        }

        if (targetBox != null
            && targetBox.CanPutContainedObjects(itemId, itemCount))
        {
            return true;
        }

        targetBox = null;
        return false;
    }

    private static bool TryResolveNearbyEmptyDropBlock(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;

        if (TryResolveCardinalDropBlock(
                terrain,
                centerCoordinate,
                1,
                itemId,
                itemCount,
                out targetBlock)
            || TryResolveNonCardinalDropRing(
                terrain,
                centerCoordinate,
                1,
                itemId,
                itemCount,
                out targetBlock)
            || TryResolveCardinalDropBlock(
                terrain,
                centerCoordinate,
                NearbyAppleDropSearchRadius,
                itemId,
                itemCount,
                out targetBlock)
            || TryResolveNonCardinalDropRing(
                terrain,
                centerCoordinate,
                NearbyAppleDropSearchRadius,
                itemId,
                itemCount,
                out targetBlock))
        {
            return true;
        }

        return false;
    }

    private static bool TryResolveCardinalDropBlock(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int radius,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        for (int directionIndex = 0;
             directionIndex < LocalHarvestDirections.Length;
             directionIndex++)
        {
            Vector2Int coordinate = centerCoordinate
                                    + LocalHarvestDirections[directionIndex] * radius;
            if (TryResolveEmptyDropCandidate(
                    terrain,
                    coordinate,
                    itemId,
                    itemCount,
                    out targetBlock))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveNonCardinalDropRing(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int radius,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                if ((Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                    || offsetX == 0
                    || offsetY == 0)
                {
                    continue;
                }

                Vector2Int coordinate = centerCoordinate
                                        + new Vector2Int(offsetX, offsetY);
                if (TryResolveEmptyDropCandidate(
                        terrain,
                        coordinate,
                        itemId,
                        itemCount,
                        out targetBlock))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveEmptyDropCandidate(
        TerrainGenerator terrain,
        Vector2Int coordinate,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        if (!terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || block.Type != Block.BlockType.Ground
            || block.IsRuntimeConveyor
            || block.MapObject != null
            || block.HasDroppedFloorObjects
            || !block.SupportsFloorObjectDrops
            || !block.CanAddFloorObjects(itemCount, itemId))
        {
            return false;
        }

        targetBlock = block;
        return true;
    }


}
}
