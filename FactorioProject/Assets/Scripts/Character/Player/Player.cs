using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class Player : Character
{
    private static readonly int PickHash = Animator.StringToHash("tPick");
    private static readonly int CarryHash = Animator.StringToHash("fCarry");
    private const string PickStateName = "Pick";
    private const string IdleStateName = "Idle";
    private const string RunningStateName = "Running";

    [Serializable]
    public struct PlayerState
    {
        [SerializeField]
        private int miningPower;
        [SerializeField]
        private int loggingPower;

        [SerializeField]
        private float miningSpeed;
        [SerializeField]
        private float loggingSpeed;

        [SerializeField]
        public float harvestRange;

        public PlayerState(
            int miningPower,
            int loggingPower,
            float miningSpeed,
            float loggingSpeed,
            float harvestRange)
        {
            this.miningPower = miningPower;
            this.loggingPower = loggingPower;
            this.miningSpeed = miningSpeed;
            this.loggingSpeed = loggingSpeed;
            this.harvestRange = harvestRange;
        }

        public float MiningSpeed => miningSpeed > 0f ? miningSpeed : 1f;
        public float LoggingSpeed => loggingSpeed > 0f ? loggingSpeed : 1f;
        public int MiningPower => miningPower > 0 ? miningPower : 1;
        public int LoggingPower => loggingPower > 0 ? loggingPower : 1;
        public float HarvestRange => harvestRange > 0f ? harvestRange : 2f;
    }

    [FormerlySerializedAs("playerStauts")]
    [SerializeField]
    private PlayerState playerState;

    private int pendingPickTriggerCount;
    private bool wasPickStateActiveLastFrame;

    [SerializeField]
    private List<PlayerBag> bagList;
    [SerializeField, Min(1)]
    private int bagLevel = 1;

    [SerializeField]
    private List<PortableObject> handStack;
    [SerializeField, Min(0f)]
    private float handToBagPortableMoveInterval = 0.1f;
    private bool handStackInitialized;
    private PlayerBag handBag;
    private readonly HashSet<PortableObject> reservedHandStack = new HashSet<PortableObject>();
    private bool isCarrying;
    private bool dropExitPending;
    private Vector2Int dropExitOriginCoord;
    private Vector2Int lastDropTargetCoord;
    private bool hasLastDropTarget;

    private void InitializeHandStack()
    {
        if (handStack == null)
        {
            handStack = new List<PortableObject>();
        }

        if (handStackInitialized)
        {
            return;
        }

        handStackInitialized = true;
        for (int i = 0; i < handStack.Count; i++)
        {
            PortableObject portableObject = handStack[i];
            if (portableObject == null)
            {
                continue;
            }

            if (portableObject.gameObject.activeSelf)
            {
                portableObject.gameObject.SetActive(false);
            }
        }
    }

    private void EnsureHandBag()
    {
        InitializeHandStack();

        if (handBag == null)
        {
            Transform handRoot = ResolveHandStackRoot();
            if (handRoot == null)
            {
                handRoot = transform;
            }

            handBag = handRoot.GetComponent<PlayerBag>();
            if (handBag == null)
            {
                handBag = handRoot.gameObject.AddComponent<PlayerBag>();
            }
        }

        if (handBag != null)
        {
            handBag.SetExternalStack(handStack);
        }
    }

    private Transform ResolveHandStackRoot()
    {
        if (handStack == null)
        {
            return null;
        }

        for (int i = 0; i < handStack.Count; i++)
        {
            PortableObject portableObject = handStack[i];
            if (portableObject == null)
            {
                continue;
            }

            Transform portableTransform = portableObject.transform;
            return portableTransform.parent != null ? portableTransform.parent : portableTransform;
        }

        return null;
    }

    protected void Awake()
    {
        ApplyBagLevelVisibility();
        RefreshBagUI();
    }

    private new void Start()
    {
        base.Start();

        RefreshBagUI();
    }

    private void OnValidate()
    {
        ApplyBagLevelVisibility();
        RefreshBagUI();
    }

    public void QueuePickAnimation()
    {
        pendingPickTriggerCount++;
    }

    public void ClearQueuedPickAnimations()
    {
        pendingPickTriggerCount = 0;
    }

    public void StopImmediateActions()
    {
        ClearQueuedPickAnimations();

        if (animator == null)
        {
            return;
        }

        animator.SetBool(MoveHash, false);

        if (IsPickStateActive())
        {
            InterruptPickAnimation(false);
        }

        wasPickStateActiveLastFrame = false;
    }

    public bool UpdateAnimationState(bool shouldRun)
    {
        if (animator == null)
        {
            return false;
        }

        bool isPickActive = IsPickStateActive();

        if (shouldRun && isPickActive)
        {
            InterruptPickAnimation(true);
            wasPickStateActiveLastFrame = false;
            animator.SetBool(MoveHash, true);
            return false;
        }

        bool finishedPickThisFrame = wasPickStateActiveLastFrame && !isPickActive;
        animator.SetBool(MoveHash, shouldRun && !isPickActive);

        if (pendingPickTriggerCount > 0 && !isPickActive)
        {
            animator.ResetTrigger(PickHash);
            animator.SetTrigger(PickHash);
            pendingPickTriggerCount--;
        }

        wasPickStateActiveLastFrame = IsPickStateActive();
        return finishedPickThisFrame;
    }

    public void UpdateCarryState()
    {
        bool nextCarry = HasVisibleHandObject();
        if (nextCarry == isCarrying)
        {
            return;
        }

        isCarrying = nextCarry;
        if (animator != null)
        {
            animator.SetFloat(CarryHash, isCarrying ? 1f : 0f);
        }
    }

    public bool IsCarrying => isCarrying;

    private bool HasVisibleHandObject()
    {
        EnsureHandBag();
        InitializeHandStack();
        if (handStack == null)
        {
            return false;
        }

        for (int i = 0; i < handStack.Count; i++)
        {
            PortableObject portableObject = handStack[i];
            if (portableObject != null && portableObject.gameObject.activeSelf)
            {
                return true;
            }
        }

        return false;
    }

    public void MarkDropExitGate(Vector3 origin, float radius)
    {
        dropExitOriginCoord = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));
        dropExitPending = true;
    }

    public void SetLastDropTarget(Vector2Int coordinate)
    {
        lastDropTargetCoord = coordinate;
        hasLastDropTarget = true;
    }

    public void UpdateDropExitGate(Vector3 currentPosition)
    {
        if (!dropExitPending)
        {
            return;
        }

        Vector2Int currentCoord = new Vector2Int(
            Mathf.RoundToInt(currentPosition.x),
            Mathf.RoundToInt(currentPosition.z));
        if (currentCoord != dropExitOriginCoord)
        {
            dropExitPending = false;
            hasLastDropTarget = false;
        }
    }

    public bool IsDropExitPending => dropExitPending;

    public bool TryGetLastDropTarget(out Vector2Int coordinate)
    {
        if (hasLastDropTarget)
        {
            coordinate = lastDropTargetCoord;
            return true;
        }

        coordinate = default;
        return false;
    }

    public void ClearLastDropTarget()
    {
        hasLastDropTarget = false;
    }

    public PlayerState State => playerState;

    public PlayerSaveData CaptureSaveState()
    {
        EnsureHandBag();
        PlayerSaveData saveData = new PlayerSaveData
        {
            hasPlayer = true,
            position = transform.position,
            rotation = transform.rotation,
            bagLevel = bagLevel,
            stats = new PlayerStatSaveData
            {
                miningPower = playerState.MiningPower,
                loggingPower = playerState.LoggingPower,
                miningSpeed = playerState.MiningSpeed,
                loggingSpeed = playerState.LoggingSpeed,
                harvestRange = playerState.HarvestRange
            }
        };

        PlayerBag activeBag = GetBag();
        activeBag?.CaptureSaveSlots(saveData.bagSlots);

        if (handBag != null)
        {
            handBag.RefreshExternalStackCounts(false);
            handBag.CaptureSaveSlots(saveData.handSlots);
        }

        return saveData;
    }

    public void ApplySaveState(PlayerSaveData saveData)
    {
        ApplyTransformState(saveData);
        ApplyInventoryAndStatState(saveData);
    }

    public void ApplyTransformState(PlayerSaveData saveData)
    {
        if (saveData == null || !saveData.hasPlayer)
        {
            return;
        }

        transform.SetPositionAndRotation(saveData.position, saveData.rotation);
        Rigidbody rigidbody = GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.velocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
        }

        StopImmediateActions();
        dropExitPending = false;
        hasLastDropTarget = false;
    }

    public void ApplyInventoryAndStatState(PlayerSaveData saveData)
    {
        if (saveData == null || !saveData.hasPlayer)
        {
            return;
        }

        if (saveData.stats != null)
        {
            playerState = new PlayerState(
                saveData.stats.miningPower,
                saveData.stats.loggingPower,
                saveData.stats.miningSpeed,
                saveData.stats.loggingSpeed,
                saveData.stats.harvestRange);
        }

        bagLevel = saveData.bagLevel;
        ApplyBagLevelVisibility();

        PlayerBag activeBag = GetBag();
        activeBag?.ApplySaveSlots(saveData.bagSlots);

        EnsureHandBag();
        reservedHandStack.Clear();
        if (handBag != null)
        {
            handBag.ApplySaveSlots(saveData.handSlots);
            handBag.RefreshExternalStackCounts(false);
        }

        UpdateCarryState();
        RefreshBagUI();
    }

    private bool IsPickStateActive()
    {
        if (animator == null)
        {
            return false;
        }

        if (animator.GetCurrentAnimatorStateInfo(0).IsName(PickStateName))
        {
            return true;
        }

        return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(PickStateName);
    }

    private void InterruptPickAnimation(bool shouldRun)
    {
        if (animator == null)
        {
            return;
        }

        animator.ResetTrigger(PickHash);
        animator.Play(shouldRun ? RunningStateName : IdleStateName, 0, 0f);
        animator.Update(0f);
    }

    public PlayerBag GetBag()
    {
        if (bagList == null || bagList.Count == 0)
        {
            return null;
        }

        int activeBagIndex = GetActiveBagIndex();
        if (activeBagIndex < 0)
        {
            return null;
        }

        PlayerBag activeBag = bagList[activeBagIndex];
        return activeBag != null && activeBag.gameObject.activeInHierarchy ? activeBag : null;
    }

    public bool TryAddToBag(int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        PlayerBag activeBag = GetBag();
        if (activeBag == null)
        {
            return false;
        }

        return activeBag.TryAddObject(objectId, out targetPortableObject);
    }

    public bool TryAddToBagAtSlot(int slotIndex, int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        PlayerBag activeBag = GetBag();
        if (activeBag == null)
        {
            return false;
        }

        return activeBag.TryAddObjectToSlotOnly(slotIndex, objectId, out targetPortableObject);
    }

    public bool TryAddToHand(int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (objectId < 0)
        {
            return false;
        }

        EnsureHandBag();
        InitializeHandStack();
        if (handStack == null || handStack.Count == 0)
        {
            return false;
        }

        int occupiedItemId = ResolveHandStackItemId();
        if (occupiedItemId >= 0 && occupiedItemId != objectId)
        {
            if (handBag != null && handBag.GetSlotCount(0) <= 0 && handBag.ClearVisualPreservedObjects(0))
            {
                occupiedItemId = ResolveHandStackItemId();
            }

            if (occupiedItemId >= 0 && occupiedItemId != objectId)
            {
                return false;
            }
        }

        if (handBag != null && handBag.TryRestoreVisualPreservedObjectToSlotOnly(0, objectId, out targetPortableObject))
        {
            return true;
        }

        for (int i = 0; i < handStack.Count; i++)
        {
            PortableObject portableObject = handStack[i];
            if (portableObject == null)
            {
                continue;
            }

            if (portableObject.gameObject.activeSelf)
            {
                continue;
            }

            if (reservedHandStack.Contains(portableObject))
            {
                continue;
            }

            portableObject.gameObject.SetActive(true);
            if (!portableObject.SetItem(objectId))
            {
                portableObject.gameObject.SetActive(false);
                continue;
            }

            targetPortableObject = portableObject;
            if (handBag != null)
            {
                handBag.RefreshExternalStackCounts();
            }
            return true;
        }

        return false;
    }

    public bool TryReserveHandObject(int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (objectId < 0)
        {
            return false;
        }

        EnsureHandBag();
        InitializeHandStack();
        if (handStack == null || handStack.Count == 0)
        {
            return false;
        }

        int occupiedItemId = ResolveHandStackItemId();
        if (occupiedItemId >= 0 && occupiedItemId != objectId)
        {
            if (handBag != null && handBag.GetSlotCount(0) <= 0 && handBag.HasVisualPreservedObjects(0))
            {
                return true;
            }

            return false;
        }

        if (handBag != null && handBag.HasVisualPreservedObject(0, objectId))
        {
            return true;
        }

        for (int i = 0; i < handStack.Count; i++)
        {
            PortableObject portableObject = handStack[i];
            if (portableObject == null)
            {
                continue;
            }

            if (portableObject.gameObject.activeSelf || reservedHandStack.Contains(portableObject))
            {
                continue;
            }

            if (!portableObject.SetItem(objectId))
            {
                continue;
            }

            portableObject.gameObject.SetActive(false);
            reservedHandStack.Add(portableObject);
            targetPortableObject = portableObject;
            return true;
        }

        return false;
    }

    public void CommitReservedHandObject(PortableObject targetPortableObject)
    {
        if (targetPortableObject == null)
        {
            return;
        }

        reservedHandStack.Remove(targetPortableObject);
        if (!targetPortableObject.gameObject.activeSelf)
        {
            targetPortableObject.gameObject.SetActive(true);
        }

        if (handBag != null)
        {
            handBag.RefreshExternalStackCounts();
        }

        UpdateCarryState();
    }

    public void ReleaseReservedHandObject(PortableObject targetPortableObject)
    {
        if (targetPortableObject == null)
        {
            return;
        }

        reservedHandStack.Remove(targetPortableObject);
        if (targetPortableObject.gameObject.activeSelf)
        {
            targetPortableObject.gameObject.SetActive(false);
        }

        if (handBag != null)
        {
            handBag.RefreshExternalStackCounts();
        }

        UpdateCarryState();
    }

    public int GetReservedHandItemId()
    {
        return ResolveHandStackItemId(includeActiveObjects: false, includeReservedObjects: true);
    }

    public PlayerBag GetHandBag()
    {
        EnsureHandBag();
        return handBag;
    }

    public int GetHandItemCount()
    {
        EnsureHandBag();
        if (handBag != null)
        {
            handBag.RefreshExternalStackCounts(false);
            return handBag.GetSlotCount(0);
        }

        if (handStack == null || handStack.Count == 0)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < handStack.Count; i++)
        {
            PortableObject portableObject = handStack[i];
            if (portableObject == null || !portableObject.gameObject.activeSelf)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public bool CanAcceptHandObject(int objectId)
    {
        if (objectId < 0)
        {
            return false;
        }

        EnsureHandBag();
        InitializeHandStack();
        if (handStack == null || handStack.Count == 0)
        {
            return false;
        }

        int occupiedItemId = ResolveHandStackItemId();
        if (occupiedItemId >= 0 && occupiedItemId != objectId)
        {
            return false;
        }

        for (int i = 0; i < handStack.Count; i++)
        {
            PortableObject portableObject = handStack[i];
            if (portableObject == null)
            {
                continue;
            }

            if (portableObject.gameObject.activeSelf || reservedHandStack.Contains(portableObject))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    public bool HasMatchingHandStackSpace(int objectId)
    {
        if (objectId < 0)
        {
            return false;
        }

        EnsureHandBag();
        InitializeHandStack();
        return ResolveHandStackItemId() == objectId && CanAcceptHandObject(objectId);
    }

    public bool CanClearHandIntoBag()
    {
        EnsureHandBag();
        if (handBag == null)
        {
            return true;
        }

        handBag.RefreshExternalStackCounts(false);
        int handCount = handBag.GetSlotCount(0);
        if (handCount <= 0)
        {
            return true;
        }

        int handItemId = handBag.GetSlotItemId(0);
        if (handItemId < 0)
        {
            return false;
        }

        PlayerBag activeBag = GetBag();
        if (activeBag == null)
        {
            return false;
        }

        return activeBag.GetAvailableCapacityForItem(handItemId) >= handCount;
    }

    public bool TryStoreHandItemsInBag()
    {
        EnsureHandBag();
        if (handBag == null)
        {
            return true;
        }

        handBag.RefreshExternalStackCounts(false);
        int handCount = handBag.GetSlotCount(0);
        if (handCount <= 0)
        {
            return true;
        }

        int handItemId = handBag.GetSlotItemId(0);
        if (handItemId < 0)
        {
            return false;
        }

        PlayerBag activeBag = GetBag();
        if (activeBag == null || !CanClearHandIntoBag())
        {
            return false;
        }

        List<PortableObject> sourcePortableObjects = new List<PortableObject>();
        handBag.TryGetOccupiedSlotObjects(0, sourcePortableObjects);
        List<PortableMoveData> pendingMoves = new List<PortableMoveData>(handCount);

        for (int i = 0; i < handCount; i++)
        {
            if (!activeBag.TryAddObject(handItemId, out PortableObject bagTargetPortableObject))
            {
                return false;
            }

            PortableObject sourcePortableObject = sourcePortableObjects.Count > 0
                ? sourcePortableObjects[Mathf.Clamp(sourcePortableObjects.Count - 1 - i, 0, sourcePortableObjects.Count - 1)]
                : null;
            Vector3 startPosition = sourcePortableObject != null
                ? sourcePortableObject.transform.position
                : (BodyTransform != null ? BodyTransform.position : transform.position);
            Vector3 targetPosition = bagTargetPortableObject != null
                ? bagTargetPortableObject.transform.position
                : startPosition;

            pendingMoves.Add(new PortableMoveData(
                sourcePortableObject,
                handItemId,
                startPosition,
                targetPosition,
                i * Mathf.Max(0f, handToBagPortableMoveInterval)));
        }

        handBag.RemoveItems(handItemId, handCount);
        handBag.RefreshExternalStackCounts();
        UpdateCarryState();

        for (int i = 0; i < pendingMoves.Count; i++)
        {
            PlayPortableMoveToBag(pendingMoves[i]);
        }

        return true;
    }

    private void PlayPortableMoveToBag(PortableMoveData moveData)
    {
        PortableObject template = moveData.template;
        if (template == null)
        {
            return;
        }

        PortableObject movingPortableObject = Instantiate(template, moveData.startPosition, template.transform.rotation);
        if (movingPortableObject == null)
        {
            return;
        }

        movingPortableObject.name = $"{template.name}_HandToBagMove";
        movingPortableObject.transform.SetParent(null, true);
        movingPortableObject.transform.position = moveData.startPosition;
        movingPortableObject.transform.localScale = template.transform.lossyScale;
        if (!movingPortableObject.gameObject.activeSelf)
        {
            movingPortableObject.gameObject.SetActive(true);
        }

        if (!movingPortableObject.SetItem(moveData.itemId))
        {
            Destroy(movingPortableObject.gameObject);
            return;
        }

        movingPortableObject.MoveTo(moveData.targetPosition, moveData.delay, () =>
        {
            if (movingPortableObject != null)
            {
                Destroy(movingPortableObject.gameObject);
            }
        }, false);
    }

    private readonly struct PortableMoveData
    {
        public readonly PortableObject template;
        public readonly int itemId;
        public readonly Vector3 startPosition;
        public readonly Vector3 targetPosition;
        public readonly float delay;

        public PortableMoveData(PortableObject template, int itemId, Vector3 startPosition, Vector3 targetPosition, float delay)
        {
            this.template = template;
            this.itemId = itemId;
            this.startPosition = startPosition;
            this.targetPosition = targetPosition;
            this.delay = delay;
        }
    }

    private int ResolveHandStackItemId()
    {
        return ResolveHandStackItemId(includeActiveObjects: true, includeReservedObjects: true);
    }

    private int ResolveHandStackItemId(bool includeActiveObjects, bool includeReservedObjects)
    {
        InitializeHandStack();
        if (handStack == null)
        {
            return -1;
        }

        reservedHandStack.RemoveWhere(portableObject => portableObject == null);

        for (int i = 0; i < handStack.Count; i++)
        {
            PortableObject portableObject = handStack[i];
            if (portableObject == null)
            {
                continue;
            }

            bool isActive = includeActiveObjects && portableObject.gameObject.activeSelf;
            bool isReserved = includeReservedObjects && reservedHandStack.Contains(portableObject);
            if (!isActive && !isReserved)
            {
                continue;
            }

            return portableObject.ItemId;
        }

        return -1;
    }

    private void ApplyBagLevelVisibility()
    {
        if (bagList == null || bagList.Count == 0)
        {
            return;
        }

        int activeBagIndex = GetActiveBagIndex();
        if (activeBagIndex < 0)
        {
            return;
        }

        for (int i = 0; i < bagList.Count; i++)
        {
            PlayerBag bag = bagList[i];
            if (bag == null)
            {
                continue;
            }

            bool shouldBeVisible = i == activeBagIndex;
            if (bag.gameObject.activeSelf != shouldBeVisible)
            {
                bag.gameObject.SetActive(shouldBeVisible);
            }
        }
    }

    private int GetActiveBagIndex()
    {
        if (bagList == null || bagList.Count == 0)
        {
            return -1;
        }

        NormalizeBagLevel();
        return bagLevel - 1;
    }

    private void NormalizeBagLevel()
    {
        int maxBagLevel = bagList != null && bagList.Count > 0 ? bagList.Count : 1;
        bagLevel = Mathf.Clamp(bagLevel, 1, maxBagLevel);
    }

    private void RefreshBagUI()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.UIManager == null)
        {
            return;
        }

        GameManager.Instance.UIManager.BindPlayerBag(GetBag());
    }
}
