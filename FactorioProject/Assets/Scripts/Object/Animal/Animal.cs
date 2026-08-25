using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Animal : MonoBehaviour
{
    [SerializeField]
    private GameObject saddleObject;

    private const int DeathAnimationState = 6;
    private const int FleeAnimationState = 8;
    private const int WalkAnimationState = 12;
    private const int RunAnimationState = 15;
    private const int WakeAnimationState = 17;
    private const float MinimumAttackAge = 4f;
    private const float MinimumSaddleAge = 7f;
    private const float StandUpCompletionNormalizedTime = 0.95f;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsEatingHash = Animator.StringToHash("IsEating");
    private static readonly int IsDrinkingHash = Animator.StringToHash("IsDrinking");
    private static readonly int IsRestingHash = Animator.StringToHash("IsResting");
    private static readonly int IsFleeingHash = Animator.StringToHash("IsFleeing");
    private static readonly int StateHash = Animator.StringToHash("State");
    private static readonly int ResetHash = Animator.StringToHash("Reset");
    private static readonly int IdleStateHash = Animator.StringToHash("Idle");
    private static readonly int DeathStateHash = Animator.StringToHash("Death");
    private static readonly int LieDownStateHash = Animator.StringToHash("IdlleToLay");
    private static readonly int SleepStateHash = Animator.StringToHash("Sleep");
    private static readonly int StandUpStateHash = Animator.StringToHash("LayToIdle");
    private static readonly int BaseColorHash = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorHash = Shader.PropertyToID("_Color");

    public enum AnimalGender
    {
        Male,
        Female
    }

    [SerializeField]
    private Animator anim;
    [FormerlySerializedAs("collider")]
    [SerializeField]
    private CapsuleCollider capsuleCollider;

    [Header("Identity")]
    [SerializeField, HideInInspector] private AnimalDefinition animalDefinition;
    [SerializeField] private AnimalGender animalGender = AnimalGender.Male;
    [Header("Age (0..10)")]
    [SerializeField, Range(0f, 10f)] private float DinoAge = 10f;
    [Header("Scale")]
    [Tooltip("Multiplier applied to the animal's original adult scale.")]
    [SerializeField, Min(0f)] private float BaseScale = 1f;
    [SerializeField] private float BabyScale = 0.5f;
    [SerializeField] private SkinnedMeshRenderer dinoRenderer;

    private SkinnedMeshRenderer outlineRenderer;
    private Renderer[] saddleOutlineRenderers;
    private bool hoverOutlineVisible;
    private bool focusedOutlineVisible;
    private MaterialPropertyBlock herdDebugPropertyBlock;
    private int animatorParameterMask;
    private bool animatorParameterCacheInitialized;
    private float currentHealth;
    private bool healthInitialized;
    private bool deathHandled;
    private AnimalWorldHealthBar worldHealthBar;
    private List<int> corpseLootItemIds;
    private int corpseLootIndex;
    private bool corpseLootInitialized;
    private bool corpseHarvestStepPrepared;
    private bool saddleEquipped;
    private Player mountedRider;
    private AnimalAIController cachedAIController;
    private Handcart attachedDraftHandcart;
    private bool hasPendingDraftHandcartRestore;
    private Vector2Int pendingDraftHandcartAnchorCoordinate;
    private long pendingDraftHandcartPlacementSequence;

    public GameObject Eye;
    private GameObject eyeLeftGO;
    private GameObject eyeRightGO;
    private SkinnedMeshRenderer eyeLeft;
    private SkinnedMeshRenderer eyeRight;
    private bool aiAnimationStateInitialized;
    private bool wakeFromRestRequested;
    private float lastAIAnimationSpeed;
    private int lastAIAnimationFlags;
    private bool detailedVisualsVisible = true;
    private bool detailedVisualsInitialized;
    [SerializeField] private Transform headBone;

    [SerializeField] private Transform dinoTransform;
    [SerializeField] private Transform youngDinoLeftEye;
    [SerializeField] private Transform youngDinoRightEye;
    [SerializeField] private Transform oldDinoLeftEye;
    [SerializeField] private Transform oldDinoRightEye;

    [Tooltip("The model scale used when the animal reaches age 10.")]
    [SerializeField] private Vector3 adultScale;
    private bool growthInitialized;

    public AnimalGender Gender => animalGender;
    public AnimalDefinition Definition => animalDefinition;
    public float Age => DinoAge;
    public float BaseScaleValue => BaseScale;
    public float MaxHealth => animalDefinition != null
        ? animalDefinition.MaxHealth
        : AnimalDefinition.DefaultMaxHealth;
    public float CurrentHealth
    {
        get
        {
            EnsureHealthInitialized();
            return currentHealth;
        }
    }
    public float NormalizedHealth => MaxHealth > 0f ? CurrentHealth / MaxHealth : 0f;
    public bool IsAlive => CurrentHealth > 0f && !deathHandled;
    public bool CanBeAttacked => IsAlive && DinoAge >= MinimumAttackAge;
    public bool IsSaddleEquipped => saddleEquipped;
    public Player MountedRider => mountedRider;
    public Handcart AttachedDraftHandcart => attachedDraftHandcart != null
        && attachedDraftHandcart.DraftAnimal == this
            ? attachedDraftHandcart
            : null;
    public bool IsAttachedToHandcart => AttachedDraftHandcart != null;
    public Transform MovementRoot
    {
        get
        {
            AnimalAIController controller = ResolveAIController();
            return controller != null ? controller.transform : transform;
        }
    }
    public Vector3 MovementRootPosition => MovementRoot.position;
    public bool CanEquipSaddle => animalDefinition != null
                                  && animalDefinition.CanBeRidden
                                  && saddleObject != null
                                  && IsAlive
                                  && !saddleEquipped
                                  && DinoAge >= MinimumSaddleAge;
    public bool CanBeMounted => animalDefinition != null
                                && animalDefinition.CanBeRidden
                                && saddleEquipped
                                && IsAlive
                                && mountedRider == null;
    public Transform SaddleMountPoint => saddleObject != null ? saddleObject.transform : null;
    public Vector3 RiderMountPosition
    {
        get
        {
            Transform mountPoint = SaddleMountPoint;
            if (mountPoint == null)
            {
                return transform.position;
            }

            Vector3 mountPosition = mountPoint.position;
            mountPosition.y = transform.position.y + RiderHeight;
            return mountPosition;
        }
    }
    private float RiderHeight
    {
        get
        {
            float adultRiderHeight = animalDefinition != null
                ? animalDefinition.RiderHeight
                : AnimalDefinition.DefaultRiderHeight;
            float normalizedAge = Mathf.Clamp01(DinoAge / AnimalDefinition.MaxSpawnAge);
            return adultRiderHeight * EvaluateGrowthScale(normalizedAge);
        }
    }
    public bool CanHarvestCorpse => !IsAlive && gameObject.activeInHierarchy;
    public bool HasTerrainInteraction
    {
        get
        {
            TerrainAnimalInstance instance = GetComponentInParent<TerrainAnimalInstance>();
            return instance != null && instance.HasInteracted;
        }
    }

    public void MarkTerrainInteraction()
    {
        TerrainAnimalInstance instance = GetComponentInParent<TerrainAnimalInstance>();
        if (instance != null)
        {
            instance.MarkInteracted();
        }
    }

    public void ConfigureHealth(AnimalDefinition definition, AnimalSaveEntry restoredState)
    {
        if (definition != null)
        {
            animalDefinition = definition;
            if (definition.TryGetDeclaredGender(out AnimalGender declaredGender))
            {
                animalGender = declaredGender;
            }
        }

        RestoreCorpseLootState(restoredState);
        currentHealth = restoredState != null && restoredState.hasHealth
            ? Mathf.Clamp(restoredState.currentHealth, 0f, MaxHealth)
            : MaxHealth;
        healthInitialized = true;
        deathHandled = false;
        wakeFromRestRequested = false;
        SetSaddleEquipped(restoredState != null && restoredState.hasSaddle);
        hasPendingDraftHandcartRestore = restoredState != null
                                         && restoredState.hasDraftHandcart;
        pendingDraftHandcartAnchorCoordinate = restoredState != null
            ? restoredState.draftHandcartAnchorCoordinate
            : default;
        pendingDraftHandcartPlacementSequence = restoredState != null
            ? restoredState.draftHandcartPlacementSequence
            : 0L;
        EnsureWorldHealthBar();
        if (currentHealth <= 0f)
        {
            HandleDeath();
        }
    }

    public float TakeDamage(float damage)
    {
        return TakeDamageInternal(damage, false, Vector3.zero);
    }

    public float TakeDamage(float damage, Vector3 threatPosition)
    {
        return TakeDamageInternal(damage, true, threatPosition);
    }

    private float TakeDamageInternal(
        float damage,
        bool notifyThreat,
        Vector3 threatPosition)
    {
        EnsureHealthInitialized();
        if (damage <= 0f || currentHealth <= 0f || deathHandled)
        {
            return 0f;
        }

        float previousHealth = currentHealth;
        currentHealth = Mathf.Max(0f, currentHealth - damage);
        MarkTerrainInteraction();
        if (currentHealth <= 0f)
        {
            HandleDeath();
        }
        else
        {
            if (worldHealthBar != null)
            {
                worldHealthBar.Refresh();
            }
        }

        if (notifyThreat)
        {
            float nearbyRadius = animalDefinition != null
                ? animalDefinition.AISettings.NearbyThreatRadius
                : AnimalAISettings.DefaultNearbyThreatRadius;
            AnimalAIWorld.Instance?.NotifyThreat(
                threatPosition,
                transform.position,
                nearbyRadius);
        }

        return previousHealth - currentHealth;
    }

    public float Heal(float amount, bool markTerrainInteraction = true)
    {
        EnsureHealthInitialized();
        if (amount <= 0f || currentHealth <= 0f || deathHandled)
        {
            return 0f;
        }

        float previousHealth = currentHealth;
        currentHealth = Mathf.Min(MaxHealth, currentHealth + amount);
        if (currentHealth > previousHealth)
        {
            if (markTerrainInteraction)
            {
                MarkTerrainInteraction();
            }

            if (worldHealthBar != null)
            {
                worldHealthBar.Refresh();
            }
        }

        return currentHealth - previousHealth;
    }

    public void CaptureHealthSaveState(AnimalSaveEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        EnsureHealthInitialized();
        entry.hasHealth = true;
        entry.currentHealth = currentHealth;
        entry.hasSaddle = saddleEquipped;
        Handcart handcart = AttachedDraftHandcart;
        if (handcart != null && handcart.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            entry.hasDraftHandcart = true;
            entry.draftHandcartAnchorCoordinate = anchorCoordinate;
            entry.draftHandcartPlacementSequence = handcart.RuntimePlacementSequence;
        }
        else
        {
            entry.hasDraftHandcart = hasPendingDraftHandcartRestore;
            entry.draftHandcartAnchorCoordinate = pendingDraftHandcartAnchorCoordinate;
            entry.draftHandcartPlacementSequence = pendingDraftHandcartPlacementSequence;
        }
        entry.corpseLootInitialized = corpseLootInitialized;
        entry.corpseRemainingItemIds ??= new List<int>();
        entry.corpseRemainingItemIds.Clear();
        for (int i = corpseLootIndex; corpseLootItemIds != null && i < corpseLootItemIds.Count; i++)
        {
            entry.corpseRemainingItemIds.Add(corpseLootItemIds[i]);
        }
    }

    public bool TryEquipSaddle()
    {
        if (!CanEquipSaddle)
        {
            return false;
        }

        SetSaddleEquipped(true);
        MarkTerrainInteraction();
        return true;
    }

    public bool TryMount(Player targetPlayer, PlayerController playerController)
    {
        Transform mountPoint = SaddleMountPoint;
        AnimalAIController aiController = ResolveAIController();
        if (!CanBeMounted
            || targetPlayer == null
            || playerController == null
            || mountPoint == null
            || aiController == null
            || !aiController.SetMountedRider(targetPlayer))
        {
            return false;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        terrain?.PinMountedAnimal(this);
        if (!playerController.TrySnapBodyToAnimalMountPoint(mountPoint, this))
        {
            aiController.SetMountedRider(null);
            terrain?.ReleaseMountedAnimal(this);
            return false;
        }

        mountedRider = targetPlayer;
        MarkTerrainInteraction();
        return true;
    }

    public void HandleMountedInput(
        Vector3 worldMoveDirection,
        bool runRequested,
        float deltaTime)
    {
        ResolveAIController()?.TryMoveMounted(worldMoveDirection, runRequested, deltaTime);
    }

    public void NotifyRiderDismounted(Player rider)
    {
        if (mountedRider != null && rider != null && mountedRider != rider)
        {
            return;
        }

        mountedRider = null;
        ResolveAIController()?.SetMountedRider(null);
        TerrainGenerator.ResolveActive()?.ReleaseMountedAnimal(this);
    }

    internal bool TrySetAttachedDraftHandcart(Handcart handcart)
    {
        Handcart currentHandcart = AttachedDraftHandcart;
        if (handcart == null
            || !IsAlive
            || currentHandcart != null && currentHandcart != handcart)
        {
            return false;
        }

        attachedDraftHandcart = handcart;
        hasPendingDraftHandcartRestore = false;
        ResolveAIController()?.SetDraftAttached(true);
        MarkTerrainInteraction();
        return true;
    }

    internal void ClearAttachedDraftHandcart(Handcart handcart)
    {
        if (attachedDraftHandcart != handcart)
        {
            return;
        }

        attachedDraftHandcart = null;
        hasPendingDraftHandcartRestore = false;
        ResolveAIController()?.SetDraftAttached(false);
        MarkTerrainInteraction();
    }

    internal bool TryMoveAttachedHandcart(
        Vector3 worldMoveDirection,
        float animalMoveSpeed,
        float deltaTime,
        Player animalRider,
        out float actualMoveSpeed)
    {
        Handcart handcart = AttachedDraftHandcart;
        if (handcart == null)
        {
            actualMoveSpeed = 0f;
            return false;
        }

        return handcart.TryMovePulledByAnimal(
            this,
            worldMoveDirection,
            animalMoveSpeed,
            deltaTime,
            animalRider,
            out actualMoveSpeed);
    }

    internal void ApplyDraftPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        AnimalAIController controller = ResolveAIController();
        if (controller != null)
        {
            controller.ApplyExternalControlledPose(worldPosition, worldRotation);
            return;
        }

        transform.SetPositionAndRotation(worldPosition, worldRotation);
    }

    internal bool TryRestorePendingDraftHandcart()
    {
        if (!hasPendingDraftHandcartRestore
            || !IsAlive
            || !Handcart.TryFindByPlacementRuntime(
                pendingDraftHandcartAnchorCoordinate,
                pendingDraftHandcartPlacementSequence,
                out Handcart handcart)
            || !handcart.TryAttachDraftAnimal(this))
        {
            return false;
        }

        hasPendingDraftHandcartRestore = false;
        return true;
    }

    private AnimalAIController ResolveAIController()
    {
        if (cachedAIController == null)
        {
            cachedAIController = GetComponentInParent<AnimalAIController>();
        }

        return cachedAIController;
    }

    private void SetSaddleEquipped(bool equipped)
    {
        saddleEquipped = equipped
                         && animalDefinition != null
                         && animalDefinition.CanBeRidden
                         && saddleObject != null;
        if (saddleObject != null && saddleObject.activeSelf != saddleEquipped)
        {
            saddleObject.SetActive(saddleEquipped);
        }

        CacheSaddleOutlineRenderers();
    }

    private void CacheSaddleOutlineRenderers()
    {
        if (saddleObject != null && saddleOutlineRenderers == null)
        {
            saddleOutlineRenderers = saddleObject.GetComponentsInChildren<Renderer>(true);
        }
    }

    public bool PrepareCorpseHarvestStep()
    {
        if (!CanHarvestCorpse || corpseHarvestStepPrepared)
        {
            return false;
        }

        EnsureCorpseLootInitialized();
        corpseHarvestStepPrepared = true;
        return true;
    }

    public bool TryGetPreparedCorpseLootItem(out int itemId)
    {
        itemId = -1;
        if (!corpseHarvestStepPrepared
            || corpseLootItemIds == null
            || corpseLootIndex < 0
            || corpseLootIndex >= corpseLootItemIds.Count)
        {
            return false;
        }

        itemId = corpseLootItemIds[corpseLootIndex];
        return itemId >= 0;
    }

    public ItemDefinition ResolveCorpseLootItemDefinition(int itemId)
    {
        IReadOnlyList<AnimalDropEntry> dropItems = animalDefinition != null
            ? animalDefinition.DropItems
            : null;
        for (int i = 0; dropItems != null && i < dropItems.Count; i++)
        {
            ItemDefinition definition = dropItems[i]?.ItemDefinition;
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    public bool CommitPreparedCorpseHarvestStep(bool rewardDelivered)
    {
        if (!corpseHarvestStepPrepared || !CanHarvestCorpse)
        {
            return false;
        }

        bool hasReward = corpseLootItemIds != null
                         && corpseLootIndex >= 0
                         && corpseLootIndex < corpseLootItemIds.Count;
        if (hasReward && !rewardDelivered)
        {
            return false;
        }

        corpseHarvestStepPrepared = false;
        if (hasReward)
        {
            corpseLootIndex++;
        }

        MarkTerrainInteraction();
        return true;
    }

    public void CancelPreparedCorpseHarvestStep()
    {
        corpseHarvestStepPrepared = false;
    }

    public bool HasRemainingCorpseLoot =>
        corpseLootItemIds != null && corpseLootIndex < corpseLootItemIds.Count;

    public Vector3 GetCorpseLootOrigin()
    {
        return GetWorldCenter();
    }

    public Vector3 GetWorldCenter()
    {
        if (dinoRenderer == null)
        {
            dinoRenderer = FindGrowthRenderer();
        }

        return dinoRenderer != null ? dinoRenderer.bounds.center : transform.position;
    }

    public float GetWorldRadius()
    {
        if (dinoRenderer == null)
        {
            dinoRenderer = FindGrowthRenderer();
        }

        if (dinoRenderer == null)
        {
            return 0.5f;
        }

        Vector3 extents = dinoRenderer.bounds.extents;
        return Mathf.Max(0.25f, Mathf.Max(extents.x, extents.z));
    }

    private void EnsureHealthInitialized()
    {
        if (healthInitialized)
        {
            return;
        }

        currentHealth = MaxHealth;
        healthInitialized = true;
        deathHandled = false;
        EnsureWorldHealthBar();
    }

    private void EnsureWorldHealthBar()
    {
        if (worldHealthBar != null)
        {
            worldHealthBar.Refresh();
            return;
        }

        if (dinoRenderer == null)
        {
            dinoRenderer = FindGrowthRenderer();
        }

        worldHealthBar = AnimalWorldHealthBar.Create(this, dinoRenderer);
    }

    public void NotifyAttackAnimationStarted()
    {
        if (!IsAlive)
        {
            return;
        }

        EnsureWorldHealthBar();
        worldHealthBar?.NotifyAttackAnimationStarted();
    }

    private void HandleDeath()
    {
        if (deathHandled)
        {
            return;
        }

        deathHandled = true;
        hasPendingDraftHandcartRestore = false;
        attachedDraftHandcart?.DetachDraftAnimal(this);
        AnimalAIController controller = GetComponentInParent<AnimalAIController>();
        if (controller != null)
        {
            controller.StopForDeath();
        }

        ClearFocusVisuals();
        EnsureCorpseLootInitialized();
        PlayDeathAnimation();
    }

    private void RestoreCorpseLootState(AnimalSaveEntry restoredState)
    {
        corpseLootIndex = 0;
        corpseHarvestStepPrepared = false;
        corpseLootInitialized = restoredState != null && restoredState.corpseLootInitialized;
        corpseLootItemIds = null;
        List<int> restoredItems = restoredState != null
            ? restoredState.corpseRemainingItemIds
            : null;
        if (!corpseLootInitialized || restoredItems == null || restoredItems.Count == 0)
        {
            return;
        }

        corpseLootItemIds = new List<int>(restoredItems.Count);
        for (int i = 0; i < restoredItems.Count; i++)
        {
            if (restoredItems[i] >= 0)
            {
                corpseLootItemIds.Add(restoredItems[i]);
            }
        }
    }

    private void EnsureCorpseLootInitialized()
    {
        if (corpseLootInitialized)
        {
            return;
        }

        corpseLootInitialized = true;
        IReadOnlyList<AnimalDropEntry> dropItems = animalDefinition != null
            ? animalDefinition.DropItems
            : null;
        if (dropItems == null || dropItems.Count == 0)
        {
            return;
        }

        System.Random random = new System.Random(BuildCorpseLootSeed());
        for (int i = 0; i < dropItems.Count; i++)
        {
            AnimalDropEntry entry = dropItems[i];
            ItemDefinition itemDefinition = entry?.ItemDefinition;
            if (itemDefinition == null
                || itemDefinition.id < 0
                || random.NextDouble() >= entry.DropChance)
            {
                continue;
            }

            int minAmount = entry.MinAmount;
            int maxAmount = entry.MaxAmount;
            int amount = maxAmount > minAmount
                ? minAmount + (int)(random.NextDouble() * ((long)maxAmount - minAmount + 1L))
                : minAmount;
            if (amount <= 0)
            {
                continue;
            }

            corpseLootItemIds ??= new List<int>(amount);
            for (int itemIndex = 0; itemIndex < amount; itemIndex++)
            {
                corpseLootItemIds.Add(itemDefinition.id);
            }
        }
    }

    private int BuildCorpseLootSeed()
    {
        TerrainAnimalInstance instance = GetComponentInParent<TerrainAnimalInstance>();
        long deterministicId = instance != null ? instance.DeterministicId : GetInstanceID();
        unchecked
        {
            int seed = (int)deterministicId ^ (int)(deterministicId >> 32);
            seed = (seed * 397) ^ (animalDefinition != null ? animalDefinition.Id : 0);
            return seed;
        }
    }

    private void PlayDeathAnimation()
    {
        wakeFromRestRequested = false;
        if (anim == null)
        {
            anim = GetComponent<Animator>();
        }

        if (anim == null)
        {
            return;
        }

        AnimatorStateInfo currentState = anim.GetCurrentAnimatorStateInfo(0);
        bool currentStateIsDeath = currentState.shortNameHash == DeathStateHash;
        bool nextStateIsDeath = anim.IsInTransition(0)
                                && anim.GetNextAnimatorStateInfo(0).shortNameHash
                                == DeathStateHash;
        if (currentStateIsDeath || nextStateIsDeath)
        {
            return;
        }

        bool transitioningToIdle = anim.IsInTransition(0)
                                   && anim.GetNextAnimatorStateInfo(0).shortNameHash
                                   == IdleStateHash;
        if (currentState.shortNameHash != IdleStateHash && !transitioningToIdle)
        {
            anim.SetTrigger(ResetHash);
        }

        anim.SetInteger(StateHash, DeathAnimationState);
    }

    private void Reset()
    {
        anim = GetComponent<Animator>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        AlignCapsuleColliderBottomToPivot();
        dinoTransform = transform;
        dinoRenderer = FindGrowthRenderer();
        headBone = FindDescendantByExactName(transform, "head");
        youngDinoLeftEye = FindDescendantByNamePrefix(transform, "EyeBabyPlace_L", "EyePlaceBaby_L");
        youngDinoRightEye = FindDescendantByNamePrefix(transform, "EyeBabyPlace_R", "EyePlaceBaby_R");
        oldDinoLeftEye = FindDescendantByNamePrefix(transform, "EyePlace_L");
        oldDinoRightEye = FindDescendantByNamePrefix(transform, "EyePlace_R");

#if UNITY_EDITOR
        Eye = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/GrigoriyArx/DinoGK/Prefabs/Eyes/Eye.prefab");
        Transform sourceTransform = PrefabUtility.GetCorrespondingObjectFromOriginalSource(dinoTransform);
        adultScale = sourceTransform != null ? sourceTransform.localScale : dinoTransform.localScale;
#else
        adultScale = dinoTransform.localScale;
#endif

        growthInitialized = false;
        if (InitializeGrowth())
        {
            SetGrowth(DinoAge * 0.1f);
        }
    }

    private void Awake()
    {
        if (saddleObject == null)
        {
            Transform saddleTransform = FindDescendantByExactName(transform, "Saddle");
            saddleObject = saddleTransform != null ? saddleTransform.gameObject : null;
        }

        SetSaddleEquipped(false);
        anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }

        eyeLeftGO = Instantiate(Eye, oldDinoLeftEye.position, oldDinoLeftEye.rotation);
        eyeRightGO = Instantiate(Eye, oldDinoRightEye.position, oldDinoRightEye.rotation);

        if (headBone != null)
        {
            eyeLeftGO.transform.SetParent(headBone, false);
            eyeRightGO.transform.SetParent(headBone, false);
        }
        else
        {
            Debug.LogError("Parent bone is not assigned!");
        }

        eyeLeft = eyeLeftGO.GetComponent<SkinnedMeshRenderer>();
        eyeRight = eyeRightGO.GetComponent<SkinnedMeshRenderer>();
        ConfigureSkinnedRenderer(dinoRenderer, true);
        ConfigureSkinnedRenderer(eyeLeft, false);
        ConfigureSkinnedRenderer(eyeRight, false);
    }

    private void Start()
    {
        if (anim == null)
        {
            anim = GetComponent<Animator>();
        }

        InitializeGrowth();
        SetAge(DinoAge);
    }

    private static void ConfigureSkinnedRenderer(
        SkinnedMeshRenderer renderer,
        bool castsShadows)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.updateWhenOffscreen = false;
        renderer.skinnedMotionVectors = false;
        renderer.allowOcclusionWhenDynamic = true;
        renderer.quality = SkinQuality.Bone2;
        renderer.shadowCastingMode = castsShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    public void SetDetailedVisuals(bool visible)
    {
        if (detailedVisualsInitialized && detailedVisualsVisible == visible)
        {
            return;
        }

        detailedVisualsInitialized = true;
        detailedVisualsVisible = visible;
        if (eyeLeft != null)
        {
            eyeLeft.enabled = visible;
        }

        if (eyeRight != null)
        {
            eyeRight.enabled = visible;
        }
    }

    private void OnDisable()
    {
        ClearFocusVisuals();
    }

    private void OnDestroy()
    {
        hasPendingDraftHandcartRestore = false;
        attachedDraftHandcart?.DetachDraftAnimal(this);
    }

    private void ClearFocusVisuals()
    {
        if (outlineRenderer != null)
        {
            AnimalScreenSpaceOutline.HideHovered(outlineRenderer);
            AnimalScreenSpaceOutline.HideFocused(outlineRenderer);
        }

        hoverOutlineVisible = false;
        focusedOutlineVisible = false;
        if (worldHealthBar != null)
        {
            worldHealthBar.HideImmediately();
        }
    }

    public void SetHoverOutline(bool visible)
    {
        if (hoverOutlineVisible == visible)
        {
            return;
        }

        if (!TryResolveOutlineRenderer())
        {
            return;
        }

        hoverOutlineVisible = visible;
        if (visible)
        {
            AnimalScreenSpaceOutline.ShowHovered(outlineRenderer);
        }
        else
        {
            AnimalScreenSpaceOutline.HideHovered(outlineRenderer);
        }
    }

    public void SetFocusedOutline(bool visible)
    {
        if (focusedOutlineVisible == visible)
        {
            return;
        }

        focusedOutlineVisible = visible;
        if (!TryResolveOutlineRenderer())
        {
            return;
        }

        if (visible)
        {
            AnimalScreenSpaceOutline.ShowFocused(outlineRenderer);
        }
        else
        {
            AnimalScreenSpaceOutline.HideFocused(outlineRenderer);
        }
    }

    private bool TryResolveOutlineRenderer()
    {
        if (outlineRenderer == null)
        {
            outlineRenderer = dinoRenderer != null
                ? dinoRenderer
                : GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        return outlineRenderer != null;
    }

    public int CopyOutlineMaskRenderers(Renderer[] destination)
    {
        if (destination == null || destination.Length == 0 || !TryResolveOutlineRenderer())
        {
            return 0;
        }

        int count = 0;
        count = AddOutlineMaskRenderer(destination, count, outlineRenderer);
        count = AddOutlineMaskRenderer(destination, count, eyeLeft);
        count = AddOutlineMaskRenderer(destination, count, eyeRight);

        CacheSaddleOutlineRenderers();
        for (int i = 0; saddleOutlineRenderers != null && i < saddleOutlineRenderers.Length; i++)
        {
            // 동물과 안장을 같은 마스크에 합쳐 접촉면이 아닌 전체 외곽선만 그린다.
            count = AddOutlineMaskRenderer(destination, count, saddleOutlineRenderers[i]);
        }

        return count;
    }

    private static int AddOutlineMaskRenderer(Renderer[] destination, int count, Renderer renderer)
    {
        if (count >= destination.Length
            || renderer == null
            || !renderer.enabled
            || !renderer.gameObject.activeInHierarchy)
        {
            return count;
        }

        for (int i = 0; i < count; i++)
        {
            if (destination[i] == renderer)
            {
                return count;
            }
        }

        destination[count] = renderer;
        return count + 1;
    }

    public void SetAge(float age)
    {
        DinoAge = Mathf.Clamp(age, 0f, 10f);
        if (growthInitialized || InitializeGrowth())
        {
            SetGrowth(DinoAge * 0.1f);
        }
    }

    public void SetBaseScale(float scale)
    {
        BaseScale = Mathf.Max(0f, scale);
        if (growthInitialized || InitializeGrowth())
        {
            SetGrowth(DinoAge * 0.1f);
        }
    }

    public void SetAIAnimation(
        float speed,
        bool isEating,
        bool isDrinking,
        bool isResting,
        bool isLookingAround,
        bool isFleeing,
        bool isRunning = false)
    {
        if (!IsAlive)
        {
            PlayDeathAnimation();
            return;
        }

        if (anim == null)
        {
            anim = GetComponent<Animator>();
        }

        if (anim == null)
        {
            return;
        }

        // Sleep은 전용 State=17을 거쳐야 LayToIdle로 전환된다. 기상 중 일반 AI 상태가
        // State=0을 덮어쓰면 Sleep에 남으므로 기상이 끝날 때까지 전용 전환을 유지한다.
        if (wakeFromRestRequested)
        {
            return;
        }

        int animationFlags = (isEating ? 1 : 0)
                             | (isDrinking ? 2 : 0)
                             | (isResting ? 4 : 0)
                             | (isLookingAround ? 8 : 0)
                             | (isFleeing ? 16 : 0)
                             | (isRunning ? 32 : 0);
        if (aiAnimationStateInitialized
            && Mathf.Abs(lastAIAnimationSpeed - speed) <= 0.001f
            && lastAIAnimationFlags == animationFlags)
        {
            return;
        }

        aiAnimationStateInitialized = true;
        lastAIAnimationSpeed = speed;
        lastAIAnimationFlags = animationFlags;

        EnsureAnimatorParameterCache();
        SetAnimatorFloatIfAvailable(SpeedHash, speed, 1);
        SetAnimatorBoolIfAvailable(IsEatingHash, isEating, 2);
        SetAnimatorBoolIfAvailable(IsDrinkingHash, isDrinking, 4);
        SetAnimatorBoolIfAvailable(IsRestingHash, isResting, 8);
        SetAnimatorBoolIfAvailable(IsFleeingHash, isFleeing, 16);

        int legacyState = isFleeing
            ? FleeAnimationState
            : isRunning && speed > 0.01f
                ? RunAnimationState
                : speed > 0.01f
                    ? WalkAnimationState
                    : isEating || isDrinking
                        ? 11
                        : isResting
                            ? 16
                            : isLookingAround
                                ? 14
                                : 0;
        SwitchAnimation(legacyState);
    }

    public void WakeFromRest()
    {
        if (!IsAlive || wakeFromRestRequested)
        {
            return;
        }

        if (anim == null)
        {
            anim = GetComponent<Animator>();
        }

        if (anim == null || !anim.isActiveAndEnabled || !anim.isInitialized)
        {
            return;
        }

        wakeFromRestRequested = true;
        aiAnimationStateInitialized = false;
        EnsureAnimatorParameterCache();
        SetAnimatorFloatIfAvailable(SpeedHash, 0f, 1);
        SetAnimatorBoolIfAvailable(IsEatingHash, false, 2);
        SetAnimatorBoolIfAvailable(IsDrinkingHash, false, 4);
        SetAnimatorBoolIfAvailable(IsRestingHash, false, 8);
        SetAnimatorBoolIfAvailable(IsFleeingHash, false, 16);

        // 이 애니메이터의 Sleep -> LayToIdle 조건은 Reset이 아니라 State == 17이다.
        anim.ResetTrigger(ResetHash);
        anim.SetInteger(StateHash, WakeAnimationState);
    }

    public bool IsReadyForAIMovement()
    {
        if (anim == null)
        {
            anim = GetComponent<Animator>();
        }

        if (anim == null
            || !anim.isActiveAndEnabled
            || !anim.isInitialized
            || anim.layerCount == 0)
        {
            return true;
        }

        AnimatorStateInfo currentState = anim.GetCurrentAnimatorStateInfo(0);
        if (wakeFromRestRequested)
        {
            if (currentState.shortNameHash == StandUpStateHash
                && currentState.normalizedTime >= StandUpCompletionNormalizedTime
                && !anim.IsInTransition(0))
            {
                wakeFromRestRequested = false;
                aiAnimationStateInitialized = false;
                SwitchAnimation(0);
                return false;
            }

            bool currentIsWakeAnimation = IsRestAnimationState(currentState.shortNameHash);
            bool nextIsWakeAnimation = anim.IsInTransition(0)
                                       && IsRestAnimationState(
                                           anim.GetNextAnimatorStateInfo(0).shortNameHash);
            if (currentIsWakeAnimation || nextIsWakeAnimation)
            {
                return false;
            }

            wakeFromRestRequested = false;
            aiAnimationStateInitialized = false;
            SwitchAnimation(0);
        }

        if (IsRestAnimationState(currentState.shortNameHash))
        {
            return false;
        }

        if (!anim.IsInTransition(0))
        {
            return true;
        }

        AnimatorStateInfo nextState = anim.GetNextAnimatorStateInfo(0);
        return !IsRestAnimationState(nextState.shortNameHash);
    }

    public void SetHerdDebugColor(Color color, bool visible)
    {
        if (dinoRenderer == null)
        {
            dinoRenderer = FindGrowthRenderer();
        }

        if (dinoRenderer == null)
        {
            return;
        }

        if (!visible)
        {
            dinoRenderer.SetPropertyBlock(null);
            return;
        }

        herdDebugPropertyBlock ??= new MaterialPropertyBlock();
        herdDebugPropertyBlock.Clear();
        herdDebugPropertyBlock.SetColor(BaseColorHash, color);
        herdDebugPropertyBlock.SetColor(ColorHash, color);
        dinoRenderer.SetPropertyBlock(herdDebugPropertyBlock);
    }

    private void OnValidate()
    {
        DinoAge = Mathf.Clamp(DinoAge, 0f, 10f);
        BaseScale = Mathf.Max(0f, BaseScale);
        AlignCapsuleColliderBottomToPivot();
        if (InitializeGrowth())
        {
            SetGrowth(DinoAge * 0.1f);
        }
    }

    private void AlignCapsuleColliderBottomToPivot()
    {
        if (capsuleCollider == null)
        {
            return;
        }

        float centerOffset = Mathf.Max(capsuleCollider.height, capsuleCollider.radius * 2f) * 0.5f;
        Vector3 center = capsuleCollider.center;
        switch (capsuleCollider.direction)
        {
            case 0:
                center.x = centerOffset;
                break;
            case 2:
                center.z = centerOffset;
                break;
            default:
                center.y = centerOffset;
                break;
        }

        capsuleCollider.center = center;
    }

    private bool InitializeGrowth()
    {
        if (dinoTransform == null)
        {
            growthInitialized = false;
            return false;
        }

        if (adultScale == Vector3.zero)
        {
            adultScale = dinoTransform.localScale;
        }

        growthInitialized = true;
        return true;
    }

    private void SetGrowth(float t)
    {
        t = Mathf.Clamp01(t);
        Vector3 baseAdultScale = adultScale * BaseScale;
        dinoTransform.localScale = baseAdultScale * EvaluateGrowthScale(t);

        if (eyeLeft != null
            && eyeRight != null
            && youngDinoLeftEye != null
            && youngDinoRightEye != null
            && oldDinoLeftEye != null
            && oldDinoRightEye != null)
        {
            eyeLeft.transform.position = Vector3.Lerp(youngDinoLeftEye.position, oldDinoLeftEye.position, t);
            eyeRight.transform.position = Vector3.Lerp(youngDinoRightEye.position, oldDinoRightEye.position, t);

            eyeLeft.transform.localScale = Vector3.Lerp(youngDinoLeftEye.localScale, oldDinoLeftEye.localScale, t);
            eyeRight.transform.localScale = Vector3.Lerp(youngDinoRightEye.localScale, oldDinoRightEye.localScale, t);

            eyeLeft.transform.rotation = Quaternion.Lerp(youngDinoLeftEye.rotation, oldDinoLeftEye.rotation, t);
            eyeRight.transform.rotation = Quaternion.Lerp(youngDinoRightEye.rotation, oldDinoRightEye.rotation, t);
        }

        if (dinoRenderer != null
            && dinoRenderer.sharedMesh != null
            && dinoRenderer.sharedMesh.blendShapeCount > 0)
        {
            dinoRenderer.SetBlendShapeWeight(0, (1f - t) * 100f);
        }
    }

    private float EvaluateGrowthScale(float normalizedAge)
    {
        return Mathf.Lerp(BabyScale, 1f, normalizedAge);
    }

    private void SwitchAnimation(int targetState)
    {
        if (anim == null)
        {
            return;
        }

        int currentState = anim.GetInteger(StateHash);
        if (currentState == targetState)
        {
            return;
        }

        if (currentState != 0 && currentState < 97)
        {
            anim.SetTrigger(ResetHash);
        }

        anim.SetInteger(StateHash, targetState);
    }

    private static bool IsRestAnimationState(int stateHash)
    {
        return IsRestPoseAnimationState(stateHash)
               || stateHash == StandUpStateHash;
    }

    private static bool IsRestPoseAnimationState(int stateHash)
    {
        return stateHash == LieDownStateHash || stateHash == SleepStateHash;
    }

    private void EnsureAnimatorParameterCache()
    {
        if (animatorParameterCacheInitialized || anim == null)
        {
            return;
        }

        animatorParameterCacheInitialized = true;
        animatorParameterMask = 0;
        AnimatorControllerParameter[] parameters = anim.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            int hash = parameters[i].nameHash;
            if (hash == SpeedHash)
            {
                animatorParameterMask |= 1;
            }
            else if (hash == IsEatingHash)
            {
                animatorParameterMask |= 2;
            }
            else if (hash == IsDrinkingHash)
            {
                animatorParameterMask |= 4;
            }
            else if (hash == IsRestingHash)
            {
                animatorParameterMask |= 8;
            }
            else if (hash == IsFleeingHash)
            {
                animatorParameterMask |= 16;
            }
        }
    }

    private void SetAnimatorFloatIfAvailable(int parameterHash, float value, int mask)
    {
        if ((animatorParameterMask & mask) != 0)
        {
            anim.SetFloat(parameterHash, value);
        }
    }

    private void SetAnimatorBoolIfAvailable(int parameterHash, bool value, int mask)
    {
        if ((animatorParameterMask & mask) != 0)
        {
            anim.SetBool(parameterHash, value);
        }
    }

    private SkinnedMeshRenderer FindGrowthRenderer()
    {
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer candidate = renderers[i];
            if (candidate != null
                && candidate.sharedMesh != null
                && candidate.sharedMesh.blendShapeCount > 0)
            {
                return candidate;
            }
        }

        return renderers.Length > 0 ? renderers[0] : null;
    }

    private static Transform FindDescendantByExactName(Transform root, string targetName)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, targetName, System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            Transform nested = FindDescendantByExactName(child, targetName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static Transform FindDescendantByNamePrefix(
        Transform root,
        string primaryPrefix,
        string alternatePrefix = null)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            string childName = child.name;
            if (childName.StartsWith(primaryPrefix, System.StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(alternatePrefix)
                    && childName.StartsWith(alternatePrefix, System.StringComparison.OrdinalIgnoreCase)))
            {
                return child;
            }

            Transform nested = FindDescendantByNamePrefix(child, primaryPrefix, alternatePrefix);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
