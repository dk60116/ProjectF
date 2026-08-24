using System;
using System.Collections.Generic;
using UnityEngine;

public class FreightCar : Train, IPlayerItemStorage
{
    private const float MountedTankMotionPositionEpsilonSqr = 0.000001f;
    private const float MountedTankMotionRotationEpsilonDegrees = 0.05f;

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
    private readonly List<InstallationObject> boxPointLoads = new List<InstallationObject>();
    private readonly List<List<PortableObject>> boxPointItemStacks = new List<List<PortableObject>>();
    private bool hasLastSyncedLoadRuntimeCoordinate;
    private Vector2Int lastSyncedLoadRuntimeCoordinate;
    private bool hasMountedTankMotionSample;
    private Vector3 lastMountedTankMotionPosition;
    private Quaternion lastMountedTankMotionRotation;
    private float mountedTankCarrierStationarySeconds;

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
        bool smoothRotation,
        bool preserveSuppliedFacing = false)
    {
        if (rail == null || facingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        facingTangent.Normalize();
        Vector2 visualFacingTangent = preserveSuppliedFacing
            ? facingTangent
            : ResolveVisualFacingTangent(facingTangent);
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

    public bool CanAttachLoadObject(InstallationObject loadObject)
    {
        return IsSupportedLoadObject(loadObject)
               && (!(loadObject is Fluidtank) || !HasStoredItems());
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

    public bool TryGetClosestAttachedBoxObject(Vector3 referenceWorldPosition, out BoxObject boxObject)
    {
        boxObject = null;
        EnsureBoxPointBoxes();
        if (boxPointLoads.Count <= 0)
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;
        for (int i = 0; i < boxPointLoads.Count; i++)
        {
            if (!IsBoxPointStorageActive(i))
            {
                continue;
            }

            BoxObject candidate = boxPointLoads[i] as BoxObject;
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 offset = candidate.transform.position - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (boxObject != null && distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            boxObject = candidate;
        }

        return boxObject != null;
    }

    public void GetAutoDriveStorageSummary(
        out int storedItemCount,
        out int storageCapacity,
        out bool hasStorage)
    {
        storedItemCount = 0;
        storageCapacity = 0;
        hasStorage = false;
        if (TryGetAttachedFluidTank(out _))
        {
            return;
        }

        EnsureItemPointStacks();

        if (itemPointList != null)
        {
            for (int i = 0; i < itemPointList.Count; i++)
            {
                Transform itemPoint = itemPointList[i];
                if (itemPoint == null)
                {
                    continue;
                }

                hasStorage = true;
                List<PortableObject> stack = i < itemPointStacks.Count
                    ? itemPointStacks[i]
                    : null;
                CleanupItemStack(stack);
                int storedItemId = stack != null && stack.Count > 0 && stack[0] != null
                    ? stack[0].ItemId
                    : -1;
                storageCapacity += GetStackCapacityForItem(storedItemId);
                if (stack != null)
                {
                    storedItemCount += stack.Count;
                }
            }
        }

        if (boxPointList == null)
        {
            return;
        }

        for (int i = 0; i < boxPointList.Count; i++)
        {
            Transform boxPoint = boxPointList[i];
            if (boxPoint == null)
            {
                continue;
            }

            hasStorage = true;
            CleanupBoxPointSlot(i);
            BoxObject attachedBox = i < boxPointLoads.Count
                ? boxPointLoads[i] as BoxObject
                : null;
            if (attachedBox != null
                && attachedBox.gameObject.activeInHierarchy
                && attachedBox.TryGetObjectInfoItem(out _, out int itemCount, out int capacity))
            {
                storedItemCount += Mathf.Max(0, itemCount);
                storageCapacity += Mathf.Max(0, capacity);
            }
        }
    }

    public bool TryAttachBoxObject(BoxObject boxObject, Vector3 referenceWorldPosition)
    {
        return TryAttachLoadObject(boxObject, referenceWorldPosition);
    }

    public bool TryAttachLoadObject(
        InstallationObject loadObject,
        Vector3 referenceWorldPosition)
    {
        if (!CanAttachLoadObject(loadObject)
            || !TryGetBestAvailableBoxPoint(referenceWorldPosition, out Transform boxPoint))
        {
            return false;
        }

        return TryAttachLoadObjectToPoint(loadObject, boxPoint);
    }

    public bool TryAttachBoxObjectToPoint(BoxObject boxObject, Transform boxPoint)
    {
        return TryAttachLoadObjectToPoint(boxObject, boxPoint);
    }

    public bool TryAttachLoadObjectToPoint(
        InstallationObject loadObject,
        Transform boxPoint)
    {
        if (!CanAttachLoadObject(loadObject)
            || boxPoint == null
            || !TryGetBoxPointIndex(boxPoint, out int pointIndex)
            || IsBoxPointOccupiedByOther(pointIndex, loadObject))
        {
            return false;
        }

        boxPointLoads[pointIndex] = loadObject;
        loadObject.SetExcludeFromTerrainPersistence(true);
        loadObject.transform.SetParent(boxPoint, false);
        loadObject.transform.localPosition = GetAttachedLoadLocalPosition(loadObject);
        loadObject.transform.localRotation = Quaternion.identity;
        loadObject.transform.localScale = Vector3.one;
        loadObject.gameObject.SetActive(true);
        ApplyAttachedLoadPresentation(loadObject);
        SyncAttachedLoadRuntime(loadObject);
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
        return TryTakeOneItemInternal(
            referenceWorldPosition,
            itemFilter,
            out takenItemId,
            out pickupWorldPosition,
            out _,
            false);
    }

    public bool TryTakeOneItem(
        Vector3 referenceWorldPosition,
        Predicate<int> itemFilter,
        out int takenItemId,
        out Vector3 pickupWorldPosition,
        out PortableObject takenPortableObject)
    {
        return TryTakeOneItemInternal(
            referenceWorldPosition,
            itemFilter,
            out takenItemId,
            out pickupWorldPosition,
            out takenPortableObject,
            true);
    }

    private bool TryTakeOneItemInternal(
        Vector3 referenceWorldPosition,
        Predicate<int> itemFilter,
        out int takenItemId,
        out Vector3 pickupWorldPosition,
        out PortableObject takenPortableObject,
        bool releasePortableObject)
    {
        takenItemId = -1;
        takenPortableObject = null;
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
        if (releasePortableObject)
        {
            ReleaseTakenPortableObject(portableObject);
            takenPortableObject = portableObject;
        }
        else
        {
            PlayerItemStorageUtility.DestroyPortableObject(portableObject);
        }

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
        return TryPickupOneItem(
            player,
            playerPosition,
            pickupRange,
            preferredItemId,
            preferredSlotIndex,
            false);
    }

    public bool TryPickupOneItemToHand(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredItemId = -1)
    {
        return TryPickupOneItem(
            player,
            playerPosition,
            pickupRange,
            preferredItemId,
            -1,
            true);
    }

    private bool TryPickupOneItem(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredItemId,
        int preferredSlotIndex,
        bool handOnly)
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

        bool accepted = handOnly
            ? player.TryAddToHand(itemId, out PortableObject storageTarget)
            : PlayerItemStorageUtility.TryAddToPlayerStorage(
                player,
                itemId,
                preferredSlotIndex,
                out storageTarget);
        if (!accepted)
        {
            return false;
        }

        stack.RemoveAt(stack.Count - 1);
        PlayerItemStorageUtility.MoveVisualToPlayerStorage(portableObject, storageTarget);
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
        ResetMountedTankMotionTracking();
        ClearLoadedItems();
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        ResetMountedTankMotionTracking();
        ClearLoadedItems();
        ClearAttachedLoads();
        base.PrepareForPool();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        ResetMountedTankMotionTracking();
        ClearLoadedItems();
        ClearAttachedLoads();
        base.OnPlacementRuntimeCleared();
    }

    private void LateUpdate()
    {
        UpdateMountedTankMotionTracking(Time.deltaTime);
        SyncAttachedLoadsRuntime();
    }

    private void UpdateMountedTankMotionTracking(float deltaTime)
    {
        Vector3 currentPosition = transform.position;
        Quaternion currentRotation = transform.rotation;
        if (!hasMountedTankMotionSample)
        {
            hasMountedTankMotionSample = true;
            lastMountedTankMotionPosition = currentPosition;
            lastMountedTankMotionRotation = currentRotation;
            mountedTankCarrierStationarySeconds = 0f;
            return;
        }

        bool moved = (currentPosition - lastMountedTankMotionPosition).sqrMagnitude
                     > MountedTankMotionPositionEpsilonSqr
                     || Quaternion.Angle(currentRotation, lastMountedTankMotionRotation)
                     > MountedTankMotionRotationEpsilonDegrees;
        mountedTankCarrierStationarySeconds = moved
            ? 0f
            : mountedTankCarrierStationarySeconds + Mathf.Max(0f, deltaTime);
        lastMountedTankMotionPosition = currentPosition;
        lastMountedTankMotionRotation = currentRotation;
    }

    private void ResetMountedTankMotionTracking()
    {
        hasMountedTankMotionSample = false;
        lastMountedTankMotionPosition = Vector3.zero;
        lastMountedTankMotionRotation = Quaternion.identity;
        mountedTankCarrierStationarySeconds = 0f;
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
        if (TryGetAttachedFluidTank(out _))
        {
            return false;
        }

        EnsureItemPointStacks();

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

        int stackLimit = GetStackCapacityForItem(itemId);
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

    public int GetStackCapacityForItem(int itemId)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        return ItemDefinition.ResolveStackCapacity(
            itemManager,
            itemId,
            Mathf.Max(1, maxItemsPerPoint));
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
                    PlayerItemStorageUtility.DestroyPortableObject(portableObject);
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

        while (boxPointLoads.Count < boxPointList.Count)
        {
            boxPointLoads.Add(null);
        }

        while (boxPointItemStacks.Count < boxPointList.Count)
        {
            boxPointItemStacks.Add(new List<PortableObject>());
        }

        for (int i = boxPointLoads.Count - 1; i >= boxPointList.Count; i--)
        {
            boxPointLoads.RemoveAt(i);
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
        if (pointIndex < 0 || pointIndex >= boxPointLoads.Count)
        {
            return false;
        }

        BoxObject attachedBox = boxPointLoads[pointIndex] as BoxObject;
        return attachedBox != null && attachedBox.gameObject.activeInHierarchy;
    }

    public bool TryGetAttachedFluidTank(out Fluidtank fluidTank)
    {
        fluidTank = null;
        EnsureBoxPointBoxes();
        for (int i = 0; i < boxPointLoads.Count; i++)
        {
            CleanupBoxPointSlot(i);
            Fluidtank attachedTank = boxPointLoads[i] as Fluidtank;
            if (attachedTank != null && attachedTank.gameObject.activeInHierarchy)
            {
                fluidTank = attachedTank;
                return true;
            }
        }

        return false;
    }

    private bool HasStoredItems()
    {
        EnsureItemPointStacks();
        EnsureBoxPointBoxes();
        for (int i = 0; i < itemPointStacks.Count; i++)
        {
            List<PortableObject> stack = itemPointStacks[i];
            CleanupItemStack(stack);
            if (stack != null && stack.Count > 0)
            {
                return true;
            }
        }

        for (int i = 0; i < boxPointItemStacks.Count; i++)
        {
            List<PortableObject> stack = boxPointItemStacks[i];
            CleanupItemStack(stack);
            if (stack != null && stack.Count > 0)
            {
                return true;
            }
        }

        for (int i = 0; i < boxPointLoads.Count; i++)
        {
            CleanupBoxPointSlot(i);
            BoxObject attachedBox = boxPointLoads[i] as BoxObject;
            if (attachedBox != null
                && attachedBox.gameObject.activeInHierarchy
                && attachedBox.TryGetObjectInfoItem(out _, out int itemCount, out _)
                && itemCount > 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsBoxPointOccupiedByOther(
        int pointIndex,
        InstallationObject allowedLoadObject)
    {
        CleanupBoxPointSlot(pointIndex);
        if (pointIndex < 0 || pointIndex >= boxPointLoads.Count)
        {
            return true;
        }

        InstallationObject attachedLoad = boxPointLoads[pointIndex];
        return attachedLoad != null && attachedLoad != allowedLoadObject;
    }

    private void CleanupBoxPointSlot(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= boxPointLoads.Count)
        {
            return;
        }

        InstallationObject attachedLoad = boxPointLoads[pointIndex];
        if (attachedLoad != null
            && attachedLoad.gameObject.activeInHierarchy
            && attachedLoad.transform != null
            && boxPointList != null
            && pointIndex < boxPointList.Count
            && boxPointList[pointIndex] != null
            && attachedLoad.transform.IsChildOf(boxPointList[pointIndex]))
        {
            return;
        }

        Transform boxPoint = boxPointList != null && pointIndex < boxPointList.Count ? boxPointList[pointIndex] : null;
        boxPointLoads[pointIndex] = FindSupportedLoadObject(boxPoint);
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
            PlayerItemStorageUtility.DestroyPortableObject(portableObject);
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

    private static void ReleaseTakenPortableObject(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
        gate?.ClearGate();

        portableObject.CancelMove();
        portableObject.SetBatchedRendering(false);
        portableObject.transform.SetParent(null, true);
        if (!portableObject.gameObject.activeSelf)
        {
            portableObject.gameObject.SetActive(true);
        }
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

    private void SyncAttachedLoadsRuntime()
    {
        EnsureBoxPointBoxes();
        if (boxPointLoads.Count <= 0)
        {
            hasLastSyncedLoadRuntimeCoordinate = false;
            return;
        }

        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            return;
        }

        if (hasLastSyncedLoadRuntimeCoordinate && lastSyncedLoadRuntimeCoordinate == anchorCoordinate)
        {
            for (int i = 0; i < boxPointLoads.Count; i++)
            {
                if (boxPointLoads[i] == null)
                {
                    continue;
                }

                SyncAttachedLoadTransform(boxPointLoads[i], i);
            }

            return;
        }

        hasLastSyncedLoadRuntimeCoordinate = true;
        lastSyncedLoadRuntimeCoordinate = anchorCoordinate;
        for (int i = 0; i < boxPointLoads.Count; i++)
        {
            InstallationObject attachedLoad = boxPointLoads[i];
            if (attachedLoad == null)
            {
                continue;
            }

            SyncAttachedLoadTransform(attachedLoad, i);
            SyncAttachedLoadRuntime(attachedLoad);
        }
    }

    private void SyncAttachedLoadTransform(InstallationObject attachedLoad, int pointIndex)
    {
        Transform boxPoint = boxPointList != null && pointIndex >= 0 && pointIndex < boxPointList.Count
            ? boxPointList[pointIndex]
            : null;
        if (attachedLoad == null || boxPoint == null || attachedLoad.transform.parent != boxPoint)
        {
            return;
        }

        attachedLoad.transform.localPosition = GetAttachedLoadLocalPosition(attachedLoad);
        attachedLoad.transform.localRotation = Quaternion.identity;
        attachedLoad.transform.localScale = Vector3.one;
        ApplyAttachedLoadPresentation(attachedLoad);
    }

    private void SyncAttachedLoadRuntime(InstallationObject attachedLoad)
    {
        if (attachedLoad == null || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return;
        }

        attachedLoad.SetExcludeFromTerrainPersistence(true);
        attachedLoad.ConfigurePlacementRuntime(
            anchorCoordinate,
            quarterTurns,
            new[] { anchorCoordinate },
            attachedLoad.RuntimePlacementSequence);
    }

    private void ClearAttachedLoads()
    {
        for (int i = 0; i < boxPointItemStacks.Count; i++)
        {
            ClearItemStack(boxPointItemStacks[i]);
        }

        boxPointItemStacks.Clear();

        for (int i = 0; i < boxPointLoads.Count; i++)
        {
            DestroyAttachedLoadObject(boxPointLoads[i]);
        }

        boxPointLoads.Clear();
        hasLastSyncedLoadRuntimeCoordinate = false;
    }

    private void ClearItemStack(List<PortableObject> stack)
    {
        if (stack == null)
        {
            return;
        }

        for (int i = stack.Count - 1; i >= 0; i--)
        {
            PlayerItemStorageUtility.DestroyPortableObject(stack[i]);
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

    private static bool IsSupportedLoadObject(InstallationObject loadObject)
    {
        return loadObject is BoxObject || loadObject is Fluidtank;
    }

    private static Vector3 GetAttachedLoadLocalPosition(InstallationObject loadObject)
    {
        return loadObject is Fluidtank fluidTank
            ? fluidTank.FlatCarMountedLocalPosition
            : Vector3.zero;
    }

    private void ApplyAttachedLoadPresentation(InstallationObject loadObject)
    {
        if (loadObject is Fluidtank fluidTank)
        {
            fluidTank.SetFlatCarMountedPresentation(true);
            fluidTank.UpdateFlatCarMountedPipeVisuals(
                Time.deltaTime,
                mountedTankCarrierStationarySeconds);
        }
    }

    private static InstallationObject FindSupportedLoadObject(Transform boxPoint)
    {
        if (boxPoint == null)
        {
            return null;
        }

        BoxObject attachedBox = boxPoint.GetComponentInChildren<BoxObject>(true);
        if (attachedBox != null)
        {
            return attachedBox;
        }

        return boxPoint.GetComponentInChildren<Fluidtank>(true);
    }

    private static void DestroyAttachedLoadObject(InstallationObject loadObject)
    {
        if (loadObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(loadObject.gameObject);
            return;
        }

        DestroyImmediate(loadObject.gameObject);
    }
}
