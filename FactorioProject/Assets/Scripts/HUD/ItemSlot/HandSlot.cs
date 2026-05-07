using UnityEngine;

public class HandSlot : BagSlot
{
    protected override int GetCraftingDirectionSign()
    {
        return 1;
    }

    protected override bool AllowPickupOnClick => true;

    protected override bool TryPickupOneItem(TerrainGenerator terrain, Player player, Vector3 pickupOrigin, int radius, float pickupRange, bool allowFocusedConveyorPickup = true)
    {
        if (terrain == null)
        {
            return false;
        }

        return terrain.TryPickupOneItemToHand(player, pickupOrigin, radius, pickupRange, allowFocusedConveyorPickup);
    }

    protected override bool TryPickupOneItemAtCoordinate(TerrainGenerator terrain, Player player, Vector2Int coordinate, Vector3 pickupOrigin, float pickupRange, bool allowFocusedConveyorPickup = true)
    {
        if (terrain == null)
        {
            return false;
        }

        return terrain.TryPickupOneItemToHandAtCoordinate(player, coordinate, pickupOrigin, pickupRange, allowFocusedConveyorPickup);
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
