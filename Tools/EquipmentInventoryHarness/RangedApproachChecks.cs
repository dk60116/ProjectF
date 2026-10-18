using System;
using ProjectF.Combat;
using ProjectF.Monsters;
using UnityEngine;

static partial class Checks
{
    static void CheckRangedApproach()
    {
        var manager = GameManager.Instance;
        var weapon = manager.ItemManger.Items[22]; var arrow = manager.ItemManger.Items[23];
        float oldRange = weapon.attackRange, oldAmmoRange = arrow.attackRange;
        weapon.attackRange = arrow.attackRange = 5f;
        Time.deltaTime = .1f;
        (Player Player, PlayerController Controller, MonsterInstance Target) Setup(float distance = 20f)
        {
            manager.PlayerInteractionLocked = manager.FreeCamera = GameManager.TextInputFocused = false;
            SaveManager.GameplayInputBlocked = MapObjectTickManager.SimulationPaused = MapObjectTickManager.WaitingForWorldLoad = false;
            Time.time = 100f;
            var player = new Player();
            player.GetEquipmentBag().SetSlotContents(0,22,1);
            player.Bag.SetSlotContents(0,23,20);
            PlayerInventoryTransfer.TryMove(player.Bag,0,player.GetAmmunitionBag(),0,20);
            return (player, player.ControllerForTest, new MonsterInstance { Position = new(0,.65f,distance) });
        }
        void Step(Player player, PlayerController controller)
        {
            Time.time += Time.deltaTime; Time.frameCount++;
            if (controller.ApproachRangedForTest(out var direction))
                player.transform.position += direction * controller.MoveSpeed * Time.deltaTime;
        }
        var s = Setup();
        Check(s.Player.RequestRangedAttack(s.Target) && s.Controller.HasRangedRequestForTest,
            "An out-of-range attack queues one chase instead of failing silently");
        Check(s.Player.GetAmmunitionBag().GetSlotCount(0) == 20 && s.Target.CurrentHealth == 100,
            "Requesting a chase consumes no ammo and deals no remote damage");
        Check(s.Controller.CancelledKnife > 0 && s.Controller.CancelledResource > 0 && s.Controller.CancelledCorpse > 0
            && s.Controller.CancelledDigging > 0 && s.Controller.CancelledPlanting > 0,
            "Ranged pursuit cancels conflicting harvest and melee actions");
        for (int i = 0; i < 100 && s.Controller.HasRangedRequestForTest; i++) Step(s.Player,s.Controller);
        float distance = (s.Target.Position - s.Player.transform.position - Vector3.up * .65f).sqrMagnitude;
        Check(!s.Controller.HasRangedRequestForTest && distance <= 25f && distance > 20f,
            "The controller stops at firing range rather than running all the way into the target");
        Check(s.Player.GetAmmunitionBag().GetSlotCount(0) == 19 && s.Target.CurrentHealth == 100
            && s.Player.gameObject.GetComponent<PlayerProjectileWorld>() != null,
            "Entering range launches one real projectile and consumes exactly one arrow");
        for (int i = 0; i < 20; i++) Step(s.Player,s.Controller);
        Check(s.Player.GetAmmunitionBag().GetSlotCount(0) == 19, "A single attack command does not become endless automatic fire");

        s = Setup(); s.Player.RequestRangedAttack(s.Target);
        Step(s.Player,s.Controller);
        s.Target.Position = new(10,.65f,20);
        Check(s.Controller.ApproachRangedForTest(out var redirected) && redirected.x > 0f && redirected.z > 0f && redirected.y == 0f,
            "Pursuit follows the target's current position on the ground plane");
        int cancelled = s.Controller.CancelledKnife;
        for (int i = 0; i < 25; i++) s.Player.RequestRangedAttack(s.Target);
        Check(s.Controller.CancelledKnife == cancelled && s.Player.GetAmmunitionBag().GetSlotCount(0) == 20,
            "Repeated held-button requests retain one chase without restarting actions or consuming ammunition");
        s.Controller.CancelRangedAttackUnlessFocused(s.Target);
        Check(s.Controller.HasRangedRequestForTest, "Keeping focus on the same target preserves pursuit");
        s.Controller.CancelRangedAttackUnlessFocused(new MonsterInstance());
        Check(!s.Controller.HasRangedRequestForTest, "Changing the focused target cancels the old pursuit");
        s.Player.RequestRangedAttack(s.Target);
        s.Controller.PendingMoveForTest = Vector3.forward;
        s.Controller.CancelRangedAttack();
        Check(!s.Controller.ApproachRangedForTest(out _) && s.Controller.PendingMoveForTest == Vector3.zero,
            "Manual cancellation clears the pending automatic physics movement");

        s = Setup(); s.Player.RequestRangedAttack(s.Target); s.Target.CurrentHealth = 0;
        Check(!s.Controller.ApproachRangedForTest(out _) && !s.Controller.HasRangedRequestForTest
            && s.Player.GetAmmunitionBag().GetSlotCount(0) == 20, "A dead target cancels pursuit without spending arrows");
        s = Setup(); s.Player.RequestRangedAttack(s.Target); s.Target.IsActive = false;
        Check(!s.Controller.ApproachRangedForTest(out _) && !s.Controller.HasRangedRequestForTest,
            "A despawned target cancels pursuit");
        s = Setup(); s.Player.RequestRangedAttack(s.Target); s.Player.IsDead = true;
        Check(!s.Controller.ApproachRangedForTest(out _) && !s.Controller.HasRangedRequestForTest,
            "A dead player cannot continue a pending chase");
        s = Setup(); s.Player.RequestRangedAttack(s.Target); s.Player.TrySelectEquipment(0);
        Check(!s.Controller.HasRangedRequestForTest && !s.Controller.ApproachRangedForTest(out _),
            "Unequipping the weapon immediately cancels pursuit");
        s = Setup(); s.Player.RequestRangedAttack(s.Target);
        PlayerInventoryTransfer.TryMove(s.Player.GetAmmunitionBag(),0,s.Player.Bag,0,20);
        Check(!s.Controller.ApproachRangedForTest(out _) && !s.Controller.HasRangedRequestForTest,
            "Unloading all ammo cancels pursuit instead of chasing with an empty crossbow");
        Check(!s.Player.RequestRangedAttack(s.Target), "An empty magazine cannot start another chase");

        foreach (string barrier in new[] { "Load", "Pause", "Input", "Camera", "Mount", "Inactive" })
        {
            s = Setup(); s.Player.RequestRangedAttack(s.Target);
            if (barrier == "Load") MapObjectTickManager.WaitingForWorldLoad = true;
            if (barrier == "Pause") MapObjectTickManager.SimulationPaused = true;
            if (barrier == "Input") SaveManager.GameplayInputBlocked = true;
            if (barrier == "Camera") manager.FreeCamera = true;
            if (barrier == "Mount") s.Player.SetMountedForTest(true);
            if (barrier == "Inactive") s.Controller.isActiveAndEnabled = false;
            Check(!s.Controller.ApproachRangedForTest(out _) && !s.Controller.HasRangedRequestForTest,
                "Pursuit cancels safely at gameplay barrier: " + barrier);
        }
        s = Setup(3f);
        s.Controller.PendingMoveForTest = Vector3.forward;
        Check(s.Player.RequestRangedAttack(s.Target) && !s.Controller.HasRangedRequestForTest
            && s.Controller.PendingMoveForTest == Vector3.forward && s.Player.GetAmmunitionBag().GetSlotCount(0) == 19,
            "An immediate in-range shot does not erase normal manual movement");
        Check(!s.Player.RequestRangedAttack(s.Target) && !s.Controller.HasRangedRequestForTest,
            "Held fire during an in-range cooldown does not leave a queued shot after the key is released");
        s.Target.Position = new(0,.65f,20);
        Check(s.Player.RequestRangedAttack(s.Target) && s.Controller.HasRangedRequestForTest,
            "An out-of-range attack can begin pursuit even during the previous shot's cooldown");
        s.Target.Position = new(0,.65f,3);
        Check(!s.Controller.ApproachRangedForTest(out _) && s.Controller.HasRangedRequestForTest
            && s.Player.GetAmmunitionBag().GetSlotCount(0) == 19, "Cooldown waiting is stationary and spends no ammo");
        Time.time += 2f;
        Check(!s.Controller.ApproachRangedForTest(out _) && !s.Controller.HasRangedRequestForTest
            && s.Player.GetAmmunitionBag().GetSlotCount(0) == 18, "The queued cooldown attack fires exactly once when ready");
        s = Setup(5.01f);
        Check(s.Player.RequestRangedAttack(s.Target) && s.Controller.ApproachRangedForTest(out var near)
            && near.z > 0f && near.z < 1f, "Near the range boundary the last approach step is shortened to avoid overshooting");
        s = Setup();
        var animal = new Animal(); animal.transform.position = new(0,.3f,20);
        Check(s.Player.RequestRangedAttack(animal) && s.Controller.ApproachRangedForTest(out _),
            "The same ranged pursuit supports animal targets");
        s.Controller.CancelRangedAttack();
        weapon.attackRange = oldRange; arrow.attackRange = oldAmmoRange;
    }
}
