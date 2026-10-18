using UnityEngine;

public partial class PlayerController
{
    private object currentRangedAttackTarget;
    private ItemDefinition rangedAttackWeapon;

    public bool RequestRangedAttack(object target)
    {
        if (!TryResolveRangedApproach(target, out Vector3 origin, out Vector3 aim, out float range)) return false;
        if (ReferenceEquals(currentRangedAttackTarget, target) && rangedAttackWeapon == player.SelectedEquipment) return true;
        CancelRangedAttack();
        CancelKnifeAttack();
        CancelActiveResourceHarvest();
        CancelActiveCorpseHarvest();
        CancelPitchforkDigging();
        CancelSeedPlanting();
        if ((aim - origin).sqrMagnitude <= range * range) return player.TryFireSelectedWeapon(target);
        currentRangedAttackTarget = target;
        rangedAttackWeapon = player.SelectedEquipment;
        return true;
    }

    public void CancelRangedAttack()
    {
        if (currentRangedAttackTarget != null) pendingMoveDirection = Vector3.zero;
        currentRangedAttackTarget = null;
        rangedAttackWeapon = null;
    }

    public void CancelRangedAttackUnlessFocused(object target)
    {
        if (!ReferenceEquals(currentRangedAttackTarget, target)) CancelRangedAttack();
    }

    private bool TryResolveRangedApproach(object target, out Vector3 origin, out Vector3 aim, out float range)
    {
        origin = aim = Vector3.zero;
        range = 0f;
        return player != null && isActiveAndEnabled && interactionPointSnapTarget == null
            && !IsPlayerInputLocked() && !IsNooseThrowInFlight
            && (GameManager.Instance == null || !GameManager.Instance.FreeCamera)
            && player.TryGetRangedAttackSolution(target, out origin, out aim, out range);
    }

    private bool TryGetRangedAttackApproachDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        object target = currentRangedAttackTarget;
        if (target == null) return false;
        if (player == null || player.SelectedEquipment != rangedAttackWeapon
            || !TryResolveRangedApproach(target, out Vector3 origin, out Vector3 aim, out float range))
        {
            CancelRangedAttack();
            return false;
        }
        Vector3 offset = aim - origin;
        if (offset.sqrMagnitude <= range * range)
        {
            if (offset.sqrMagnitude < .000001f || player.TryFireSelectedWeapon(target)) CancelRangedAttack();
            return false;
        }
        Vector3 planar = offset;
        planar.y = 0f;
        if (planar.sqrMagnitude <= .000001f)
        {
            CancelRangedAttack();
            return false;
        }
        float step = GetCurrentOnFootMoveSpeed() * Mathf.Max(Time.deltaTime, Time.fixedDeltaTime);
        float remaining = Mathf.Sqrt(offset.sqrMagnitude) - range + .05f;
        direction = planar.normalized * Mathf.Clamp01(remaining / Mathf.Max(step, .0001f));
        return true;
    }

    public void FaceRangedTarget(Vector3 point)
    {
        CancelKnifeAttack();
        CancelActiveResourceHarvest();
        CancelActiveCorpseHarvest();
        Vector3 direction = point - (player.BodyTransform != null ? player.BodyTransform.position : transform.position);
        direction.y = 0f;
        if (direction.sqrMagnitude <= .0001f) return;
        pendingFacingDirection = direction.normalized;
        hasPendingFacingDirection = true;
        RotateBodyTowards(pendingFacingDirection);
    }
}
