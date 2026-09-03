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
    private Resource SpawnResourceOnBlock(Block block, Resource prefab, Vector2Int worldCoordinate)
    {
        if (block == null || prefab == null)
        {
            return null;
        }

        EnsureResourceStateStore();
        if (resourceStateStore != null && resourceStateStore.IsDepleted(worldCoordinate))
        {
            block.SetMapObject(null);
            return null;
        }

        Resource spawnedResource = Instantiate(prefab, block.RuntimeObjectRoot);
        spawnedResource.transform.position = block.WorldPosition;
        spawnedResource.transform.rotation = Quaternion.identity;
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
        return spawnedResource;
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

    private void SaveChunkResourceStates(Block[] chunkBlocks)
    {
        IEnumerator routine = SaveChunkResourceStatesRoutine(chunkBlocks, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator SaveChunkResourceStatesRoutine(Block[] chunkBlocks, bool allowYield)
    {
        using (SaveChunkStatesMarker.Auto())
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
        int blockBudget = GetChunkProcessingStepBudget();
        resourceStateStore.CaptureLiveUtilityPoleTopologyIfComplete();
        HashSet<InstallationObject> savedInstallations = new HashSet<InstallationObject>();
        HashSet<Vector2Int> chunkCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            using (SaveChunkStatesMarker.Auto())
            {
                Block block = chunkBlocks[i];
                if (block != null)
                {
                    chunkCoordinates.Add(block.Coordinate);
                    SaveLoadedBlockFloorObjects(block);

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
        using (RemoveChunkLookupMarker.Auto())
        {
            if (chunkBlocks == null || chunkBlocks.Length <= 0)
            {
                yield break;
            }
        }

        int blocksSinceYield = 0;
        int blockBudget = GetChunkProcessingStepBudget();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            using (RemoveChunkLookupMarker.Auto())
            {
                Block block = chunkBlocks[i];
                if (block != null)
                {
                    if (loadedBlocks.TryGetValue(block.Coordinate, out Block loadedBlock) && loadedBlock == block)
                    {
                        removedAnyConveyorBlock |= block.IsRuntimeConveyor;
                        loadedBlocks.Remove(block.Coordinate);
                    }

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

        if (!TryGetLoadedBlock(centerCoordinate, out Block centerBlock)
            || centerBlock == null)
        {
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
                bool foundBlock = requireSameItem
                    ? loadedBlocks.TryGetValue(coordinate, out Block block)
                    : TryGetLoadedBlock(coordinate, out block);
                if (!foundBlock || block == null)
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

    private bool IsValidDropBlock(Block block, int itemId, int itemCount)
    {
        return block != null
               && block.Type == Block.BlockType.Ground
               && (CanAbsorbDroppedFarmlandFertilizer(block.Coordinate, itemId)
                   || block.CanAddFloorObjects(itemCount, itemId));
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

    private IEnumerator RestoreChunkInstallationsRoutine(Block[] chunkBlocks, bool allowYield)
    {
        if (chunkBlocks == null)
        {
            yield break;
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            yield break;
        }

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

        InstallationObject restoredInstallation = CreateInstallationObject(sourcePrefab, transform);

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
        RemoveRestoredBelt2FBridgeCenterPassthroughCoordinate(
            restoredInstallation,
            savedState.anchorCoordinate,
            occupiedCoordinates);
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
                placementController.ClearPlacedTrainRailSample(restoredInstallation);
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
        if (restoredInstallation is LoggingMachine restoredLoggingMachine)
        {
            restoredLoggingMachine.ApplyTreeFilterState(
                savedState.loggingTreeFilterInitialized,
                savedState.loggingEnabledTreeDefinitionKeys,
                savedState.loggingMinimumGrowth);
        }
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
                if (ShouldPreserveBelt2FBridgeCenterPassthrough(block, installedObject))
                {
                    continue;
                }

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

    private void RemoveRestoredBelt2FBridgeCenterPassthroughCoordinate(
        InstallationObject restoredInstallation,
        Vector2Int bridgeCenterCoordinate,
        List<Vector2Int> occupiedCoordinates)
    {
        if (!(restoredInstallation is ConvayorBelt2F)
            || occupiedCoordinates == null
            || !occupiedCoordinates.Contains(bridgeCenterCoordinate)
            || !loadedBlocks.TryGetValue(bridgeCenterCoordinate, out Block bridgeCenterBlock)
            || bridgeCenterBlock == null
            || !IsBelt2FBridgeCenterPassthrough(bridgeCenterBlock.MapObject))
        {
            return;
        }

        occupiedCoordinates.Remove(bridgeCenterCoordinate);
    }

    private static bool ShouldPreserveBelt2FBridgeCenterPassthrough(
        Block block,
        MapObject installedObject)
    {
        return block != null
               && installedObject is ConvayorBelt2F belt2F
               && belt2F.IsBridgeCenterCoordinate(block.Coordinate)
               && IsBelt2FBridgeCenterPassthrough(block.MapObject);
    }

    private static bool IsBelt2FBridgeCenterPassthrough(MapObject mapObject)
    {
        return mapObject is Pipe
               || (mapObject is ConveyorBelt conveyorBelt
                   && !(conveyorBelt is ConvayorBelt2F));
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

    private int GetChunkProcessingStepBudget()
    {
        return Mathf.Max(1, chunkGenerationBlocksPerFrame);
    }

    private void ReleaseChunkBlockRuntimeProxies(Block[] chunkBlocks)
    {
        IEnumerator routine = ReleaseChunkBlockRuntimeProxiesRoutine(chunkBlocks, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator ReleaseChunkBlockRuntimeProxiesRoutine(Block[] chunkBlocks, bool allowYield)
    {
        using (ReleaseChunkBlocksMarker.Auto())
        {
            if (chunkBlocks == null || chunkBlocks.Length <= 0)
            {
                yield break;
            }
        }

        if (!Application.isPlaying)
        {
            for (int i = 0; i < chunkBlocks.Length; i++)
            {
                Block block = chunkBlocks[i];
                if (block != null)
                {
                    ReleaseFarmlandVisual(block.Coordinate);
                    block.PrepareForRuntimeRelease();
                    UnityEngine.Object.DestroyImmediate(block);
                }
            }

            yield break;
        }

        int blocksSinceYield = 0;
        int blockBudget = GetChunkProcessingStepBudget();
        suppressedBlockProxyMaterializationDepth++;
        try
        {
            for (int i = 0; i < chunkBlocks.Length; i++)
            {
                using (ReleaseChunkBlocksMarker.Auto())
                {
                    Block block = chunkBlocks[i];
                    if (block != null)
                    {
                        ReleaseFarmlandVisual(block.Coordinate);
                        block.PrepareForRuntimeRelease();
                        UnityEngine.Object.Destroy(block);
                    }
                }

                if (allowYield && ++blocksSinceYield >= blockBudget)
                {
                    blocksSinceYield = 0;
                    yield return null;
                }
            }
        }
        finally
        {
            suppressedBlockProxyMaterializationDepth--;
        }
    }

    private bool HasSavedOrLiveInstallationAtCoordinate(Vector2Int worldCoordinate)
    {
        EnsureResourceStateStore();
        return resourceStateStore != null
               && resourceStateStore.TryGetInstallationAnchorAtCoordinate(worldCoordinate, out _);
    }

    private bool RequiresInitialBlockRuntimeProxy(
        Vector2Int worldCoordinate,
        out Resource generatedResourcePrefab)
    {
        bool hasGeneratedResource = TryGetResourcePrefab(
                                        worldCoordinate,
                                        out generatedResourcePrefab)
                                    && CanSpawnResourceAtGeneratedCoordinate(
                                        worldCoordinate,
                                        generatedResourcePrefab);
        if (!hasGeneratedResource)
        {
            generatedResourcePrefab = null;
        }

        bool hasStoredState = resourceStateStore != null
                              && (resourceStateStore.TryGet(worldCoordinate, out _)
                                  || resourceStateStore.HasFloorObjectState(worldCoordinate)
                                  || resourceStateStore.TryGetConveyorItems(worldCoordinate, out _));
        return hasGeneratedResource
               || hasStoredState
               || HasSavedOrLiveInstallationAtCoordinate(worldCoordinate)
               || farmlandCoordinates.Contains(worldCoordinate)
               || plantedSeedItemIds.ContainsKey(worldCoordinate);
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
        int anchorBudget = GetChunkProcessingStepBudget();
        for (int i = 0; i < liveAnchors.Count; i++)
        {
            using (CleanupInstallationsMarker.Auto())
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

                if (resourceStateStore.TryDetachLiveInstallation(
                        anchorCoordinate,
                        out InstallationObject detachedInstallation,
                        out _))
                {
                    ReleaseInstallationObject(detachedInstallation, ResolveInstallationSourcePrefab(state));
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
