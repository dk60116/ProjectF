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
            || handBag.GetSlotItemId(0) != fertilizer.id
            || !handBag.TryRemoveOneAtSlot(0, out int consumedItemId, false))
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

        UpdateHandItemGauge();
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
