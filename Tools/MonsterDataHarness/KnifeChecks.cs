using System;
using ProjectF.Monsters;
using ProjectF.Simulation.Monsters;
using UnityEngine;

internal static class KnifeChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static (TerrainGenerator Terrain, Player Player, PlayerController Controller, MonsterInstance Monster) Setup(float x = 1f)
    {
        MonsterWorld.Current?.Dispose();
        MonsterSpawnBrush.Stop();
        Time.frameCount += 2;
        Time.time = 0f;
        MapObjectTickManager.SimulationPaused = MapObjectTickManager.WaitingForWorldLoad = false;
        SaveManager.GameplayInputBlocked = GameManager.TextInputFocused = PlayerController.KnifeInputLocked = false;
        GameManager.Instance.Player = null;
        var terrain = new TerrainGenerator();
        var player = new Player();
        var controller = new PlayerController();
        controller.BindKnifePlayer(player);
        var definition = new MonsterDefinition();
        var root = FixtureMonsterPrefab.Create();
        root.transform.position = new Vector3(x, 0f, 0f);
        var monster = new MonsterInstance(root, definition, 1);
        MonsterWorld.Register(monster, definition, terrain);
        return (terrain, player, controller, monster);
    }

    private static void Start(PlayerController controller, MonsterInstance monster)
    {
        Check(controller.RequestMonsterKnifeInteraction(monster), "Living monster accepts a knife request");
        Check(!controller.ApproachKnife(out _), "In-range monster starts a stationary swing");
        controller.KnifeTick(0f, true);
    }

    internal static void Run()
    {
        var moving = Setup();
        Check(moving.Controller.RequestMonsterKnifeInteraction(moving.Monster), "Moving player accepts an in-range knife request");
        moving.Controller.MoveManuallyForKnifeTest();
        Check(moving.Player.QueuedPicks == 1, "Manual movement can start a nearby swing without an auto-approach");
        moving.Controller.KnifeTick(0f, true);
        moving.Player.transform.position = new Vector3(.1f, 0f, .1f);
        moving.Controller.MoveManuallyForKnifeTest();
        moving.Controller.KnifeTick(.6f);
        Check(moving.Monster.CurrentHealth == 80f, "Walking during a swing retains exactly one in-range impact");
        moving = Setup(5f);
        moving.Controller.RequestMonsterKnifeInteraction(moving.Monster);
        moving.Controller.MoveManuallyForKnifeTest();
        Check(moving.Player.QueuedPicks == 0 && !moving.Controller.TryGetKnifeFocusTarget(out _),
            "Manual movement cancels a distant auto-approach instead of overriding movement input");
        var s = Setup();
        Check(!s.Controller.RequestMonsterKnifeInteraction(null), "Null monster rejected");
        Start(s.Controller, s.Monster);
        Check(s.Controller.HasKnifeFacing, "Knife swing faces the monster before its animation starts");
        Check(s.Controller.TryGetKnifeFocusTarget(out var focus) && ReferenceEquals(focus, s.Monster),
            "Monster remains the focus throughout wind-up");
        for (int i = 0; i < 20; i++) s.Controller.RequestMonsterKnifeInteraction(s.Monster);
        Check(s.Player.QueuedPicks == 1, "Repeated clicks enqueue only one swing");
        s.Controller.KnifeTick(.58f);
        Check(s.Monster.CurrentHealth == 100f, "No damage before the knife's midpoint");
        s.Controller.KnifeTick(.59f);
        Check(s.Monster.CurrentHealth == 80f, "Knife deals twenty damage at its animation midpoint");
        s.Controller.KnifeTick(.8f);
        Check(s.Monster.CurrentHealth == 80f, "One swing cannot damage twice");
        s.Controller.KnifeTick(1.17f);
        Check(!s.Controller.TryGetKnifeFocusTarget(out _), "Recovery releases the completed swing");

        s = Setup(5f);
        Check(s.Controller.RequestMonsterKnifeInteraction(s.Monster) && s.Controller.ApproachKnife(out var direction)
            && direction.x > .9f && s.Player.QueuedPicks == 0, "Far monster is approached before swinging");
        s.Player.transform.position = new Vector3(4f, 0f, 0f);
        Check(!s.Controller.ApproachKnife(out _) && s.Player.QueuedPicks == 1,
            "Approach queues exactly one swing on reaching range");

        s = Setup();
        Start(s.Controller, s.Monster);
        s.Monster.Root.transform.position = new Vector3(5f, 0f, 0f);
        s.Controller.KnifeTick(.6f);
        Check(s.Monster.CurrentHealth == 100f, "Monster escaping during wind-up is not hit remotely");
        s.Monster.Root.transform.position = new Vector3(1f, 0f, 0f);
        s.Controller.KnifeTick(.8f);
        Check(s.Monster.CurrentHealth == 100f, "A missed swing cannot hit later on target return");

        s = Setup();
        Start(s.Controller, s.Monster);
        s.Terrain.Walls.Add(new Vector2Int(1, 0));
        s.Terrain.InvalidateAnimalNavigation();
        s.Controller.KnifeTick(.6f);
        Check(s.Monster.CurrentHealth == 100f, "An obstacle appearing during wind-up blocks the hit");

        s = Setup();
        s.Terrain.Walls.Add(new Vector2Int(1, 0));
        s.Terrain.InvalidateAnimalNavigation();
        Check(s.Controller.RequestMonsterKnifeInteraction(s.Monster), "Blocked target may be selected");
        s.Controller.ApproachKnife(out _);
        Check(s.Player.QueuedPicks == 0 && !s.Controller.TryGetKnifeFocusTarget(out _),
            "An obstructed in-range target does not start a wall-piercing swing");

        foreach (int barrier in new[] { 0, 1, 2, 3, 4, 5 })
        {
            s = Setup();
            Start(s.Controller, s.Monster);
            switch (barrier)
            {
                case 0: MapObjectTickManager.SimulationPaused = true; break;
                case 1: MapObjectTickManager.WaitingForWorldLoad = true; break;
                case 2: SaveManager.GameplayInputBlocked = true; break;
                case 3: s.Player.Health.Damage(100f); break;
                case 4: s.Player.IsCarrying = true; break;
                case 5: s.Controller.SetKnifeMounted(true); break;
            }
            s.Controller.KnifeTick(.6f);
            Check(s.Monster.CurrentHealth == 100f && !s.Controller.TryGetKnifeFocusTarget(out _),
                "Input/death/carry/mount barrier cancels the pending knife hit: " + barrier);
            Check(!s.Controller.RequestMonsterKnifeInteraction(s.Monster), "Barrier rejects a new attack: " + barrier);
        }

        s = Setup();
        s.Controller.RequestMonsterKnifeInteraction(s.Monster);
        s.Controller.ApproachKnife(out _);
        s.Controller.KnifeTick(1.6f);
        s.Controller.KnifeTick(2f, true);
        Check(s.Player.QueuedPicks == 0 && s.Monster.CurrentHealth == 100f
            && !s.Controller.TryGetKnifeFocusTarget(out _), "An animation-start timeout cancels queued swings and cannot deliver a late hit");

        s = Setup();
        Start(s.Controller, s.Monster);
        s.Controller.CancelKnifeForTest();
        s.Controller.KnifeTick(.6f);
        Check(s.Monster.CurrentHealth == 100f && s.Player.ClearedPicks > 0, "Explicit cancellation removes the queued hit");

        s = Setup();
        Start(s.Controller, s.Monster);
        s.Monster.Dispose();
        s.Controller.KnifeTick(.6f);
        Check(s.Monster.CurrentHealth == 100f && !s.Controller.TryGetKnifeFocusTarget(out _),
            "Disposed target cancels the swing without damage");

        s = Setup();
        Check(s.Monster.TakeDamage(float.NaN) == 0f && s.Monster.TakeDamage(float.PositiveInfinity) == 0f
            && s.Monster.TakeDamage(-1f) == 0f, "Monster rejects invalid damage");
        for (int i = 0; i < 5; i++)
        {
            Time.time = 0f;
            Start(s.Controller, s.Monster);
            s.Controller.KnifeTick(.6f);
            s.Controller.KnifeTick(1.17f);
        }
        Check(s.Monster.CurrentHealth == 0f && !s.Monster.HasCollision, "Five knife swings kill and immediately disable collision");
        Check(!s.Controller.RequestMonsterKnifeInteraction(s.Monster) && s.Monster.TakeDamage(20f) == 0f,
            "Dead monsters cannot be attacked or take damage again");
        MonsterWorld.Current.ManagedUpdateTick(1f / 60f);
        Check(s.Monster.Root.AnimationState == MonsterAnimationState.Death, "Lethal knife damage enters existing ECS death animation");

        s = Setup();
        var animal = new Animal();
        animal.transform.position = new Vector3(1f, 0f, 0f);
        Check(s.Controller.RequestAnimalKnifeForTest(animal), "Animal attacks still use the shared knife controller");
        s.Controller.ApproachKnife(out _);
        s.Controller.KnifeTick(0f, true);
        s.Controller.KnifeTick(.6f);
        Check(animal.Health.Current == 80f && animal.AttackNotifications == 1,
            "Animal damage and animation-start notification remain intact");

        s = Setup();
        Start(s.Controller, s.Monster);
        for (int i = 0; i < 100; i++) s.Controller.KnifeTick(.2f);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) s.Controller.KnifeTick(.2f);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(allocated == 0, "Steady player knife tick allocates no managed memory: " + allocated);
        MonsterWorld.Current.Dispose();
        Console.WriteLine($"PASS: {checks} production player knife/monster damage checks");
    }
}
