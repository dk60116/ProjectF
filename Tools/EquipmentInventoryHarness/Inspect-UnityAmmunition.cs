// Read-only Pipeline eval_file probe. Does not refresh/import assets or enter Play mode.
var issues = new System.Collections.Generic.List<string>();
var weapon = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Data/Items/Item_132_Crossbow.asset");
var arrow = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Data/Items/Item_35_Arrow.asset");
if (weapon == null || !weapon.isEquipment || !weapon.isRangedWeapon || !weapon.requiresAmmo)
    issues.Add("Loaded Crossbow must be equipment, ranged and require ammunition.");
if (arrow == null || !arrow.isAmmo || arrow.portableMesh == null || arrow.portableMat == null)
    issues.Add("Loaded Arrow must be ammunition with a projectile mesh/material.");
if (weapon != null && arrow != null && !weapon.AcceptsAmmo(arrow))
    issues.Add("Loaded Crossbow must accept the authored Arrow asset.");
ProjectF.Combat.WeaponShotStats shot = default;
if (!ProjectF.Combat.WeaponShotStats.TryResolve(weapon, arrow, out shot))
    issues.Add("Loaded weapon/ammunition settings cannot produce a valid shot.");
var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var report = new System.Text.StringBuilder();
report.AppendLine("Crossbow ranged=" + (weapon != null && weapon.isRangedWeapon)
    + " requiresAmmo=" + (weapon != null && weapon.requiresAmmo)
    + "; Arrow isAmmo=" + (arrow != null && arrow.isAmmo));
report.AppendLine("Resolved shot damage=" + shot.Damage + "; interval=" + shot.Interval + "; range=" + shot.Range);
foreach (var hud in UnityEngine.Object.FindObjectsByType<PlayerHUD>(UnityEngine.FindObjectsInactive.Include,
    UnityEngine.FindObjectsSortMode.None))
{
    var equipmentSlots = (ItemSlot[])typeof(PlayerHUD).GetField("equipSlots", fields).GetValue(hud);
    var ammoSlots = (ItemSlot[])typeof(PlayerHUD).GetField("ammoSlots", fields).GetValue(hud);
    if (equipmentSlots == null || ammoSlots == null || equipmentSlots.Length != ammoSlots.Length)
        issues.Add("HUD equipment/ammunition slot counts must match.");
    if (ammoSlots != null)
        for (int i = 0; i < ammoSlots.Length; i++)
            if (!(ammoSlots[i] is BagSlot)) issues.Add("HUD ammunition slot " + i + " is missing or cannot bind inventory.");
}
var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
report.AppendLine("playing=" + UnityEngine.Application.isPlaying + "; selected="
    + (player != null ? player.SelectedEquipmentSlot : -1) + "; requiresAmmo="
    + (player != null && player.RequiresAmmunition));
report.AppendLine(issues.Count == 0 ? "PASS: Loaded ammunition assets and HUD bindings."
    : "FAIL: " + string.Join(" ", issues));
return report.ToString();
