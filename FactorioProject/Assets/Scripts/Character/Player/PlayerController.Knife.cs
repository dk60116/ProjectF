using System;
using ProjectF.Monsters;
using UnityEngine;

public partial class PlayerController
{
    private const float MinimumKnifeInteractionRange = .75f;
    private const float KnifeInteractionTimeout = 1.5f;
    private const float KnifeDamage = 20f;
    private const float KnifeDamageDelay = PickAnimationDuration * .5f;
    private object currentKnifeTarget, pendingKnifeTarget;
    private bool knifePickPending, knifeAnimationStarted, knifeDamageApplied;
    private float knifeDamageTime, knifeInteractionEndTime, knifeInteractionTimeout;
    private TerrainGenerator knifeTerrain;
    private Func<Vector3, bool> knifeCanMoveTo;

    public bool RequestMonsterKnifeInteraction(MonsterInstance monster) => RequestKnifeAttack(monster);

    private bool CanUseKnife => player != null && !player.IsDead && !player.IsCarrying
        && interactionPointSnapTarget == null && !IsPlayerInputLocked()
        && !MapObjectTickManager.SimulationPaused && !MapObjectTickManager.WaitingForWorldLoad;

    private bool RequestKnifeAttack(object target)
    {
        if (!CanUseKnife || !CanAttackWithKnife(target)) return false;
        // Repeated button presses never enqueue additional hits during the same swing.
        if (ReferenceEquals(currentKnifeTarget, target) || knifePickPending) return true;
        CancelActiveResourceHarvest();
        CancelActiveCorpseHarvest();
        currentKnifeTarget = target;
        return true;
    }

    public bool TryGetKnifeFocusTarget(out object target)
    {
        target = currentKnifeTarget ?? pendingKnifeTarget;
        return CanAttackWithKnife(target);
    }

    private static bool CanAttackWithKnife(object target) => target is MonsterInstance monster
        ? monster.IsActive && monster.CurrentHealth > 0f
        : target is Animal animal && animal != null && animal.gameObject.activeInHierarchy && animal.CanBeAttacked;

    private Vector3 KnifeOrigin => player.BodyTransform != null ? player.BodyTransform.position : transform.position;
    private float KnifeRange => Mathf.Max(MinimumKnifeInteractionRange, player.State.HarvestRange);

    private static Vector3 KnifeAimPoint(object target, Vector3 origin) => target is MonsterInstance monster
        ? monster.ClosestCombatPoint(origin) : ((Animal)target).transform.position;

    private bool HasKnifeAttackLine(Vector3 origin, Vector3 point)
    {
        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (terrain != knifeTerrain)
        {
            knifeTerrain = terrain;
            knifeCanMoveTo = terrain != null ? terrain.CanMonsterMoveTo : null;
        }
        return terrain != null && AnimalGridPathfinder.HasWalkableLine(terrain, origin, point,
            origin, Vector3.Distance(origin, point) + 1f, true, knifeCanMoveTo);
    }

    private bool TryGetKnifeApproachDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        object target = currentKnifeTarget;
        if (!CanUseKnife || !CanAttackWithKnife(target))
        {
            CancelKnifeApproach();
            return false;
        }
        Vector3 origin = KnifeOrigin;
        Vector3 point = KnifeAimPoint(target, origin);
        Vector3 offset = point - origin;
        offset.y = 0f;
        if (offset.sqrMagnitude > KnifeRange * KnifeRange)
        {
            direction = offset.normalized;
            return true;
        }
        if (!HasKnifeAttackLine(origin, point))
        {
            CancelKnifeApproach();
            return false;
        }
        currentKnifeTarget = null;
        if (offset.sqrMagnitude > .0001f)
        {
            pendingFacingDirection = offset.normalized;
            hasPendingFacingDirection = true;
        }
        pendingKnifeTarget = target;
        knifePickPending = true;
        knifeAnimationStarted = knifeDamageApplied = false;
        knifeDamageTime = knifeInteractionEndTime = 0f;
        knifeInteractionTimeout = Time.time + KnifeInteractionTimeout;
        player.QueuePickAnimation();
        return false;
    }

    private void TickKnifeAttack()
    {
        if (!CanUseKnife)
        {
            CancelKnifeAttack();
            return;
        }
        if (!knifePickPending) return;
        if (!CanAttackWithKnife(pendingKnifeTarget) || Time.time >= knifeInteractionTimeout)
        {
            CancelKnifeAttack();
            return;
        }
        if (!knifeAnimationStarted && player.PickAnimationStartedThisFrame)
        {
            knifeAnimationStarted = true;
            knifeDamageTime = Time.time + KnifeDamageDelay;
            knifeInteractionEndTime = Time.time + PickAnimationDuration;
            knifeInteractionTimeout = Time.time + KnifeInteractionTimeout;
            if (pendingKnifeTarget is Animal animal) animal.NotifyAttackAnimationStarted();
        }
        if (knifeAnimationStarted && !knifeDamageApplied && Time.time >= knifeDamageTime)
        {
            // Commit exactly once, then validate again so moving targets cannot be hit remotely.
            knifeDamageApplied = true;
            Vector3 origin = KnifeOrigin;
            Vector3 point = KnifeAimPoint(pendingKnifeTarget, origin);
            Vector3 offset = point - origin;
            offset.y = 0f;
            if (offset.sqrMagnitude <= KnifeRange * KnifeRange && HasKnifeAttackLine(origin, point))
            {
                if (pendingKnifeTarget is MonsterInstance monster) monster.TakeDamage(KnifeDamage);
                else ((Animal)pendingKnifeTarget).TakeDamage(KnifeDamage, player.transform.position);
            }
        }
        if (knifeAnimationStarted && Time.time >= knifeInteractionEndTime)
            ClearPendingKnifeAttack();
    }

    private void HandleManualKnifeMovement()
    {
        // Manual movement may start an in-range swing, but never redirects the player's movement.
        if (currentKnifeTarget != null) TryGetKnifeApproachDirection(out _);
        CancelKnifeApproach();
    }

    private void CancelKnifeApproach() => currentKnifeTarget = null;

    private void ClearPendingKnifeAttack()
    {
        pendingKnifeTarget = null;
        knifePickPending = knifeAnimationStarted = knifeDamageApplied = false;
        knifeDamageTime = knifeInteractionEndTime = knifeInteractionTimeout = 0f;
    }

    private void CancelKnifeAttack()
    {
        bool active = currentKnifeTarget != null || knifePickPending;
        CancelKnifeApproach();
        ClearPendingKnifeAttack();
        if (active) player?.ClearQueuedPickAnimations();
    }
}
