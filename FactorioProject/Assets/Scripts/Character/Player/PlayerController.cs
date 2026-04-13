using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Player))]
public class PlayerController : MonoBehaviour
{
    [SerializeField]
    private Transform movementReference;

    [SerializeField, Range(-1f, 1f)]
    private float autoHarvestFacingDot = 0.45f;

    [SerializeField]
    private float harvestStartDelay = 0.5f;

    private Player player;
    private Joystick joystick;
    private ResourceWrokGauge resourceWorkGauge;
    private Resource currentTargetResource;
    private Block currentFocusedBlock;
    private float stationaryHarvestTimer;
    private readonly Queue<Resource> pendingHarvestResources = new Queue<Resource>();

    private void Awake()
    {
        player = GetComponent<Player>();
    }

    private void Start()
    {
        joystick = FindObjectOfType<Joystick>();
        resourceWorkGauge = ResourceWrokGauge.FindOrCreate();
        resourceWorkGauge?.Hide();
        ResolveMovementReference();
    }

    private void OnDisable()
    {
        SetFocusedBlock(null);
    }

    private void Update()
    {
        Vector2 input = Vector2.zero;

        if (joystick == null)
        {
            joystick = FindObjectOfType<Joystick>();
        }

        if (movementReference == null)
        {
            ResolveMovementReference();
        }

        if (joystick != null)
        {
            input = joystick.InputDirection;
        }

        Vector3 moveDirection = GetMoveDirection(input);
        bool hasMovement = moveDirection.sqrMagnitude > 0.0001f;

        if (hasMovement)
        {
            stationaryHarvestTimer = 0f;
        }
        else
        {
            stationaryHarvestTimer += Time.deltaTime;
        }

        if (hasMovement)
        {
            transform.position += moveDirection * player.Stat.currentMoveSpeed * Time.deltaTime;

            Quaternion targetRotation = Quaternion.LookRotation(moveDirection.normalized, Vector3.up);
            Transform rotationTarget = player.BodyTransform != null ? player.BodyTransform : transform;
            rotationTarget.rotation = Quaternion.RotateTowards(
                rotationTarget.rotation,
                targetRotation,
                player.Stat.rotateSpeed * Time.deltaTime);
        }

        UpdateAutoHarvest(hasMovement);

        bool finishedPickThisFrame = player.UpdateAnimationState(hasMovement);
        ResolveCompletedPick(finishedPickThisFrame);
    }

    private bool UpdateAutoHarvest(bool hasMovement)
    {
        Resource nextTarget = FindBestHarvestTarget();

        if (currentTargetResource != nextTarget)
        {
            CancelPendingHarvest();
            currentTargetResource = nextTarget;
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
        }

        if (currentTargetResource == null)
        {
            CancelPendingHarvest();
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
            return false;
        }

        if (hasMovement)
        {
            CancelPendingHarvest();
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
            return false;
        }
        
        if (stationaryHarvestTimer < harvestStartDelay)
        {
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
            return false;
        }

        float harvestSpeed = GetHarvestSpeed(currentTargetResource);
        if (harvestSpeed <= 0f)
        {
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
            return false;
        }

        SetFocusedBlock(currentTargetResource.OwningBlock);
        resourceWorkGauge?.Bind(currentTargetResource);

        int preparedStepCount = currentTargetResource.PrepareHarvestSteps(harvestSpeed * Time.deltaTime);

        for (int i = 0; i < preparedStepCount; i++)
        {
            pendingHarvestResources.Enqueue(currentTargetResource);
            player.QueuePickAnimation();
        }

        if (!currentTargetResource.CanHarvest)
        {
            SetFocusedBlock(null);
            currentTargetResource = null;
            stationaryHarvestTimer = 0f;
        }

        return preparedStepCount > 0;
    }

    private void ResolveCompletedPick(bool finishedPickThisFrame)
    {
        if (!finishedPickThisFrame || pendingHarvestResources.Count == 0)
        {
            return;
        }

        Resource harvestedResource = pendingHarvestResources.Dequeue();
        if (harvestedResource == null)
        {
            return;
        }

        harvestedResource.CommitPreparedHarvestStep();

        if (harvestedResource == currentTargetResource)
        {
            resourceWorkGauge?.Bind(currentTargetResource);

            if (!currentTargetResource.CanHarvest)
            {
                SetFocusedBlock(null);
                currentTargetResource = null;
                return;
            }
        }
    }

    private void CancelPendingHarvest()
    {
        if (pendingHarvestResources.Count == 0)
        {
            player.ClearQueuedPickAnimations();
            resourceWorkGauge?.HideIfNotFinishing();
            return;
        }

        foreach (Resource resource in pendingHarvestResources)
        {
            resource?.CancelPreparedHarvestStep();
        }

        pendingHarvestResources.Clear();
        player.ClearQueuedPickAnimations();
        resourceWorkGauge?.HideIfNotFinishing();
    }

    private Resource FindBestHarvestTarget()
    {
        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        Vector3 forward = player.BodyTransform != null ? player.BodyTransform.forward : transform.forward;
        float harvestRange = player.State.HarvestRange;
        float maxDistanceSqr = harvestRange * harvestRange;
        float bestScore = float.NegativeInfinity;
        Resource bestResource = null;

        IReadOnlyList<Resource> resources = Resource.ActiveResources;
        for (int i = 0; i < resources.Count; i++)
        {
            Resource resource = resources[i];
            if (resource == null || !resource.CanHarvest)
            {
                continue;
            }

            Vector3 offset = resource.FocusPoint - origin;
            offset.y = 0f;

            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr <= 0.0001f || distanceSqr > maxDistanceSqr)
            {
                continue;
            }

            Vector3 direction = offset.normalized;
            float facingDot = Vector3.Dot(forward, direction);
            if (facingDot < autoHarvestFacingDot)
            {
                continue;
            }

            float normalizedDistanceScore = 1f - (distanceSqr / maxDistanceSqr);
            float score = facingDot * 2f + normalizedDistanceScore;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestResource = resource;
        }

        return bestResource;
    }

    private float GetHarvestSpeed(Resource resource)
    {
        if (resource == null)
        {
            return 0f;
        }

        return resource.ResolvedHarvestMode == Resource.HarvestMode.Logging
            ? player.State.LoggingSpeed
            : player.State.MiningSpeed;
    }

    private void SetFocusedBlock(Block nextBlock)
    {
        if (currentFocusedBlock == nextBlock)
        {
            return;
        }

        currentFocusedBlock?.SetFocusVisible(false);
        currentFocusedBlock = nextBlock;
        currentFocusedBlock?.SetFocusVisible(true);
    }

    private void ResolveMovementReference()
    {
        if (movementReference != null)
        {
            return;
        }

        if (Camera.main != null)
        {
            movementReference = Camera.main.transform;
        }
    }

    private Vector3 GetMoveDirection(Vector2 input)
    {
        if (input.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        if (movementReference == null)
        {
            return new Vector3(input.x, 0f, input.y);
        }

        Vector3 forward = movementReference.forward;
        Vector3 right = movementReference.right;
        forward.y = 0f;
        right.y = 0f;

        if (forward.sqrMagnitude <= 0.0001f || right.sqrMagnitude <= 0.0001f)
        {
            return new Vector3(input.x, 0f, input.y);
        }

        forward.Normalize();
        right.Normalize();

        Vector3 moveDirection = (right * input.x) + (forward * input.y);
        return moveDirection.sqrMagnitude > 1f ? moveDirection.normalized : moveDirection;
    }
}
