using System.Collections.Generic;
using UnityEngine;

public class RailHandcar : Train
{
    [SerializeField, Min(0.01f)]
    private float railMoveSpeedMultiplier = 1f;
    [SerializeField, Min(0.05f)]
    private float railSnapMaxDistance = 0.75f;
    [SerializeField, Min(0.01f)]
    private float railRotationInterpolationSpeed = 10f;
    [SerializeField, Min(0.05f)]
    private float railSearchRadius = 2.25f;
    [SerializeField, Min(0.01f)]
    private float railInputDeadZone = 0.12f;
    [SerializeField, Min(0.05f)]
    private float branchSwitchMaxDistance = 0.7f;
    [SerializeField, Min(0.05f)]
    private float branchSwitchLookAhead = 0.8f;
    [SerializeField, Range(0f, 1f)]
    private float branchSwitchMinInputDot = 0.35f;
    [SerializeField, Min(0.05f)]
    private float railConnectionMaxDistance = 0.08f;
    [SerializeField, Min(0.05f)]
    private float railConnectionLookAhead = 0.5f;
    [SerializeField, Min(0.001f)]
    private float internalConnectionMaxDistance = 0.015f;

    private const int RailNetworkAdvanceMaxHops = 16;
    private const float MinRailConnectionMaxDistance = 0.22f;
    private const float MinInternalConnectionMaxDistance = 0.22f;
    private const float RailDirectionReferenceDeadZone = 0.05f;

    private readonly List<InstallationObject> railSearchScratch = new List<InstallationObject>(16);
    private readonly List<Railload> railCandidateScratch = new List<Railload>(8);

    private Railload currentRail;
    private float currentRailDistance;
    private Vector2 currentRailTangent;
    private Rigidbody cachedRigidbody;

    private struct RailSample
    {
        public Railload Rail;
        public float DistanceAlongPath;
        public Vector2 Point;
        public Vector2 Tangent;
        public float SqrDistance;
    }

    public override void ApplyPlacedRailSample(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        if (rail == null)
        {
            return;
        }

        RailSample sample = new RailSample
        {
            Rail = rail,
            DistanceAlongPath = distanceAlongPath,
            Point = railPoint,
            Tangent = facingTangent,
            SqrDistance = 0f
        };
        ApplyRailPose(sample, ResolveFacingTangent(facingTangent), 0f, false);
    }

    public override void HandleMountedInput(Vector3 worldMoveDirection, float moveSpeed, float deltaTime)
    {
        Vector2 inputVector = new Vector2(worldMoveDirection.x, worldMoveDirection.z);
        float inputMagnitude = Mathf.Clamp01(inputVector.magnitude);
        bool hasInput = inputMagnitude > railInputDeadZone;
        Vector2 inputDirection = hasInput ? inputVector / inputMagnitude : Vector2.zero;
        Vector2 currentPoint = new Vector2(transform.position.x, transform.position.z);
        float maxSqrDistance = railSnapMaxDistance * railSnapMaxDistance;
        Vector2 sampleSearchDirection = hasInput
            ? inputDirection
            : ResolveFacingTangent(currentRailTangent);
        if (sampleSearchDirection.sqrMagnitude <= 0.0001f)
        {
            sampleSearchDirection = new Vector2(transform.forward.x, transform.forward.z);
        }

        if (sampleSearchDirection.sqrMagnitude <= 0.0001f)
        {
            sampleSearchDirection = Vector2.up;
        }

        sampleSearchDirection.Normalize();
        if (!TryGetCurrentRailSample(currentPoint, maxSqrDistance, out RailSample currentSample)
            && !TryFindBestRailSample(currentPoint, sampleSearchDirection, maxSqrDistance, out currentSample))
        {
            ResetVehicleMotion();
            return;
        }

        bool switchedToBranch = false;
        if (hasInput && TryFindBranchRailSample(currentSample, inputDirection, out RailSample branchSample))
        {
            currentSample = branchSample;
            switchedToBranch = true;
        }

        Vector2 currentFacingTangent = switchedToBranch
            ? ResolveBranchFacingTangent(currentSample.Tangent, inputDirection)
            : ResolveFacingTangent(currentSample.Tangent);
        float inputAxis = 0f;
        if (hasInput)
        {
            inputAxis = Vector2.Dot(inputDirection, currentFacingTangent) * inputMagnitude;
            if (Mathf.Abs(inputAxis) <= railInputDeadZone)
            {
                inputAxis = 0f;
            }
        }

        float signedSpeed = UpdateVehicleSignedSpeed(inputAxis, deltaTime);
        if (Mathf.Abs(signedSpeed) <= 0.0001f)
        {
            ApplyRailPose(currentSample, currentFacingTangent, deltaTime, true);
            return;
        }

        float signedStep = signedSpeed
                           * Mathf.Max(0.01f, railMoveSpeedMultiplier)
                           * Mathf.Max(0f, deltaTime);
        if (Mathf.Abs(signedStep) <= 0.0001f)
        {
            ApplyRailPose(currentSample, currentFacingTangent, deltaTime, true);
            return;
        }

        Vector2 travelDirection = signedSpeed >= 0f ? currentFacingTangent : -currentFacingTangent;
        if (!TryAdvanceAlongRailNetwork(
                currentSample,
                travelDirection,
                Mathf.Abs(signedStep),
                out RailSample targetSample,
                out float traveledDistance))
        {
            targetSample = currentSample;
            traveledDistance = 0f;
        }

        Vector2 targetFacingTangent = ResolveFacingTangent(
            targetSample.Tangent,
            currentFacingTangent);
        float signedWheelDistance = Mathf.Sign(signedStep) * traveledDistance;
        ApplyRailPose(targetSample, targetFacingTangent, deltaTime, true);
        RotateWheelsByDistance(signedWheelDistance);
    }

