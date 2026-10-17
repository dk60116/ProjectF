using UnityEngine;

namespace ProjectF.Combat
{
    public readonly struct WeaponShotStats
    {
        public readonly float Damage, Interval, Range, Speed;
        private WeaponShotStats(float damage, float interval, float range, float speed)
        { Damage = damage; Interval = interval; Range = range; Speed = speed; }

        public static bool TryResolve(ItemDefinition weapon, ItemDefinition ammo, out WeaponShotStats stats)
        {
            stats = default;
            if (weapon == null || !weapon.isEquipment || !weapon.isRangedWeapon
                || weapon.requiresAmmo && !weapon.AcceptsAmmo(ammo)) return false;
            float rate = Positive(weapon.fireRate, 1f), range = Positive(weapon.attackRange, 15f);
            float damage = NonNegative(weapon.attackDamage);
            ItemDefinition projectile = weapon.requiresAmmo ? ammo : weapon;
            float speed = Positive(projectile.projectileSpeed, 20f);
            if (weapon.requiresAmmo)
            {
                damage += NonNegative(ammo.attackDamage);
                rate = Mathf.Min(rate, Positive(ammo.fireRate, 1f));
                range = Mathf.Min(range, Positive(ammo.attackRange, 15f));
            }
            if (float.IsInfinity(damage)) return false;
            stats = new WeaponShotStats(damage, 1f / rate, range, speed);
            return damage > 0f;
        }

        private static float Positive(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? fallback : Mathf.Max(.01f, value);
        private static float NonNegative(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
    }
}
