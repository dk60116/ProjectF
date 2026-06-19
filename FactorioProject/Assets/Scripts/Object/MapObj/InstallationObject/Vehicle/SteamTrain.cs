using UnityEngine;

public class SteamTrain : RailHandcar
{
    private const float MovementParticleMinDistanceSqr = 0.000001f;

    private Vector3 lastMovementParticlePosition;
    private bool hasLastMovementParticlePosition;
    private int lastDrivenInputFrame = -1;

    protected override void OnEnable()
    {
        base.OnEnable();
        ResetMovementParticleState();
    }

    protected override void OnDisable()
    {
        StopMovementParticle(true);
        hasLastMovementParticlePosition = false;
        lastDrivenInputFrame = -1;
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        StopMovementParticle(true);
        hasLastMovementParticlePosition = false;
        lastDrivenInputFrame = -1;
        base.PrepareForPool();
    }

    public override void HandleMountedInput(Vector3 worldMoveDirection, float moveSpeed, float deltaTime)
    {
        lastDrivenInputFrame = Time.frameCount;
        base.HandleMountedInput(worldMoveDirection, moveSpeed, deltaTime);
    }

    private void LateUpdate()
    {
        Vector3 currentPosition = transform.position;
        if (!hasLastMovementParticlePosition)
        {
            lastMovementParticlePosition = currentPosition;
            hasLastMovementParticlePosition = true;
            StopMovementParticle(false);
            return;
        }

        bool isDrivenThisFrame = lastDrivenInputFrame == Time.frameCount;
        bool isDrivenAndMoving = isDrivenThisFrame
                                 && GetPlanarDistanceSqr(lastMovementParticlePosition, currentPosition)
                                 > MovementParticleMinDistanceSqr;
        SetMovementParticleActive(isDrivenAndMoving);
        lastMovementParticlePosition = currentPosition;
    }

    private void ResetMovementParticleState()
    {
        lastMovementParticlePosition = transform.position;
        hasLastMovementParticlePosition = true;
        StopMovementParticle(true);
    }

    private void SetMovementParticleActive(bool isMoving)
    {
        if (particleEffect == null)
        {
            return;
        }

        if (isMoving)
        {
            if (!particleEffect.isEmitting)
            {
                particleEffect.Play(true);
            }

            return;
        }

        StopMovementParticle(false);
    }

    private void StopMovementParticle(bool clearParticles)
    {
        if (particleEffect == null || (!particleEffect.isPlaying && !particleEffect.isEmitting))
        {
            return;
        }

        particleEffect.Stop(
            true,
            clearParticles
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting);
    }

    private static float GetPlanarDistanceSqr(Vector3 from, Vector3 to)
    {
        float deltaX = to.x - from.x;
        float deltaZ = to.z - from.z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }
}
