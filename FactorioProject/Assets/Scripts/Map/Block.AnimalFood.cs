using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Block
{
    public bool TryGetClosestAnimalFoodWorldPosition(
        Vector3 referencePosition, Predicate<int> itemFilter, out Vector3 worldPosition)
    {
        bool found = TryFindAnimalFoodStack(referencePosition, itemFilter, out _, out PortableObject food);
        worldPosition = found ? food.transform.position : WorldPosition;
        return found;
    }

    public bool TryTakeAnimalFood(
        Vector3 foodPosition, Predicate<int> itemFilter, out int itemId)
    {
        itemId = -1;
        if (!TryFindAnimalFoodStack(foodPosition, itemFilter, out List<PortableObject> stack, out PortableObject food))
        {
            return false;
        }

        // The selected pile may have been taken by an arm while the animal approached.
        // Do not consume a different nearby pile without approaching and facing it first.
        Vector3 offset = food.transform.position - foodPosition;
        offset.y = 0f;
        if (offset.sqrMagnitude > 0.0025f)
        {
            return false;
        }

        itemId = food.ItemId;
        stack.RemoveAt(stack.Count - 1);
        ReleaseFloorObject(food);
        NotifyRuntimeItemStackChanged();
        return true;
    }

    private bool TryFindAnimalFoodStack(
        Vector3 referencePosition, Predicate<int> itemFilter,
        out List<PortableObject> bestStack, out PortableObject bestFood)
    {
        bestStack = null;
        bestFood = null;
        EnsureFloorObjectsInitialized();
        float bestDistanceSqr = float.PositiveInfinity;
        // Arms place ground deliveries in the central stack, separate from dropped piles.
        // Container contents and hidden installation storage are not ground food.
        bool includeCenter = inputAreaCenterObjectsVisible && !BoxObject.IsRuntimeContentBlock(this);
        int stackCount = floorStacks.Count + (includeCenter ? 1 : 0);
        for (int i = 0; i < stackCount; i++)
        {
            List<PortableObject> stack = i < floorStacks.Count ? floorStacks[i] : inputAreaCenterStack;
            PortableObject food = GetTopPortableObject(stack);
            if (food == null || food.ItemId < 0 || (itemFilter != null && !itemFilter(food.ItemId)))
            {
                continue;
            }

            DroppedItemPickupGate gate = food.GetComponent<DroppedItemPickupGate>();
            if (gate != null && !gate.CanManualPickup(0f, float.MaxValue))
            {
                continue;
            }

            Vector3 offset = food.transform.position - referencePosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestStack = stack;
            bestFood = food;
            bestDistanceSqr = distanceSqr;
        }

        return bestFood != null;
    }
}
