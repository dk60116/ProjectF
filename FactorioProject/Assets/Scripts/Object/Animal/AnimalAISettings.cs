using System;
using UnityEngine;

[Serializable]
public sealed class AnimalAISettings
{
    public const float DefaultHerdAreaRadius = 30f;
    public const float DefaultMoveSpeed = 0.625f;
    public const float DefaultRunSpeedRatio = 1.5f;
    public const float DefaultAccelerationPerSecond = 0.5f;
    public const float DefaultDecelerationPerSecond = 2f;
    public const float DefaultTurnSpeed = 220f;
    public const float DefaultLookAroundWeight = 2f;
    public const float DefaultFleeSafeDistance = 12f;
    public const float DefaultNearbyThreatRadius = 8f;
    public const float DefaultFleeSpeedMultiplier = 1.5f;
    public static readonly Vector2 DefaultLookAroundDuration = new Vector2(2f, 5f);

    [Header("Herd Area")]
    [SerializeField, Min(1f)] private float herdAreaRadius = DefaultHerdAreaRadius;
    [SerializeField, Min(0.1f)] private float separationRadius = 1.25f;
    [SerializeField, Min(0f)] private float separationWeight = 1.5f;
    [SerializeField, Min(0f)] private float cohesionWeight = 0.65f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = DefaultMoveSpeed;
    [SerializeField, Min(0f)] private float runSpeedRatio = DefaultRunSpeedRatio;
    [SerializeField, Min(0.01f)] private float accelerationPerSecond = DefaultAccelerationPerSecond;
    [SerializeField, Min(0.01f)] private float decelerationPerSecond = DefaultDecelerationPerSecond;
    [SerializeField, Min(0f)] private float turnSpeed = DefaultTurnSpeed;
    [SerializeField, Min(0.1f)] private float obstacleProbeDistance = 1.5f;
    [SerializeField, Min(0.05f)] private float arrivalDistance = 0.35f;

    [Header("Threat Response")]
    [SerializeField, Min(1f)] private float fleeSafeDistance = DefaultFleeSafeDistance;
    [SerializeField, Min(0f)] private float nearbyThreatRadius = DefaultNearbyThreatRadius;
    [SerializeField, Min(0.1f)] private float fleeSpeedMultiplier = DefaultFleeSpeedMultiplier;

    [Header("Age And Gender")]
    [SerializeField, Range(0.1f, 2f)] private float youngSpeedMultiplier = 0.8f;
    [SerializeField, Range(0.1f, 2f)] private float maleSpeedMultiplier = 1.05f;
    [SerializeField, Range(0.1f, 2f)] private float femaleSpeedMultiplier = 1f;
    [SerializeField, Range(0.1f, 3f)] private float youngWanderWeightMultiplier = 1.35f;
    [SerializeField, Range(0.1f, 3f)] private float youngRestWeightMultiplier = 1.25f;

    [Header("Behavior Weights")]
    [SerializeField, Min(0f)] private float idleWeight = 2f;
    [SerializeField, Min(0f)] private float lookAroundWeight = DefaultLookAroundWeight;
    [SerializeField, Min(0f)] private float wanderWeight = 4f;
    [SerializeField, Min(0f)] private float grazeWeight = 3f;
    [SerializeField, Min(0f)] private float drinkWeight = 1f;
    [SerializeField, Min(0f)] private float restWeight = 1.5f;

    [Header("Behavior Durations")]
    [SerializeField] private Vector2 idleDuration = new Vector2(2f, 6f);
    [SerializeField] private Vector2 lookAroundDuration = DefaultLookAroundDuration;
    [SerializeField] private Vector2 wanderDuration = new Vector2(4f, 10f);
    [SerializeField] private Vector2 grazeDuration = new Vector2(4f, 8f);
    [SerializeField] private Vector2 drinkDuration = new Vector2(3f, 6f);
    [SerializeField] private Vector2 restDuration = new Vector2(6f, 14f);

    public float HerdAreaRadius
    {
        get => Mathf.Max(1f, herdAreaRadius);
        set => herdAreaRadius = Mathf.Max(1f, value);
    }

    public float SeparationRadius
    {
        get => Mathf.Max(0.1f, separationRadius);
        set => separationRadius = Mathf.Max(0.1f, value);
    }

    public float SeparationWeight
    {
        get => Mathf.Max(0f, separationWeight);
        set => separationWeight = Mathf.Max(0f, value);
    }

