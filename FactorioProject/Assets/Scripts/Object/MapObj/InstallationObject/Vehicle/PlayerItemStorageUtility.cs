using UnityEngine;

internal static class PlayerItemStorageUtility
{
    public static bool TryAddToPlayerStorage(
        Player player,
        int itemId,
        int preferredSlotIndex,
        out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (player == null || itemId < 0)
        {
            return false;
        }

        if (preferredSlotIndex >= 0)
        {
            if (player.TryAddToBagAtSlot(preferredSlotIndex, itemId, out targetPortableObject))
            {
                return true;
            }

            if (player.HasMatchingHandStackSpace(itemId)
                && player.TryAddToHand(itemId, out targetPortableObject))
            {
                return true;
            }

            return player.TryAddToBag(itemId, out targetPortableObject);
        }

        return (player.HasMatchingHandStackSpace(itemId)
                && player.TryAddToHand(itemId, out targetPortableObject))
               || player.TryAddToBag(itemId, out targetPortableObject);
    }

    public static void MoveVisualToPlayerStorage(
        PortableObject portableObject,
        PortableObject storageTarget)
    {
        if (portableObject == null)
        {
            return;
        }

        portableObject.GetComponent<DroppedItemPickupGate>()?.ClearGate();
        portableObject.CancelMove();
        portableObject.SetBatchedRendering(false);
        portableObject.transform.SetParent(null, true);
        if (storageTarget == null || !Application.isPlaying)
        {
            DestroyPortableObject(portableObject);
            return;
        }

        portableObject.MoveTo(
            storageTarget.transform,
            () => DestroyPortableObject(portableObject));
    }

    public static void DestroyPortableObject(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        portableObject.CancelMove();
        if (Application.isPlaying)
        {
            Object.Destroy(portableObject.gameObject);
            return;
        }

        Object.DestroyImmediate(portableObject.gameObject);
    }
}
