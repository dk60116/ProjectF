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
    [SerializeField, Min(1)]
    private int maxCoupledTrainCars = 12;
    [SerializeField, Range(0f, 1f)]
    private float coupledTrainMinFacingDot = 0.15f;

    private const float StableCoupledTrainExtraSnapDistance = 0.5f;
    private const float CoupledConnectionPreferenceWeight = 2.5f;
    private const int RailNetworkAdvanceMaxHops = 16;
    private const float MinRailConnectionMaxDistance = 0.22f;
    private const float MinInternalConnectionMaxDistance = 0.22f;

    private readonly List<InstallationObject> railSearchScratch = new List<InstallationObject>(16);
    private readonly List<Railload> railCandidateScratch = new List<Railload>(8);
    private readonly List<InstallationObject> coupledTrainSearchScratch = new List<InstallationObject>(16);
    private readonly HashSet<Train> coupledTrainVisitedScratch = new HashSet<Train>();
    private readonly List<Train> backwardCoupledTrainChain = new List<Train>(8);
    private readonly List<Train> forwardCoupledTrainChain = new List<Train>(8);
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

        if (hasInput && TryFindBranchRailSample(currentSample, inputDirection, out RailSample branchSample))
        {
            currentSample = branchSample;
        }

        float inputAxis = 0f;
        if (hasInput)
        {
            inputAxis = Vector2.Dot(inputDirection, currentSample.Tangent) * inputMagnitude;
            if (Mathf.Abs(inputAxis) <= railInputDeadZone)
            {
                inputAxis = 0f;
            }
        }

        float signedSpeed = UpdateVehicleSignedSpeed(inputAxis, deltaTime);
        if (Mathf.Abs(signedSpeed) <= 0.0001f)
        {
            Vector2 currentFacingTangent = ResolveFacingTangent(currentSample.Tangent);
            ApplyRailPose(currentSample, currentFacingTangent, deltaTime, true);
            UpdateCoupledTrains(currentSample, currentFacingTangent, deltaTime, 0f);
            return;
        }

        float signedStep = signedSpeed
                           * Mathf.Max(0.01f, railMoveSpeedMultiplier)
                           * Mathf.Max(0f, deltaTime);
        if (Mathf.Abs(signedStep) <= 0.0001f)
        {
            Vector2 currentFacingTangent = ResolveFacingTangent(currentSample.Tangent);
            ApplyRailPose(currentSample, currentFacingTangent, deltaTime, true);
            UpdateCoupledTrains(currentSample, currentFacingTangent, deltaTime, 0f);
            return;
        }

        if (!TryAdvanceAlongRailNetwork(
                currentSample,
                signedStep,
                out RailSample targetSample,
                out float traveledDistance))
        {
            targetSample = currentSample;
            traveledDistance = 0f;
        }

        Vector2 targetFacingTangent = ResolveFacingTangent(targetSample.Tangent);
        float signedWheelDistance = Mathf.Sign(signedStep) * traveledDistance;
        if (!CanMoveWithCachedCoupledTrains(targetSample, targetFacingTangent, signedWheelDistance))
        {
            targetSample = currentSample;
            targetFacingTangent = ResolveFacingTangent(currentSample.Tangent);
            traveledDistance = 0f;
            signedWheelDistance = 0f;
        }

        ApplyRailPose(targetSample, targetFacingTangent, deltaTime, true);
        RotateWheelsByDistance(signedWheelDistance);
        UpdateCoupledTrains(targetSample, targetFacingTangent, deltaTime, signedWheelDistance);
    }

    private bool CanMoveWithCachedCoupledTrains(
        RailSample leadSample,
        Vector2 leadFacingTangent,
        float signedWheelDistance)
    {
        if (leadSample.Rail == null || leadFacingTangent.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        if (backwardCoupledTrainChain.Count == 0 && forwardCoupledTrainChain.Count == 0)
        {
            return true;
        }

        leadFacingTangent.Normalize();
        coupledTrainVisitedScratch.Clear();
        coupledTrainVisitedScratch.Add(this);
        bool canMove = CanMoveCachedCoupledTrainChain(this, leadSample, leadFacingTangent, -1, signedWheelDistance)
                       && CanMoveCachedCoupledTrainChain(this, leadSample, leadFacingTangent, 1, signedWheelDistance);
        coupledTrainVisitedScratch.Clear();
        return canMove;
    }

    private bool CanMoveCachedCoupledTrainChain(
        Train leadTrain,
        RailSample leadSample,
        Vector2 leadFacingTangent,
        int chainDirection,
        float signedWheelDistance)
    {
        Train previousTrain = leadTrain;
        RailSample previousSample = leadSample;
        Vector2 previousFacingTangent = leadFacingTangent;
        int normalizedDirection = chainDirection >= 0 ? 1 : -1;
        int maxCars = Mathf.Max(1, maxCoupledTrainCars);
        List<Train> chain = GetCoupledTrainChain(normalizedDirection);
        int count = Mathf.Min(chain.Count, maxCars);

        for (int i = 0; i < count; i++)
        {
            Train cachedTrain = chain[i];
            if (cachedTrain == null
                || !cachedTrain.gameObject.activeInHierarchy
                || coupledTrainVisitedScratch.Contains(cachedTrain))
            {
                return true;
            }

            Train coupledTrain = null;
            RailSample coupledSample = default;
            Vector2 coupledFacingTangent = Vector2.zero;
            bool resolved = false;
            if (Mathf.Abs(signedWheelDistance) > 0.0001f)
            {
                resolved = TryAdvanceLockedCoupledTrainFromCurrentRail(
                    previousTrain,
                    previousSample,
                    previousFacingTangent,
                    normalizedDirection,
                    i,
                    signedWheelDistance,
                    out coupledTrain,
                    out coupledSample,
                    out coupledFacingTangent);
            }

            if (!resolved)
            {
                resolved = TryGetCachedCoupledTrain(
                    previousTrain,
                    previousSample,
                    previousFacingTangent,
                    normalizedDirection,
                    i,
                    false,
                    out coupledTrain,
                    out coupledSample,
                    out coupledFacingTangent,
                    out _);
            }

            if (!resolved)
            {
                return false;
            }

            coupledTrainVisitedScratch.Add(coupledTrain);
            previousTrain = coupledTrain;
            previousSample = coupledSample;
            previousFacingTangent = coupledFacingTangent;
        }

        return true;
    }

    private bool CanUpdateCoupledTrains(RailSample leadSample, Vector2 leadFacingTangent)
    {
        if (leadSample.Rail == null || leadFacingTangent.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        leadFacingTangent.Normalize();
        coupledTrainVisitedScratch.Clear();
        coupledTrainVisitedScratch.Add(this);
        bool canUpdate = CanUpdateCoupledTrainChain(this, leadSample, leadFacingTangent, -1)
                         && CanUpdateCoupledTrainChain(this, leadSample, leadFacingTangent, 1);
        coupledTrainVisitedScratch.Clear();
        return canUpdate;
    }

    private bool CanUpdateCoupledTrainChain(
        Train leadTrain,
        RailSample leadSample,
        Vector2 leadFacingTangent,
        int chainDirection)
    {
        Train previousTrain = leadTrain;
        RailSample previousSample = leadSample;
        Vector2 previousFacingTangent = leadFacingTangent;
        int normalizedDirection = chainDirection >= 0 ? 1 : -1;
        int maxCars = Mathf.Max(1, maxCoupledTrainCars);

        for (int i = 0; i < maxCars; i++)
        {
            if (!TryResolveStableCoupledTrain(
                    previousTrain,
                    previousSample,
                    previousFacingTangent,
                    normalizedDirection,
                    i,
                    false,
                    out Train coupledTrain,
                    out RailSample coupledSample,
                    out Vector2 coupledFacingTangent,
                    out bool blockedByRailLimit))
            {
                return !blockedByRailLimit;
            }

            coupledTrainVisitedScratch.Add(coupledTrain);
            previousTrain = coupledTrain;
            previousSample = coupledSample;
            previousFacingTangent = coupledFacingTangent;
        }

        return true;
    }

    private void UpdateCoupledTrains(
        RailSample leadSample,
        Vector2 leadFacingTangent,
        float deltaTime,
        float signedWheelDistance)
    {
        if (leadSample.Rail == null || leadFacingTangent.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        leadFacingTangent.Normalize();
        coupledTrainVisitedScratch.Clear();
        coupledTrainVisitedScratch.Add(this);
        UpdateCoupledTrainChain(
            this,
            leadSample,
            leadFacingTangent,
            -1,
            deltaTime,
            signedWheelDistance);
        UpdateCoupledTrainChain(
            this,
            leadSample,
            leadFacingTangent,
            1,
            deltaTime,
            signedWheelDistance);
        coupledTrainVisitedScratch.Clear();
        Physics.SyncTransforms();
    }

    private void UpdateCoupledTrainChain(
        Train leadTrain,
        RailSample leadSample,
        Vector2 leadFacingTangent,
        int chainDirection,
        float deltaTime,
        float signedWheelDistance)
    {
        Train previousTrain = leadTrain;
        RailSample previousSample = leadSample;
        Vector2 previousFacingTangent = leadFacingTangent;
        int normalizedDirection = chainDirection >= 0 ? 1 : -1;
        int maxCars = Mathf.Max(1, maxCoupledTrainCars);

        for (int i = 0; i < maxCars; i++)
        {
            bool hasLockedTrain = HasActiveCachedCoupledTrain(normalizedDirection, i);
            Train coupledTrain = null;
            RailSample coupledSample = default;
            Vector2 coupledFacingTangent = Vector2.zero;
            bool resolved = false;
            if (hasLockedTrain && Mathf.Abs(signedWheelDistance) > 0.0001f)
            {
                resolved = TryAdvanceLockedCoupledTrainFromCurrentRail(
                    previousTrain,
                    previousSample,
                    previousFacingTangent,
                    normalizedDirection,
                    i,
                    signedWheelDistance,
                    out coupledTrain,
                    out coupledSample,
                    out coupledFacingTangent);
            }

            if (!resolved)
            {
                resolved = TryResolveStableCoupledTrain(
                    previousTrain,
                    previousSample,
                    previousFacingTangent,
                    normalizedDirection,
                    i,
                    true,
                    out coupledTrain,
                    out coupledSample,
                    out coupledFacingTangent,
                    out _);
            }

            if (!resolved)
            {
                if (!hasLockedTrain)
                {
                    TrimCoupledTrainChain(normalizedDirection, i);
                }

                return;
            }

            Vector3 previousPosition = coupledTrain.transform.position;
            float movedDistance = Mathf.Sqrt(GetPlanarSqrDistance(
                previousPosition,
                new Vector3(coupledSample.Point.x, previousPosition.y, coupledSample.Point.y)));
            float coupledWheelDistance = Mathf.Abs(signedWheelDistance) > 0.0001f
                ? Mathf.Sign(signedWheelDistance) * movedDistance
                : 0f;
            coupledTrain.ApplyCoupledRailPose(
                coupledSample.Rail,
                coupledSample.DistanceAlongPath,
                coupledSample.Point,
                coupledFacingTangent,
                deltaTime,
                railRotationInterpolationSpeed,
                coupledWheelDistance);

            coupledTrainVisitedScratch.Add(coupledTrain);
            previousTrain = coupledTrain;
            previousSample = coupledSample;
            previousFacingTangent = coupledFacingTangent;
        }
    }

    private bool TryResolveStableCoupledTrain(
        Train leadTrain,
        RailSample leadSample,
        Vector2 leadFacingTangent,
        int chainDirection,
        int chainIndex,
        bool updateCache,
        out Train coupledTrain,
        out RailSample coupledSample,
        out Vector2 coupledFacingTangent,
        out bool blockedByRailLimit)
    {
        bool hasLockedTrain = HasActiveCachedCoupledTrain(chainDirection, chainIndex);
        if (TryGetCachedCoupledTrain(
                leadTrain,
                leadSample,
                leadFacingTangent,
                chainDirection,
                chainIndex,
                updateCache,
                out coupledTrain,
                out coupledSample,
                out coupledFacingTangent,
                out blockedByRailLimit))
        {
            return true;
        }

        if (hasLockedTrain)
        {
            return false;
        }

        if (TryFindBestCoupledTrain(
                leadTrain,
                leadSample,
                leadFacingTangent,
                chainDirection,
                out coupledTrain,
                out coupledSample,
                out coupledFacingTangent,
                out blockedByRailLimit))
        {
            if (updateCache)
            {
                SetCoupledTrainChainEntry(chainDirection, chainIndex, coupledTrain);
            }

            return true;
        }

        if (updateCache && !blockedByRailLimit)
        {
            TrimCoupledTrainChain(chainDirection, chainIndex);
        }

        return false;
    }

    private bool TryGetCachedCoupledTrain(
        Train leadTrain,
        RailSample leadSample,
        Vector2 leadFacingTangent,
        int chainDirection,
        int chainIndex,
        bool updateCache,
        out Train coupledTrain,
        out RailSample coupledSample,
        out Vector2 coupledFacingTangent,
        out bool blockedByRailLimit)
    {
        coupledTrain = null;
        coupledSample = default;
        coupledFacingTangent = Vector2.zero;
        blockedByRailLimit = false;

        List<Train> chain = GetCoupledTrainChain(chainDirection);
        if (chainIndex < 0 || chainIndex >= chain.Count)
        {
            return false;
        }

        Train candidate = chain[chainIndex];
        if (candidate == null || !candidate.gameObject.activeInHierarchy)
        {
            if (updateCache)
            {
                TrimCoupledTrainChain(chainDirection, chainIndex);
            }

            return false;
        }

        if (coupledTrainVisitedScratch.Contains(candidate))
        {
            return false;
        }

        float couplingDistance = ResolveCouplingDistance(leadTrain, candidate);
        bool potentialCoupledCandidate = IsPotentialCoupledTrain(
            leadTrain,
            leadSample,
            leadFacingTangent,
            chainDirection,
            candidate,
            couplingDistance);
        Vector2 candidatePoint = new Vector2(
            candidate.transform.position.x,
            candidate.transform.position.z);
        if (!TrySampleCoupledRailPosition(
                leadSample,
                leadFacingTangent,
                chainDirection,
                couplingDistance,
                candidatePoint,
                out coupledSample,
                out coupledFacingTangent))
        {
            blockedByRailLimit = potentialCoupledCandidate;
            return false;
        }

        if (!IsValidCoupledTrainCandidate(
                leadTrain,
                candidate,
                coupledSample,
                coupledFacingTangent,
                StableCoupledTrainExtraSnapDistance,
                false,
                out _,
                out _))
        {
            return false;
        }

        coupledTrain = candidate;
        return true;
    }

    private bool TryFindBestCoupledTrain(
        Train leadTrain,
        RailSample leadSample,
        Vector2 leadFacingTangent,
        int chainDirection,
        out Train coupledTrain,
        out RailSample coupledSample,
        out Vector2 coupledFacingTangent,
        out bool blockedByRailLimit)
    {
        coupledTrain = null;
        coupledSample = default;
        coupledFacingTangent = Vector2.zero;
        blockedByRailLimit = false;
        if (leadTrain == null
            || leadSample.Rail == null
            || leadFacingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        int searchCells = Mathf.CeilToInt(
            Mathf.Max(
                1f,
                leadTrain.TrainCouplingCenterDistance
                + leadTrain.TrainCouplingSnapMaxDistance
                + 1f));
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.RoundToInt(leadSample.Point.x),
            Mathf.RoundToInt(leadSample.Point.y));
        bool found = false;
        float bestScore = float.MaxValue;

        for (int offsetY = -searchCells; offsetY <= searchCells; offsetY++)
        {
            for (int offsetX = -searchCells; offsetX <= searchCells; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                coupledTrainSearchScratch.Clear();
                InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
                    coordinate,
                    coupledTrainSearchScratch);

                for (int i = 0; i < coupledTrainSearchScratch.Count; i++)
                {
                    if (coupledTrainSearchScratch[i] is not Train candidate
                        || coupledTrainVisitedScratch.Contains(candidate)
                        || !candidate.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    float couplingDistance = ResolveCouplingDistance(leadTrain, candidate);
                    bool potentialCoupledCandidate = IsPotentialCoupledTrain(
                        leadTrain,
                        leadSample,
                        leadFacingTangent,
                        chainDirection,
                        candidate,
                        couplingDistance);
                    Vector2 candidatePoint = new Vector2(
                        candidate.transform.position.x,
                        candidate.transform.position.z);
                    if (!TrySampleCoupledRailPosition(
                            leadSample,
                            leadFacingTangent,
                            chainDirection,
                            couplingDistance,
                            candidatePoint,
                            out RailSample candidateSample,
                            out Vector2 candidateFacingTangent))
                    {
                        if (potentialCoupledCandidate)
                        {
                            blockedByRailLimit = true;
                        }

                        continue;
                    }

                    if (!IsValidCoupledTrainCandidate(
                            leadTrain,
                            candidate,
                            candidateSample,
                            candidateFacingTangent,
                            0f,
                            true,
                            out float sqrDistance,
                            out float facingScore))
                    {
                        continue;
                    }

                    float score = sqrDistance - facingScore * 0.05f;
                    if (score >= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    coupledTrain = candidate;
                    coupledSample = candidateSample;
                    coupledFacingTangent = candidateFacingTangent;
                    found = true;
                }
            }
        }

        coupledTrainSearchScratch.Clear();
        return found;
    }

    private bool IsValidCoupledTrainCandidate(
        Train leadTrain,
        Train candidate,
        RailSample candidateSample,
        Vector2 candidateFacingTangent,
        float extraSnapDistance,
        bool enforceFacing,
        out float sqrDistance,
        out float facingScore)
    {
        sqrDistance = float.MaxValue;
        facingScore = 0f;
        if (leadTrain == null || candidate == null)
        {
            return false;
        }

        float maxSnapDistance = Mathf.Max(
            leadTrain.TrainCouplingSnapMaxDistance,
            candidate.TrainCouplingSnapMaxDistance)
                                + Mathf.Max(0f, extraSnapDistance);
        Vector3 candidatePosition = candidate.transform.position;
        sqrDistance = GetPlanarSqrDistance(
            candidatePosition,
            new Vector3(candidateSample.Point.x, candidatePosition.y, candidateSample.Point.y));
        if (sqrDistance > maxSnapDistance * maxSnapDistance)
        {
            return false;
        }

        Vector2 candidateForward = new Vector2(
            candidate.transform.forward.x,
            candidate.transform.forward.z);
        if (candidateForward.sqrMagnitude > 0.0001f)
        {
            candidateForward.Normalize();
            facingScore = Mathf.Abs(Vector2.Dot(candidateForward, candidateFacingTangent));
            if (enforceFacing && facingScore < coupledTrainMinFacingDot)
            {
                return false;
            }
        }

        return true;
    }

    private List<Train> GetCoupledTrainChain(int chainDirection)
    {
        return chainDirection >= 0
            ? forwardCoupledTrainChain
            : backwardCoupledTrainChain;
    }

    private void SetCoupledTrainChainEntry(int chainDirection, int chainIndex, Train train)
    {
        if (chainIndex < 0 || train == null)
        {
            return;
        }

        List<Train> chain = GetCoupledTrainChain(chainDirection);
        if (chainIndex < chain.Count && chain[chainIndex] == train)
        {
            return;
        }

        TrimCoupledTrainChain(chainDirection, chainIndex);
        while (chain.Count < chainIndex)
        {
            chain.Add(null);
        }

        if (chain.Count == chainIndex)
        {
            chain.Add(train);
        }
        else
        {
            chain[chainIndex] = train;
        }
    }

    private void TrimCoupledTrainChain(int chainDirection, int startIndex)
    {
        List<Train> chain = GetCoupledTrainChain(chainDirection);
        if (startIndex <= 0)
        {
            chain.Clear();
            return;
        }

        if (startIndex < chain.Count)
        {
            chain.RemoveRange(startIndex, chain.Count - startIndex);
        }
    }

    private bool HasActiveCachedCoupledTrain(int chainDirection, int chainIndex)
    {
        List<Train> chain = GetCoupledTrainChain(chainDirection);
        if (chainIndex < 0 || chainIndex >= chain.Count)
        {
            return false;
        }

        Train train = chain[chainIndex];
        return train != null && train.gameObject.activeInHierarchy;
    }

    private bool TryAdvanceLockedCoupledTrainFromCurrentRail(
        Train leadTrain,
        RailSample leadSample,
        Vector2 leadFacingTangent,
        int chainDirection,
        int chainIndex,
        float signedWheelDistance,
        out Train coupledTrain,
        out RailSample coupledSample,
        out Vector2 coupledFacingTangent)
    {
        coupledTrain = null;
        coupledSample = default;
        coupledFacingTangent = Vector2.zero;
        if (leadTrain == null
            || leadSample.Rail == null
            || leadFacingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        List<Train> chain = GetCoupledTrainChain(chainDirection);
        if (chainIndex < 0 || chainIndex >= chain.Count)
        {
            return false;
        }

        Train candidate = chain[chainIndex];
        if (candidate == null
            || !candidate.gameObject.activeInHierarchy
            || coupledTrainVisitedScratch.Contains(candidate))
        {
            return false;
        }

        Vector2 candidatePoint = new Vector2(
            candidate.transform.position.x,
            candidate.transform.position.z);
        Vector2 candidateForward = new Vector2(
            candidate.transform.forward.x,
            candidate.transform.forward.z);
        if (candidateForward.sqrMagnitude <= 0.0001f)
        {
            candidateForward = leadFacingTangent;
        }

        float maxDistance = Mathf.Max(
            railSnapMaxDistance,
            candidate.TrainCouplingSnapMaxDistance);
        RailSample currentCandidateSample;
        if (!TryGetTrainCurrentRailSample(
                candidate,
                candidatePoint,
                maxDistance * maxDistance,
                out currentCandidateSample)
            && !TryFindBestRailSample(
                candidatePoint,
                candidateForward.normalized,
                maxDistance * maxDistance,
                out currentCandidateSample))
        {
            return false;
        }

        Vector2 currentFacingTangent = ResolveCoupledFacingTangent(
            currentCandidateSample.Tangent,
            leadFacingTangent);
        float moveDistance = Mathf.Abs(signedWheelDistance);
        if (moveDistance <= 0.0001f)
        {
            coupledTrain = candidate;
            coupledSample = currentCandidateSample;
            coupledFacingTangent = currentFacingTangent;
            return IsCoupledTrainDistanceAcceptable(
                leadTrain,
                candidate,
                leadSample,
                coupledSample);
        }

        float tangentDot = Vector2.Dot(currentFacingTangent, currentCandidateSample.Tangent);
        if (Mathf.Abs(tangentDot) <= 0.0001f)
        {
            tangentDot = 1f;
        }

        float candidateSignedStep = Mathf.Sign(signedWheelDistance)
                                    * Mathf.Sign(tangentDot)
                                    * moveDistance;
        if (!TryAdvanceAlongRailNetwork(
                currentCandidateSample,
                candidateSignedStep,
                leadSample.Point,
                out coupledSample,
                out float traveledDistance)
            || traveledDistance + 0.0001f < moveDistance)
        {
            return false;
        }

        coupledTrain = candidate;
        coupledFacingTangent = ResolveCoupledFacingTangent(
            coupledSample.Tangent,
            leadFacingTangent);
        return IsCoupledTrainDistanceAcceptable(
            leadTrain,
            candidate,
            leadSample,
            coupledSample);
    }

    private static bool IsCoupledTrainDistanceAcceptable(
        Train leadTrain,
        Train candidate,
        RailSample leadSample,
        RailSample candidateSample)
    {
        if (leadTrain == null || candidate == null)
        {
            return false;
        }

        float couplingDistance = ResolveCouplingDistance(leadTrain, candidate);
        float maxDistance = couplingDistance
                            + Mathf.Max(
                                leadTrain.TrainCouplingSnapMaxDistance,
                                candidate.TrainCouplingSnapMaxDistance)
                            + StableCoupledTrainExtraSnapDistance;
        return (leadSample.Point - candidateSample.Point).sqrMagnitude <= maxDistance * maxDistance;
    }

    private bool IsPotentialCoupledTrain(
        Train leadTrain,
        RailSample leadSample,
        Vector2 leadFacingTangent,
        int chainDirection,
        Train candidate,
        float couplingDistance)
    {
        if (leadTrain == null
            || candidate == null
            || leadSample.Rail == null
            || leadFacingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        leadFacingTangent.Normalize();
        Vector2 candidatePoint = new Vector2(
            candidate.transform.position.x,
            candidate.transform.position.z);
        Vector2 delta = candidatePoint - leadSample.Point;
        if (delta.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        float sideProjection = Vector2.Dot(delta, leadFacingTangent);
        if (chainDirection >= 0)
        {
            if (sideProjection < -0.05f)
            {
                return false;
            }
        }
        else if (sideProjection > 0.05f)
        {
            return false;
        }

        float maxSnapDistance = Mathf.Max(
            leadTrain.TrainCouplingSnapMaxDistance,
            candidate.TrainCouplingSnapMaxDistance);
        float maxCoupledDistance = Mathf.Max(0.1f, couplingDistance)
                                   + Mathf.Max(0.05f, maxSnapDistance)
                                   + 0.25f;
        return delta.sqrMagnitude <= maxCoupledDistance * maxCoupledDistance;
    }

    private bool TrySampleCoupledRailPosition(
        RailSample leadSample,
        Vector2 leadFacingTangent,
        int chainDirection,
        float couplingDistance,
        Vector2? connectionPreferencePoint,
        out RailSample coupledSample,
        out Vector2 coupledFacingTangent)
    {
        coupledSample = default;
        coupledFacingTangent = Vector2.zero;
        if (leadSample.Rail == null || leadFacingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        leadFacingTangent.Normalize();
        float tangentDot = Vector2.Dot(leadFacingTangent, leadSample.Tangent);
        if (Mathf.Abs(tangentDot) <= 0.0001f)
        {
            tangentDot = 1f;
        }

        float signedStep = (chainDirection >= 0 ? 1f : -1f)
                           * Mathf.Sign(tangentDot)
                           * Mathf.Max(0.1f, couplingDistance);
        if (!TryAdvanceAlongRailNetwork(
                leadSample,
                signedStep,
                connectionPreferencePoint,
                out coupledSample,
                out float traveledDistance)
            || traveledDistance + 0.0001f < Mathf.Abs(signedStep))
        {
            return false;
        }

        coupledFacingTangent = ResolveCoupledFacingTangent(
            coupledSample.Tangent,
            leadFacingTangent);
        return coupledFacingTangent.sqrMagnitude > 0.0001f;
    }

    private static Vector2 ResolveCoupledFacingTangent(Vector2 railTangent, Vector2 referenceFacing)
    {
        if (railTangent.sqrMagnitude <= 0.0001f)
        {
            return referenceFacing.sqrMagnitude > 0.0001f
                ? referenceFacing.normalized
                : Vector2.up;
        }

        railTangent.Normalize();
        if (referenceFacing.sqrMagnitude > 0.0001f
            && Vector2.Dot(railTangent, referenceFacing.normalized) < 0f)
        {
            railTangent = -railTangent;
        }

        return railTangent;
    }

    private static float ResolveCouplingDistance(Train a, Train b)
    {
        if (a == null || b == null)
        {
            return 1f;
        }

        return a.ResolveCouplingCenterDistance(b);
    }

    private static float GetPlanarSqrDistance(Vector3 a, Vector3 b)
    {
        float deltaX = a.x - b.x;
        float deltaZ = a.z - b.z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }

    private void SnapToNearestRailIfPossible()
    {
        Vector2 currentPoint = new Vector2(transform.position.x, transform.position.z);
        if (TryGetCurrentRailSample(
                currentPoint,
                railSnapMaxDistance * railSnapMaxDistance,
                out RailSample currentSample))
        {
            ApplyRailPose(currentSample, ResolveFacingTangent(currentSample.Tangent));
            return;
        }

        Vector2 preferredDirection = currentRailTangent.sqrMagnitude > 0.0001f
            ? currentRailTangent
            : new Vector2(transform.forward.x, transform.forward.z);
        if (preferredDirection.sqrMagnitude <= 0.0001f)
        {
            preferredDirection = Vector2.up;
        }

        if (TryFindBestRailSample(
                currentPoint,
                preferredDirection.normalized,
                railSnapMaxDistance * railSnapMaxDistance,
                out RailSample sample))
        {
            ApplyRailPose(sample, ResolveFacingTangent(sample.Tangent));
        }
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
        float signedStep,
        out RailSample targetSample,
        out float traveledDistance)
    {
        return TryAdvanceAlongRailNetwork(
            startSample,
            signedStep,
            null,
            out targetSample,
            out traveledDistance);
    }

    private bool TryAdvanceAlongRailNetwork(
        RailSample startSample,
        float signedStep,
        Vector2? connectionPreferencePoint,
        out RailSample targetSample,
        out float traveledDistance)
    {
        targetSample = startSample;
        traveledDistance = 0f;
        float remainingDistance = Mathf.Abs(signedStep);
        if (remainingDistance <= 0.0001f)
        {
            return true;
        }

        RailSample currentSample = startSample;
        Vector2 travelDirection = signedStep >= 0f ? currentSample.Tangent : -currentSample.Tangent;
        Railload excludedInternalRail = null;
        if (travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

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
                travelDot = signedStep >= 0f ? 1f : -1f;
            }

            float directionSign = Mathf.Sign(travelDot);
            float availableDistance = directionSign > 0f
                ? pathLength - currentSample.DistanceAlongPath
                : currentSample.DistanceAlongPath;
            if (connectionPreferencePoint.HasValue
                && TryFindPreferredInternalConnectedRailSample(
                    currentSample,
                    directionSign,
                    travelDirection,
                    remainingDistance,
                    availableDistance,
                    pathLength,
                    excludedInternalRail,
                    connectionPreferencePoint.Value,
                    out RailSample internalJunctionSample,
                    out RailSample internalConnectedSample,
                    out float distanceToInternalJunction))
            {
                targetSample = internalJunctionSample;
                traveledDistance += distanceToInternalJunction;
                remainingDistance -= distanceToInternalJunction;
                if (remainingDistance <= 0.0001f)
                {
                    return true;
                }

                excludedInternalRail = currentSample.Rail;
                currentSample = internalConnectedSample;
                travelDirection = ResolveConnectedTravelDirection(
                    currentSample,
                    travelDirection,
                    connectionPreferencePoint);
                continue;
            }

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
                    connectionPreferencePoint,
                    out RailSample connectedSample))
            {
                return true;
            }

            currentSample = connectedSample;
            excludedInternalRail = endpointSample.Rail;
            travelDirection = ResolveConnectedTravelDirection(
                currentSample,
                exitDirection,
                connectionPreferencePoint);
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

    private bool TryGetTrainCurrentRailSample(
        Train train,
        Vector2 currentPoint,
        float maxSqrDistance,
        out RailSample sample)
    {
        sample = default;
        if (train == null
            || !train.TryGetCurrentRailSample(
                currentPoint,
                maxSqrDistance,
                out Railload rail,
                out float distanceAlongPath,
                out Vector2 pathPoint,
                out Vector2 tangent,
                out float sqrDistance))
        {
            return false;
        }

        sample.Rail = rail;
        sample.DistanceAlongPath = distanceAlongPath;
        sample.Point = pathPoint;
        sample.Tangent = tangent;
        sample.SqrDistance = sqrDistance;
        return sample.Rail != null;
    }

    private bool TryFindPreferredInternalConnectedRailSample(
        RailSample currentSample,
        float directionSign,
        Vector2 travelDirection,
        float remainingDistance,
        float availableDistance,
        float currentPathLength,
        Railload excludedRail,
        Vector2 preferencePoint,
        out RailSample junctionSample,
        out RailSample connectedSample,
        out float distanceToJunction)
    {
        junctionSample = default;
        connectedSample = default;
        distanceToJunction = 0f;
        if (currentSample.Rail == null
            || travelDirection.sqrMagnitude <= 0.0001f
            || remainingDistance <= 0.0001f
            || availableDistance <= 0.0001f)
        {
            return false;
        }

        float maxSearchDistance = Mathf.Min(remainingDistance, availableDistance);
        if (maxSearchDistance <= 0.03f)
        {
            return false;
        }

        travelDirection.Normalize();
        railCandidateScratch.Clear();
        AddRailCandidates(currentSample.Point);
        AddRailCandidates(currentSample.Point + travelDirection * Mathf.Min(maxSearchDistance, railSearchRadius));
        AddRailCandidates(preferencePoint);

        bool found = false;
        float bestScore = 0.01f;
        float maxConnectionDistance = ResolveInternalConnectionMaxDistance();
        float maxConnectionSqrDistance = maxConnectionDistance * maxConnectionDistance;
        for (int i = 0; i < railCandidateScratch.Count; i++)
        {
            Railload rail = railCandidateScratch[i];
            if (rail == null
                || rail == currentSample.Rail
                || rail == excludedRail)
            {
                continue;
            }

            if (TryUpdatePreferredInternalConnection(
                    currentSample,
                    directionSign,
                    travelDirection,
                    remainingDistance,
                    maxSearchDistance,
                    currentPathLength,
                    rail,
                    true,
                    preferencePoint,
                    maxConnectionSqrDistance,
                    ref bestScore,
                    ref junctionSample,
                    ref connectedSample,
                    ref distanceToJunction))
            {
                found = true;
            }

            if (TryUpdatePreferredInternalConnection(
                    currentSample,
                    directionSign,
                    travelDirection,
                    remainingDistance,
                    maxSearchDistance,
                    currentPathLength,
                    rail,
                    false,
                    preferencePoint,
                    maxConnectionSqrDistance,
                    ref bestScore,
                    ref junctionSample,
                    ref connectedSample,
                    ref distanceToJunction))
            {
                found = true;
            }
        }

        railCandidateScratch.Clear();
        railSearchScratch.Clear();
        return found;
    }

    private bool TryUpdatePreferredInternalConnection(
        RailSample currentSample,
        float directionSign,
        Vector2 travelDirection,
        float remainingDistance,
        float maxSearchDistance,
        float currentPathLength,
        Railload candidateRail,
        bool startEndpoint,
        Vector2 preferencePoint,
        float maxConnectionSqrDistance,
        ref float bestScore,
        ref RailSample bestJunctionSample,
        ref RailSample bestConnectedSample,
        ref float bestDistanceToJunction)
    {
        if (currentSample.Rail == null
            || candidateRail == null
            || !candidateRail.TryGetRenderedEndpointSample(
                startEndpoint,
                out float candidateEndpointDistance,
                out Vector2 candidateEndpointPoint,
                out _)
            || !currentSample.Rail.TryFindNearestRenderedPathSample(
                candidateEndpointPoint,
                out float currentConnectionDistance,
                out _,
                out _,
                out float connectionSqrDistance)
            || connectionSqrDistance > maxConnectionSqrDistance)
        {
            return false;
        }

        float distanceToConnection =
            (currentConnectionDistance - currentSample.DistanceAlongPath) * directionSign;
        float backtrackTolerance = Mathf.Max(0.02f, ResolveInternalConnectionMaxDistance() * 0.75f);
        if (distanceToConnection < -backtrackTolerance
            || distanceToConnection > maxSearchDistance + 0.0001f)
        {
            return false;
        }

        distanceToConnection = Mathf.Max(0f, distanceToConnection);
        float remainingAfterConnection = remainingDistance - distanceToConnection;
        if (remainingAfterConnection <= 0.0001f
            || !TryCreateRailSampleAtDistance(currentSample.Rail, currentConnectionDistance, out RailSample candidateJunctionSample)
            || !TryCreateRailSampleAtDistance(candidateRail, candidateEndpointDistance, out RailSample candidateConnectedSample)
            || !TryResolvePreferenceSqrOnRail(
                currentSample.Rail,
                currentConnectionDistance,
                directionSign,
                remainingAfterConnection,
                currentPathLength,
                preferencePoint,
                out float currentPreferenceSqr)
            || !candidateRail.TryGetRenderedPathLength(out float candidatePathLength))
        {
            return false;
        }

        float candidateDirectionSign = ResolveDirectionSignTowardPreference(
            candidateConnectedSample.Tangent,
            travelDirection,
            candidateConnectedSample.Point,
            preferencePoint);
        if (!TryResolvePreferenceSqrOnRail(
                candidateRail,
                candidateEndpointDistance,
                candidateDirectionSign,
                remainingAfterConnection,
                candidatePathLength,
                preferencePoint,
                out float candidatePreferenceSqr))
        {
            return false;
        }

        float directionScore = candidateConnectedSample.Tangent.sqrMagnitude > 0.0001f
            ? Mathf.Abs(Vector2.Dot(travelDirection, candidateConnectedSample.Tangent.normalized))
            : 0f;
        float score = currentPreferenceSqr - candidatePreferenceSqr
                      + directionScore * 0.05f
                      - connectionSqrDistance * 2f;
        if (score <= bestScore)
        {
            return false;
        }

        bestScore = score;
        bestJunctionSample = candidateJunctionSample;
        bestConnectedSample = candidateConnectedSample;
        bestDistanceToJunction = distanceToConnection;
        return true;
    }

    private static bool TryResolvePreferenceSqrOnRail(
        Railload rail,
        float distanceAlongPath,
        float directionSign,
        float moveDistance,
        float pathLength,
        Vector2 preferencePoint,
        out float sqrDistance)
    {
        sqrDistance = float.MaxValue;
        if (rail == null)
        {
            return false;
        }

        float sampleDistance = Mathf.Clamp(
            distanceAlongPath + Mathf.Sign(directionSign) * Mathf.Max(0f, moveDistance),
            0f,
            pathLength);
        if (!rail.TrySampleRenderedPath(sampleDistance, out Vector2 samplePoint, out _))
        {
            return false;
        }

        sqrDistance = (samplePoint - preferencePoint).sqrMagnitude;
        return true;
    }

    private static float ResolveDirectionSignTowardPreference(
        Vector2 tangent,
        Vector2 fallbackDirection,
        Vector2 originPoint,
        Vector2 preferencePoint)
    {
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            return 1f;
        }

        tangent.Normalize();
        Vector2 preferenceDirection = preferencePoint - originPoint;
        if (preferenceDirection.sqrMagnitude > 0.0001f)
        {
            float preferenceDot = Vector2.Dot(tangent, preferenceDirection.normalized);
            if (Mathf.Abs(preferenceDot) > 0.05f)
            {
                return Mathf.Sign(preferenceDot);
            }
        }

        if (fallbackDirection.sqrMagnitude > 0.0001f)
        {
            float fallbackDot = Vector2.Dot(tangent, fallbackDirection.normalized);
            if (Mathf.Abs(fallbackDot) > 0.05f)
            {
                return Mathf.Sign(fallbackDot);
            }
        }

        return 1f;
    }

    private static Vector2 ResolveConnectedTravelDirection(
        RailSample connectedSample,
        Vector2 fallbackDirection,
        Vector2? preferencePoint)
    {
        Vector2 travelDirection = connectedSample.Tangent;
        if (travelDirection.sqrMagnitude <= 0.0001f)
        {
            travelDirection = fallbackDirection;
        }

        if (travelDirection.sqrMagnitude <= 0.0001f)
        {
            return Vector2.up;
        }

        travelDirection.Normalize();
        if (preferencePoint.HasValue)
        {
            Vector2 preferenceDirection = preferencePoint.Value - connectedSample.Point;
            if (preferenceDirection.sqrMagnitude > 0.0001f
                && Vector2.Dot(travelDirection, preferenceDirection.normalized) < 0f)
            {
                travelDirection = -travelDirection;
            }
        }
        else if (fallbackDirection.sqrMagnitude > 0.0001f
                 && Vector2.Dot(travelDirection, fallbackDirection.normalized) < 0f)
        {
            travelDirection = -travelDirection;
        }

        return travelDirection;
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
        float currentScore = ResolveBranchSelectionScore(
            currentInputDot,
            currentProgress,
            0f);

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
        Vector2? connectionPreferencePoint,
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
        if (connectionPreferencePoint.HasValue)
        {
            AddRailCandidates(connectionPreferencePoint.Value);
        }

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
            float preferenceScore = connectionPreferencePoint.HasValue
                ? ResolveConnectionPreferenceScore(
                    rail,
                    distanceAlongPath,
                    tangent,
                    endpointSample.Point,
                    exitDirection,
                    connectionPreferencePoint.Value)
                : 0f;
            if (progress <= 0.01f
                && directionScore < 0.12f
                && preferenceScore <= 0.01f)
            {
                continue;
            }

            float score = progress * 1.2f
                          + directionScore * 0.6f
                          + preferenceScore
                          - sqrDistance * 3f;

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

    private float ResolveConnectionPreferenceScore(
        Railload rail,
        float distanceAlongPath,
        Vector2 tangent,
        Vector2 endpointPoint,
        Vector2 exitDirection,
        Vector2 preferencePoint)
    {
        if (rail == null
            || tangent.sqrMagnitude <= 0.0001f
            || exitDirection.sqrMagnitude <= 0.0001f
            || !rail.TryGetRenderedPathLength(out float pathLength))
        {
            return 0f;
        }

        tangent.Normalize();
        exitDirection.Normalize();
        float directionSign = Mathf.Sign(Vector2.Dot(exitDirection, tangent));
        if (Mathf.Abs(directionSign) <= 0.0001f)
        {
            directionSign = 1f;
        }

        float preferenceDistance = Mathf.Clamp(
            (preferencePoint - endpointPoint).magnitude,
            Mathf.Max(railConnectionLookAhead, 0.05f),
            Mathf.Max(railConnectionLookAhead, pathLength));
        float sampleDistance = Mathf.Clamp(
            distanceAlongPath + directionSign * preferenceDistance,
            0f,
            pathLength);
        if (!rail.TrySampleRenderedPath(sampleDistance, out Vector2 futurePoint, out _))
        {
            return 0f;
        }

        float currentSqrDistanceToPreference = (endpointPoint - preferencePoint).sqrMagnitude;
        float sqrDistanceToPreference = (futurePoint - preferencePoint).sqrMagnitude;
        Vector2 preferenceDirection = preferencePoint - endpointPoint;
        float directionScore = preferenceDirection.sqrMagnitude > 0.0001f
            ? Mathf.Max(0f, Vector2.Dot(exitDirection, preferenceDirection.normalized))
            : 0f;
        return (currentSqrDistanceToPreference - sqrDistanceToPreference) * CoupledConnectionPreferenceWeight
               + directionScore * 0.5f;
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
        if (railTangent.sqrMagnitude <= 0.0001f)
        {
            return currentFacing.sqrMagnitude > 0.0001f
                ? currentFacing.normalized
                : Vector2.up;
        }

        railTangent.Normalize();
        if (currentFacing.sqrMagnitude > 0.0001f
            && Vector2.Dot(railTangent, currentFacing.normalized) < 0f)
        {
            railTangent = -railTangent;
        }

        return railTangent;
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
