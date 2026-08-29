using UnityEngine;

public partial class PlayerHUD
{
    private bool bucketFluidInteractionActive;
    private Resource bucketOilInteractionSource;

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

        playerController.TryFindNearestOilBucketFillSource(out Resource oilSource);
        ItemDefinition filledBucket;
        if (oilSource != null)
        {
            if (!Bucket.TryResolveOilBucketDefinition(itemManager, out filledBucket))
            {
                return false;
            }
        }
        else if (!playerController.IsNearWaterForPortableInteraction()
                 || !Bucket.TryResolveWaterBucketDefinition(itemManager, out filledBucket))
        {
            return false;
        }

        Sprite icon = emptyBucket.icon;
        if (icon == null || InteractionButton == null)
        {
            return false;
        }

        ClearInteractionTargets();
        bucketFluidInteractionActive = true;
        bucketOilInteractionSource = oilSource;
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

        ItemDefinition filledBucket;
        if (bucketOilInteractionSource != null)
        {
            if (!IsUsableOilInteractionSource(bucketOilInteractionSource, playerController)
                || !Bucket.TryResolveOilBucketDefinition(itemManager, out filledBucket))
            {
                return;
            }
        }
        else if (!playerController.IsNearWaterForPortableInteraction()
                 || !Bucket.TryResolveWaterBucketDefinition(itemManager, out filledBucket))
        {
            return;
        }

        currentPlayer.TryConvertHeldItem(emptyBucket.id, filledBucket.id);
    }

    private static bool IsUsableOilInteractionSource(
        Resource resource,
        PlayerController playerController)
    {
        return resource != null
               && resource.gameObject.activeInHierarchy
               && resource.CanHarvest
               && resource.PlacementCategory == ResourceDefinition.PlacementCategory.Oil
               && playerController != null
               && playerController.IsWithinOilBucketFillRange(resource);
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
