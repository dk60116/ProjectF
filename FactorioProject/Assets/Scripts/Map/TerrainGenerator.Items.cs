using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class TerrainGenerator : MonoBehaviour
{
    private static Player GetActivePlayer()
    {
        return GameManager.Instance != null ? GameManager.Instance.Player : null;
    }

    public bool TryAddDroppedItemAtPlayerBlock(Vector3 worldPosition, int itemId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);

        if (TryGetFocusedConveyorBeltBlock(GetActivePlayer(), out _, out Block focusedConveyorBlock))
        {
            if (!TryAddDroppedItemToFocusedConveyor(
                    worldPosition,
                    focusedConveyorBlock,
                    itemId,
                    worldPosition,
                    0f,
                    out targetPortableObject,
                    out Block targetConveyorBlock))
            {
                return false;
            }

            MarkDroppedPickupGate(targetPortableObject, true, worldPosition);
            targetConveyorBlock?.MarkConveyorDroppedItem(worldPosition, true);
            return true;
        }

        if (TryResolveFocusedGroundBoxDropBlock(worldPosition, itemId, 1, out Block focusedBoxBlock)
            && focusedBoxBlock.TryAddInputAreaCenterObjectAnimated(itemId, worldPosition, 0f, out targetPortableObject))
        {
            MarkInputAreaDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

        if (TryResolveInputAreaDropBlock(centerCoordinate, itemId, 1, out Block inputAreaBlock)
            && inputAreaBlock.TryAddInputAreaCenterObjectAnimated(itemId, worldPosition, 0f, out targetPortableObject))
        {
            MarkInputAreaDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

        Block targetBlock = FindPreferredDropBlock(worldPosition, itemId, 1);
        if (targetBlock != null && targetBlock.TryAddFloorObject(itemId, out targetPortableObject))
        {
            MarkDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

        return false;
    }

    public bool TryGetFocusedConveyorDropLimit(out int dropLimit)
    {
        dropLimit = 0;
        Player player = GetActivePlayer();
        if (!TryGetFocusedConveyorBeltBlock(player, out _, out Block focusedConveyorBlock)
            || focusedConveyorBlock == null)
        {
            return false;
        }

        int focusedCapacity = Mathf.Max(0, focusedConveyorBlock.GetAvailableConveyorCapacity());
        int connectedCapacity = GetDirectlyConnectedConveyorDropCapacity(
            focusedConveyorBlock,
            Mathf.Max(0, Block.ConveyorCellItemUnit - focusedCapacity));
        dropLimit = Mathf.Min(Block.ConveyorCellItemUnit, focusedCapacity + connectedCapacity);
        return true;
    }

    public bool TryResolveDroppedItemStackTargetBlockAtPlayerBlock(
        Vector3 worldPosition,
        int itemId,
        int itemCount,
        out Block targetBlock,
        out Vector2Int dropCoordinate)
    {
        targetBlock = null;
        dropCoordinate = GetWorldBlockCoordinate(worldPosition);
        if (itemId < 0 || itemCount <= 0)
        {
            return false;
        }

        if (TryGetFocusedConveyorBeltBlock(GetActivePlayer(), out _, out Block focusedConveyorBlock))
        {
            if (!TryResolveFocusedConveyorDropBlock(focusedConveyorBlock, null, out targetBlock)
                || targetBlock == null)
            {
                return false;
            }

            dropCoordinate = targetBlock.Coordinate;
            return true;
        }

        if (TryResolveFocusedGroundBoxDropBlock(worldPosition, itemId, 1, out targetBlock)
            && targetBlock != null)
        {
            dropCoordinate = targetBlock.Coordinate;
            return true;
        }

        if (TryResolveInputAreaDropBlock(dropCoordinate, itemId, 1, out targetBlock)
            && targetBlock != null)
        {
            dropCoordinate = targetBlock.Coordinate;
            return true;
        }

        targetBlock = FindPreferredDropBlock(worldPosition, itemId, itemCount);
        if (targetBlock == null)
        {
            return false;
        }

        dropCoordinate = targetBlock.Coordinate;
        return true;
    }

    private bool TryResolveFocusedConveyorDropBlock(
        Block focusedConveyorBlock,
        HashSet<BlockHandle> excludedHandles,
        out Block targetConveyorBlock)
    {
        targetConveyorBlock = null;
        if (focusedConveyorBlock == null)
        {
            return false;
        }

        BlockHandle focusedHandle = focusedConveyorBlock.RuntimeHandle;
        if ((excludedHandles == null || !excludedHandles.Contains(focusedHandle))
            && focusedConveyorBlock.GetAvailableConveyorCapacity() > 0)
        {
            targetConveyorBlock = focusedConveyorBlock;
            return true;
        }

        return TryFindDirectlyConnectedConveyorDropBlock(
            focusedConveyorBlock,
            excludedHandles,
            out targetConveyorBlock);
    }

    private int GetDirectlyConnectedConveyorDropCapacity(Block focusedConveyorBlock, int maxCapacity)
    {
        if (focusedConveyorBlock == null || maxCapacity <= 0)
        {
            return 0;
        }

        int totalCapacity = 0;
        conveyorDropBlockScratch.Clear();

        if (TryGetDirectlyConnectedConveyorDropCandidate(
                focusedConveyorBlock,
                true,
                conveyorDropBlockScratch,
                out Block downstreamBlock,
                out int downstreamCapacity))
        {
            totalCapacity += downstreamCapacity;
            if (totalCapacity >= maxCapacity)
            {
                conveyorDropBlockScratch.Clear();
                return maxCapacity;
            }

            BlockHandle downstreamHandle = downstreamBlock.RuntimeHandle;
            if (downstreamHandle.IsValid)
            {
                conveyorDropBlockScratch.Add(downstreamHandle);
            }
        }

        if (TryGetDirectlyConnectedConveyorDropCandidate(
                focusedConveyorBlock,
                false,
                conveyorDropBlockScratch,
                out _,
                out int upstreamCapacity))
        {
            totalCapacity += upstreamCapacity;
        }

        conveyorDropBlockScratch.Clear();
        return Mathf.Min(totalCapacity, maxCapacity);
    }

    private bool TryFindDirectlyConnectedConveyorDropBlock(
        Block focusedConveyorBlock,
        HashSet<BlockHandle> excludedHandles,
        out Block targetConveyorBlock)
    {
        targetConveyorBlock = null;

        if (TryGetDirectlyConnectedConveyorDropCandidate(
                focusedConveyorBlock,
                true,
                excludedHandles,
                out targetConveyorBlock,
                out _))
        {
            return true;
        }

        return TryGetDirectlyConnectedConveyorDropCandidate(
            focusedConveyorBlock,
            false,
            excludedHandles,
            out targetConveyorBlock,
            out _);
    }

    private static bool TryGetDirectlyConnectedConveyorDropCandidate(
        Block focusedConveyorBlock,
        bool downstream,
        HashSet<BlockHandle> excludedHandles,
        out Block candidateBlock,
        out int capacity)
    {
        candidateBlock = null;
        capacity = 0;
        if (focusedConveyorBlock == null)
        {
            return false;
        }

        bool hasCandidate = downstream
            ? focusedConveyorBlock.TryGetRuntimeNextConveyorBlock(out candidateBlock)
            : focusedConveyorBlock.TryGetRuntimePreviousConveyorBlock(out candidateBlock);
        if (!hasCandidate
            || candidateBlock == null
            || candidateBlock == focusedConveyorBlock
            || !candidateBlock.IsRuntimeConveyor
            || (excludedHandles != null && excludedHandles.Contains(candidateBlock.RuntimeHandle)))
        {
            candidateBlock = null;
            return false;
        }

        capacity = candidateBlock.GetAvailableConveyorCapacity();
        if (capacity <= 0)
        {
            candidateBlock = null;
            return false;
        }

        return true;
    }

    private bool TryAddDroppedItemToFocusedConveyor(
        Vector3 worldPosition,
        Block focusedConveyorBlock,
        int itemId,
        Vector3 startWorldPosition,
        float delay,
        out PortableObject targetPortableObject,
        out Block targetConveyorBlock,
        Action onComplete = null,
        Func<Vector3> startWorldPositionProvider = null,
        float movementReleaseDelay = 0f)
    {
        targetPortableObject = null;
        targetConveyorBlock = null;
        conveyorDropBlockScratch.Clear();
        while (TryResolveFocusedConveyorDropBlock(
                   focusedConveyorBlock,
                   conveyorDropBlockScratch,
                   out Block candidateBlock))
        {
            if (candidateBlock.TryAddConveyorObjectAnimatedAtPlacement(
                    itemId,
                    worldPosition,
                    startWorldPosition,
                    delay,
                    out targetPortableObject,
                    onComplete,
                    startWorldPositionProvider,
                    movementReleaseDelay))
            {
                targetConveyorBlock = candidateBlock;
                conveyorDropBlockScratch.Clear();
                return true;
            }

            BlockHandle candidateHandle = candidateBlock.RuntimeHandle;
            if (!candidateHandle.IsValid || !conveyorDropBlockScratch.Add(candidateHandle))
            {
                break;
            }
        }

        conveyorDropBlockScratch.Clear();
        return false;
    }

    public bool TryAddDroppedItemStackAtPlayerBlock(
        Vector3 worldPosition,
        int itemId,
        int itemCount,
        Vector3 startWorldPosition,
        Func<Vector3> startWorldPositionProvider,
        float moveInterval,
        out Vector2Int dropCoordinate,
        out int droppedCount)
    {
        dropCoordinate = GetWorldBlockCoordinate(worldPosition);
        droppedCount = 0;
        if (itemCount <= 0)
        {
            return false;
        }

        Player activePlayer = GetActivePlayer();
        if (TryGetFocusedConveyorBeltBlock(activePlayer, out _, out Block focusedConveyorBlock))
        {
            dropCoordinate = focusedConveyorBlock.Coordinate;
            int acceptedCount = Mathf.Min(itemCount, Block.ConveyorCellItemUnit);
            float moveIntervalSeconds = Mathf.Max(0f, moveInterval);
            float conveyorBatchReleaseDelay = acceptedCount > 1
                ? ((acceptedCount - 1) * moveIntervalSeconds) + PortableObject.MoveToDuration
                : 0f;
            for (int i = 0; i < acceptedCount; i++)
            {
                if (!TryAddDroppedItemToFocusedConveyor(
                        worldPosition,
                        focusedConveyorBlock,
                        itemId,
                        startWorldPosition,
                        i * moveIntervalSeconds,
                        out PortableObject droppedObject,
                        out Block targetConveyorBlock,
                        null,
                        startWorldPositionProvider,
                        conveyorBatchReleaseDelay))
                {
                    break;
                }

                droppedCount++;
                dropCoordinate = targetConveyorBlock.Coordinate;
                MarkDroppedPickupGate(droppedObject, false, worldPosition);
                targetConveyorBlock.MarkConveyorDroppedItem(worldPosition, false);
            }

            return droppedCount > 0;
        }

        if (TryGetFocusedBoxObject(activePlayer, out BoxObject focusedBoxObject))
        {
            if (focusedBoxObject == null
                || !TryResolveFocusedGroundBoxDropBlock(worldPosition, itemId, 1, out Block focusedBoxBlock))
            {
                return false;
            }

            dropCoordinate = focusedBoxBlock.Coordinate;
            for (int i = 0; i < itemCount; i++)
            {
                if (!focusedBoxBlock.TryAddInputAreaCenterObjectAnimated(
                        itemId,
                        startWorldPosition,
                        i * Mathf.Max(0f, moveInterval),
                        out PortableObject droppedObject,
                        null,
                        startWorldPositionProvider))
                {
                    return droppedCount > 0;
                }

                droppedCount++;
                MarkInputAreaDroppedPickupGate(droppedObject, false, worldPosition);
            }

            return true;
        }

        if (TryResolveInputAreaDropBlock(dropCoordinate, itemId, 1, out Block inputAreaBlock))
        {
            dropCoordinate = inputAreaBlock.Coordinate;
            for (int i = 0; i < itemCount; i++)
            {
                if (!inputAreaBlock.TryAddInputAreaCenterObjectAnimated(
                        itemId,
                        startWorldPosition,
                        i * Mathf.Max(0f, moveInterval),
                        out PortableObject droppedObject,
                        null,
                        startWorldPositionProvider))
                {
                    return droppedCount > 0;
                }

                droppedCount++;
                MarkInputAreaDroppedPickupGate(droppedObject, false, worldPosition);
            }

            return true;
        }

        Block targetBlock = FindPreferredDropBlock(worldPosition, itemId, itemCount);
        if (targetBlock == null)
        {
            return false;
        }

        dropCoordinate = targetBlock.Coordinate;
        for (int i = 0; i < itemCount; i++)
        {
            if (!targetBlock.TryAddFloorObjectAnimated(itemId, startWorldPosition, i * Mathf.Max(0f, moveInterval), out PortableObject droppedObject, null, startWorldPositionProvider))
            {
                return droppedCount > 0;
            }

            droppedCount++;
            MarkDroppedPickupGate(droppedObject, false, worldPosition);
        }

        return true;
    }

    public bool TryAddDroppedItemAnimated(
        Vector3 worldPosition,
        int itemId,
        Vector3 startWorldPosition,
        out PortableObject targetPortableObject,
        Action onComplete = null)
    {
        targetPortableObject = null;
        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);
        if (TryGetFocusedConveyorBeltBlock(GetActivePlayer(), out _, out Block focusedConveyorBlock))
        {
            if (!TryAddDroppedItemToFocusedConveyor(
                    worldPosition,
                    focusedConveyorBlock,
                    itemId,
                    startWorldPosition,
                    0f,
                    out targetPortableObject,
                    out Block targetConveyorBlock,
                    onComplete))
            {
                return false;
            }

            MarkDroppedPickupGate(targetPortableObject, false, worldPosition);
            targetConveyorBlock?.MarkConveyorDroppedItem(worldPosition, false);
            return true;
        }

        if (TryResolveFocusedGroundBoxDropBlock(worldPosition, itemId, 1, out Block focusedBoxBlock))
        {
            if (!focusedBoxBlock.TryAddInputAreaCenterObjectAnimated(itemId, startWorldPosition, 0f, out targetPortableObject, onComplete))
            {
                return false;
            }

            MarkInputAreaDroppedPickupGate(targetPortableObject, false, worldPosition);
            return true;
        }

        if (TryResolveInputAreaDropBlock(centerCoordinate, itemId, 1, out Block inputAreaBlock))
        {
            if (!inputAreaBlock.TryAddInputAreaCenterObjectAnimated(itemId, startWorldPosition, 0f, out targetPortableObject, onComplete))
            {
                return false;
            }

            MarkInputAreaDroppedPickupGate(targetPortableObject, false, worldPosition);
            return true;
        }

        Block targetBlock = FindPreferredDropBlock(worldPosition, itemId, 1);
        if (targetBlock == null)
        {
            return false;
        }

        if (!targetBlock.TryAddFloorObjectAnimated(itemId, startWorldPosition, 0f, out targetPortableObject, onComplete))
        {
            return false;
        }

        MarkDroppedPickupGate(targetPortableObject, false, worldPosition);
        return true;
    }

    public bool TryPickupOneItemToHandAtCoordinate(Player player, Vector2Int coordinate)
    {
        return TryPickupOneItemToHandAtCoordinate(player, coordinate, true);
    }

    public bool TryPickupOneItemToHandAtCoordinate(Player player, Vector2Int coordinate, bool allowFocusedConveyorPickup)
    {
        if (player == null)
        {
            return false;
        }

        return TryPickupOneItemToHandAtCoordinate(
            player,
            coordinate,
            player.transform.position,
            999f,
            allowFocusedConveyorPickup);
    }

    public bool TryPickupOneItemToHandAtCoordinate(Player player, Vector2Int coordinate, Vector3 pickupOrigin, float pickupRadius, bool allowFocusedConveyorPickup)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        if (allowFocusedConveyorPickup
            && TryGetFocusedConveyorBeltBlock(player, out _, out Block focusedConveyorBlock)
            && focusedConveyorBlock != null
            && focusedConveyorBlock.TryPickupOneConveyorObjectToHand(player, pickupOrigin, pickupRadius))
        {
            return true;
        }

        if (TryGetFocusedBoxObject(player, out BoxObject focusedBoxObject)
            && focusedBoxObject != null
            && focusedBoxObject.TryPickupContainedObjectToHand(player, pickupOrigin, pickupRadius))
        {
            return true;
        }

        if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
        {
            loadedBlocks.Remove(coordinate);
            return false;
        }

        if (block.Type != Block.BlockType.Ground)
        {
            return false;
        }

        return block.TryPickupOneFloorObjectToHand(player, pickupOrigin, pickupRadius);
    }

    public bool TryPickupOneItemToBagAtCoordinate(Player player, Vector2Int coordinate)
    {
        return TryPickupOneItemToBagAtCoordinate(player, coordinate, -1);
    }

    public bool TryPickupOneItemToBagAtCoordinate(Player player, Vector2Int coordinate, int preferredSlotIndex)
    {
        return TryPickupOneItemToBagAtCoordinate(player, coordinate, preferredSlotIndex, -1);
    }

    public bool TryPickupOneItemToBagAtCoordinate(Player player, Vector2Int coordinate, int preferredSlotIndex, int preferredItemId)
    {
        return TryPickupOneItemToBagAtCoordinate(player, coordinate, preferredSlotIndex, preferredItemId, true);
    }

    public bool TryPickupOneItemToBagAtCoordinate(Player player, Vector2Int coordinate, int preferredSlotIndex, int preferredItemId, bool allowFocusedConveyorPickup)
    {
        if (player == null)
        {
            return false;
        }

        return TryPickupOneItemToBagAtCoordinate(
            player,
            coordinate,
            player.transform.position,
            999f,
            preferredSlotIndex,
            preferredItemId,
            allowFocusedConveyorPickup);
    }

    public bool TryPickupOneItemToBagAtCoordinate(Player player, Vector2Int coordinate, Vector3 pickupOrigin, float pickupRadius, int preferredSlotIndex, int preferredItemId, bool allowFocusedConveyorPickup)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        if (allowFocusedConveyorPickup
            && TryGetFocusedConveyorBeltBlock(player, out _, out Block focusedConveyorBlock)
            && focusedConveyorBlock != null
            && focusedConveyorBlock.TryPickupOneConveyorObjectToBag(player, pickupOrigin, pickupRadius, preferredSlotIndex, preferredItemId))
        {
            return true;
        }

        if (TryGetFocusedBoxObject(player, out BoxObject focusedBoxObject)
            && focusedBoxObject != null
            && focusedBoxObject.TryPickupContainedObjectToBag(player, pickupOrigin, pickupRadius, preferredSlotIndex, preferredItemId))
        {
            return true;
        }

        if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
        {
            loadedBlocks.Remove(coordinate);
            return false;
        }

        if (block.Type != Block.BlockType.Ground)
        {
            return false;
        }

        return block.TryPickupOneFloorObjectToBag(player, pickupOrigin, pickupRadius, preferredSlotIndex, preferredItemId);
    }

    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block)
    {
        if (loadedBlocks.TryGetValue(coordinate, out block))
        {
            if (block == null)
            {
                loadedBlocks.Remove(coordinate);
            }
            else
            {
                return true;
            }
        }

        return TryMaterializeBlockRuntimeProxy(coordinate, out block);
    }

    public bool TryGetLoadedBlockRuntimeProxy(Vector2Int coordinate, out Block block)
    {
        return loadedBlocks.TryGetValue(coordinate, out block);
    }

    public int LoadedBlockDataCellCount => loadedBlocks.RegisteredCellCount;

    public int LoadedBlockRuntimeProxyCount => loadedBlocks.Count;

    public int LoadedBlockSimulationStateCount => loadedBlocks.RuntimeSimulationStateCount;

    internal bool TryGetOrCreateBlockRuntimeSimulationState(
        BlockHandle handle,
        out BlockRuntimeSimulationState state)
    {
        return loadedBlocks.TryGetOrCreateRuntimeSimulationState(handle, out state);
    }

    public int LoadedDedicatedBlockGameObjectCount => 0;

    public int LoadedBlockHostGameObjectCount => loadedBlocks.Count > 0 ? 1 : 0;

    public int LoadedBlockDataOnlyCellCount =>
        Mathf.Max(0, loadedBlocks.RegisteredCellCount - loadedBlocks.Count);

    public int LoadedChunkGameObjectCount => 0;

    public int LoadedChunkSurfaceMeshCount => GetLoadedChunkSurfaceMeshCount();

    public bool TryGetLoadedBlockHandle(Vector2Int coordinate, out BlockHandle handle)
    {
        return loadedBlocks.TryGetHandle(coordinate, out handle);
    }

    public bool TryGetLoadedBlockCellData(Vector2Int coordinate, out BlockCellData cellData)
    {
        return loadedBlocks.TryGetCell(coordinate, out cellData);
    }

    public bool TryGetLoadedBlockCellData(BlockHandle handle, out BlockCellData cellData)
    {
        return loadedBlocks.TryGetCell(handle, out cellData);
    }

    public bool TryResolveLoadedBlock(BlockHandle handle, out Block block)
    {
        return loadedBlocks.TryGetValue(handle, out block);
    }

    public bool IsConveyorItemCoordinateVirtualized(Vector2Int coordinate)
    {
        return false;
    }

    public bool IsFloorObjectCoordinateVirtualized(Vector2Int coordinate)
    {
        return false;
    }

    public bool HasDroppedFloorObjectsAt(Vector2Int coordinate)
    {
        if (loadedBlocks.TryGetValue(coordinate, out Block block)
            && block != null
            && block.HasDroppedFloorObjects)
        {
            return true;
        }

        return false;
    }

    public void RegisterLiveInstallationObject(InstallationObject installationObject)
    {
        if (installationObject == null || installationObject.ExcludeFromTerrainPersistence)
        {
            return;
        }

        EnsureResourceStateStore();
        if (installationObject is Trainstation trainStation)
        {
            EnsureTrainStationNameAssigned(trainStation);
        }

        resourceStateStore?.RegisterLiveInstallation(installationObject);
        RegisterVirtualConveyorBelt(installationObject as ConveyorBelt);
        WakeRobotArmsAroundInstallation(installationObject);
        if (installationObject is Trainstation || installationObject is Railload)
        {
            RefreshAutomaticTrainStationNames();
        }
    }

    public InstallationObject CreateInstallationObject(MapObject sourcePrefab, Transform parent = null)
    {
        if (sourcePrefab == null)
        {
            return null;
        }

        Transform resolvedParent = parent != null ? parent : transform;
        if (Application.isPlaying)
        {
            return ResolveInstallationObjectPool()?.Get(sourcePrefab, resolvedParent);
        }

        return Instantiate(sourcePrefab, resolvedParent) as InstallationObject;
    }

    public void ReleaseInstallationObject(InstallationObject installationObject, MapObject sourcePrefab = null)
    {
        if (installationObject == null)
        {
            return;
        }

        UnregisterVirtualConveyorBelt(installationObject as ConveyorBelt);
        if (Application.isPlaying)
        {
            InstallationObjectPool resolvedPool = ResolveInstallationObjectPool();
            if (resolvedPool != null)
            {
                resolvedPool.Release(installationObject, sourcePrefab);
            }
            else
            {
                Destroy(installationObject.gameObject);
            }
        }
        else
        {
            DestroyImmediate(installationObject.gameObject);
        }
    }

    public void RegisterInstallationRuntimeState(InstallationObject installationObject)
    {
        if (installationObject == null || installationObject.ExcludeFromTerrainPersistence)
        {
            return;
        }

        EnsureResourceStateStore();
        if (installationObject is Trainstation trainStation)
        {
            EnsureTrainStationNameAssigned(trainStation);
        }

        resourceStateStore?.RegisterLiveInstallation(installationObject);
        RegisterVirtualConveyorBelt(installationObject as ConveyorBelt);
        WakeRobotArmsAroundInstallation(installationObject);
        if (installationObject is Trainstation || installationObject is Railload)
        {
            RefreshAutomaticTrainStationNames();
        }
    }

    private static void WakeRobotArmsAroundInstallation(InstallationObject installationObject)
    {
        if (!Application.isPlaying || installationObject == null)
        {
            return;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = installationObject.RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null || occupiedCoordinates.Count <= 0)
        {
            if (installationObject.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
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

    public void RegisterVirtualConveyorBelt(ConveyorBelt conveyorBelt)
    {
        if (!Application.isPlaying || conveyorBelt == null)
        {
            return;
        }

        bool hideBelts = IsBeltRenderingHidden();
        conveyorBelt.SetRuntimeRootSuspended(hideBelts);
        conveyorBelt.SetRuntimeRenderingHidden(hideBelts);
        if (hideBelts)
        {
            return;
        }

        if (!VirtualizeConveyorBelts)
        {
            UnregisterVirtualConveyorBelt(conveyorBelt);
            return;
        }

        EnsureVirtualConveyorBeltRenderer()?.Register(conveyorBelt);
    }

    public void UnregisterVirtualConveyorBelt(ConveyorBelt conveyorBelt, bool restoreNativeRenderers = true)
    {
        if (!Application.isPlaying || conveyorBelt == null)
        {
            return;
        }

        virtualConveyorBeltRenderer?.Unregister(conveyorBelt, restoreNativeRenderers);
    }

    public void RefreshBeltRenderingVisibility()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        bool hideBelts = IsBeltRenderingHidden();
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            if (pair.Value != null && pair.Value.MapObject is ConveyorBelt conveyorBelt)
            {
                if (hideBelts)
                {
                    conveyorBelt.SetRuntimeRenderingHidden(true);
                    conveyorBelt.SetRuntimeRootSuspended(true);
                }
                else
                {
                    conveyorBelt.SetRuntimeRootSuspended(false);
                    conveyorBelt.SetRuntimeRenderingHidden(false);
                }
            }
        }
    }

    private static bool IsBeltRenderingHidden()
    {
        return GameManager.Instance != null && GameManager.Instance.HideBelts;
    }

    public void RemoveInstallationPersistence(Vector2Int anchorCoordinate)
    {
        EnsureResourceStateStore();
        resourceStateStore?.RemoveInstallation(anchorCoordinate);
    }

    public void RemoveInstallationPersistence(InstallationObject installationObject)
    {
        EnsureResourceStateStore();
        resourceStateStore?.RemoveInstallation(installationObject);
    }

    public bool TryGetLoadedBlockBounds(out Vector2Int minCoordinate, out Vector2Int maxCoordinate)
    {
        return loadedBlocks.TryGetRegisteredBounds(out minCoordinate, out maxCoordinate);
    }

    public bool TryAddDroppedItemNear(Vector3 worldPosition, int itemId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);

        if (TryGetFocusedConveyorBeltBlock(GameManager.Instance != null ? GameManager.Instance.Player : null, out _, out Block focusedConveyorBlock))
        {
            if (!TryAddDroppedItemToFocusedConveyor(
                    worldPosition,
                    focusedConveyorBlock,
                    itemId,
                    worldPosition,
                    0f,
                    out targetPortableObject,
                    out Block targetConveyorBlock))
            {
                return false;
            }

            MarkDroppedPickupGate(targetPortableObject, true, worldPosition);
            targetConveyorBlock?.MarkConveyorDroppedItem(worldPosition, true);
            return true;
        }

        if (TryResolveFocusedGroundBoxDropBlock(worldPosition, itemId, 1, out Block focusedBoxBlock)
            && focusedBoxBlock.TryAddInputAreaCenterObjectAnimated(itemId, worldPosition, 0f, out targetPortableObject))
        {
            MarkInputAreaDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

        if (TryResolveInputAreaDropBlock(centerCoordinate, itemId, 1, out Block inputAreaBlock)
            && inputAreaBlock.TryAddInputAreaCenterObjectAnimated(itemId, worldPosition, 0f, out targetPortableObject))
        {
            MarkInputAreaDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

        const int maxSearchRadius = 2;
        for (int radius = 0; radius <= maxSearchRadius; radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (radius > 0 && Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                    {
                        continue;
                    }

                    Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                    if (!TryGetLoadedBlock(coordinate, out Block block) || block == null)
                    {
                        continue;
                    }

                    if (block.Type != Block.BlockType.Ground)
                    {
                        continue;
                    }

                    if (block.TryAddFloorObject(itemId, out targetPortableObject))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public bool TryAddDroppedItemToNearestStack(Vector3 worldPosition, int itemId, int searchRadius, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;

        if (itemId < 0)
        {
            return false;
        }

        int radius = Mathf.Max(0, searchRadius);
        if (radius <= 0)
        {
            return false;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);
        if (TryResolveInputAreaDropBlock(centerCoordinate, itemId, 1, out Block inputAreaBlock)
            && inputAreaBlock.HasInputAreaCenterItem(itemId)
            && inputAreaBlock.TryAddInputAreaCenterObjectAnimated(itemId, worldPosition, 0f, out targetPortableObject))
        {
            MarkInputAreaDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

        Block targetBlock = FindNearestDropBlock(centerCoordinate, itemId, 1, radius, true);
        if (targetBlock == null)
        {
            return false;
        }

        return targetBlock.TryAddFloorObject(itemId, out targetPortableObject);
    }

    public int GetDroppedItemCountAround(Vector3 worldPosition, int itemId, int radius)
    {
        if (itemId < 0 || radius < 0)
        {
            return 0;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);
        int total = 0;

        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
                {
                    loadedBlocks.Remove(coordinate);
                    continue;
                }

                if (block.Type != Block.BlockType.Ground)
                {
                    continue;
                }

                total += block.CountFloorObjects(itemId);
            }
        }

        return total;
    }

    public int RemoveDroppedItemsAround(Vector3 worldPosition, int itemId, int radius, int count)
    {
        if (itemId < 0 || radius < 0 || count <= 0)
        {
            return 0;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);
        int remaining = count;

        for (int searchRadius = 0; searchRadius <= radius && remaining > 0; searchRadius++)
        {
            for (int offsetY = -searchRadius; offsetY <= searchRadius && remaining > 0; offsetY++)
            {
                for (int offsetX = -searchRadius; offsetX <= searchRadius && remaining > 0; offsetX++)
                {
                    if (searchRadius > 0 && Mathf.Abs(offsetX) != searchRadius && Mathf.Abs(offsetY) != searchRadius)
                    {
                        continue;
                    }

                    Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                    if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
                    {
                        loadedBlocks.Remove(coordinate);
                        continue;
                    }

                    if (block.Type != Block.BlockType.Ground)
                    {
                        continue;
                    }

                    int removed = block.RemoveFloorObjects(itemId, remaining);
                    remaining -= removed;
                }
            }
        }

        return count - remaining;
    }

    public int TransferDroppedItemsToHand(Player player, Vector3 worldPosition, int radius)
    {
        if (player == null || radius < 0)
        {
            return 0;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);
        int total = 0;

        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
                {
                    loadedBlocks.Remove(coordinate);
                    continue;
                }

                if (block.Type != Block.BlockType.Ground)
                {
                    continue;
                }

                total += block.TransferFloorObjectsToHand(player);
            }
        }

        return total;
    }

    public bool TryPickupOneItemToHand(Player player, Vector3 pickupOrigin, int radius, float pickupRadius)
    {
        return TryPickupOneItemToHand(player, pickupOrigin, radius, pickupRadius, true);
    }

    public bool TryPickupOneItemToHand(Player player, Vector3 pickupOrigin, int radius, float pickupRadius, bool allowFocusedConveyorPickup)
    {
        if (player == null || radius < 0 || pickupRadius <= 0f)
        {
            return false;
        }

        if (allowFocusedConveyorPickup
            && TryGetFocusedConveyorBeltBlock(player, out _, out Block focusedConveyorBlock)
            && focusedConveyorBlock != null
            && focusedConveyorBlock.TryPickupOneConveyorObjectToHand(player, pickupOrigin, pickupRadius))
        {
            return true;
        }

        if (TryGetFocusedBoxObject(player, out BoxObject focusedBoxObject)
            && focusedBoxObject != null
            && focusedBoxObject.TryPickupContainedObjectToHand(player, pickupOrigin, pickupRadius))
        {
            return true;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(pickupOrigin);

        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
                {
                    loadedBlocks.Remove(coordinate);
                    continue;
                }

                if (block.Type != Block.BlockType.Ground)
                {
                    continue;
                }

                if (block.TryPickupOneFloorObjectToHand(player, pickupOrigin, pickupRadius))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool TryPickupOneItemToBag(Player player, Vector3 pickupOrigin, int radius, float pickupRadius)
    {
        return TryPickupOneItemToBag(player, pickupOrigin, radius, pickupRadius, -1);
    }

    public bool TryPickupOneItemToBag(Player player, Vector3 pickupOrigin, int radius, float pickupRadius, int preferredSlotIndex)
    {
        return TryPickupOneItemToBag(player, pickupOrigin, radius, pickupRadius, preferredSlotIndex, -1);
    }

    public bool TryPickupOneItemToBag(Player player, Vector3 pickupOrigin, int radius, float pickupRadius, int preferredSlotIndex, int preferredItemId)
    {
        return TryPickupOneItemToBag(player, pickupOrigin, radius, pickupRadius, preferredSlotIndex, preferredItemId, true);
    }

    public bool TryPickupOneItemToBag(Player player, Vector3 pickupOrigin, int radius, float pickupRadius, int preferredSlotIndex, int preferredItemId, bool allowFocusedConveyorPickup)
    {
        if (player == null || radius < 0 || pickupRadius <= 0f)
        {
            return false;
        }

        if (allowFocusedConveyorPickup
            && TryGetFocusedConveyorBeltBlock(player, out _, out Block focusedConveyorBlock)
            && focusedConveyorBlock != null
            && focusedConveyorBlock.TryPickupOneConveyorObjectToBag(player, pickupOrigin, pickupRadius, preferredSlotIndex, preferredItemId))
        {
            return true;
        }

        if (TryGetFocusedBoxObject(player, out BoxObject focusedBoxObject)
            && focusedBoxObject != null
            && focusedBoxObject.TryPickupContainedObjectToBag(player, pickupOrigin, pickupRadius, preferredSlotIndex, preferredItemId))
        {
            return true;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(pickupOrigin);

        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
                {
                    loadedBlocks.Remove(coordinate);
                    continue;
                }

                if (block.Type != Block.BlockType.Ground)
                {
                    continue;
                }

                if (block.TryPickupOneFloorObjectToBag(player, pickupOrigin, pickupRadius, preferredSlotIndex, preferredItemId))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