    public float CohesionWeight
    {
        get => Mathf.Max(0f, cohesionWeight);
        set => cohesionWeight = Mathf.Max(0f, value);
    }

    public float MoveSpeed
    {
        get => Mathf.Max(0f, moveSpeed);
        set => moveSpeed = Mathf.Max(0f, value);
    }

    public float RunSpeedRatio
    {
        get => Mathf.Max(0f, runSpeedRatio);
        set => runSpeedRatio = Mathf.Max(0f, value);
    }

    public float AccelerationPerSecond
    {
        get => Mathf.Max(0.01f, accelerationPerSecond);
        set => accelerationPerSecond = Mathf.Max(0.01f, value);
    }

    public float DecelerationPerSecond
    {
        get => Mathf.Max(0.01f, decelerationPerSecond);
        set => decelerationPerSecond = Mathf.Max(0.01f, value);
    }

    public float TurnSpeed
    {
        get => Mathf.Max(0f, turnSpeed);
        set => turnSpeed = Mathf.Max(0f, value);
    }

    public float ObstacleProbeDistance
    {
        get => Mathf.Max(0.1f, obstacleProbeDistance);
        set => obstacleProbeDistance = Mathf.Max(0.1f, value);
    }

    public float ArrivalDistance
    {
        get => Mathf.Max(0.05f, arrivalDistance);
        set => arrivalDistance = Mathf.Max(0.05f, value);
    }

    public float FleeSafeDistance
    {
        get => Mathf.Max(1f, fleeSafeDistance);
        set => fleeSafeDistance = Mathf.Max(1f, value);
    }

    public float NearbyThreatRadius
    {
        get => Mathf.Max(0f, nearbyThreatRadius);
        set => nearbyThreatRadius = Mathf.Max(0f, value);
    }

    public float FleeSpeedMultiplier
    {
        get => Mathf.Max(0.1f, fleeSpeedMultiplier);
        set => fleeSpeedMultiplier = Mathf.Max(0.1f, value);
    }

    public float YoungSpeedMultiplier
    {
        get => Mathf.Clamp(youngSpeedMultiplier, 0.1f, 2f);
        set => youngSpeedMultiplier = Mathf.Clamp(value, 0.1f, 2f);
    }

    public float MaleSpeedMultiplier
    {
        get => Mathf.Clamp(maleSpeedMultiplier, 0.1f, 2f);
        set => maleSpeedMultiplier = Mathf.Clamp(value, 0.1f, 2f);
    }

    public float FemaleSpeedMultiplier
    {
        get => Mathf.Clamp(femaleSpeedMultiplier, 0.1f, 2f);
        set => femaleSpeedMultiplier = Mathf.Clamp(value, 0.1f, 2f);
    }

    public float YoungWanderWeightMultiplier
    {
        get => Mathf.Clamp(youngWanderWeightMultiplier, 0.1f, 3f);
        set => youngWanderWeightMultiplier = Mathf.Clamp(value, 0.1f, 3f);
    }

    public float YoungRestWeightMultiplier
    {
        get => Mathf.Clamp(youngRestWeightMultiplier, 0.1f, 3f);
        set => youngRestWeightMultiplier = Mathf.Clamp(value, 0.1f, 3f);
    }

    public float IdleWeight
    {
        get => Mathf.Max(0f, idleWeight);
        set => idleWeight = Mathf.Max(0f, value);
    }

    public float WanderWeight
    {
        get => Mathf.Max(0f, wanderWeight);
        set => wanderWeight = Mathf.Max(0f, value);
    }

    public float LookAroundWeight
    {
        get => Mathf.Max(0f, lookAroundWeight);
        set => lookAroundWeight = Mathf.Max(0f, value);
    }

    public float GrazeWeight
    {
        get => Mathf.Max(0f, grazeWeight);
        set => grazeWeight = Mathf.Max(0f, value);
    }

    public float DrinkWeight
    {
        get => Mathf.Max(0f, drinkWeight);
        set => drinkWeight = Mathf.Max(0f, value);
    }

    public float RestWeight
    {
        get => Mathf.Max(0f, restWeight);
        set => restWeight = Mathf.Max(0f, value);
    }

    public Vector2 IdleDuration
    {
        get => NormalizeDuration(idleDuration);
        set => idleDuration = NormalizeDuration(value);
    }

