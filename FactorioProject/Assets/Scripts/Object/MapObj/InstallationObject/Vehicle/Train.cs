using System.Collections.Generic;
using UnityEngine;

public class Train : Vehicle
{
    private const float MinConnectionDistance = 0.05f;
    private const float DefaultConnectionFallbackDistance = 1.4f;
    private const float TrainLoadSpeedReductionPerMass = 0.01f;
    private const float MinTrainLoadSpeedMultiplier = 0.01f;
    private const float ConnectionSideEpsilon = 0.01f;
    private const float StoredRailPointDeviationSqr = 0.000001f;

    private static readonly HashSet<Train> ActiveRuntimeTrains = new HashSet<Train>();
    private static ulong connectionGraphRevision;

    [SerializeField, Min(0.05f)]
    private float trainConnectionCenterDistance = 0.9f;
    [SerializeField, Min(0f)]
    private float trainConnectionGapDistance = 0.35f;
    [SerializeField, Min(0.01f)]
    private float trainConnectionSnapMaxDistance = 0.35f;
    [SerializeField, Min(0.01f)]
    private float trainConnectionMaxLateralDistance = 0.45f;
    [SerializeField, Range(0f, 1f)]
    private float trainConnectionMinForwardDot = 0.5f;
    [SerializeField, Min(0.01f)]
    private float trainMass = 1f;

    private Rigidbody cachedTrainRigidbody;
    private Railload currentRail;
    private float currentRailDistance;
    private Vector2 currentRailPoint;
    private Vector2 currentRailTangent;
    private Railload currentRailConnectionTargetRail;
    private float currentRailConnectionTargetDistance;
    private Vector2 currentRailConnectionTargetPoint;
    private Vector2 currentRailConnectionTargetTangent;
    private float currentRailConnectionPathDistance;
    private float currentRailConnectionProgress;
    private readonly HashSet<Train> connectedTrains = new HashSet<Train>();

