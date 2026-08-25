using UnityEngine;

public enum AnimalAIState
{
    Idle,
    Wander,
    Graze,
    Drink,
    Rest,
    LookAround,
    Flee
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
    private const int ObstacleBufferCapacity = 16;
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
    private const float AgeGenderSpeedMultiplierInfluence = 1f / 3f;
    private const float MountedMovementDirectionEpsilonSqr = 0.0000001f;
    private const float HealthRecoveryFractionPerSecond = 0.05f;
    private const float PostAggroHealthRecoveryDelay = 1f;

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

    private readonly Collider[] obstacleBuffer = new Collider[ObstacleBufferCapacity];
    private readonly RaycastHit[] obstacleSweepBuffer = new RaycastHit[ObstacleBufferCapacity];

    private Animal animal;
    private AnimalDefinition definition;
    private TerrainAnimalInstance terrainInstance;
    private AnimalAISettings settings;
    private AnimalAIState currentState;
    private float stateTimeRemaining;
    private Vector3 targetPosition;
    private bool hasTarget;
    private bool movingToActivity;
    private uint randomState;
    private bool configured;
    private bool executionActive;
    private bool nooseLeashed;
    private bool draftAttached;
    private Player mountedRider;
    private float scheduledTickAccumulator;
    private float scheduledRecoveryElapsedTime;
    private float scheduledTickPhase;
    private bool scheduledTickPhaseApplied;
    private Vector3 presentationStartPosition;
    private Vector3 presentationTargetPosition;
    private Quaternion presentationStartRotation;
    private Quaternion presentationTargetRotation;
    private float presentationDuration;
    private float presentationElapsed;
    private bool presentationActive;
    private Vector3 simulationPosition;
    private Quaternion simulationRotation;
    private bool simulationPoseInitialized;
    private bool waitingForStandUp;
    private Vector3[] navigationWaypoints;
    private int navigationWaypointCount;
    private int navigationWaypointIndex;
    private Vector3 navigationGoal;
    private bool navigationPrepared;
    private float navigationBlockedTime;
    private float navigationRepathCooldown;
    private Vector3 navigationProgressTarget;
    private float navigationBestDistance;
    private float navigationNoProgressTime;
    private bool navigationProgressTracked;
    private float herdReturnRetryCooldown;
    private bool herdReturnTargetActive;
    private bool preferReachableFallbackTarget;
    private bool terrainEscapeActive;
    private Vector3 terrainEscapeTarget;
    private Vector3 fleeThreatPosition;
    private bool hasFleeThreat;
    private float postAggroHealthRecoveryDelayRemaining = -1f;
    private int fleeRouteAttemptOffset;
    private int forcedThreatPulseCount;
    private Collider[] animalColliders;
    private int[] originalAnimalColliderLayers;
    private float avoidanceColliderRadius = 0.5f;
    private Vector3 committedAvoidanceDirection;
    private float avoidanceDirectClearTime;
    private float avoidanceTurnSign = 1f;
    private Vector3 smoothedSeparation;
    private Vector3 crowdSnapshotPosition;
    private bool crowdSnapshotValid;
    private int obstacleLayerMask = Physics.AllLayers;
    private int reachableFallbackTargetCount;
    private int stuckTargetAbandonCount;
    private int herdReturnSuppressionCount;

    private static bool obstacleLayerMaskInitialized;
    private static int cachedObstacleLayerMask = Physics.AllLayers;

