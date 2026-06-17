using System;
using System.Collections.Generic;
using UnityEngine;

public class FreightCar : Train
{
    [SerializeField, Min(0.01f)]
    private float railRotationInterpolationSpeed = 10f;
    [SerializeField]
    private List<Transform> itemPointList;
    [SerializeField]
    private List<Transform> boxPointList;
    [SerializeField]
    private PortableObject itemObjectPrefab;
    [SerializeField, Min(1)]
    private int maxItemsPerPoint = 10;
    [SerializeField, Min(0.001f)]
    private float itemStackVerticalSpacing = 0.05f;

    private readonly List<List<PortableObject>> itemPointStacks = new List<List<PortableObject>>();
    private readonly List<BoxObject> boxPointBoxes = new List<BoxObject>();
    private readonly List<List<PortableObject>> boxPointItemStacks = new List<List<PortableObject>>();
    private bool hasLastSyncedBoxRuntimeCoordinate;
    private Vector2Int lastSyncedBoxRuntimeCoordinate;

    public override void ApplyPlacedRailSample(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        if (rail != null
            && rail.TrySampleRenderedPath(distanceAlongPath, out Vector2 sampledPoint, out Vector2 railTangent)
            && railTangent.sqrMagnitude > 0.0001f)
        {
            base.ApplyPlacedRailSample(
                rail,
                distanceAlongPath,
                sampledPoint,
                railTangent);
            return;
        }

        base.ApplyPlacedRailSample(rail, distanceAlongPath, railPoint, facingTangent);
    }

