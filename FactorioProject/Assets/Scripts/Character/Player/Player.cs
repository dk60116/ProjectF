using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Player : Character
{
    private static readonly int PickHash = Animator.StringToHash("tPick");
    private static readonly int ThrowHash = Animator.StringToHash("tThrow");
    private static readonly int CarryHash = Animator.StringToHash("fCarry");
    private static readonly int MoveAnimationSpeedHash = Animator.StringToHash("fMoveSpeed");
    private static readonly int HandcartMountedHash = Animator.StringToHash("bHandcartMounted");
    private static readonly int HandcartDirectionHash = Animator.StringToHash("fHandcartDirection");
    private const string PickStateName = "Pick";
    private const string IdleStateName = "Idle";
    private const string RunningStateName = "Running";
    private const string TorchLightAnchorName = "_TorchLightAnchor";
    private const float PlayerRootY = 0f;
    private const float TorchEnergyEpsilon = 0.0001f;

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
    private bool handcartAnimationActive;

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

    [Header("Equips")]
    [SerializeField]
    private GameObject knifeObject;
    [SerializeField]
    private GameObject axeObject;
    [SerializeField]
    private GameObject pickaxeObject;
    [SerializeField]
    private GameObject torchObject;

    private PlayerController playerController;
    private Transform torchLightAnchor;
    private int toggledLightItemId = -1;
    private ItemDefinition toggledLightDefinition;
    private bool itemLightToggleRequested;
    private float activeTorchEnergy;
    private float activeTorchEnergyCapacity;

    private enum ToolEquipVisual
    {
        None,
        Knife,
        Axe,
        Pickaxe
    }

    private ToolEquipVisual activeToolEquipVisual;
    private bool torchEquipVisualActive;

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
        playerController = GetComponent<PlayerController>();
        ApplyToolEquipVisual(ToolEquipVisual.None, true);
        ApplyTorchEquipVisual(false, true);
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

    private void LateUpdate()
    {
        UpdateActiveTorchEnergy(Time.deltaTime);
        RefreshEquipVisual();
    }

    public bool ToggleTorchEquip(int torchItemId)
    {
        return ToggleTorchEquip(ResolveItemDefinition(torchItemId));
    }

    public bool ToggleTorchEquip(ItemDefinition torchDefinition)
    {
        return ToggleHeldItemLight(torchDefinition);
    }

    public bool ToggleHeldItemLight(ItemDefinition itemDefinition)
    {
        bool usesTorchEquip = IsTorchDefinition(itemDefinition);
        if (itemDefinition == null
            || itemDefinition.id < 0
            || (!usesTorchEquip && itemDefinition.lightMode != ItemDefinition.ItemLightMode.Toggle))
        {
            return false;
        }

        bool togglingOff = itemLightToggleRequested
                           && toggledLightItemId == itemDefinition.id;
        if (togglingOff)
        {
            DeactivateHeldItemLight();
            RefreshEquipVisual();
            return true;
        }

        EnsureHandBag();
        if (handBag == null
            || handBag.GetSlotCount(0) <= 0
            || handBag.GetSlotItemId(0) != itemDefinition.id)
        {
            return false;
        }

        if (usesTorchEquip)
        {
            float energyCapacity = ItemDefinition.ResolveCompleteEnergyAmount(itemDefinition);
            float energyRate = ItemDefinition.ResolveUseEnergyRatePerSecond(itemDefinition);
            if (!IsValidTorchEnergy(energyCapacity)
                || !IsValidTorchEnergy(energyRate)
                || !handBag.TryRemoveOneAtSlot(0, out int consumedItemId, false)
                || consumedItemId != itemDefinition.id)
            {
                return false;
            }

            DeactivateHeldItemLight();
            itemLightToggleRequested = true;
            toggledLightItemId = itemDefinition.id;
            toggledLightDefinition = itemDefinition;
            activeTorchEnergy = energyCapacity;
            activeTorchEnergyCapacity = energyCapacity;
            UpdateCarryState();
        }
        else
        {
            DeactivateHeldItemLight();
            itemLightToggleRequested = true;
            toggledLightItemId = itemDefinition.id;
            toggledLightDefinition = itemDefinition;
            SetHeldPortableLightToggled(itemDefinition.id, true);
        }

        RefreshEquipVisual();
        return true;
    }

    public bool TryGetActiveTorchEnergy(
        out ItemDefinition itemDefinition,
        out float remainingEnergy,
        out float energyCapacity)
    {
        bool isActive = itemLightToggleRequested
                        && IsTorchDefinition(toggledLightDefinition)
                        && IsValidTorchEnergy(activeTorchEnergy)
                        && IsValidTorchEnergy(activeTorchEnergyCapacity);
        itemDefinition = isActive ? toggledLightDefinition : null;
        remainingEnergy = isActive ? activeTorchEnergy : 0f;
        energyCapacity = isActive ? activeTorchEnergyCapacity : 0f;
        return isActive;
    }

    private void UpdateActiveTorchEnergy(float deltaTime)
    {
        if (!itemLightToggleRequested || !IsTorchDefinition(toggledLightDefinition))
        {
            return;
        }

        float energyRate = ItemDefinition.ResolveUseEnergyRatePerSecond(toggledLightDefinition);
        if (!IsValidTorchEnergy(activeTorchEnergy) || !IsValidTorchEnergy(energyRate))
        {
            DeactivateHeldItemLight();
            return;
        }

        activeTorchEnergy = Mathf.Max(0f, activeTorchEnergy - energyRate * Mathf.Max(0f, deltaTime));
        if (!IsValidTorchEnergy(activeTorchEnergy))
        {
            DeactivateHeldItemLight();
        }
    }

    private void DeactivateHeldItemLight()
    {
        if (itemLightToggleRequested && !IsTorchDefinition(toggledLightDefinition))
        {
            SetHeldPortableLightToggled(toggledLightItemId, false);
        }

        itemLightToggleRequested = false;
        toggledLightItemId = -1;
        toggledLightDefinition = null;
        activeTorchEnergy = 0f;
        activeTorchEnergyCapacity = 0f;
    }

    private void RefreshEquipVisual()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (itemLightToggleRequested
            && !IsTorchDefinition(toggledLightDefinition)
            && !IsToggledLightItemStillHeld())
        {
            DeactivateHeldItemLight();
        }

        ToolEquipVisual nextToolEquip = ToolEquipVisual.None;
        if (playerController != null && playerController.IsAnimalKnifeInteractionActive)
        {
            nextToolEquip = ToolEquipVisual.Knife;
        }
        else if (playerController != null
                 && playerController.TryGetActiveResourceHarvestMode(
                     out Resource.HarvestMode harvestMode))
        {
            nextToolEquip = harvestMode switch
            {
                Resource.HarvestMode.Logging => ToolEquipVisual.Axe,
                Resource.HarvestMode.Cut => ToolEquipVisual.Knife,
                _ => ToolEquipVisual.Pickaxe
            };
        }

        ApplyToolEquipVisual(nextToolEquip);
        ApplyTorchEquipVisual(
            itemLightToggleRequested && IsTorchDefinition(toggledLightDefinition));
    }

    private bool IsToggledLightItemStillHeld()
    {
        EnsureHandBag();
        return toggledLightItemId >= 0
               && handBag != null
               && handBag.GetSlotCount(0) > 0
               && handBag.GetSlotItemId(0) == toggledLightItemId;
    }

    private void ApplyToolEquipVisual(ToolEquipVisual nextEquip, bool force = false)
    {
        if (!force && activeToolEquipVisual == nextEquip)
        {
            return;
        }

        SetEquipObjectActive(knifeObject, nextEquip == ToolEquipVisual.Knife);
        SetEquipObjectActive(axeObject, nextEquip == ToolEquipVisual.Axe);
        SetEquipObjectActive(pickaxeObject, nextEquip == ToolEquipVisual.Pickaxe);
        activeToolEquipVisual = nextEquip;
    }

    private void ApplyTorchEquipVisual(bool active, bool force = false)
    {
        if (!force && torchEquipVisualActive == active)
        {
            return;
        }

        SetEquipObjectActive(torchObject, active);
        torchObject?.GetComponent<ItemLightController>()?.SetToggled(false);

        Transform lightAnchor = ResolveTorchLightAnchor();
        ItemLightController.Configure(
            lightAnchor != null ? lightAnchor.gameObject : null,
            active ? toggledLightDefinition : null,
            active);
        torchEquipVisualActive = active;
    }

    private Transform ResolveTorchLightAnchor()
    {
        if (torchLightAnchor != null)
        {
            return torchLightAnchor;
        }

        Transform existing = transform.Find(TorchLightAnchorName);
        if (existing != null)
        {
            torchLightAnchor = existing;
        }
        else
        {
            GameObject anchorObject = new GameObject(TorchLightAnchorName);
            torchLightAnchor = anchorObject.transform;
            torchLightAnchor.SetParent(transform, false);
        }

        // Match installed-object lights: the owner origin is the zero-height reference,
        // and ItemLightController applies the shared range-based height offset.
        torchLightAnchor.localPosition = Vector3.zero;
        torchLightAnchor.localRotation = Quaternion.identity;
        return torchLightAnchor;
    }

    private void SetHeldPortableLightToggled(int itemId, bool active)
    {
        if (itemId < 0 || handStack == null)
        {
            return;
        }

        for (int i = 0; i < handStack.Count; i++)
        {
            PortableObject portableObject = handStack[i];
            if (portableObject != null && portableObject.ItemId == itemId)
            {
                portableObject.SetItemLightToggled(active);
            }
        }
    }

    private static bool IsTorchDefinition(ItemDefinition definition)
    {
        return definition != null
               && string.Equals(
                   definition.itemName,
                   "Torch",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidTorchEnergy(float value)
    {
        return value > TorchEnergyEpsilon
               && !float.IsNaN(value)
               && !float.IsInfinity(value);
    }

    private static ItemDefinition ResolveItemDefinition(int itemId)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        return ItemDefinitionLookup.ResolveById(
            itemManager != null ? itemManager.ItemDefinitions : null,
            itemId);
    }

    private static void SetEquipObjectActive(GameObject equipObject, bool active)
    {
        if (equipObject != null && equipObject.activeSelf != active)
        {
            equipObject.SetActive(active);
        }
    }

    public void QueuePickAnimation()
    {
        pendingPickTriggerCount++;
    }

    public void ClearQueuedPickAnimations()
    {
        pendingPickTriggerCount = 0;
    }

    public bool TriggerThrowAnimation()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator == null)
        {
            return false;
        }

        ClearQueuedPickAnimations();
        animator.SetBool(MoveHash, false);
        animator.ResetTrigger(PickHash);
        animator.ResetTrigger(ThrowHash);
        animator.SetTrigger(ThrowHash);
        return true;
    }

    public void StopImmediateActions()
    {
        ClearQueuedPickAnimations();

        if (animator == null)
        {
            return;
        }

        animator.SetBool(MoveHash, false);
        animator.ResetTrigger(ThrowHash);

        if (IsPickStateActive())
        {
            InterruptPickAnimation(false);
        }

        wasPickStateActiveLastFrame = false;
    }

    public void UpdateMountedVehicleAnimation(Vehicle vehicle)
    {
        if (!(vehicle is Handcart))
        {
            ClearMountedVehicleAnimation();
            return;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator == null)
        {
            return;
        }

        if (!handcartAnimationActive)
        {
            ClearQueuedPickAnimations();
            animator.ResetTrigger(PickHash);
            animator.ResetTrigger(ThrowHash);
            animator.SetBool(MoveHash, false);
            handcartAnimationActive = true;
            animator.SetBool(HandcartMountedHash, true);
        }

        Transform animationFacing = BodyTransform != null ? BodyTransform : transform;
        float playerRelativeSignedSpeed = vehicle.ResolveSignedSpeedRelativeToFacing(animationFacing);
        float normalizedSignedSpeed = Mathf.Clamp(
            playerRelativeSignedSpeed / Mathf.Max(0.01f, vehicle.EffectiveVehicleMaxSpeed),
            -1f,
            1f);
        if (Mathf.Abs(normalizedSignedSpeed) <= 0.01f)
        {
            normalizedSignedSpeed = 0f;
        }

        float animationSpeed = Mathf.Lerp(0.65f, 1.15f, Mathf.Abs(normalizedSignedSpeed));
        animator.SetFloat(HandcartDirectionHash, normalizedSignedSpeed);
        animator.SetFloat(MoveAnimationSpeedHash, animationSpeed);
    }

    public void ClearMountedVehicleAnimation()
    {
        if (!handcartAnimationActive)
        {
            return;
        }

        handcartAnimationActive = false;
        if (animator == null)
        {
            return;
        }

        animator.SetBool(HandcartMountedHash, false);
        animator.SetFloat(HandcartDirectionHash, 0f);
        animator.SetFloat(MoveAnimationSpeedHash, 1f);
    }

    public bool UpdateAnimationState(bool shouldRun, float movementAnimationSpeed = 1f)
    {
        if (animator == null)
        {
            return false;
        }

        if (handcartAnimationActive)
        {
            return false;
        }

        animator.SetFloat(
            MoveAnimationSpeedHash,
            Mathf.Max(0f, movementAnimationSpeed));
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
            position = ResolveSavePosition(),
            rotation = ResolveSaveRotation(),
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

        if (TryGetActiveTorchEnergy(
                out ItemDefinition activeTorchDefinition,
                out float remainingTorchEnergy,
                out _))
        {
            saveData.activeTorchItemId = activeTorchDefinition.id;
            saveData.activeTorchRemainingEnergy = remainingTorchEnergy;
        }

        PlayerController playerController = GetComponent<PlayerController>();
        if (playerController != null
            && playerController.TryGetMountedVehicleState(out Vehicle mountedVehicle, out int playerPointIndex)
            && mountedVehicle != null)
        {
            saveData.mountedOnVehicle = true;
            saveData.mountedVehiclePlacementSequence = mountedVehicle.RuntimePlacementSequence;
            saveData.mountedVehicleAnchorCoordinate = mountedVehicle.RuntimeAnchorCoordinate;
            saveData.mountedVehiclePlayerPointIndex = playerPointIndex;
        }

        if (playerController != null
            && playerController.TryGetNooseLeashedAnimalId(out long leashedAnimalId))
        {
            saveData.nooseLeashedAnimalId = leashedAnimalId;
        }

        ResolvePlayerHUD()?.CaptureCraftingQueueSaveState(saveData.craftingQueue);
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

        DeactivateHeldItemLight();
        ApplyToolEquipVisual(ToolEquipVisual.None);
        ApplyTorchEquipVisual(false);

        PlayerController playerController = GetComponent<PlayerController>();
        playerController?.ClearInteractionPointSnapForLoad();
        playerController?.ClearNooseForLoad();

        Vector3 rootPosition = ClampRootPositionToGroundY(saveData.position);
        Rigidbody rigidbody = GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.position = rootPosition;
            rigidbody.rotation = saveData.rotation;
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(rootPosition, saveData.rotation);
        Physics.SyncTransforms();
        StopImmediateActions();
        dropExitPending = false;
        hasLastDropTarget = false;
    }

    private Vector3 ResolveSavePosition()
    {
        Rigidbody rigidbody = GetComponent<Rigidbody>();
        return ClampRootPositionToGroundY(rigidbody != null ? rigidbody.position : transform.position);
    }

    private static Vector3 ClampRootPositionToGroundY(Vector3 position)
    {
        position.y = PlayerRootY;
        return position;
    }

    private Quaternion ResolveSaveRotation()
    {
        Rigidbody rigidbody = GetComponent<Rigidbody>();
        return rigidbody != null ? rigidbody.rotation : transform.rotation;
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

        RestoreActiveTorch(saveData.activeTorchItemId, saveData.activeTorchRemainingEnergy);

        UpdateCarryState();
        RefreshBagUI();
        ResolvePlayerHUD()?.ApplyCraftingQueueSaveState(saveData.craftingQueue);
    }

    private void RestoreActiveTorch(int itemId, float remainingEnergy)
    {
        ItemDefinition definition = ResolveItemDefinition(itemId);
        float energyCapacity = ItemDefinition.ResolveCompleteEnergyAmount(definition);
        float energyRate = ItemDefinition.ResolveUseEnergyRatePerSecond(definition);
        if (!IsTorchDefinition(definition)
            || !IsValidTorchEnergy(remainingEnergy)
            || !IsValidTorchEnergy(energyCapacity)
            || !IsValidTorchEnergy(energyRate))
        {
            RefreshEquipVisual();
            return;
        }

        itemLightToggleRequested = true;
        toggledLightItemId = definition.id;
        toggledLightDefinition = definition;
        activeTorchEnergyCapacity = energyCapacity;
        activeTorchEnergy = Mathf.Min(remainingEnergy, energyCapacity);
        RefreshEquipVisual();
    }

    private PlayerHUD ResolvePlayerHUD()
    {
        if (GameManager.Instance != null
            && GameManager.Instance.UIManager != null
            && GameManager.Instance.UIManager.PlayerHUD != null)
        {
            return GameManager.Instance.UIManager.PlayerHUD;
        }

        if (UIManager.Instance != null && UIManager.Instance.PlayerHUD != null)
        {
            return UIManager.Instance.PlayerHUD;
        }

        return FindObjectOfType<PlayerHUD>(true);
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

    public int GetCarriedItemCount(int itemId)
    {
        if (itemId < 0)
        {
            return 0;
        }

        int total = 0;
        PlayerBag activeBag = GetBag();
        if (activeBag != null)
        {
            total += activeBag.GetTotalItemCount(itemId);
        }

        PlayerBag activeHandBag = GetHandBag();
        if (activeHandBag != null)
        {
            activeHandBag.RefreshExternalStackCounts(false);
            total += activeHandBag.GetTotalItemCount(itemId);
        }

        return total;
    }

    public bool HasCraftingManual(int manualItemId)
    {
        if (manualItemId < 0)
        {
            return false;
        }

        if (GetCarriedItemCount(manualItemId) > 0 || Desk.HasStoredManual(manualItemId))
        {
            return true;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        return terrain != null && terrain.HasStoredInstallationItem(manualItemId);
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

    public bool TryAddToBagAnimated(int objectId, Vector3 sourceWorldPosition)
    {
        if (!TryAddToBag(objectId, out PortableObject targetPortableObject))
        {
            return false;
        }

        if (targetPortableObject == null)
        {
            return true;
        }

        PlayPortableMoveToBag(new PortableMoveData(
            targetPortableObject,
            null,
            targetPortableObject,
            objectId,
            sourceWorldPosition,
            targetPortableObject.transform.position,
            0f));
        return true;
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

        if (!HasHandStackCapacityForItem(objectId))
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

        if (!HasHandStackCapacityForItem(objectId))
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

        if (handBag != null && handBag.HasVisualPreservedObject(0, objectId))
        {
            return true;
        }

        if (!HasHandStackCapacityForItem(objectId))
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

    public bool CanConvertHeldItem(int sourceItemId, int targetItemId)
    {
        if (sourceItemId < 0 || targetItemId < 0 || sourceItemId == targetItemId)
        {
            return false;
        }

        EnsureHandBag();
        if (handBag == null)
        {
            return false;
        }

        handBag.RefreshExternalStackCounts(false);
        int handCount = handBag.GetSlotCount(0);
        if (handCount <= 0 || handBag.GetSlotItemId(0) != sourceItemId)
        {
            return false;
        }

        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (itemManager == null || !itemManager.TryGetItemSetById(targetItemId, out _))
        {
            return false;
        }

        if (handCount == 1)
        {
            return handBag.GetSlotCapacityForItem(0, targetItemId) >= 1;
        }

        PlayerBag activeBag = GetBag();
        return activeBag != null && activeBag.GetAvailableCapacityForItem(targetItemId) >= 1;
    }

    public bool TryConvertHeldItem(int sourceItemId, int targetItemId)
    {
        if (!CanConvertHeldItem(sourceItemId, targetItemId))
        {
            return false;
        }

        int handCount = handBag.GetSlotCount(0);
        if (handCount == 1)
        {
            if (!handBag.SetSlotContents(0, targetItemId, 1, false, false))
            {
                return false;
            }

            handBag.ForceNotifyChanged();
            UpdateCarryState();
            return true;
        }

        PlayerBag activeBag = GetBag();
        if (activeBag == null || !activeBag.TryAddObject(targetItemId, out _))
        {
            return false;
        }

        if (!handBag.SetSlotContents(0, sourceItemId, handCount - 1, false, false))
        {
            activeBag.RemoveItems(targetItemId, 1);
            return false;
        }

        handBag.ForceNotifyChanged();
        UpdateCarryState();
        return true;
    }

    private bool HasHandStackCapacityForItem(int itemId)
    {
        int physicalCapacity = handStack != null ? handStack.Count : 0;
        if (physicalCapacity <= 0)
        {
            return false;
        }

        ItemDefinition definition = ResolveItemDefinition(itemId);
        int capacity = ItemDefinition.ResolveStackCapacity(definition, physicalCapacity);
        int occupiedCount = 0;
        for (int i = 0; i < handStack.Count; i++)
        {
            PortableObject portableObject = handStack[i];
            if (portableObject != null
                && (portableObject.gameObject.activeSelf || reservedHandStack.Contains(portableObject)))
            {
                occupiedCount++;
            }
        }

        return occupiedCount < capacity;
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
        int movableHandCount = handBag.GetSlotRemovableCount(0);
        if (movableHandCount <= 0)
        {
            return false;
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

        return activeBag.GetAvailableCapacityForItem(handItemId) >= movableHandCount;
    }

    public bool TryStoreHandItemsInBag()
    {
        EnsureHandBag();
        if (handBag == null)
        {
            return true;
        }

        handBag.RefreshExternalStackCounts(false);
        int movableHandCount = handBag.GetSlotRemovableCount(0);
        if (movableHandCount <= 0)
        {
            return false;
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
        List<PortableMoveData> pendingMoves = new List<PortableMoveData>(movableHandCount);

        for (int i = 0; i < movableHandCount; i++)
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
                sourcePortableObject,
                bagTargetPortableObject,
                handItemId,
                startPosition,
                targetPosition,
                i * Mathf.Max(0f, handToBagPortableMoveInterval)));
        }

        handBag.RemoveItems(handItemId, movableHandCount);
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

        Vector3 startPosition = ResolvePortableMoveStartPosition(moveData);
        PortableObject movingPortableObject = Instantiate(template, startPosition, template.transform.rotation);
        if (movingPortableObject == null)
        {
            return;
        }

        movingPortableObject.name = $"{template.name}_HandToBagMove";
        movingPortableObject.transform.SetParent(null, true);
        movingPortableObject.transform.position = startPosition;
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

        movingPortableObject.MoveTo(
            () => ResolvePortableMoveTargetPosition(moveData),
            moveData.delay,
            () => ResolvePortableMoveStartPosition(moveData),
            () =>
            {
                if (movingPortableObject != null)
                {
                    Destroy(movingPortableObject.gameObject);
                }
            },
            false);
    }

    private static Vector3 ResolvePortableMoveStartPosition(PortableMoveData moveData)
    {
        return moveData.sourcePortableObject != null
            ? moveData.sourcePortableObject.transform.position
            : moveData.startPosition;
    }

    private static Vector3 ResolvePortableMoveTargetPosition(PortableMoveData moveData)
    {
        return moveData.targetPortableObject != null
            ? moveData.targetPortableObject.transform.position
            : moveData.targetPosition;
    }

    private readonly struct PortableMoveData
    {
        public readonly PortableObject template;
        public readonly PortableObject sourcePortableObject;
        public readonly PortableObject targetPortableObject;
        public readonly int itemId;
        public readonly Vector3 startPosition;
        public readonly Vector3 targetPosition;
        public readonly float delay;

        public PortableMoveData(
            PortableObject template,
            PortableObject sourcePortableObject,
            PortableObject targetPortableObject,
            int itemId,
            Vector3 startPosition,
            Vector3 targetPosition,
            float delay)
        {
            this.template = template;
            this.sourcePortableObject = sourcePortableObject;
            this.targetPortableObject = targetPortableObject;
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