    public Animal Animal => animal;
    public AnimalDefinition Definition => definition;
    public TerrainAnimalInstance TerrainInstance => terrainInstance;
    public AnimalAIState CurrentState => currentState;
    public float StateTimeRemaining => stateTimeRemaining;
    public Vector3 TargetPosition => targetPosition;
    public bool HasTarget => hasTarget;
    public long HerdId => terrainInstance != null ? terrainInstance.HerdId : 0L;
    public Vector3 HerdAreaCenter => terrainInstance != null ? terrainInstance.HerdCenter : transform.position;
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
    public float NooseMovementSpeed => configured && animal != null && animal.IsAlive
        ? GetEffectiveMoveSpeed()
        : 0f;
    public bool IsFleeing => configured && currentState == AnimalAIState.Flee && hasFleeThreat;
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
        AnimalAIWorld.Unregister(this);
    }

    public void Configure(
        Animal sourceAnimal,
        AnimalDefinition sourceDefinition,
        TerrainAnimalInstance sourceInstance,
        AnimalSaveEntry restoredState = null)
    {
        animal = sourceAnimal != null ? sourceAnimal : GetComponentInChildren<Animal>(true);
        definition = sourceDefinition != null
            ? sourceDefinition
            : animal != null
                ? animal.Definition
                : null;
        terrainInstance = sourceInstance != null
            ? sourceInstance
            : GetComponent<TerrainAnimalInstance>();
        settings = definition != null && definition.AISettings != null
            ? definition.AISettings
            : new AnimalAISettings();
        settings.Normalize();

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
        waitingForStandUp = false;
        hasFleeThreat = false;
        postAggroHealthRecoveryDelayRemaining = -1f;
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
        ResetScheduledTick();
        ResetPresentation();
        AnimalAIWorld.Register(this);
        ApplyAnimation(0f);
        animal?.TryRestorePendingDraftHandcart();
    }

    public bool QueueScheduledTick(float deltaTime, float interval)
    {
        if (!configured || !executionActive || IsExternallyControlled || deltaTime <= 0f)
        {
            return false;
        }

        float resolvedInterval = Mathf.Max(0.01f, interval);
        if (!scheduledTickPhaseApplied)
        {
            scheduledTickAccumulator = resolvedInterval * scheduledTickPhase;
            scheduledTickPhaseApplied = true;
        }

        scheduledTickAccumulator += deltaTime;
        scheduledRecoveryElapsedTime += deltaTime;
        if (scheduledTickAccumulator < resolvedInterval)
        {
            return false;
        }

        return true;
    }

    public bool ExecuteScheduledTick()
    {
        if (!configured || !executionActive || IsExternallyControlled || scheduledTickAccumulator <= 0f)
        {
            return false;
        }

        float simulationDelta = Mathf.Min(scheduledTickAccumulator, 0.2f);
        float recoveryElapsedTime = scheduledRecoveryElapsedTime;
        scheduledTickAccumulator = 0f;
        scheduledRecoveryElapsedTime = 0f;
        EnsureSimulationPoseInitialized();
        Vector3 framePosition = transform.position;
        Quaternion frameRotation = transform.rotation;
        transform.SetPositionAndRotation(simulationPosition, simulationRotation);
        TickSimulation(simulationDelta, recoveryElapsedTime, true);
        Vector3 nextSimulationPosition = transform.position;
        Quaternion nextSimulationRotation = transform.rotation;
        simulationPosition = nextSimulationPosition;
        simulationRotation = nextSimulationRotation;
        transform.SetPositionAndRotation(framePosition, frameRotation);
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
        animal?.SetDetailedVisuals(visible);
    }

    public void TickBackground(float deltaTime)
    {
        if (!configured || IsExternallyControlled || !IsInteracted || deltaTime <= 0f)
        {
            return;
        }

        EnsureSimulationPoseInitialized();
        transform.SetPositionAndRotation(simulationPosition, simulationRotation);
        TickSimulation(deltaTime, deltaTime, false);
        simulationPosition = transform.position;
        simulationRotation = transform.rotation;
    }

    public void SetBehaviorExecutionActive(bool active)
    {
        active &= !IsExternallyControlled;
        if (executionActive == active)
        {
            return;
        }

        executionActive = active;
        if (!active)
        {
            ResetScheduledTick();
            ResetPresentation();
            SnapToSimulationPose();
            ApplyAnimation(0f);
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
            ApplyAnimation(0f);
            return true;
        }

        if (!configured || animal == null || !animal.IsAlive || mountedRider != null)
        {
            return false;
        }

        bool wasResting = currentState == AnimalAIState.Rest || waitingForStandUp;
        nooseLeashed = true;
        executionActive = false;
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
        executionActive = false;
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
        if (attached)
        {
            executionActive = false;
        }

        ApplyAnimation(0f);
    }

    public void ApplyExternalControlledPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        transform.SetPositionAndRotation(worldPosition, worldRotation);
        simulationPosition = worldPosition;
        simulationRotation = worldRotation;
        simulationPoseInitialized = true;
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
            ApplyAnimation(0f);
            return false;
        }

        worldMoveDirection.y = 0f;
        float rawInputMagnitude = worldMoveDirection.magnitude;
        float inputMagnitude = Mathf.Clamp01(rawInputMagnitude);
        if (inputMagnitude <= 0.01f)
        {
            ApplyAnimation(0f);
            return false;
        }

        worldMoveDirection /= rawInputMagnitude;
        bool isRunning = runRequested;
        // 탑승 이동에도 야생 이동과 동일한 나이/성별 보정 속도를 사용한다.
        // RunSpeedRatio는 보정된 걷기 속도에 대한 달리기 비율이다.
        float movementSpeed = GetEffectiveMoveSpeed()
                              * inputMagnitude
                              * (isRunning ? settings.RunSpeedRatio : 1f);
        if (draftAttached && animal.IsAttachedToHandcart)
        {
            bool moved = animal.TryMoveAttachedHandcart(
                worldMoveDirection,
                movementSpeed,
                deltaTime,
                mountedRider,
                out float actualMoveSpeed);
            ApplyAnimation(actualMoveSpeed, isRunning && moved);
            return moved;
        }

        Vector3 previousPosition = simulationPosition;
        if (!TryApplyPlayerPush(
                previousPosition - worldMoveDirection,
                worldMoveDirection,
                movementSpeed * deltaTime))
        {
            ApplyAnimation(0f);
            return false;
        }

        Vector3 movedDirection = simulationPosition - previousPosition;
        movedDirection.y = 0f;
        if (movedDirection.sqrMagnitude > MountedMovementDirectionEpsilonSqr)
        {
            movedDirection.Normalize();
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

        ApplyAnimation(movementSpeed, isRunning);
        return true;
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
        float targetDistance = toTarget.magnitude;
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

    private void ResetScheduledTick()
    {
        scheduledTickAccumulator = 0f;
        scheduledRecoveryElapsedTime = 0f;
        scheduledTickPhaseApplied = false;
        uint phaseHash = randomState * 2654435761u;
        scheduledTickPhase = (phaseHash & 1023u) / 1024f;
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
        simulationPosition = transform.position;
        simulationRotation = transform.rotation;
        simulationPoseInitialized = true;
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

        obstacleLayerMask = GetObstacleLayerMask();
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

    private static int GetObstacleLayerMask()
    {
        if (obstacleLayerMaskInitialized)
        {
            return cachedObstacleLayerMask;
        }

        cachedObstacleLayerMask = Physics.AllLayers;
        ExcludeObstacleLayer(ref cachedObstacleLayerMask, LiveAnimalLayerName);
        ExcludeObstacleLayer(ref cachedObstacleLayerMask, "TransparentFX");
        ExcludeObstacleLayer(ref cachedObstacleLayerMask, "Ignore Raycast");
        ExcludeObstacleLayer(ref cachedObstacleLayerMask, "Water");
        ExcludeObstacleLayer(ref cachedObstacleLayerMask, "UI");
        obstacleLayerMaskInitialized = true;
        return cachedObstacleLayerMask;
    }

    private static void ExcludeObstacleLayer(ref int mask, string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0)
        {
            mask &= ~(1 << layer);
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

            playerMovement.Normalize();
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
            pushDirection.Normalize();
        }

        bool hasAnimalBlockedCandidate = false;
        Vector3 animalBlockedCandidate = Vector3.zero;
        for (int i = 0; i < PlayerPushAngles.Length; i++)
        {
            Vector3 candidateDirection = Quaternion.Euler(
                0f,
                PlayerPushAngles[i],
                0f) * pushDirection;
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

        entry.herdId = HerdId;
        entry.herdCenter = HerdAreaCenter;
        entry.herdRadius = HerdAreaRadius;
        bool hasTransientFleeState = currentState == AnimalAIState.Flee;
        entry.behaviorState = hasTransientFleeState
            ? (int)AnimalAIState.Idle
            : (int)currentState;
        entry.behaviorTimeRemaining = hasTransientFleeState
            ? 0f
            : Mathf.Max(0f, stateTimeRemaining);
        entry.targetPosition = hasTransientFleeState ? SimulationPosition : targetPosition;
        entry.hasTarget = !hasTransientFleeState && hasTarget;
        entry.movingToActivity = !hasTransientFleeState && movingToActivity;
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
                transform.position,
                out Vector3 direction)
            || direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            settings.TurnSpeed * Mathf.Min(deltaTime, MaximumRotationDeltaTime));
    }

    private bool WaitForStandUpBeforeMovement(bool useLiveCollision)
    {
        if (!waitingForStandUp)
        {
            return false;
        }

        if (!useLiveCollision || animal == null)
        {
            waitingForStandUp = false;
            return false;
        }

        if (!animal.IsReadyForAIMovement())
        {
            return true;
        }

        waitingForStandUp = false;
        ApplyAnimation(0f);
        return false;
    }

    private void BeginNextBehavior(bool requireLoadedGround)
    {
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

        switch (currentState)
        {
            case AnimalAIState.Wander:
                hasTarget = TryChooseTarget(false, requireLoadedGround, out targetPosition);
                break;
            case AnimalAIState.Graze:
                hasTarget = TryChooseTarget(false, requireLoadedGround, out targetPosition);
                movingToActivity = hasTarget;
                break;
            case AnimalAIState.Drink:
                hasTarget = TryChooseDrinkTarget(
                    requireLoadedGround,
                    out targetPosition);
                movingToActivity = hasTarget;
                if (!hasTarget)
                {
                    currentState = AnimalAIState.Idle;
                    stateTimeRemaining = RandomDuration(settings.IdleDuration);
                }
                break;
        }
    }

    private bool TryChooseDrinkTarget(
        bool requireLoadedGround,
        out Vector3 result)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        Vector3 position = transform.position;
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
        Vector3 position = transform.position;
        if (!IsOutsideHerdArea(position))
        {
            herdReturnTargetActive = false;
            return false;
        }

        if (herdReturnRetryCooldown > 0f)
        {
            return false;
        }

        if (!hasTarget || IsOutsideHerdArea(targetPosition))
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
                    out targetPosition);
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
        Vector3 areaCenter = HerdAreaCenter;
        Vector3 areaOffset = position - areaCenter;
        areaOffset.y = 0f;
        if (areaOffset.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float inset = Mathf.Max(settings.ArrivalDistance * 2f, 0.5f);
        float returnRadius = Mathf.Max(0f, HerdAreaRadius - inset);
        Vector3 returnTarget = areaCenter + areaOffset.normalized * returnRadius;
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
        bool wasResting = currentState == AnimalAIState.Rest || waitingForStandUp;
        currentState = AnimalAIState.Flee;
        stateTimeRemaining = 0f;
        fleeThreatPosition = threatPosition;
        fleeThreatPosition.y = transform.position.y;
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
        if (!hasFleeThreat)
        {
            BeginNextBehavior(useLiveCollision);
            ApplyAnimation(0f);
            return;
        }

        Vector3 position = transform.position;
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
            awayDirection = awayFromThreat.normalized;
        }
        else
        {
            awayDirection = -transform.forward;
            awayDirection.y = 0f;
            if (awayDirection.sqrMagnitude <= 0.0001f)
            {
                float angle = Next01() * Mathf.PI * 2f;
                awayDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            else
            {
                awayDirection.Normalize();
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
            ? toMovementTarget.normalized
            : awayDirection;
        Vector3 crowdSteering = UpdateSmoothedSeparation(
            AnimalAIWorld.Instance,
            deltaTime) * settings.SeparationWeight;
        movementDirection += Vector3.ClampMagnitude(
            crowdSteering,
            MaximumCrowdSteering);
        if (movementDirection.sqrMagnitude > 0.0001f)
        {
            movementDirection.Normalize();
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
                    transform.position,
                    deltaTime))
            {
                TryRepathFlee(awayDirection, useLiveCollision);
            }
        }
        else if (IsNavigationProgressStalled(
                     movementTarget,
                     transform.position,
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
                Quaternion.Euler(0f, FleeTargetAngles[angleIndex], 0f) * awayDirection;
            Vector3 candidate =
                fleeThreatPosition + candidateDirection * targetDistance;
            candidate.y = transform.position.y;
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
        Vector3 position = transform.position;
        areaCenter = (position + destination) * 0.5f;
        areaCenter.y = position.y;
        Vector3 offset = destination - position;
        offset.y = 0f;
        areaRadius = Mathf.Min(
            MaxExtendedNavigationRadius,
            offset.magnitude * 0.5f + ExtendedNavigationMargin);
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

        Vector3 candidate = position + direction * moveDistance;
        candidate.y = position.y;
        ApplyMovement(candidate, direction, deltaTime);
        navigationBlockedTime = 0f;
        return MovementAvailability.Clear;
    }

    private void UpdateFleeTarget()
    {
        Vector3 direction = transform.position - fleeThreatPosition;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = -transform.forward;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude > 0.0001f)
        {
            UpdateFleeTarget(direction.normalized);
        }
        else
        {
            targetPosition = transform.position;
            hasTarget = false;
        }
    }

    private void UpdateFleeTarget(Vector3 direction)
    {
        targetPosition = fleeThreatPosition + direction * GetFleeTargetDistance();
        targetPosition.y = transform.position.y;
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
        float idle = settings.IdleWeight;
        float lookAround = settings.LookAroundWeight;
        float wander = settings.WanderWeight
                       * Mathf.Lerp(1f, settings.YoungWanderWeightMultiplier, youngFactor);
        float graze = settings.GrazeWeight;
        float drink = settings.DrinkWeight;
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
        out Vector3 result)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        Vector3 center = HerdAreaCenter;
        float radius = Mathf.Max(1f, HerdAreaRadius - settings.ArrivalDistance);
        bool originIsWalkable = terrain == null
                                || terrain.CanAnimalMoveTo(
                                    transform.position,
                                    requireLoadedGround);
        int pathSearchCount = 0;
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
            float angle = Next01() * Mathf.PI * 2f;
            float distance = Mathf.Sqrt(Next01()) * radius;
            Vector3 candidate = new Vector3(
                center.x + Mathf.Cos(angle) * distance,
                transform.position.y,
                center.z + Mathf.Sin(angle) * distance);

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
        result = transform.position;
        return false;
    }

    private bool TryBuildReachableFallbackPath(
        bool requireWaterEdge,
        bool requireLoadedGround,
        out Vector3 destination)
    {
        destination = transform.position;
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null)
        {
            return false;
        }

        navigationWaypoints ??= new Vector3[MaxNavigationWaypoints];
        int waypointCount = AnimalGridPathfinder.FindReachableTargetPath(
            terrain,
            transform.position,
            HerdAreaCenter,
            HerdAreaRadius,
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
        if (navigationPrepared && goalOffset.sqrMagnitude <= 0.0001f)
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
                   transform.position,
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
        if (currentState == AnimalAIState.Drink
            || IsOutsideHerdArea(transform.position))
        {
            GetExtendedNavigationArea(destination, out areaCenter, out areaRadius);
            return;
        }

        areaCenter = HerdAreaCenter;
        areaRadius = HerdAreaRadius;
    }

    private bool IsOutsideHerdArea(Vector3 position)
    {
        Vector3 areaOffset = position - HerdAreaCenter;
        areaOffset.y = 0f;
        float areaRadius = HerdAreaRadius;
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
            transform.position,
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
        navigationWaypointCount = waypointCount;
        navigationWaypointIndex = 0;
        navigationBlockedTime = 0f;
        return true;
    }

    private void PrepareDirectNavigation(Vector3 destination)
    {
        navigationGoal = destination;
        navigationPrepared = true;
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
        hasTarget = false;
        movingToActivity = false;
        currentState = AnimalAIState.Idle;
        stateTimeRemaining = RandomDuration(settings.IdleDuration);
        ResetNavigation();
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
        float distance = remaining.magnitude;
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
        if (animal != null && !animal.IsAlive)
        {
            StopForDeath();
            return false;
        }

        Vector3 position = transform.position;
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

        Vector3 desiredDirection = toTarget.normalized;
        Vector3 flockSteering = Vector3.zero;
        AnimalAIWorld world = escapingTerrain ? null : AnimalAIWorld.Instance;
        if (world != null)
        {
            if (currentState != AnimalAIState.Drink
                && world.TryGetHerdCenter(
                    HerdId,
                    out Vector3 currentHerdCenter))
            {
                Vector3 cohesion = currentHerdCenter - position;
                cohesion.y = 0f;
                if (cohesion.sqrMagnitude > 0.01f)
                {
                    flockSteering += cohesion.normalized * settings.CohesionWeight;
                }
            }

            flockSteering += UpdateSmoothedSeparation(world, deltaTime)
                             * settings.SeparationWeight;
        }
        else
        {
            UpdateSmoothedSeparation(null, deltaTime);
        }

        float areaRadius = HerdAreaRadius;
        bool restrictToHerdArea = currentState != AnimalAIState.Drink;
        bool startedOutsideHerdArea = false;
        if (!escapingTerrain && restrictToHerdArea)
        {
            Vector3 areaOffset = position - HerdAreaCenter;
            areaOffset.y = 0f;
            startedOutsideHerdArea = areaOffset.sqrMagnitude > areaRadius * areaRadius;
            if (startedOutsideHerdArea)
            {
                flockSteering += (-areaOffset.normalized) * 4f;
            }
        }

        Vector3 direction = navigationWaypointCount > 0
            ? desiredDirection + Vector3.ClampMagnitude(flockSteering, 0.35f)
            : desiredDirection + flockSteering;
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

        direction.Normalize();
        float speed = GetEffectiveMoveSpeed();
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

        Vector3 candidate = position + direction * (speed * deltaTime);
        candidate.y = position.y;
        if (!escapingTerrain
            && restrictToHerdArea
            && !startedOutsideHerdArea)
        {
            Vector3 clampedOffset = candidate - HerdAreaCenter;
            clampedOffset.y = 0f;
            if (clampedOffset.sqrMagnitude > areaRadius * areaRadius)
            {
                Vector3 clamped = HerdAreaCenter + clampedOffset.normalized * areaRadius;
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
                        ProbePhysicsPosition(
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
        transform.position = position;
        if (settings.TurnSpeed > 0f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                settings.TurnSpeed * Mathf.Min(deltaTime, MaximumRotationDeltaTime));
        }
    }

    private Vector3 UpdateSmoothedSeparation(
        AnimalAIWorld world,
        float deltaTime)
    {
        Vector3 target = world != null
            ? world.GetSeparation(this, settings.SeparationRadius)
            : Vector3.zero;
        float blend = 1f - Mathf.Exp(
            -SeparationSmoothingRate * Mathf.Max(0f, deltaTime));
        smoothedSeparation = Vector3.Lerp(
            smoothedSeparation,
            target,
            blend);
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
        MovementAvailability reverseAvailability = ProbeMovementDirection(
            origin,
            reverseDirection,
            moveDistance,
            useLiveCollision,
            allowTerrainEscape);
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

            Vector3 candidateDirection = Quaternion.Euler(
                0f,
                angle * turnSign,
                0f) * forward;
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

    private void CommitAvoidance(Vector3 direction, float turnSign)
    {
        committedAvoidanceDirection = direction.normalized;
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
        MovementAvailability positionAvailability = ProbePhysicsPosition(
            origin,
            candidate,
            radius);
        if (positionAvailability
            == MovementAvailability.BlockedByStaticObstacle)
        {
            return positionAvailability;
        }

        float probeDistance = Mathf.Max(moveDistance, settings.ObstacleProbeDistance);
        Vector3 probe = origin + direction * probeDistance;
        probe.y = origin.y;
        if (!IsPhysicsPathClearOrEscaping(origin, direction, probeDistance, radius)
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
        MovementAvailability positionAvailability = ProbePhysicsPosition(
            origin,
            candidate,
            radius);
        if (positionAvailability == MovementAvailability.BlockedByStaticObstacle
            || !IsPhysicsPathClearOrEscaping(
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
        simulationPosition = position;
        crowdSnapshotPosition = position;
        crowdSnapshotValid = true;
        navigationBlockedTime = 0f;
        ResetPresentation();
        transform.SetPositionAndRotation(simulationPosition, simulationRotation);
        animal?.MarkTerrainInteraction();
    }

    private bool IsPhysicsPathClearOrEscaping(
        Vector3 origin,
        Vector3 direction,
        float distance,
        float radius)
    {
        if (!gameObject.activeInHierarchy || distance <= 0f)
        {
            return true;
        }

        Vector3 normalizedDirection = direction;
        normalizedDirection.y = 0f;
        if (normalizedDirection.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        normalizedDirection.Normalize();
        Vector3 destination = origin + normalizedDirection * distance;
        destination.y = origin.y;
        Vector3 probeOrigin = origin + Vector3.up * radius;
        int hitCount = Physics.SphereCastNonAlloc(
            probeOrigin,
            radius,
            normalizedDirection,
            obstacleSweepBuffer,
            distance,
            obstacleLayerMask,
            QueryTriggerInteraction.Ignore);
        AnimalAIWorld.Instance?.ReportObstaclePhysicsProbe(hitCount);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = obstacleSweepBuffer[i].collider;
            obstacleSweepBuffer[i] = default;
            if (ShouldIgnoreObstacle(hit)
                || IsMovingOutOfOverlap(hit, origin, destination, radius))
            {
                continue;
            }

            return false;
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

    private MovementAvailability ProbePhysicsPosition(
        Vector3 origin,
        Vector3 position,
        float radius,
        bool allowEscape = true)
    {
        if (!gameObject.activeInHierarchy)
        {
            return MovementAvailability.Clear;
        }

        AnimalAIWorld world = AnimalAIWorld.Instance;
        int layerMask = Physics.AllLayers;
        bool blockedByAnimal = false;
        if (world != null && world.HasSpatialIndex)
        {
            blockedByAnimal = !world.IsAnimalPositionClearOrEscaping(
                this,
                origin,
                position,
                radius,
                allowEscape);

            layerMask = obstacleLayerMask;
        }

        Vector3 probePosition = position + Vector3.up * radius;
        int hitCount = Physics.OverlapSphereNonAlloc(
            probePosition,
            radius,
            obstacleBuffer,
            layerMask,
            QueryTriggerInteraction.Ignore);
        world?.ReportObstaclePhysicsProbe(hitCount);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = obstacleBuffer[i];
            obstacleBuffer[i] = null;
            if (ShouldIgnoreObstacle(hit))
            {
                continue;
            }

            if (allowEscape && IsMovingOutOfOverlap(hit, origin, position, radius))
            {
                continue;
            }

            return MovementAvailability.BlockedByStaticObstacle;
        }

        return blockedByAnimal
            ? MovementAvailability.BlockedByAnimal
            : MovementAvailability.Clear;
    }

    private bool ShouldIgnoreObstacle(Collider hit)
    {
        if (hit == null
            || hit.transform.IsChildOf(transform)
            || transform.IsChildOf(hit.transform)
            || mountedRider != null
               && (hit.transform.IsChildOf(mountedRider.transform)
                   || mountedRider.transform.IsChildOf(hit.transform)))
        {
            return true;
        }

        // 설치물은 Block의 자식이 아니라 Terrain 아래에 생성되고 Block은 참조만 보관한다.
        // 따라서 열린 문처럼 통과 가능한 설치물의 자식 Collider는 MapObject에서 먼저 판정한다.
        MapObject colliderMapObject = hit.GetComponentInParent<MapObject>();
        if (colliderMapObject != null)
        {
            return colliderMapObject.AllowsAnimalTraversal;
        }

        Block block = hit.GetComponentInParent<Block>();
        if (block == null)
        {
            return false;
        }

        MapObject mapObject = block.MapObject;
        return mapObject == null
               || mapObject.AllowsAnimalTraversal;
    }

    private static bool IsMovingOutOfOverlap(
        Collider obstacle,
        Vector3 origin,
        Vector3 candidate,
        float radius)
    {
        Vector3 originProbe = origin + Vector3.up * radius;
        Vector3 candidateProbe = candidate + Vector3.up * radius;
        Vector3 originClosest = obstacle.ClosestPoint(originProbe);
        Vector3 candidateClosest = obstacle.ClosestPoint(candidateProbe);
        float originDistanceSqr = (originClosest - originProbe).sqrMagnitude;
        float candidateDistanceSqr = (candidateClosest - candidateProbe).sqrMagnitude;
        float radiusSqr = radius * radius;

        if (originDistanceSqr >= radiusSqr)
        {
            return false;
        }

        if (candidateDistanceSqr > originDistanceSqr + 0.000001f)
        {
            return true;
        }

        // Collider 내부에서는 ClosestPoint가 입력 위치와 같아 거리 비교가 불가능하다.
        // 이 경우 Bounds 중심에서 멀어지는 방향만 임시로 허용해 탈출시킨다.
        if (originDistanceSqr <= 0.000001f && candidateDistanceSqr <= 0.000001f)
        {
            Vector3 outward = originProbe - obstacle.bounds.center;
            outward.y = 0f;
            Vector3 movement = candidateProbe - originProbe;
            movement.y = 0f;
            return outward.sqrMagnitude > 0.0001f
                   && Vector3.Dot(movement, outward) > 0f;
        }

        return false;
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

    private void ApplyAnimation(float speed, bool isRunning = false)
    {
        if (animal == null)
        {
            return;
        }

        float resolvedSpeed = Mathf.Max(0f, speed);
        bool performingActivity = !movingToActivity && !hasTarget;
        animal.SetAIAnimation(
            resolvedSpeed,
            performingActivity && currentState == AnimalAIState.Graze,
            performingActivity && currentState == AnimalAIState.Drink,
            performingActivity && currentState == AnimalAIState.Rest && IsNightTime(),
            performingActivity && currentState == AnimalAIState.LookAround,
            currentState == AnimalAIState.Flee && resolvedSpeed > 0.01f,
            isRunning);
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
            long id = terrainInstance != null ? terrainInstance.DeterministicId : GetInstanceID();
            uint value = (uint)id ^ (uint)(id >> 32) ^ 0x9E3779B9u;
            return value != 0u ? value : 0x6D2B79F5u;
        }
    }

    private static AnimalAIState ClampState(int value)
    {
        return value >= (int)AnimalAIState.Idle && value <= (int)AnimalAIState.Flee
            ? (AnimalAIState)value
            : AnimalAIState.Idle;
    }
}
