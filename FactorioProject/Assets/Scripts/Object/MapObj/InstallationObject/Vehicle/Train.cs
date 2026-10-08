using System.Collections.Generic;
using ProjectF.Railway;
using UnityEngine;

public class Train : Vehicle
{
    // Grid cells are one world unit. Installation and movement share this pitch.
    public const float ConnectionCenterDistance = 1f;
    private const float MinConnectionDistance = 0.05f;
    private const float DefaultConnectionFallbackDistance = 1.4f;

    [SerializeField, Min(0.01f)]
    private float trainConnectionSnapMaxDistance = 0.6f;
    [SerializeField, Min(0.01f)]
    private float trainConnectionMaxLateralDistance = 0.45f;
    [SerializeField, Range(0f, 1f)]
    private float trainConnectionMinForwardDot = 0.5f;
    private Rigidbody cachedTrainRigidbody;
    private TrainInstance runtimeTrain;
    private readonly Queue<Train> connectionActionGroupQueue = new Queue<Train>();
    private readonly HashSet<Train> connectionActionGroupVisited = new HashSet<Train>();
    private readonly List<TrainInstance> connectionCandidateScratch = new List<TrainInstance>();

    internal TrainInstance RuntimeTrain
    {
        get
        {
            if (runtimeTrain == null || !runtimeTrain.IsAlive)
                runtimeTrain = TrainWorld.Shared.Create();
            return runtimeTrain;
        }
    }

    protected override ref ProjectF.Simulation.VehicleMotionState MotionState
    {
        get
        {
            // Pool release runs before OnDisable. Cleanup may reset the released
            // record, but must not allocate a new entity for a parked pooled view.
            if (runtimeTrain == null) runtimeTrain = TrainWorld.Shared.Create();
            return ref runtimeTrain.Motion;
        }
    }

    public float ConnectionSnapMaxDistance => Mathf.Max(MinConnectionDistance, trainConnectionSnapMaxDistance);
    public float ConnectionMaxLateralDistance => Mathf.Max(MinConnectionDistance, trainConnectionMaxLateralDistance);
    public float ConnectionMinForwardDot => Mathf.Clamp01(trainConnectionMinForwardDot);
    public NativeTrainConnections ConnectedTrains => new NativeTrainConnections(runtimeTrain);
    public static ulong ConnectionGraphRevision => TrainWorld.Shared.ConnectionGraphRevision;

    public bool IsConsistMoving(float speedThreshold = 0.0001f)
    {
        float normalizedThreshold = Mathf.Max(0f, speedThreshold);
        connectionActionGroupQueue.Clear();
        connectionActionGroupVisited.Clear();
        connectionActionGroupQueue.Enqueue(this);
        connectionActionGroupVisited.Add(this);

        bool isMoving = false;
        while (connectionActionGroupQueue.Count > 0)
        {
            Train current = connectionActionGroupQueue.Dequeue();
            if (current != null && current.CurrentVehicleSpeed > normalizedThreshold)
            {
                isMoving = true;
                break;
            }

            if (current == null)
            {
                continue;
            }

            foreach (Train connectedTrain in current.ConnectedTrains)
            {
                if (connectedTrain == null
                    || !connectedTrain.gameObject.activeInHierarchy
                    || !connectionActionGroupVisited.Add(connectedTrain))
                {
                    continue;
                }

                connectionActionGroupQueue.Enqueue(connectedTrain);
            }
        }

        connectionActionGroupQueue.Clear();
        connectionActionGroupVisited.Clear();
        return isMoving;
    }

    internal bool TryGetConsistSteamTrain(out SteamTrain steamTrain)
    {
        steamTrain = null;
        connectionActionGroupQueue.Clear();
        connectionActionGroupVisited.Clear();
        connectionActionGroupQueue.Enqueue(this);
        connectionActionGroupVisited.Add(this);

        while (connectionActionGroupQueue.Count > 0)
        {
            Train current = connectionActionGroupQueue.Dequeue();
            if (current is SteamTrain candidate
                && candidate != null
                && candidate.gameObject.activeInHierarchy)
            {
                steamTrain = candidate;
                break;
            }

            if (current == null)
            {
                continue;
            }

            foreach (Train connectedTrain in current.ConnectedTrains)
            {
                if (connectedTrain == null
                    || !connectedTrain.gameObject.activeInHierarchy
                    || !connectionActionGroupVisited.Add(connectedTrain))
                {
                    continue;
                }

                connectionActionGroupQueue.Enqueue(connectedTrain);
            }
        }

        connectionActionGroupQueue.Clear();
        connectionActionGroupVisited.Clear();
        return steamTrain != null;
    }

