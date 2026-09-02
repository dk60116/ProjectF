using UnityEngine;
using PlantResource = ProjectF.MapObjects.Tree;

public partial class PlayerHUD
{
    private enum PlantGrowthInteractionType
    {
        None,
        Water
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

        if (!Bucket.IsWaterBucketDefinition(heldDefinition)
            || !playerController.TryFindNearestPlantWateringTarget(out _, out PlantResource tree))
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
        plantGrowthInteractionType = PlantGrowthInteractionType.Water;
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

        if (plantGrowthInteractionType == PlantGrowthInteractionType.Water
            && playerController.TryFindNearestPlantWateringTarget(
                out _,
                out PlantResource waterTarget)
            && waterTarget == expectedTarget
            && Bucket.IsWaterBucketDefinition(heldDefinition))
        {
            TryApplyGrowthWater(currentPlayer, waterTarget, heldDefinition);
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

}
