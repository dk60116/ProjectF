using UnityEngine;
using ProjectF.Animals;
using ProjectF.Rendering;

public enum AnimalAIState
{
    Idle,
    Wander,
    Graze,
    Drink,
    Rest,
    LookAround,
    Flee,
    Eat
}

[DisallowMultipleComponent]
public sealed class AnimalAIController : MonoBehaviour
{
    private enum MovementAvailability
    {
        Clear,
        BlockedByAnimal,
        BlockedByStaticObstacle
    }

    private const string LiveAnimalLayerName = "Animal";
    private const int TargetSearchAttempts = 24;
    private const int TargetPathSearchAttempts = 4;
    private const int MaxNavigationWaypoints = 96;
    public const int MaxRemainingNavigationPathPoints = MaxNavigationWaypoints + 2;
    private const float BlockedRepathDelay = 0.3f;
    private const float AbandonBlockedTargetDelay = 1.5f;
    private const float RepathCooldown = 0.5f;
    private const float FleeTargetOvershoot = 0.5f;
    private const float ExtendedNavigationMargin = 8f;
    private const float MaxExtendedNavigationRadius = 64f;
    private const float AvoidanceExitClearDuration = 0.3f;
    private const float MaximumRotationDeltaTime = 1f / 30f;
    private const float ObstacleClearanceSkin = 0.03f;
    private const float SeparationSmoothingRate = 8f;
    private const float MaximumCrowdSteering = 0.65f;
    private const float NavigationProgressEpsilon = 0.04f;
    private const float HerdReturnRetryDelay = 5f;
    private const float LocalRoamingSearchRadius = 6f;
    private const float FeedingDuration = 1f;
    private const float IntervalBetweenMeals = 0.5f;
    private const float BlockedFoodRetryDelay = 2f;
    private const float AgeGenderSpeedMultiplierInfluence = 1f / 3f;
    private const float MountedMovementDirectionEpsilonSqr = 0.0000001f;
    private const float DormantDefecationSpreadRadius = 1.5f;
    private const float MountedRunTransitionMinimumMargin = 0.01f;
    private const float MountedRunTransitionRelativeMargin = 0.03f;
    private const float HealthRecoveryFractionPerSecond = 0.05f;
    private const float PostAggroHealthRecoveryDelay = 1f;
    private const float SaddledFreeRoamRadiusMultiplier = 0.2f;
    private const float SaddledFreeMovementWeightMultiplier = 0.15f;
    private const float MinimumSaddledFreeRoamRadius = 2f;
    private const float MaximumSaddledFreeRoamRadius = 6f;

    private static readonly float[] AvoidanceAngles =
    {
        30f,
        60f,
        90f,
        120f,
        150f,
        180f
    };

    private static readonly float[] FleeTargetAngles =
    {
        0f,
        30f,
        -30f,
        60f,
        -60f,
        90f,
        -90f
    };

    private static readonly float[] PlayerPushAngles =
    {
        0f,
        30f,
        -30f,
        60f,
        -60f,
        90f,
        -90f,
        120f,
        -120f,
        150f,
        -150f,
        180f
    };

    private Animal animal;
    private AnimalDefinition definition;
    private TerrainAnimalInstance terrainInstance;
    private AnimalAISettings settings;
    private AnimalAIState currentState;
    private AnimalTickTimer stateTimeRemaining;
    private AnimalFixedPosition fixedTarget;
    private Vector3 targetPosition { get => fixedTarget.Value; set => fixedTarget = new AnimalFixedPosition(value); }
    private bool hasTarget;
    private bool movingToActivity;
    private uint randomState;
    private bool configured;
    private bool executionActive;
    private bool behaviorAnimationActivityInitialized;
    private bool nooseLeashed;
    private bool draftAttached;
    private Player mountedRider;
    private float mountedCurrentSpeed;
    private float mountedMaximumSpeed;
    private Vector3 mountedMovementDirection;
    private bool mountedRunAnimationActive;
    private long scheduledTicks;
    private AnimalNeedsSchedule needsSchedule;
    private long scheduledRecoveryTicks;
    private uint scheduledTickPhase;
    private bool scheduledTickPhaseApplied;
    private Vector3 presentationStartPosition;
    private Vector3 presentationTargetPosition;
    private Quaternion presentationStartRotation;
    private Quaternion presentationTargetRotation;
    private float presentationDuration;
    private float presentationElapsed;
    private bool presentationActive;
    private AnimalFixedPosition fixedPosition;
    private Vector3 simulationPosition { get => fixedPosition.Value; set => fixedPosition = new AnimalFixedPosition(value); }
    private int simulationYaw;
    private Quaternion simulationRotation
    {
        get => Quaternion.Euler(0f, AnimalSimulationMath.Degrees(simulationYaw), 0f);
        set => simulationYaw = AnimalSimulationMath.Angle(value.eulerAngles.y);
    }
    private bool simulationPoseInitialized;
    private float standUpDuration = 1f;
    private float feedingDuration = FeedingDuration;
    private bool standUpPending;
    private long standUpReadyTick;
    private bool waitingForStandUp
    {
        get => standUpPending;
        set
        {
            if (value && !standUpPending)
                standUpReadyTick = (AnimalAIWorld.Instance?.NeedsTick ?? 0L) + AnimalSimulationMath.Ticks(standUpDuration);
            standUpPending = value;
        }
    }
    private bool detailedVisualsInitialized;
    private bool detailedVisualsVisible;
    private long fallbackSimulationId;
    private Vector3[] navigationWaypoints;
    private int navigationWaypointCount;
    private int navigationWaypointIndex;
    private Vector3 navigationGoal;
    private bool navigationPrepared;
    private long navigationRevision;
    private AnimalTickTimer navigationBlockedTime;
    private AnimalTickTimer navigationRepathCooldown;
    private Vector3 navigationProgressTarget;
    private float navigationBestDistance;
    private AnimalTickTimer navigationNoProgressTime;
    private bool navigationProgressTracked;
    private AnimalTickTimer herdReturnRetryCooldown;
    private bool herdReturnTargetActive;
    private bool preferReachableFallbackTarget;
    private bool terrainEscapeActive;
    private Vector3 terrainEscapeTarget;
    private Vector3 fleeThreatPosition;
    private bool hasFleeThreat;
    private AnimalTickTimer postAggroHealthRecoveryDelayRemaining = -1f;
    private int fleeRouteAttemptOffset;
    private int forcedThreatPulseCount;
    private Collider[] animalColliders;
    private int[] originalAnimalColliderLayers;
    private float avoidanceColliderRadius = 0.5f;
    private Vector3 committedAvoidanceDirection;
    private AnimalTickTimer avoidanceDirectClearTime;
    private float avoidanceTurnSign = 1f;
    private Vector3 smoothedSeparation;
    private Vector3 crowdSnapshotPosition;
    private bool crowdSnapshotValid;
    private int reachableFallbackTargetCount;
    private int stuckTargetAbandonCount;
    private int herdReturnSuppressionCount;
    private Vector3 saddledFreeRoamCenter;
    private bool saddledFreeRoamCenterInitialized;
    private Vector2Int foodTargetCoordinate;
    private AnimalTickTimer foodSearchCooldown;
    private TerrainGenerator feedingTerrain;
    internal bool IsConsumingDroppedFood => executionActive && animal != null && animal.IsAlive
        && currentState == AnimalAIState.Eat && !hasTarget && stateTimeRemaining > 0f;
    private System.Func<Vector3, bool, bool> foodReachabilityFilter;
    private Vector3[] foodNavigationScratch;

    public Animal Animal => animal;
    public AnimalDefinition Definition => definition;
    public TerrainAnimalInstance TerrainInstance => terrainInstance;
    public long SimulationId => ResolveSimulationId();
    public AnimalAIState CurrentState => currentState;
    public float StateTimeRemaining => stateTimeRemaining;
    public Vector3 TargetPosition => targetPosition;
    public bool HasTarget => hasTarget;
    public long HerdId => terrainInstance != null ? terrainInstance.HerdId : 0L;
    public Vector3 HerdAreaCenter => terrainInstance != null ? terrainInstance.HerdCenter : SimulationPosition;
    public float HerdAreaRadius => terrainInstance != null
        ? terrainInstance.HerdRadius
        : settings != null
            ? settings.HerdAreaRadius
            : AnimalAISettings.DefaultHerdAreaRadius;
    public bool IsConfigured => configured;
    public bool IsInteracted => terrainInstance != null && terrainInstance.HasInteracted;
    public bool IsExecuting => executionActive;
    public bool IsNooseLeashed => nooseLeashed;
    public bool IsDraftAttached => draftAttached;
    public bool HasMountedRider => mountedRider != null;
    private bool IsExternallyControlled => nooseLeashed || draftAttached || mountedRider != null;
    private bool IsSaddledFreeRoaming => animal != null
                                          && animal.IsSaddleEquipped
                                          && !IsExternallyControlled;
    public float NooseMovementSpeed => configured && animal != null && animal.IsAlive
        ? GetEffectiveMoveSpeed()
        : 0f;
    public bool IsFleeing => configured && currentState == AnimalAIState.Flee && hasFleeThreat;
    internal bool HasPendingPresentation => !IsExternallyControlled && presentationActive;
    public int ForcedThreatPulseCount => forcedThreatPulseCount;
    public int ReachableFallbackTargetCount => reachableFallbackTargetCount;
    public int StuckTargetAbandonCount => stuckTargetAbandonCount;
    public int HerdReturnSuppressionCount => herdReturnSuppressionCount;
    public float AvoidanceColliderRadius => Mathf.Max(
        0.05f,
        avoidanceColliderRadius + ObstacleClearanceSkin);
    public Vector3 SimulationPosition => simulationPoseInitialized
        ? simulationPosition
        : transform.position;
    public Vector3 CrowdSnapshotPosition => crowdSnapshotValid
        ? crowdSnapshotPosition
        : SimulationPosition;

    public void CaptureCrowdSnapshot()
    {
        crowdSnapshotPosition = SimulationPosition;
        crowdSnapshotValid = true;
    }

    public int CopyRemainingNavigationPath(Vector3[] destination)
    {
        if (destination == null
            || destination.Length <= 0
            || !configured
            || !hasTarget)
        {
            return 0;
        }

        int count = AppendNavigationPathPoint(destination, 0, SimulationPosition);
        if (terrainEscapeActive)
        {
            return AppendNavigationPathPoint(destination, count, terrainEscapeTarget);
        }

        int firstWaypoint = Mathf.Clamp(
            navigationWaypointIndex,
            0,
            navigationWaypointCount);
        for (int i = firstWaypoint;
             i < navigationWaypointCount && count < destination.Length;
             i++)
        {
            count = AppendNavigationPathPoint(destination, count, navigationWaypoints[i]);
        }

        Vector3 goal = navigationPrepared ? navigationGoal : targetPosition;
        return AppendNavigationPathPoint(destination, count, goal);
    }

    private void Awake()
    {
        AnimalAIWorld.Register(this);
    }

    private void OnEnable() => AnimalAIWorld.NotifySpatialChanged(this);
    private void OnDisable() => AnimalAIWorld.NotifySpatialChanged(this);

    private void LateUpdate()
    {
        if (configured && !draftAttached)
        {
            animal?.TryRestorePendingDraftHandcart();
        }

        ApplyMountedRotation();
    }

    private void OnDestroy()
    {
        ReleaseFoodConsumption();
        AnimalAIWorld.Unregister(this);
    }

    public void Configure(
        Animal sourceAnimal,
        AnimalDefinition sourceDefinition,
        TerrainAnimalInstance sourceInstance,
        AnimalSaveEntry restoredState = null)
    {
        ReleaseFoodConsumption();
        animal = sourceAnimal != null ? sourceAnimal : GetComponentInChildren<Animal>(true);
        definition = sourceDefinition != null
            ? sourceDefinition
            : animal != null
                ? animal.Definition
                : null;
        terrainInstance = sourceInstance != null
            ? sourceInstance
            : GetComponent<TerrainAnimalInstance>();
        AnimalAIWorld.NotifySimulationIdentityChanged(this);
        settings = definition != null && definition.AISettings != null
            ? definition.AISettings
            : new AnimalAISettings();
        settings.Normalize();
        standUpDuration = animal != null ? animal.GetSimulationAnimationDuration("_LayToIdle", 1f) : 1f;
        feedingDuration = animal != null ? animal.GetSimulationAnimationDuration("_Eating", FeedingDuration) : FeedingDuration;

        if (restoredState != null)
        {
            currentState = ClampState(restoredState.behaviorState);
            if (currentState == AnimalAIState.Flee)
            {
                currentState = AnimalAIState.Idle;
            }

            stateTimeRemaining = Mathf.Max(0f, restoredState.behaviorTimeRemaining);
            targetPosition = restoredState.targetPosition;
            hasTarget = currentState != AnimalAIState.Idle && restoredState.hasTarget;
            movingToActivity = hasTarget && restoredState.movingToActivity;
            randomState = unchecked((uint)restoredState.randomState);
        }
        else
        {
            currentState = AnimalAIState.Idle;
            stateTimeRemaining = 0f;
            hasTarget = false;
            movingToActivity = false;
            randomState = BuildInitialRandomState();
        }

        if (randomState == 0u)
        {
            randomState = 0x6D2B79F5u;
        }

        avoidanceTurnSign = (randomState & 1u) == 0u ? 1f : -1f;
        smoothedSeparation = Vector3.zero;
        crowdSnapshotValid = false;
        nooseLeashed = false;
        draftAttached = false;
        mountedRider = null;
        ResetMountedMovement();
        waitingForStandUp = false;
        hasFleeThreat = false;
        postAggroHealthRecoveryDelayRemaining = -1f;
        foodSearchCooldown = 0f;
        foodTargetCoordinate = default;
        herdReturnRetryCooldown = 0f;
        herdReturnTargetActive = false;
        preferReachableFallbackTarget = false;
        reachableFallbackTargetCount = 0;
        stuckTargetAbandonCount = 0;
        herdReturnSuppressionCount = 0;
        ResetNavigation();
        configured = animal == null || animal.IsAlive;
        if (!configured)
        {
            StopForDeath();
            return;
        }

        PrepareLiveAnimalCollision();
        InitializeSimulationPose();
        CaptureSaddledFreeRoamCenter();
        if (IsSaddledFreeRoaming)
        {
            ResetToIdleBehavior();
        }

        ResetScheduledTick();
        ResetPresentation();
        AnimalAIWorld.Register(this);
        detailedVisualsInitialized = false;
        SyncBehaviorAnimationActivity();
        InitializeNeedsSchedule(AnimalAIWorld.Instance != null ? AnimalAIWorld.Instance.NeedsTick : 0L);
        ApplyAnimation(0f);
        animal?.TryRestorePendingDraftHandcart();
    }