    public void RotateTrainWheelsByDistance(float signedDistance)
    {
        RotateWheelsByDistance(signedDistance);
    }

    public static void CollectActiveRuntimeTrains(ICollection<Train> results)
    {
        TrainWorld.Shared.CollectActiveViews(results);
    }

    public bool TryGetTouchingUnconnectedTrain(out Train target)
    {
        target = null;
        if (!gameObject.activeInHierarchy || !TryGetPlacementRuntime(out _, out _))
        {
            return false;
        }

        CollectConnectionActionGroup();
        float nearestDistanceSqr = float.MaxValue;
        var world = TrainWorld.Shared;
        world.IncludeConnectionRange(ConnectionSnapMaxDistance, ConnectionMaxLateralDistance);
        Vector3 position = transform.position;
        world.CollectNearby(new Vector2(position.x, position.z),
            ConnectionCenterDistance + world.MaxConnectionSnapDistance + world.MaxConnectionLateralDistance,
            connectionCandidateScratch);
        foreach (TrainInstance instance in connectionCandidateScratch)
        {
            if (!world.TryGetView(instance, out Train candidate)
                || connectionActionGroupVisited.Contains(candidate)
                || !CanConnectTo(candidate))
            {
                continue;
            }

            Vector3 offset = candidate.transform.position - transform.position;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            target = candidate;
            nearestDistanceSqr = distanceSqr;
        }

        connectionActionGroupQueue.Clear();
        connectionActionGroupVisited.Clear();
        connectionCandidateScratch.Clear();
        return target != null;
    }

    public bool TryConnectTouchingTrain()
    {
        return TryGetTouchingUnconnectedTrain(out Train target) && ConnectTo(target);
    }

    public bool TryGetConnectedTrain(out Train target)
    {
        target = null;
        float nearestDistanceSqr = float.MaxValue;
        foreach (Train candidate in ConnectedTrains)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 offset = candidate.transform.position - transform.position;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            target = candidate;
            nearestDistanceSqr = distanceSqr;
        }

