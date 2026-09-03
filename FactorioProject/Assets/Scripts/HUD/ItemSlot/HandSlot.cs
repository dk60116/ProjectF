using UnityEngine;

public class HandSlot : BagSlot
{
    protected override bool AllowPickupOnClick => true;

    protected override bool TryPickupOneItemAtCoordinate(TerrainGenerator terrain, Player player, Vector2Int coordinate, Vector3 pickupOrigin, float pickupRange)
    {
        if (terrain == null || player == null)
        {
            return false;
        }

        PortableObject requiredPortableObject = null;
        Block block;
        if (TryGetPickupPreviewSource(out PortableObject previewPortableObject, out Block previewBlock))
        {
            requiredPortableObject = previewPortableObject;
            block = previewBlock;
        }
        else if (!TryGetGroundPickupBlock(terrain, player, coordinate, out block))
        {
            return false;
        }

        return block.TryPickupOneFloorObjectToHand(
            player,
            pickupOrigin,
            pickupRange,
            requiredPortableObject);
    }

    protected override bool TryPickupFromFocusedBox(
        Player player,
        BoxObject focusedBoxObject,
        Vector3 pickupOrigin,
        float pickupRange)
    {
        return focusedBoxObject != null
               && focusedBoxObject.TryPickupContainedObjectToHand(player, pickupOrigin, pickupRange);
    }

    protected override bool TryPickupFromFocusedItemStorage(
        Player player,
        IPlayerItemStorage focusedItemStorage,
        Vector3 pickupOrigin,
        float pickupRange)
    {
        return focusedItemStorage != null
               && focusedItemStorage.TryPickupOneItemToHand(
                   player,
                   pickupOrigin,
                   pickupRange,
                   GetPreferredPickupItemId());
    }

    protected override bool TryPickupFocusedConveyorItem(Player player, Block focusedConveyorBlock, float pickupRange, int maxPickupCount = int.MaxValue)
    {
        if (player == null || focusedConveyorBlock == null)
        {
            return false;
        }

        return focusedConveyorBlock.TryPickupOneConveyorObjectToHand(player, player.transform.position, pickupRange, maxPickupCount);
    }

    protected override bool CanPreviewAcceptPickupItem(Player player, int itemId)
    {
        return player != null && player.CanAcceptHandObject(itemId);
    }
}
