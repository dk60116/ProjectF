using System.Collections.Generic;
using UnityEngine;

public partial class ItemDefinition
{
    [Header("Weapon / Ammunition")]
    public bool isRangedWeapon;
    public bool isAmmo;
    public bool requiresAmmo;
    public List<ItemDefinition> compatibleAmmo = new List<ItemDefinition>();
    [Min(0f), Tooltip("Weapon and ammunition damage are added together.")]
    public float attackDamage;
    [Min(.01f), Tooltip("Shots per second. The lower weapon/ammunition value limits the final rate.")]
    public float fireRate = 1f;
    [Min(.01f), Tooltip("The lower weapon/ammunition range limits the final range.")]
    public float attackRange = 15f;
    [Min(.01f), Tooltip("Projectile travel speed in world units per second. Ammunition weapons use the ammunition item's value.")]
    public float projectileSpeed = 20f;

    public bool AcceptsAmmo(ItemDefinition ammunition)
    {
        if (!isEquipment || !isRangedWeapon || !requiresAmmo || ammunition == null || !ammunition.isAmmo
            || compatibleAmmo == null) return false;
        for (int i = 0; i < compatibleAmmo.Count; i++)
            if (compatibleAmmo[i] == ammunition) return true;
        return false;
    }
}