    public bool TryApplyRailPose(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent,
        float deltaTime,
        bool smoothRotation)
    {
        if (rail == null || facingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        facingTangent.Normalize();
        Vector2 visualFacingTangent = ResolveVisualFacingTangent(facingTangent);
        Quaternion targetRotation = Quaternion.LookRotation(
            new Vector3(visualFacingTangent.x, 0f, visualFacingTangent.y),
            Vector3.up);
        Quaternion rotation = targetRotation;
        if (smoothRotation && deltaTime > 0f)
        {
            float interpolation = 1f - Mathf.Exp(
                -Mathf.Max(0.01f, railRotationInterpolationSpeed) * deltaTime);
            rotation = Quaternion.Slerp(transform.rotation, targetRotation, interpolation);
        }

        return ApplyRailPoseToRail(
            rail,
            distanceAlongPath,
            railPoint,
            facingTangent,
            rotation);
    }

    private Vector2 ResolveVisualFacingTangent(Vector2 targetFacingTangent)
    {
        if (targetFacingTangent.sqrMagnitude <= 0.0001f)
        {
            return Vector2.up;
        }

        targetFacingTangent.Normalize();
        Vector2 currentForward = new Vector2(transform.forward.x, transform.forward.z);
        if (currentForward.sqrMagnitude <= 0.0001f)
        {
            return targetFacingTangent;
        }

        currentForward.Normalize();
        return Vector2.Dot(currentForward, targetFacingTangent) < 0f
            ? -targetFacingTangent
            : targetFacingTangent;
    }

    public bool TryAddItemStack(
        int itemId,
        int itemCount,
        Vector3 startWorldPosition,
        Func<Vector3> startWorldPositionProvider,
        float moveInterval,
        out int addedCount)
    {
        addedCount = 0;
        if (itemId < 0
            || itemCount <= 0
            || InputOutputModule.IsFluidItemId(itemId))
        {
            return false;
        }

        float interval = Mathf.Max(0f, moveInterval);
        for (int i = 0; i < itemCount; i++)
        {
            if (!TryAddItem(
                    itemId,
                    startWorldPosition,
                    startWorldPositionProvider,
                    i * interval,
                    out _))
            {
                break;
            }

            addedCount++;
        }

        return addedCount > 0;
    }

    public bool CanAddItem(int itemId, Vector3 referenceWorldPosition)
    {
        return itemId >= 0
               && !InputOutputModule.IsFluidItemId(itemId)
               && TryGetBestItemStorageStack(itemId, referenceWorldPosition, out _, out _);
    }

    public bool CanAttachBoxObject(Vector3 referenceWorldPosition)
    {
        return TryGetBestAvailableBoxPoint(referenceWorldPosition, out _);
    }

    public bool TryGetBestAvailableBoxPoint(Vector3 referenceWorldPosition, out Transform boxPoint)
    {
        return TryGetBestAvailableBoxPoint(referenceWorldPosition, null, out boxPoint);
    }

    public bool TryGetBestAvailableBoxPoint(
        Vector3 referenceWorldPosition,
        Predicate<Transform> pointFilter,
        out Transform boxPoint)
    {
        boxPoint = null;
        EnsureBoxPointBoxes();
        if (boxPointList == null || boxPointList.Count <= 0)
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;
        for (int i = 0; i < boxPointList.Count; i++)
        {
            Transform candidatePoint = boxPointList[i];
            if (candidatePoint == null
                || !candidatePoint.gameObject.activeInHierarchy
                || IsBoxPointOccupied(i)
                || (pointFilter != null && !pointFilter(candidatePoint)))
            {
                continue;
            }

            Vector3 offset = candidatePoint.position - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (boxPoint != null && distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            boxPoint = candidatePoint;
        }

        return boxPoint != null;
    }

    public bool IsBoxPointAvailable(Transform boxPoint)
    {
        return TryGetBoxPointIndex(boxPoint, out int pointIndex)
               && !IsBoxPointOccupied(pointIndex);
    }

    public bool TryAttachBoxObject(BoxObject boxObject, Vector3 referenceWorldPosition)
    {
        if (boxObject == null
            || !TryGetBestAvailableBoxPoint(referenceWorldPosition, out Transform boxPoint))
        {
            return false;
        }

        return TryAttachBoxObjectToPoint(boxObject, boxPoint);
    }

    public bool TryAttachBoxObjectToPoint(BoxObject boxObject, Transform boxPoint)
    {
        if (boxObject == null
            || boxPoint == null
            || !TryGetBoxPointIndex(boxPoint, out int pointIndex)
            || IsBoxPointOccupiedByOther(pointIndex, boxObject))
        {
            return false;
        }

        boxPointBoxes[pointIndex] = boxObject;
        boxObject.SetExcludeFromTerrainPersistence(true);
        boxObject.transform.SetParent(boxPoint, false);
        boxObject.transform.localPosition = Vector3.zero;
        boxObject.transform.localRotation = Quaternion.identity;
        boxObject.transform.localScale = Vector3.one;
        boxObject.gameObject.SetActive(true);
        SyncAttachedBoxRuntime(boxObject);
        return true;
    }

    public bool TryGetTopItem(
        Vector3 referenceWorldPosition,
        Predicate<int> itemFilter,
        out int itemId,
        out Vector3 worldPosition)
    {
        itemId = -1;
        return TryFindClosestTopItem(referenceWorldPosition, itemFilter, out _, out _, out itemId, out worldPosition);
    }

    public bool TryTakeOneItem(
        Vector3 referenceWorldPosition,
        Predicate<int> itemFilter,
        out int takenItemId,
        out Vector3 pickupWorldPosition)
    {
        takenItemId = -1;
        if (!TryFindClosestTopItem(
                referenceWorldPosition,
                itemFilter,
                out List<PortableObject> stack,
                out PortableObject portableObject,
                out int itemId,
                out pickupWorldPosition)
            || portableObject == null
            || stack == null)
        {
            return false;
        }

        if (stack.Count <= 0 || stack[stack.Count - 1] != portableObject)
        {
            return false;
        }

        stack.RemoveAt(stack.Count - 1);
        DestroyPortableObject(portableObject);
        NotifyRobotArmsAtRuntimeCoordinates();
        takenItemId = itemId;
        return takenItemId >= 0;
    }

    public bool TryPickupOneItemToBag(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredSlotIndex,
        int preferredItemId = -1)
    {
        if (player == null || pickupRange <= 0f)
        {
            return false;
        }

        if (!TryFindClosestManualPickupItem(
                player,
                playerPosition,
                pickupRange,
                preferredItemId,
                out List<PortableObject> stack,
                out PortableObject portableObject,
                out int itemId,
                out _))
        {
            return false;
        }

        if (!TryAddPickupObjectToPlayerStorage(
                player,
                itemId,
                preferredSlotIndex,
                out PortableObject storageTarget,
                out _))
        {
            return false;
        }

        stack.RemoveAt(stack.Count - 1);
        ReleasePickupObjectToPlayerStorage(portableObject, storageTarget);
        NotifyRobotArmsAtRuntimeCoordinates();
        return true;
    }

    public bool TryPreviewPickupItems(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredItemId,
        out int previewItemId,
        out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 0;
        if (player == null || pickupRange <= 0f)
        {
            return false;
        }

        if (!TryFindClosestManualPickupItem(
                player,
                playerPosition,
                pickupRange,
                preferredItemId,
                out List<PortableObject> stack,
                out _,
                out int itemId,
                out float distanceSqr))
        {
            return false;
        }

        float pickupRadiusSqr = pickupRange * pickupRange;
        int pickupCount = CountManualPickupStackObjectsFromTop(
            stack,
            itemId,
            distanceSqr,
            pickupRadiusSqr);
        if (pickupCount <= 0)
        {
            return false;
        }

        previewItemId = itemId;
        previewPickupCount = pickupCount;
        return true;
    }

    protected override void OnDisable()
    {
        ClearLoadedItems();
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        ClearLoadedItems();
        ClearAttachedBoxes();
        base.PrepareForPool();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        ClearLoadedItems();
        ClearAttachedBoxes();
        base.OnPlacementRuntimeCleared();
    }

    private void LateUpdate()
    {
        SyncAttachedBoxesRuntime();
    }

    private bool TryAddItem(
        int itemId,
        Vector3 startWorldPosition,
        Func<Vector3> startWorldPositionProvider,
        float delay,
        out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (!TryGetBestItemStorageStack(itemId, startWorldPosition, out Transform itemPoint, out List<PortableObject> stack)
            || itemPoint == null
            || stack == null)
        {
            return false;
        }

        PortableObject portableObject = CreateItemPortableObject(itemId);
        if (portableObject == null)
        {
            return false;
        }

        portableObject.SetBatchedRendering(false);
        portableObject.transform.SetParent(itemPoint, true);
        portableObject.transform.position = startWorldPositionProvider != null
            ? startWorldPositionProvider()
            : startWorldPosition;
        portableObject.transform.rotation = itemPoint.rotation;
        portableObject.transform.localScale = Vector3.one;
        portableObject.gameObject.SetActive(true);

        int objectIndex = stack.Count;
        Vector3 finalLocalPosition = new Vector3(0f, objectIndex * Mathf.Max(0.001f, itemStackVerticalSpacing), 0f);
        Vector3 finalWorldPosition = itemPoint.TransformPoint(finalLocalPosition);
        stack.Add(portableObject);
        NotifyRobotArmsAtRuntimeCoordinates();
        targetPortableObject = portableObject;

        portableObject.MoveTo(
            () => itemPoint != null ? itemPoint.TransformPoint(finalLocalPosition) : finalWorldPosition,
            delay,
            startWorldPositionProvider,
            () =>
            {
                if (portableObject == null || itemPoint == null)
                {
                    return;
                }

                portableObject.transform.SetParent(itemPoint, false);
                portableObject.transform.localPosition = finalLocalPosition;
                portableObject.transform.localRotation = Quaternion.identity;
                portableObject.transform.localScale = Vector3.one;
                portableObject.gameObject.SetActive(true);
                portableObject.SetBatchedRendering(false);
                portableObject.GetOrAddPickupGate()?.MarkSettled();
            },
            false,
            true,
            PortableObject.MoveToDuration,
            false);

        return true;
    }

    private bool TryGetBestItemStorageStack(
        int itemId,
        Vector3 referenceWorldPosition,
        out Transform itemPoint,
        out List<PortableObject> stack)
    {
        itemPoint = null;
        stack = null;
        EnsureItemPointStacks();
        EnsureBoxPointBoxes();

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireExistingStack = pass == 0;
            float bestDistanceSqr = float.MaxValue;
            TrySelectBestItemStorageStack(
                itemPointList,
                itemPointStacks,
                null,
                itemId,
                referenceWorldPosition,
                requireExistingStack,
                ref bestDistanceSqr,
                ref itemPoint,
                ref stack);
            TrySelectBestItemStorageStack(
                boxPointList,
                boxPointItemStacks,
                IsBoxPointStorageActive,
                itemId,
                referenceWorldPosition,
                requireExistingStack,
                ref bestDistanceSqr,
                ref itemPoint,
                ref stack);

            if (itemPoint != null)
            {
                return true;
            }
        }

        return false;
    }

    private void TrySelectBestItemStorageStack(
        List<Transform> pointList,
        List<List<PortableObject>> stackList,
        Predicate<int> pointIndexFilter,
        int itemId,
        Vector3 referenceWorldPosition,
        bool requireExistingStack,
        ref float bestDistanceSqr,
        ref Transform bestPoint,
        ref List<PortableObject> bestStack)
    {
        if (pointList == null || stackList == null)
        {
            return;
        }

        int stackLimit = Mathf.Max(1, maxItemsPerPoint);
        for (int i = 0; i < pointList.Count; i++)
        {
            Transform candidatePoint = pointList[i];
            List<PortableObject> candidateStack = i < stackList.Count ? stackList[i] : null;
            CleanupItemStack(candidateStack);
            if (candidatePoint == null
                || !candidatePoint.gameObject.activeInHierarchy
                || candidateStack == null
                || candidateStack.Count >= stackLimit
                || !IsStackCompatible(candidateStack, itemId)
                || (requireExistingStack && candidateStack.Count <= 0)
                || (!requireExistingStack && candidateStack.Count > 0)
                || (pointIndexFilter != null && !pointIndexFilter(i)))
            {
                continue;
            }

            Vector3 offset = candidatePoint.position - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (bestPoint != null && distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            bestPoint = candidatePoint;
            bestStack = candidateStack;
        }
    }

    private bool TryFindClosestTopItem(
        Vector3 referenceWorldPosition,
        Predicate<int> itemFilter,
        out List<PortableObject> bestStack,
        out PortableObject bestPortableObject,
        out int bestItemId,
        out Vector3 bestWorldPosition)
    {
        bestStack = null;
        bestPortableObject = null;
        bestItemId = -1;
        bestWorldPosition = transform.position;
        EnsureItemPointStacks();
        EnsureBoxPointBoxes();

        float bestDistanceSqr = float.MaxValue;
        TryFindClosestTopItemInStacks(
            itemPointStacks,
            null,
            referenceWorldPosition,
            itemFilter,
            ref bestDistanceSqr,
            ref bestStack,
            ref bestPortableObject,
            ref bestItemId,
            ref bestWorldPosition);
        TryFindClosestTopItemInStacks(
            boxPointItemStacks,
            IsBoxPointStorageActive,
            referenceWorldPosition,
            itemFilter,
            ref bestDistanceSqr,
            ref bestStack,
            ref bestPortableObject,
            ref bestItemId,
            ref bestWorldPosition);

        return bestStack != null;
    }

    private void TryFindClosestTopItemInStacks(
        List<List<PortableObject>> stackList,
        Predicate<int> stackIndexFilter,
        Vector3 referenceWorldPosition,
        Predicate<int> itemFilter,
        ref float bestDistanceSqr,
        ref List<PortableObject> bestStack,
        ref PortableObject bestPortableObject,
        ref int bestItemId,
        ref Vector3 bestWorldPosition)
    {
        if (stackList == null)
        {
            return;
        }

        for (int stackIndex = 0; stackIndex < stackList.Count; stackIndex++)
        {
            if (stackIndexFilter != null && !stackIndexFilter(stackIndex))
            {
                continue;
            }

            List<PortableObject> stack = stackList[stackIndex];
            CleanupItemStack(stack);
            while (stack != null && stack.Count > 0)
            {
                PortableObject portableObject = stack[stack.Count - 1];
                if (portableObject == null)
                {
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                int itemId = portableObject.ItemId;
                if (itemId < 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                    DestroyPortableObject(portableObject);
                    continue;
                }

                if (itemFilter != null && !itemFilter(itemId))
                {
                    break;
                }

                Vector3 worldPosition = portableObject.transform.position;
                Vector3 offset = worldPosition - referenceWorldPosition;
                offset.y = 0f;
                float distanceSqr = offset.sqrMagnitude;
                if (bestStack != null && distanceSqr >= bestDistanceSqr)
                {
                    break;
                }

                bestStack = stack;
                bestPortableObject = portableObject;
                bestItemId = itemId;
                bestWorldPosition = worldPosition;
                bestDistanceSqr = distanceSqr;
                break;
            }
        }
    }

    private bool TryFindClosestManualPickupItem(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredItemId,
        out List<PortableObject> bestStack,
        out PortableObject bestPortableObject,
        out int bestItemId,
        out float bestDistanceSqr)
    {
        bestStack = null;
        bestPortableObject = null;
        bestItemId = -1;
        bestDistanceSqr = float.MaxValue;
        if (player == null || pickupRange <= 0f)
        {
            return false;
        }

        EnsureItemPointStacks();
        EnsureBoxPointBoxes();

        Vector3 gateOriginPosition = player.transform.position;
        float pickupRadiusSqr = pickupRange * pickupRange;
        TryFindClosestManualPickupItemInStacks(
            itemPointStacks,
            null,
            playerPosition,
            gateOriginPosition,
            pickupRadiusSqr,
            preferredItemId,
            ref bestStack,
            ref bestPortableObject,
            ref bestItemId,
            ref bestDistanceSqr);
        TryFindClosestManualPickupItemInStacks(
            boxPointItemStacks,
            IsBoxPointStorageActive,
            playerPosition,
            gateOriginPosition,
            pickupRadiusSqr,
            preferredItemId,
            ref bestStack,
            ref bestPortableObject,
            ref bestItemId,
            ref bestDistanceSqr);

        return bestStack != null && bestPortableObject != null && bestItemId >= 0;
    }

    private void TryFindClosestManualPickupItemInStacks(
        List<List<PortableObject>> stackList,
        Predicate<int> stackIndexFilter,
        Vector3 playerPosition,
        Vector3 gateOriginPosition,
        float pickupRadiusSqr,
        int preferredItemId,
        ref List<PortableObject> bestStack,
        ref PortableObject bestPortableObject,
        ref int bestItemId,
        ref float bestDistanceSqr)
    {
        if (stackList == null)
        {
            return;
        }

        for (int stackIndex = 0; stackIndex < stackList.Count; stackIndex++)
        {
            if (stackIndexFilter != null && !stackIndexFilter(stackIndex))
            {
                continue;
            }

            List<PortableObject> stack = stackList[stackIndex];
            CleanupItemStack(stack);
            if (stack == null || stack.Count <= 0)
            {
                continue;
            }

            PortableObject portableObject = stack[stack.Count - 1];
            if (portableObject == null)
            {
                continue;
            }

            int itemId = portableObject.ItemId;
            if (itemId < 0 || (preferredItemId >= 0 && itemId != preferredItemId))
            {
                continue;
            }

            Vector3 offset = portableObject.transform.position - playerPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > pickupRadiusSqr
                || (bestStack != null && distanceSqr >= bestDistanceSqr))
            {
                continue;
            }

            DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
            gate?.UpdateExitState(gateOriginPosition);
            if (gate != null && !gate.CanManualPickup(distanceSqr, pickupRadiusSqr))
            {
                continue;
            }

            bestStack = stack;
            bestPortableObject = portableObject;
            bestItemId = itemId;
            bestDistanceSqr = distanceSqr;
        }
    }

    private void EnsureItemPointStacks()
    {
        if (itemPointList == null)
        {
            itemPointList = new List<Transform>();
        }

        if (!HasUsableItemPoint())
        {
            itemPointList.Clear();
            Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < childTransforms.Length; i++)
            {
                Transform child = childTransforms[i];
                if (child == null
                    || child == transform
                    || string.IsNullOrEmpty(child.name)
                    || !child.name.StartsWith("ItemPoint", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                itemPointList.Add(child);
            }
        }

        while (itemPointStacks.Count < itemPointList.Count)
        {
            itemPointStacks.Add(new List<PortableObject>());
        }

        for (int i = itemPointStacks.Count - 1; i >= itemPointList.Count; i--)
        {
            ClearItemStack(itemPointStacks[i]);
            itemPointStacks.RemoveAt(i);
        }
    }

    private void EnsureBoxPointBoxes()
    {
        if (boxPointList == null)
        {
            boxPointList = new List<Transform>();
        }

        if (!HasUsableBoxPoint())
        {
            boxPointList.Clear();
            Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < childTransforms.Length; i++)
            {
                Transform child = childTransforms[i];
                if (child == null
                    || child == transform
                    || string.IsNullOrEmpty(child.name)
                    || !child.name.StartsWith("BoxPoint", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                boxPointList.Add(child);
            }
        }

        while (boxPointBoxes.Count < boxPointList.Count)
        {
            boxPointBoxes.Add(null);
        }

        while (boxPointItemStacks.Count < boxPointList.Count)
        {
            boxPointItemStacks.Add(new List<PortableObject>());
        }

        for (int i = boxPointBoxes.Count - 1; i >= boxPointList.Count; i--)
        {
            boxPointBoxes.RemoveAt(i);
        }

        for (int i = boxPointItemStacks.Count - 1; i >= boxPointList.Count; i--)
        {
            ClearItemStack(boxPointItemStacks[i]);
            boxPointItemStacks.RemoveAt(i);
        }

        for (int i = 0; i < boxPointList.Count; i++)
        {
            CleanupBoxPointSlot(i);
            if (i >= boxPointItemStacks.Count)
            {
                continue;
            }

            if (!IsBoxPointStorageActive(i))
            {
                ClearItemStack(boxPointItemStacks[i]);
                continue;
            }

            CleanupItemStack(boxPointItemStacks[i]);
        }
    }

    private bool HasUsableItemPoint()
    {
        if (itemPointList == null)
        {
            return false;
        }

        for (int i = 0; i < itemPointList.Count; i++)
        {
            Transform itemPoint = itemPointList[i];
            if (itemPoint != null)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasUsableBoxPoint()
    {
        if (boxPointList == null)
        {
            return false;
        }

        for (int i = 0; i < boxPointList.Count; i++)
        {
            Transform boxPoint = boxPointList[i];
            if (boxPoint != null)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetBoxPointIndex(Transform boxPoint, out int pointIndex)
    {
        pointIndex = -1;
        if (boxPoint == null)
        {
            return false;
        }

        EnsureBoxPointBoxes();
        for (int i = 0; i < boxPointList.Count; i++)
        {
            if (boxPointList[i] == boxPoint)
            {
                pointIndex = i;
                return true;
            }
        }

        return false;
    }

    private bool IsBoxPointOccupied(int pointIndex)
    {
        return IsBoxPointOccupiedByOther(pointIndex, null);
    }

    private bool IsBoxPointStorageActive(int pointIndex)
    {
        CleanupBoxPointSlot(pointIndex);
        if (pointIndex < 0 || pointIndex >= boxPointBoxes.Count)
        {
            return false;
        }

        BoxObject attachedBox = boxPointBoxes[pointIndex];
        return attachedBox != null && attachedBox.gameObject.activeInHierarchy;
    }

    private bool IsBoxPointOccupiedByOther(int pointIndex, BoxObject allowedBoxObject)
    {
        CleanupBoxPointSlot(pointIndex);
        if (pointIndex < 0 || pointIndex >= boxPointBoxes.Count)
        {
            return true;
        }

        BoxObject attachedBox = boxPointBoxes[pointIndex];
        return attachedBox != null && attachedBox != allowedBoxObject;
    }

    private void CleanupBoxPointSlot(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= boxPointBoxes.Count)
        {
            return;
        }

        BoxObject attachedBox = boxPointBoxes[pointIndex];
        if (attachedBox != null
            && attachedBox.gameObject.activeInHierarchy
            && attachedBox.transform != null
            && boxPointList != null
            && pointIndex < boxPointList.Count
            && boxPointList[pointIndex] != null
            && attachedBox.transform.IsChildOf(boxPointList[pointIndex]))
        {
            return;
        }

        Transform boxPoint = boxPointList != null && pointIndex < boxPointList.Count ? boxPointList[pointIndex] : null;
        BoxObject existingChildBox = boxPoint != null
            ? boxPoint.GetComponentInChildren<BoxObject>(true)
            : null;
        boxPointBoxes[pointIndex] = existingChildBox;
    }

    private PortableObject CreateItemPortableObject(int itemId)
    {
        PortableObject portableObject = itemObjectPrefab != null
            ? Instantiate(itemObjectPrefab)
            : CreateGeneratedPortableObject(itemId);
        if (portableObject == null)
        {
            return null;
        }

        portableObject.gameObject.layer = gameObject.layer;
        if (!portableObject.SetItem(itemId))
        {
            DestroyPortableObject(portableObject);
            return null;
        }

        return portableObject;
    }

    private PortableObject CreateGeneratedPortableObject(int itemId)
    {
        GameObject itemObject = new GameObject($"FreightCarItem_{itemId}");
        itemObject.transform.SetParent(transform, false);
        itemObject.AddComponent<MeshFilter>();
        itemObject.AddComponent<MeshRenderer>();
        return itemObject.AddComponent<PortableObject>();
    }

    private static bool IsStackCompatible(List<PortableObject> stack, int itemId)
    {
        if (stack == null || stack.Count <= 0)
        {
            return true;
        }

        for (int i = 0; i < stack.Count; i++)
        {
            PortableObject portableObject = stack[i];
            if (portableObject == null)
            {
                continue;
            }

            return portableObject.ItemId == itemId;
        }

        return true;
    }

    private static void CleanupItemStack(List<PortableObject> stack)
    {
        if (stack == null)
        {
            return;
        }

        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i] == null)
            {
                stack.RemoveAt(i);
            }
        }
    }

    private static int CountManualPickupStackObjectsFromTop(
        List<PortableObject> stack,
        int itemId,
        float distanceSqr,
        float pickupRadiusSqr)
    {
        if (stack == null || itemId < 0)
        {
            return 0;
        }

        int count = 0;
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            PortableObject portableObject = stack[i];
            if (portableObject == null)
            {
                continue;
            }

            if (portableObject.ItemId != itemId)
            {
                break;
            }

            DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
            if (gate != null && !gate.CanManualPickup(distanceSqr, pickupRadiusSqr))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static bool TryAddPickupObjectToPlayerStorage(
        Player player,
        int itemId,
        int preferredSlotIndex,
        out PortableObject targetPortableObject,
        out bool addedToHand)
    {
        targetPortableObject = null;
        addedToHand = false;
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
                addedToHand = true;
                return true;
            }

            return false;
        }

        if (player.HasMatchingHandStackSpace(itemId)
            && player.TryAddToHand(itemId, out targetPortableObject))
        {
            addedToHand = true;
            return true;
        }

        return player.TryAddToBag(itemId, out targetPortableObject);
    }

    private static void ReleasePickupObjectToPlayerStorage(
        PortableObject portableObject,
        PortableObject storageTarget)
    {
        if (portableObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
        gate?.ClearGate();

        portableObject.SetBatchedRendering(false);
        portableObject.transform.SetParent(null, true);
        if (storageTarget != null)
        {
            portableObject.MoveTo(storageTarget.transform, () => DestroyPortableObject(portableObject));
            return;
        }

        DestroyPortableObject(portableObject);
    }

    private void ClearLoadedItems()
    {
        for (int i = 0; i < itemPointStacks.Count; i++)
        {
            ClearItemStack(itemPointStacks[i]);
        }

        itemPointStacks.Clear();

        for (int i = 0; i < boxPointItemStacks.Count; i++)
        {
            ClearItemStack(boxPointItemStacks[i]);
        }

        boxPointItemStacks.Clear();
    }

    private void SyncAttachedBoxesRuntime()
    {
        EnsureBoxPointBoxes();
        if (boxPointBoxes.Count <= 0)
        {
            hasLastSyncedBoxRuntimeCoordinate = false;
            return;
        }

        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            return;
        }

        if (hasLastSyncedBoxRuntimeCoordinate && lastSyncedBoxRuntimeCoordinate == anchorCoordinate)
        {
            for (int i = 0; i < boxPointBoxes.Count; i++)
            {
                if (boxPointBoxes[i] == null)
                {
                    continue;
                }

                SyncAttachedBoxTransform(boxPointBoxes[i], i);
            }

            return;
        }

        hasLastSyncedBoxRuntimeCoordinate = true;
        lastSyncedBoxRuntimeCoordinate = anchorCoordinate;
        for (int i = 0; i < boxPointBoxes.Count; i++)
        {
            BoxObject attachedBox = boxPointBoxes[i];
            if (attachedBox == null)
            {
                continue;
            }

            SyncAttachedBoxTransform(attachedBox, i);
            SyncAttachedBoxRuntime(attachedBox);
        }
    }

    private void SyncAttachedBoxTransform(BoxObject attachedBox, int pointIndex)
    {
        Transform boxPoint = boxPointList != null && pointIndex >= 0 && pointIndex < boxPointList.Count
            ? boxPointList[pointIndex]
            : null;
        if (attachedBox == null || boxPoint == null || attachedBox.transform.parent != boxPoint)
        {
            return;
        }

        attachedBox.transform.localPosition = Vector3.zero;
        attachedBox.transform.localRotation = Quaternion.identity;
        attachedBox.transform.localScale = Vector3.one;
    }

    private void SyncAttachedBoxRuntime(BoxObject attachedBox)
    {
        if (attachedBox == null || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return;
        }

        attachedBox.SetExcludeFromTerrainPersistence(true);
        attachedBox.ConfigurePlacementRuntime(
            anchorCoordinate,
            quarterTurns,
            new[] { anchorCoordinate },
            attachedBox.RuntimePlacementSequence);
    }

    private void ClearAttachedBoxes()
    {
        for (int i = 0; i < boxPointItemStacks.Count; i++)
        {
            ClearItemStack(boxPointItemStacks[i]);
        }

        boxPointItemStacks.Clear();

        for (int i = 0; i < boxPointBoxes.Count; i++)
        {
            DestroyAttachedBoxObject(boxPointBoxes[i]);
        }

        boxPointBoxes.Clear();
        hasLastSyncedBoxRuntimeCoordinate = false;
    }

    private void ClearItemStack(List<PortableObject> stack)
    {
        if (stack == null)
        {
            return;
        }

        for (int i = stack.Count - 1; i >= 0; i--)
        {
            DestroyPortableObject(stack[i]);
        }

        stack.Clear();
    }

    private void NotifyRobotArmsAtRuntimeCoordinates()
    {
        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null || occupiedCoordinates.Count <= 0)
        {
            if (TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
            {
                RobotArm.WakeAroundCoordinate(anchorCoordinate);
            }

            return;
        }

        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            RobotArm.WakeAroundCoordinate(occupiedCoordinates[i]);
        }
    }

    private static void DestroyPortableObject(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        portableObject.CancelMove();
        if (Application.isPlaying)
        {
            Destroy(portableObject.gameObject);
            return;
        }

        DestroyImmediate(portableObject.gameObject);
    }

    private static void DestroyAttachedBoxObject(BoxObject boxObject)
    {
        if (boxObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(boxObject.gameObject);
            return;
        }

        DestroyImmediate(boxObject.gameObject);
    }
}
