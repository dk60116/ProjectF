using UnityEngine;

public partial class PlayerController
{
    private Block selectedSeedGroundBlock;
    private ItemDefinition selectedSeedDefinition;
    private Block seedPlantTargetBlock;
    private ItemDefinition seedPlantDefinition;
    private bool seedPlantingQueued;

    public bool IsSeedPlantingActive => seedPlantTargetBlock != null;

    public bool TrySelectSeedGroundAtPointer(
        Vector2 pointerPosition,
        ItemDefinition seedDefinition)
    {
        if (!IsHeldSeedDefinition(seedDefinition))
        {
            SetSelectedSeedGroundBlock(null, null);
            return false;
        }

        Camera targetCamera = ResolveMouseFocusCamera();
        if (targetCamera == null
            || !TryGetPointerBlockFromGroundPlane(
                targetCamera.ScreenPointToRay(pointerPosition),
                out Block block)
            || !CanFocusSeedGroundBlock(block, seedDefinition))
        {
            SetSelectedSeedGroundBlock(null, null);
            return false;
        }

        SetSelectedSeedGroundBlock(block, seedDefinition);
        return true;
    }

    public bool TryGetSelectedSeedGroundBlock(
        out Block block,
        out ItemDefinition seedDefinition)
    {
        block = selectedSeedGroundBlock;
        seedDefinition = selectedSeedDefinition;
        if (block != null
            && IsHeldSeedDefinition(seedDefinition)
            && CanFocusSeedGroundBlock(block, seedDefinition))
        {
            return true;
        }

        SetSelectedSeedGroundBlock(null, null);
        block = null;
        seedDefinition = null;
        return false;
    }

    public bool TryGetSeedGroundInteractionBlock(
        out Block block,
        out ItemDefinition seedDefinition)
    {
        if (TryGetSelectedSeedGroundBlock(out block, out seedDefinition))
        {
            return true;
        }

        return TryGetStandingSeedGroundBlock(out block, out seedDefinition);
    }

    private bool TryGetStandingSeedGroundBlock(
        out Block block,
        out ItemDefinition seedDefinition)
    {
        block = null;
        if (!TryResolveHeldSeedDefinition(out seedDefinition))
        {
            return false;
        }

        TerrainGenerator terrain = ResolveTerrainGenerator();
        Vector3 playerPosition = transform.position;
        Vector2Int playerCoordinate = new Vector2Int(
            Mathf.RoundToInt(playerPosition.x),
            Mathf.RoundToInt(playerPosition.z));
        if (terrain == null
            || !terrain.TryGetLoadedBlock(playerCoordinate, out block)
            || !CanFocusSeedGroundBlock(block, seedDefinition))
        {
            block = null;
            seedDefinition = null;
            return false;
        }

        return true;
    }

    public bool RequestSeedPlanting(ItemDefinition seedDefinition)
    {
        if (interactionPointSnapTarget != null
            || player == null
            || !TryGetSeedGroundInteractionBlock(
                out Block targetBlock,
                out ItemDefinition selectedDefinition)
            || selectedDefinition != seedDefinition)
        {
            return false;
        }

        CancelActiveResourceHarvest();
        CancelAnimalKnifeInteraction();
        CancelPitchforkDigging();
        seedPlantTargetBlock = targetBlock;
        seedPlantDefinition = seedDefinition;
        seedPlantingQueued = false;
        return true;
    }

    private void SetSelectedSeedGroundBlock(
        Block block,
        ItemDefinition seedDefinition)
    {
        if (selectedSeedGroundBlock == block
            && selectedSeedDefinition == seedDefinition)
        {
            return;
        }

        if (seedPlantTargetBlock != null && seedPlantTargetBlock != block)
        {
            CancelSeedPlanting();
        }

        selectedPitchforkGroundBlock = null;
        selectedSeedGroundBlock = block;
        selectedSeedDefinition = block != null ? seedDefinition : null;
        selectedFocusBlocks.Clear();
        if (block != null)
        {
            selectedFocusBlocks.Add(block);
        }

        SetSelectedFocusedBlocks(block != null ? selectedFocusBlocks : null);
    }

    private void RefreshSeedGroundMouseFocus(Vector2 pointerPosition)
    {
        SetMouseFocusedAnimal(null);
        SetMouseFocusedPortableObject(null);

        Camera targetCamera = ResolveMouseFocusCamera();
        if (!TryResolveHeldSeedDefinition(out ItemDefinition seedDefinition)
            || targetCamera == null
            || !TryGetPointerBlockFromGroundPlane(
                targetCamera.ScreenPointToRay(pointerPosition),
                out Block block)
            || !CanFocusSeedGroundBlock(block, seedDefinition))
        {
            SetMouseFocusedBlocks(null);
            return;
        }

        mouseFocusBlocks.Clear();
        mouseFocusBlocks.Add(block);
        SetMouseFocusedBlocks(mouseFocusBlocks);
    }

