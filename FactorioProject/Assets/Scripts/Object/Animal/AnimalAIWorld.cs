using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AnimalAIWorld : MonoBehaviour
{
    private const float BackgroundStepInterval = 1f;
    private const float SpatialCellSize = 2f;
    private const float NearActiveDistance = 12f;
    private const float MidActiveDistance = 30f;
    private const float NearTickInterval = 1f / 30f;
    private const float MidTickInterval = 1f / 15f;
    private const float FarTickInterval = 1f / 8f;
    private const float DetailedVisualDistance = 8f;
    private const int NormalSimulationTickBudget = 24;
    private const int FleeSimulationTickBonus = 8;
    private const float CrowdOverlapTolerance = 0.0001f;

    private struct HerdFrame
    {
        public Vector3 positionSum;
        public int count;
    }

    private static readonly List<AnimalAIController> PendingControllers = new List<AnimalAIController>();

    private readonly List<AnimalAIController> controllers = new List<AnimalAIController>();
    private readonly HashSet<AnimalAIController> controllerLookup = new HashSet<AnimalAIController>();
    private readonly Dictionary<long, List<AnimalAIController>> controllersByHerd =
        new Dictionary<long, List<AnimalAIController>>();
    private readonly Dictionary<AnimalAIController, long> herdIdByController =
        new Dictionary<AnimalAIController, long>();
    private readonly Dictionary<long, HerdFrame> herdFrames = new Dictionary<long, HerdFrame>();
    private readonly Dictionary<Vector2Int, List<AnimalAIController>> controllersBySpatialCell =
        new Dictionary<Vector2Int, List<AnimalAIController>>();
    private readonly List<List<AnimalAIController>> activeSpatialBuckets =
        new List<List<AnimalAIController>>();
    private readonly Stack<List<AnimalAIController>> spatialBucketPool =
        new Stack<List<AnimalAIController>>();
    private readonly List<AnimalAIController> dueNormalControllers =
        new List<AnimalAIController>(128);
    private readonly List<AnimalAIController> dueFleeControllers =
        new List<AnimalAIController>(32);

    private float backgroundAccumulator;
    private float maximumAnimalColliderRadius = 0.5f;
    private bool paused;
    private bool spatialIndexReady;
    private int separationCandidateChecks;
    private int separationCandidateChecksLastFrame;
    private int animalCollisionCandidateChecks;
    private int animalCollisionCandidateChecksLastFrame;
    private int animalCollisionCellChecks;
    private int animalCollisionCellChecksLastFrame;
    private int obstaclePhysicsQueries;
    private int obstaclePhysicsQueriesLastFrame;
    private int obstaclePhysicsHits;
    private int obstaclePhysicsHitsLastFrame;
    private int activeSimulationTicks;
    private int activeSimulationTicksLastFrame;
    private int simulationTickCandidates;
    private int simulationTickCandidatesLastFrame;
    private int deferredSimulationTicks;
    private int deferredSimulationTicksLastFrame;
    private int simulationTickBudget;
    private int simulationTickBudgetLastFrame;
    private int normalSimulationCursor;
    private int fleeSimulationCursor;
    private int nearActiveControllers;
    private int midActiveControllers;
    private int farActiveControllers;

    public static AnimalAIWorld Instance { get; private set; }
    public bool Paused => paused;
    public int ControllerCount => controllers.Count;
    public int HerdGroupCount => controllersByHerd.Count;
    public int SeparationCandidateChecksLastFrame => separationCandidateChecksLastFrame;
    public int AnimalCollisionCandidateChecksLastFrame => animalCollisionCandidateChecksLastFrame;
    public int AnimalCollisionCellChecksLastFrame => animalCollisionCellChecksLastFrame;
    public float MaximumAnimalColliderRadius => maximumAnimalColliderRadius;
    public int ObstaclePhysicsQueriesLastFrame => obstaclePhysicsQueriesLastFrame;
    public int ObstaclePhysicsHitsLastFrame => obstaclePhysicsHitsLastFrame;
    public int ActiveSimulationTicksLastFrame => activeSimulationTicksLastFrame;
    public int SimulationTickCandidatesLastFrame => simulationTickCandidatesLastFrame;
    public int DeferredSimulationTicksLastFrame => deferredSimulationTicksLastFrame;
    public int SimulationTickBudgetLastFrame => simulationTickBudgetLastFrame;
    public int NearActiveControllerCount => nearActiveControllers;
    public int MidActiveControllerCount => midActiveControllers;
    public int FarActiveControllerCount => farActiveControllers;
    public bool HasSpatialIndex => spatialIndexReady;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        for (int i = 0; i < PendingControllers.Count; i++)
        {
            AddController(PendingControllers[i]);
        }

        PendingControllers.Clear();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        TickPresentations(deltaTime);
        RebuildFrameCaches();

        GameManager gameManager = GameManager.Instance;
        Transform playerTransform = gameManager != null && gameManager.Player != null
            ? gameManager.Player.transform
            : null;
        float activeRadius = gameManager != null ? gameManager.AnimalAIActiveRadius : 60f;
        float activeRadiusSqr = activeRadius * activeRadius;
        bool runBackgroundStep = false;
        dueNormalControllers.Clear();
        dueFleeControllers.Clear();
        nearActiveControllers = 0;
        midActiveControllers = 0;
        farActiveControllers = 0;

        if (!paused)
        {
            backgroundAccumulator += deltaTime;
            if (backgroundAccumulator >= BackgroundStepInterval)
            {
                runBackgroundStep = true;
                backgroundAccumulator = Mathf.Min(backgroundAccumulator, BackgroundStepInterval * 2f);
            }
        }

        for (int i = controllers.Count - 1; i >= 0; i--)
        {
            AnimalAIController controller = controllers[i];
            if (controller == null)
            {
                controllers.RemoveAt(i);
                if (!ReferenceEquals(controller, null))
                {
                    controllerLookup.Remove(controller);
                    RemoveHerdMembership(controller);
                }

                continue;
            }

            if (!paused)
            {
                controller.TickNeeds(deltaTime);
            }

            float playerDistanceSqr = playerTransform != null
                ? HorizontalSqrDistance(
                    controller.SimulationPosition,
                    playerTransform.position)
                : float.PositiveInfinity;
            bool active = !paused
                          && IsActiveController(controller)
                          && playerDistanceSqr <= activeRadiusSqr;
            controller.SetBehaviorExecutionActive(active);
            controller.SetDetailedVisuals(
                playerTransform == null
                || playerDistanceSqr <= DetailedVisualDistance * DetailedVisualDistance);

            if (active)
            {
                float tickInterval;
                if (controller.IsFleeing
                    || playerDistanceSqr <= NearActiveDistance * NearActiveDistance)
                {
                    nearActiveControllers++;
                    tickInterval = NearTickInterval;
                }
                else if (playerDistanceSqr <= MidActiveDistance * MidActiveDistance)
                {
                    midActiveControllers++;
                    tickInterval = MidTickInterval;
                }
                else
                {
                    farActiveControllers++;
                    tickInterval = FarTickInterval;
                }

                if (controller.QueueScheduledTick(deltaTime, tickInterval))
                {
                    if (controller.IsFleeing)
                    {
                        dueFleeControllers.Add(controller);
                    }
                    else
                    {
                        dueNormalControllers.Add(controller);
                    }
                }
            }
            else if (!paused && runBackgroundStep && controller.IsInteracted)
            {
                controller.TickBackground(BackgroundStepInterval);
            }
        }

        if (runBackgroundStep)
        {
            backgroundAccumulator -= BackgroundStepInterval;
        }

        RunScheduledTicks();
    }

    private void TickPresentations(float deltaTime)
    {
        for (int i = 0; i < controllers.Count; i++)
        {
            controllers[i]?.TickPresentation(deltaTime);
        }
    }

    private void LateUpdate()
    {
        CommitFrameCounter(
            ref separationCandidateChecks,
            ref separationCandidateChecksLastFrame);
        CommitFrameCounter(
            ref animalCollisionCandidateChecks,
            ref animalCollisionCandidateChecksLastFrame);
        CommitFrameCounter(
            ref animalCollisionCellChecks,
            ref animalCollisionCellChecksLastFrame);
        CommitFrameCounter(
            ref obstaclePhysicsQueries,
            ref obstaclePhysicsQueriesLastFrame);
        CommitFrameCounter(ref obstaclePhysicsHits, ref obstaclePhysicsHitsLastFrame);
        CommitFrameCounter(
            ref activeSimulationTicks,
            ref activeSimulationTicksLastFrame);
        CommitFrameCounter(
            ref simulationTickCandidates,
            ref simulationTickCandidatesLastFrame);
        CommitFrameCounter(
            ref deferredSimulationTicks,
            ref deferredSimulationTicksLastFrame);
        CommitFrameCounter(ref simulationTickBudget, ref simulationTickBudgetLastFrame);
    }

    private void RunScheduledTicks()
    {
        int fleeCount = dueFleeControllers.Count;
        int normalCount = dueNormalControllers.Count;
        simulationTickCandidates += fleeCount + normalCount;

        int totalBudget = NormalSimulationTickBudget
                          + Mathf.Min(FleeSimulationTickBonus, fleeCount);
        simulationTickBudget = totalBudget;
        int processed = RunScheduledTicks(
            dueFleeControllers,
            totalBudget,
            ref fleeSimulationCursor);
        processed += RunScheduledTicks(
            dueNormalControllers,
            Mathf.Min(NormalSimulationTickBudget, totalBudget - processed),
            ref normalSimulationCursor);

        activeSimulationTicks += processed;
        deferredSimulationTicks += fleeCount + normalCount - processed;
    }

    private static int RunScheduledTicks(
        List<AnimalAIController> candidates,
        int budget,
        ref int cursor)
    {
        int count = candidates.Count;
        if (count == 0 || budget <= 0)
        {
            if (count == 0)
            {
                cursor = 0;
            }

            return 0;
        }

        int start = cursor % count;
        int visited = 0;
        int processed = 0;
        while (visited < count && processed < budget)
        {
            AnimalAIController controller = candidates[(start + visited) % count];
            if (controller != null && controller.ExecuteScheduledTick())
            {
                processed++;
            }

            visited++;
        }

        cursor = (start + visited) % count;
        return processed;
    }

    public static AnimalAIWorld EnsureFor(GameObject owner)
    {
        if (Instance != null)
        {
            return Instance;
        }

        if (owner == null)
        {
            return null;
        }

        AnimalAIWorld world = owner.GetComponent<AnimalAIWorld>();
        return world != null ? world : owner.AddComponent<AnimalAIWorld>();
    }

    public static void Register(AnimalAIController controller)
    {
        if (controller == null)
        {
            return;
        }

        if (Instance != null)
        {
            Instance.AddController(controller);
        }
        else if (!PendingControllers.Contains(controller))
        {
            PendingControllers.Add(controller);
        }
    }

    public static void Unregister(AnimalAIController controller)
    {
        PendingControllers.Remove(controller);
        Instance?.RemoveController(controller);
    }

    public void SetPaused(bool value)
    {
        paused = value;
        if (paused)
        {
            for (int i = 0; i < controllers.Count; i++)
            {
                if (controllers[i] != null)
                {
                    controllers[i].SetBehaviorExecutionActive(false);
                }
            }
        }
    }

    public bool TryGetHerdCenter(long herdId, out Vector3 center)
    {
        if (herdId != 0L
            && herdFrames.TryGetValue(herdId, out HerdFrame frame)
            && frame.count > 0)
        {
            center = frame.positionSum / frame.count;
            return true;
        }

        center = Vector3.zero;
        return false;
    }

    public Vector3 GetSeparation(AnimalAIController source, float radius)
    {
        if (!spatialIndexReady || source == null || radius <= 0f)
        {
            return Vector3.zero;
        }

        float radiusSqr = radius * radius;
        Vector3 result = Vector3.zero;
        int contributingNeighborCount = 0;
        Vector3 sourcePosition = source.CrowdSnapshotPosition;
        int minimumX = Mathf.FloorToInt((sourcePosition.x - radius) / SpatialCellSize);
        int maximumX = Mathf.FloorToInt((sourcePosition.x + radius) / SpatialCellSize);
        int minimumZ = Mathf.FloorToInt((sourcePosition.z - radius) / SpatialCellSize);
        int maximumZ = Mathf.FloorToInt((sourcePosition.z + radius) / SpatialCellSize);
        for (int cellZ = minimumZ; cellZ <= maximumZ; cellZ++)
        {
            for (int cellX = minimumX; cellX <= maximumX; cellX++)
            {
                Vector2Int cell = new Vector2Int(cellX, cellZ);
                if (!controllersBySpatialCell.TryGetValue(
                        cell,
                        out List<AnimalAIController> occupants))
                {
                    continue;
                }

                for (int i = 0; i < occupants.Count; i++)
                {
                    separationCandidateChecks++;
                    AnimalAIController neighbor = occupants[i];
                    if (!IsActiveController(neighbor) || neighbor == source)
                    {
                        continue;
                    }

                    Vector3 offset = sourcePosition - neighbor.CrowdSnapshotPosition;
                    offset.y = 0f;
                    float distanceSqr = offset.sqrMagnitude;
                    if (distanceSqr >= radiusSqr)
                    {
                        continue;
                    }

                    contributingNeighborCount++;
                    if (distanceSqr <= 0.0001f)
                    {
                        offset = GetStableOverlapDirection(source, neighbor);
                        result += offset;
                        continue;
                    }

                    float distance = Mathf.Sqrt(distanceSqr);
                    result += offset / distance * (1f - distance / radius);
                }
            }
        }

        // A symmetric cluster can cancel every pairwise separation vector.
        // Give each animal a stable personal escape direction so the group can
        // break symmetry without frame-to-frame jitter or random allocations.
        if (contributingNeighborCount > 0 && result.sqrMagnitude <= 0.0001f)
        {
            result = GetStableCrowdDirection(source);
        }

        return Vector3.ClampMagnitude(result, 1f);
    }

    private static Vector3 GetStableCrowdDirection(AnimalAIController source)
    {
        long sourceId = source.TerrainInstance != null
            ? source.TerrainInstance.DeterministicId
            : source.GetHashCode();
        uint hash = unchecked((uint)((ulong)sourceId ^ ((ulong)sourceId >> 32)));
        return GetStableHorizontalDirection(hash);
    }

    private static Vector3 GetStableOverlapDirection(
        AnimalAIController source,
        AnimalAIController neighbor)
    {
        long sourceId = source.TerrainInstance != null
            ? source.TerrainInstance.DeterministicId
            : source.GetHashCode();
        long neighborId = neighbor.TerrainInstance != null
            ? neighbor.TerrainInstance.DeterministicId
            : neighbor.GetHashCode();
        bool invert = sourceId > neighborId;
        if (sourceId == neighborId)
        {
            int sourceHash = source.GetHashCode();
            int neighborHash = neighbor.GetHashCode();
            invert = sourceHash > neighborHash;
            sourceId = sourceHash;
            neighborId = neighborHash;
        }

        ulong first = unchecked((ulong)(invert ? neighborId : sourceId));
        ulong second = unchecked((ulong)(invert ? sourceId : neighborId));
        uint hash = unchecked((uint)(first ^ (first >> 32) ^ second ^ (second >> 32)));
        Vector3 direction = GetStableHorizontalDirection(hash);
        return invert ? -direction : direction;
    }

    private static Vector3 GetStableHorizontalDirection(uint hash)
    {
        hash ^= hash << 13;
        hash ^= hash >> 17;
        hash ^= hash << 5;
        float angle = (hash & 0xFFFFu) * (Mathf.PI * 2f / 65536f);
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    public bool IsAnimalPositionClearOrEscaping(
        AnimalAIController source,
        Vector3 origin,
        Vector3 candidate,
        float sourceRadius,
        bool allowEscape)
    {
        if (!spatialIndexReady || source == null)
        {
            return true;
        }

        float normalizedSourceRadius = Mathf.Max(0.01f, sourceRadius);
        float searchRadius = normalizedSourceRadius + maximumAnimalColliderRadius;
        int minimumX = Mathf.FloorToInt(
            (Mathf.Min(origin.x, candidate.x) - searchRadius) / SpatialCellSize);
        int maximumX = Mathf.FloorToInt(
            (Mathf.Max(origin.x, candidate.x) + searchRadius) / SpatialCellSize);
        int minimumZ = Mathf.FloorToInt(
            (Mathf.Min(origin.z, candidate.z) - searchRadius) / SpatialCellSize);
        int maximumZ = Mathf.FloorToInt(
            (Mathf.Max(origin.z, candidate.z) + searchRadius) / SpatialCellSize);
        float originOverlapDepth = 0f;
        float candidateOverlapDepth = 0f;

        for (int cellZ = minimumZ; cellZ <= maximumZ; cellZ++)
        {
            for (int cellX = minimumX; cellX <= maximumX; cellX++)
            {
                animalCollisionCellChecks++;
                Vector2Int cell = new Vector2Int(cellX, cellZ);
                if (!controllersBySpatialCell.TryGetValue(
                        cell,
                        out List<AnimalAIController> occupants))
                {
                    continue;
                }

                for (int i = 0; i < occupants.Count; i++)
                {
                    animalCollisionCandidateChecks++;
                    AnimalAIController neighbor = occupants[i];
                    if (!IsActiveController(neighbor) || neighbor == source)
                    {
                        continue;
                    }

                    Vector3 neighborPosition = neighbor.CrowdSnapshotPosition;
                    float combinedRadius = normalizedSourceRadius
                                           + neighbor.AvoidanceColliderRadius;
                    float combinedRadiusSqr = combinedRadius * combinedRadius;
                    float originX = origin.x - neighborPosition.x;
                    float originZ = origin.z - neighborPosition.z;
                    float originDistanceSqr = originX * originX + originZ * originZ;
                    if (originDistanceSqr < combinedRadiusSqr)
                    {
                        originOverlapDepth += combinedRadius
                                              - Mathf.Sqrt(Mathf.Max(0f, originDistanceSqr));
                    }

                    float candidateX = candidate.x - neighborPosition.x;
                    float candidateZ = candidate.z - neighborPosition.z;
                    float candidateDistanceSqr = candidateX * candidateX
                                                 + candidateZ * candidateZ;
                    if (candidateDistanceSqr < combinedRadiusSqr)
                    {
                        candidateOverlapDepth += combinedRadius
                                                 - Mathf.Sqrt(Mathf.Max(0f, candidateDistanceSqr));
                    }
                }
            }
        }

        if (candidateOverlapDepth <= 0f)
        {
            return true;
        }

        // In a packed group the first useful step is often tangential: it keeps
        // total overlap equal before later steps reduce it. Reject only movement
        // that materially worsens an existing overlap so animals cannot deadlock
        // on that flat part of the crowd-avoidance field.
        return allowEscape
               && originOverlapDepth > 0f
               && candidateOverlapDepth
               <= originOverlapDepth + CrowdOverlapTolerance;
    }

    public int PushAnimalsAlongPath(
        Vector3 start,
        Vector3 end,
        float playerRadius,
        float clearance)
    {
        return ProcessAnimalsAlongPath(
            start,
            end,
            playerRadius,
            clearance,
            true,
            int.MaxValue);
    }

    public int CountAnimalsAlongPath(
        Vector3 start,
        Vector3 end,
        float playerRadius,
        float clearance,
        int stopAfter)
    {
        return ProcessAnimalsAlongPath(
            start,
            end,
            playerRadius,
            clearance,
            false,
            Mathf.Max(1, stopAfter));
    }

    private int ProcessAnimalsAlongPath(
        Vector3 start,
        Vector3 end,
        float playerRadius,
        float clearance,
        bool pushAnimals,
        int stopAfter)
    {
        Vector3 movement = end - start;
        movement.y = 0f;
        if (movement.sqrMagnitude <= 0.000001f)
        {
            return 0;
        }

        float normalizedPlayerRadius = Mathf.Max(0.01f, playerRadius);
        float normalizedClearance = Mathf.Max(0f, clearance);
        if (!spatialIndexReady)
        {
            return ProcessAnimalsAlongPath(
                controllers,
                start,
                movement,
                normalizedPlayerRadius,
                normalizedClearance,
                pushAnimals,
                stopAfter);
        }

        float searchRadius = normalizedPlayerRadius
                             + maximumAnimalColliderRadius
                             + normalizedClearance;
        int minimumX = Mathf.FloorToInt(
            (Mathf.Min(start.x, end.x) - searchRadius) / SpatialCellSize);
        int maximumX = Mathf.FloorToInt(
            (Mathf.Max(start.x, end.x) + searchRadius) / SpatialCellSize);
        int minimumZ = Mathf.FloorToInt(
            (Mathf.Min(start.z, end.z) - searchRadius) / SpatialCellSize);
        int maximumZ = Mathf.FloorToInt(
            (Mathf.Max(start.z, end.z) + searchRadius) / SpatialCellSize);
        int processed = 0;
        for (int cellZ = minimumZ; cellZ <= maximumZ; cellZ++)
        {
            for (int cellX = minimumX; cellX <= maximumX; cellX++)
            {
                animalCollisionCellChecks++;
                Vector2Int cell = new Vector2Int(cellX, cellZ);
                if (!controllersBySpatialCell.TryGetValue(
                        cell,
                        out List<AnimalAIController> occupants))
                {
                    continue;
                }

                processed += ProcessAnimalsAlongPath(
                    occupants,
                    start,
                    movement,
                    normalizedPlayerRadius,
                    normalizedClearance,
                    pushAnimals,
                    stopAfter - processed);
                if (processed >= stopAfter)
                {
                    return processed;
                }
            }
        }

        return processed;
    }

    private int ProcessAnimalsAlongPath(
        List<AnimalAIController> candidates,
        Vector3 start,
        Vector3 movement,
        float playerRadius,
        float clearance,
        bool pushAnimals,
        int stopAfter)
    {
        float movementLengthSqr = movement.sqrMagnitude;
        int processed = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            animalCollisionCandidateChecks++;
            AnimalAIController controller = candidates[i];
            if (!IsActiveController(controller))
            {
                continue;
            }

            Vector3 animalPosition = controller.SimulationPosition;
            Vector3 fromStart = animalPosition - start;
            fromStart.y = 0f;
            float pathT = Mathf.Clamp01(
                Vector3.Dot(fromStart, movement) / movementLengthSqr);
            Vector3 closestPathPosition = start + movement * pathT;
            closestPathPosition.y = animalPosition.y;
            Vector3 offset = animalPosition - closestPathPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            float combinedRadius = playerRadius
                                   + controller.AvoidanceColliderRadius
                                   + clearance;
            float combinedRadiusSqr = combinedRadius * combinedRadius;
            if (distanceSqr >= combinedRadiusSqr)
            {
                continue;
            }

            if (!pushAnimals)
            {
                processed++;
                if (processed >= stopAfter)
                {
                    return processed;
                }

                continue;
            }

            float distance = Mathf.Sqrt(Mathf.Max(0f, distanceSqr));
            float pushDistance = combinedRadius - distance + 0.001f;
            if (controller.TryApplyPlayerPush(
                    closestPathPosition,
                    movement,
                    pushDistance))
            {
                processed++;
            }
        }

        return processed;
    }

    public void ReportObstaclePhysicsProbe(int hitCount)
    {
        obstaclePhysicsQueries++;
        obstaclePhysicsHits += Mathf.Max(0, hitCount);
    }

    public int ForceThreatPulse(Vector3 center, float radius)
    {
        return NotifyThreatInternal(center, center, radius, true);
    }

    public int NotifyThreat(
        Vector3 threatPosition,
        Vector3 affectedCenter,
        float affectedRadius)
    {
        return NotifyThreatInternal(
            threatPosition,
            affectedCenter,
            affectedRadius,
            false);
    }

    private int NotifyThreatInternal(
        Vector3 threatPosition,
        Vector3 affectedCenter,
        float affectedRadius,
        bool forced)
    {
        float radius = Mathf.Max(0f, affectedRadius);
        float radiusSqr = radius * radius;
        int notified = 0;
        for (int i = 0; i < controllers.Count; i++)
        {
            AnimalAIController controller = controllers[i];
            if (controller == null
                || !controller.IsConfigured
                || HorizontalSqrDistance(controller.SimulationPosition, affectedCenter) > radiusSqr)
            {
                continue;
            }

            if (forced)
            {
                controller.NotifyForcedThreat(threatPosition);
            }
            else
            {
                controller.NotifyThreat(threatPosition);
            }

            notified++;
        }

        return notified;
    }

    public int CountActiveControllers()
    {
        int count = 0;
        for (int i = 0; i < controllers.Count; i++)
        {
            if (controllers[i] != null && controllers[i].IsExecuting)
            {
                count++;
            }
        }

        return count;
    }

    public void CopyControllers(List<AnimalAIController> destination, bool activeOnly)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        for (int i = 0; i < controllers.Count; i++)
        {
            AnimalAIController controller = controllers[i];
            if (controller == null
                || (activeOnly
                    && !IsActiveController(controller)))
            {
                continue;
            }

            destination.Add(controller);
        }
    }

    public bool TryGetControllerByDeterministicId(
        long deterministicId,
        out AnimalAIController controller)
    {
        controller = null;
        if (deterministicId == 0L)
        {
            return false;
        }

        // 같은 프레임에 맵을 다시 만들면 파괴 예약된 이전 뷰가 목록에 잠시 남는다.
        // 새로 등록된 컨트롤러를 우선해 복원 대상이 이전 뷰에 연결되지 않게 한다.
        for (int i = controllers.Count - 1; i >= 0; i--)
        {
            AnimalAIController candidate = controllers[i];
            TerrainAnimalInstance instance = candidate != null
                ? candidate.TerrainInstance
                : null;
            if (candidate != null
                && candidate.gameObject.activeInHierarchy
                && candidate.IsConfigured
                && candidate.Animal != null
                && candidate.Animal.IsAlive
                && instance != null
                && instance.DeterministicId == deterministicId)
            {
                controller = candidate;
                return true;
            }
        }

        return false;
    }

    private void AddController(AnimalAIController controller)
    {
        if (controller == null)
        {
            return;
        }

        if (controllerLookup.Add(controller))
        {
            controllers.Add(controller);
        }

        RefreshHerdMembership(controller);
    }

    private void RemoveController(AnimalAIController controller)
    {
        if (ReferenceEquals(controller, null))
        {
            return;
        }

        if (controllerLookup.Remove(controller))
        {
            controllers.Remove(controller);
        }

        RemoveHerdMembership(controller);
    }

    private void RefreshHerdMembership(AnimalAIController controller)
    {
        if (controller == null || !controller.IsConfigured)
        {
            RemoveHerdMembership(controller);
            return;
        }

        long herdId = controller.HerdId;
        if (herdIdByController.TryGetValue(controller, out long previousHerdId))
        {
            if (previousHerdId == herdId)
            {
                return;
            }

            RemoveHerdMembership(controller, previousHerdId);
        }

        if (!controllersByHerd.TryGetValue(herdId, out List<AnimalAIController> herdMembers))
        {
            herdMembers = new List<AnimalAIController>(4);
            controllersByHerd.Add(herdId, herdMembers);
        }

        herdMembers.Add(controller);
        herdIdByController[controller] = herdId;
    }

    private void RemoveHerdMembership(AnimalAIController controller)
    {
        if (ReferenceEquals(controller, null)
            || !herdIdByController.TryGetValue(controller, out long herdId))
        {
            return;
        }

        RemoveHerdMembership(controller, herdId);
    }

    private void RemoveHerdMembership(AnimalAIController controller, long herdId)
    {
        herdIdByController.Remove(controller);
        if (!controllersByHerd.TryGetValue(herdId, out List<AnimalAIController> herdMembers))
        {
            return;
        }

        herdMembers.Remove(controller);
        if (herdMembers.Count == 0)
        {
            controllersByHerd.Remove(herdId);
        }
    }

    private void RebuildFrameCaches()
    {
        herdFrames.Clear();
        RecycleSpatialBuckets();
        maximumAnimalColliderRadius = 0.5f;
        for (int i = 0; i < controllers.Count; i++)
        {
            AnimalAIController controller = controllers[i];
            if (!IsActiveController(controller))
            {
                continue;
            }

            controller.CaptureCrowdSnapshot();
        }

        for (int i = 0; i < controllers.Count; i++)
        {
            AnimalAIController controller = controllers[i];
            if (!IsActiveController(controller))
            {
                continue;
            }

            AddToSpatialIndex(controller);
            maximumAnimalColliderRadius = Mathf.Max(
                maximumAnimalColliderRadius,
                controller.AvoidanceColliderRadius);
            if (controller.IsFleeing)
            {
                continue;
            }

            long herdId = controller.HerdId;
            if (!herdFrames.TryGetValue(herdId, out HerdFrame frame))
            {
                frame = default;
            }

            frame.positionSum += controller.CrowdSnapshotPosition;
            frame.count++;
            herdFrames[herdId] = frame;
        }

        spatialIndexReady = true;
    }

    private void RecycleSpatialBuckets()
    {
        controllersBySpatialCell.Clear();
        for (int i = 0; i < activeSpatialBuckets.Count; i++)
        {
            List<AnimalAIController> bucket = activeSpatialBuckets[i];
            bucket.Clear();
            spatialBucketPool.Push(bucket);
        }

        activeSpatialBuckets.Clear();
    }

    private void AddToSpatialIndex(AnimalAIController controller)
    {
        Vector3 position = controller.CrowdSnapshotPosition;
        Vector2Int cell = new Vector2Int(
            Mathf.FloorToInt(position.x / SpatialCellSize),
            Mathf.FloorToInt(position.z / SpatialCellSize));
        if (!controllersBySpatialCell.TryGetValue(
                cell,
                out List<AnimalAIController> bucket))
        {
            bucket = spatialBucketPool.Count > 0
                ? spatialBucketPool.Pop()
                : new List<AnimalAIController>(4);
            controllersBySpatialCell.Add(cell, bucket);
            activeSpatialBuckets.Add(bucket);
        }

        bucket.Add(controller);
    }

    private static float HorizontalSqrDistance(Vector3 left, Vector3 right)
    {
        float x = left.x - right.x;
        float z = left.z - right.z;
        return x * x + z * z;
    }

    private static bool IsActiveController(AnimalAIController controller)
    {
        return controller != null
               && controller.IsConfigured
               && controller.gameObject.activeInHierarchy;
    }

    private static void CommitFrameCounter(ref int current, ref int previous)
    {
        previous = current;
        current = 0;
    }
}
