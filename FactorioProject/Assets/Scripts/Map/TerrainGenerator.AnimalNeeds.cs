using System;
using System.Collections.Generic;
using UnityEngine;

public partial class TerrainGenerator
{
    private static readonly Predicate<int> AnimalFoodItemFilter =
        IsAnimalFoodItemId;
    private readonly Dictionary<Vector2Int, AnimalAIController> animalFoodConsumers =
        new Dictionary<Vector2Int, AnimalAIController>();

    public bool TryFindNearestDroppedAnimalFood(
        Vector3 worldPosition,
        float searchRadius,
        out Vector2Int foodCoordinate,
        out Vector3 foodWorldPosition,
        bool requireLoadedGround,
        Func<Vector3, bool, bool> canReach)
    {
        foodCoordinate = default;
        foodWorldPosition = worldPosition;
        int coordinateRadius = Mathf.CeilToInt(Mathf.Max(0.5f, searchRadius));
        float maximumDistanceSqr = searchRadius * searchRadius;
        float closestDistanceSqr = float.PositiveInfinity;
        Vector2Int center = GetWorldBlockCoordinate(worldPosition);
        bool found = false;

        for (int offsetY = -coordinateRadius; offsetY <= coordinateRadius; offsetY++)
        {
            for (int offsetX = -coordinateRadius; offsetX <= coordinateRadius; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (!TryGetLoadedBlock(coordinate, out Block block)
                    || block == null
                    || block.Type != Block.BlockType.Ground
                    || !block.TryGetClosestAnimalFoodWorldPosition(
                        worldPosition,
                        AnimalFoodItemFilter,
                        out Vector3 candidatePosition)
                    || IsAnimalFoodBeingConsumed(coordinate))
                {
                    continue;
                }

                Vector3 offset = candidatePosition - worldPosition;
                offset.y = 0f;
                float distanceSqr = offset.sqrMagnitude;
                if (distanceSqr > maximumDistanceSqr
                    || distanceSqr >= closestDistanceSqr
                    || !canReach(candidatePosition, requireLoadedGround))
                {
                    continue;
                }

                closestDistanceSqr = distanceSqr;
                foodCoordinate = coordinate;
                foodWorldPosition = candidatePosition;
                found = true;
            }
        }

        return found;
    }

    public bool TryConsumeDroppedAnimalFood(
        Vector2Int coordinate,
        Vector3 foodWorldPosition,
        AnimalAIController consumer,
        out ItemDefinition foodDefinition)
    {
        foodDefinition = null;
        if (consumer == null || IsAnimalFoodBeingConsumed(coordinate)
            || !TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !block.TryTakeAnimalFood(
                foodWorldPosition,
                AnimalFoodItemFilter,
                out int itemId))
        {
            return false;
        }

        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        bool consumed = itemManager != null
               && itemManager.TryGetItemDefinitionById(itemId, out foodDefinition)
               && ItemDefinition.IsFoodEnergyItemDefinition(foodDefinition);
        if (consumed)
        {
            animalFoodConsumers[coordinate] = consumer;
        }

        return consumed;
    }

    private bool IsAnimalFoodBeingConsumed(Vector2Int coordinate)
    {
        if (!animalFoodConsumers.TryGetValue(coordinate, out AnimalAIController consumer))
        {
            return false;
        }

        if (consumer != null && consumer.IsConsumingDroppedFood)
        {
            return true;
        }

        animalFoodConsumers.Remove(coordinate);
        return false;
    }

    public void ReleaseAnimalFoodConsumption(Vector2Int coordinate, AnimalAIController consumer)
    {
        if (animalFoodConsumers.TryGetValue(coordinate, out AnimalAIController owner) && owner == consumer)
        {
            animalFoodConsumers.Remove(coordinate);
        }
    }