    public Vector2 WanderDuration
    {
        get => NormalizeDuration(wanderDuration);
        set => wanderDuration = NormalizeDuration(value);
    }

    public Vector2 LookAroundDuration
    {
        get => NormalizeDuration(lookAroundDuration);
        set => lookAroundDuration = NormalizeDuration(value);
    }

    public Vector2 GrazeDuration
    {
        get => NormalizeDuration(grazeDuration);
        set => grazeDuration = NormalizeDuration(value);
    }

    public Vector2 DrinkDuration
    {
        get => NormalizeDuration(drinkDuration);
        set => drinkDuration = NormalizeDuration(value);
    }

    public Vector2 RestDuration
    {
        get => NormalizeDuration(restDuration);
        set => restDuration = NormalizeDuration(value);
    }

    public AnimalAISettings Clone()
    {
        return new AnimalAISettings
        {
            herdAreaRadius = HerdAreaRadius,
            separationRadius = SeparationRadius,
            separationWeight = SeparationWeight,
            cohesionWeight = CohesionWeight,
            moveSpeed = MoveSpeed,
            runSpeedRatio = RunSpeedRatio,
            accelerationPerSecond = AccelerationPerSecond,
            decelerationPerSecond = DecelerationPerSecond,
            turnSpeed = TurnSpeed,
            obstacleProbeDistance = ObstacleProbeDistance,
            arrivalDistance = ArrivalDistance,
            fleeSafeDistance = FleeSafeDistance,
            nearbyThreatRadius = NearbyThreatRadius,
            fleeSpeedMultiplier = FleeSpeedMultiplier,
            youngSpeedMultiplier = YoungSpeedMultiplier,
            maleSpeedMultiplier = MaleSpeedMultiplier,
            femaleSpeedMultiplier = FemaleSpeedMultiplier,
            youngWanderWeightMultiplier = YoungWanderWeightMultiplier,
            youngRestWeightMultiplier = YoungRestWeightMultiplier,
            idleWeight = IdleWeight,
            lookAroundWeight = LookAroundWeight,
            wanderWeight = WanderWeight,
            grazeWeight = GrazeWeight,
            drinkWeight = DrinkWeight,
            restWeight = RestWeight,
            idleDuration = IdleDuration,
            lookAroundDuration = LookAroundDuration,
            wanderDuration = WanderDuration,
            grazeDuration = GrazeDuration,
            drinkDuration = DrinkDuration,
            restDuration = RestDuration
        };
    }

    public void Normalize()
    {
        HerdAreaRadius = herdAreaRadius;
        SeparationRadius = separationRadius;
        SeparationWeight = separationWeight;
        CohesionWeight = cohesionWeight;
        MoveSpeed = moveSpeed;
        RunSpeedRatio = runSpeedRatio;
        AccelerationPerSecond = accelerationPerSecond;
        DecelerationPerSecond = decelerationPerSecond;
        TurnSpeed = turnSpeed;
        ObstacleProbeDistance = obstacleProbeDistance;
        ArrivalDistance = arrivalDistance;
        FleeSafeDistance = fleeSafeDistance;
        NearbyThreatRadius = nearbyThreatRadius;
        FleeSpeedMultiplier = fleeSpeedMultiplier;
        YoungSpeedMultiplier = youngSpeedMultiplier;
        MaleSpeedMultiplier = maleSpeedMultiplier;
        FemaleSpeedMultiplier = femaleSpeedMultiplier;
        YoungWanderWeightMultiplier = youngWanderWeightMultiplier;
        YoungRestWeightMultiplier = youngRestWeightMultiplier;
        IdleWeight = idleWeight;
        LookAroundWeight = lookAroundWeight;
        WanderWeight = wanderWeight;
        GrazeWeight = grazeWeight;
        DrinkWeight = drinkWeight;
        RestWeight = restWeight;
        IdleDuration = idleDuration;
        LookAroundDuration = lookAroundDuration;
        WanderDuration = wanderDuration;
        GrazeDuration = grazeDuration;
        DrinkDuration = drinkDuration;
        RestDuration = restDuration;
    }

    private static Vector2 NormalizeDuration(Vector2 value)
    {
        float minimum = Mathf.Max(0.1f, value.x);
        return new Vector2(minimum, Mathf.Max(minimum, value.y));
    }
}