    public float ConnectionCenterDistance => Mathf.Max(
        MinConnectionDistance,
        trainConnectionCenterDistance + trainConnectionGapDistance);
    public float ConnectionSnapMaxDistance => Mathf.Max(MinConnectionDistance, trainConnectionSnapMaxDistance);
    public float ConnectionMaxLateralDistance => Mathf.Max(MinConnectionDistance, trainConnectionMaxLateralDistance);
    public float ConnectionMinForwardDot => Mathf.Clamp01(trainConnectionMinForwardDot);
    public float TrainMass => Mathf.Max(0.01f, trainMass);
    public float TrainLoadSpeedMultiplier => Mathf.Clamp(
        1f - TrainMass * TrainLoadSpeedReductionPerMass,
        MinTrainLoadSpeedMultiplier,
        1f);
    public IReadOnlyCollection<Train> ConnectedTrains => connectedTrains;
    public static ulong ConnectionGraphRevision => connectionGraphRevision;

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
        ClearTrainConnections();
        ClearCurrentRailSample();
        base.OnPlacementRuntimeCleared();
    }

    private void ClearCurrentRailSample()
    {
        currentRail = null;
        currentRailDistance = 0f;
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

        bool changed = connectedTrains.Add(other);
        changed |= other.connectedTrains.Add(this);
        if (changed)
        {
            IncrementConnectionGraphRevision();
        }

        return changed;
    }

    public void DisconnectFrom(Train other)
    {
        if (other == null)
        {
            return;
        }

        bool changed = connectedTrains.Remove(other);
        changed |= other.connectedTrains.Remove(this);
        if (changed)
        {
            IncrementConnectionGraphRevision();
        }
    }

    public void ClearTrainConnections()
    {
        if (connectedTrains.Count <= 0)
        {
            return;
        }

        Train[] connectedSnapshot = new Train[connectedTrains.Count];
        connectedTrains.CopyTo(connectedSnapshot);
        for (int i = 0; i < connectedSnapshot.Length; i++)
        {
            DisconnectFrom(connectedSnapshot[i]);
        }

        connectedTrains.Clear();
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

        float tangentDot = Mathf.Abs(Vector2.Dot(firstTangent, secondTangent));
        float minForwardDot = Mathf.Min(first.ConnectionMinForwardDot, second.ConnectionMinForwardDot);
        if (tangentDot < minForwardDot)
        {
            return false;
        }

        Vector2 delta = secondPoint - firstPoint;
        if (!first.CanConnectToTrainAtOffset(second, delta, firstTangent)
            || !second.CanConnectToTrainAtOffset(first, -delta, secondTangent))
        {
            return false;
        }

        float alongDistance = Mathf.Abs(Vector2.Dot(delta, firstTangent));
        float maxCenterDistance = ResolveConnectionMaxCenterDistance(first, second);
        if (alongDistance > maxCenterDistance)
        {
            return false;
        }

        float lateralDistance = Mathf.Abs(Cross(firstTangent, delta));
        float maxLateralDistance = Mathf.Max(
            first.ConnectionMaxLateralDistance,
            second.ConnectionMaxLateralDistance);
        return lateralDistance <= maxLateralDistance;
    }

    protected virtual bool CanConnectToTrainAtOffset(
        Train other,
        Vector2 offsetToOther,
        Vector2 forwardTangent)
    {
        return other != null
               && forwardTangent.sqrMagnitude > 0.0001f;
    }

    protected static bool IsConnectionOffsetAhead(Vector2 offsetToOther, Vector2 forwardTangent)
    {
        if (forwardTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        forwardTangent.Normalize();
        return Vector2.Dot(offsetToOther, forwardTangent) > ConnectionSideEpsilon;
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

    private static float ResolveConnectionMaxCenterDistance(Train first, Train second)
    {
        if (first == null || second == null)
        {
            return DefaultConnectionFallbackDistance;
        }

        float centerDistance = (first.ConnectionCenterDistance + second.ConnectionCenterDistance) * 0.5f;
        float snapDistance = Mathf.Max(first.ConnectionSnapMaxDistance, second.ConnectionSnapMaxDistance);
        return Mathf.Max(MinConnectionDistance, centerDistance + snapDistance);
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
            || !currentRail.TrySampleRenderedPath(currentRailDistance, out Vector2 sampledPoint, out tangent))
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
        distanceAlongPath = currentRailDistance;
        return true;
    }

    protected void SetCurrentRailSample(Railload rail, float distanceAlongPath, Vector2 point, Vector2 tangent)
    {
        currentRail = rail;
        currentRailDistance = Mathf.Max(0f, distanceAlongPath);
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
        currentRailConnectionTargetDistance = Mathf.Max(0f, targetDistanceAlongPath);
        currentRailConnectionTargetPoint = targetPoint;
        currentRailConnectionTargetTangent = targetTangent;
        currentRailConnectionPathDistance = connectionPathDistance;
        currentRailConnectionProgress = Mathf.Clamp(connectionProgress, 0f, connectionPathDistance);
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
        targetDistanceAlongPath = currentRailConnectionTargetDistance;
        targetPoint = currentRailConnectionTargetPoint;
        targetTangent = currentRailConnectionTargetTangent;
        connectionPathDistance = currentRailConnectionPathDistance;
        connectionProgress = currentRailConnectionProgress;
        return targetRail != null && connectionPathDistance > 0f;
    }

    internal void ClearCurrentRailConnectionTransition()
    {
        currentRailConnectionTargetRail = null;
        currentRailConnectionTargetDistance = 0f;
        currentRailConnectionTargetPoint = Vector2.zero;
        currentRailConnectionTargetTangent = Vector2.zero;
        currentRailConnectionPathDistance = 0f;
        currentRailConnectionProgress = 0f;
    }

    protected void RefreshRuntimeCoordinate(Vector3 worldPosition)
    {
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.z));
        var occupiedCoordinates = RuntimeOccupiedCoordinates;
        if (occupiedCoordinates != null
            && occupiedCoordinates.Count == 1
            && occupiedCoordinates[0] == coordinate)
        {
            return;
        }

        ConfigurePlacementRuntime(
            coordinate,
            RuntimeQuarterTurns,
            new[] { coordinate },
            RuntimePlacementSequence);
        RobotArm.WakeAroundCoordinate(coordinate);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        trainMass = Mathf.Max(0.01f, trainMass);
    }
#endif
}
