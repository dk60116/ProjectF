using UnityEngine;

public class HandSlot : BagSlot
{
    protected override int GetCraftingDirectionSign()
    {
        return 1;
    }

    protected override bool AllowPickupOnClick => true;

    protected override bool TryPickupOneItemAtCoordinate(TerrainGenerator terrain, Player player, Vector2Int coordinate, Vector3 pickupOrigin, float pickupRange, bool allowFocusedConveyorPickup = true)
    {
        if (terrain == null || player == null)
        {
            return false;
        }

        if (allowFocusedConveyorPickup
            && TryGetFocusedConveyorBlock(player, out Block focusedConveyorBlock)
            && TryPickupFocusedConveyorItem(player, focusedConveyorBlock, FocusedPickupRange))
        {
            return true;
        }

        if (TryPickupFocusedBoxToHand(player, pickupOrigin, FocusedPickupRange))
        {
            return true;
        }

        if (!TryGetGroundPickupBlock(terrain, coordinate, out Block block))
        {
            return false;
        }

        return block.TryPickupOneFloorObjectToHand(player, pickupOrigin, pickupRange);
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