    private bool TryGetCurrentRailSample(Vector2 currentPoint, float maxSqrDistance, out RailSample sample)
    {
        sample = default;
        if (currentRail == null
            || !TryCreateRailSampleAtDistance(currentRail, currentRailDistance, out sample))
        {
            return false;
        }

        sample.SqrDistance = (currentPoint - sample.Point).sqrMagnitude;
        return sample.SqrDistance <= maxSqrDistance;
    }

    private bool TryAdvanceAlongRailNetwork(
        RailSample startSample,
        Vector2 travelDirection,
        float moveDistance,
        out RailSample targetSample,
        out float traveledDistance)
    {
        targetSample = startSample;
        traveledDistance = 0f;
        float remainingDistance = Mathf.Max(0f, moveDistance);
        if (remainingDistance <= 0.0001f)
        {
            return true;
        }

        if (travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        RailSample currentSample = startSample;
        travelDirection.Normalize();
        for (int hop = 0; hop < RailNetworkAdvanceMaxHops; hop++)
        {
            if (currentSample.Rail == null
                || !currentSample.Rail.TryGetRenderedPathLength(out float pathLength))
            {
                return false;
            }

            float travelDot = Vector2.Dot(travelDirection, currentSample.Tangent);
            if (Mathf.Abs(travelDot) <= 0.0001f)
            {
                travelDot = 1f;
            }

            float directionSign = Mathf.Sign(travelDot);
            float availableDistance = directionSign > 0f
                ? pathLength - currentSample.DistanceAlongPath
                : currentSample.DistanceAlongPath;
            if (remainingDistance <= availableDistance + 0.0001f)
            {
                float targetDistance = Mathf.Clamp(
                    currentSample.DistanceAlongPath + directionSign * remainingDistance,
                    0f,
                    pathLength);
                traveledDistance += remainingDistance;
                return TryCreateRailSampleAtDistance(currentSample.Rail, targetDistance, out targetSample);
            }

            float endpointDistance = directionSign > 0f ? pathLength : 0f;
            if (!TryCreateRailSampleAtDistance(currentSample.Rail, endpointDistance, out RailSample endpointSample))
            {
                return false;
            }

            targetSample = endpointSample;
            float consumedDistance = Mathf.Max(0f, availableDistance);
            traveledDistance += consumedDistance;
            remainingDistance -= consumedDistance;
            if (remainingDistance <= 0.0001f)
            {
                return true;
            }

            Vector2 exitDirection = directionSign > 0f ? endpointSample.Tangent : -endpointSample.Tangent;
            if (exitDirection.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            exitDirection.Normalize();
            if (!TryFindConnectedRailSample(
                    endpointSample,
                    exitDirection,
                    currentSample.Rail,
                    out RailSample connectedSample))
            {
                return true;
            }

            currentSample = connectedSample;
            travelDirection = ResolveFacingTangent(currentSample.Tangent, exitDirection);
        }

        targetSample = currentSample;
        return true;
    }

    private static bool TryCreateRailSampleAtDistance(
        Railload rail,
        float distanceAlongPath,
        out RailSample sample)
    {
        sample = default;
        if (rail == null
            || !rail.TrySampleRenderedPath(distanceAlongPath, out Vector2 pathPoint, out Vector2 tangent))
        {
            return false;
        }

        sample.Rail = rail;
        sample.DistanceAlongPath = distanceAlongPath;
        sample.Point = pathPoint;
        sample.Tangent = tangent;
        sample.SqrDistance = 0f;
        return true;
    }

    private bool TryFindBestRailSample(
        Vector2 point,
        Vector2 preferredDirection,
        float maxSqrDistance,
        out RailSample bestSample,
        Railload excludedRail = null)
    {
        bestSample = default;
        if (preferredDirection.sqrMagnitude > 0.0001f)
        {
            preferredDirection.Normalize();
        }

        CollectRailCandidates(point);
        bool found = false;
        float bestScore = float.MaxValue;
        for (int i = 0; i < railCandidateScratch.Count; i++)
        {
            Railload rail = railCandidateScratch[i];
            if (rail == null
                || rail == excludedRail
                || !rail.TryFindNearestRenderedPathSample(
                    point,
                    out float distanceAlongPath,
                    out Vector2 pathPoint,
                    out Vector2 tangent,
                    out float sqrDistance)
                || sqrDistance > maxSqrDistance)
            {
                continue;
            }

            float directionScore = preferredDirection.sqrMagnitude > 0.0001f
                ? Mathf.Abs(Vector2.Dot(preferredDirection, tangent))
                : 0f;
            float sameRailBonus = rail == currentRail ? 0.02f : 0f;
            float score = sqrDistance - directionScore * 0.08f - sameRailBonus;
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestSample.Rail = rail;
            bestSample.DistanceAlongPath = distanceAlongPath;
            bestSample.Point = pathPoint;
            bestSample.Tangent = tangent;
            bestSample.SqrDistance = sqrDistance;
            found = true;
        }

        railCandidateScratch.Clear();
        railSearchScratch.Clear();
        return found;
    }

    private bool TryFindBranchRailSample(
        RailSample currentSample,
        Vector2 inputDirection,
        out RailSample branchSample)
    {
        branchSample = default;
        if (currentSample.Rail == null || inputDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        inputDirection.Normalize();
        float maxBranchSnapDistance = Mathf.Min(branchSwitchMaxDistance, ResolveRailConnectionMaxDistance());
        float maxBranchSqrDistance = maxBranchSnapDistance * maxBranchSnapDistance;
        float currentInputDot = Mathf.Abs(Vector2.Dot(inputDirection, currentSample.Tangent));
        float currentProgress = ResolveRailLookAheadProgress(
            currentSample.Rail,
            currentSample.DistanceAlongPath,
            currentSample.Tangent,
            currentSample.Point,
            inputDirection,
            branchSwitchLookAhead);
        float currentScore = ResolveBranchSelectionScore(currentInputDot, currentProgress, 0f);

        railCandidateScratch.Clear();
        AddRailCandidates(currentSample.Point);
        AddRailCandidates(currentSample.Point + inputDirection * branchSwitchLookAhead);

        bool found = false;
        float bestScore = currentScore + 0.05f;
        for (int i = 0; i < railCandidateScratch.Count; i++)
        {
            Railload rail = railCandidateScratch[i];
            if (rail == null
                || rail == currentSample.Rail
                || !TryFindRailConnectionSampleNearPoint(
                    rail,
                    currentSample.Point,
                    out float distanceAlongPath,
                    out Vector2 pathPoint,
                    out Vector2 tangent,
                    out float sqrDistance)
                || sqrDistance > maxBranchSqrDistance)
            {
                continue;
            }

            float inputDot = Mathf.Abs(Vector2.Dot(inputDirection, tangent));
            if (inputDot < branchSwitchMinInputDot)
            {
                continue;
            }

            float progress = ResolveRailLookAheadProgress(
                rail,
                distanceAlongPath,
                tangent,
                currentSample.Point,
                inputDirection,
                branchSwitchLookAhead);
            if (progress <= 0.03f)
            {
                continue;
            }

            float score = ResolveBranchSelectionScore(inputDot, progress, sqrDistance);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            branchSample.Rail = rail;
            branchSample.DistanceAlongPath = distanceAlongPath;
            branchSample.Point = pathPoint;
            branchSample.Tangent = tangent;
            branchSample.SqrDistance = sqrDistance;
            found = true;
        }

        railCandidateScratch.Clear();
        railSearchScratch.Clear();
        return found;
    }

    private float ResolveRailLookAheadProgress(
        Railload rail,
        float distanceAlongPath,
        Vector2 tangent,
        Vector2 originPoint,
        Vector2 inputDirection,
        float lookAheadDistance)
    {
        if (rail == null || inputDirection.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        float tangentDot = Vector2.Dot(inputDirection.normalized, tangent);
        if (Mathf.Abs(tangentDot) <= 0.0001f
            || !rail.TryGetRenderedPathLength(out float pathLength))
        {
            return 0f;
        }

        float targetDistance = Mathf.Clamp(
            distanceAlongPath + Mathf.Sign(tangentDot) * Mathf.Max(0.05f, lookAheadDistance),
            0f,
            pathLength);
        if (Mathf.Abs(targetDistance - distanceAlongPath) <= 0.0001f
            || !rail.TrySampleRenderedPath(targetDistance, out Vector2 futurePoint, out _))
        {
            return 0f;
        }

        return Mathf.Max(0f, Vector2.Dot(futurePoint - originPoint, inputDirection.normalized));
    }

    private static float ResolveBranchSelectionScore(float inputDot, float progress, float sqrDistance)
    {
        return inputDot * 1.4f + progress * 0.8f - sqrDistance * 2f;
    }

    private bool TryFindConnectedRailSample(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        out RailSample connectedSample)
    {
        connectedSample = default;
        if (endpointSample.Rail == null || exitDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        exitDirection.Normalize();
        railCandidateScratch.Clear();
        AddRailCandidates(endpointSample.Point);
        AddRailCandidates(endpointSample.Point + exitDirection * railConnectionLookAhead);

        bool found = false;
        float maxConnectionDistance = ResolveRailConnectionMaxDistance();
        float maxConnectionSqrDistance = maxConnectionDistance * maxConnectionDistance;
        float bestScore = float.MinValue;
        for (int i = 0; i < railCandidateScratch.Count; i++)
        {
            Railload rail = railCandidateScratch[i];
            if (rail == null
                || rail == excludedRail
                || !TryFindRailConnectionSample(
                    rail,
                    endpointSample.Point,
                    true,
                    out float distanceAlongPath,
                    out Vector2 pathPoint,
                    out Vector2 tangent,
                    out float sqrDistance)
                || sqrDistance > maxConnectionSqrDistance)
            {
                continue;
            }

            float directionScore = Mathf.Abs(Vector2.Dot(exitDirection, tangent));
            float progress = ResolveRailLookAheadProgress(
                rail,
                distanceAlongPath,
                tangent,
                endpointSample.Point,
                exitDirection,
                railConnectionLookAhead);
            if (progress <= 0.01f && directionScore < 0.12f)
            {
                continue;
            }

            float score = progress * 1.2f + directionScore * 0.6f - sqrDistance * 3f;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            connectedSample.Rail = rail;
            connectedSample.DistanceAlongPath = distanceAlongPath;
            connectedSample.Point = pathPoint;
            connectedSample.Tangent = tangent;
            connectedSample.SqrDistance = sqrDistance;
            found = true;
        }

        railCandidateScratch.Clear();
        railSearchScratch.Clear();
        return found;
    }

    private bool TryFindRailConnectionSampleNearPoint(
        Railload rail,
        Vector2 point,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        return TryFindRailConnectionSample(
            rail,
            point,
            true,
            out distanceAlongPath,
            out pathPoint,
            out tangent,
            out sqrDistance);
    }

    private bool TryFindRailConnectionSample(
        Railload rail,
        Vector2 point,
        bool allowInternalFallback,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        distanceAlongPath = 0f;
        pathPoint = point;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (rail == null)
        {
            return false;
        }

        bool found = false;
        if (TryUpdateEndpointConnectionSample(rail, point, true, ref distanceAlongPath, ref pathPoint, ref tangent, ref sqrDistance))
        {
            found = true;
        }

        if (TryUpdateEndpointConnectionSample(rail, point, false, ref distanceAlongPath, ref pathPoint, ref tangent, ref sqrDistance))
        {
            found = true;
        }

        if (!allowInternalFallback)
        {
            return found;
        }

        float maxInternalConnectionDistance = ResolveInternalConnectionMaxDistance();
        if (rail.TryFindNearestRenderedPathSample(
                point,
                out float internalDistance,
                out Vector2 internalPoint,
                out Vector2 internalTangent,
                out float internalSqrDistance)
            && internalSqrDistance <= maxInternalConnectionDistance * maxInternalConnectionDistance
            && (!found || internalSqrDistance < sqrDistance))
        {
            distanceAlongPath = internalDistance;
            pathPoint = internalPoint;
            tangent = internalTangent;
            sqrDistance = internalSqrDistance;
            found = true;
        }

        return found;
    }

    private float ResolveRailConnectionMaxDistance()
    {
        return Mathf.Max(MinRailConnectionMaxDistance, railConnectionMaxDistance);
    }

    private float ResolveInternalConnectionMaxDistance()
    {
        return Mathf.Max(MinInternalConnectionMaxDistance, internalConnectionMaxDistance);
    }

    private static bool TryUpdateEndpointConnectionSample(
        Railload rail,
        Vector2 point,
        bool startEndpoint,
        ref float bestDistanceAlongPath,
        ref Vector2 bestPathPoint,
        ref Vector2 bestTangent,
        ref float bestSqrDistance)
    {
        if (rail == null
            || !rail.TryGetRenderedEndpointSample(
                startEndpoint,
                out float distanceAlongPath,
                out Vector2 pathPoint,
                out Vector2 tangent))
        {
            return false;
        }

        float sqrDistance = (point - pathPoint).sqrMagnitude;
        if (sqrDistance >= bestSqrDistance)
        {
            return false;
        }

        bestDistanceAlongPath = distanceAlongPath;
        bestPathPoint = pathPoint;
        bestTangent = tangent;
        bestSqrDistance = sqrDistance;
        return true;
    }

    private void CollectRailCandidates(Vector2 point)
    {
        railCandidateScratch.Clear();
        AddRailCandidates(point);
    }

    private void AddRailCandidates(Vector2 point)
    {
        int searchCells = Mathf.CeilToInt(Mathf.Max(0.05f, railSearchRadius));
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.RoundToInt(point.x),
            Mathf.RoundToInt(point.y));

        for (int offsetY = -searchCells; offsetY <= searchCells; offsetY++)
        {
            for (int offsetX = -searchCells; offsetX <= searchCells; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                railSearchScratch.Clear();
                InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
                    coordinate,
                    railSearchScratch);

                for (int i = 0; i < railSearchScratch.Count; i++)
                {
                    if (railSearchScratch[i] is not Railload rail
                        || railCandidateScratch.Contains(rail))
                    {
                        continue;
                    }

                    railCandidateScratch.Add(rail);
                }
            }
        }
    }

    private Vector2 ResolveFacingTangent(Vector2 railTangent)
    {
        Vector2 currentFacing = currentRailTangent.sqrMagnitude > 0.0001f
            ? currentRailTangent
            : new Vector2(transform.forward.x, transform.forward.z);
        return ResolveFacingTangent(railTangent, currentFacing);
    }

    private Vector2 ResolveBranchFacingTangent(Vector2 railTangent, Vector2 inputDirection)
    {
        Vector2 currentFacing = currentRailTangent.sqrMagnitude > 0.0001f
            ? currentRailTangent
            : new Vector2(transform.forward.x, transform.forward.z);
        if (railTangent.sqrMagnitude <= 0.0001f)
        {
            return currentFacing.sqrMagnitude > 0.0001f
                ? currentFacing.normalized
                : inputDirection.sqrMagnitude > 0.0001f
                    ? inputDirection.normalized
                    : Vector2.up;
        }

        railTangent.Normalize();
        if (TryResolveTangentReferenceSign(
                railTangent,
                currentFacing,
                out float referenceSign)
            || TryResolveTangentReferenceSign(
                railTangent,
                inputDirection,
                out referenceSign))
        {
            railTangent *= referenceSign;
        }

        return railTangent;
    }

    private Vector2 ResolveFacingTangent(Vector2 railTangent, Vector2 referenceDirection)
    {
        Vector2 currentFacing = currentRailTangent.sqrMagnitude > 0.0001f
            ? currentRailTangent
            : new Vector2(transform.forward.x, transform.forward.z);
        if (railTangent.sqrMagnitude <= 0.0001f)
        {
            if (referenceDirection.sqrMagnitude > 0.0001f)
            {
                return referenceDirection.normalized;
            }

            return currentFacing.sqrMagnitude > 0.0001f
                ? currentFacing.normalized
                : Vector2.up;
        }

        railTangent.Normalize();
        if (TryResolveTangentReferenceSign(
                railTangent,
                referenceDirection,
                out float referenceSign)
            || TryResolveTangentReferenceSign(
                railTangent,
                currentFacing,
                out referenceSign))
        {
            railTangent *= referenceSign;
        }

        return railTangent;
    }

    private static bool TryResolveTangentReferenceSign(
        Vector2 tangent,
        Vector2 referenceDirection,
        out float sign)
    {
        sign = 1f;
        if (tangent.sqrMagnitude <= 0.0001f
            || referenceDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float dot = Vector2.Dot(tangent.normalized, referenceDirection.normalized);
        if (Mathf.Abs(dot) <= RailDirectionReferenceDeadZone)
        {
            return false;
        }

        sign = Mathf.Sign(dot);
        return true;
    }

    private void ApplyRailPose(
        RailSample sample,
        Vector2 facingTangent,
        float deltaTime = 0f,
        bool smoothRotation = false)
    {
        if (sample.Rail == null)
        {
            return;
        }

        if (facingTangent.sqrMagnitude <= 0.0001f)
        {
            facingTangent = sample.Tangent.sqrMagnitude > 0.0001f
                ? sample.Tangent
                : Vector2.up;
        }

        facingTangent.Normalize();
        Vector3 position = transform.position;
        position.x = sample.Point.x;
        position.z = sample.Point.y;
        Quaternion targetRotation = Quaternion.LookRotation(
            new Vector3(facingTangent.x, 0f, facingTangent.y),
            Vector3.up);
        Quaternion rotation = targetRotation;
        if (smoothRotation && deltaTime > 0f)
        {
            float interpolation = 1f - Mathf.Exp(
                -Mathf.Max(0.01f, railRotationInterpolationSpeed) * deltaTime);
            rotation = Quaternion.Slerp(transform.rotation, targetRotation, interpolation);
        }

        if (cachedRigidbody == null)
        {
            cachedRigidbody = GetComponent<Rigidbody>();
        }

        if (cachedRigidbody != null)
        {
            cachedRigidbody.position = position;
            cachedRigidbody.rotation = rotation;
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(position, rotation);
        currentRail = sample.Rail;
        currentRailDistance = sample.DistanceAlongPath;
        currentRailTangent = facingTangent;
        SetCurrentRailSample(sample.Rail, sample.DistanceAlongPath, facingTangent);
        RefreshRuntimeCoordinate(position);
        Physics.SyncTransforms();
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
