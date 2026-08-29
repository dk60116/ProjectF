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
    private void SpawnResourceOnBlock(Block block, Resource prefab, Vector2Int worldCoordinate)
    {
        if (block == null || prefab == null)
        {
            return;
        }

        EnsureResourceStateStore();
        if (resourceStateStore != null && resourceStateStore.IsDepleted(worldCoordinate))
        {
            block.SetMapObject(null);
            return;
        }

        Resource spawnedResource = Instantiate(prefab, block.transform);
        spawnedResource.transform.localPosition = Vector3.zero;
        spawnedResource.transform.localRotation = Quaternion.identity;
        ApplyResourceScaleProfile(spawnedResource, prefab);
        bool isOilResource = IsOilResourcePrefab(prefab);
        spawnedResource.ApplyBodyYawStep(GetResourceBodyYawStep(prefab, worldCoordinate));

        if (resourceStateStore != null && resourceStateStore.TryGet(worldCoordinate, out Resource.ResourceSaveState savedState))
        {
            spawnedResource.ApplySavedState(savedState);
            if (spawnedResource is ProjectF.MapObjects.Tree savedTree && !savedState.hasGrowth)
            {
                savedTree.SetGrowth(GetInitialTreeGrowth(prefab, worldCoordinate));
            }
        }
        else
        {
            spawnedResource.InitializeRuntimeQuantity(GetInitialResourceCount(prefab, worldCoordinate));
            if (spawnedResource is ProjectF.MapObjects.Tree spawnedTree)
            {
                spawnedTree.SetGrowth(GetInitialTreeGrowth(prefab, worldCoordinate));
            }
        }

        // Oil is a grid-aligned liquid plane. Old resource state may contain a
        // random ore yaw, so restore the fixed orientation after loading it.
        if (isOilResource)
        {
            spawnedResource.ApplyBodyYawStep(0);
        }

        block.SetMapObject(spawnedResource);
    }

    private void ApplyResourceScaleProfile(Resource spawnedResource, Resource prefab)
    {
        if (spawnedResource == null)
        {
            return;
        }

        if (IsOilResourcePrefab(prefab))
        {
            spawnedResource.ConfigureDynamicBodyScale(1f, 1f, 1);
            return;
        }

        if (IsTreeResourcePrefab(prefab))
        {
            spawnedResource.ConfigureFixedBodyScale();
            return;
        }

        spawnedResource.ConfigureDynamicBodyScale(
            oreMinimumBodyScaleRatio,
            oreMaximumBodyScaleRatio,
            oreScaleAtResourceCount);
    }

    private void SaveChunkResourceStates(Transform chunkTransform)
    {
        if (chunkTransform == null)
        {
            return;
        }

        SaveChunkResourceStates(GetDirectChunkBlocks(chunkTransform));
    }

    private void SaveChunkResourceStates(Block[] chunkBlocks)
    {
        IEnumerator routine = SaveChunkResourceStatesRoutine(chunkBlocks, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator SaveChunkResourceStatesRoutine(Block[] chunkBlocks, bool allowYield)
    {
        using (UnloadChunkSaveStatesMarker.Auto())
        {
            if (chunkBlocks == null || chunkBlocks.Length <= 0)
            {
                yield break;
            }

            EnsureResourceStateStore();
            if (resourceStateStore == null)
            {
                yield break;
            }
        }

        int blocksSinceYield = 0;
        int blockBudget = GetChunkUnloadBlockStepBudget();
        HashSet<InstallationObject> savedInstallations = new HashSet<InstallationObject>();
        HashSet<Vector2Int> chunkCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            using (UnloadChunkSaveStatesMarker.Auto())
            {
                Block block = chunkBlocks[i];
                if (block != null)
                {
                    chunkCoordinates.Add(block.Coordinate);
                    SaveLoadedBlockFloorObjects(block, VirtualObjectResidency.Virtual);

                    if (block.MapObject is InstallationObject installationObject
                        && !installationObject.ExcludeFromTerrainPersistence
                        && savedInstallations.Add(installationObject))
                    {
                        resourceStateStore.SaveInstallation(installationObject);
                        resourceStateStore.RegisterLiveInstallation(installationObject);
                    }

                    Resource resource = block.Resource;
                    if (resource != null)
                    {
                        resourceStateStore.Save(block.Coordinate, resource);
                    }
                }
            }

            if (allowYield && ++blocksSinceYield >= blockBudget)
            {
                blocksSinceYield = 0;
                yield return null;
            }
        }

        SaveActiveRuntimeInstallations(savedInstallations, chunkCoordinates);
    }

    private List<Vector2Int> CollectChunkInstallationAnchors(Transform chunkTransform)
    {
        if (chunkTransform == null)
        {
            return new List<Vector2Int>();
        }

        return CollectChunkInstallationAnchors(GetDirectChunkBlocks(chunkTransform));
    }

    private List<Vector2Int> CollectChunkInstallationAnchors(Block[] chunkBlocks)
    {
        List<Vector2Int> installationAnchors = new List<Vector2Int>();
        IEnumerator routine = CollectChunkInstallationAnchorsRoutine(chunkBlocks, installationAnchors, false);
        while (routine.MoveNext())
        {
        }

        return installationAnchors;
    }

    private IEnumerator CollectChunkInstallationAnchorsRoutine(
        Block[] chunkBlocks,
        List<Vector2Int> installationAnchors,
        bool allowYield)
    {
        using (UnloadChunkCollectAnchorsMarker.Auto())
        {
            if (installationAnchors == null
                || chunkBlocks == null
                || chunkBlocks.Length <= 0)
            {
                yield break;
            }

            EnsureResourceStateStore();
            if (resourceStateStore == null)
            {
                yield break;
            }
        }

        int blocksSinceYield = 0;
        int blockBudget = GetChunkUnloadBlockStepBudget();
        HashSet<Vector2Int> uniqueAnchors = new HashSet<Vector2Int>();
        HashSet<Vector2Int> chunkCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            using (UnloadChunkCollectAnchorsMarker.Auto())
            {
                Block block = chunkBlocks[i];
                if (block != null)
                {
                    chunkCoordinates.Add(block.Coordinate);
                    if (resourceStateStore.TryGetInstallationAnchorAtCoordinate(block.Coordinate, out Vector2Int anchorCoordinate)
                        && uniqueAnchors.Add(anchorCoordinate))
                    {
                        installationAnchors.Add(anchorCoordinate);
                    }
                }
            }

            if (allowYield && ++blocksSinceYield >= blockBudget)
            {
                blocksSinceYield = 0;
                yield return null;
            }
        }

        AddSavedInstallationAnchorsIntersectingCoordinates(chunkCoordinates, uniqueAnchors, installationAnchors);
    }

    private void AddSavedInstallationAnchorsIntersectingCoordinates(
        ISet<Vector2Int> chunkCoordinates,
        HashSet<Vector2Int> installationAnchors)
    {
        AddSavedInstallationAnchorsIntersectingCoordinates(chunkCoordinates, installationAnchors, null);
    }

    private void AddSavedInstallationAnchorsIntersectingCoordinates(
        ISet<Vector2Int> chunkCoordinates,
        HashSet<Vector2Int> uniqueAnchors,
        List<Vector2Int> installationAnchors)
    {
        if (chunkCoordinates == null
            || chunkCoordinates.Count <= 0
            || uniqueAnchors == null
            || resourceStateStore == null)
        {
            return;
        }

        List<Vector2Int> savedStorageKeys = resourceStateStore.GetSavedInstallationStorageKeys();
        for (int i = 0; i < savedStorageKeys.Count; i++)
        {
            Vector2Int storageKey = savedStorageKeys[i];
            if (uniqueAnchors.Contains(storageKey)
                || !resourceStateStore.TryGetInstallationState(storageKey, out BlockStateStore.InstallationSaveState savedState)
                || !InstallationStateIntersectsCoordinates(savedState, chunkCoordinates))
            {
                continue;
            }

            uniqueAnchors.Add(storageKey);
            installationAnchors?.Add(storageKey);
        }
    }

    private static bool InstallationStateIntersectsCoordinates(
        BlockStateStore.InstallationSaveState state,
        ISet<Vector2Int> coordinates)
    {
        if (state == null || coordinates == null || coordinates.Count <= 0)
        {
            return false;
        }

        if (coordinates.Contains(state.anchorCoordinate))
        {
            return true;
        }

        if (state.occupiedCoordinates == null)
        {
            return false;
        }

        for (int i = 0; i < state.occupiedCoordinates.Count; i++)
        {
            if (coordinates.Contains(state.occupiedCoordinates[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void RemoveChunkBlocksFromLookup(Transform chunkTransform)
    {
        if (chunkTransform == null)
        {
            return;
        }

        RemoveChunkBlocksFromLookup(GetDirectChunkBlocks(chunkTransform));
    }

    private void RemoveChunkBlocksFromLookup(Block[] chunkBlocks)
    {
        IEnumerator routine = RemoveChunkBlocksFromLookupRoutine(chunkBlocks, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator RemoveChunkBlocksFromLookupRoutine(Block[] chunkBlocks, bool allowYield)
    {
        bool removedAnyConveyorBlock = false;
        using (UnloadChunkRemoveLookupMarker.Auto())
        {
            if (chunkBlocks == null || chunkBlocks.Length <= 0)
            {
                yield break;
            }
        }

        int blocksSinceYield = 0;
        int blockBudget = GetChunkUnloadBlockStepBudget();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            using (UnloadChunkRemoveLookupMarker.Auto())
            {
                Block block = chunkBlocks[i];
                if (block != null)
                {
                    if (loadedBlocks.TryGetValue(block.Coordinate, out Block loadedBlock) && loadedBlock == block)
                    {
                        removedAnyConveyorBlock |= block.IsRuntimeConveyor;
                        loadedBlocks.Remove(block.Coordinate);
                    }

                    virtualizedFloorObjectCoordinates.Remove(block.Coordinate);
                    virtualizedConveyorItemCoordinates.Remove(block.Coordinate);
                }
            }

            if (allowYield && ++blocksSinceYield >= blockBudget)
            {
                blocksSinceYield = 0;
                yield return null;
            }
        }

        if (removedAnyConveyorBlock)
        {
            MarkConveyorNetworkDirty();
        }
    }

    private Block FindPreferredDropBlock(Vector3 worldPosition, int itemId, int itemCount)
    {
        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);

        if (!loadedBlocks.TryGetValue(centerCoordinate, out Block centerBlock)
            || centerBlock == null)
        {
            loadedBlocks.Remove(centerCoordinate);
            return null;
        }

        if (IsValidDropBlock(centerBlock, itemId, itemCount))
        {
            return centerBlock;
        }

        // 설치물 입출력처럼 바닥 투척 자체가 금지된 셀에서는 다른 셀로 우회하지 않는다.
        // 일반 바닥의 스택이 찼거나 다른 아이템이 놓인 경우에만 발밑 포함 3x3을 탐색한다.
        return centerBlock.SupportsFloorObjectDrops
            ? FindNearestDropBlock(centerCoordinate, itemId, itemCount, 1, false)
            : null;
    }

    private Block FindNearestDropBlock(Vector2Int centerCoordinate, int itemId, int itemCount, int radius, bool requireSameItem)
    {
        Block bestBlock = null;
        int bestDistance = int.MaxValue;

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(x, y);
                if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                if (!IsValidDropBlock(block, itemId, itemCount))
                {
                    continue;
                }

                if (requireSameItem && !block.HasFloorObjectItem(itemId))
                {
                    continue;
                }

                int distance = Mathf.Abs(x) + Mathf.Abs(y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestBlock = block;
                }
            }
        }

        return bestBlock;
    }

    private static bool IsValidDropBlock(Block block, int itemId, int itemCount)
    {
        return block != null
               && block.Type == Block.BlockType.Ground
               && block.CanAddFloorObjects(itemCount, itemId);
    }

    private bool TryResolveFocusedGroundBoxDropBlock(
        Vector3 worldPosition,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        if (itemId < 0 || itemCount <= 0)
        {
            return false;
        }

        Player currentPlayer = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (!TryGetFocusedBoxObject(currentPlayer, out BoxObject focusedBox)
            || focusedBox == null
            || !focusedBox.IsOpen
            || !focusedBox.AcceptsItem(itemId))
        {
            return false;
        }

        if (focusedBox.TryGetGroundDropCoordinate(out Vector2Int targetCoordinate))
        {
            if (!loadedBlocks.TryGetValue(targetCoordinate, out Block groundBlock) || groundBlock == null)
            {
                loadedBlocks.Remove(targetCoordinate);
                return false;
            }

            if (groundBlock.Type != Block.BlockType.Ground
                || groundBlock.MapObject != focusedBox
                || !groundBlock.CanAddInputAreaCenterObjects(itemCount, itemId))
            {
                return false;
            }

            targetBlock = groundBlock;
            return true;
        }

        return TryResolveFocusedItemAreaBoxDropBlock(focusedBox, itemId, itemCount, out targetBlock);
    }

    private bool TryResolveFocusedItemAreaBoxDropBlock(
        BoxObject focusedBox,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        if (focusedBox == null || itemId < 0 || itemCount <= 0)
        {
            return false;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = focusedBox.RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null || occupiedCoordinates.Count <= 0)
        {
            return false;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireMatchingExistingStack = pass == 0;

            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                Vector2Int coordinate = occupiedCoordinates[i];
                if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                if (block.Type != Block.BlockType.Ground
                    || block.MapObject != focusedBox
                    || !IsValidFocusedItemAreaBoxDropBlock(block, itemId, itemCount))
                {
                    continue;
                }

                if (requireMatchingExistingStack && !block.HasInputAreaCenterItem(itemId))
                {
                    continue;
                }

                targetBlock = block;
                return true;
            }
        }

        return false;
    }

    private bool IsValidFocusedItemAreaBoxDropBlock(Block block, int itemId, int itemCount)
    {
        if (block == null || block.Type != Block.BlockType.Ground)
        {
            return false;
        }

        if (IsValidInputItemAreaDropBlock(block, itemId, itemCount))
        {
            return true;
        }

        ItemDefinition definition = ResolveItemDefinition(itemId);
        if (definition != null
            && definition.energyType != ItemDefinition.EnergyType.None
            && IsValidInputEnergyAreaDropBlock(block, definition.energyType, itemId, itemCount))
        {
            return true;
        }

        return IsValidOutputAreaDropBlock(block, itemId, itemCount);
    }

    private static bool TryGetFocusedBoxObject(Player player, out BoxObject focusedBoxObject)
    {
        focusedBoxObject = null;
        if (player == null)
        {
            return false;
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController == null)
        {
            return false;
        }

        return playerController.TryGetFocusedBoxObject(out focusedBoxObject);
    }

    private static bool TryGetFocusedConveyorBeltBlock(Player player, out ConveyorBelt focusedConveyorBelt, out Block focusedBlock)
    {
        focusedConveyorBelt = null;
        focusedBlock = null;
        if (player == null)
        {
            return false;
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController == null)
        {
            return false;
        }

        return playerController.TryGetFocusedConveyorBelt(out focusedConveyorBelt, out focusedBlock);
    }

    private bool TryResolveInputAreaDropBlock(Vector2Int centerCoordinate, int itemId, int itemCount, out Block targetBlock)
    {
        targetBlock = null;
        if (!loadedBlocks.TryGetValue(centerCoordinate, out Block block) || block == null)
        {
            return false;
        }

        // Input은 플레이어가 실제로 서 있는 셀만 대상으로 삼는다.
        // 에너지 아이템은 영역이 겹치더라도 EnergyInput을 우선한다.
        if (TryResolveInputEnergyAreaDropBlock(centerCoordinate, itemId, itemCount, out targetBlock))
        {
            return true;
        }

        if (IsValidInputItemAreaDropBlock(block, itemId, itemCount))
        {
            targetBlock = block;
            return true;
        }

        return false;
    }

    private bool TryResolveInputEnergyAreaDropBlock(Vector2Int centerCoordinate, int itemId, int itemCount, out Block targetBlock)
    {
        targetBlock = null;
        ItemDefinition definition = ResolveItemDefinition(itemId);
        if (definition == null || definition.energyType == ItemDefinition.EnergyType.None)
        {
            return false;
        }

        if (!loadedBlocks.TryGetValue(centerCoordinate, out Block block) || block == null)
        {
            return false;
        }

        if (!IsValidInputEnergyAreaDropBlock(block, definition.energyType, itemId, itemCount))
        {
            return false;
        }

        targetBlock = block;
        return true;
    }

    private static bool IsValidInputItemAreaDropBlock(Block block, int itemId, int itemCount)
    {
        return block != null
               && itemId >= 0
               && block.Type == Block.BlockType.Ground
               && CoordinateAcceptsInputItemId(block.Coordinate, itemId)
               && block.CanAddInputAreaCenterObjects(itemCount, itemId);
    }

    private static bool CoordinateAcceptsInputItemId(Vector2Int coordinate, int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

        if (InputOutputModuleItemAreaController.CoordinateAcceptsItemId(coordinate, itemId))
        {
            return true;
        }

        HashSet<int> runtimeInputItemIds = new HashSet<int>();
        return InputOutputModule.TryGetAcceptedInputItemIdsAtRuntimeGridCoordinate(coordinate, runtimeInputItemIds)
               && runtimeInputItemIds.Contains(itemId);
    }

    private static bool IsValidInputEnergyAreaDropBlock(
        Block block,
        ItemDefinition.EnergyType energyType,
        int itemId,
        int itemCount)
    {
        return block != null
               && block.Type == Block.BlockType.Ground
               && CoordinateAcceptsInputEnergyType(block.Coordinate, energyType)
               && block.CanAddInputAreaCenterObjects(itemCount, itemId);
    }

    private static bool CoordinateAcceptsInputEnergyType(
        Vector2Int coordinate,
        ItemDefinition.EnergyType energyType)
    {
        if (energyType == ItemDefinition.EnergyType.None)
        {
            return false;
        }

        if (InputOutputModuleEnergyAreaController.CoordinateAcceptsEnergyType(coordinate, energyType))
        {
            return true;
        }

        HashSet<ItemDefinition.EnergyType> runtimeEnergyTypes = new HashSet<ItemDefinition.EnergyType>();
        return InputOutputModule.TryGetInputEnergyTypesAtRuntimeGridCoordinate(coordinate, runtimeEnergyTypes)
               && runtimeEnergyTypes.Contains(energyType);
    }

    private static bool IsValidOutputAreaDropBlock(Block block, int itemId, int itemCount)
    {
        return block != null
               && itemId >= 0
               && block.Type == Block.BlockType.Ground
               && InputOutputModuleOutputAreaController.CoordinateIsOutputArea(block.Coordinate)
               && InputOutputModule.RuntimeOutputCoordinateProducesItemId(block.Coordinate, itemId)
               && block.CanAddInputAreaCenterObjects(itemCount, itemId);
    }

    private static void MarkDroppedPickupGate(PortableObject droppedObject, bool settled, Vector3 origin)
    {
        if (droppedObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = droppedObject.GetComponent<DroppedItemPickupGate>();
        if (gate == null)
        {
            gate = droppedObject.gameObject.AddComponent<DroppedItemPickupGate>();
        }

        gate.MarkDropped(0.5f, settled, origin);
    }

    private static void MarkInputAreaDroppedPickupGate(PortableObject droppedObject, bool settled, Vector3 origin)
    {
        MarkDroppedPickupGate(droppedObject, settled, origin);
        if (droppedObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = droppedObject.GetComponent<DroppedItemPickupGate>();
        gate?.SetAutoPickupBlocked(true);
    }

    private static ItemDefinition ResolveItemDefinition(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    private static Vector2Int GetWorldBlockCoordinate(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.z));
    }

    private void RestoreChunkInstallations(Transform chunkTransform)
    {
        IEnumerator routine = RestoreChunkInstallationsRoutine(chunkTransform, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator RestoreChunkInstallationsRoutine(Transform chunkTransform, bool allowYield)
    {
        if (chunkTransform == null)
        {
            yield break;
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            yield break;
        }

        Block[] chunkBlocks = chunkTransform.GetComponentsInChildren<Block>(true);
        HashSet<Vector2Int> installationAnchors = new HashSet<Vector2Int>();
        HashSet<Vector2Int> chunkCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            if (chunkBlocks[i] == null)
            {
                continue;
            }

            chunkCoordinates.Add(chunkBlocks[i].Coordinate);
            if (resourceStateStore.TryGetInstallationAnchorAtCoordinate(chunkBlocks[i].Coordinate, out Vector2Int anchorCoordinate))
            {
                installationAnchors.Add(anchorCoordinate);
            }
        }

        AddSavedInstallationAnchorsIntersectingCoordinates(chunkCoordinates, installationAnchors);

        int restoresSinceYield = 0;
        int restoreBudget = Mathf.Max(1, chunkInstallationRestoresPerFrame);
        BeginConveyorRuntimeRefreshBatch();
        try
        {
            List<Vector2Int> orderedInstallationAnchors = new List<Vector2Int>(installationAnchors);
            orderedInstallationAnchors.Sort(CompareInstallationRestoreOrder);
            foreach (Vector2Int anchorCoordinate in orderedInstallationAnchors)
            {
                RestoreOrBindSavedInstallation(anchorCoordinate);
                restoresSinceYield++;
                if (allowYield && restoresSinceYield >= restoreBudget)
                {
                    restoresSinceYield = 0;
                    yield return null;
                }
            }

            InstallationPlacementController placementController = ResolveInstallationPlacementController();
            placementController?.NormalizeLoadedFenceVariants(orderedInstallationAnchors);
            placementController?.NormalizeLoadedLegacyPipeVariants(orderedInstallationAnchors);
        }
        finally
        {
            EndConveyorRuntimeRefreshBatch();
        }
    }

    private int CompareInstallationRestoreOrder(Vector2Int left, Vector2Int right)
    {
        int leftPriority = ResolveSavedInstallationRestorePriority(left);
        int rightPriority = ResolveSavedInstallationRestorePriority(right);
        if (leftPriority != rightPriority)
        {
            return leftPriority.CompareTo(rightPriority);
        }

        int xComparison = left.x.CompareTo(right.x);
        return xComparison != 0 ? xComparison : left.y.CompareTo(right.y);
    }

    private int ResolveSavedInstallationRestorePriority(Vector2Int storageKey)
    {
        if (resourceStateStore == null
            || !resourceStateStore.TryGetInstallationState(storageKey, out BlockStateStore.InstallationSaveState savedState)
            || savedState == null)
        {
            return 1;
        }

        ItemDefinition definition = ResolveInstallationDefinition(savedState);
        if (IsRailloadDefinition(definition))
        {
            return 0;
        }

        if (IsTrainDefinition(definition))
        {
            return 2;
        }

        if (ItemDefinitionLookup.IsConveyorBelt2FDefinition(definition)
            || (savedState.occupiedCoordinates != null
                && savedState.occupiedCoordinates.Count > 0
                && !savedState.occupiedCoordinates.Contains(savedState.anchorCoordinate)))
        {
            return 3;
        }

        return 1;
    }

    private static bool IsRailloadDefinition(ItemDefinition definition)
    {
        return definition?.mapObject != null
               && (definition.mapObject is Railload
                   || definition.mapObject.GetComponent<Railload>() != null
                   || definition.mapObject.GetComponentInChildren<Railload>(true) != null);
    }

    private static bool IsTrainDefinition(ItemDefinition definition)
    {
        return definition?.mapObject != null
               && (definition.mapObject is Train
                   || definition.mapObject.GetComponent<Train>() != null
                   || definition.mapObject.GetComponentInChildren<Train>(true) != null);
    }

    private void RestoreOrBindSavedInstallation(Vector2Int anchorCoordinate)
    {
        using (RestoreSavedInstallationMarker.Auto())
        {
            EnsureResourceStateStore();
            if (resourceStateStore == null)
            {
                return;
            }

            if (resourceStateStore.TryGetLiveInstallation(anchorCoordinate, out InstallationObject liveInstallation, out BlockStateStore.InstallationSaveState liveState))
            {
                IReadOnlyList<Vector2Int> occupiedCoordinates = liveState != null && liveState.occupiedCoordinates != null && liveState.occupiedCoordinates.Count > 0
                    ? liveState.occupiedCoordinates
                    : liveInstallation.RuntimeOccupiedCoordinates;
                BindLoadedBlocksToInstallation(liveInstallation, occupiedCoordinates);
                return;
            }

            InstallationBackgroundSimulator backgroundSimulator = ResolveInstallationBackgroundSimulator();
            if (backgroundSimulator != null)
            {
                using (SimulateSavedInstallationMarker.Auto())
                {
                    backgroundSimulator.SimulateSavedInstallation(anchorCoordinate, chunkRestoreBackgroundSimulationIterations);
                }
            }

            if (!resourceStateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState savedState))
            {
                return;
            }

            if (TryInstantiateSavedInstallation(savedState, out InstallationObject restoredInstallation))
            {
                if (restoredInstallation is Trainstation restoredTrainStation)
                {
                    EnsureTrainStationNameAssigned(restoredTrainStation);
                    savedState.stationName = restoredTrainStation.StoredStationName;
                }

                resourceStateStore.RegisterLiveInstallation(restoredInstallation, savedState);
            }
        }
    }

    private MapObject ResolveInstallationSourcePrefab(
        BlockStateStore.InstallationSaveState savedState,
        InstallationPlacementController placementController = null,
        ItemDefinition definition = null)
    {
        if (savedState == null)
        {
            return null;
        }

        definition ??= ResolveInstallationDefinition(savedState);
        if (definition == null || definition.mapObject == null)
        {
            return null;
        }

        MapObject sourcePrefab = null;
        if (ItemDefinitionLookup.IsConveyorBelt2FDefinition(definition))
        {
            sourcePrefab = definition.mapObject;
        }
        else if (definition.mapObject is Wall fencePrototype && savedState.conveyorVariantKind >= 0)
        {
            sourcePrefab = InstallationPlacementController.ResolveFenceVariantPrefab(
                fencePrototype,
                savedState.conveyorVariantKind);
        }
        else if (definition.mapObject is Pipe pipePrototype && savedState.conveyorVariantKind >= 0)
        {
            sourcePrefab = InstallationPlacementController.ResolvePipeVariantPrefab(
                pipePrototype,
                savedState.conveyorVariantKind);
        }
        else if (definition.mapObject is ConveyorBelt conveyorPrototype && savedState.conveyorVariantKind >= 0)
        {
            sourcePrefab = savedState.conveyorVariantKind switch
            {
                2 => conveyorPrototype.ReverseCornerVariantPrefab != null
                    ? conveyorPrototype.ReverseCornerVariantPrefab
                    : conveyorPrototype.CornerVariantPrefab,
                1 => conveyorPrototype.CornerVariantPrefab != null
                    ? conveyorPrototype.CornerVariantPrefab
                    : conveyorPrototype.StraightVariantPrefab,
                _ => conveyorPrototype.StraightVariantPrefab
            };
        }

        if (sourcePrefab == null)
        {
            placementController ??= ResolveInstallationPlacementController();
            sourcePrefab = placementController != null
                ? placementController.ResolveInstalledObjectSourcePrefab(definition, savedState.anchorCoordinate, savedState.quarterTurns)
                : definition.mapObject;
        }

        return sourcePrefab != null ? sourcePrefab : definition.mapObject;
    }

    private bool TryInstantiateSavedInstallation(
        BlockStateStore.InstallationSaveState savedState,
        out InstallationObject installationObject)
    {
        using (InstantiateSavedInstallationMarker.Auto())
        {
        installationObject = null;
        if (savedState == null)
        {
            return false;
        }

        ItemDefinition definition = ResolveInstallationDefinition(savedState);
        if (definition == null || definition.mapObject == null)
        {
            return false;
        }

        InstallationPlacementController placementController = ResolveInstallationPlacementController();
        MapObject sourcePrefab = ResolveInstallationSourcePrefab(savedState, placementController, definition);
        if (sourcePrefab == null)
        {
            return false;
        }

        bool restoresUndergroundPipe = definition.mapObject is UndergroundPipe;
        if (restoresUndergroundPipe)
        {
            sourcePrefab = definition.mapObject;
        }

        int restoreQuarterTurns = ((savedState.quarterTurns % 4) + 4) % 4;
        bool resolvedPipeBeforeActivation = false;
        if (definition.mapObject is Pipe && !restoresUndergroundPipe)
        {
            if (placementController == null
                || !placementController.TryResolvePipeLoadPlacement(
                    definition,
                    savedState.anchorCoordinate,
                    restoreQuarterTurns,
                    savedState.conveyorVariantKind,
                    savedState.pipeConnectionMask,
                    out MapObject resolvedPipePrefab,
                    out int resolvedPipeQuarterTurns,
                    out int resolvedPipeVariantKind)
                || !(resolvedPipePrefab is Pipe resolvedPipe))
            {
                return false;
            }

            sourcePrefab = resolvedPipe;
            restoreQuarterTurns = resolvedPipeQuarterTurns;
            savedState.quarterTurns = restoreQuarterTurns;
            savedState.conveyorVariantKind = resolvedPipeVariantKind;
            savedState.pipeConnectionMask = resolvedPipe.GetConnectionMask(
                placementController.GetInstalledObjectRotation(
                    resolvedPipe,
                    restoreQuarterTurns));
            resolvedPipeBeforeActivation = true;
        }

        // Variant kind and quarter-turns are one persisted state. Re-resolving a wall
        // or pipe while its neighbors are still loading can overwrite a valid shape.
        if (savedState.conveyorVariantKind < 0
            && placementController != null
            && placementController.TryResolveFenceLoadPlacement(
                definition,
                savedState.anchorCoordinate,
                restoreQuarterTurns,
                out MapObject resolvedFencePrefab,
                out int resolvedFenceQuarterTurns,
                out int resolvedFenceVariantKind)
            && resolvedFencePrefab != null)
        {
            sourcePrefab = resolvedFencePrefab;
            restoreQuarterTurns = resolvedFenceQuarterTurns;
            savedState.quarterTurns = restoreQuarterTurns;
            savedState.conveyorVariantKind = resolvedFenceVariantKind;
        }
        else if (!restoresUndergroundPipe
            && !resolvedPipeBeforeActivation
            && savedState.conveyorVariantKind < 0
            && placementController != null
            && placementController.TryResolvePipeLoadPlacement(
                definition,
                savedState.anchorCoordinate,
                restoreQuarterTurns,
                out MapObject resolvedPipePrefab,
                out int resolvedPipeQuarterTurns,
                out int resolvedPipeVariantKind)
            && resolvedPipePrefab != null)
        {
            sourcePrefab = resolvedPipePrefab;
            restoreQuarterTurns = resolvedPipeQuarterTurns;
            savedState.quarterTurns = restoreQuarterTurns;
            savedState.conveyorVariantKind = resolvedPipeVariantKind;
        }

        if (!restoresUndergroundPipe
            && !resolvedPipeBeforeActivation
            && placementController != null
            && sourcePrefab is Pipe savedPipePrefab
            && savedState.pipeConnectionMask >= 0
            && placementController.TryResolvePipeQuarterTurnsFromConnectionMask(
                savedPipePrefab,
                savedState.pipeConnectionMask,
                restoreQuarterTurns,
                out int connectionMaskQuarterTurns))
        {
            restoreQuarterTurns = connectionMaskQuarterTurns;
            savedState.quarterTurns = restoreQuarterTurns;
        }

        Quaternion rotation = placementController != null
            ? placementController.GetInstalledObjectRotation(sourcePrefab, restoreQuarterTurns)
            : sourcePrefab.transform.rotation * Quaternion.Euler(0f, restoreQuarterTurns * 90f, 0f);
        Vector3 position = placementController != null
            ? placementController.GetInstalledObjectWorldPosition(savedState.anchorCoordinate, sourcePrefab, restoreQuarterTurns)
            : new Vector3(savedState.anchorCoordinate.x, transform.position.y, savedState.anchorCoordinate.y);
        if (savedState.hasWorldPose)
        {
            position = savedState.worldPosition;
            rotation = savedState.worldRotation;
        }

        bool reusedSleepingView = TryTakeSleepingInstallationView(savedState, out InstallationObject restoredInstallation);
        if (reusedSleepingView)
        {
            restoredInstallation.transform.SetParent(transform, true);
            restoredInstallation.gameObject.SetActive(false);
        }
        else
        {
            restoredInstallation = CreateInstallationObject(sourcePrefab, transform);
        }

        if (restoredInstallation == null)
        {
            return false;
        }

        MapObject restoredObject = restoredInstallation;
        restoredObject.transform.SetPositionAndRotation(position, rotation);

        List<Vector2Int> occupiedCoordinates = savedState.occupiedCoordinates != null && savedState.occupiedCoordinates.Count > 0
            ? new List<Vector2Int>(savedState.occupiedCoordinates)
            : placementController != null
                ? placementController.GetInstalledObjectFootprintCoordinates(savedState.anchorCoordinate, sourcePrefab, restoreQuarterTurns)
                : new List<Vector2Int> { savedState.anchorCoordinate };
        if (restoredInstallation is Railload)
        {
            List<Vector2Int> rebuiltRailOccupiedCoordinates = new List<Vector2Int>(occupiedCoordinates.Count);
            if (RailloadInstallationController.TryBuildRailCoordinatesFromVisualPath(
                    savedState.railVisualPathPoints,
                    savedState.railVisualPathExtendsStart,
                    savedState.railVisualPathExtendsEnd,
                    null,
                    rebuiltRailOccupiedCoordinates))
            {
                occupiedCoordinates = rebuiltRailOccupiedCoordinates;
            }
        }

        savedState.occupiedCoordinates = occupiedCoordinates;

        if (placementController != null)
        {
            placementController.ConfigureInstalledObjectRuntime(
                restoredInstallation,
                savedState.anchorCoordinate,
                restoreQuarterTurns,
                savedState.inputOutputState,
                savedState.placementSequence,
                occupiedCoordinates);
        }
        else
        {
            restoredInstallation.ConfigurePlacementRuntime(
                savedState.anchorCoordinate,
                restoreQuarterTurns,
                occupiedCoordinates,
                savedState.placementSequence);
            if (restoredInstallation is ConvayorBelt2F restoredBelt2F)
            {
                ConvayorBelt2F.MarkCoverageDirty();
                restoredBelt2F.RefreshCoveredConveyorTopology();
            }

            if (savedState.inputOutputState != null && restoredInstallation is InputOutputModule inputOutputModule)
            {
                inputOutputModule.ApplyPersistentState(savedState.inputOutputState);
            }
        }

        if (placementController != null && restoredInstallation is Train)
        {
            bool restoredTrainRailSample = placementController.TryRestorePlacedTrainRailSample(
                restoredInstallation,
                savedState);
            if (!restoredTrainRailSample)
            {
                placementController.InitializePlacedTrainRailSample(restoredInstallation, savedState.anchorCoordinate);
            }
        }

        if (savedState.hasSteamTrainBurnEnergyState && restoredInstallation is SteamTrain steamTrain)
        {
            steamTrain.ApplyBurnEnergyState(
                savedState.steamTrainStoredBurnEnergy,
                savedState.steamTrainBurnEnergyGaugeCapacity);
        }

        if (restoredInstallation is SteamTrain restoredSteamTrain)
        {
            restoredSteamTrain.ApplyAutoDriveState(
                savedState.steamTrainAutoDriveEnabled,
                savedState.steamTrainAutoDriveTargetAStationName,
                savedState.steamTrainAutoDriveTargetBStationName,
                savedState.steamTrainAutoDriveFuelFilter,
                savedState.steamTrainAutoDriveFreightFilter,
                savedState.steamTrainAutoDriveRouteTargetStationName,
                savedState.steamTrainAutoDriveLastArrivedStationName,
                savedState.steamTrainAutoDriveStationWaitTimer);
        }

        if (restoredInstallation is Trainstation restoredTrainStation)
        {
            restoredTrainStation.ApplyStationName(savedState.stationName);
        }

        if (savedState.robotArmState != null && restoredInstallation is RobotArm robotArm)
        {
            robotArm.ApplyPersistentState(savedState.robotArmState);
        }

        if (restoredInstallation.RuntimeOccupiedCoordinates != null
            && restoredInstallation.RuntimeOccupiedCoordinates.Count > 0)
        {
            occupiedCoordinates = new List<Vector2Int>(restoredInstallation.RuntimeOccupiedCoordinates);
            savedState.occupiedCoordinates = occupiedCoordinates;
        }

        if (restoredInstallation is Railload restoredRailload)
        {
            if (savedState.railRequiredItemCount > 0)
            {
                restoredRailload.ConfigureRequiredItemCount(savedState.railRequiredItemCount);
            }

            restoredRailload.ConfigureVisualPath(
                savedState.railVisualPathPoints,
                savedState.railVisualPathExtendsStart,
                savedState.railVisualPathExtendsEnd);

            if (savedState.railRequiredItemCount <= 0)
            {
                restoredRailload.ConfigureRequiredItemCount(restoredRailload.RequiredItemCount);
            }
        }

        restoredInstallation.ApplyItemFilterMask(savedState.itemFilterMaskWords, savedState.itemFilterMaskInitialized);
        restoredInstallation.SetStoredFluid(
            savedState.storedFluidItemId,
            savedState.storedFluidLiters,
            savedState.storedFluidTemperatureCelsius);

        if (restoredInstallation is IPersistentInstallationItemStorage itemStorage)
        {
            itemStorage.ApplyPersistentStoredItemId(savedState.storedInstallationItemId);
        }
        if (restoredInstallation is IPersistentInstallationItemCollectionStorage collectionStorage)
        {
            collectionStorage.ApplyPersistentStoredItemIds(savedState.storedInstallationItemIds);
        }

        if (restoredInstallation is BoxObject restoredBoxObject && savedState.boxIsOpen.HasValue)
        {
            restoredBoxObject.SetOpenState(savedState.boxIsOpen.Value, false);
        }

        restoredInstallation.gameObject.SetActive(true);
        BindLoadedBlocksToInstallation(restoredInstallation, occupiedCoordinates);
        if (restoredInstallation is Handcart restoredHandcart)
        {
            restoredHandcart.ConnectToNearbyActiveHandcarts();
        }
        installationObject = restoredInstallation;
        return true;
        }
    }

    private void BindLoadedBlocksToInstallation(MapObject installedObject, IReadOnlyList<Vector2Int> occupiedCoordinates)
    {
        using (BindLoadedInstallationBlocksMarker.Auto())
        {
        if (installedObject == null || occupiedCoordinates == null)
        {
            return;
        }

        if (installedObject is Train)
        {
            return;
        }

        IReadOnlyList<Vector2Int> bindingCoordinates = occupiedCoordinates;
        if (installedObject is InstallationObject installationObject
            && installationObject.RuntimeOccupiedCoordinates != null
            && installationObject.RuntimeOccupiedCoordinates.Count > 0)
        {
            bindingCoordinates = installationObject.RuntimeOccupiedCoordinates;
        }

        for (int i = 0; i < bindingCoordinates.Count; i++)
        {
            if (loadedBlocks.TryGetValue(bindingCoordinates[i], out Block block) && block != null)
            {
                block.SetMapObject(installedObject);
            }
        }

        if (installedObject is ConvayorBelt2F belt2F)
        {
            ConvayorBelt2F.MarkCoverageDirty();
            belt2F.RefreshCoveredConveyorTopology();
        }

        RegisterVirtualConveyorBelt(installedObject as ConveyorBelt);
        }
    }

    public bool TryGetInstallationStateAtCoordinate(Vector2Int worldCoordinate, out BlockStateStore.InstallationSaveState state)
    {
        state = null;
        EnsureResourceStateStore();
        if (resourceStateStore == null
            || !resourceStateStore.TryGetInstallationAnchorAtCoordinate(worldCoordinate, out Vector2Int anchorCoordinate))
        {
            return false;
        }

        if (resourceStateStore.TryGetLiveInstallation(anchorCoordinate, out _, out BlockStateStore.InstallationSaveState liveState)
            && liveState != null)
        {
            state = liveState;
            return true;
        }

        return resourceStateStore.TryGetInstallationState(anchorCoordinate, out state);
    }

    public bool TryGetSavedPipeInstallationStateAtCoordinate(
        Vector2Int worldCoordinate,
        out BlockStateStore.InstallationSaveState state)
    {
        state = null;
        EnsureResourceStateStore();
        return resourceStateStore != null
               && resourceStateStore.TryGetSavedPipeInstallationStateAtCoordinate(
                   worldCoordinate,
                   out state);
    }

    public int CollectSavedInstallationStatesAtInteractionCoordinate(
        Vector2Int worldCoordinate,
        ICollection<BlockStateStore.InstallationSaveState> states)
    {
        if (states == null)
        {
            return 0;
        }

        EnsureResourceStateStore();
        return resourceStateStore != null
            ? resourceStateStore.CollectSavedInstallationStatesAtInteractionCoordinate(
                worldCoordinate,
                states)
            : 0;
    }

    private sealed class ChunkSurfaceWorkerInput
    {
        public Vector2Int origin;
        public int chunkSizeInBlocks;
        public int resolution;
        public int cellCount;
        public int biomeGridMinX;
        public int biomeGridMinY;
        public int biomeGridWidth;
        public int biomeGridHeight;
        public int mapMinX;
        public int mapMinY;
        public int mapMaxExclusiveX;
        public int mapMaxExclusiveY;
        public TerrainBiome[] biomeGrid;
        public bool[] blockedWaterGrid;
        public bool[] oilGrid;
        public float generatedSurfaceYOffset;
        public float waterSurfaceDepth;
        public bool generateWaterFoamOverlay;
        public float waterFoamWidth;
        public float waterFoamSurfaceOffset;
        public Color waterFoamOverlayColor;
        public float terrainBlendJitter;
        public float terrainSurfaceVertexJitter;
        public int seed;
    }

    private Block[] GetLoadedChunkBlockSnapshot(Vector2Int chunkCoordinate, Transform chunkTransform)
    {
        int normalizedChunkSize = Mathf.Max(4, chunkSize);
        int expectedCount = normalizedChunkSize * normalizedChunkSize;
        List<Block> chunkBlocks = new List<Block>(expectedCount);
        Vector2Int origin = new Vector2Int(chunkCoordinate.x * normalizedChunkSize, chunkCoordinate.y * normalizedChunkSize);

        for (int localY = 0; localY < normalizedChunkSize; localY++)
        {
            for (int localX = 0; localX < normalizedChunkSize; localX++)
            {
                Vector2Int coordinate = new Vector2Int(origin.x + localX, origin.y + localY);
                if (loadedBlocks.TryGetValue(coordinate, out Block block) && block != null)
                {
                    chunkBlocks.Add(block);
                }
            }
        }

        if (chunkTransform != null && chunkBlocks.Count < expectedCount)
        {
            for (int i = 0; i < chunkTransform.childCount; i++)
            {
                Transform child = chunkTransform.GetChild(i);
                if (child != null
                    && child.TryGetComponent(out Block directBlock)
                    && directBlock != null
                    && !chunkBlocks.Contains(directBlock))
                {
                    chunkBlocks.Add(directBlock);
                }
            }
        }

        if (chunkBlocks.Count > 0 || chunkTransform == null)
        {
            return chunkBlocks.ToArray();
        }

        return GetDirectChunkBlocks(chunkTransform);
    }

    private static Block[] GetDirectChunkBlocks(Transform chunkTransform)
    {
        if (chunkTransform == null)
        {
            return Array.Empty<Block>();
        }

        List<Block> chunkBlocks = new List<Block>(chunkTransform.childCount);
        for (int i = 0; i < chunkTransform.childCount; i++)
        {
            Transform child = chunkTransform.GetChild(i);
            if (child != null && child.TryGetComponent(out Block block) && block != null)
            {
                chunkBlocks.Add(block);
            }
        }

        return chunkBlocks.ToArray();
    }

    private bool TryTakeSleepingChunkView(Vector2Int chunkCoordinate, int normalizedChunkSize, out Transform chunkTransform)
    {
        chunkTransform = null;
        if (!Application.isPlaying
            || !sleepingChunkViews.TryGetValue(chunkCoordinate, out Transform cachedChunk)
            || cachedChunk == null)
        {
            sleepingChunkViews.Remove(chunkCoordinate);
            return false;
        }

        int expectedCount = Mathf.Max(4, normalizedChunkSize) * Mathf.Max(4, normalizedChunkSize);
        if (GetDirectChunkBlocks(cachedChunk).Length < expectedCount)
        {
            sleepingChunkViews.Remove(chunkCoordinate);
            DestroyChunkObject(cachedChunk.gameObject);
            return false;
        }

        sleepingChunkViews.Remove(chunkCoordinate);
        chunkTransform = cachedChunk;
        return true;
    }

    private IEnumerator WakeSleepingChunkViewRoutine(
        Vector2Int chunkCoordinate,
        Transform chunkTransform,
        Vector2Int origin,
        bool allowYield)
    {
        using (WakeChunkViewMarker.Auto())
        {
            if (chunkTransform == null)
            {
                yield break;
            }

            chunkTransform.gameObject.SetActive(false);
            chunkTransform.SetParent(transform, false);
            chunkTransform.position = new Vector3(origin.x, 0f, origin.y);
            chunkTransform.gameObject.name = $"Chunk ({chunkCoordinate.x}, {chunkCoordinate.y})";
            loadedChunks[chunkCoordinate] = chunkTransform;
        }

        Block[] chunkBlocks = GetDirectChunkBlocks(chunkTransform);
        int blocksSinceYield = 0;
        int blockBudget = GetChunkUnloadBlockStepBudget();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            using (WakeChunkViewMarker.Auto())
            {
                Block block = chunkBlocks[i];
                if (block != null)
                {
                    loadedBlocks[block.Coordinate] = block;
                    EnqueueFloorObjectVirtualizationCoordinate(block.Coordinate);
                    EnqueueConveyorItemResidencyCoordinate(block.Coordinate);
                }
            }

            if (allowYield && ++blocksSinceYield >= blockBudget)
            {
                blocksSinceYield = 0;
                yield return null;
            }
        }

        if (allowYield)
        {
            yield return null;
        }

        IEnumerator installationRestoreRoutine = RestoreChunkInstallationsRoutine(chunkTransform, allowYield);
        while (installationRestoreRoutine.MoveNext())
        {
            yield return installationRestoreRoutine.Current;
        }

        IEnumerator blockStateRestoreRoutine = RestoreChunkBlockStatesRoutine(chunkTransform, allowYield);
        while (blockStateRestoreRoutine.MoveNext())
        {
            yield return blockStateRestoreRoutine.Current;
        }

        using (WakeChunkViewMarker.Auto())
        {
            chunkTransform.gameObject.SetActive(true);
            RefreshChunkBlockRuntimeViews(chunkBlocks);
            ApplyStoredConveyorItemSaveStates(chunkBlocks);
        }
    }

    private IEnumerator SleepChunkBlocksForStreamingRoutine(Block[] chunkBlocks, bool allowYield)
    {
        using (UnloadChunkSleepBlocksMarker.Auto())
        {
            if (!Application.isPlaying || chunkBlocks == null || chunkBlocks.Length <= 0)
            {
                yield break;
            }
        }

        int blocksSinceYield = 0;
        int blockBudget = GetChunkUnloadBlockStepBudget();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            using (UnloadChunkSleepBlocksMarker.Auto())
            {
                DetachChunkBlockRuntimeView(chunkBlocks[i]);
            }

            if (allowYield && ++blocksSinceYield >= blockBudget)
            {
                blocksSinceYield = 0;
                yield return null;
            }
        }

    }

    private void DetachChunkBlockRuntimeView(Block block)
    {
        if (block == null)
        {
            return;
        }

        SetConveyorActive(block, false, false);
        SetConveyorDataMotionActive(block, false);
        SetConveyorDotVisualActive(block, false);
        SetBeltDirectionVisualActive(block, false);
        SetConveyorItemVisualActive(block, false);
        conveyorWakeQueued.Remove(block);
    }

    private void RefreshChunkBlockRuntimeViews(Block[] chunkBlocks)
    {
        if (chunkBlocks == null)
        {
            return;
        }

        bool hasConveyorBlock = false;
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            Block block = chunkBlocks[i];
            if (block == null)
            {
                continue;
            }

            hasConveyorBlock |= block.IsRuntimeConveyor;
        }

        if (hasConveyorBlock)
        {
            MarkConveyorNetworkDirty();
        }

        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            Block block = chunkBlocks[i];
            if (block == null)
            {
                continue;
            }

            RefreshRestoredBlockRuntimeRegistration(block);
        }
    }

    private void SleepChunkView(Vector2Int chunkCoordinate, Transform chunkTransform)
    {
        if (!Application.isPlaying || chunkTransform == null)
        {
            return;
        }

        Transform cacheRoot = EnsureSleepingChunkViewRoot();
        chunkTransform.SetParent(cacheRoot, true);
        chunkTransform.gameObject.SetActive(false);
        sleepingChunkViews[chunkCoordinate] = chunkTransform;
    }

    private void SleepInstallationView(Vector2Int storageKey, InstallationObject installationObject)
    {
        using (UnloadChunkSleepInstallationsMarker.Auto())
        {
            if (!Application.isPlaying || installationObject == null)
            {
                return;
            }

            if (sleepingInstallationViews.TryGetValue(storageKey, out InstallationObject existingView)
                && existingView != null
                && existingView != installationObject)
            {
                ReleaseInstallationObject(existingView);
            }

            UnregisterVirtualConveyorBelt(installationObject as ConveyorBelt, false);
            Transform cacheRoot = EnsureSleepingInstallationViewRoot();
            installationObject.transform.SetParent(cacheRoot, true);
            installationObject.gameObject.SetActive(false);
            sleepingInstallationViews[storageKey] = installationObject;
        }
    }

    private bool TryTakeSleepingInstallationView(
        BlockStateStore.InstallationSaveState savedState,
        out InstallationObject installationObject)
    {
        installationObject = null;
        Vector2Int storageKey = BlockStateStore.GetInstallationStorageKey(savedState);
        if (!Application.isPlaying
            || savedState == null
            || !sleepingInstallationViews.TryGetValue(storageKey, out InstallationObject cachedInstallation)
            || cachedInstallation == null)
        {
            if (savedState != null)
            {
                sleepingInstallationViews.Remove(storageKey);
            }

            return false;
        }

        ItemDefinition definition = ResolveInstallationDefinition(savedState);
        int expectedItemId = definition != null ? definition.id : savedState.itemId;
        if (cachedInstallation.ResolveItemId() != expectedItemId)
        {
            sleepingInstallationViews.Remove(storageKey);
            ReleaseInstallationObject(cachedInstallation, ResolveInstallationSourcePrefab(savedState));
            return false;
        }

        if (!SleepingInstallationMatchesSavedVariant(cachedInstallation, savedState, definition))
        {
            sleepingInstallationViews.Remove(storageKey);
            ReleaseInstallationObject(cachedInstallation);
            return false;
        }

        sleepingInstallationViews.Remove(storageKey);
        installationObject = cachedInstallation;
        return true;
    }

    private static bool SleepingInstallationMatchesSavedVariant(
        InstallationObject cachedInstallation,
        BlockStateStore.InstallationSaveState savedState,
        ItemDefinition definition)
    {
        if (cachedInstallation == null || savedState == null || savedState.conveyorVariantKind < 0)
        {
            return true;
        }

        MapObject sourcePrefab = definition != null ? definition.mapObject : null;
        if (sourcePrefab is Pipe)
        {
            return cachedInstallation is Pipe pipe && pipe.VariantKindId == savedState.conveyorVariantKind;
        }

        if (sourcePrefab is Wall)
        {
            return cachedInstallation is Wall wall && wall.VariantKindId == savedState.conveyorVariantKind;
        }

        if (sourcePrefab is ConveyorBelt && !ItemDefinitionLookup.IsConveyorBelt2FDefinition(definition))
        {
            if (!(cachedInstallation is ConveyorBelt conveyorBelt))
            {
                return false;
            }

            int cachedVariantKind = conveyorBelt.IsReverseCornerVariant
                ? 2
                : (conveyorBelt.IsCornerVariant ? 1 : 0);
            return cachedVariantKind == savedState.conveyorVariantKind;
        }

        return true;
    }

    private Transform EnsureSleepingChunkViewRoot()
    {
        if (sleepingChunkViewRoot != null)
        {
            return sleepingChunkViewRoot;
        }

        GameObject rootObject = new GameObject("__SleepingChunkViews");
        if (Application.isPlaying)
        {
            rootObject.hideFlags = HideFlags.HideInHierarchy;
        }

        sleepingChunkViewRoot = rootObject.transform;
        sleepingChunkViewRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return sleepingChunkViewRoot;
    }

    private Transform EnsureSleepingInstallationViewRoot()
    {
        if (sleepingInstallationViewRoot != null)
        {
            return sleepingInstallationViewRoot;
        }

        GameObject rootObject = new GameObject("__SleepingInstallationViews");
        if (Application.isPlaying)
        {
            rootObject.hideFlags = HideFlags.HideInHierarchy;
        }

        sleepingInstallationViewRoot = rootObject.transform;
        sleepingInstallationViewRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return sleepingInstallationViewRoot;
    }

    private void DestroySleepingViewCaches()
    {
        foreach (KeyValuePair<Vector2Int, Transform> pair in sleepingChunkViews)
        {
            if (pair.Value != null)
            {
                DestroyChunkObject(pair.Value.gameObject);
            }
        }

        sleepingChunkViews.Clear();

        foreach (KeyValuePair<Vector2Int, InstallationObject> pair in sleepingInstallationViews)
        {
            if (pair.Value != null)
            {
                ReleaseInstallationObject(pair.Value);
            }
        }

        sleepingInstallationViews.Clear();

        if (sleepingChunkViewRoot != null)
        {
            DestroyChunkObject(sleepingChunkViewRoot.gameObject);
            sleepingChunkViewRoot = null;
        }

        if (sleepingInstallationViewRoot != null)
        {
            DestroyChunkObject(sleepingInstallationViewRoot.gameObject);
            sleepingInstallationViewRoot = null;
        }
    }

    private int GetChunkUnloadBlockStepBudget()
    {
        return Mathf.Max(1, chunkGenerationBlocksPerFrame);
    }

    private void ReleaseChunkBlocksToPool(Transform chunkTransform)
    {
        if (!Application.isPlaying || chunkTransform == null)
        {
            return;
        }

        ReleaseChunkBlocksToPool(GetDirectChunkBlocks(chunkTransform));
    }

    private void ReleaseChunkBlocksToPool(Block[] chunkBlocks)
    {
        IEnumerator routine = ReleaseChunkBlocksToPoolRoutine(chunkBlocks, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator ReleaseChunkBlocksToPoolRoutine(Block[] chunkBlocks, bool allowYield)
    {
        using (UnloadChunkReleaseBlocksMarker.Auto())
        {
            if (!Application.isPlaying || chunkBlocks == null || chunkBlocks.Length <= 0)
            {
                yield break;
            }
        }

        BlockPool resolvedBlockPool;
        using (UnloadChunkReleaseBlocksMarker.Auto())
        {
            resolvedBlockPool = ResolveBlockPool();
            if (resolvedBlockPool == null)
            {
                yield break;
            }
        }

        int blocksSinceYield = 0;
        int blockBudget = GetChunkUnloadBlockStepBudget();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            using (UnloadChunkReleaseBlocksMarker.Auto())
            {
                Block block = chunkBlocks[i];
                if (block != null)
                {
                    resolvedBlockPool.Release(block);
                }
            }

            if (allowYield && ++blocksSinceYield >= blockBudget)
            {
                blocksSinceYield = 0;
                yield return null;
            }
        }
    }

    private bool HasSavedOrLiveInstallationAtCoordinate(Vector2Int worldCoordinate)
    {
        EnsureResourceStateStore();
        return resourceStateStore != null
               && resourceStateStore.TryGetInstallationAnchorAtCoordinate(worldCoordinate, out _);
    }

    private bool CanSpawnResourceAtGeneratedCoordinate(Vector2Int worldCoordinate, Resource resourcePrefab)
    {
        return !HasSavedOrLiveInstallationAtCoordinate(worldCoordinate)
               || CanSpawnResourceUnderInstallation(worldCoordinate, resourcePrefab);
    }

    private bool CanSpawnResourceUnderInstallation(Vector2Int worldCoordinate, Resource resourcePrefab)
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null
            || resourcePrefab == null
            || !resourceStateStore.TryGetInstallationAnchorAtCoordinate(worldCoordinate, out Vector2Int anchorCoordinate)
            || !resourceStateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState)
            || installationState == null)
        {
            return false;
        }

        ItemDefinition definition = ResolveInstallationDefinition(installationState);
        if (definition == null || definition.mapObject == null)
        {
            return false;
        }

        InstallationObject installationObject = ResolveInstallationObject(definition.mapObject);
        if (installationObject == null)
        {
            return false;
        }

        if (installationObject is MiningMachine)
        {
            if (resourcePrefab.ResolvedHarvestMode != Resource.HarvestMode.Mining)
            {
                return false;
            }

            if (installationState.occupiedCoordinates != null && installationState.occupiedCoordinates.Count > 0)
            {
                return installationState.occupiedCoordinates.Contains(worldCoordinate);
            }

            return anchorCoordinate == worldCoordinate;
        }

        InstallationMapFilter allowedFilter = InstallationPlacementController.ResolvePlacementMapFilter(
            definition.mapObject,
            installationObject);
        return InstallationPlacementController.IsResourceAllowedByMapFilter(resourcePrefab, allowedFilter);
    }

    private static InstallationObject ResolveInstallationObject(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return null;
        }

        InstallationObject installationObject = mapObject as InstallationObject;
        if (installationObject != null)
        {
            return installationObject;
        }

        installationObject = mapObject.GetComponent<InstallationObject>();
        return installationObject != null
            ? installationObject
            : mapObject.GetComponentInChildren<InstallationObject>(true);
    }

    private void CleanupOrphanedLiveInstallations()
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        CleanupOrphanedLiveInstallations(resourceStateStore.GetLiveInstallationStorageKeys());
    }

    private void CleanupOrphanedLiveInstallations(IReadOnlyList<Vector2Int> liveAnchors)
    {
        IEnumerator routine = CleanupOrphanedLiveInstallationsRoutine(liveAnchors, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator CleanupOrphanedLiveInstallationsRoutine(IReadOnlyList<Vector2Int> liveAnchors, bool allowYield)
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null || liveAnchors == null || liveAnchors.Count <= 0)
        {
            yield break;
        }

        int anchorsSinceYield = 0;
        int anchorBudget = GetChunkUnloadBlockStepBudget();
        for (int i = 0; i < liveAnchors.Count; i++)
        {
            using (UnloadChunkCleanupInstallationsMarker.Auto())
            {
                Vector2Int anchorCoordinate = liveAnchors[i];
                if (!resourceStateStore.TryGetLiveInstallation(anchorCoordinate, out InstallationObject installationObject, out BlockStateStore.InstallationSaveState state))
                {
                    continue;
                }

                IReadOnlyList<Vector2Int> occupiedCoordinates = state != null && state.occupiedCoordinates != null && state.occupiedCoordinates.Count > 0
                    ? state.occupiedCoordinates
                    : installationObject.RuntimeOccupiedCoordinates;
                bool hasAnyLoadedBlock = false;
                if (occupiedCoordinates != null)
                {
                    for (int coordinateIndex = 0; coordinateIndex < occupiedCoordinates.Count; coordinateIndex++)
                    {
                        if (loadedBlocks.TryGetValue(occupiedCoordinates[coordinateIndex], out Block loadedBlock) && loadedBlock != null)
                        {
                            hasAnyLoadedBlock = true;
                            break;
                        }
                    }
                }

                if (!hasAnyLoadedBlock
                    && installationObject is Handcart handcart
                    && handcart.ConnectedHandcarts.Count > 0)
                {
                    hasAnyLoadedBlock = true;
                }

                if (hasAnyLoadedBlock)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    if (resourceStateStore.TryDetachLiveInstallation(anchorCoordinate, out InstallationObject detachedInstallation, out _))
                    {
                        SleepInstallationView(anchorCoordinate, detachedInstallation);
                    }
                }
                else
                {
                    resourceStateStore.UnregisterLiveInstallation(anchorCoordinate);
                    if (installationObject != null)
                    {
                        ReleaseInstallationObject(installationObject, ResolveInstallationSourcePrefab(state));
                    }
                }
            }

            if (allowYield && ++anchorsSinceYield >= anchorBudget)
            {
                anchorsSinceYield = 0;
                yield return null;
            }
        }
    }

    private void EnsureResourceStateStore()
    {
        if (resourceStateStore != null)
        {
            return;
        }

        resourceStateStore = GetComponent<BlockStateStore>();
        if (resourceStateStore == null)
        {
            resourceStateStore = gameObject.AddComponent<BlockStateStore>();
        }
    }

    private InstallationPlacementController ResolveInstallationPlacementController()
    {
        if (installationRestoreController != null)
        {
            return installationRestoreController;
        }

        InstallationPlacementController[] controllers = Resources.FindObjectsOfTypeAll<InstallationPlacementController>();
        for (int i = 0; i < controllers.Length; i++)
        {
            InstallationPlacementController controller = controllers[i];
            if (controller != null && controller.gameObject.scene.IsValid())
            {
                installationRestoreController = controller;
                return installationRestoreController;
            }
        }

        return null;
    }

    private InstallationBackgroundSimulator ResolveInstallationBackgroundSimulator()
    {
        if (installationBackgroundSimulator != null)
        {
            return installationBackgroundSimulator;
        }

        installationBackgroundSimulator = GetComponent<InstallationBackgroundSimulator>();
        if (installationBackgroundSimulator == null)
        {
            installationBackgroundSimulator = gameObject.AddComponent<InstallationBackgroundSimulator>();
        }

        return installationBackgroundSimulator;
    }

    private BlockPool ResolveBlockPool()
    {
        if (blockPool != null)
        {
            return blockPool;
        }

        blockPool = GetComponent<BlockPool>();
        if (blockPool == null)
        {
            blockPool = gameObject.AddComponent<BlockPool>();
        }

        return blockPool;
    }

    private InstallationObjectPool ResolveInstallationObjectPool()
    {
        if (installationObjectPool != null)
        {
            return installationObjectPool;
        }

        installationObjectPool = GetComponent<InstallationObjectPool>();
        if (installationObjectPool == null)
        {
            installationObjectPool = gameObject.AddComponent<InstallationObjectPool>();
        }

        return installationObjectPool;
    }

    private PortableItemRenderer EnsurePortableItemRenderer()
    {
        if (portableItemRenderer != null)
        {
            return portableItemRenderer;
        }

        portableItemRenderer = PortableItemRenderer.EnsureFor(gameObject);

        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        portableItemRenderer.Configure(this, itemManager);
        return portableItemRenderer;
    }

    private VirtualConveyorBeltRenderer EnsureVirtualConveyorBeltRenderer()
    {
        if (virtualConveyorBeltRenderer != null)
        {
            return virtualConveyorBeltRenderer;
        }

        virtualConveyorBeltRenderer = GetComponent<VirtualConveyorBeltRenderer>();
        if (virtualConveyorBeltRenderer == null)
        {
            virtualConveyorBeltRenderer = gameObject.AddComponent<VirtualConveyorBeltRenderer>();
        }

        return virtualConveyorBeltRenderer;
    }

    private static ItemDefinition ResolveInstallationDefinition(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        return ItemDefinitionLookup.ResolveInstallationById(definitions, itemId);
    }

    private static ItemDefinition ResolveInstallationDefinition(BlockStateStore.InstallationSaveState savedState)
    {
        if (savedState == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance?.ItemManger?.ItemDefinitions;
        ItemDefinition namedDefinition = ItemDefinitionLookup.ResolveInstallationByStableName(
            definitions,
            savedState.itemName);
        if (namedDefinition != null)
        {
            return namedDefinition;
        }

        ItemDefinition legacyDefinition = ResolveLegacyInstallationDefinition(savedState, definitions);
        if (legacyDefinition != null)
        {
            return legacyDefinition;
        }

        ItemDefinition definition = ResolveInstallationDefinition(savedState.itemId);
        if (!ItemDefinitionLookup.LooksLikeLegacyConveyorBelt2FState(
                savedState.itemId,
                definition,
                savedState.occupiedCoordinates))
        {
            return definition;
        }

        ItemDefinition belt2FDefinition = ItemDefinitionLookup.ResolveConveyorBelt2F(definitions);
        return belt2FDefinition != null ? belt2FDefinition : definition;
    }

    private static ItemDefinition ResolveLegacyInstallationDefinition(
        BlockStateStore.InstallationSaveState savedState,
        IReadOnlyList<ItemDefinition> definitions)
    {
        if (savedState == null || definitions == null)
        {
            return null;
        }

        return savedState.itemId switch
        {
            44 when savedState.hasWorldPose => ItemDefinitionLookup.ResolveInstallationByStableName(definitions, "Flatcar"),
            45 when savedState.hasWorldPose => ItemDefinitionLookup.ResolveInstallationByStableName(definitions, "Rail handcar"),
            46 when savedState.hasWorldPose => ItemDefinitionLookup.ResolveInstallationByStableName(definitions, "Steam train"),
            47 when savedState.railVisualPathPoints != null && savedState.railVisualPathPoints.Count > 0
                => ItemDefinitionLookup.ResolveInstallationByStableName(definitions, "Railload"),
            _ => null
        };
    }
}
