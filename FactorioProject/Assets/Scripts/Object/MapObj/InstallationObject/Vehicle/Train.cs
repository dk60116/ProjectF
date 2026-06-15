using System.Collections.Generic;
using UnityEngine;

public class Train : Vehicle
{
    private const float DefaultAutoConnectDistance = 1.8f;
    private const float AutoConnectForwardMinDot = 0.55f;
    private const float CoupledSpacingDistanceEpsilon = 0.01f;
    private const float CoupledPushDirectionMinDot = 0.05f;
    private const float CoupledSpacingRailConnectionMaxDistance = 0.35f;
    private const float ConnectionVisualBrightness = 1.8f;
    private const float ConnectionVisualMinBrightness = 0.42f;
    private const float ConnectionVisualContrast = 2.65f;
    private const float ConnectionVisualAlpha = 0.95f;
    private const float ConnectionVisualRimStrength = 0.8f;
    private const float ConnectionVisualRimPower = 2.2f;
    private static readonly Color[] ConnectionColorPalette =
    {
        new Color(0.18f, 0.72f, 1f, 0.95f),
        new Color(1f, 0.62f, 0.14f, 0.95f),
        new Color(0.42f, 0.9f, 0.36f, 0.95f),
        new Color(1f, 0.32f, 0.48f, 0.95f),
        new Color(0.72f, 0.45f, 1f, 0.95f),
        new Color(1f, 0.92f, 0.22f, 0.95f),
        new Color(0.15f, 0.9f, 0.78f, 0.95f),
        new Color(0.95f, 0.42f, 0.94f, 0.95f)
    };

    private static readonly HashSet<Train> ActiveTrains = new HashSet<Train>();
    private static readonly List<Train> ConnectionBuildList = new List<Train>(32);
    private static readonly Queue<Train> ConnectionQueue = new Queue<Train>(16);
    private static readonly HashSet<Train> CoupledSpacingVisited = new HashSet<Train>();
    private static readonly Queue<Train> CoupledSpacingQueue = new Queue<Train>(16);
    private static readonly Queue<Vector2> CoupledSpacingPushDirectionQueue = new Queue<Vector2>(16);
    private static readonly int BlueprintPreviewPropertyId = Shader.PropertyToID("_BlueprintPreview");
    private static readonly int BlueprintTintPropertyId = Shader.PropertyToID("_BlueprintTint");
    private static readonly int BlueprintBrightnessPropertyId = Shader.PropertyToID("_BlueprintBrightness");
    private static readonly int BlueprintMinBrightnessPropertyId = Shader.PropertyToID("_BlueprintMinBrightness");
    private static readonly int BlueprintContrastPropertyId = Shader.PropertyToID("_BlueprintContrast");
    private static readonly int BlueprintAlphaPropertyId = Shader.PropertyToID("_BlueprintAlpha");
    private static readonly int BlueprintRimColorPropertyId = Shader.PropertyToID("_BlueprintRimColor");
    private static readonly int BlueprintRimStrengthPropertyId = Shader.PropertyToID("_BlueprintRimStrength");
    private static readonly int BlueprintRimPowerPropertyId = Shader.PropertyToID("_BlueprintRimPower");

    [SerializeField, Min(0.05f)]
    private float autoConnectDistance = DefaultAutoConnectDistance;
    [SerializeField, Min(0.05f)]
    private float trainCouplingCenterDistance = 1f;
    [SerializeField, Min(0f)]
    private float trainCouplingGapDistance = 0.4f;
    [SerializeField, Min(0.05f)]
    private float trainCouplingSnapMaxDistance = DefaultAutoConnectDistance;

    private Rigidbody cachedTrainRigidbody;
    private Railload currentRail;
    private float currentRailDistance;
    private Vector2 currentRailTangent;
    private readonly List<Train> connectedTrains = new List<Train>(2);
    private Color blueprintConnectionColor = ConnectionColorPalette[0];
    private int connectionGroupSeed;
    private Renderer[] connectionColorRenderers;
    private MaterialPropertyBlock connectionColorPropertyBlock;
    private bool connectionVisualApplied;
    private Color connectionVisualColor;
    private bool connectionVisualOverrideActive;
    private Color connectionVisualOverrideColor;

