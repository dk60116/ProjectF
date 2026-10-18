using System;
using System.Reflection;
using ProjectF.Combat;
using UnityEngine;

static partial class Checks
{
    static void CheckRangedRange()
    {
        var manager = GameManager.Instance;
        var items = manager.ItemManger.Items;
        var weapon = items[22]; var arrow = items[23];
        float oldWeaponRange = weapon.attackRange, oldAmmoRange = arrow.attackRange;
        weapon.attackRange = 10f; arrow.attackRange = 6f;
        var player = new Player();
        var target = new ProjectF.Monsters.MonsterInstance();
        Check(!player.TryGetRangedAttackRange(target, out _), "No equipped ranged weapon means no attack range marker");
        player.GetEquipmentBag().SetSlotContents(0,22,1);
        Check(player.TryGetRangedAttackRange(target, out float range) && range == 10f,
            "An empty magazine still previews the equipped weapon range without auto-loading ammo");
        player.Bag.SetSlotContents(0,23,2);
        Check(PlayerInventoryTransfer.TryMove(player.Bag,0,player.GetAmmunitionBag(),0,2), "Ammo can be loaded for the range preview");
        Check(player.TryGetRangedAttackRange(target, out range) && range == 6f
            && WeaponShotStats.TryResolve(weapon,arrow,out var stats) && range == stats.Range,
            "The range preview and actual shot share the same weapon/ammo range calculation");
        Check(!player.TryGetRangedAttackRange(new object(),out _), "Non-attackable structures do not request a ranged marker");
        Check(player.TryGetRangedAttackRange(new Animal(),out _), "Attackable animals also request a ranged marker");

        manager.Player = player;
        player.transform.position = new(3,0,4);
        var hud = new PlayerHUD();
        hud.FocusForTest(target);
        var flags = BindingFlags.Instance|BindingFlags.NonPublic;
        LineRenderer Line() => (LineRenderer)typeof(RangedAttackRangeView).GetField("line",flags).GetValue(hud.RangeView);
        GameObject Root(RangedAttackRangeView view) => (GameObject)typeof(RangedAttackRangeView).GetField("root",flags).GetValue(view);
        var line = Line();
        Check(line.enabled && line.loop && line.positionCount == 96 && line.useWorldSpace
            && line.startColor == Color.white && line.endColor == Color.white && line.startWidth > 0f,
            "Focusing an attackable target enables a closed white world-space range ring");
        foreach (var point in line.Positions)
        {
            var offset = point-player.transform.position;
            Check(MathF.Abs(offset.x*offset.x+offset.z*offset.z-36f) < .001f && MathF.Abs(offset.y-.05f) < .00001f,
                "Every ring vertex follows the actual attack radius on the player's ground plane");
        }
        int calls = line.SetPositionCalls;
        hud.TickRangeForTest();
        Check(line.SetPositionCalls == calls, "An unchanged visible ring does not rebuild geometry each frame");
        player.transform.position = new(5,0,7);
        hud.TickRangeForTest();
        Check(line.SetPositionCalls == calls+96 && line.Positions[0] == new Vector3(11,.05f,7),
            "The range ring follows player movement without creating a second renderer");
        arrow.attackRange = 4f;
        hud.TickRangeForTest();
        Check(line.Positions[0] == new Vector3(9,.05f,7), "Editing ammunition range updates the preview radius");
        hud.FocusForTest(null);
        Check(!line.enabled, "Clearing focus hides the ring");
        hud.FocusForTest(new object());
        Check(!line.enabled, "Focusing an ineligible object hides the ring");
        hud.FocusForTest(target);
        target.CurrentHealth = 0;
        hud.TickRangeForTest();
        Check(!line.enabled, "A focused target dying hides the ring immediately");
        target.CurrentHealth = 100;
        player.IsDead = true;
        hud.TickRangeForTest();
        Check(!line.enabled, "A dead player has no ranged attack marker");
        player.IsDead = false;
        player.SetMountedForTest(true);
        hud.TickRangeForTest();
        Check(!line.enabled, "Mounted players cannot show a marker for unavailable crossbow attacks");
        player.SetMountedForTest(false);
        manager.PlayerInteractionLocked = true;
        hud.TickRangeForTest();
        Check(!line.enabled, "Locked interactions hide the ring");
        manager.PlayerInteractionLocked = false;
        SaveManager.GameplayInputBlocked = true;
        hud.TickRangeForTest();
        Check(!line.enabled, "Loading input barriers hide the ring");
        SaveManager.GameplayInputBlocked = false;
        MapObjectTickManager.WaitingForWorldLoad = true;
        hud.TickRangeForTest();
        Check(!line.enabled, "Waiting for world restoration hides the ring");
        MapObjectTickManager.WaitingForWorldLoad = false;
        player.TrySelectEquipment(0);
        hud.TickRangeForTest();
        Check(!line.enabled, "Unequipping the crossbow hides the ring");
        player.TrySelectEquipment(0);
        hud.TickRangeForTest();
        Check(line.enabled && ReferenceEquals(line,Line()), "Re-equipping reuses the existing range renderer");
        var oldView = hud.RangeView;
        var nextPlayer = new Player();
        nextPlayer.GetEquipmentBag().SetSlotContents(0,22,1);
        manager.Player = nextPlayer;
        hud.TickRangeForTest();
        Check(Root(oldView).Destroyed && line.sharedMaterial.Destroyed && Line().enabled,
            "Replacing the player disposes the old range view and binds to the new player");
        var lastView = hud.RangeView;
        line = Line();
        hud.ReleaseRangeForTest();
        Check(hud.RangeView == null && Root(lastView).Destroyed && !line.enabled && line.sharedMaterial.Destroyed,
            "Disabling the HUD releases its range geometry and material");
        manager.Player = null;
        weapon.attackRange = oldWeaponRange; arrow.attackRange = oldAmmoRange;
    }
}
