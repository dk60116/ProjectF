using UnityEngine;

public partial class PlayerHUD
{
    private bool bucketWaterInteractionActive;

    private bool TryActivateBucketWaterInteraction(
        Player currentPlayer,
        PlayerController playerController)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (currentPlayer == null
            || playerController == null
            || !playerController.IsNearWaterForPortableInteraction()
            || !TryResolveHeldEmptyBucket(currentPlayer, out ItemDefinition emptyBucket)
            || !Bucket.TryResolveWaterBucketDefinition(itemManager, out ItemDefinition waterBucket)
            || !currentPlayer.CanConvertHeldItem(emptyBucket.id, waterBucket.id))
        {
            return false;
        }

        Sprite icon = waterBucket.icon != null
            ? waterBucket.icon
            : emptyBucket.icon;
        if (icon == null || InteractionButton == null)
        {
            return false;
        }

        ClearInteractionTargets();
        bucketWaterInteractionActive = true;
        SetActiveInteractionButton(InteractionButton, icon);
        return true;
    }

    private void HandleBucketWaterInteraction(Player currentPlayer)
    {
        PlayerController playerController = currentPlayer != null
            ? currentPlayer.GetComponent<PlayerController>()
            : null;
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (playerController == null
            || !playerController.IsNearWaterForPortableInteraction()
            || !TryResolveHeldEmptyBucket(currentPlayer, out ItemDefinition emptyBucket)
            || !Bucket.TryResolveWaterBucketDefinition(itemManager, out ItemDefinition waterBucket))
        {
            return;
        }

        currentPlayer.TryConvertHeldItem(emptyBucket.id, waterBucket.id);
    }

    private static bool TryResolveHeldEmptyBucket(
        Player currentPlayer,
        out ItemDefinition bucketDefinition)
    {
        bucketDefinition = null;
        if (currentPlayer == null)
        {
            return false;
        }

        PlayerBag handBag = currentPlayer.GetHandBag();
        if (handBag == null || handBag.GetSlotCount(0) <= 0)
        {
            return false;
        }

        bucketDefinition = GetItemDefinition(handBag.GetSlotItemId(0));
        if (Bucket.IsEmptyBucketDefinition(bucketDefinition))
        {
            return true;
        }

        bucketDefinition = null;
        return false;
    }
}
