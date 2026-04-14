using UnityEngine;

public class HandSlot : BagSlot
{
    protected override int GetCraftingDirectionSign()
    {
        return 1;
    }

    protected override bool AllowPickupOnClick => true;

    protected override bool TryPickupOneItem(TerrainGenerator terrain, Player player, Vector3 pickupOrigin, int radius, float pickupRange)
    {
        if (terrain == null)
        {
            return false;
        }

        return terrain.TryPickupOneItemToHand(player, pickupOrigin, radius, pickupRange);
    }

    protected override bool TryPickupOneItemAtCoordinate(TerrainGenerator terrain, Player player, Vector2Int coordinate)
    {
        if (terrain == null)
        {
            return false;
        }

        return terrain.TryPickupOneItemToHandAtCoordinate(player, coordinate);
    }
}