        return target != null;
    }

    public virtual bool BlocksManualDisconnection => false;

    public bool TryDisconnectConnectedTrain()
    {
        if (BlocksManualDisconnection
            || !TryGetConnectedTrain(out Train target))
        {
            return false;
        }

        DisconnectFrom(target);
        return true;
    }

    private void CollectConnectionActionGroup()
    {
        connectionActionGroupQueue.Clear();
        connectionActionGroupVisited.Clear();
        connectionActionGroupQueue.Enqueue(this);
        connectionActionGroupVisited.Add(this);

        while (connectionActionGroupQueue.Count > 0)
        {
            Train current = connectionActionGroupQueue.Dequeue();
            foreach (Train connectedTrain in current.ConnectedTrains)
            {
                if (connectedTrain == null
                    || !connectedTrain.gameObject.activeInHierarchy
                    || !connectionActionGroupVisited.Add(connectedTrain))
                {
                    continue;
                }

                connectionActionGroupQueue.Enqueue(connectedTrain);
            }
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SynchronizeTrainPlacement();
        TrainWorld.Shared.AttachView(RuntimeTrain, this);
    }

    protected override void OnDisable()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;

        ClearTrainConnections();
        if (runtimeTrain != null)
        {
            TrainWorld.Shared.SetActive(runtimeTrain, false);
            TrainWorld.Shared.DetachView(runtimeTrain);
        }
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        base.PrepareForPool();
        TrainWorld.Shared.Release(runtimeTrain);
    }

    private void OnDestroy()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;
        TrainWorld.Shared.Release(runtimeTrain);
        runtimeTrain = null;
    }

    protected override void OnRuntimeMapObjectHandleChanged()
    {
        if (RuntimeMapObjectHandle.IsValid) RuntimeTrain.Handle = RuntimeMapObjectHandle;
        else if (runtimeTrain != null) runtimeTrain.Handle = default;
        base.OnRuntimeMapObjectHandleChanged();
    }

    protected override void OnPlacementRuntimeChanged()
    {
        SynchronizeTrainPlacement();
        base.OnPlacementRuntimeChanged();
    }

    private void SynchronizeTrainPlacement()
    {
        TrainInstance state = RuntimeTrain;
        state.Handle = RuntimeMapObjectHandle;
        TrainWorld.Shared.IncludeConnectionRange(ConnectionSnapMaxDistance, ConnectionMaxLateralDistance);
        TrainWorld.Shared.SetWorldPose(state, transform.position, transform.rotation);
        TrainWorld.Shared.SetPlacement(state, TryGetPlacementRuntime(out _, out _), RuntimePlacementSequence);
    }

    protected override void OnPlacementRuntimeCleared()
    {
        ClearPlacedRailSample();
        TrainWorld.Shared.SetPlacement(runtimeTrain, false, 0);
        if (runtimeTrain != null) runtimeTrain.Handle = default;
        base.OnPlacementRuntimeCleared();
    }

    public void ClearPlacedRailSample()
    {
        ClearTrainConnections();
        ClearCurrentRailSample();
    }

    private void ClearCurrentRailSample()
    {
        runtimeTrain?.ClearRailSample();
    }

    public bool ConnectTo(Train other)
    {
        if (!CanConnectTo(other))
        {
            return false;
        }

        TryGetConnectionPose(this, out Vector2 point, out Vector2 facing);
        TryGetConnectionPose(other, out Vector2 otherPoint, out Vector2 otherFacing);
        return TrainWorld.Shared.Connect(RuntimeTrain, other.RuntimeTrain,
            Vector2.Dot(otherPoint - point, facing) > 0f,
            Vector2.Dot(point - otherPoint, otherFacing) > 0f);
    }

    internal void SetConnectionEnd(Train other, bool atFront)
    {
        if (other != null) TrainWorld.Shared.SetConnectionEnd(runtimeTrain, other.runtimeTrain, atFront);
    }

    internal bool TryGetConnectionFacingSign(Train other, bool otherIsAheadOnPath, out float sign)
    {
        sign = 1f;
        return other != null && runtimeTrain != null
            && runtimeTrain.TryGetConnectionFacingSign(other.runtimeTrain, otherIsAheadOnPath, out sign);
    }

    internal static Vector2 ResolveRailConnectionForward(
        ProjectF.Railway.IRailTarget sourceRail,
        float sourceDistance,
        ProjectF.Railway.IRailTarget targetRail,
        float targetDistance,
        float progress,
        Vector2 referenceForward)
    {
        // The gap between two rail endpoints can be lateral (or even point
        // backwards). It joins positions; the rails define the car's axis.
        Vector2 sourceForward = ResolveRailConnectionEndpointForward(
            sourceRail, sourceDistance, true, referenceForward);
        Vector2 targetForward = ResolveRailConnectionEndpointForward(
            targetRail, targetDistance, false, sourceForward);
        Vector2 forward = Vector2.Lerp(sourceForward, targetForward, Mathf.Clamp01(progress));
        return forward.sqrMagnitude > 0.0001f
            ? forward.normalized
            : (progress < 0.5f ? sourceForward : targetForward);
    }

    private static Vector2 ResolveRailConnectionEndpointForward(
        ProjectF.Railway.IRailTarget rail, float distance, bool exiting, Vector2 referenceForward)
    {
        if (rail == null
            || !rail.TrySampleRenderedPath(distance, out _, out Vector2 tangent)
            || tangent.sqrMagnitude <= 0.0001f)
        {
            return referenceForward.sqrMagnitude > 0.0001f ? referenceForward.normalized : Vector2.up;
        }

        float sign;
        if (distance <= 0.0001f)
        {
            sign = exiting ? -1f : 1f;
        }
        else if (rail.TryGetRenderedPathLength(out float length) && distance >= length - 0.0001f)
        {
            sign = exiting ? 1f : -1f;
        }
        else
        {
            // A branch may enter the interior of the adjacent rail.
            sign = Vector2.Dot(tangent, referenceForward) < 0f ? -1f : 1f;
        }

        return tangent.normalized * sign;
    }

    public void DisconnectFrom(Train other)
    {
        if (other == null)
        {
            return;
        }

        TrainWorld.Shared.Disconnect(runtimeTrain, other.runtimeTrain);
    }

    public void ClearTrainConnections()
    {
        TrainWorld.Shared.ClearConnections(runtimeTrain);
    }

    public bool CanConnectTo(Train other)
    {
        return other != null
               && other != this
               && gameObject.activeInHierarchy
               && other.gameObject.activeInHierarchy
               && TryGetPlacementRuntime(out _, out _)
               && other.TryGetPlacementRuntime(out _, out _)
               && CanConnectByPose(this, other);
    }

    public static bool CanConnectByPose(Train first, Train second)
    {
        if (first == null
            || second == null
            || first == second
            || !TryGetConnectionPose(first, out Vector2 firstPoint, out Vector2 firstTangent)
            || !TryGetConnectionPose(second, out Vector2 secondPoint, out Vector2 secondTangent))
        {
            return false;
        }

        return CanConnectByPose(first, firstPoint, firstTangent, second, secondPoint, secondTangent);
    }

    internal static bool CanConnectByPose(
        Train first, Vector2 firstPoint, Vector2 firstTangent,
        Train second, Vector2 secondPoint, Vector2 secondTangent)
    {
        if (first == null || second == null || first == second
            || firstTangent.sqrMagnitude <= 0.0001f || secondTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        firstTangent.Normalize();
        secondTangent.Normalize();
        Vector2 delta = secondPoint - firstPoint;
        float maxCenterDistance = ResolveConnectionMaxCenterDistance(first, second);
        float maxLateralDistance = Mathf.Max(
            first.ConnectionMaxLateralDistance,
            second.ConnectionMaxLateralDistance);

        if (first is SteamTrain || second is SteamTrain)
        {
            // A locomotive's tail supplies the coupling point. The approaching
            // car's heading, including another locomotive's nose, cannot veto it.
            return (first is SteamTrain
                    && IsConnectionOffsetInRange(delta, -firstTangent, maxCenterDistance, maxLateralDistance))
                   || (second is SteamTrain
                       && IsConnectionOffsetInRange(-delta, -secondTangent, maxCenterDistance, maxLateralDistance));
        }

        float tangentDot = Mathf.Abs(Vector2.Dot(firstTangent, secondTangent));
        float minForwardDot = Mathf.Min(first.ConnectionMinForwardDot, second.ConnectionMinForwardDot);
        if (tangentDot < minForwardDot)
        {
            return false;
        }

        // The bisector follows the connection between two cars on a curve.
        // Testing only the first car's axis made connection depend on call order.
        Vector2 alignedSecondTangent = Vector2.Dot(firstTangent, secondTangent) < 0f
            ? -secondTangent : secondTangent;
        Vector2 connectionAxis = (firstTangent + alignedSecondTangent).normalized;
        if (Vector2.Dot(delta, connectionAxis) < 0f)
        {
            connectionAxis = -connectionAxis;
        }

        return IsConnectionOffsetInRange(delta, connectionAxis, maxCenterDistance, maxLateralDistance);
    }

    private static bool IsConnectionOffsetInRange(
        Vector2 offset, Vector2 connectionAxis, float maxCenterDistance, float maxLateralDistance)
    {
        float alongDistance = Vector2.Dot(offset, connectionAxis);
        return alongDistance >= ConnectionCenterDistance * 0.5f
               && alongDistance <= maxCenterDistance
               && Mathf.Abs(Cross(connectionAxis, offset)) <= maxLateralDistance;
    }

    private static bool TryGetConnectionPose(Train train, out Vector2 point, out Vector2 tangent)
    {
        point = Vector2.zero;
        tangent = Vector2.up;
        if (train == null)
        {
            return false;
        }

        if (train.TryGetCurrentRailPose(out _, out _, out point, out tangent)
            && tangent.sqrMagnitude > 0.0001f)
        {
            tangent.Normalize();
            return true;
        }

        Vector3 position = train.transform.position;
        Vector3 forward = train.transform.forward;
        point = new Vector2(position.x, position.z);
        tangent = new Vector2(forward.x, forward.z);
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        tangent.Normalize();
        return true;
    }

    internal static float ResolveConnectionMaxCenterDistance(Train first, Train second)
    {
        if (first == null || second == null)
        {
            return DefaultConnectionFallbackDistance;
        }

        float snapDistance = Mathf.Max(first.ConnectionSnapMaxDistance, second.ConnectionSnapMaxDistance);
        return ConnectionCenterDistance + snapDistance;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    public virtual void ApplyPlacedRailSample(
        ProjectF.Railway.IRailTarget rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        TryApplyRailPose(rail, distanceAlongPath, railPoint, facingTangent);
    }

    public virtual void ApplyPlacedRailSampleUnits(
        ProjectF.Railway.IRailTarget rail,
        long distanceAlongPathUnits,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        if (TryApplyRailPose(
                rail,
                DeterministicSimulationUnits.ToFloat(distanceAlongPathUnits),
                railPoint,
                facingTangent))
        {
            RuntimeTrain.RailDistanceUnits = System.Math.Max(0L, distanceAlongPathUnits);
        }
    }

    public virtual bool TryApplyRailPose(
        ProjectF.Railway.IRailTarget rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        if (!rail.IsAlive() || facingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        facingTangent.Normalize();
        Quaternion rotation = Quaternion.LookRotation(
            new Vector3(facingTangent.x, 0f, facingTangent.y),
            Vector3.up);

        return ApplyRailPoseToRail(
            rail,
            distanceAlongPath,
            railPoint,
            facingTangent,
            rotation);
    }

    protected bool ApplyRailPoseToRail(
        ProjectF.Railway.IRailTarget rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent,
        Quaternion rotation)
    {
        if (!rail.IsAlive() || facingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        facingTangent.Normalize();
        Vector3 position = transform.position;
        position.x = railPoint.x;
        position.z = railPoint.y;
        TrainWorld.Shared.SetWorldPose(RuntimeTrain, position, rotation);
        if (cachedTrainRigidbody == null)
        {
            cachedTrainRigidbody = GetComponent<Rigidbody>();
        }

        if (cachedTrainRigidbody != null)
        {
            cachedTrainRigidbody.position = position;
            cachedTrainRigidbody.rotation = rotation;
            cachedTrainRigidbody.linearVelocity = Vector3.zero;
            cachedTrainRigidbody.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(position, rotation);
        SetCurrentRailSample(rail, distanceAlongPath, railPoint, facingTangent);
        RefreshRuntimeCoordinate(position);
        return true;
    }

    public bool TryGetCurrentRailSample(
        Vector2 currentPoint,
        float maxSqrDistance,
        out ProjectF.Railway.IRailTarget rail,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        rail = null;
        distanceAlongPath = 0f;
        pathPoint = currentPoint;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (!TryGetCurrentRailPose(out rail, out distanceAlongPath, out pathPoint, out tangent))
        {
            return false;
        }

        sqrDistance = (currentPoint - pathPoint).sqrMagnitude;
        return sqrDistance <= maxSqrDistance;
    }

    public bool TryGetCurrentRailPose(
        out ProjectF.Railway.IRailTarget rail,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent)
    {
        rail = null;
        distanceAlongPath = 0;
        pathPoint = tangent = Vector2.zero;
        return runtimeTrain != null && runtimeTrain.TryGetRailPose(out rail, out distanceAlongPath, out pathPoint, out tangent);
    }

    public bool TryGetCurrentRailPoseUnits(
        out ProjectF.Railway.IRailTarget rail,
        out long distanceAlongPathUnits,
        out Vector2 pathPoint,
        out Vector2 tangent)
    {
        bool found = TryGetCurrentRailPose(out rail, out _, out pathPoint, out tangent);
        distanceAlongPathUnits = found ? runtimeTrain.RailDistanceUnits : 0L;
        return found;
    }

    protected void SetCurrentRailSample(ProjectF.Railway.IRailTarget rail, float distanceAlongPath, Vector2 point, Vector2 tangent)
    {
        RuntimeTrain.SetRailSample(rail, distanceAlongPath, point, tangent);
    }

    internal void ConfigureCurrentRailConnectionTransition(
        ProjectF.Railway.IRailTarget targetRail,
        float targetDistanceAlongPath,
        Vector2 targetPoint,
        Vector2 targetTangent,
        float connectionPathDistance,
        float connectionProgress)
    {
        RuntimeTrain.ConfigureRailConnectionTransition(targetRail, targetDistanceAlongPath,
            targetPoint, targetTangent, connectionPathDistance, connectionProgress);
    }

    internal bool TryGetCurrentRailConnectionTransition(
        out ProjectF.Railway.IRailTarget targetRail,
        out float targetDistanceAlongPath,
        out Vector2 targetPoint,
        out Vector2 targetTangent,
        out float connectionPathDistance,
        out float connectionProgress)
    {
        targetRail = null;
        targetDistanceAlongPath = connectionPathDistance = connectionProgress = 0;
        targetPoint = targetTangent = Vector2.zero;
        return runtimeTrain != null && runtimeTrain.TryGetRailConnectionTransition(out targetRail, out targetDistanceAlongPath,
            out targetPoint, out targetTangent, out connectionPathDistance, out connectionProgress);
    }

    internal void ClearCurrentRailConnectionTransition()
    {
        runtimeTrain?.ClearRailConnectionTransition();
    }

    protected void RefreshRuntimeCoordinate(Vector3 worldPosition)
    {
        RefreshSingleCellRuntimePlacement(worldPosition, RuntimeQuarterTurns);
    }

}