    public IReadOnlyList<Train> ConnectedTrains => connectedTrains;
    public bool HasTrainConnections => connectedTrains.Count > 0;
    public Color BlueprintConnectionColor => blueprintConnectionColor;
    public int ConnectionGroupSeed => connectionGroupSeed;
    public float AutoConnectDistance => Mathf.Max(
        0.05f,
        autoConnectDistance,
        trainCouplingSnapMaxDistance,
        trainCouplingCenterDistance + trainCouplingGapDistance);
    public float CouplingCenterDistance => Mathf.Max(0.05f, trainCouplingCenterDistance + trainCouplingGapDistance);
    public float CouplingOverlapAllowance => Mathf.Clamp(trainCouplingGapDistance * 0.5f, 0.02f, 0.25f);

    private static bool coupledSpacingRefreshInProgress;

    protected override void OnEnable()
    {
        base.OnEnable();
        ActiveTrains.Add(this);
        RefreshAllConnections();
    }

    protected override void OnDisable()
    {
        ActiveTrains.Remove(this);
        connectionVisualOverrideActive = false;
        ClearConnections();
        RefreshAllConnections();
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        ActiveTrains.Remove(this);
        connectionVisualOverrideActive = false;
        ClearConnections();
        currentRail = null;
        currentRailDistance = 0f;
        currentRailTangent = Vector2.zero;
        base.PrepareForPool();
    }

    protected override void OnPlacementRuntimeChanged()
    {
        base.OnPlacementRuntimeChanged();
        if (!coupledSpacingRefreshInProgress)
        {
            RefreshAllConnections();
        }
    }

    protected override void OnPlacementRuntimeCleared()
    {
        ClearConnections();
        base.OnPlacementRuntimeCleared();
        RefreshAllConnections();
    }

    public virtual void ApplyPlacedRailSample(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        if (!ApplyRailPose(rail, distanceAlongPath, railPoint, facingTangent, false))
        {
            return;
        }

        RefreshAllConnections();
    }

