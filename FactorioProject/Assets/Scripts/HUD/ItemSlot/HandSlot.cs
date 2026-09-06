using UnityEngine;

public class HandSlot : BagSlot
{
    protected override bool AllowPickupOnClick => true;

    protected override bool TryPickupGroundCandidate(Player player, Block block,
        PortableObject requiredPortableObject, Vector3 pickupOrigin, float pickupRange)
    {
        return player != null && block != null
            && block.TryPickupOneFloorObjectToHand(player, pickupOrigin, pickupRange, requiredPortableObject);
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

        return focusedConveyorBlock.TryPickupOneConveyorObjectToHand(player, ResolvePickupOrigin(player), pickupRange, maxPickupCount);
    }

    protected override bool CanPreviewAcceptPickupItem(Player player, int itemId)
    {
        return player != null && player.CanAcceptHandObject(itemId);
    }
}
