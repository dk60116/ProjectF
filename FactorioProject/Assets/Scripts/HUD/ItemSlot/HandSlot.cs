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

    protected override bool TryPickupOneItemAtCoordinate(TerrainGenerator terrain, Player player, Vector2Int coordinate, bool allowFocusedConveyorPickup = true)
    {
        if (terrain == null)
        {
            return false;
        }

        return terrain.TryPickupOneItemToHandAtCoordinate(player, coordinate, allowFocusedConveyorPickup);
    }

    protected override bool TryPickupFocusedConveyorItem(Player player, Block focusedConveyorBlock)
    {
        if (player == null || focusedConveyorBlock == null)
        {
            return false;
        }

        return focusedConveyorBlock.TryPickupOneConveyorObjectToHand(player, player.transform.position, 999f);
    }
}
