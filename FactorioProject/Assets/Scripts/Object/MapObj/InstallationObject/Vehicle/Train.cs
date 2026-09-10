using System.Collections.Generic;
using UnityEngine;

public class Train : Vehicle
{
    // Grid cells are one world unit. Installation and movement share this pitch.
    public const float ConnectionCenterDistance = 1f;
    private const float MinConnectionDistance = 0.05f;
    private const float DefaultConnectionFallbackDistance = 1.4f;
    private const float StoredRailPointDeviationSqr = 0.000001f;

    private static readonly HashSet<Train> ActiveRuntimeTrains = new HashSet<Train>();
    private static ulong connectionGraphRevision;

    [SerializeField, Min(0.01f)]
    private float trainConnectionSnapMaxDistance = 0.6f;
    [SerializeField, Min(0.01f)]
    private float trainConnectionMaxLateralDistance = 0.45f;
    [SerializeField, Range(0f, 1f)]
    private float trainConnectionMinForwardDot = 0.5f;
    private Rigidbody cachedTrainRigidbody;
    private Railload currentRail;
    private long currentRailDistanceUnits;
    private Vector2 currentRailPoint;
    private Vector2 currentRailTangent;
    private Railload currentRailConnectionTargetRail;
    private long currentRailConnectionTargetDistanceUnits;
    private Vector2 currentRailConnectionTargetPoint;
    private Vector2 currentRailConnectionTargetTangent;
    private long currentRailConnectionPathDistanceUnits;
    private long currentRailConnectionProgressUnits;
    // Connection identity includes the physical end of this car. Rail path point
    // order and the direction of travel must not change which end is the front.
    private readonly Dictionary<Train, bool> connectedTrainEnds = new Dictionary<Train, bool>();
    private readonly Queue<Train> connectionActionGroupQueue = new Queue<Train>();
    private readonly HashSet<Train> connectionActionGroupVisited = new HashSet<Train>();

    public float ConnectionSnapMaxDistance => Mathf.Max(MinConnectionDistance, trainConnectionSnapMaxDistance);
    public float ConnectionMaxLateralDistance => Mathf.Max(MinConnectionDistance, trainConnectionMaxLateralDistance);
    public float ConnectionMinForwardDot => Mathf.Clamp01(trainConnectionMinForwardDot);
    public bool HasPlacedRailSample => currentRail != null;
    public IReadOnlyCollection<Train> ConnectedTrains => connectedTrainEnds.Keys;
    public static ulong ConnectionGraphRevision => connectionGraphRevision;

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

    public void RotateTrainWheelsByDistance(float signedDistance)
    {
        RotateWheelsByDistance(signedDistance);
    }

    public static void CollectActiveRuntimeTrains(ICollection<Train> results)
    {
        if (results == null || ActiveRuntimeTrains.Count <= 0)
        {
            return;
        }

        foreach (Train train in ActiveRuntimeTrains)
        {
            if (train == null
                || !train.gameObject.activeInHierarchy
                || !train.TryGetPlacementRuntime(out _, out _))
            {
                continue;
            }

            results.Add(train);
        }
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
        foreach (Train candidate in ActiveRuntimeTrains)
        {
            if (candidate == null
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
        ActiveRuntimeTrains.Add(this);
    }

    protected override void OnDisable()
    {
        ClearTrainConnections();
        ActiveRuntimeTrains.Remove(this);
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        ActiveRuntimeTrains.Remove(this);
        base.PrepareForPool();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        ClearPlacedRailSample();
        base.OnPlacementRuntimeCleared();
    }

    public void ClearPlacedRailSample()
    {
        ClearTrainConnections();
        ClearCurrentRailSample();
    }

    private void ClearCurrentRailSample()
    {
        currentRail = null;
        currentRailDistanceUnits = 0L;
        currentRailPoint = Vector2.zero;
        currentRailTangent = Vector2.zero;
        ClearCurrentRailConnectionTransition();
    }

    public bool ConnectTo(Train other)
    {
        if (!CanConnectTo(other))
        {
            return false;
        }

        bool changed = AddTrainConnection(other);
        changed |= other.AddTrainConnection(this);
        if (changed)
        {
            IncrementConnectionGraphRevision();
        }

        return changed;
    }

    private bool AddTrainConnection(Train other)
    {
        if (connectedTrainEnds.ContainsKey(other)) return false;
        TryGetConnectionPose(this, out Vector2 point, out Vector2 facing);
        TryGetConnectionPose(other, out Vector2 otherPoint, out _);
        connectedTrainEnds.Add(other, Vector2.Dot(otherPoint - point, facing) > 0f);
        return true;
    }

    internal void SetConnectionEnd(Train other, bool atFront)
    {
        if (other != null && connectedTrainEnds.TryGetValue(other, out bool previous) && previous != atFront)
        {
            connectedTrainEnds[other] = atFront;
            IncrementConnectionGraphRevision();
        }
    }

    internal bool TryGetConnectionFacingSign(Train other, bool otherIsAheadOnPath, out float sign)
    {
        sign = 1f;
        if (other == null || !connectedTrainEnds.TryGetValue(other, out bool atFront)) return false;
        sign = atFront == otherIsAheadOnPath ? 1f : -1f;
        return true;
    }

    internal static Vector2 ResolveRailConnectionForward(
        Railload sourceRail,
        float sourceDistance,
        Railload targetRail,
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
        Railload rail, float distance, bool exiting, Vector2 referenceForward)
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

        bool changed = connectedTrainEnds.Remove(other);
        changed |= other.connectedTrainEnds.Remove(this);
        if (changed)
        {
            IncrementConnectionGraphRevision();
        }
    }

    public void ClearTrainConnections()
    {
        if (connectedTrainEnds.Count <= 0)
        {
            return;
        }

        Train[] connectedSnapshot = new Train[connectedTrainEnds.Count];
        connectedTrainEnds.Keys.CopyTo(connectedSnapshot, 0);
        for (int i = 0; i < connectedSnapshot.Length; i++)
        {
            DisconnectFrom(connectedSnapshot[i]);
        }

        connectedTrainEnds.Clear();
    }

    private static void IncrementConnectionGraphRevision()
    {
        unchecked
        {
            connectionGraphRevision++;
            if (connectionGraphRevision == 0)
            {
                connectionGraphRevision = 1;
            }
        }
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
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        TryApplyRailPose(rail, distanceAlongPath, railPoint, facingTangent);
    }

    public virtual void ApplyPlacedRailSampleUnits(
        Railload rail,
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
            currentRailDistanceUnits = System.Math.Max(0L, distanceAlongPathUnits);
        }
    }

