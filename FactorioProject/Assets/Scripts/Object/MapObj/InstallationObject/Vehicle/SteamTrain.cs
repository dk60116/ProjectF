using System.Collections.Generic;
using UnityEngine;

public class SteamTrain : RailHandcar
{
    private const float MovementParticleMinDistanceSqr = 0.000001f;
    private const float BurnEnergyEpsilon = 0.0001f;
    private const float BurnEnergyDrivingSpeedThreshold = 0.0001f;
    private const float RearFreightCarMinBehindDistance = 0.01f;

    private Vector3 lastMovementParticlePosition;
    private bool hasLastMovementParticlePosition;
    private int lastDrivenInputFrame = -1;
    private float storedBurnEnergy;
    private float burnEnergyGaugeCapacity;
    private float pendingBurnEnergyCost;
    private int pendingBurnEnergyFrame = -1;
    private readonly List<PortableObject> burnEnergyPortableMoveBuffer = new List<PortableObject>();

    public float ObjectInfoStoredBurnEnergy => Mathf.Max(0f, storedBurnEnergy);
    public float ObjectInfoBurnEnergyGaugeCapacity => Mathf.Max(0f, burnEnergyGaugeCapacity, storedBurnEnergy);
    public float ObjectInfoBurnEnergyGaugeFillAmount
    {
        get
        {
            float gaugeCapacity = ObjectInfoBurnEnergyGaugeCapacity;
            return gaugeCapacity > BurnEnergyEpsilon
                ? Mathf.Clamp01(ObjectInfoStoredBurnEnergy / gaugeCapacity)
                : 0f;
        }
    }
    public float ObjectInfoBurnEnergyUseRatePerSecond
    {
        get
        {
            ItemDefinition installedDefinition = ResolveInstalledDefinition();
            return installedDefinition != null && installedDefinition.useEnergyType == ItemDefinition.EnergyType.Burn
                ? ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition)
                : 0f;
        }
    }

    protected override bool CanConnectToTrainAtOffset(
        Train other,
        Vector2 offsetToOther,
        Vector2 forwardTangent)
    {
        return base.CanConnectToTrainAtOffset(other, offsetToOther, forwardTangent)
               && !IsConnectionOffsetAhead(offsetToOther, forwardTangent);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ResetMovementParticleState();
    }

    protected override void OnDisable()
    {
        StopMovementParticle(true);
        hasLastMovementParticlePosition = false;
        lastDrivenInputFrame = -1;
        ClearPendingBurnEnergyCost();
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        StopMovementParticle(true);
        hasLastMovementParticlePosition = false;
        lastDrivenInputFrame = -1;
        ClearPendingBurnEnergyCost();
        storedBurnEnergy = 0f;
        burnEnergyGaugeCapacity = 0f;
        base.PrepareForPool();
    }

    public override void HandleMountedInput(Vector3 worldMoveDirection, float moveSpeed, float deltaTime)
    {
        HandleMountedInput(worldMoveDirection, moveSpeed, deltaTime, null);
    }

    public override void HandleMountedInput(
        Vector3 worldMoveDirection,
        float moveSpeed,
        float deltaTime,
        Player mountedPlayer)
    {
        ClearPendingBurnEnergyCost();
        if (RequiresPoweredBurnEnergy(worldMoveDirection, deltaTime, out float burnEnergyCost)
            && !TryEnsureBurnEnergyAvailable(burnEnergyCost, mountedPlayer))
        {
            StopMovementParticle(false);
            base.HandleMountedInput(Vector3.zero, moveSpeed, deltaTime);
            return;
        }

        if (burnEnergyCost > BurnEnergyEpsilon)
        {
            pendingBurnEnergyCost = burnEnergyCost;
            pendingBurnEnergyFrame = Time.frameCount;
        }

        lastDrivenInputFrame = Time.frameCount;
        base.HandleMountedInput(worldMoveDirection, moveSpeed, deltaTime);
    }

    private void LateUpdate()
    {
        Vector3 currentPosition = transform.position;
        if (!hasLastMovementParticlePosition)
        {
            lastMovementParticlePosition = currentPosition;
            hasLastMovementParticlePosition = true;
            StopMovementParticle(false);
            return;
        }

        bool isDrivenThisFrame = lastDrivenInputFrame == Time.frameCount;
        bool isDrivenAndMoving = isDrivenThisFrame
                                 && GetPlanarDistanceSqr(lastMovementParticlePosition, currentPosition)
                                 > MovementParticleMinDistanceSqr;
        if (isDrivenThisFrame
            && pendingBurnEnergyFrame == Time.frameCount
            && CurrentVehicleSpeed > BurnEnergyDrivingSpeedThreshold)
        {
            SpendStoredBurnEnergy(pendingBurnEnergyCost);
        }

        ClearPendingBurnEnergyCost();
        SetMovementParticleActive(isDrivenAndMoving);
        lastMovementParticlePosition = currentPosition;
    }

    public void CaptureBurnEnergyState(out float storedEnergy, out float gaugeCapacity)
    {
        storedEnergy = Mathf.Max(0f, storedBurnEnergy);
        gaugeCapacity = Mathf.Max(0f, burnEnergyGaugeCapacity, storedEnergy);
    }

    public void ApplyBurnEnergyState(float storedEnergy, float gaugeCapacity)
    {
        storedBurnEnergy = Mathf.Max(0f, storedEnergy);
        burnEnergyGaugeCapacity = Mathf.Max(0f, gaugeCapacity, storedBurnEnergy);
    }

    private bool RequiresPoweredBurnEnergy(Vector3 worldMoveDirection, float deltaTime, out float burnEnergyCost)
    {
        burnEnergyCost = 0f;
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null
            || installedDefinition.useEnergyType != ItemDefinition.EnergyType.Burn
            || worldMoveDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float burnEnergyPerSecond = ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition);
        burnEnergyCost = burnEnergyPerSecond * Mathf.Max(0f, deltaTime);
        return burnEnergyCost > BurnEnergyEpsilon;
    }

    private bool TryEnsureBurnEnergyAvailable(float requiredEnergy, Player mountedPlayer)
    {
        requiredEnergy = Mathf.Max(0f, requiredEnergy);
        while (storedBurnEnergy + BurnEnergyEpsilon < requiredEnergy)
        {
            if (!TryConsumeOneBurnEnergyItem(mountedPlayer, out int gainedEnergy))
            {
                break;
            }

            storedBurnEnergy += gainedEnergy;
            burnEnergyGaugeCapacity = Mathf.Max(burnEnergyGaugeCapacity, storedBurnEnergy, 1f);
        }

        return storedBurnEnergy + BurnEnergyEpsilon >= requiredEnergy;
    }

    private ItemDefinition ResolveInstalledDefinition()
    {
        return BoundItemDefinition != null
            ? BoundItemDefinition
            : InputOutputModule.ResolveItemDefinition(ResolveItemId());
    }

    private bool TryConsumeOneBurnEnergyItem(Player mountedPlayer, out int gainedEnergy)
    {
        if (TryConsumeBurnEnergyFromRearFreightCar(out gainedEnergy))
        {
            return true;
        }

        return TryConsumeBurnEnergyFromMountedPlayer(mountedPlayer, out gainedEnergy);
    }

    private bool TryConsumeBurnEnergyFromRearFreightCar(out int gainedEnergy)
    {
        gainedEnergy = 0;
        if (!TryGetRearFreightCar(out FreightCar freightCar)
            || !freightCar.TryTakeOneItem(
                transform.position,
                IsUsableBurnEnergyItem,
                out int consumedItemId,
                out Vector3 pickupWorldPosition,
                out PortableObject consumedPortableObject))
        {
            return false;
        }

        if (!TryResolveBurnEnergyAmount(consumedItemId, out gainedEnergy))
        {
            DestroyPortableMoveObject(consumedPortableObject);
            return false;
        }

        PlayBurnEnergyPortableMove(consumedPortableObject, consumedItemId, pickupWorldPosition, true);
        return true;
    }

    private bool TryConsumeBurnEnergyFromMountedPlayer(Player mountedPlayer, out int gainedEnergy)
    {
        gainedEnergy = 0;
        if (mountedPlayer == null)
        {
            return false;
        }

        PlayerBag bag = mountedPlayer.GetBag();
        if (TryConsumeBurnEnergyFromBag(bag, out gainedEnergy))
        {
            return true;
        }

        PlayerBag handBag = mountedPlayer.GetHandBag();
        if (handBag != null
            && handBag != bag
            && TryConsumeBurnEnergyFromBag(handBag, out gainedEnergy))
        {
            handBag.RefreshExternalStackCounts(false);
            mountedPlayer.UpdateCarryState();
            return true;
        }

        return false;
    }

    private bool TryConsumeBurnEnergyFromBag(PlayerBag bag, out int gainedEnergy)
    {
        gainedEnergy = 0;
        if (bag == null)
        {
            return false;
        }

        int slotCount = bag.SlotCount;
        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            int itemId = bag.GetSlotItemId(slotIndex);
            if (!TryResolveBurnEnergyAmount(itemId, out int itemEnergyAmount))
            {
                continue;
            }

            TryGetTopPortableObjectInSlot(bag, slotIndex, out PortableObject sourcePortableObject);
            Vector3 startPosition = sourcePortableObject != null
                ? sourcePortableObject.transform.position
                : transform.position;
            if (!bag.TryRemoveItemsAtSlot(
                    slotIndex,
                    1,
                    out int removedItemId,
                    out int removedCount,
                    out Vector3 removedStartPosition)
                || removedCount <= 0)
            {
                continue;
            }

            if (sourcePortableObject == null)
            {
                startPosition = removedStartPosition;
            }

            if (removedItemId != itemId
                && !TryResolveBurnEnergyAmount(removedItemId, out itemEnergyAmount))
            {
                continue;
            }

            gainedEnergy = itemEnergyAmount;
            PlayBurnEnergyPortableMove(sourcePortableObject, removedItemId, startPosition, false);
            return true;
        }

        return false;
    }

    private bool TryGetTopPortableObjectInSlot(
        PlayerBag bag,
        int slotIndex,
        out PortableObject portableObject)
    {
        portableObject = null;
        burnEnergyPortableMoveBuffer.Clear();
        if (bag == null
            || !bag.TryGetOccupiedSlotObjects(slotIndex, burnEnergyPortableMoveBuffer)
            || burnEnergyPortableMoveBuffer.Count <= 0)
        {
            return false;
        }

        for (int i = burnEnergyPortableMoveBuffer.Count - 1; i >= 0; i--)
        {
            if (burnEnergyPortableMoveBuffer[i] == null)
            {
                continue;
            }

            portableObject = burnEnergyPortableMoveBuffer[i];
            return true;
        }

        return false;
    }

    private bool TryGetRearFreightCar(out FreightCar freightCar)
    {
        freightCar = null;
        if (!TryResolveForward2D(out Vector2 forward))
        {
            return false;
        }

        Vector2 position = new Vector2(transform.position.x, transform.position.z);
        float bestRearScore = 0f;
        foreach (Train connectedTrain in ConnectedTrains)
        {
            if (connectedTrain is not FreightCar candidate
                || candidate == null
                || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 candidatePosition = candidate.transform.position;
            Vector2 delta = new Vector2(candidatePosition.x, candidatePosition.z) - position;
            float rearScore = -Vector2.Dot(delta, forward);
            if (rearScore <= RearFreightCarMinBehindDistance || rearScore <= bestRearScore)
            {
                continue;
            }

            bestRearScore = rearScore;
            freightCar = candidate;
        }

        return freightCar != null;
    }

    private bool TryResolveForward2D(out Vector2 forward)
    {
        if (TryGetCurrentRailPose(out _, out _, out _, out forward)
            && forward.sqrMagnitude > 0.0001f)
        {
            forward.Normalize();
            return true;
        }

        forward = new Vector2(transform.forward.x, transform.forward.z);
        if (forward.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        forward.Normalize();
        return true;
    }

    private bool IsUsableBurnEnergyItem(int itemId)
    {
        return TryResolveBurnEnergyAmount(itemId, out _);
    }

    private static bool TryResolveBurnEnergyAmount(int itemId, out int energyAmount)
    {
        energyAmount = 0;
        ItemDefinition definition = InputOutputModule.ResolveItemDefinition(itemId);
        if (definition == null
            || definition.energyType != ItemDefinition.EnergyType.Burn
            || definition.energyAmount <= 0)
        {
            return false;
        }

        energyAmount = definition.energyAmount;
        return true;
    }

    private void SpendStoredBurnEnergy(float cost)
    {
        if (cost <= BurnEnergyEpsilon)
        {
            return;
        }

        storedBurnEnergy = Mathf.Max(0f, storedBurnEnergy - cost);
        if (storedBurnEnergy <= BurnEnergyEpsilon)
        {
            storedBurnEnergy = 0f;
            burnEnergyGaugeCapacity = 0f;
        }
    }

    private void PlayBurnEnergyPortableMove(
        PortableObject sourcePortableObject,
        int itemId,
        Vector3 startPosition,
        bool useSourcePortableObject)
    {
        PortableObject movingPortableObject = useSourcePortableObject
            ? sourcePortableObject
            : CreateBurnEnergyPortableMoveObject(sourcePortableObject, itemId, startPosition);
        if (movingPortableObject == null)
        {
            return;
        }

        Transform movingTransform = movingPortableObject.transform;
        movingPortableObject.name = $"{movingPortableObject.name}_BurnEnergyMove";
        movingTransform.SetParent(null, true);
        movingTransform.position = startPosition;
        if (sourcePortableObject != null)
        {
            movingTransform.localScale = sourcePortableObject.transform.lossyScale;
        }

        if (!movingPortableObject.gameObject.activeSelf)
        {
            movingPortableObject.gameObject.SetActive(true);
        }

        if (!movingPortableObject.SetItem(itemId))
        {
            DestroyPortableMoveObject(movingPortableObject);
            return;
        }

        Vector3 targetPosition = ResolveBurnEnergyPortableMoveTargetPosition();
        movingPortableObject.MoveTo(
            () => this != null ? ResolveBurnEnergyPortableMoveTargetPosition() : targetPosition,
            0f,
            () => startPosition,
            () => DestroyPortableMoveObject(movingPortableObject),
            false);
    }

    private PortableObject CreateBurnEnergyPortableMoveObject(
        PortableObject sourcePortableObject,
        int itemId,
        Vector3 startPosition)
    {
        PortableObject movingPortableObject = null;
        if (sourcePortableObject != null)
        {
            movingPortableObject = Instantiate(
                sourcePortableObject,
                startPosition,
                sourcePortableObject.transform.rotation);
        }
        else
        {
            GameObject itemObject = new GameObject($"SteamTrainBurnEnergyMove_{itemId}");
            itemObject.AddComponent<MeshFilter>();
            itemObject.AddComponent<MeshRenderer>();
            movingPortableObject = itemObject.AddComponent<PortableObject>();
        }

        if (movingPortableObject == null)
        {
            return null;
        }

        movingPortableObject.gameObject.layer = gameObject.layer;
        movingPortableObject.transform.SetParent(null, true);
        movingPortableObject.transform.position = startPosition;
        if (!movingPortableObject.gameObject.activeSelf)
        {
            movingPortableObject.gameObject.SetActive(true);
        }

        if (!movingPortableObject.SetItem(itemId))
        {
            DestroyPortableMoveObject(movingPortableObject);
            return null;
        }

        return movingPortableObject;
    }

    private Vector3 ResolveBurnEnergyPortableMoveTargetPosition()
    {
        if (particleEffect != null)
        {
            return particleEffect.transform.position;
        }

        return transform.position;
    }

    private static void DestroyPortableMoveObject(PortableObject portableObject)
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

    private void ClearPendingBurnEnergyCost()
    {
        pendingBurnEnergyCost = 0f;
        pendingBurnEnergyFrame = -1;
    }

    private void ResetMovementParticleState()
    {
        lastMovementParticlePosition = transform.position;
        hasLastMovementParticlePosition = true;
        StopMovementParticle(true);
    }

    private void SetMovementParticleActive(bool isMoving)
    {
        if (particleEffect == null)
        {
            return;
        }

        if (isMoving)
        {
            if (!particleEffect.isEmitting)
            {
                particleEffect.Play(true);
            }

            return;
        }

        StopMovementParticle(false);
    }

    private void StopMovementParticle(bool clearParticles)
    {
        if (particleEffect == null || (!particleEffect.isPlaying && !particleEffect.isEmitting))
        {
            return;
        }

        particleEffect.Stop(
            true,
            clearParticles
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting);
    }

    private static float GetPlanarDistanceSqr(Vector3 from, Vector3 to)
    {
        float deltaX = to.x - from.x;
        float deltaZ = to.z - from.z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }
}
