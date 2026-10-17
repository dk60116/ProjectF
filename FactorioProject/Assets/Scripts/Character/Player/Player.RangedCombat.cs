using ProjectF.Combat;
using UnityEngine;

public partial class Player
{
    private PlayerProjectileWorld projectileWorld;
    private float nextRangedShotTime;

    public bool TryFireSelectedWeapon(object target)
    {
        if (IsDead || !isActiveAndEnabled || SaveManager.GameplayInputBlocked || GameManager.TextInputFocused
            || MapObjectTickManager.SimulationPaused || MapObjectTickManager.WaitingForWorldLoad
            || GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked
            || playerController != null && playerController.IsMounted || Time.time < nextRangedShotTime
            || !PlayerProjectileWorld.IsDamageable(target)) return false;
        ItemDefinition weapon = SelectedEquipment;
        if (weapon == null || !weapon.isRangedWeapon) return false;
        PlayerBag ammoBag = GetAmmunitionBag();
        ItemDefinition ammo = ResolveItemDefinition(ammoBag.GetSlotItemId(0));
        if (!WeaponShotStats.TryResolve(weapon, ammo, out WeaponShotStats stats)) return false;
        ItemDefinition projectile = weapon.requiresAmmo ? ammo : weapon;
        if (projectile == null || projectile.portableMesh == null || projectile.portableMat == null) return false;
        Vector3 origin = crossBowObject != null ? crossBowObject.transform.position
            : (BodyTransform != null ? BodyTransform.position : transform.position) + Vector3.up * .65f;
        Vector3 aim = PlayerProjectileWorld.AimPoint(target, origin);
        Vector3 offset = aim-origin;
        if (offset.sqrMagnitude < .000001f || offset.sqrMagnitude > stats.Range*stats.Range) return false;
        if (weapon.requiresAmmo && (ammoBag.GetSlotCount(0) <= 0
            || !ammoBag.TryRemoveOneAtSlot(0, out int consumed, false) || consumed != ammo.id)) return false;
        if (projectileWorld == null) projectileWorld = gameObject.AddComponent<PlayerProjectileWorld>();
        projectileWorld.Launch(projectile, origin, target, stats, transform.position);
        nextRangedShotTime = Time.time + stats.Interval;
        playerController?.FaceRangedTarget(aim);
        PlayRangedShotAnimation();
        if (weapon.requiresAmmo) RefillAmmunitionAfterShot(ammo.id);
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