    public int DropAnimalDefecation(
        Vector3 worldPosition,
        int itemId,
        int amount,
        bool interactedAnimal,
        float unattendedLifetimeSeconds)
    {
        if (itemId < 0 || amount <= 0)
        {
            return 0;
        }

        int dropped = 0;
        Vector2Int center = GetWorldBlockCoordinate(worldPosition);
        for (int itemIndex = 0; itemIndex < amount; itemIndex++)
        {
            if (!TryDropAnimalDefecationItem(
                    center,
                    itemId,
                    out Block targetBlock,
                    out PortableObject portableObject))
            {
                break;
            }

            dropped++;
            if (portableObject == null)
            {
                continue;
            }

            AnimalTemporaryDropping temporaryDropping =
                portableObject.GetComponent<AnimalTemporaryDropping>();
            if (!interactedAnimal && unattendedLifetimeSeconds > 0f)
            {
                if (temporaryDropping == null)
                {
                    temporaryDropping = portableObject.gameObject
                        .AddComponent<AnimalTemporaryDropping>();
                }

                temporaryDropping.SetExpiration(
                    targetBlock,
                    portableObject,
                    unattendedLifetimeSeconds);
            }
            else
            {
                temporaryDropping?.ClearExpiration();
            }
        }

        return dropped;
    }

    private bool TryDropAnimalDefecationItem(
        Vector2Int center,
        int itemId,
        out Block targetBlock,
        out PortableObject portableObject)
    {
        const int maximumSearchRadius = 2;
        targetBlock = null;
        portableObject = null;

        // 빈 칸을 먼저 사용하고, 없을 때만 기존 호환 스택에 자연스럽게 합친다.
        for (int pass = 0; pass < 2; pass++)
        {
            bool requireEmpty = pass == 0;
            for (int radius = 0; radius <= maximumSearchRadius; radius++)
            {
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    for (int offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        if (radius > 0
                            && Mathf.Abs(offsetX) != radius
                            && Mathf.Abs(offsetY) != radius)
                        {
                            continue;
                        }

                        Vector2Int coordinate = center
                                                + new Vector2Int(offsetX, offsetY);
                        if (!TryGetLoadedBlock(coordinate, out Block block)
                            || block == null
                            || block.Type != Block.BlockType.Ground
                            || requireEmpty
                            && !IsFarmlandAt(coordinate)
                            && block.HasDroppedFloorObjects)
                        {
                            continue;
                        }

                        if (block.TryAddFloorObject(itemId, out portableObject))
                        {
                            targetBlock = block;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool IsAnimalFoodItemId(int itemId)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        return itemManager != null
               && itemManager.TryGetItemDefinitionById(
                   itemId,
                   out ItemDefinition definition)
               && ItemDefinition.IsFoodEnergyItemDefinition(definition)
               && definition.energyAmount > 0f;
    }
}

[DisallowMultipleComponent]
public sealed class AnimalTemporaryDropping : MonoBehaviour
{
    private Block owningBlock;
    private PortableObject portableObject;
    private float timeRemaining;
    private bool expirationActive;
    public bool IsTemporary => expirationActive;

    public void SetExpiration(
        Block block,
        PortableObject targetPortableObject,
        float lifetimeSeconds)
    {
        owningBlock = block;
        portableObject = targetPortableObject;
        timeRemaining = Mathf.Max(0f, lifetimeSeconds);
        expirationActive = owningBlock != null
                           && portableObject != null
                           && timeRemaining > 0f;
    }

    public void ClearExpiration()
    {
        expirationActive = false;
        owningBlock = null;
        portableObject = null;
        timeRemaining = 0f;
    }

    private void Update()
    {
        if (!expirationActive)
        {
            return;
        }

        timeRemaining -= Time.deltaTime;
        if (timeRemaining > 0f)
        {
            return;
        }

        Block block = owningBlock;
        PortableObject target = portableObject;
        ClearExpiration();
        block?.TryRemoveFloorObject(target);
    }

    private void OnDisable()
    {
        ClearExpiration();
    }
}