    public void NotifySaddleEquipped()
    {
        CaptureSaddledFreeRoamCenter();
        if (!configured || IsExternallyControlled)
        {
            return;
        }

        ResetToIdleBehavior();
        ResetScheduledTick();
        ResetPresentation();
        ApplyAnimation(0f);
    }

    public bool QueueScheduledTick(float deltaTime, float interval)
    {
        if (!configured || !executionActive || IsExternallyControlled || deltaTime <= 0f) return false;
        long intervalTicks = System.Math.Max(1L, AnimalSimulationMath.Ticks(interval));
        if (!scheduledTickPhaseApplied)
        {
            scheduledTicks = scheduledTickPhase % intervalTicks;
            scheduledTickPhaseApplied = true;
        }
        long elapsed = AnimalSimulationMath.Ticks(deltaTime);
        scheduledTicks += elapsed;
        scheduledRecoveryTicks += elapsed;
        return scheduledTicks >= intervalTicks;
    }

    public bool ExecuteScheduledTick()
    {
        using var sample = AnimalAIProfiler.Sample("Animal Decision and Movement");
        if (!configured || !executionActive || IsExternallyControlled || scheduledTicks <= 0)
        {
            return false;
        }

        float simulationDelta = AnimalSimulationMath.Seconds(System.Math.Min(scheduledTicks, 12L));
        float recoveryElapsedTime = AnimalSimulationMath.Seconds(scheduledRecoveryTicks);
        scheduledTicks = 0;
        scheduledRecoveryTicks = 0;
        EnsureSimulationPoseInitialized();
        Vector3 framePosition = transform.position;
        Quaternion frameRotation = transform.rotation;
        bool wasFleeing = IsFleeing;
        Vector3 previousSimulationPosition = simulationPosition;
        TickSimulation(simulationDelta, recoveryElapsedTime, true);
        Vector3 nextSimulationPosition = AnimalSimulationMath.Quantize(simulationPosition);
        Quaternion nextSimulationRotation = simulationRotation;
        bool spatialChanged = !previousSimulationPosition.Equals(nextSimulationPosition) || wasFleeing != IsFleeing;
        simulationPosition = nextSimulationPosition;
        simulationRotation = nextSimulationRotation;
        if (spatialChanged) AnimalAIWorld.NotifySpatialChanged(this);
        if (configured)
        {
            bool poseChanged = (nextSimulationPosition - framePosition).sqrMagnitude
                               > 0.0000001f
                               || Quaternion.Angle(frameRotation, nextSimulationRotation) > 0.01f;
            if (poseChanged)
            {
                BeginPresentation(
                    framePosition,
                    frameRotation,
                    nextSimulationPosition,
                    nextSimulationRotation,
                    simulationDelta);
            }
        }

        return true;
    }

    public void TickPresentation(float deltaTime)
    {
        if (IsExternallyControlled || !presentationActive || deltaTime <= 0f)
        {
            return;
        }

        presentationElapsed = Mathf.Min(
            presentationElapsed + deltaTime,
            presentationDuration);
        float t = presentationDuration > 0.0001f
            ? presentationElapsed / presentationDuration
            : 1f;
        transform.SetPositionAndRotation(
            Vector3.LerpUnclamped(
                presentationStartPosition,
                presentationTargetPosition,
                t),
            Quaternion.SlerpUnclamped(
                presentationStartRotation,
                presentationTargetRotation,
                t));
        if (t >= 1f)
        {
            presentationActive = false;
        }
    }

    public void SetDetailedVisuals(bool visible)
    {
        if (detailedVisualsInitialized && detailedVisualsVisible == visible) return;
        detailedVisualsInitialized = true;
        detailedVisualsVisible = visible;
        animal?.SetDetailedVisuals(visible);
    }

    internal bool IsPresentationVisible(CameraRenderCulling culling)
    {
        if (!culling.Enabled) return true;
        Bounds bounds = animal != null ? animal.PresentationBounds
            : new Bounds(transform.position, Vector3.one * 4f);
        // Test both ends using authoritative pose, so a culled interpolation can reenter view.
        bounds.Encapsulate(new Bounds(bounds.center + SimulationPosition - transform.position, bounds.size));
        return culling.IsLayerVisible(animal != null ? animal.PresentationLayer : gameObject.layer)
               && culling.Intersects(bounds);
    }

    internal void TickCulledPresentation(float deltaTime, bool visible)
    {
        if (visible) TickPresentation(deltaTime);
        else
        {
            SnapToSimulationPose();
            ResetPresentation();
        }
    }

    public bool TickDormant()
    {
        if (!configured
            || executionActive
            || IsExternallyControlled
            || !CanDefecate())
        {
            return false;
        }

        return TryDefecateAt(GetDormantDefecationPosition());
    }

    public void SetBehaviorExecutionActive(bool active)
    {
        active &= !IsExternallyControlled;
        bool changed = executionActive != active;
        executionActive = active;

        if (!changed)
        {
            if (!behaviorAnimationActivityInitialized) SyncBehaviorAnimationActivity();
            return;
        }

        SyncBehaviorAnimationActivity();
        if (!active)
        {
            ResetScheduledTick();
            ResetPresentation();
            SnapToSimulationPose();
            ApplyAnimation(0f);
        }
        else if (waitingForStandUp)
        {
            animal?.WakeFromRest();
        }
    }

    public bool SetNooseLeashed(bool leashed)
    {
        if (!leashed)
        {
            if (!nooseLeashed)
            {
                return true;
            }

            nooseLeashed = false;
            currentState = AnimalAIState.Idle;
            stateTimeRemaining = 0f;
            hasTarget = false;
            movingToActivity = false;
            hasFleeThreat = false;
            waitingForStandUp = false;
            ResetNavigation();
            ResetScheduledTick();
            ResetPresentation();
            InitializeSimulationPose();
            CaptureSaddledFreeRoamCenter();
            ApplyAnimation(0f);
            return true;
        }

        if (!configured || animal == null || !animal.IsAlive || mountedRider != null)
        {
            return false;
        }

        // 올가미와 수레 견인은 동시에 동물을 제어할 수 없다. 결속이 확정되는
        // 이 지점에서 해제하면 투척 명중과 세이브 복원 모두 같은 규칙을 따른다.
        animal.DetachFromDraftHandcart();
        bool wasResting = currentState == AnimalAIState.Rest || waitingForStandUp;
        nooseLeashed = true;
        executionActive = false;
        SyncBehaviorAnimationActivity();
        currentState = AnimalAIState.Idle;
        stateTimeRemaining = 0f;
        hasTarget = false;
        movingToActivity = false;
        hasFleeThreat = false;
        waitingForStandUp = wasResting;
        smoothedSeparation = Vector3.zero;
        ResetNavigation();
        ResetScheduledTick();
        ResetPresentation();
        InitializeSimulationPose();
        if (wasResting)
        {
            animal.WakeFromRest();
        }
        else
        {
            ApplyAnimation(0f);
        }
        animal.MarkTerrainInteraction();
        return true;
    }

    public bool SetMountedRider(Player rider)
    {
        if (rider == null)
        {
            if (mountedRider == null)
            {
                return true;
            }

            mountedRider = null;
            ResetMountedMovement();
            currentState = AnimalAIState.Idle;
            stateTimeRemaining = 0f;
            hasTarget = false;
            movingToActivity = false;
            hasFleeThreat = false;
            waitingForStandUp = false;
            ResetNavigation();
            ResetScheduledTick();
            ResetPresentation();
            InitializeSimulationPose();
            CaptureSaddledFreeRoamCenter();
            ApplyAnimation(0f);
            return true;
        }

        if (!configured
            || animal == null
            || !animal.IsAlive
            || nooseLeashed
            || (mountedRider != null && mountedRider != rider))
        {
            return false;
        }

        bool wasResting = currentState == AnimalAIState.Rest || waitingForStandUp;
        mountedRider = rider;
        ResetMountedMovement();
        executionActive = false;
        SyncBehaviorAnimationActivity();
        currentState = AnimalAIState.Idle;
        stateTimeRemaining = 0f;
        hasTarget = false;
        movingToActivity = false;
        hasFleeThreat = false;
        waitingForStandUp = wasResting;
        smoothedSeparation = Vector3.zero;
        ResetNavigation();
        ResetScheduledTick();
        ResetPresentation();
        InitializeSimulationPose();
        if (wasResting)
        {
            animal.WakeFromRest();
        }
        else
        {
            ApplyAnimation(0f);
        }

        animal.MarkTerrainInteraction();
        return true;
    }

    public void SetDraftAttached(bool attached)
    {
        if (draftAttached == attached)
        {
            return;
        }

        draftAttached = attached;
        ResetMountedMovement();
        currentState = AnimalAIState.Idle;
        stateTimeRemaining = 0f;
        hasTarget = false;
        movingToActivity = false;
        hasFleeThreat = false;
        waitingForStandUp = false;
        smoothedSeparation = Vector3.zero;
        ResetNavigation();
        ResetScheduledTick();
        ResetPresentation();
        InitializeSimulationPose();
        if (!attached)
        {
            CaptureSaddledFreeRoamCenter();
        }

        if (attached)
        {
            executionActive = false;
        }

        ApplyAnimation(0f);
    }

