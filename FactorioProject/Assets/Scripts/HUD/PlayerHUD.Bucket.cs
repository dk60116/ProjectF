using UnityEngine;

public partial class PlayerHUD
{
    private bool bucketFluidInteractionActive;

    private bool TryActivateBucketFluidInteraction(
        Player currentPlayer,
        PlayerController playerController)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (currentPlayer == null
            || playerController == null
            || !TryResolveHeldEmptyBucket(currentPlayer, out ItemDefinition emptyBucket))
        {
            return false;
        }

        if (!playerController.TryFindNearestBucketFluidSource(out _, out Resource oilSource))
        {
            return false;
        }

        ItemDefinition filledBucket;
        if (oilSource != null)
        {
            if (!Bucket.TryResolveOilBucketDefinition(itemManager, out filledBucket))
            {
                return false;
            }
        }
        else if (!Bucket.TryResolveWaterBucketDefinition(itemManager, out filledBucket))
        {
            return false;
        }

        Sprite icon = ResolveInteractionIcon(emptyBucket, 0, false);
        if (icon == null || InteractionButton == null)
        {
            return false;
        }

        ClearInteractionTargets();
        bucketFluidInteractionActive = true;
        SetActiveInteractionButton(InteractionButton, icon);
        return true;
    }

    private void HandleBucketFluidInteraction(Player currentPlayer)
    {
        PlayerController playerController = currentPlayer != null
            ? currentPlayer.GetComponent<PlayerController>()
            : null;
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (playerController == null
            || !TryResolveHeldEmptyBucket(currentPlayer, out ItemDefinition emptyBucket))
        {
            return;
        }

        if (!playerController.TryFindNearestBucketFluidSource(out _, out Resource oilSource))
        {
            return;
        }

        ItemDefinition filledBucket;
        if (oilSource != null)
        {
            if (!Bucket.TryResolveOilBucketDefinition(itemManager, out filledBucket))
            {
                return;
            }
        }
        else if (!Bucket.TryResolveWaterBucketDefinition(itemManager, out filledBucket))
        {
            return;
        }

        currentPlayer.TryConvertHeldItem(emptyBucket.id, filledBucket.id);
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
