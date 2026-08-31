using UnityEngine;
using PlantResource = ProjectF.MapObjects.Tree;

public partial class PlayerHUD
{
    private enum PlantGrowthInteractionType
    {
        None,
        Water,
        Fertilizer
    }

    private PlantGrowthInteractionType plantGrowthInteractionType;
    private PlantResource currentPlantGrowthTarget;

    private bool TryActivatePlantGrowthInteraction(
        Player currentPlayer,
        PlayerController playerController)
    {
        if (currentPlayer == null
            || playerController == null
            || !TryResolveHeldItem(currentPlayer, out ItemDefinition heldDefinition))
        {
            return false;
        }

        PlantGrowthInteractionType interactionType;
        PlantResource tree = null;
        if (Bucket.IsWaterBucketDefinition(heldDefinition))
        {
            interactionType = PlantGrowthInteractionType.Water;
            if (!playerController.TryFindNearestPlantWateringTarget(out _, out tree))
            {
                return false;
            }
        }
        else if (ItemDefinition.IsFertilizerEnergyItemDefinition(heldDefinition))
        {
            interactionType = PlantGrowthInteractionType.Fertilizer;
            if (!playerController.TryFindNearestPlantFertilizingTarget(out _, out tree))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        Sprite icon = ResolveInteractionIcon(
            heldDefinition,
            0,
            true);
        if (icon == null || InteractionButton == null)
        {
            return false;
        }

        ClearInteractionTargets();
        plantGrowthInteractionType = interactionType;
        currentPlantGrowthTarget = tree;
        SetActiveInteractionButton(InteractionButton, icon);
        return true;
    }

    private void HandlePlantGrowthInteraction(Player currentPlayer)
    {
        PlayerController playerController = currentPlayer != null
            ? currentPlayer.GetComponent<PlayerController>()
            : null;
        PlantResource expectedTarget = currentPlantGrowthTarget;
        if (playerController == null
            || expectedTarget == null
            || !TryResolveHeldItem(currentPlayer, out ItemDefinition heldDefinition))
        {
            return;
        }

        switch (plantGrowthInteractionType)
        {
            case PlantGrowthInteractionType.Water:
                if (playerController.TryFindNearestPlantWateringTarget(
                        out _,
                        out PlantResource waterTarget)
                    && waterTarget == expectedTarget
                    && Bucket.IsWaterBucketDefinition(heldDefinition))
                {
                    TryApplyGrowthWater(currentPlayer, waterTarget, heldDefinition);
                }
                break;

            case PlantGrowthInteractionType.Fertilizer:
                if (playerController.TryFindNearestPlantFertilizingTarget(
                        out _,
                        out PlantResource fertilizerTarget)
                    && fertilizerTarget == expectedTarget
                    && ItemDefinition.IsFertilizerEnergyItemDefinition(heldDefinition))
                {
                    TryApplyGrowthFertilizer(currentPlayer, fertilizerTarget, heldDefinition);
                }
                break;
        }
    }

    private static void TryApplyGrowthWater(
        Player currentPlayer,
        PlantResource targetTree,
        ItemDefinition waterBucket)
    {
        if (currentPlayer == null || targetTree == null || waterBucket == null)
        {
            return;
        }

        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (!Bucket.TryResolveEmptyBucketDefinition(
                itemManager,
                out ItemDefinition emptyBucket)
            || !currentPlayer.CanConvertHeldItem(
                waterBucket.id,
                emptyBucket.id))
        {
            return;
        }

        float waterLiters = Bucket.ResolveContainedFluidLiters(waterBucket);
        if (!targetTree.TryAddGrowthWater(waterLiters, out _))
        {
            return;
        }

        currentPlayer.TryConvertHeldItem(waterBucket.id, emptyBucket.id);
    }

    private void TryApplyGrowthFertilizer(
        Player currentPlayer,
        PlantResource targetTree,
        ItemDefinition fertilizer)
    {
        PlayerBag handBag = currentPlayer != null ? currentPlayer.GetHandBag() : null;
        if (targetTree == null
            || fertilizer == null
            || handBag == null
            || handBag.GetSlotItemId(0) != fertilizer.id)
        {
            return;
        }

        PortableObject sourcePortableObject = handBag.GetTopObject(0);
        Vector3 startPosition = sourcePortableObject != null
            ? sourcePortableObject.transform.position
            : currentPlayer.BodyTransform != null
                ? currentPlayer.BodyTransform.position
                : currentPlayer.transform.position;
        Quaternion startRotation = sourcePortableObject != null
            ? sourcePortableObject.transform.rotation
            : Quaternion.identity;
        Vector3 startScale = sourcePortableObject != null
            ? sourcePortableObject.transform.lossyScale
            : Vector3.one;
        if (!handBag.TryRemoveOneAtSlot(0, out int consumedItemId, false))
        {
            return;
        }

        if (consumedItemId != fertilizer.id
            || !targetTree.TryAddGrowthFertilizer(fertilizer.energyAmount, out _))
        {
            RestoreConsumedPlantGrowthItem(currentPlayer, consumedItemId);
            UpdateHandItemGauge();
            return;
        }

        PlayFertilizerPortableAnimation(
            sourcePortableObject,
            consumedItemId,
            startPosition,
            startRotation,
            startScale,
            targetTree.transform);
        UpdateHandItemGauge();
    }

    private static void PlayFertilizerPortableAnimation(
        PortableObject template,
        int itemId,
        Vector3 startPosition,
        Quaternion startRotation,
        Vector3 startScale,
        Transform target)
    {
        if (template == null || itemId < 0 || target == null)
        {
            return;
        }

        PortableObject movingPortableObject = Instantiate(
            template,
            startPosition,
            startRotation);
        if (movingPortableObject == null)
        {
            return;
        }

        movingPortableObject.name = $"{template.name}_FertilizerMove";
        movingPortableObject.transform.SetParent(null, true);
        movingPortableObject.transform.position = startPosition;
        movingPortableObject.transform.localScale = startScale;
        if (!movingPortableObject.gameObject.activeSelf)
        {
            movingPortableObject.gameObject.SetActive(true);
        }

        if (!movingPortableObject.SetItem(itemId))
        {
            PlayerItemStorageUtility.DestroyPortableObject(movingPortableObject);
            return;
        }

        movingPortableObject.MoveTo(
            target,
            0f,
            null,
            () => PlayerItemStorageUtility.DestroyPortableObject(movingPortableObject),
            false);
    }

    private static void RestoreConsumedPlantGrowthItem(
        Player currentPlayer,
        int consumedItemId)
    {
        if (currentPlayer == null
            || consumedItemId < 0
            || currentPlayer.TryAddToHand(consumedItemId, out _)
            || currentPlayer.TryAddToBag(consumedItemId, out _))
        {
            return;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        Transform dropOrigin = currentPlayer.BodyTransform != null
            ? currentPlayer.BodyTransform
            : currentPlayer.transform;
        terrain?.TryAddDroppedItemNear(dropOrigin.position, consumedItemId, out _);
    }
}
