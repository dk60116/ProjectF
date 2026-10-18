using ProjectF.Combat;
using UnityEngine;

public partial class Player
{
    private PlayerProjectileWorld projectileWorld;
    private float nextRangedShotTime;

    public bool RequestRangedAttack(object target) => playerController != null
        ? playerController.RequestRangedAttack(target) : TryFireSelectedWeapon(target);

    public bool TryGetRangedAttackRange(object target, out float range)
    {
        range = 0f;
        if (IsDead || !isActiveAndEnabled || playerController != null && playerController.IsMounted
            || !PlayerProjectileWorld.IsDamageable(target)) return false;
        ItemDefinition weapon = SelectedEquipment;
        if (weapon == null || !weapon.isRangedWeapon) return false;
        ItemDefinition ammo = null;
        if (weapon.requiresAmmo)
        {
            PlayerBag bag = GetAmmunitionBag();
            if (bag.GetSlotCount(0) > 0) ammo = ResolveItemDefinition(bag.GetSlotItemId(0));
        }
        range = WeaponShotStats.ResolveRange(weapon, ammo);
        return range > 0f;
    }

    public bool TryFireSelectedWeapon(object target)
    {
        if (Time.time < nextRangedShotTime
            || !TryResolveRangedShot(target, out WeaponShotStats stats, out ItemDefinition projectile,
                out Vector3 origin, out Vector3 aim)) return false;
        Vector3 offset = aim - origin;
        if (offset.sqrMagnitude < .000001f || offset.sqrMagnitude > stats.Range * stats.Range) return false;
        bool requiresAmmo = SelectedEquipment.requiresAmmo;
        if (requiresAmmo && (!GetAmmunitionBag().TryRemoveOneAtSlot(0, out int consumed, false)
            || consumed != projectile.id)) return false;
        if (projectileWorld == null) projectileWorld = gameObject.AddComponent<PlayerProjectileWorld>();
        projectileWorld.Launch(projectile, origin, target, stats, transform.position);
        nextRangedShotTime = Time.time + stats.Interval;
        playerController?.FaceRangedTarget(aim);
        PlayRangedShotAnimation();
        if (requiresAmmo) RefillAmmunitionAfterShot(projectile.id);
        return true;
    }

    internal bool TryGetRangedAttackSolution(object target, out Vector3 origin, out Vector3 aim, out float range)
    {
        bool ready = TryResolveRangedShot(target, out WeaponShotStats stats, out _, out origin, out aim);
        range = stats.Range;
        return ready;
    }

    private bool TryResolveRangedShot(object target, out WeaponShotStats stats, out ItemDefinition projectile,
        out Vector3 origin, out Vector3 aim)
    {
        stats = default;
        projectile = null;
        origin = aim = Vector3.zero;
        if (IsDead || !isActiveAndEnabled || SaveManager.GameplayInputBlocked || GameManager.TextInputFocused
            || MapObjectTickManager.SimulationPaused || MapObjectTickManager.WaitingForWorldLoad
            || GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked
            || playerController != null && playerController.IsMounted
            || !PlayerProjectileWorld.IsDamageable(target)) return false;
        ItemDefinition weapon = SelectedEquipment;
        if (weapon == null || !weapon.isRangedWeapon) return false;
        PlayerBag ammoBag = GetAmmunitionBag();
        ItemDefinition ammo = ResolveItemDefinition(ammoBag.GetSlotItemId(0));
        if (weapon.requiresAmmo && ammoBag.GetSlotCount(0) <= 0
            || !WeaponShotStats.TryResolve(weapon, ammo, out stats)) return false;
        projectile = weapon.requiresAmmo ? ammo : weapon;
        if (projectile == null || projectile.portableMesh == null || projectile.portableMat == null) return false;
        origin = crossBowObject != null ? crossBowObject.transform.position
            : (BodyTransform != null ? BodyTransform.position : transform.position) + Vector3.up * .65f;
        aim = PlayerProjectileWorld.AimPoint(target, origin);
        return true;
    }

    private void PlayRangedShotAnimation()
    {
        if (animator == null) return;
        int layer = GetUpperBodyLayer();
        if (layer >= 0)
        {
            animator.SetLayerWeight(layer, 1f);
            animator.CrossFadeInFixedTime("UpperBody.Shoot", .04f, layer, 0f);
        }
    }
}