    private bool ApplyRailPose(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent,
        bool notifyCoupledPoseApplied)
    {
        if (rail == null || facingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        facingTangent.Normalize();
        Vector3 position = transform.position;
        position.x = railPoint.x;
        position.z = railPoint.y;
        Quaternion rotation = Quaternion.LookRotation(
            new Vector3(facingTangent.x, 0f, facingTangent.y),
            Vector3.up);

        if (cachedTrainRigidbody == null)
        {
            cachedTrainRigidbody = GetComponent<Rigidbody>();
        }

        if (cachedTrainRigidbody != null)
        {
            cachedTrainRigidbody.position = position;
            cachedTrainRigidbody.rotation = rotation;
            cachedTrainRigidbody.velocity = Vector3.zero;
            cachedTrainRigidbody.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(position, rotation);
        SetCurrentRailSample(rail, distanceAlongPath, facingTangent);
        RefreshRuntimeCoordinate(position);
        if (notifyCoupledPoseApplied)
        {
            OnCoupledRailPoseApplied(facingTangent);
        }

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
        if (sqrDistance > maxSqrDistance)
        {
            return false;
        }

        return true;
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
            || !currentRail.TrySampleRenderedPath(currentRailDistance, out pathPoint, out tangent))
        {
            return false;
        }

        if (currentRailTangent.sqrMagnitude > 0.0001f
            && tangent.sqrMagnitude > 0.0001f
            && Vector2.Dot(tangent, currentRailTangent.normalized) < 0f)
        {
            tangent = -tangent;
        }

        rail = currentRail;
        distanceAlongPath = currentRailDistance;
        return true;
    }

    protected void SetCurrentRailSample(Railload rail, float distanceAlongPath, Vector2 tangent)
    {
        currentRail = rail;
        currentRailDistance = Mathf.Max(0f, distanceAlongPath);
        currentRailTangent = tangent;
    }

    public bool CanAutoConnectToPose(Vector3 otherPosition, Vector3 otherForward, float otherAutoConnectDistance)
    {
        if (!HasRuntimePlacement())
        {
            return false;
        }

        return CanAutoConnectTrainPoses(
            transform.position,
            transform.forward,
            AutoConnectDistance,
            otherPosition,
            otherForward,
            otherAutoConnectDistance);
    }

    public static bool CanAutoConnectTrainPoses(
        Vector3 firstPosition,
        Vector3 firstForward,
        float firstAutoConnectDistance,
        Vector3 secondPosition,
        Vector3 secondForward,
        float secondAutoConnectDistance)
    {
        float maxDistance = Mathf.Max(
            0.05f,
            Mathf.Min(
                Mathf.Max(0.05f, firstAutoConnectDistance),
                Mathf.Max(0.05f, secondAutoConnectDistance)));
        if (PlanarSqrDistance(firstPosition, secondPosition) > maxDistance * maxDistance)
        {
            return false;
        }

        Vector2 firstDirection = NormalizePlanarForward(firstForward);
        Vector2 secondDirection = NormalizePlanarForward(secondForward);
        if (firstDirection.sqrMagnitude <= 0.0001f || secondDirection.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        return Mathf.Abs(Vector2.Dot(firstDirection, secondDirection)) >= AutoConnectForwardMinDot;
    }

    public static bool TryGetAutoConnectColorNearPose(
        Vector3 position,
        Vector3 forward,
        float autoConnectDistance,
        out Color color)
    {
        return TryGetAutoConnectInfoNearPose(
            position,
            forward,
            autoConnectDistance,
            out color,
            out _,
            out _);
    }

    public static bool CollectAutoConnectTrainsNearPose(
        Vector3 position,
        Vector3 forward,
        float autoConnectDistance,
        ICollection<Train> results)
    {
        if (results == null)
        {
            return false;
        }

        bool addedAny = false;
        foreach (Train train in ActiveTrains)
        {
            if (train == null
                || !train.gameObject.activeInHierarchy
                || !train.HasRuntimePlacement()
                || !train.CanAutoConnectToPose(position, forward, autoConnectDistance)
                || results.Contains(train))
            {
                continue;
            }

            results.Add(train);
            addedAny = true;
        }

        return addedAny;
    }

    public static bool TryGetAutoConnectInfoNearPose(
        Vector3 position,
        Vector3 forward,
        float autoConnectDistance,
        out Color color,
        out int groupSeed,
        out float sqrDistance)
    {
        color = default;
        groupSeed = int.MaxValue;
        sqrDistance = float.MaxValue;
        Train bestTrain = null;
        foreach (Train train in ActiveTrains)
        {
            if (train == null
                || !train.gameObject.activeInHierarchy
                || !train.HasRuntimePlacement()
                || !train.CanAutoConnectToPose(position, forward, autoConnectDistance))
            {
                continue;
            }

            int candidateGroupSeed = train.ConnectionGroupSeed;
            float candidateSqrDistance = PlanarSqrDistance(position, train.transform.position);
            if (candidateGroupSeed > groupSeed
                || (candidateGroupSeed == groupSeed && candidateSqrDistance >= sqrDistance))
            {
                continue;
            }

            bestTrain = train;
            groupSeed = candidateGroupSeed;
            sqrDistance = candidateSqrDistance;
        }

        if (bestTrain == null)
        {
            return false;
        }

        color = bestTrain.BlueprintConnectionColor;
        return true;
    }

    public static Color GetConnectionColorForSeed(int seed)
    {
        int index = Mathf.Abs(seed) % ConnectionColorPalette.Length;
        return ConnectionColorPalette[index];
    }

    public static float GetDefaultAutoConnectDistance()
    {
        return DefaultAutoConnectDistance;
    }

    public void SetConnectionPreviewVisualOverride(Color color)
    {
        connectionVisualOverrideActive = true;
        connectionVisualOverrideColor = GetOpaqueConnectionColor(color);
        RefreshConnectionColorVisual();
    }

    public void ClearConnectionPreviewVisualOverride()
    {
        if (!connectionVisualOverrideActive)
        {
            return;
        }

        connectionVisualOverrideActive = false;
        RefreshConnectionColorVisual();
    }

    private bool CanAutoConnectTo(Train other)
    {
        if (other == null || other == this || !HasRuntimePlacement() || !other.HasRuntimePlacement())
        {
            return false;
        }

        if (!CanAutoConnectTrainPoses(
                transform.position,
                transform.forward,
                AutoConnectDistance,
                other.transform.position,
                other.transform.forward,
                other.AutoConnectDistance))
        {
            return false;
        }

        if (currentRail != null
            && currentRail == other.currentRail
            && Mathf.Abs(currentRailDistance - other.currentRailDistance) <= Mathf.Max(AutoConnectDistance, other.AutoConnectDistance))
        {
            return true;
        }

        if (currentRailTangent.sqrMagnitude <= 0.0001f || other.currentRailTangent.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        return Mathf.Abs(Vector2.Dot(currentRailTangent.normalized, other.currentRailTangent.normalized)) >= AutoConnectForwardMinDot;
    }

    private bool HasRuntimePlacement()
    {
        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        return occupiedCoordinates != null && occupiedCoordinates.Count > 0;
    }

    protected static void CollectActiveRuntimeTrains(ICollection<Train> results)
    {
        if (results == null)
        {
            return;
        }

        foreach (Train train in ActiveTrains)
        {
            if (train == null
                || !train.gameObject.activeInHierarchy
                || !train.HasRuntimePlacement()
                || results.Contains(train))
            {
                continue;
            }

            results.Add(train);
        }
    }

    protected void CollectConnectedTrainGroup(HashSet<Train> results)
    {
        if (results == null || !results.Add(this))
        {
            return;
        }

        ConnectionQueue.Clear();
        ConnectionQueue.Enqueue(this);
        while (ConnectionQueue.Count > 0)
        {
            Train current = ConnectionQueue.Dequeue();
            if (current == null || current.connectedTrains.Count <= 0)
            {
                continue;
            }

            for (int i = 0; i < current.connectedTrains.Count; i++)
            {
                Train connectedTrain = current.connectedTrains[i];
                if (connectedTrain == null || !results.Add(connectedTrain))
                {
                    continue;
                }

                ConnectionQueue.Enqueue(connectedTrain);
            }
        }

        ConnectionQueue.Clear();
    }

    protected void RefreshPushedConnectedTrainSpacing(Vector2 pushDirection)
    {
        if (coupledSpacingRefreshInProgress
            || connectedTrains.Count <= 0
            || pushDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        pushDirection.Normalize();
        coupledSpacingRefreshInProgress = true;
        try
        {
            CoupledSpacingVisited.Clear();
            CoupledSpacingVisited.Add(this);
            RefreshPushedConnectedTrainSpacingFrom(this, pushDirection);
        }
        finally
        {
            CoupledSpacingVisited.Clear();
            CoupledSpacingQueue.Clear();
            CoupledSpacingPushDirectionQueue.Clear();
            coupledSpacingRefreshInProgress = false;
        }

        Physics.SyncTransforms();
        RefreshAllConnections();
    }

    private static void RefreshPushedConnectedTrainSpacingFrom(Train anchor, Vector2 pushDirection)
    {
        if (anchor == null || pushDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        pushDirection.Normalize();
        CoupledSpacingQueue.Clear();
        CoupledSpacingPushDirectionQueue.Clear();
        CoupledSpacingQueue.Enqueue(anchor);
        CoupledSpacingPushDirectionQueue.Enqueue(pushDirection);
        while (CoupledSpacingQueue.Count > 0)
        {
            Train currentAnchor = CoupledSpacingQueue.Dequeue();
            Vector2 currentPushDirection = CoupledSpacingPushDirectionQueue.Count > 0
                ? CoupledSpacingPushDirectionQueue.Dequeue()
                : pushDirection;
            if (currentAnchor == null || currentAnchor.connectedTrains.Count <= 0)
            {
                continue;
            }

            for (int i = 0; i < currentAnchor.connectedTrains.Count; i++)
            {
                Train connectedTrain = currentAnchor.connectedTrains[i];
                if (connectedTrain == null
                    || CoupledSpacingVisited.Contains(connectedTrain)
                    || !IsConnectedTrainInPushDirection(currentAnchor, connectedTrain, currentPushDirection))
                {
                    continue;
                }

                CoupledSpacingVisited.Add(connectedTrain);
                if (TryResolveCoupledTrainTargetPose(
                        currentAnchor,
                        connectedTrain,
                        out Railload rail,
                        out float distanceAlongPath,
                        out Vector2 railPoint,
                        out Vector2 facingTangent))
                {
                    connectedTrain.ApplyCoupledRailPose(rail, distanceAlongPath, railPoint, facingTangent);
                    Vector2 nextPushDirection = ResolveNextCoupledPushDirection(currentPushDirection, facingTangent);
                    CoupledSpacingQueue.Enqueue(connectedTrain);
                    CoupledSpacingPushDirectionQueue.Enqueue(nextPushDirection);
                }
            }
        }

        CoupledSpacingQueue.Clear();
        CoupledSpacingPushDirectionQueue.Clear();
    }

    private static bool IsConnectedTrainInPushDirection(Train anchor, Train connectedTrain, Vector2 pushDirection)
    {
        if (anchor == null
            || connectedTrain == null
            || pushDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        pushDirection.Normalize();
        if (anchor.currentRail != null
            && anchor.currentRail == connectedTrain.currentRail
            && anchor.currentRailTangent.sqrMagnitude > 0.0001f)
        {
            float distanceDelta = connectedTrain.currentRailDistance - anchor.currentRailDistance;
            float pushRailDot = Vector2.Dot(pushDirection, anchor.currentRailTangent.normalized);
            if (Mathf.Abs(distanceDelta) > CoupledSpacingDistanceEpsilon
                && Mathf.Abs(pushRailDot) > CoupledSpacingDistanceEpsilon)
            {
                return Mathf.Sign(distanceDelta) == Mathf.Sign(pushRailDot);
            }
        }

        Vector2 separation = new Vector2(
            connectedTrain.transform.position.x - anchor.transform.position.x,
            connectedTrain.transform.position.z - anchor.transform.position.z);
        if (separation.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        return Vector2.Dot(separation.normalized, pushDirection) >= CoupledPushDirectionMinDot;
    }

    private static Vector2 ResolveNextCoupledPushDirection(Vector2 pushDirection, Vector2 facingTangent)
    {
        if (facingTangent.sqrMagnitude <= 0.0001f)
        {
            return pushDirection;
        }

        facingTangent.Normalize();
        if (pushDirection.sqrMagnitude > 0.0001f
            && Vector2.Dot(facingTangent, pushDirection.normalized) < 0f)
        {
            facingTangent = -facingTangent;
        }

        return facingTangent;
    }

    private static bool TryResolveCoupledTrainTargetPose(
        Train anchor,
        Train connectedTrain,
        out Railload rail,
        out float distanceAlongPath,
        out Vector2 railPoint,
        out Vector2 facingTangent)
    {
        rail = null;
        distanceAlongPath = 0f;
        railPoint = Vector2.zero;
        facingTangent = Vector2.zero;
        if (anchor == null
            || connectedTrain == null
            || anchor.currentRail == null
            || !anchor.currentRail.TryGetRenderedPathLength(out float pathLength))
        {
            return false;
        }

        float directionSign = ResolveCoupledTrainDirectionSign(anchor, connectedTrain);
        float spacingDistance = Mathf.Max(anchor.CouplingCenterDistance, connectedTrain.CouplingCenterDistance);
        float targetDistance = anchor.currentRailDistance + directionSign * spacingDistance;
        if (targetDistance < 0f || targetDistance > pathLength)
        {
            return TryResolveCoupledTrainTargetPoseAcrossRailConnection(
                anchor,
                connectedTrain.currentRail,
                directionSign,
                spacingDistance,
                out rail,
                out distanceAlongPath,
                out railPoint,
                out facingTangent);
        }

        if (Mathf.Abs(targetDistance - connectedTrain.currentRailDistance) <= CoupledSpacingDistanceEpsilon
            && anchor.currentRail.TrySampleRenderedPath(targetDistance, out railPoint, out facingTangent))
        {
            rail = anchor.currentRail;
            distanceAlongPath = targetDistance;
            facingTangent = ResolveCoupledTrainFacing(anchor, facingTangent);
            return true;
        }

        if (!anchor.currentRail.TrySampleRenderedPath(targetDistance, out railPoint, out facingTangent))
        {
            return false;
        }

        rail = anchor.currentRail;
        distanceAlongPath = targetDistance;
        facingTangent = ResolveCoupledTrainFacing(anchor, facingTangent);
        return true;
    }

    private static bool TryResolveCoupledTrainTargetPoseAcrossRailConnection(
        Train anchor,
        Railload connectedRail,
        float directionSign,
        float spacingDistance,
        out Railload rail,
        out float distanceAlongPath,
        out Vector2 railPoint,
        out Vector2 facingTangent)
    {
        rail = null;
        distanceAlongPath = 0f;
        railPoint = Vector2.zero;
        facingTangent = Vector2.zero;
        if (anchor == null
            || anchor.currentRail == null
            || connectedRail == null
            || connectedRail == anchor.currentRail
            || !anchor.currentRail.TryGetRenderedPathLength(out float anchorPathLength))
        {
            return false;
        }

        float availableDistance = directionSign > 0f
            ? anchorPathLength - anchor.currentRailDistance
            : anchor.currentRailDistance;
        if (availableDistance < -CoupledSpacingDistanceEpsilon)
        {
            return false;
        }

        float remainingDistance = spacingDistance - Mathf.Max(0f, availableDistance);
        if (remainingDistance <= CoupledSpacingDistanceEpsilon)
        {
            float endpointDistance = directionSign > 0f ? anchorPathLength : 0f;
            return TryResolveCoupledTrainTargetPoseOnRail(
                anchor,
                anchor.currentRail,
                endpointDistance,
                out rail,
                out distanceAlongPath,
                out railPoint,
                out facingTangent);
        }

        float anchorEndpointDistance = directionSign > 0f ? anchorPathLength : 0f;
        if (!anchor.currentRail.TrySampleRenderedPath(anchorEndpointDistance, out Vector2 anchorEndpoint, out Vector2 anchorEndpointTangent))
        {
            return false;
        }

        Vector2 exitDirection = directionSign > 0f ? anchorEndpointTangent : -anchorEndpointTangent;
        if (exitDirection.sqrMagnitude <= 0.0001f
            || !connectedRail.TryFindNearestRenderedPathSample(
                anchorEndpoint,
                out float connectedDistance,
                out _,
                out Vector2 connectedTangent,
                out float connectedSqrDistance))
        {
            return false;
        }

        float maxConnectionSqrDistance = CoupledSpacingRailConnectionMaxDistance * CoupledSpacingRailConnectionMaxDistance;
        if (connectedSqrDistance > maxConnectionSqrDistance
            || !connectedRail.TryGetRenderedPathLength(out float connectedPathLength))
        {
            return false;
        }

        exitDirection.Normalize();
        if (connectedTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        connectedTangent.Normalize();
        float connectedDirectionSign = Vector2.Dot(exitDirection, connectedTangent) >= 0f ? 1f : -1f;
        float targetDistance = connectedDistance + connectedDirectionSign * remainingDistance;
        if (targetDistance < 0f || targetDistance > connectedPathLength)
        {
            return false;
        }

        return TryResolveCoupledTrainTargetPoseOnRail(
            anchor,
            connectedRail,
            targetDistance,
            out rail,
            out distanceAlongPath,
            out railPoint,
            out facingTangent);
    }

    private static bool TryResolveCoupledTrainTargetPoseOnRail(
        Train anchor,
        Railload targetRail,
        float targetDistance,
        out Railload rail,
        out float distanceAlongPath,
        out Vector2 railPoint,
        out Vector2 facingTangent)
    {
        rail = null;
        distanceAlongPath = 0f;
        railPoint = Vector2.zero;
        facingTangent = Vector2.zero;
        if (targetRail == null || !targetRail.TrySampleRenderedPath(targetDistance, out railPoint, out facingTangent))
        {
            return false;
        }

        rail = targetRail;
        distanceAlongPath = targetDistance;
        facingTangent = ResolveCoupledTrainFacing(anchor, facingTangent);
        return true;
    }

    private static float ResolveCoupledTrainDirectionSign(Train anchor, Train connectedTrain)
    {
        if (anchor.currentRail != null && anchor.currentRail == connectedTrain.currentRail)
        {
            float distanceDelta = connectedTrain.currentRailDistance - anchor.currentRailDistance;
            if (Mathf.Abs(distanceDelta) > CoupledSpacingDistanceEpsilon)
            {
                return Mathf.Sign(distanceDelta);
            }
        }

        Vector2 separation = new Vector2(
            connectedTrain.transform.position.x - anchor.transform.position.x,
            connectedTrain.transform.position.z - anchor.transform.position.z);
        Vector2 anchorFacing = anchor.currentRailTangent.sqrMagnitude > 0.0001f
            ? anchor.currentRailTangent.normalized
            : NormalizePlanarForward(anchor.transform.forward);
        if (separation.sqrMagnitude <= 0.0001f || anchorFacing.sqrMagnitude <= 0.0001f)
        {
            return -1f;
        }

        return Vector2.Dot(separation.normalized, anchorFacing) >= 0f ? 1f : -1f;
    }

    private static Vector2 ResolveCoupledTrainFacing(Train anchor, Vector2 tangent)
    {
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            return NormalizePlanarForward(anchor != null ? anchor.transform.forward : Vector3.forward);
        }

        tangent.Normalize();
        Vector2 referenceFacing = anchor != null && anchor.currentRailTangent.sqrMagnitude > 0.0001f
            ? anchor.currentRailTangent.normalized
            : NormalizePlanarForward(anchor != null ? anchor.transform.forward : Vector3.forward);
        if (referenceFacing.sqrMagnitude > 0.0001f && Vector2.Dot(tangent, referenceFacing) < 0f)
        {
            tangent = -tangent;
        }

        return tangent;
    }

    private void ApplyCoupledRailPose(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        ApplyRailPose(rail, distanceAlongPath, railPoint, facingTangent, true);
    }

    protected virtual void OnCoupledRailPoseApplied(Vector2 facingTangent)
    {
    }

    private void ClearConnections()
    {
        connectedTrains.Clear();
        connectionGroupSeed = 0;
        blueprintConnectionColor = ConnectionColorPalette[0];
        RefreshConnectionColorVisual();
    }

    private static void RefreshAllConnections()
    {
        ConnectionBuildList.Clear();
        foreach (Train train in ActiveTrains)
        {
            if (train == null)
            {
                continue;
            }

            if (!train.gameObject.activeInHierarchy || !train.HasRuntimePlacement())
            {
                train.ClearConnections();
                continue;
            }

            train.connectedTrains.Clear();
            train.connectionGroupSeed = Mathf.Max(1, (int)(train.RuntimePlacementSequence & 0x7fffffff));
            ConnectionBuildList.Add(train);
        }

        for (int i = 0; i < ConnectionBuildList.Count; i++)
        {
            Train first = ConnectionBuildList[i];
            for (int j = i + 1; j < ConnectionBuildList.Count; j++)
            {
                Train second = ConnectionBuildList[j];
                if (!first.CanAutoConnectTo(second))
                {
                    continue;
                }

                first.connectedTrains.Add(second);
                second.connectedTrains.Add(first);
            }
        }

        AssignConnectionGroupColors();
        ConnectionBuildList.Clear();
    }

    private static void AssignConnectionGroupColors()
    {
        HashSet<Train> visited = new HashSet<Train>();
        for (int i = 0; i < ConnectionBuildList.Count; i++)
        {
            Train root = ConnectionBuildList[i];
            if (root == null || !visited.Add(root))
            {
                continue;
            }

            int seed = root.connectionGroupSeed;
            ConnectionQueue.Clear();
            ConnectionQueue.Enqueue(root);
            while (ConnectionQueue.Count > 0)
            {
                Train current = ConnectionQueue.Dequeue();
                seed = Mathf.Min(seed, current.connectionGroupSeed);
                for (int connectionIndex = 0; connectionIndex < current.connectedTrains.Count; connectionIndex++)
                {
                    Train connected = current.connectedTrains[connectionIndex];
                    if (connected == null || !visited.Add(connected))
                    {
                        continue;
                    }

                    ConnectionQueue.Enqueue(connected);
                }
            }

            Color color = GetConnectionColorForSeed(seed);
            ApplyConnectionColor(root, seed, color);
        }
    }

    private static void ApplyConnectionColor(Train root, int seed, Color color)
    {
        if (root == null)
        {
            return;
        }

        HashSet<Train> visited = new HashSet<Train>();
        ConnectionQueue.Clear();
        ConnectionQueue.Enqueue(root);
        visited.Add(root);
        while (ConnectionQueue.Count > 0)
        {
            Train current = ConnectionQueue.Dequeue();
            current.connectionGroupSeed = seed;
            current.blueprintConnectionColor = color;
            current.RefreshConnectionColorVisual();
            for (int i = 0; i < current.connectedTrains.Count; i++)
            {
                Train connected = current.connectedTrains[i];
                if (connected == null || !visited.Add(connected))
                {
                    continue;
                }

                ConnectionQueue.Enqueue(connected);
            }
        }
    }

    private void RefreshConnectionColorVisual()
    {
        bool shouldApply = connectionVisualOverrideActive;
        Color targetColor = connectionVisualOverrideActive
            ? connectionVisualOverrideColor
            : Color.white;
        if (connectionVisualApplied == shouldApply
            && (!shouldApply || connectionVisualColor == targetColor))
        {
            return;
        }

        EnsureConnectionColorRenderers();
        connectionColorPropertyBlock ??= new MaterialPropertyBlock();
        if (connectionColorRenderers != null)
        {
            for (int i = 0; i < connectionColorRenderers.Length; i++)
            {
                ApplyConnectionColorVisual(connectionColorRenderers[i], shouldApply, targetColor);
            }
        }

        connectionVisualApplied = shouldApply;
        connectionVisualColor = targetColor;
    }

    private void EnsureConnectionColorRenderers()
    {
        if (connectionColorRenderers == null || connectionColorRenderers.Length == 0)
        {
            connectionColorRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }

    private void ApplyConnectionColorVisual(Renderer renderer, bool enabled, Color color)
    {
        if (!ShouldApplyConnectionColorVisual(renderer))
        {
            return;
        }

        Material sharedMaterial = renderer.sharedMaterial;
        if (sharedMaterial == null)
        {
            return;
        }

        bool applied = false;
        connectionColorPropertyBlock.Clear();
        renderer.GetPropertyBlock(connectionColorPropertyBlock);

        if (sharedMaterial.HasProperty(BlueprintPreviewPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintPreviewPropertyId, enabled ? 1f : 0f);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintTintPropertyId))
        {
            connectionColorPropertyBlock.SetColor(BlueprintTintPropertyId, color);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintBrightnessPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintBrightnessPropertyId, ConnectionVisualBrightness);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintMinBrightnessPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintMinBrightnessPropertyId, ConnectionVisualMinBrightness);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintContrastPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintContrastPropertyId, ConnectionVisualContrast);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintAlphaPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintAlphaPropertyId, enabled ? ConnectionVisualAlpha : 1f);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintRimColorPropertyId))
        {
            connectionColorPropertyBlock.SetColor(BlueprintRimColorPropertyId, color);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintRimStrengthPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintRimStrengthPropertyId, enabled ? ConnectionVisualRimStrength : 0f);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintRimPowerPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintRimPowerPropertyId, ConnectionVisualRimPower);
            applied = true;
        }

        if (applied)
        {
            renderer.SetPropertyBlock(connectionColorPropertyBlock);
        }
    }

    private static bool ShouldApplyConnectionColorVisual(Renderer renderer)
    {
        if (renderer == null
            || renderer is LineRenderer
            || renderer is ParticleSystemRenderer
            || renderer is SpriteRenderer
            || renderer.GetComponent<WorkableObjectRangeVisual>() != null
            || renderer.GetComponent<TMPro.TextMeshPro>() != null)
        {
            return false;
        }

        return renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
    }

    private static Color GetOpaqueConnectionColor(Color color)
    {
        return new Color(color.r, color.g, color.b, 1f);
    }

    private static Vector2 NormalizePlanarForward(Vector3 forward)
    {
        Vector2 direction = new Vector2(forward.x, forward.z);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
    }

    private static float PlanarSqrDistance(Vector3 first, Vector3 second)
    {
        float deltaX = first.x - second.x;
        float deltaZ = first.z - second.z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }

    private void RefreshRuntimeCoordinate(Vector3 worldPosition)
    {
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.z));
        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
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
    }
}
