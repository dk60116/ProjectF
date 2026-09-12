using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// Prefab authoring and temporary placement presentation. Installed arms live in RobotArmWorld.
public class RobotArm : InputOutputModule
{
    public enum RobotArmState
    {
        WaitingForPickup,
        WaitingBeforePickupTake,
        WaitingAfterPickupTake,
        TurningToDrop,
        WaitingForDrop,
        WaitingBeforeDropPlace, // Legacy save state; normalized to WaitingForDrop on restore.
        WaitingAfterDropPlace,
        TurningToPickup
    }


    [System.Serializable]
    public sealed class TransferState
    {
        public int heldItemId = -1;
        public RobotArmState state = RobotArmState.WaitingForPickup;
        public float pickupTimer;
        public float dropRetryTimer;
        public float actionTurnTimer;
        public float turnTimer;
        public bool waitingForDropRetry;

        public TransferState Clone()
        {
            return new TransferState
            {
                heldItemId = heldItemId,
                state = state,
                pickupTimer = pickupTimer,
                dropRetryTimer = dropRetryTimer,
                actionTurnTimer = actionTurnTimer,
                turnTimer = turnTimer,
                waitingForDropRetry = waitingForDropRetry
            };
        }
    }


    [SerializeField]
    private Transform body;

    [SerializeField]
    private PortableObject handItem;

    [SerializeField]
    private bool useLongArmAnimation;

    [SerializeField, Min(0.01f)]
    private float pickupInterval = 0.1f;

    [SerializeField, Min(1f)]
    private float bodyTurnSpeedDegreesPerSecond = 540f;

    [SerializeField, Min(0.01f)]
    private float dropRetryInterval = 0.1f;

    [SerializeField, Min(0f), Tooltip("Pickup action delay; drop recovery uses twice this delay after a successful transfer.")]
    [FormerlySerializedAs("postActionTurnDelay")]
    private float actionTurnDelay = 0.1f;


    private TransferState previewTransferState;
    internal Transform RuntimeBodyTemplate => body;
    internal Transform RuntimeHandTemplate => handItem != null ? handItem.transform : body;
    internal bool UseLongArmAnimation => useLongArmAnimation;
    public float PickupIntervalSeconds => Mathf.Max(0.01f, pickupInterval);
    public float BodyTurnSpeedDegreesPerSecond => Mathf.Max(1f, bodyTurnSpeedDegreesPerSecond);
    public float DropRetryIntervalSeconds => Mathf.Max(0.01f, dropRetryInterval);
    public float ActionTurnDelaySeconds => Mathf.Max(0f, actionTurnDelay);
    public bool HasHeldItem => previewTransferState != null && previewTransferState.heldItemId >= 0;
    public int HeldItemId => previewTransferState != null ? previewTransferState.heldItemId : -1;
    public Vector3 HeldItemWorldPosition => RuntimeHandTemplate != null ? RuntimeHandTemplate.position : transform.position;
    public void SetEditorSettings(
        float pickupIntervalSeconds,
        float turnSpeedDegreesPerSecond,
        float retryIntervalSeconds,
        float turnDelaySeconds)
    {
        pickupInterval = Mathf.Max(0.01f, pickupIntervalSeconds);
        bodyTurnSpeedDegreesPerSecond = Mathf.Max(1f, turnSpeedDegreesPerSecond);
        dropRetryInterval = Mathf.Max(0.01f, retryIntervalSeconds);
        actionTurnDelay = Mathf.Max(0f, turnDelaySeconds);
    }
    protected override void OnEnable() { }
    protected override void OnDisable() { base.OnDisable(); }
    protected override void OnPlacementRuntimeChanged() { }
    protected override void OnPlacementRuntimeCleared() { }
    public override void ManagedUpdateTick(float deltaTime) { }
    public override void PlanManagedUpdateTick(float deltaTime) { }
    public override void ApplyManagedUpdateTick() { }
    protected override void WakeRuntimeUpdate() { }
    public override bool IsWorkingForItemLight => false;
    public override void PrepareForPool() { previewTransferState = null; base.PrepareForPool(); }
    public TransferState CaptureTransferState() => previewTransferState?.Clone() ?? new TransferState();
    public void ApplyTransferState(TransferState state) { previewTransferState = state?.Clone(); }
    public void ClearHeldItemAndTransferState() { previewTransferState = null; }
    public bool TryClearHeldItemForPacking(int expectedItemId)
    {
        if (!HasHeldItem || expectedItemId >= 0 && HeldItemId != expectedItemId) return false;
        previewTransferState.heldItemId = -1; return true;
    }
    public override bool TryGetElectricPowerRequirement(out float watts)
    { watts = ItemDefinition.ResolveElectricUseWatts(BoundItemDefinition ?? ResolveItemDefinition(ResolveItemId())); return watts > 0f; }
    public override bool TryGetElectricPowerDemand(out float watts) { watts = 0f; return false; }
    public static void WakeAroundCoordinate(Vector2Int coordinate) => RobotArmWorld.Current?.Wake(coordinate);
    public static void WakeAllHeldItemTransfers() => RobotArmWorld.Current?.WakeAll();
    public bool TryCollectTransferItemIds(ICollection<int> itemIds)
    {
        List<ItemDefinition> definitions = GameManager.Instance?.ItemManger?.ItemDefinitions;
        if (itemIds == null || definitions == null)
            return false;
        bool found = false;
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || definition.id < 0 || IsFluidItemDefinition(definition))
                continue;
            // Area storage accepts solids; the existing pickup filter selects what the arm moves.
            itemIds.Add(definition.id);
            found = true;
        }
        return found;
    }
    protected override bool AppendOutputItemIds(ISet<int> itemIds) => TryCollectTransferItemIds(itemIds);
    protected override bool ShouldPlayWorkAnimation() => false;
}