    private bool CanFocusSeedGroundBlock(
        Block block,
        ItemDefinition seedDefinition)
    {
        TerrainGenerator terrain = ResolveTerrainGenerator();
        return ItemDefinition.IsPlantableSeedDefinition(seedDefinition)
               && block != null
               && terrain != null
               && terrain.IsFarmlandAt(block.Coordinate)
               && IsClearGroundActionBlock(block)
               && terrain.CanPlantSeed(block, seedDefinition);
    }

    private bool IsClearGroundActionBlock(Block block)
    {
        Resource resource = block != null ? block.Resource : null;
        if (block == null
            || block.Type != Block.BlockType.Ground
            || block.MapObject != null
            || (resource != null && resource.gameObject.activeInHierarchy)
            || block.HasDroppedFloorObjects)
        {
            return false;
        }

        nearbyRuntimeInstallationScratch.Clear();
        bool hasInstallation = InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
            block.Coordinate,
            nearbyRuntimeInstallationScratch);
        nearbyRuntimeInstallationScratch.Clear();
        return !hasInstallation;
    }

    private bool TryGetSeedPlantingApproachDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        if (seedPlantTargetBlock == null
            || !IsHeldSeedDefinition(seedPlantDefinition)
            || !CanFocusSeedGroundBlock(seedPlantTargetBlock, seedPlantDefinition))
        {
            CancelSeedPlanting();
            return false;
        }

        Vector3 targetPosition = seedPlantTargetBlock.transform.position;
        Vector3 offset = targetPosition - transform.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        if (distance > PitchforkDiggingRange)
        {
            float moveSpeed = Mathf.Max(0.01f, GetCurrentOnFootMoveSpeed());
            float maximumStep = moveSpeed * Mathf.Max(Time.deltaTime, Time.fixedDeltaTime);
            float inputScale = Mathf.Clamp01(
                (distance - PitchforkDiggingRange) / maximumStep);
            direction = (offset / distance) * inputScale;
            return true;
        }

        if (offset.sqrMagnitude > 0.0001f)
        {
            pendingFacingDirection = offset;
            hasPendingFacingDirection = true;
        }

        if (!seedPlantingQueued)
        {
            seedPlantingQueued = true;
            player.QueueDiggingAnimation();
        }

        return false;
    }

    private void ResolveCompletedSeedPlanting()
    {
        if (seedPlantTargetBlock == null
            || player == null
            || !player.DiggingAnimationFinishedThisFrame)
        {
            return;
        }

        Block completedBlock = seedPlantTargetBlock;
        ItemDefinition completedSeed = seedPlantDefinition;
        bool completed = TryConsumeHeldSeedAndPlant(completedBlock, completedSeed);
        CancelSeedPlanting(false);
        if (completed)
        {
            SetSelectedSeedGroundBlock(null, null);
        }
    }

    private bool TryConsumeHeldSeedAndPlant(
        Block targetBlock,
        ItemDefinition seedDefinition)
    {
        TerrainGenerator terrain = ResolveTerrainGenerator();
        PlayerBag handBag = player != null ? player.GetHandBag() : null;
        if (terrain == null
            || handBag == null
            || !CanFocusSeedGroundBlock(targetBlock, seedDefinition)
            || handBag.GetSlotItemId(0) != seedDefinition.id
            || !handBag.TryRemoveOneAtSlot(0, out int consumedItemId, false))
        {
            return false;
        }

        if (consumedItemId == seedDefinition.id
            && terrain.TryPlantSeed(targetBlock, seedDefinition))
        {
            return true;
        }

        RestoreConsumedSeed(terrain, consumedItemId);
        return false;
    }

    private void RestoreConsumedSeed(TerrainGenerator terrain, int consumedItemId)
    {
        if (player == null || consumedItemId < 0)
        {
            return;
        }

        if (!player.TryAddToHand(consumedItemId, out _)
            && !player.TryAddToBag(consumedItemId, out _))
        {
            Transform dropOrigin = player.BodyTransform != null
                ? player.BodyTransform
                : player.transform;
            terrain?.TryAddDroppedItemNear(dropOrigin.position, consumedItemId, out _);
        }
    }

    private bool IsHeldSeedDefinition(ItemDefinition expectedDefinition)
    {
        return expectedDefinition != null
               && TryResolveHeldSeedDefinition(out ItemDefinition heldDefinition)
               && heldDefinition == expectedDefinition;
    }

    private bool TryResolveHeldSeedDefinition(out ItemDefinition seedDefinition)
    {
        seedDefinition = null;
        PlayerBag handBag = player != null ? player.GetHandBag() : null;
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (handBag == null
            || handBag.GetSlotCount(0) <= 0
            || itemManager == null
            || !itemManager.TryGetItemDefinitionById(
                handBag.GetSlotItemId(0),
                out ItemDefinition heldDefinition)
            || !ItemDefinition.IsPlantableSeedDefinition(heldDefinition))
        {
            return false;
        }

        seedDefinition = heldDefinition;
        return true;
    }

    private void CancelSeedPlanting(bool interruptAnimation = true)
    {
        bool wasActive = seedPlantTargetBlock != null || seedPlantingQueued;
        seedPlantTargetBlock = null;
        seedPlantDefinition = null;
        seedPlantingQueued = false;
        if (interruptAnimation && wasActive)
        {
            player?.CancelDiggingAnimation(false);
        }
    }
}
