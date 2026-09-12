using System.Collections.Generic;
using UnityEngine;
using ProjectF.Animals;
using ProjectF.Rendering;

[DisallowMultipleComponent]
public sealed partial class AnimalAIWorld : MonoBehaviour, IMapObjectUpdateTick, IMapObjectSimulationIdentity
{
    private const float SpatialCellSize = 2f;
    private const float NearActiveDistance = 12f;
    private const float MidActiveDistance = 30f;
    private const float NearTickInterval = 1f / 30f;
    private const float MidTickInterval = 1f / 15f;
    private const float FarTickInterval = 8f / 60f;
    private const float DetailedVisualDistance = 8f;
    private const int NormalSimulationTickBudget = 24;
    private const int FleeSimulationTickBonus = 8;
    private const int PathWorkBudgetPerTick = 2048;
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
    private readonly Stack<List<AnimalAIController>> spatialBucketPool =
        new Stack<List<AnimalAIController>>();
    private readonly List<AnimalAIController> dueNormalControllers =
        new List<AnimalAIController>(128);
    private readonly List<AnimalAIController> dueFleeControllers =
        new List<AnimalAIController>(32);

    private long needsTick;
    private long pathBudgetStartWork;
    private readonly CameraRenderCulling presentationCulling = new CameraRenderCulling();
    private Camera presentationCamera;
    public long NeedsTick => needsTick;
    private float maximumAnimalColliderRadius = 0.5f;
    private bool paused;
    private bool spatialIndexReady;
    private bool controllerOrderDirty;
    private int separationCandidateChecks;
    private int separationCandidateChecksLastFrame;
    private int animalCollisionCandidateChecks;
    private int animalCollisionCandidateChecksLastFrame;
    private int animalCollisionCellChecks;
    private int animalCollisionCellChecksLastFrame;
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
    public int ActiveSimulationTicksLastFrame => activeSimulationTicksLastFrame;
    public int SimulationTickCandidatesLastFrame => simulationTickCandidatesLastFrame;
    public int DeferredSimulationTicksLastFrame => deferredSimulationTicksLastFrame;
    public int SimulationTickBudgetLastFrame => simulationTickBudgetLastFrame;
    public int NearActiveControllerCount => nearActiveControllers;
    public int MidActiveControllerCount => midActiveControllers;
    public int FarActiveControllerCount => farActiveControllers;
    public bool HasSpatialIndex => spatialIndexReady;
    public long SimulationId => long.MinValue + 2L;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        AnimalAIProfiler.Reset();
        AnimalGridPathfinder.ClearRegionCache();
        for (int i = 0; i < PendingControllers.Count; i++)
        {
            AddController(PendingControllers[i]);
        }