    public void ApplyExternalControlledPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        transform.SetPositionAndRotation(worldPosition, worldRotation);
        simulationPosition = AnimalSimulationMath.Quantize(worldPosition);
        simulationRotation = worldRotation;
        simulationPoseInitialized = true;
        AnimalAIWorld.NotifySpatialChanged(this);
        ResetPresentation();
    }

    public bool TryMoveMounted(
        Vector3 worldMoveDirection,
        bool runRequested,
        float deltaTime)
    {
        if (mountedRider == null
            || !configured
            || animal == null
            || !animal.IsAlive
            || deltaTime <= 0f)
        {
            return false;
        }

        EnsureSimulationPoseInitialized();
        if (WaitForStandUpBeforeMovement(true))
        {
            mountedRunAnimationActive = false;
            ApplyAnimation(0f);
            return false;
        }

        worldMoveDirection.y = 0f;
        float rawInputMagnitude = AnimalSimulationMath.Magnitude(worldMoveDirection);
        float inputMagnitude = Mathf.Clamp01(rawInputMagnitude);
        bool hasInput = inputMagnitude > 0.01f;
        float effectiveWalkSpeed = GetEffectiveMoveSpeed();
        if (hasInput)
        {
            worldMoveDirection /= rawInputMagnitude;
            mountedMovementDirection = worldMoveDirection;
            mountedMaximumSpeed = effectiveWalkSpeed
                                  * inputMagnitude
                                  * (runRequested ? settings.RunSpeedRatio : 1f);
        }

        if (draftAttached && animal.IsAttachedToHandcart)
        {
            bool moved = animal.TryMoveAttachedHandcart(
                hasInput ? worldMoveDirection : Vector3.zero,
                mountedMaximumSpeed,
                deltaTime,
                mountedRider,
                out float cartActualMoveSpeed);
            bool useRunAnimation = ResolveMountedRunAnimation(
                moved ? cartActualMoveSpeed : 0f,
                effectiveWalkSpeed);
            float locomotionPlaybackScale = effectiveWalkSpeed > 0.0001f
                ? cartActualMoveSpeed / effectiveWalkSpeed
                : 1f;
            ApplyAnimation(
                cartActualMoveSpeed,
                useRunAnimation,
                locomotionPlaybackScale);
            if (!hasInput && !moved)
            {
                mountedMaximumSpeed = 0f;
            }

            return moved;
        }

        float targetSpeed = hasInput ? mountedMaximumSpeed : 0f;
        float speedChangePerSecond = targetSpeed > mountedCurrentSpeed
            ? settings.AccelerationPerSecond
            : settings.DecelerationPerSecond;
        mountedCurrentSpeed = Mathf.MoveTowards(
            mountedCurrentSpeed,
            targetSpeed,
            speedChangePerSecond * deltaTime);
        if (mountedCurrentSpeed <= 0.0001f
            || mountedMovementDirection.sqrMagnitude
               <= MountedMovementDirectionEpsilonSqr)
        {
            mountedCurrentSpeed = 0f;
            if (!hasInput)
            {
                mountedMaximumSpeed = 0f;
            }

            mountedRunAnimationActive = false;
            ApplyAnimation(0f);
            return false;
        }

        Vector3 previousPosition = simulationPosition;
        if (!TryApplyPlayerPush(
                previousPosition - mountedMovementDirection,
                mountedMovementDirection,
                mountedCurrentSpeed * deltaTime))
        {
            mountedCurrentSpeed = 0f;
            mountedRunAnimationActive = false;
            ApplyAnimation(0f);
            return false;
        }

        Vector3 movedDirection = simulationPosition - previousPosition;
        movedDirection.y = 0f;
        float actualMoveDistance = AnimalSimulationMath.Magnitude(movedDirection);
        if (movedDirection.sqrMagnitude > MountedMovementDirectionEpsilonSqr)
        {
            movedDirection /= actualMoveDistance;
            Quaternion targetRotation = Quaternion.LookRotation(movedDirection, Vector3.up);
            float turnSpeed = settings != null ? settings.TurnSpeed : 360f;
            // Mounted movement is frame-driven, so capping delta time would slow
            // real-time turning whenever a player build runs below 30 FPS.
            simulationRotation = Quaternion.RotateTowards(
                simulationRotation,
                targetRotation,
                Mathf.Max(90f, turnSpeed) * deltaTime);
            ApplyMountedRotation();
        }

        float actualMoveSpeed = actualMoveDistance / Mathf.Max(0.0001f, deltaTime);
        bool useMountedRunAnimation = ResolveMountedRunAnimation(
            actualMoveSpeed,
            effectiveWalkSpeed);
        float animationPlaybackScale = effectiveWalkSpeed > 0.0001f
            ? actualMoveSpeed / effectiveWalkSpeed
            : 1f;
        ApplyAnimation(
            actualMoveSpeed,
            useMountedRunAnimation,
            animationPlaybackScale);
        return true;
    }

    public void TickNeeds(float deltaTime)
    {
        using var sample = AnimalAIProfiler.Sample("Animal Needs");
        animal?.TickNeeds(deltaTime);
        AnimalAIProfiler.Add(AnimalAIProfiler.Counter.NeedsUpdates);
    }

    internal void InitializeNeedsSchedule(long tick) => needsSchedule.Initialize(tick, SimulationId);

    internal bool TickScheduledNeeds(long tick, bool lowFrequency)
    {
        long elapsed = needsSchedule.TakeElapsedTicks(tick, lowFrequency);
        if (elapsed <= 0L) return false;
        TickNeeds(elapsed * MapObjectTickManager.FixedSimulationDeltaSeconds);
        return true;
    }

    internal void FlushPendingNeeds()
    {
        if (AnimalAIWorld.Instance != null)
            TickScheduledNeeds(AnimalAIWorld.Instance.NeedsTick, false);
    }

    private void ResetMountedMovement()
    {
        mountedCurrentSpeed = 0f;
        mountedMaximumSpeed = 0f;
        mountedMovementDirection = Vector3.zero;
        mountedRunAnimationActive = false;
    }

    private bool ResolveMountedRunAnimation(float actualMoveSpeed, float effectiveWalkSpeed)
    {
        float walkSpeed = Mathf.Max(0f, effectiveWalkSpeed);
        if (actualMoveSpeed <= 0.01f || walkSpeed <= 0.0001f)
        {
            mountedRunAnimationActive = false;
            return false;
        }

        float margin = Mathf.Max(
            MountedRunTransitionMinimumMargin,
            walkSpeed * MountedRunTransitionRelativeMargin);
        float threshold = mountedRunAnimationActive
            ? Mathf.Max(0f, walkSpeed - margin)
            : walkSpeed + margin;
        mountedRunAnimationActive = actualMoveSpeed > threshold;
        return mountedRunAnimationActive;
    }

    private void ApplyMountedRotation()
    {
        if (mountedRider != null && simulationPoseInitialized)
        {
            transform.rotation = simulationRotation;
        }
    }

    public bool TryPullNooseToward(
        Vector3 targetPosition,
        float slackDistance,
        float movementSpeed,
        float deltaTime)
    {
        if (!nooseLeashed
            || !configured
            || animal == null
            || !animal.IsAlive
            || deltaTime <= 0f)
        {
            return false;
        }

        EnsureSimulationPoseInitialized();
        if (WaitForStandUpBeforeMovement(true))
        {
            ApplyAnimation(0f);
            return false;
        }

        Vector3 toTarget = targetPosition - simulationPosition;
        toTarget.y = 0f;
        float targetDistance = AnimalSimulationMath.Magnitude(toTarget);
        float resolvedSlackDistance = Mathf.Max(0.1f, slackDistance);
        if (targetDistance <= resolvedSlackDistance)
        {
            ApplyAnimation(0f);
            return false;
        }

        Vector3 pullDirection = toTarget / targetDistance;
        float pullSpeed = Mathf.Max(0f, movementSpeed);
        if (pullSpeed <= 0f)
        {
            ApplyAnimation(0f);
            return false;
        }

        float pullDistance = Mathf.Min(
            targetDistance - resolvedSlackDistance,
            pullSpeed * deltaTime);
        Vector3 pullOrigin = simulationPosition - pullDirection;
        if (!TryApplyPlayerPush(pullOrigin, pullDirection, pullDistance))
        {
            ApplyAnimation(0f);
            return false;
        }

        Quaternion targetRotation = Quaternion.LookRotation(pullDirection, Vector3.up);
        float turnSpeed = settings != null ? settings.TurnSpeed : 360f;
        simulationRotation = Quaternion.RotateTowards(
            simulationRotation,
            targetRotation,
            Mathf.Max(90f, turnSpeed) * Mathf.Min(deltaTime, MaximumRotationDeltaTime));
        transform.rotation = simulationRotation;
        ApplyAnimation(pullSpeed);
        return true;
    }

    public void StopForDeath()
    {
        ReleaseFoodConsumption();
        RestoreAnimalColliderLayers();
        nooseLeashed = false;
        draftAttached = false;
        mountedRider = null;
        configured = false;
        executionActive = false;
        waitingForStandUp = false;
        hasTarget = false;
        movingToActivity = false;
        hasFleeThreat = false;
        smoothedSeparation = Vector3.zero;
        ResetScheduledTick();
        ResetPresentation();
        ResetNavigation();

        AnimalAIWorld.Unregister(this);
    }

    private void ResetToIdleBehavior()
    {
        ReleaseFoodConsumption();
        currentState = AnimalAIState.Idle;
        stateTimeRemaining = settings != null
            ? RandomDuration(settings.IdleDuration)
            : 0f;
        hasTarget = false;
        movingToActivity = false;
        hasFleeThreat = false;
        waitingForStandUp = false;
        herdReturnTargetActive = false;
        ResetNavigation();
    }

    private void CaptureSaddledFreeRoamCenter()
    {
        if (animal == null || !animal.IsSaddleEquipped)
        {
            saddledFreeRoamCenterInitialized = false;
            return;
        }

        saddledFreeRoamCenter = simulationPoseInitialized
            ? simulationPosition
            : transform.position;
        saddledFreeRoamCenter.y = transform.position.y;
        saddledFreeRoamCenterInitialized = true;
    }

    private Vector3 GetRoamingAreaCenter()
    {
        if (!IsSaddledFreeRoaming)
        {
            return HerdAreaCenter;
        }

        if (!saddledFreeRoamCenterInitialized)
        {
            CaptureSaddledFreeRoamCenter();
        }

        return saddledFreeRoamCenter;
    }

    private float GetRoamingAreaRadius()
    {
        if (!IsSaddledFreeRoaming)
        {
            return HerdAreaRadius;
        }

        return Mathf.Clamp(
            HerdAreaRadius * SaddledFreeRoamRadiusMultiplier,
            MinimumSaddledFreeRoamRadius,
            MaximumSaddledFreeRoamRadius);
    }

    private void ResetScheduledTick()
    {
        scheduledTicks = 0;
        scheduledRecoveryTicks = 0;
        scheduledTickPhaseApplied = false;
        long id = SimulationId;
        scheduledTickPhase = unchecked(((uint)id ^ (uint)(id >> 32)) * 2654435761u);
    }

    private void BeginPresentation(
        Vector3 startPosition,
        Quaternion startRotation,
        Vector3 targetPosition,
        Quaternion targetRotation,
        float duration)
    {
        presentationStartPosition = startPosition;
        presentationTargetPosition = targetPosition;
        presentationStartRotation = startRotation;
        presentationTargetRotation = targetRotation;
        presentationDuration = Mathf.Clamp(duration, 0.01f, 0.2f);
        presentationElapsed = 0f;
        presentationActive = (targetPosition - startPosition).sqrMagnitude > 0.0000001f
                             || Quaternion.Angle(startRotation, targetRotation) > 0.01f;
    }

    private void ResetPresentation()
    {
        presentationActive = false;
        presentationElapsed = 0f;
        presentationDuration = 0f;
    }

    private void InitializeSimulationPose()
    {
        simulationPosition = AnimalSimulationMath.Quantize(transform.position);
        simulationRotation = transform.rotation;
        simulationPoseInitialized = true;
        AnimalAIWorld.NotifySpatialChanged(this);
    }

    private void EnsureSimulationPoseInitialized()
    {
        if (!simulationPoseInitialized)
        {
            InitializeSimulationPose();
        }
    }

    private void SnapToSimulationPose()
    {
        if (simulationPoseInitialized)
        {
            transform.SetPositionAndRotation(simulationPosition, simulationRotation);
        }
    }

    private void PrepareLiveAnimalCollision()
    {
        if (animalColliders == null)
        {
            animalColliders = GetComponentsInChildren<Collider>(true);
            originalAnimalColliderLayers = new int[animalColliders.Length];
            for (int i = 0; i < animalColliders.Length; i++)
            {
                Collider animalCollider = animalColliders[i];
                originalAnimalColliderLayers[i] = animalCollider != null
                    ? animalCollider.gameObject.layer
                    : 0;
            }
        }

        avoidanceColliderRadius = GetConfiguredObstacleRadius();
        for (int i = 0; i < animalColliders.Length; i++)
        {
            Collider animalCollider = animalColliders[i];
            if (animalCollider == null || animalCollider.isTrigger || !animalCollider.enabled)
            {
                continue;
            }

            Vector3 extents = animalCollider.bounds.extents;
            avoidanceColliderRadius = Mathf.Max(
                avoidanceColliderRadius,
                Mathf.Max(extents.x, extents.z));
        }

        int liveAnimalLayer = LayerMask.NameToLayer(LiveAnimalLayerName);
        if (liveAnimalLayer < 0)
        {
            return;
        }

        for (int i = 0; i < animalColliders.Length; i++)
        {
            Collider animalCollider = animalColliders[i];
            if (animalCollider != null)
            {
                animalCollider.gameObject.layer = liveAnimalLayer;
            }
        }
    }

    private void RestoreAnimalColliderLayers()
    {
        if (animalColliders == null || originalAnimalColliderLayers == null)
        {
            return;
        }

        int count = Mathf.Min(animalColliders.Length, originalAnimalColliderLayers.Length);
        for (int i = 0; i < count; i++)
        {
            Collider animalCollider = animalColliders[i];
            if (animalCollider != null)
            {
                animalCollider.gameObject.layer = originalAnimalColliderLayers[i];
            }
        }
    }

    public void NotifyThreat(Vector3 threatPosition)
    {
        if (!configured || animal != null && !animal.IsAlive)
        {
            return;
        }

        BeginFlee(threatPosition);
    }

    public void NotifyForcedThreat(Vector3 threatPosition)
    {
        forcedThreatPulseCount++;
        NotifyThreat(threatPosition);
    }

    public bool TryApplyPlayerPush(
        Vector3 pushOrigin,
        Vector3 playerMovement,
        float pushDistance)
    {
        if (!configured
            || pushDistance <= 0f
            || animal != null && !animal.IsAlive)
        {
            return false;
        }

        EnsureSimulationPoseInitialized();
        Vector3 origin = simulationPosition;
        Vector3 pushDirection = origin - pushOrigin;
        pushDirection.y = 0f;
        if (pushDirection.sqrMagnitude <= 0.0001f)
        {
            playerMovement.y = 0f;
            if (playerMovement.sqrMagnitude <= 0.0001f)
            {
                playerMovement = simulationRotation * Vector3.forward;
            }

            playerMovement = AnimalSimulationMath.Normalize(playerMovement);
            pushDirection = new Vector3(
                -playerMovement.z,
                0f,
                playerMovement.x);
            if ((randomState & 1u) != 0u)
            {
                pushDirection = -pushDirection;
            }
        }
        else
        {
            pushDirection = AnimalSimulationMath.Normalize(pushDirection);
        }

        bool hasAnimalBlockedCandidate = false;
        Vector3 animalBlockedCandidate = Vector3.zero;
        for (int i = 0; i < PlayerPushAngles.Length; i++)
        {
            Vector3 candidateDirection = AnimalSimulationMath.Rotate(pushDirection, PlayerPushAngles[i]);
            Vector3 candidate = origin + candidateDirection * pushDistance;
            candidate.y = origin.y;
            MovementAvailability availability = ProbePlayerPush(
                origin,
                candidate,
                candidateDirection,
                pushDistance);
            if (availability == MovementAvailability.Clear)
            {
                ApplyPlayerPush(candidate);
                return true;
            }

            if (!hasAnimalBlockedCandidate
                && availability == MovementAvailability.BlockedByAnimal)
            {
                hasAnimalBlockedCandidate = true;
                animalBlockedCandidate = candidate;
            }
        }

        if (!hasAnimalBlockedCandidate)
        {
            return false;
        }

        // 밀집 상태에서는 동물끼리의 겹침보다 고정 장애물 침범을 우선 방지한다.
        // 약간의 동물 겹침은 다음 AI separation 단계에서 자연스럽게 해소된다.
        ApplyPlayerPush(animalBlockedCandidate);
        return true;
    }

    public void CaptureSaveState(AnimalSaveEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        entry.position = SimulationPosition;
        entry.rotation = simulationRotation;
        entry.herdId = HerdId;
        entry.herdCenter = HerdAreaCenter;
        entry.herdRadius = HerdAreaRadius;
        bool hasTransientState = currentState == AnimalAIState.Flee
                                 || currentState == AnimalAIState.Eat;
        entry.behaviorState = hasTransientState
            ? (int)AnimalAIState.Idle
            : (int)currentState;
        entry.behaviorTimeRemaining = hasTransientState
            ? 0f
            : Mathf.Max(0f, stateTimeRemaining);
        entry.targetPosition = hasTransientState ? SimulationPosition : targetPosition;
        entry.hasTarget = !hasTransientState && hasTarget;
        entry.movingToActivity = !hasTransientState && movingToActivity;
        entry.randomState = unchecked((int)randomState);
    }

    private void TickSimulation(
        float deltaTime,
        float elapsedTime,
        bool useLiveCollision)
    {
        if (animal != null && !animal.IsAlive)
        {
            StopForDeath();
            return;
        }

        navigationRepathCooldown = Mathf.Max(0f, navigationRepathCooldown - deltaTime);
        herdReturnRetryCooldown = Mathf.Max(0f, herdReturnRetryCooldown - deltaTime);
        foodSearchCooldown = Mathf.Max(0f, foodSearchCooldown - deltaTime);
        // Digestion completes independently of walking, eating, or resting.
        TryDefecateAt(simulationPosition);
        if (currentState == AnimalAIState.Rest && !IsNightTime())
        {
            stateTimeRemaining = 0f;
        }

        if (currentState == AnimalAIState.Flee)
        {
            if (WaitForStandUpBeforeMovement(useLiveCollision))
            {
                return;
            }

            TickFlee(deltaTime, useLiveCollision);
            return;
        }

        if (TryTickFeeding(deltaTime, useLiveCollision, out bool feedingMoved))
        {
            RecoverHealthWhenCalm(elapsedTime);
            ApplyAnimation(feedingMoved ? GetEffectiveMoveSpeed() : 0f);
            return;
        }

        // A drink target can be outside the herd's ordinary roaming circle.
        // Finish reaching and using the shoreline before normal herd return resumes.
        bool returningToHerdArea = currentState != AnimalAIState.Drink
                                   && TryBeginHerdAreaReturn(useLiveCollision);
        if (!returningToHerdArea && stateTimeRemaining <= 0f)
        {
            BeginNextBehavior(useLiveCollision);
        }

        RecoverHealthWhenCalm(elapsedTime);

        if (WaitForStandUpBeforeMovement(useLiveCollision))
        {
            return;
        }

        bool moved = false;
        if (hasTarget)
        {
            moved = MoveTowardTarget(deltaTime, useLiveCollision);
        }

        if (currentState == AnimalAIState.Drink
            && !movingToActivity
            && !hasTarget)
        {
            FaceDrinkWater(deltaTime);
        }

        if (!movingToActivity)
        {
            stateTimeRemaining -= deltaTime;
        }

        ApplyAnimation(moved ? GetEffectiveMoveSpeed() : 0f);
    }

    private bool TryTickFeeding(
        float deltaTime,
        bool requireLoadedGround,
        out bool moved)
    {
        using var sample = AnimalAIProfiler.Sample("Animal Feeding");
        moved = false;
        if (animal == null || !animal.IsAlive)
        {
            return false;
        }

        if (currentState == AnimalAIState.Eat && !hasTarget && stateTimeRemaining > 0f)
        {
            FaceDroppedFood(deltaTime);
            stateTimeRemaining = Mathf.Max(0f, stateTimeRemaining - deltaTime);
            if (stateTimeRemaining <= 0f)
            {
                ResetToIdleBehavior();
                foodSearchCooldown = IntervalBetweenMeals;
            }

            return true;
        }

        if (!animal.IsHungry
            || currentState == AnimalAIState.Rest
            || waitingForStandUp)
        {
            if (currentState == AnimalAIState.Eat)
            {
                ResetToIdleBehavior();
            }

            return false;
        }

        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null)
        {
            return false;
        }

        if (currentState != AnimalAIState.Eat)
        {
            // Waiting must not restart the retry timer on every simulation tick.
            if (foodSearchCooldown > 0f)
            {
                return false;
            }

            if (!terrain.TryFindNearestDroppedAnimalFood(
                    simulationPosition,
                    definition != null
                        ? definition.NeedsSettings.FoodSearchRadius
                        : AnimalNeedsSettings.DefaultFoodSearchRadius,
                    out Vector2Int candidateCoordinate,
                    out Vector3 candidatePosition,
                    requireLoadedGround,
                    foodReachabilityFilter ??= CanReachDroppedFood))
            {
                foodSearchCooldown = 1f;
                return false;
            }

            foodTargetCoordinate = candidateCoordinate;
            targetPosition = candidatePosition;
            currentState = AnimalAIState.Eat;
            stateTimeRemaining = 0f;
            hasTarget = true;
            movingToActivity = true;
            ResetNavigation();
        }

        if (hasTarget && !IsWithinDroppedFoodReach(requireLoadedGround))
        {
            moved = MoveTowardTarget(deltaTime, requireLoadedGround);
            if (hasTarget)
            {
                return true;
            }
        }

        if (currentState != AnimalAIState.Eat
            || !IsWithinDroppedFoodReach(requireLoadedGround))
        {
            // Navigation can also clear hasTarget when a route fails. Do not eat remotely.
            ResetToIdleBehavior();
            foodSearchCooldown = BlockedFoodRetryDelay;
            return true;
        }

        // Arrival alone does not mean the body is facing the food, especially after avoidance.
        if (!FaceDroppedFood(deltaTime))
        {
            return true;
        }

        bool consumed = terrain.TryConsumeDroppedAnimalFood(
            foodTargetCoordinate,
            targetPosition,
            this,
            out ItemDefinition consumedFood)
            && animal.ConsumeDroppedFood(consumedFood);
        if (consumed)
        {
            feedingTerrain = terrain;
            hasTarget = false;
            movingToActivity = false;
            stateTimeRemaining = feedingDuration;
            ResetNavigation();
        }
        else
        {
            ResetToIdleBehavior();
        }

        foodSearchCooldown = consumed ? 0f : 1f;
        return true;
    }

    private bool FaceDroppedFood(float deltaTime)
    {
        Vector3 direction = targetPosition - simulationPosition;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        float turnSpeed = settings != null ? settings.TurnSpeed : 360f;
        int targetYaw = AnimalSimulationMath.Yaw(direction);
        simulationYaw = AnimalSimulationMath.Turn(simulationYaw, direction, Mathf.Max(90f, turnSpeed), Mathf.Min(deltaTime, MaximumRotationDeltaTime));
        if (System.Math.Abs(AnimalSimulationMath.AngleDelta(simulationYaw, targetYaw)) > AnimalSimulationMath.Angle(3f))
        {
            return false;
        }

        simulationYaw = targetYaw;
        return true;
    }

    private void ReleaseFoodConsumption()
    {
        feedingTerrain?.ReleaseAnimalFoodConsumption(foodTargetCoordinate, this);
        feedingTerrain = null;
    }

    private bool IsWithinDroppedFoodReach(bool requireLoadedGround)
    {
        Vector3 position = simulationPosition;
        Vector3 offset = targetPosition - position;
        offset.y = 0f;
        float reach = Mathf.Max(settings.ArrivalDistance, GetObstacleRadius() + 0.2f);
        return offset.sqrMagnitude <= reach * reach
               && AnimalGridPathfinder.HasWalkableLine(
                   TerrainGenerator.Active, position, targetPosition,
                   position, Mathf.Max(1f, reach), requireLoadedGround);
    }

    private bool CanReachDroppedFood(Vector3 destination, bool requireLoadedGround)
    {
        Vector3 areaCenter;
        float areaRadius;
        if (IsSaddledFreeRoaming)
        {
            GetNavigationArea(destination, out areaCenter, out areaRadius);
        }
        else
        {
            GetExtendedNavigationArea(destination, out areaCenter, out areaRadius);
        }

        if (CanNavigateDirectly(destination, areaCenter, areaRadius, requireLoadedGround))
        {
            return true;
        }

        // Candidate checks must not overwrite the animal's current movement path.
        foodNavigationScratch ??= new Vector3[MaxNavigationWaypoints];
        return AnimalGridPathfinder.FindPath(
            TerrainGenerator.Active, simulationPosition, destination,
            areaCenter, areaRadius, requireLoadedGround, foodNavigationScratch) > 0;
    }

    private bool CanDefecate()
    {
        return animal != null
               && definition != null
               && animal.IsDefecationDue
               && !IsExternallyControlled
               && ItemDefinition.IsFertilizerEnergyItemDefinition(
                   definition.DefecationItem);
    }

    private bool TryDefecateAt(Vector3 worldPosition)
    {
        if (!CanDefecate())
        {
            return false;
        }

        ItemDefinition dropping = definition.DefecationItem;
        TerrainGenerator terrain = TerrainGenerator.Active;
        int amount = definition.NeedsSettings.DefecationAmount;
        if (terrain == null
            || terrain.DropAnimalDefecation(
                worldPosition,
                dropping.id,
                amount,
                IsInteracted,
                definition.NeedsSettings.UnattendedDroppingLifetimeSeconds)
            <= 0)
        {
            return false;
        }

        if (!animal.CompleteDefecation())
        {
            return false;
        }

        return true;
    }

    private Vector3 GetDormantDefecationPosition()
    {
        Vector3 center = SimulationPosition;
        // Spread stationary-animal droppings across nearby blocks without using
        // UnityEngine.Random or allocating temporary collections.
        uint angle = NextRandomUInt();
        center += AnimalSimulationMath.RandomDisk(NextRandomUInt(), angle, DormantDefecationSpreadRadius);
        return center;
    }

    private void RecoverHealthWhenCalm(float elapsedTime)
    {
        if (elapsedTime <= 0f
            || animal == null
            || !animal.IsAlive
            || currentState == AnimalAIState.Flee)
        {
            return;
        }

        if (animal.CurrentHealth >= animal.MaxHealth)
        {
            postAggroHealthRecoveryDelayRemaining = -1f;
            return;
        }

        bool activelyResting = currentState == AnimalAIState.Rest
                               && !movingToActivity
                               && !hasTarget
                               && IsNightTime();
        float recoveryElapsedTime = 0f;
        if (postAggroHealthRecoveryDelayRemaining >= 0f)
        {
            float delayBeforeTick = postAggroHealthRecoveryDelayRemaining;
            postAggroHealthRecoveryDelayRemaining = Mathf.Max(
                0f,
                delayBeforeTick - elapsedTime);
            recoveryElapsedTime = Mathf.Max(0f, elapsedTime - delayBeforeTick);
        }

        if (activelyResting)
        {
            recoveryElapsedTime = elapsedTime;
        }

        if (recoveryElapsedTime <= 0f)
        {
            return;
        }

        float recovery = animal.MaxHealth
                         * HealthRecoveryFractionPerSecond
                         * recoveryElapsedTime;
        animal.Heal(recovery, markTerrainInteraction: false);
        if (animal.CurrentHealth >= animal.MaxHealth)
        {
            postAggroHealthRecoveryDelayRemaining = -1f;
        }
    }

    private void FaceDrinkWater(float deltaTime)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null
            || !terrain.TryGetAnimalDrinkDirection(
                simulationPosition,
                out Vector3 direction)
            || direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        simulationYaw = AnimalSimulationMath.Turn(simulationYaw, direction, settings.TurnSpeed, Mathf.Min(deltaTime, MaximumRotationDeltaTime));
    }

    private bool WaitForStandUpBeforeMovement(bool useLiveCollision)
    {
        if (!waitingForStandUp) return false;
        if ((AnimalAIWorld.Instance?.NeedsTick ?? standUpReadyTick) < standUpReadyTick) return true;
        waitingForStandUp = false;
        animal?.WakeUp();
        ApplyAnimation(0f);
        return false;
    }

    private void BeginNextBehavior(bool requireLoadedGround)
    {
        using var sample = AnimalAIProfiler.Sample("Animal Behavior Selection");
        bool wasResting = currentState == AnimalAIState.Rest;
        hasFleeThreat = false;
        herdReturnTargetActive = false;
        currentState = ChooseBehavior();
        waitingForStandUp = wasResting;
        stateTimeRemaining = RandomDuration(GetDuration(currentState));
        hasTarget = false;
        movingToActivity = false;
        ResetNavigation();
        if (waitingForStandUp)
        {
            animal?.WakeFromRest();
        }

        Vector3 selectedTarget = targetPosition;
        switch (currentState)
        {
            case AnimalAIState.Wander:
                hasTarget = TryChooseTarget(false, requireLoadedGround, out selectedTarget);
                break;
            case AnimalAIState.Graze:
                hasTarget = TryChooseTarget(false, requireLoadedGround, out selectedTarget);
                movingToActivity = hasTarget;
                break;
            case AnimalAIState.Drink:
                hasTarget = TryChooseDrinkTarget(
                    requireLoadedGround,
                    out selectedTarget);
                movingToActivity = hasTarget;
                if (!hasTarget)
                {
                    currentState = AnimalAIState.Idle;
                    stateTimeRemaining = RandomDuration(settings.IdleDuration);
                }
                break;
        }
        targetPosition = selectedTarget;
    }

    private bool TryChooseDrinkTarget(
        bool requireLoadedGround,
        out Vector3 result)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        Vector3 position = simulationPosition;
        if (terrain == null)
        {
            result = position;
            return false;
        }

        if (terrain.IsAnimalDrinkLocation(position))
        {
            result = position;
            PrepareDirectNavigation(result);
            return true;
        }

        if (TryBuildReachableFallbackPath(
                true,
                requireLoadedGround,
                out result))
        {
            reachableFallbackTargetCount++;
            return true;
        }

        // 안장을 장착한 자유 상태에서는 물을 찾기 위해 제한된 생활 반경을
        // 벗어나지 않는다. 반경 안에 물이 없으면 이번 행동을 건너뛴다.
        if (IsSaddledFreeRoaming)
        {
            result = position;
            return false;
        }

        // Water is not guaranteed to exist inside a herd's roaming circle. Search
        // the connected walkable region around the animal so Drink remains a real
        // activity instead of repeatedly degrading to Idle.
        navigationWaypoints ??= new Vector3[MaxNavigationWaypoints];
        int waypointCount = AnimalGridPathfinder.FindReachableTargetPath(
            terrain,
            position,
            position,
            MaxExtendedNavigationRadius,
            requireLoadedGround,
            true,
            0f,
            NextRandomUInt(),
            navigationWaypoints,
            out result);
        return StoreNavigationPath(result, waypointCount);
    }

    private bool TryBeginHerdAreaReturn(bool requireLoadedGround)
    {
        // Rest after eating or a failed route must not be cancelled every tick.
        if (currentState == AnimalAIState.Idle && !hasTarget && stateTimeRemaining > 0f)
        {
            return false;
        }

        Vector3 position = simulationPosition;
        if (!IsOutsideRoamingArea(position))
        {
            herdReturnTargetActive = false;
            return false;
        }

        if (herdReturnRetryCooldown > 0f)
        {
            return false;
        }

        // Finish a reachable local activity before retrying a blocked route home.
        if (hasTarget && !herdReturnTargetActive)
        {
            return false;
        }

        if (!hasTarget || IsOutsideRoamingArea(targetPosition))
        {
            ResetNavigation();
            hasTarget = TryPrepareNearestHerdReturnTarget(
                position,
                requireLoadedGround);
            if (!hasTarget)
            {
                hasTarget = TryChooseTarget(
                    false,
                    requireLoadedGround,
                    out Vector3 selectedTarget,
                    allowLocalRoaming: false);
                targetPosition = selectedTarget;
            }
        }

        if (!hasTarget)
        {
            movingToActivity = false;
            SuppressHerdReturnRetry();
            return false;
        }

        bool wasResting = currentState == AnimalAIState.Rest;
        currentState = AnimalAIState.Wander;
        movingToActivity = true;
        herdReturnTargetActive = true;
        if (stateTimeRemaining <= 0f)
        {
            stateTimeRemaining = RandomDuration(settings.WanderDuration);
        }

        if (wasResting)
        {
            waitingForStandUp = true;
            animal?.WakeFromRest();
        }

        return true;
    }

    private bool TryPrepareNearestHerdReturnTarget(
        Vector3 position,
        bool requireLoadedGround)
    {
        Vector3 areaCenter = GetRoamingAreaCenter();
        Vector3 areaOffset = position - areaCenter;
        areaOffset.y = 0f;
        if (areaOffset.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float inset = Mathf.Max(settings.ArrivalDistance * 2f, 0.5f);
        float returnRadius = Mathf.Max(0f, GetRoamingAreaRadius() - inset);
        Vector3 returnTarget = areaCenter + AnimalSimulationMath.Normalize(areaOffset) * returnRadius;
        returnTarget.y = position.y;
        if (!CanOccupyTerrain(returnTarget, requireLoadedGround))
        {
            return false;
        }

        GetExtendedNavigationArea(
            returnTarget,
            out Vector3 navigationCenter,
            out float navigationRadius);
        targetPosition = returnTarget;
        if (CanNavigateDirectly(
                returnTarget,
                navigationCenter,
                navigationRadius,
                requireLoadedGround))
        {
            PrepareDirectNavigation(returnTarget);
            return true;
        }

        return TryBuildNavigationPath(
            returnTarget,
            navigationCenter,
            navigationRadius,
            requireLoadedGround,
            checkDirectPath: false);
    }

    private void BeginFlee(Vector3 threatPosition)
    {
        AnimalAIWorld.NotifySpatialChanged(this);
        bool wasResting = currentState == AnimalAIState.Rest || waitingForStandUp;
        currentState = AnimalAIState.Flee;
        stateTimeRemaining = 0f;
        fleeThreatPosition = threatPosition;
        fleeThreatPosition.y = simulationPosition.y;
        hasFleeThreat = true;
        postAggroHealthRecoveryDelayRemaining = PostAggroHealthRecoveryDelay;
        fleeRouteAttemptOffset = 0;
        waitingForStandUp = wasResting;
        movingToActivity = false;
        ResetNavigation();
        UpdateFleeTarget();
        animal?.MarkTerrainInteraction();
        if (waitingForStandUp)
        {
            animal?.WakeFromRest();
        }
    }

    private void TickFlee(float deltaTime, bool useLiveCollision)
    {
        using var sample = AnimalAIProfiler.Sample("Animal Movement");
        if (!hasFleeThreat)
        {
            BeginNextBehavior(useLiveCollision);
            ApplyAnimation(0f);
            return;
        }

        Vector3 position = simulationPosition;
        Vector3 awayFromThreat = position - fleeThreatPosition;
        awayFromThreat.y = 0f;
        float safeDistance = settings.FleeSafeDistance;
        if (awayFromThreat.sqrMagnitude >= safeDistance * safeDistance)
        {
            BeginNextBehavior(useLiveCollision);
            ApplyAnimation(0f);
            return;
        }

        Vector3 awayDirection;
        if (awayFromThreat.sqrMagnitude > 0.0001f)
        {
            awayDirection = AnimalSimulationMath.Normalize(awayFromThreat);
        }
        else
        {
            awayDirection = -AnimalSimulationMath.Direction(simulationYaw);
            awayDirection.y = 0f;
            if (awayDirection.sqrMagnitude <= 0.0001f)
            {
                awayDirection = AnimalSimulationMath.Direction((int)(NextRandomUInt() & 65535));
            }
            else
            {
                awayDirection = AnimalSimulationMath.Normalize(awayDirection);
            }
        }

        if (!navigationPrepared)
        {
            TryPrepareFleeNavigation(awayDirection, useLiveCollision);
        }

        Vector3 movementTarget = targetPosition;
        if (navigationPrepared)
        {
            GetCurrentNavigationTarget(position, out movementTarget);
        }

        Vector3 toMovementTarget = movementTarget - position;
        toMovementTarget.y = 0f;
        Vector3 movementDirection = toMovementTarget.sqrMagnitude
                                    > settings.ArrivalDistance * settings.ArrivalDistance
            ? AnimalSimulationMath.Normalize(toMovementTarget)
            : awayDirection;
        Vector3 crowdSteering = UpdateSmoothedSeparation(
            AnimalAIWorld.Instance,
            deltaTime) * settings.SeparationWeight;
        movementDirection += AnimalSimulationMath.ClampMagnitude(
            crowdSteering,
            MaximumCrowdSteering);
        if (movementDirection.sqrMagnitude > 0.0001f)
        {
            movementDirection = AnimalSimulationMath.Normalize(movementDirection);
        }
        else
        {
            movementDirection = awayDirection;
        }

        float speed = GetEffectiveMoveSpeed() * settings.FleeSpeedMultiplier;
        MovementAvailability movementAvailability = MoveFlee(
            position,
            movementDirection,
            speed,
            deltaTime,
            useLiveCollision);
        if (movementAvailability == MovementAvailability.BlockedByStaticObstacle)
        {
            RepathBlockedFlee(awayDirection, useLiveCollision, deltaTime);
        }
        else if (movementAvailability == MovementAvailability.BlockedByAnimal)
        {
            navigationBlockedTime = 0f;
            if (IsNavigationProgressStalled(
                    movementTarget,
                    simulationPosition,
                    deltaTime))
            {
                TryRepathFlee(awayDirection, useLiveCollision);
            }
        }
        else if (IsNavigationProgressStalled(
                     movementTarget,
                     simulationPosition,
                     deltaTime))
        {
            // 로컬 회피가 옆이나 뒤 방향을 계속 선택하면 물리 이동 자체는 성공해도
            // 탈출 경로의 다음 웨이포인트에는 가까워지지 않을 수 있다. 도망 중에는
            // 목표를 포기하지 않고 다음 탈출 각도로 경로를 다시 찾는다.
            TryRepathFlee(awayDirection, useLiveCollision);
        }

        ApplyAnimation(
            movementAvailability == MovementAvailability.Clear ? speed : 0f);
    }

    private bool TryPrepareFleeNavigation(
        Vector3 awayDirection,
        bool requireLoadedGround)
    {
        ResetNavigation();
        float targetDistance = GetFleeTargetDistance();
        for (int i = 0; i < FleeTargetAngles.Length; i++)
        {
            int angleIndex = (fleeRouteAttemptOffset + i) % FleeTargetAngles.Length;
            Vector3 candidateDirection =
                AnimalSimulationMath.Rotate(awayDirection, FleeTargetAngles[angleIndex]);
            Vector3 candidate =
                fleeThreatPosition + candidateDirection * targetDistance;
            candidate.y = simulationPosition.y;
            if (!CanOccupyTerrain(candidate, requireLoadedGround))
            {
                continue;
            }

            GetExtendedNavigationArea(candidate, out Vector3 areaCenter, out float areaRadius);
            targetPosition = candidate;
            hasTarget = true;
            if (TryBuildNavigationPath(
                    candidate,
                    areaCenter,
                    areaRadius,
                    requireLoadedGround))
            {
                return true;
            }
        }

        UpdateFleeTarget(awayDirection);
        PrepareDirectNavigation(targetPosition);
        return false;
    }

    private void RepathBlockedFlee(
        Vector3 awayDirection,
        bool requireLoadedGround,
        float deltaTime)
    {
        navigationBlockedTime += deltaTime;
        if (navigationBlockedTime < BlockedRepathDelay
            || navigationRepathCooldown > 0f)
        {
            return;
        }

        TryRepathFlee(awayDirection, requireLoadedGround);
    }

    private void TryRepathFlee(
        Vector3 awayDirection,
        bool requireLoadedGround)
    {
        if (navigationRepathCooldown > 0f)
        {
            return;
        }

        fleeRouteAttemptOffset =
            (fleeRouteAttemptOffset + 1) % FleeTargetAngles.Length;
        TryPrepareFleeNavigation(
            awayDirection,
            requireLoadedGround);
        navigationRepathCooldown = RepathCooldown;
    }

    private void GetExtendedNavigationArea(
        Vector3 destination,
        out Vector3 areaCenter,
        out float areaRadius)
    {
        Vector3 position = simulationPosition;
        areaCenter = (position + destination) * 0.5f;
        areaCenter.y = position.y;
        Vector3 offset = destination - position;
        offset.y = 0f;
        areaRadius = Mathf.Min(
            MaxExtendedNavigationRadius,
            AnimalSimulationMath.Magnitude(offset) * 0.5f + ExtendedNavigationMargin);
    }

    private MovementAvailability MoveFlee(
        Vector3 position,
        Vector3 direction,
        float speed,
        float deltaTime,
        bool useLiveCollision)
    {
        float moveDistance = speed * deltaTime;
        if (moveDistance <= 0f)
        {
            return MovementAvailability.BlockedByStaticObstacle;
        }

        MovementAvailability availability = ResolveMovementDirection(
            position,
            direction,
            speed,
            deltaTime,
            useLiveCollision,
            false,
            out direction);
        if (availability != MovementAvailability.Clear)
        {
            return availability;
        }

        Vector3 candidate = AnimalSimulationMath.Advance(position, direction, speed, deltaTime);
        candidate.y = position.y;
        ApplyMovement(candidate, direction, deltaTime);
        navigationBlockedTime = 0f;
        return MovementAvailability.Clear;
    }

    private void UpdateFleeTarget()
    {
        Vector3 direction = simulationPosition - fleeThreatPosition;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = -AnimalSimulationMath.Direction(simulationYaw);
            direction.y = 0f;
        }

        if (direction.sqrMagnitude > 0.0001f)
        {
            UpdateFleeTarget(AnimalSimulationMath.Normalize(direction));
        }
        else
        {
            targetPosition = simulationPosition;
            hasTarget = false;
        }
    }

    private void UpdateFleeTarget(Vector3 direction)
    {
        targetPosition = fleeThreatPosition + direction * GetFleeTargetDistance();
        targetPosition = new Vector3(targetPosition.x, simulationPosition.y, targetPosition.z);
        hasTarget = true;
    }

    private float GetFleeTargetDistance()
    {
        return settings.FleeSafeDistance
               + settings.ArrivalDistance
               + FleeTargetOvershoot;
    }

    private AnimalAIState ChooseBehavior()
    {
        float normalizedAge = animal != null ? Mathf.Clamp01(animal.Age * 0.1f) : 1f;
        float youngFactor = 1f - normalizedAge;
        float movementWeightMultiplier = IsSaddledFreeRoaming
            ? SaddledFreeMovementWeightMultiplier
            : 1f;
        float idle = settings.IdleWeight;
        float lookAround = settings.LookAroundWeight;
        float wander = settings.WanderWeight
                       * Mathf.Lerp(1f, settings.YoungWanderWeightMultiplier, youngFactor)
                       * movementWeightMultiplier;
        float graze = settings.GrazeWeight * movementWeightMultiplier;
        float drink = settings.DrinkWeight * movementWeightMultiplier;
        float rest = IsNightTime()
            ? settings.RestWeight
              * Mathf.Lerp(1f, settings.YoungRestWeightMultiplier, youngFactor)
            : 0f;
        float total = idle + lookAround + wander + graze + drink + rest;
        if (total <= 0f)
        {
            return AnimalAIState.Idle;
        }

        float selection = Next01() * total;
        if ((selection -= idle) < 0f)
        {
            return AnimalAIState.Idle;
        }

        if ((selection -= lookAround) < 0f)
        {
            return AnimalAIState.LookAround;
        }

        if ((selection -= wander) < 0f)
        {
            return AnimalAIState.Wander;
        }

        if ((selection -= graze) < 0f)
        {
            return AnimalAIState.Graze;
        }

        if ((selection -= drink) < 0f)
        {
            return AnimalAIState.Drink;
        }

        return AnimalAIState.Rest;
    }

    private Vector2 GetDuration(AnimalAIState state)
    {
        switch (state)
        {
            case AnimalAIState.LookAround:
                return settings.LookAroundDuration;
            case AnimalAIState.Wander:
                return settings.WanderDuration;
            case AnimalAIState.Graze:
                return settings.GrazeDuration;
            case AnimalAIState.Drink:
                return settings.DrinkDuration;
            case AnimalAIState.Rest:
                return settings.RestDuration;
            default:
                return settings.IdleDuration;
        }
    }

    private bool TryChooseTarget(
        bool requireWaterEdge,
        bool requireLoadedGround,
        out Vector3 result,
        bool allowLocalRoaming = true)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        Vector3 center = GetRoamingAreaCenter();
        float radius = Mathf.Max(1f, GetRoamingAreaRadius() - settings.ArrivalDistance);
        bool originIsWalkable = terrain == null
                                || terrain.CanAnimalMoveTo(
                                    simulationPosition,
                                    requireLoadedGround);
        int pathSearchCount = 0;
        if (allowLocalRoaming && !IsSaddledFreeRoaming
            && IsOutsideRoamingArea(simulationPosition))
        {
            // A relocated animal's original herd circle may be outside its pen.
            // Search the current connected area without changing its saved herd home.
            if (originIsWalkable && TryBuildReachableFallbackPath(
                    requireWaterEdge, requireLoadedGround, out result, useLocalArea: true))
            {
                preferReachableFallbackTarget = false;
                reachableFallbackTargetCount++;
                return true;
            }

            ResetNavigation();
            result = simulationPosition;
            return false;
        }
        // Wall로 나뉜 작은 연결 영역에서는 전체 무리 반경의 무작위 후보가
        // 연속으로 실패할 수 있으므로, 직전 정체가 있었다면 연결 영역을 우선한다.
        if (preferReachableFallbackTarget
            && originIsWalkable
            && TryBuildReachableFallbackPath(
                requireWaterEdge,
                requireLoadedGround,
                out result))
        {
            preferReachableFallbackTarget = false;
            reachableFallbackTargetCount++;
            return true;
        }

        for (int attempt = 0; attempt < TargetSearchAttempts; attempt++)
        {
            uint angle = NextRandomUInt();
            Vector3 candidate = center + AnimalSimulationMath.RandomDisk(NextRandomUInt(), angle, radius);
            candidate.y = simulationPosition.y;

            if (terrain != null
                && (!terrain.CanAnimalMoveTo(candidate, requireLoadedGround)
                    || (requireWaterEdge && !terrain.IsAnimalDrinkLocation(candidate))))
            {
                continue;
            }

            if (!originIsWalkable)
            {
                PrepareDirectNavigation(candidate);
                result = candidate;
                preferReachableFallbackTarget = false;
                return true;
            }

            if (CanNavigateDirectly(candidate, requireLoadedGround))
            {
                PrepareDirectNavigation(candidate);
                result = candidate;
                preferReachableFallbackTarget = false;
                return true;
            }

            if (pathSearchCount >= TargetPathSearchAttempts)
            {
                continue;
            }

            pathSearchCount++;
            if (!TryBuildNavigationPath(
                    candidate,
                    requireLoadedGround,
                    checkDirectPath: false))
            {
                continue;
            }

            result = candidate;
            preferReachableFallbackTarget = false;
            return true;
        }

        if (originIsWalkable
            && TryBuildReachableFallbackPath(
                requireWaterEdge,
                requireLoadedGround,
                out result))
        {
            preferReachableFallbackTarget = false;
            reachableFallbackTargetCount++;
            return true;
        }

        ResetNavigation();
        result = simulationPosition;
        return false;
    }

    private bool TryBuildReachableFallbackPath(
        bool requireWaterEdge,
        bool requireLoadedGround,
        out Vector3 destination,
        bool useLocalArea = false)
    {
        destination = simulationPosition;
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null)
        {
            return false;
        }

        navigationWaypoints ??= new Vector3[MaxNavigationWaypoints];
        int waypointCount = AnimalGridPathfinder.FindReachableTargetPath(
            terrain,
            simulationPosition,
            useLocalArea ? simulationPosition : GetRoamingAreaCenter(),
            useLocalArea ? Mathf.Min(LocalRoamingSearchRadius, GetRoamingAreaRadius()) : GetRoamingAreaRadius(),
            requireLoadedGround,
            requireWaterEdge,
            settings.ArrivalDistance * 2f,
            NextRandomUInt(),
            navigationWaypoints,
            out destination);
        return StoreNavigationPath(destination, waypointCount);
    }

    private bool EnsureNavigationForTarget(bool requireLoadedGround)
    {
        Vector3 goalOffset = navigationGoal - targetPosition;
        goalOffset.y = 0f;
        if (navigationPrepared && goalOffset.sqrMagnitude <= 0.0001f
            && navigationRevision == (TerrainGenerator.Active?.AnimalNavigationRevision ?? 0L))
        {
            return true;
        }

        if (CanNavigateDirectly(targetPosition, requireLoadedGround))
        {
            PrepareDirectNavigation(targetPosition);
            return true;
        }

        return TryBuildNavigationPath(targetPosition, requireLoadedGround);
    }

    private bool CanNavigateDirectly(Vector3 destination, bool requireLoadedGround)
    {
        GetNavigationArea(destination, out Vector3 areaCenter, out float areaRadius);
        return CanNavigateDirectly(
            destination,
            areaCenter,
            areaRadius,
            requireLoadedGround);
    }

    private bool CanNavigateDirectly(
        Vector3 destination,
        Vector3 areaCenter,
        float areaRadius,
        bool requireLoadedGround)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        return terrain == null
               || AnimalGridPathfinder.HasWalkableLine(
                   terrain,
                   simulationPosition,
                   destination,
                   areaCenter,
                   areaRadius,
                   requireLoadedGround);
    }

    private bool TryBuildNavigationPath(
        Vector3 destination,
        bool requireLoadedGround,
        bool checkDirectPath = true)
    {
        GetNavigationArea(destination, out Vector3 areaCenter, out float areaRadius);
        return TryBuildNavigationPath(
            destination,
            areaCenter,
            areaRadius,
            requireLoadedGround,
            checkDirectPath);
    }

    private void GetNavigationArea(
        Vector3 destination,
        out Vector3 areaCenter,
        out float areaRadius)
    {
        if (!IsSaddledFreeRoaming
            && (currentState == AnimalAIState.Drink
                || currentState == AnimalAIState.Eat
                || IsOutsideRoamingArea(simulationPosition)))
        {
            GetExtendedNavigationArea(destination, out areaCenter, out areaRadius);
            return;
        }

        areaCenter = GetRoamingAreaCenter();
        areaRadius = GetRoamingAreaRadius();
    }

    private bool IsOutsideRoamingArea(Vector3 position)
    {
        Vector3 areaOffset = position - GetRoamingAreaCenter();
        areaOffset.y = 0f;
        float areaRadius = GetRoamingAreaRadius();
        return areaOffset.sqrMagnitude > areaRadius * areaRadius;
    }

    private bool TryBuildNavigationPath(
        Vector3 destination,
        Vector3 areaCenter,
        float areaRadius,
        bool requireLoadedGround,
        bool checkDirectPath = true)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null)
        {
            PrepareDirectNavigation(destination);
            return true;
        }

        if (checkDirectPath
            && CanNavigateDirectly(
                destination,
                areaCenter,
                areaRadius,
                requireLoadedGround))
        {
            PrepareDirectNavigation(destination);
            return true;
        }

        navigationWaypoints ??= new Vector3[MaxNavigationWaypoints];
        int waypointCount = AnimalGridPathfinder.FindPath(
            terrain,
            simulationPosition,
            destination,
            areaCenter,
            areaRadius,
            requireLoadedGround,
            navigationWaypoints);
        return StoreNavigationPath(destination, waypointCount);
    }

    private bool StoreNavigationPath(Vector3 destination, int waypointCount)
    {
        if (waypointCount <= 0)
        {
            navigationPrepared = false;
            navigationWaypointCount = 0;
            navigationWaypointIndex = 0;
            return false;
        }

        navigationGoal = destination;
        navigationPrepared = true;
        navigationRevision = TerrainGenerator.Active?.AnimalNavigationRevision ?? 0L;
        navigationWaypointCount = waypointCount;
        navigationWaypointIndex = 0;
        navigationBlockedTime = 0f;
        return true;
    }

    private void PrepareDirectNavigation(Vector3 destination)
    {
        navigationGoal = destination;
        navigationPrepared = true;
        navigationRevision = TerrainGenerator.Active?.AnimalNavigationRevision ?? 0L;
        navigationWaypointCount = 0;
        navigationWaypointIndex = 0;
        navigationBlockedTime = 0f;
    }

    private void GetCurrentNavigationTarget(
        Vector3 position,
        out Vector3 result)
    {
        float arrivalDistanceSqr = settings.ArrivalDistance * settings.ArrivalDistance;
        while (navigationWaypointIndex < navigationWaypointCount)
        {
            Vector3 waypoint = navigationWaypoints[navigationWaypointIndex];
            Vector3 offset = waypoint - position;
            offset.y = 0f;
            if (offset.sqrMagnitude > arrivalDistanceSqr)
            {
                result = waypoint;
                return;
            }

            navigationWaypointIndex++;
        }

        navigationWaypointCount = 0;
        navigationWaypointIndex = 0;
        result = targetPosition;
    }

    private bool TryGetTerrainEscapeTarget(
        Vector3 position,
        bool requireLoadedGround,
        out Vector3 result)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null
            || terrain.CanAnimalMoveTo(position, requireLoadedGround))
        {
            if (terrainEscapeActive)
            {
                terrainEscapeActive = false;
                navigationPrepared = false;
                navigationWaypointCount = 0;
                navigationWaypointIndex = 0;
            }

            result = position;
            return false;
        }

        if (!terrainEscapeActive
            || !terrain.CanAnimalMoveTo(
                terrainEscapeTarget,
                requireLoadedGround))
        {
            if (!AnimalGridPathfinder.TryFindNearestWalkable(
                    terrain,
                    position,
                    requireLoadedGround,
                    out terrainEscapeTarget))
            {
                result = position;
                return false;
            }

            terrainEscapeActive = true;
            navigationBlockedTime = 0f;
        }

        result = terrainEscapeTarget;
        return true;
    }

    private bool HandleBlockedMovement(
        float deltaTime,
        bool requireLoadedGround,
        bool escapingTerrain)
    {
        navigationBlockedTime += deltaTime;
        if (escapingTerrain)
        {
            if (navigationBlockedTime >= AbandonBlockedTargetDelay)
            {
                terrainEscapeActive = false;
                navigationBlockedTime = 0f;
            }

            return false;
        }

        if (navigationBlockedTime >= BlockedRepathDelay
            && navigationRepathCooldown <= 0f)
        {
            navigationRepathCooldown = RepathCooldown;
            float accumulatedBlockedTime = navigationBlockedTime;
            TryBuildNavigationPath(targetPosition, requireLoadedGround);
            navigationBlockedTime = accumulatedBlockedTime;
        }

        if (navigationBlockedTime < AbandonBlockedTargetDelay)
        {
            return false;
        }

        AbandonCurrentNavigationTarget();
        return false;
    }

    private void AbandonCurrentNavigationTarget()
    {
        if (herdReturnTargetActive)
        {
            SuppressHerdReturnRetry();
        }

        stuckTargetAbandonCount++;
        preferReachableFallbackTarget = true;
        ResetToIdleBehavior();
    }

    private void SuppressHerdReturnRetry()
    {
        // 원래 무리 중심이 우리 밖에 있는 경우 매 틱 같은 복귀를 요구하지 않는다.
        herdReturnRetryCooldown = Mathf.Max(
            herdReturnRetryCooldown,
            HerdReturnRetryDelay);
        herdReturnTargetActive = false;
        herdReturnSuppressionCount++;
    }

    private bool IsNavigationProgressStalled(
        Vector3 movementTarget,
        Vector3 candidatePosition,
        float deltaTime)
    {
        Vector3 targetChange = movementTarget - navigationProgressTarget;
        targetChange.y = 0f;
        Vector3 remaining = movementTarget - candidatePosition;
        remaining.y = 0f;
        float distance = AnimalSimulationMath.Magnitude(remaining);
        if (!navigationProgressTracked || targetChange.sqrMagnitude > 0.0001f)
        {
            navigationProgressTracked = true;
            navigationProgressTarget = movementTarget;
            navigationBestDistance = distance;
            navigationNoProgressTime = 0f;
            return false;
        }

        // 충돌 회피로 옆걸음만 계속하는 경우에도 단순 이동 성공으로 정체 시간이
        // 초기화되지 않도록 웨이포인트까지의 실제 거리 감소를 기준으로 삼는다.
        if (distance <= navigationBestDistance - NavigationProgressEpsilon)
        {
            navigationBestDistance = distance;
            navigationNoProgressTime = 0f;
            return false;
        }

        navigationNoProgressTime += Mathf.Max(0f, deltaTime);
        return navigationNoProgressTime >= AbandonBlockedTargetDelay;
    }

    private void ResetNavigation()
    {
        navigationWaypointCount = 0;
        navigationWaypointIndex = 0;
        navigationGoal = Vector3.zero;
        navigationPrepared = false;
        navigationBlockedTime = 0f;
        navigationRepathCooldown = 0f;
        navigationProgressTarget = Vector3.zero;
        navigationBestDistance = 0f;
        navigationNoProgressTime = 0f;
        navigationProgressTracked = false;
        herdReturnTargetActive = false;
        terrainEscapeActive = false;
        terrainEscapeTarget = Vector3.zero;
        ClearAvoidanceCommitment();
    }

    private static int AppendNavigationPathPoint(
        Vector3[] destination,
        int count,
        Vector3 point)
    {
        if (count >= destination.Length)
        {
            return count;
        }

        if (count > 0)
        {
            Vector3 offset = point - destination[count - 1];
            offset.y = 0f;
            if (offset.sqrMagnitude <= 0.0001f)
            {
                return count;
            }
        }

        destination[count] = point;
        return count + 1;
    }

    private bool MoveTowardTarget(float deltaTime, bool useLiveCollision)
    {
        using var sample = AnimalAIProfiler.Sample("Animal Movement");
        if (animal != null && !animal.IsAlive)
        {
            StopForDeath();
            return false;
        }

        Vector3 position = simulationPosition;
        bool escapingTerrain = TryGetTerrainEscapeTarget(
            position,
            useLiveCollision,
            out Vector3 movementTarget);
        if (!escapingTerrain)
        {
            if (!EnsureNavigationForTarget(useLiveCollision))
            {
                AbandonCurrentNavigationTarget();
                return false;
            }

            GetCurrentNavigationTarget(position, out movementTarget);
        }

        Vector3 toTarget = movementTarget - position;
        toTarget.y = 0f;
        float arrivalDistance = settings.ArrivalDistance;
        if (toTarget.sqrMagnitude <= arrivalDistance * arrivalDistance)
        {
            if (escapingTerrain)
            {
                terrainEscapeActive = false;
                navigationPrepared = false;
            }
            else
            {
                hasTarget = false;
                movingToActivity = false;
                ResetNavigation();
            }

            return false;
        }

        Vector3 desiredDirection = AnimalSimulationMath.Normalize(toTarget);
        Vector3 flockSteering = Vector3.zero;
        AnimalAIWorld world = escapingTerrain ? null : AnimalAIWorld.Instance;
        if (world != null)
        {
            if (!IsSaddledFreeRoaming
                && currentState != AnimalAIState.Drink
                && currentState != AnimalAIState.Eat
                && world.TryGetHerdCenter(
                    HerdId,
                    out Vector3 currentHerdCenter))
            {
                Vector3 cohesion = currentHerdCenter - position;
                cohesion.y = 0f;
                if (cohesion.sqrMagnitude > 0.01f)
                {
                    flockSteering += AnimalSimulationMath.Normalize(cohesion) * settings.CohesionWeight;
                }
            }

            flockSteering += UpdateSmoothedSeparation(world, deltaTime)
                             * settings.SeparationWeight;
        }
        else
        {
            UpdateSmoothedSeparation(null, deltaTime);
        }

        Vector3 roamingAreaCenter = GetRoamingAreaCenter();
        float areaRadius = GetRoamingAreaRadius();
        bool restrictToRoamingArea = currentState != AnimalAIState.Drink
                                     && currentState != AnimalAIState.Eat
                                     || IsSaddledFreeRoaming;
        bool startedOutsideRoamingArea = false;
        if (!escapingTerrain && restrictToRoamingArea)
        {
            Vector3 areaOffset = position - roamingAreaCenter;
            areaOffset.y = 0f;
            startedOutsideRoamingArea = areaOffset.sqrMagnitude > areaRadius * areaRadius;
            if (startedOutsideRoamingArea)
            {
                flockSteering += (-AnimalSimulationMath.Normalize(areaOffset)) * 4f;
            }
        }

        // Social steering may bend a route, but must never reverse its direction.
        Vector3 direction = desiredDirection + AnimalSimulationMath.ClampMagnitude(flockSteering, 0.35f);
        if (direction.sqrMagnitude <= 0.0001f)
        {
            if (!escapingTerrain
                && IsNavigationProgressStalled(
                    movementTarget,
                    position,
                    deltaTime))
            {
                AbandonCurrentNavigationTarget();
            }

            return false;
        }

        direction = AnimalSimulationMath.Normalize(direction);
        float speed = Mathf.Min(
            GetEffectiveMoveSpeed(),
            AnimalSimulationMath.Magnitude(toTarget) / Mathf.Max(deltaTime, 0.0001f));
        MovementAvailability availability = ResolveMovementDirection(
            position,
            direction,
            speed,
            deltaTime,
            useLiveCollision,
            escapingTerrain,
            out direction);
        if (availability != MovementAvailability.Clear)
        {
            if (availability == MovementAvailability.BlockedByAnimal)
            {
                navigationBlockedTime = 0f;
                if (!escapingTerrain
                    && IsNavigationProgressStalled(
                        movementTarget,
                        position,
                        deltaTime))
                {
                    AbandonCurrentNavigationTarget();
                }

                return false;
            }

            if (!escapingTerrain
                && IsNavigationProgressStalled(
                    movementTarget,
                    position,
                    deltaTime))
            {
                AbandonCurrentNavigationTarget();
                return false;
            }

            return HandleBlockedMovement(
                deltaTime,
                useLiveCollision,
                escapingTerrain);
        }

        Vector3 candidate = AnimalSimulationMath.Advance(position, direction, speed, deltaTime);
        candidate.y = position.y;
        if (!escapingTerrain
            && restrictToRoamingArea
            && !startedOutsideRoamingArea)
        {
            Vector3 clampedOffset = candidate - roamingAreaCenter;
            clampedOffset.y = 0f;
            if (clampedOffset.sqrMagnitude > areaRadius * areaRadius)
            {
                Vector3 clamped = roamingAreaCenter + AnimalSimulationMath.Normalize(clampedOffset) * areaRadius;
                candidate.x = clamped.x;
                candidate.z = clamped.z;
                if (!CanOccupyTerrain(candidate, useLiveCollision))
                {
                    if (IsNavigationProgressStalled(
                            movementTarget,
                            position,
                            deltaTime))
                    {
                        AbandonCurrentNavigationTarget();
                        return false;
                    }

                    return HandleBlockedMovement(deltaTime, useLiveCollision, false);
                }

                if (useLiveCollision)
                {
                    MovementAvailability boundaryAvailability =
                        ProbeOccupancyPosition(
                            position,
                            candidate,
                            GetObstacleRadius());
                    if (boundaryAvailability == MovementAvailability.BlockedByAnimal)
                    {
                        navigationBlockedTime = 0f;
                        if (IsNavigationProgressStalled(
                                movementTarget,
                                position,
                                deltaTime))
                        {
                            AbandonCurrentNavigationTarget();
                        }

                        return false;
                    }

                    if (boundaryAvailability
                        == MovementAvailability.BlockedByStaticObstacle)
                    {
                        if (IsNavigationProgressStalled(
                                movementTarget,
                                position,
                                deltaTime))
                        {
                            AbandonCurrentNavigationTarget();
                            return false;
                        }

                        return HandleBlockedMovement(
                            deltaTime,
                            useLiveCollision,
                            false);
                    }
                }
            }
        }

        if (!escapingTerrain
            && IsNavigationProgressStalled(
                movementTarget,
                candidate,
                deltaTime))
        {
            AbandonCurrentNavigationTarget();
            return false;
        }

        ApplyMovement(candidate, direction, deltaTime);
        navigationBlockedTime = 0f;
        return true;
    }

    private void ApplyMovement(
        Vector3 position,
        Vector3 direction,
        float deltaTime)
    {
        simulationPosition = AnimalSimulationMath.Quantize(position);
        if (settings.TurnSpeed > 0f)
        {
            simulationYaw = AnimalSimulationMath.Turn(simulationYaw, direction, settings.TurnSpeed, Mathf.Min(deltaTime, MaximumRotationDeltaTime));
        }
    }

    private Vector3 UpdateSmoothedSeparation(
        AnimalAIWorld world,
        float deltaTime)
    {
        Vector3 target = world != null
            ? world.GetSeparation(this, settings.SeparationRadius)
            : Vector3.zero;
        float blend = Mathf.Clamp01(SeparationSmoothingRate * Mathf.Max(0f, deltaTime));
        smoothedSeparation = Vector3.Lerp(
            smoothedSeparation,
            target,
            blend);
        smoothedSeparation = AnimalSimulationMath.Quantize(smoothedSeparation);
        if (smoothedSeparation.sqrMagnitude < 0.000001f)
        {
            smoothedSeparation = Vector3.zero;
        }

        return smoothedSeparation;
    }

    private MovementAvailability ResolveMovementDirection(
        Vector3 origin,
        Vector3 desiredDirection,
        float speed,
        float deltaTime,
        bool useLiveCollision,
        bool allowTerrainEscape,
        out Vector3 direction)
    {
        float moveDistance = speed * deltaTime;
        if (moveDistance <= 0f)
        {
            direction = Vector3.zero;
            return MovementAvailability.BlockedByStaticObstacle;
        }

        MovementAvailability desiredAvailability = ProbeMovementDirection(
            origin,
            desiredDirection,
            moveDistance,
            useLiveCollision,
            allowTerrainEscape);
        bool hasCommittedAvoidance = committedAvoidanceDirection.sqrMagnitude
                                     > 0.0001f;
        if (hasCommittedAvoidance
            && !CanUseAvoidanceDirection(desiredDirection, committedAvoidanceDirection, allowTerrainEscape))
        {
            ClearAvoidanceCommitment();
            hasCommittedAvoidance = false;
        }
        if (hasCommittedAvoidance)
        {
            MovementAvailability committedAvailability = ProbeMovementDirection(
                origin,
                committedAvoidanceDirection,
                moveDistance,
                useLiveCollision,
                allowTerrainEscape);
            if (committedAvailability == MovementAvailability.Clear)
            {
                if (desiredAvailability == MovementAvailability.Clear)
                {
                    avoidanceDirectClearTime += deltaTime;
                    if (avoidanceDirectClearTime >= AvoidanceExitClearDuration)
                    {
                        ClearAvoidanceCommitment();
                        direction = desiredDirection;
                        return MovementAvailability.Clear;
                    }
                }
                else
                {
                    avoidanceDirectClearTime = 0f;
                }

                direction = committedAvoidanceDirection;
                return MovementAvailability.Clear;
            }

            avoidanceDirectClearTime = 0f;
            if (committedAvailability == MovementAvailability.BlockedByAnimal)
            {
                // 선택한 회피 방향을 다른 동물이 잠깐 막은 경우에는
                // 새 각도를 고르지 않는다. 같은 방향을 바라보며 양보한다.
                direction = Vector3.zero;
                return MovementAvailability.BlockedByAnimal;
            }

            if (desiredAvailability == MovementAvailability.Clear)
            {
                ClearAvoidanceCommitment();
                direction = desiredDirection;
                return MovementAvailability.Clear;
            }
        }

        if (desiredAvailability == MovementAvailability.Clear)
        {
            ClearAvoidanceCommitment();
            direction = desiredDirection;
            return MovementAvailability.Clear;
        }

        MovementAvailability avoidanceAvailability = FindAvoidanceDirection(
            origin,
            desiredDirection,
            moveDistance,
            useLiveCollision,
            allowTerrainEscape,
            out direction);
        return avoidanceAvailability == MovementAvailability.BlockedByStaticObstacle
               && desiredAvailability == MovementAvailability.BlockedByAnimal
            ? MovementAvailability.BlockedByAnimal
            : avoidanceAvailability;
    }

    private MovementAvailability FindAvoidanceDirection(
        Vector3 origin,
        Vector3 forward,
        float moveDistance,
        bool useLiveCollision,
        bool allowTerrainEscape,
        out Vector3 direction)
    {
        float preferredSign = avoidanceTurnSign;
        bool blockedByAnimal = false;
        if (TryFindAvoidanceDirectionOnSide(
                origin,
                forward,
                moveDistance,
                useLiveCollision,
                allowTerrainEscape,
                preferredSign,
                ref blockedByAnimal,
                out direction)
            || TryFindAvoidanceDirectionOnSide(
                origin,
                forward,
                moveDistance,
                useLiveCollision,
                allowTerrainEscape,
                -preferredSign,
                ref blockedByAnimal,
                out direction))
        {
            return MovementAvailability.Clear;
        }

        Vector3 reverseDirection = -forward;
        MovementAvailability reverseAvailability = CanUseAvoidanceDirection(forward, reverseDirection, allowTerrainEscape)
            ? ProbeMovementDirection(
            origin,
            reverseDirection,
            moveDistance,
            useLiveCollision,
            allowTerrainEscape)
            : MovementAvailability.BlockedByStaticObstacle;
        if (reverseAvailability == MovementAvailability.Clear)
        {
            CommitAvoidance(reverseDirection, preferredSign);
            direction = committedAvoidanceDirection;
            return MovementAvailability.Clear;
        }

        blockedByAnimal |= reverseAvailability == MovementAvailability.BlockedByAnimal;
        ClearAvoidanceCommitment();
        direction = Vector3.zero;
        return blockedByAnimal
            ? MovementAvailability.BlockedByAnimal
            : MovementAvailability.BlockedByStaticObstacle;
    }

    private bool TryFindAvoidanceDirectionOnSide(
        Vector3 origin,
        Vector3 forward,
        float moveDistance,
        bool useLiveCollision,
        bool allowTerrainEscape,
        float turnSign,
        ref bool blockedByAnimal,
        out Vector3 direction)
    {
        for (int i = 0; i < AvoidanceAngles.Length; i++)
        {
            float angle = AvoidanceAngles[i];
            if (angle >= 179f)
            {
                break;
            }

            Vector3 candidateDirection = AnimalSimulationMath.Rotate(forward, angle * turnSign);
            if (!CanUseAvoidanceDirection(forward, candidateDirection, allowTerrainEscape))
            {
                continue;
            }

            MovementAvailability availability = ProbeMovementDirection(
                origin,
                candidateDirection,
                moveDistance,
                useLiveCollision,
                allowTerrainEscape);
            if (availability == MovementAvailability.Clear)
            {
                CommitAvoidance(candidateDirection, turnSign);
                direction = committedAvoidanceDirection;
                return true;
            }

            blockedByAnimal |= availability == MovementAvailability.BlockedByAnimal;
        }

        direction = Vector3.zero;
        return false;
    }

    private bool CanUseAvoidanceDirection(Vector3 forward, Vector3 candidate, bool allowTerrainEscape)
    {
        return allowTerrainEscape || currentState == AnimalAIState.Flee || !hasTarget
               || Vector3.Dot(forward, candidate) > 0.05f;
    }

    private void CommitAvoidance(Vector3 direction, float turnSign)
    {
        committedAvoidanceDirection = AnimalSimulationMath.Normalize(direction);
        avoidanceTurnSign = turnSign >= 0f ? 1f : -1f;
        avoidanceDirectClearTime = 0f;
    }

    private void ClearAvoidanceCommitment()
    {
        committedAvoidanceDirection = Vector3.zero;
        avoidanceDirectClearTime = 0f;
    }

    private MovementAvailability ProbeMovementDirection(
        Vector3 origin,
        Vector3 direction,
        float moveDistance,
        bool useLiveCollision,
        bool allowTerrainEscape)
    {
        AnimalAIProfiler.Add(AnimalAIProfiler.Counter.AvoidanceProbes);
        Vector3 candidate = origin + direction * moveDistance;
        candidate.y = origin.y;
        bool originIsWalkable = CanOccupyTerrain(origin, useLiveCollision);
        bool candidateIsWalkable = CanOccupyTerrain(candidate, useLiveCollision);
        if (!candidateIsWalkable
            && (!allowTerrainEscape
                || originIsWalkable
                || !MovesCloserToTerrainEscape(origin, candidate)))
        {
            return MovementAvailability.BlockedByStaticObstacle;
        }

        if (!useLiveCollision)
        {
            return MovementAvailability.Clear;
        }

        float radius = GetObstacleRadius();
        MovementAvailability positionAvailability = ProbeOccupancyPosition(
            origin,
            candidate,
            radius);
        if (positionAvailability
            == MovementAvailability.BlockedByStaticObstacle)
        {
            return positionAvailability;
        }

        float probeDistance = Mathf.Max(moveDistance, settings.ObstacleProbeDistance);
        if (hasTarget && currentState != AnimalAIState.Flee && !allowTerrainEscape)
        {
            Vector3 waypoint = navigationWaypointIndex < navigationWaypointCount
                ? navigationWaypoints[navigationWaypointIndex]
                : targetPosition;
            Vector3 remaining = waypoint - origin;
            remaining.y = 0f;
            // A wall beyond the destination does not block reaching the destination.
            probeDistance = Mathf.Min(probeDistance, Mathf.Max(moveDistance, AnimalSimulationMath.Magnitude(remaining)));
        }
        Vector3 probe = origin + direction * probeDistance;
        probe.y = origin.y;
        if (!IsGridPathClearOrEscaping(origin, direction, probeDistance, radius)
            || !CanUsePredictiveTerrainProbe(probe))
        {
            return MovementAvailability.BlockedByStaticObstacle;
        }

        return positionAvailability;
    }

    private MovementAvailability ProbePlayerPush(
        Vector3 origin,
        Vector3 candidate,
        Vector3 direction,
        float distance)
    {
        if (!CanOccupyTerrain(candidate, true))
        {
            return MovementAvailability.BlockedByStaticObstacle;
        }

        float radius = GetObstacleRadius();
        MovementAvailability positionAvailability = ProbeOccupancyPosition(
            origin,
            candidate,
            radius);
        if (positionAvailability == MovementAvailability.BlockedByStaticObstacle
            || !IsGridPathClearOrEscaping(
                origin,
                direction,
                distance,
                radius))
        {
            return MovementAvailability.BlockedByStaticObstacle;
        }

        return positionAvailability;
    }

    private void ApplyPlayerPush(Vector3 position)
    {
        simulationPosition = AnimalSimulationMath.Quantize(position);
        AnimalAIWorld.NotifySpatialChanged(this);
        navigationBlockedTime = 0f;
        ResetPresentation();
        transform.SetPositionAndRotation(simulationPosition, simulationRotation);
        animal?.MarkTerrainInteraction();
    }

    private bool IsGridPathClearOrEscaping(Vector3 origin, Vector3 direction, float distance, float radius)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null || distance <= 0f) return true;
        direction = AnimalSimulationMath.Normalize(direction);
        int samples = Mathf.Max(1, Mathf.CeilToInt(distance * 8f));
        Vector3 previous = origin;
        for (int i = 1; i <= samples; i++)
        {
            Vector3 next = AnimalSimulationMath.Quantize(origin + direction * (distance * i / samples));
            if (!terrain.IsAnimalObstaclePositionClear(previous, next, radius, true)) return false;
            previous = next;
        }
        return true;
    }

    private bool MovesCloserToTerrainEscape(Vector3 origin, Vector3 candidate)
    {
        Vector3 originOffset = terrainEscapeTarget - origin;
        Vector3 candidateOffset = terrainEscapeTarget - candidate;
        originOffset.y = 0f;
        candidateOffset.y = 0f;
        return candidateOffset.sqrMagnitude < originOffset.sqrMagnitude;
    }

    private bool CanOccupyTerrain(Vector3 position, bool requireLoadedGround)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        return terrain == null || terrain.CanAnimalMoveTo(position, requireLoadedGround);
    }

    private bool CanUsePredictiveTerrainProbe(Vector3 position)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain != null)
        {
            Vector2Int coordinate = new Vector2Int(
                Mathf.RoundToInt(position.x),
                Mathf.RoundToInt(position.z));

            // 물은 실제 다음 위치에서만 차단한다. 먼 예측 지점의 물까지 막으면
            // 물가에 있는 Drink 목표에 접근할 수 없고 해안을 따라 이동할 수도 없다.
            if (terrain.IsWaterBiomeAt(coordinate))
            {
                return true;
            }

            if (!terrain.CanAnimalMoveTo(position, true))
            {
                return false;
            }
        }

        return true;
    }

    private MovementAvailability ProbeOccupancyPosition(Vector3 origin, Vector3 position, float radius, bool allowEscape = true)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain != null && !terrain.IsAnimalObstaclePositionClear(origin, position, radius, allowEscape))
            return MovementAvailability.BlockedByStaticObstacle;
        AnimalAIWorld world = AnimalAIWorld.Instance;
        return world != null && world.HasSpatialIndex
            && !world.IsAnimalPositionClearOrEscaping(this, origin, position, radius, allowEscape)
            ? MovementAvailability.BlockedByAnimal : MovementAvailability.Clear;
    }

    private float GetObstacleRadius()
    {
        return Mathf.Max(
                   GetConfiguredObstacleRadius(),
                   avoidanceColliderRadius)
               + ObstacleClearanceSkin;
    }

    private float GetConfiguredObstacleRadius()
    {
        return Mathf.Max(0.15f, settings.SeparationRadius * 0.35f);
    }

    private float GetEffectiveMoveSpeed()
    {
        float age = animal != null ? Mathf.Clamp01(animal.Age * 0.1f) : 1f;
        float configuredAgeMultiplier = Mathf.Lerp(settings.YoungSpeedMultiplier, 1f, age);
        float configuredGenderMultiplier = animal != null && animal.Gender == Animal.AnimalGender.Male
            ? settings.MaleSpeedMultiplier
            : settings.FemaleSpeedMultiplier;
        float ageMultiplier = ResolveReducedSpeedMultiplierInfluence(configuredAgeMultiplier);
        float genderMultiplier = ResolveReducedSpeedMultiplierInfluence(configuredGenderMultiplier);
        return settings.MoveSpeed * ageMultiplier * genderMultiplier;
    }

    private static float ResolveReducedSpeedMultiplierInfluence(float configuredMultiplier)
    {
        return Mathf.Lerp(1f, configuredMultiplier, AgeGenderSpeedMultiplierInfluence);
    }

    private void SyncBehaviorAnimationActivity()
    {
        behaviorAnimationActivityInitialized = true;
        // Rider/leash/draft movement is independent of the AI scheduler's pause/radius.
        animal?.SetBehaviorAnimationActive(executionActive || IsExternallyControlled);
    }

    private void ApplyAnimation(
        float speed,
        bool isRunning = false,
        float locomotionPlaybackScale = 1f)
    {
        if (animal == null)
        {
            return;
        }

        SyncBehaviorAnimationActivity();
        float resolvedSpeed = Mathf.Max(0f, speed);
        bool performingActivity = !movingToActivity && !hasTarget;
        animal.SetAIAnimation(
            resolvedSpeed,
            performingActivity
            && currentState == AnimalAIState.Eat
            && stateTimeRemaining > 0f,
            performingActivity && currentState == AnimalAIState.Drink,
            performingActivity && currentState == AnimalAIState.Rest && IsNightTime(),
            performingActivity && currentState == AnimalAIState.LookAround,
            currentState == AnimalAIState.Flee && resolvedSpeed > 0.01f,
            isRunning,
            locomotionPlaybackScale);
    }

    private static bool IsNightTime()
    {
        WorldTimeService worldTime = WorldTimeService.Active;
        return worldTime != null && !worldTime.IsDay;
    }

    private float RandomDuration(Vector2 range)
    {
        return Mathf.Lerp(range.x, range.y, Next01());
    }

    private float Next01()
    {
        return (NextRandomUInt() & 0x00FFFFFFu) / 16777216f;
    }

    private uint NextRandomUInt()
    {
        uint value = randomState;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        randomState = value != 0u ? value : 0x6D2B79F5u;
        return randomState;
    }

    private uint BuildInitialRandomState()
    {
        unchecked
        {
            long id = ResolveSimulationId();
            uint value = (uint)id ^ (uint)(id >> 32) ^ 0x9E3779B9u;
            return value != 0u ? value : 0x6D2B79F5u;
        }
    }

    private long ResolveSimulationId()
    {
        if (terrainInstance != null && terrainInstance.DeterministicId != 0L)
        {
            return terrainInstance.DeterministicId;
        }

        if (fallbackSimulationId != 0L) return fallbackSimulationId;
        Vector3 position = transform.position;
        int x = Mathf.RoundToInt(position.x * 1000f);
        int z = Mathf.RoundToInt(position.z * 1000f);
        int definitionId = definition != null ? definition.Id : 0;
        unchecked
        {
            ulong value = 1469598103934665603UL;
            value = (value ^ (uint)x) * 1099511628211UL;
            value = (value ^ (uint)z) * 1099511628211UL;
            value = (value ^ (uint)definitionId) * 1099511628211UL;
            fallbackSimulationId = (long)value;
            return fallbackSimulationId;
        }
    }

    private static AnimalAIState ClampState(int value)
    {
        return value >= (int)AnimalAIState.Idle && value <= (int)AnimalAIState.Eat
            ? (AnimalAIState)value
            : AnimalAIState.Idle;
    }
}