    public virtual bool TryApplyRailPose(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        if (rail == null || facingTangent.sqrMagnitude <= 0.0001f)
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
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent,
        Quaternion rotation)
    {
        if (rail == null || facingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        facingTangent.Normalize();
        Vector3 position = transform.position;
        position.x = railPoint.x;
        position.z = railPoint.y;
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
        out Railload rail,
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
        out Railload rail,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent)
    {
        rail = null;
        distanceAlongPath = 0f;
        pathPoint = Vector2.zero;
        tangent = Vector2.zero;
        if (currentRail == null
            || !currentRail.TrySampleRenderedPath(
                DeterministicSimulationUnits.ToFloat(currentRailDistanceUnits),
                out Vector2 sampledPoint,
                out tangent))
        {
            return false;
        }

        pathPoint = currentRailPoint;
        if ((currentRailPoint - sampledPoint).sqrMagnitude > StoredRailPointDeviationSqr
            && currentRailTangent.sqrMagnitude > 0.0001f)
        {
            tangent = currentRailTangent;
        }
        else if (currentRailTangent.sqrMagnitude > 0.0001f
            && tangent.sqrMagnitude > 0.0001f
            && Vector2.Dot(tangent, currentRailTangent.normalized) < 0f)
        {
            tangent = -tangent;
        }

        rail = currentRail;
        distanceAlongPath = DeterministicSimulationUnits.ToFloat(currentRailDistanceUnits);
        return true;
    }

    public bool TryGetCurrentRailPoseUnits(
        out Railload rail,
        out long distanceAlongPathUnits,
        out Vector2 pathPoint,
        out Vector2 tangent)
    {
        bool found = TryGetCurrentRailPose(out rail, out _, out pathPoint, out tangent);
        distanceAlongPathUnits = found ? currentRailDistanceUnits : 0L;
        return found;
    }

    protected void SetCurrentRailSample(Railload rail, float distanceAlongPath, Vector2 point, Vector2 tangent)
    {
        currentRail = rail;
        currentRailDistanceUnits = DeterministicSimulationUnits.FromFloat(distanceAlongPath);
        currentRailPoint = point;
        currentRailTangent = tangent;
        ClearCurrentRailConnectionTransition();
    }

    internal void ConfigureCurrentRailConnectionTransition(
        Railload targetRail,
        float targetDistanceAlongPath,
        Vector2 targetPoint,
        Vector2 targetTangent,
        float connectionPathDistance,
        float connectionProgress)
    {
        if (targetRail == null || connectionPathDistance <= 0f)
        {
            ClearCurrentRailConnectionTransition();
            return;
        }

        currentRailConnectionTargetRail = targetRail;
        currentRailConnectionTargetDistanceUnits = DeterministicSimulationUnits.FromFloat(
            targetDistanceAlongPath);
        currentRailConnectionTargetPoint = targetPoint;
        currentRailConnectionTargetTangent = targetTangent;
        currentRailConnectionPathDistanceUnits = DeterministicSimulationUnits.FromFloat(
            connectionPathDistance);
        currentRailConnectionProgressUnits = System.Math.Min(
            DeterministicSimulationUnits.FromFloat(Mathf.Max(0f, connectionProgress)),
            currentRailConnectionPathDistanceUnits);
    }

    internal bool TryGetCurrentRailConnectionTransition(
        out Railload targetRail,
        out float targetDistanceAlongPath,
        out Vector2 targetPoint,
        out Vector2 targetTangent,
        out float connectionPathDistance,
        out float connectionProgress)
    {
        targetRail = currentRailConnectionTargetRail;
        targetDistanceAlongPath = DeterministicSimulationUnits.ToFloat(
            currentRailConnectionTargetDistanceUnits);
        targetPoint = currentRailConnectionTargetPoint;
        targetTangent = currentRailConnectionTargetTangent;
        connectionPathDistance = DeterministicSimulationUnits.ToFloat(
            currentRailConnectionPathDistanceUnits);
        connectionProgress = DeterministicSimulationUnits.ToFloat(
            currentRailConnectionProgressUnits);
        return targetRail != null && currentRailConnectionPathDistanceUnits > 0L;
    }

    internal void ClearCurrentRailConnectionTransition()
    {
        currentRailConnectionTargetRail = null;
        currentRailConnectionTargetDistanceUnits = 0L;
        currentRailConnectionTargetPoint = Vector2.zero;
        currentRailConnectionTargetTangent = Vector2.zero;
        currentRailConnectionPathDistanceUnits = 0L;
        currentRailConnectionProgressUnits = 0L;
    }

    protected void RefreshRuntimeCoordinate(Vector3 worldPosition)
    {
        RefreshSingleCellRuntimePlacement(worldPosition, RuntimeQuarterTurns);
    }

}