        PendingControllers.Clear();
        MapObjectTickManager.RegisterUpdateTick(this);
    }

    private void OnDestroy()
    {
        MapObjectTickManager.UnregisterUpdateTick(this);
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!MapObjectTickManager.SimulationPaused && !MapObjectTickManager.WaitingForWorldLoad)
            TickPresentations(Time.deltaTime);
    }

    public void ManagedUpdateTick(float deltaTime)
    {
        if (controllerOrderDirty)
        {
            controllers.Sort(CompareControllers);
            controllerOrderDirty = false;
        }

        RefreshSpatialCaches();
        if (paused) return;
        needsTick += DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime);

        CollectScheduledTicks(deltaTime);
        pathBudgetStartWork = AnimalGridPathfinder.SchedulingWorkCount;
        RunScheduledTicks();
    }

    private void CollectScheduledTicks(float deltaTime)
    {
        using var sample = AnimalAIProfiler.Sample("Animal Scheduling");
        GameManager gameManager = GameManager.Instance;
        Transform playerTransform = gameManager != null && gameManager.Player != null
            ? gameManager.Player.transform
            : null;
        float activeRadius = gameManager != null ? gameManager.AnimalAIActiveRadius : 60f;
        float activeRadiusSqr = activeRadius * activeRadius;
        dueNormalControllers.Clear();
        dueFleeControllers.Clear();
        nearActiveControllers = 0;
        midActiveControllers = 0;
        farActiveControllers = 0;

        for (int i = 0; i < controllers.Count; i++)
        {
            AnimalAIController controller = controllers[i];
            if (controller == null)
            {
                controllers.RemoveAt(i--);
                if (!ReferenceEquals(controller, null))
                {
                    controllerLookup.Remove(controller);
                    RemoveHerdMembership(controller);
                    MarkSpatialDirty(controller);
                }

                continue;
            }

            float playerDistanceSqr = playerTransform != null
                ? HorizontalSqrDistance(
                    controller.SimulationPosition,
                    playerTransform.position)
                : float.PositiveInfinity;
            bool active = IsActiveController(controller)
                          && playerDistanceSqr <= activeRadiusSqr;
            controller.SetBehaviorExecutionActive(active);
            controller.SetDetailedVisuals(
                playerTransform == null
                || playerDistanceSqr <= DetailedVisualDistance * DetailedVisualDistance);

            bool needsUpdated = controller.TickScheduledNeeds(
                needsTick, !active || playerDistanceSqr > MidActiveDistance * MidActiveDistance);

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
            else if (needsUpdated)
            {
                // Needs timers advance above, but dormant animals must not enter
                // pathfinding, collision avoidance, or behavior simulation.
                controller.TickDormant();
            }
        }
    }

    private void TickPresentations(float deltaTime)
    {
        using var sample = MapObjectTickProfiler.SampleNamed("AI Render", "AnimalAI", "Animal Presentation");
        if (presentationCamera == null || !presentationCamera.isActiveAndEnabled)
            presentationCamera = Camera.main;
        presentationCulling.Update(presentationCamera);
        for (int i = 0; i < controllers.Count; i++)
        {
            AnimalAIController controller = controllers[i];
            if (controller == null || !controller.HasPendingPresentation) continue;
            bool visible = controller.IsPresentationVisible(presentationCulling);
            controller.TickCulledPresentation(deltaTime, visible);
            AnimalAIProfiler.Add(visible ? AnimalAIProfiler.Counter.Presentations
                : AnimalAIProfiler.Counter.CulledPresentations);
        }
    }

    private void LateUpdate()
    {
        AnimalAIProfiler.CompleteFrame();
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
        int scheduledWork = (int)(AnimalGridPathfinder.SchedulingWorkCount - pathBudgetStartWork);
        AnimalAIProfiler.Max(AnimalAIProfiler.Counter.PathBudgetMaxWorkPerTick, scheduledWork);
        if (scheduledWork > PathWorkBudgetPerTick)
            AnimalAIProfiler.Add(AnimalAIProfiler.Counter.PathBudgetOverruns);
    }

    private int RunScheduledTicks(
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
            // Finish the current animal's query/decision atomically. Never turn budget
            // exhaustion into "no path" or consume random numbers in a discarded retry.
            if (AnimalGridPathfinder.SchedulingWorkCount - pathBudgetStartWork >= PathWorkBudgetPerTick)
            {
                AnimalAIProfiler.Add(AnimalAIProfiler.Counter.PathBudgetDeferrals, count - visited);
                break;
            }
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

    public static void NotifySimulationIdentityChanged(AnimalAIController controller)
    {
        if (controller != null && Instance != null && Instance.controllerLookup.Contains(controller))
        {
            Instance.controllerOrderDirty = true;
            Instance.MarkSpatialDirty(controller);
        }
    }

    public void SetPaused(bool value)
    {
        paused = value;
        if (paused)
        {
            nearActiveControllers = midActiveControllers = farActiveControllers = 0;
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

    public void CopyOccupiedCoordinates(
        Vector2Int minCoordinate,
        Vector2Int maxCoordinate,
        HashSet<Vector2Int> destination)
    {
        if (destination == null)
        {
            return;
        }

        if (!spatialIndexReady)
        {
            for (int i = 0; i < controllers.Count; i++)
            {
                AddOccupiedCoordinate(
                    controllers[i],
                    minCoordinate,
                    maxCoordinate,
                    destination);
            }

            return;
        }

        int minCellX = Mathf.FloorToInt((minCoordinate.x - SpatialCellSize) / SpatialCellSize);
        int maxCellX = Mathf.FloorToInt((maxCoordinate.x + SpatialCellSize) / SpatialCellSize);
        int minCellY = Mathf.FloorToInt((minCoordinate.y - SpatialCellSize) / SpatialCellSize);
        int maxCellY = Mathf.FloorToInt((maxCoordinate.y + SpatialCellSize) / SpatialCellSize);
        for (int cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (int cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                if (!controllersBySpatialCell.TryGetValue(
                        new Vector2Int(cellX, cellY),
                        out List<AnimalAIController> bucket))
                {
                    continue;
                }

                for (int i = 0; i < bucket.Count; i++)
                {
                    AddOccupiedCoordinate(
                        bucket[i],
                        minCoordinate,
                        maxCoordinate,
                        destination);
                }
            }
        }
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

                    float distance = AnimalSimulationMath.Magnitude(offset);
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
        long sourceId = source.SimulationId;
        uint hash = unchecked((uint)((ulong)sourceId ^ ((ulong)sourceId >> 32)));
        return GetStableHorizontalDirection(hash);
    }

    private static Vector3 GetStableOverlapDirection(
        AnimalAIController source,
        AnimalAIController neighbor)
    {
        long sourceId = source.SimulationId;
        long neighborId = neighbor.SimulationId;
        bool invert = sourceId > neighborId;
        if (sourceId == neighborId)
        {
            Vector3 sourcePosition = source.SimulationPosition;
            Vector3 neighborPosition = neighbor.SimulationPosition;
            invert = sourcePosition.x > neighborPosition.x
                     || (Mathf.Approximately(sourcePosition.x, neighborPosition.x)
                         && sourcePosition.z > neighborPosition.z);
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
        return AnimalSimulationMath.Direction((int)(hash & 0xFFFFu));
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

        // Explicit removals unregister before destroying views. Visibility never
        // determines whether a simulation identity can be restored.
        for (int i = controllers.Count - 1; i >= 0; i--)
        {
            AnimalAIController candidate = controllers[i];
            TerrainAnimalInstance instance = candidate != null
                ? candidate.TerrainInstance
                : null;
            if (candidate != null
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
            controllerOrderDirty = true;
            controller.InitializeNeedsSchedule(needsTick);
        }

        RefreshHerdMembership(controller);
        MarkSpatialDirty(controller);
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
        MarkSpatialDirty(controller);
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

        int insert = herdMembers.BinarySearch(controller, ControllerComparer.Instance);
        herdMembers.Insert(insert < 0 ? ~insert : insert, controller);
        herdIdByController[controller] = herdId;
        dirtyHerds.Add(herdId);
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
        dirtyHerds.Add(herdId);
        if (herdMembers.Count == 0)
        {
            controllersByHerd.Remove(herdId);
        }
    }

    private static float HorizontalSqrDistance(Vector3 left, Vector3 right)
    {
        float x = left.x - right.x;
        float z = left.z - right.z;
        return x * x + z * z;
    }

    private static int CompareControllers(AnimalAIController left, AnimalAIController right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        return left.SimulationId.CompareTo(right.SimulationId);
    }

    private static void AddOccupiedCoordinate(
        AnimalAIController controller,
        Vector2Int minCoordinate,
        Vector2Int maxCoordinate,
        HashSet<Vector2Int> destination)
    {
        if (!IsActiveController(controller) || controller.TerrainInstance == null)
        {
            return;
        }

        Vector3 position = controller.SimulationPosition;
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(position.x),
            Mathf.RoundToInt(position.z));
        if (coordinate.x >= minCoordinate.x
            && coordinate.x <= maxCoordinate.x
            && coordinate.y >= minCoordinate.y
            && coordinate.y <= maxCoordinate.y)
        {
            destination.Add(coordinate);
        }
    }

    private static bool IsActiveController(AnimalAIController controller)
    {
        return controller != null
               && controller.IsConfigured;
    }

    private static void CommitFrameCounter(ref int current, ref int previous)
    {
        previous = current;
        current = 0;
    }
}
