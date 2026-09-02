using System.Collections.Generic;
using UnityEngine;
using PlantResource = ProjectF.MapObjects.Tree;

public partial class PlayerController
{
    public bool TryFindNearestPlantWateringTarget(
        out Block targetBlock,
        out PlantResource targetTree)
    {
        return TryFindNearestPlantGrowthTarget(
            out targetBlock,
            out targetTree);
    }

    private bool TryFindNearestPlantGrowthTarget(
        out Block targetBlock,
        out PlantResource targetTree)
    {
        targetBlock = null;
        targetTree = null;
        if (player == null
            || IsMounted
            || !TryResolveHeldWaterBucket(out _))
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null
            ? player.BodyTransform.position
            : transform.position;
        float interactionRange = player.State.HarvestRange;
        float maximumDistanceSqr = interactionRange * interactionRange;
        float nearestDistanceSqr = float.MaxValue;
        IReadOnlyList<Resource> resources = Resource.ActiveResources;
        bool usingNearbyCandidates = TryCollectNearbyResourceCandidates(
            origin,
            interactionRange,
            out IReadOnlyList<Resource> nearbyResources);
        if (usingNearbyCandidates)
        {
            resources = nearbyResources;
        }

        for (int i = 0; i < resources.Count; i++)
        {
            if (!(resources[i] is PlantResource tree)
                || !tree.gameObject.activeInHierarchy
                || !tree.CanAcceptGrowthWater)
            {
                continue;
            }

            Block owningBlock = ResolveResourceOwningBlock(tree);
            if (owningBlock == null)
            {
                continue;
            }

            Vector3 offset = tree.FocusPoint - origin;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > maximumDistanceSqr
                || distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            targetBlock = owningBlock;
            targetTree = tree;
        }

        if (usingNearbyCandidates)
        {
            nearbyResourceCandidates.Clear();
        }

        return targetTree != null;
    }

    private bool TryResolveHeldWaterBucket(out ItemDefinition waterBucket)
    {
        return TryResolveHeldPlantGrowthItem(out waterBucket)
               && Bucket.IsWaterBucketDefinition(waterBucket);
    }

    private bool TryResolveHeldPlantGrowthItem(out ItemDefinition heldDefinition)
    {
        heldDefinition = null;
        PlayerBag handBag = player != null ? player.GetHandBag() : null;
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        return handBag != null
               && handBag.GetSlotCount(0) > 0
               && itemManager != null
               && itemManager.TryGetItemDefinitionById(
                   handBag.GetSlotItemId(0),
                   out heldDefinition);
    }
}
