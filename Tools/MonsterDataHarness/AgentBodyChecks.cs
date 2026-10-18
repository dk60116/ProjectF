using System;
using ProjectF.Monsters;
using ProjectF.Simulation.Monsters;
using UnityEngine;

internal static class AgentBodyChecks
{
    private static int checks;
    private static void Check(bool value, string label)
    { checks++; if (!value) throw new Exception(label); }
    private static TerrainGenerator Reset()
    {
        MonsterWorld.Current?.Dispose();
        GameManager.Instance.Player = null;
        GameManager.Instance.SetCloakingMode(false);
        MapObjectTickManager.SimulationPaused = MapObjectTickManager.WaitingForWorldLoad = false;
        ConveyorWorld.Current = null;
        PipeWorld.Current = null;
        return new TerrainGenerator();
    }
    private static MonsterInstance Spawn(TerrainGenerator terrain, Vector3 position, float speed = 0f)
    {
        var definition = new MonsterDefinition();
        var stats = MonsterStats.Default;
        stats.MoveSpeed = speed;
        stats.AttackRange = .1f;
        stats.DetectionRange = 32;
        stats.LeashRange = 64;
        definition.Stats = stats;
        var root = FixtureMonsterPrefab.Create();
        var capsule = root.GetComponent<CapsuleCollider>();
        capsule.radius = .25f;
        capsule.height = 1f;
        capsule.center = new Vector3(0, .5f, 0);
        root.transform.position = position;
        var monster = new MonsterInstance(root, definition, 1);
        MonsterWorld.Register(monster, definition, terrain);
        return monster;
    }
    private static void Tick(int count = 1)
    { for (int i = 0; i < count; i++) MonsterWorld.Current.ManagedUpdateTick(1f / 60f); }
    private static float Distance(Vector3 a, Vector3 b)
    { float x = a.x - b.x, z = a.z - b.z; return MathF.Sqrt(x * x + z * z); }

    internal static void Run()
    {
        var terrain = Reset();
        var monster = Spawn(terrain, default);
        var player = new PlayerController();
        var right = new Vector3(1, 0, 0);
        Check(player.Sweep(new Vector3(-2, 0, 0), right, 4, out var hit)
            && MathF.Abs(hit.distance - 1.549f) < .0001f && hit.normal.x < -.99f,
            "The actual player combined sweep stops at the ECS capsule surface");
        Check(player.Sweep(new Vector3(-20, 0, 0), right, 40, out hit) && hit.distance < 20,
            "High-speed player movement cannot tunnel through a monster");
        Check(!player.Sweep(new Vector3(-2, 0, 1), right, 4, out _), "A clear parallel player path stays open");
        player.PhysicsDistance = .2f;
        Check(player.Sweep(new Vector3(-2, 0, 0), right, 4, out hit) && hit.distance == .2f,
            "A nearer physical wall wins over an ECS monster");
        player.PhysicsDistance = -1;
        player.Mask = 0;
        Check(!player.Sweep(new Vector3(-2, 0, 0), right, 4, out _), "Player collision masks are preserved");
        player.Mask = 1 << 7;
        Check(!player.Sweep(new Vector3(-2, 0, 0), right, 4, out _), "A nonzero mask excluding the monster layer also stays clear");
        player.Mask = ~0;
        player.Height = 2;
        Check(!player.Sweep(new Vector3(-2, 0, 0), right, 4, out _), "Vertically separated bodies do not block each other");
        player.Height = 0;
        monster.SetCurrentHealth(0);
        Check(!player.Sweep(new Vector3(-2, 0, 0), right, 4, out _), "Death releases player collision immediately");
        monster.SetCurrentHealth(monster.MaxHealth);
        Tick();
        Check(player.Sweep(new Vector3(-2, 0, 0), right, 4, out _), "Health restoration re-registers collision");
        monster.Root.GetComponent<CapsuleCollider>().enabled = false;
        Check(!player.Sweep(new Vector3(-2, 0, 0), right, 4, out _), "A disabled authored collider is not a hidden ECS obstacle");
        monster.Root.GetComponent<CapsuleCollider>().enabled = true;
        Check(!player.Sweep(new Vector3(-.1f, 0, 0), new Vector3(-1, 0, 0), .2f, out _),
            "An initially overlapping player can escape");
        Check(player.Sweep(new Vector3(-.1f, 0, 0), right, .05f, out hit) && hit.distance == 0,
            "An initially overlapping player cannot move deeper");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) player.Sweep(new Vector3(-2, 0, 0), right, 4, out _);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Warm player-to-monster sweeps allocate zero bytes");
        monster.Root.activeInHierarchy = false;
        Tick();
        Check(!player.Sweep(new Vector3(-2, 0, 0), right, 4, out _), "Inactive actors leave the collision index");

        terrain = Reset();
        var target = new Player { Health = new CombatHealth(100000f) };
        target.transform.position = new Vector3(4, 0, 0);
        GameManager.Instance.Player = target;
        var blocker = Spawn(terrain, default);
        var mover = Spawn(terrain, new Vector3(-2, 0, 0), 2);
        for (int i = 0; i < 240; i++)
        {
            Tick();
            Check(Distance(mover.Root.transform.position, blocker.Root.transform.position) >= .4999f,
                "Monsters do not intersect while approaching or passing a stationary neighbor");
            Check(Distance(mover.Root.transform.position, target.transform.position) >= .4499f,
                "Monster movement cannot enter the player's capsule");
        }
        Check(mover.Root.transform.position.x > .6f, "A head-on blocked monster sidesteps instead of remaining stuck: "
            + mover.Root.transform.position.x + "," + mover.Root.transform.position.z);
        Check(target.Hits > 0, "Short-range attacks still work at body contact without requiring penetration");

        terrain = Reset();
        target = new Player { Health = new CombatHealth(100000f) };
        target.transform.position = new Vector3(4, 0, 0);
        GameManager.Instance.Player = target;
        mover = Spawn(terrain, new Vector3(-2, 0, 0), 600);
        Tick();
        Check(Distance(mover.Root.transform.position, target.transform.position) >= .4499f,
            "A monster moving faster than a body diameter per tick cannot tunnel into the player");

        terrain = Reset();
        var first = Spawn(terrain, default, 2);
        var second = Spawn(terrain, default, 2);
        Tick(90);
        Check(Distance(first.Root.transform.position, second.Root.transform.position) >= .4999f,
            "Coincident spawn bodies separate without a zero-direction deadlock");
        terrain = Reset();
        target = new Player { Health = new CombatHealth(100000f) };
        GameManager.Instance.Player = target;
        GameManager.Instance.SetCloakingMode(true);
        mover = Spawn(terrain, default, 2);
        Tick(90);
        Check(Distance(mover.Root.transform.position, target.transform.position) >= .4499f,
            "Cloaking disables targeting, not physical overlap correction");
        Check(target.Hits == 0, "Collision correction cannot bypass cloaking and deal attack damage");
        terrain = Reset();
        mover = Spawn(terrain, default);
        Tick();
        Vector3 unchanged = mover.Root.transform.position;
        MonsterWorld.Current.ManagedUpdateTick(0f);
        Check(Distance(mover.Root.transform.position, unchanged) == 0f, "Zero elapsed time cannot move collision bodies");
        before = GC.GetAllocatedBytesForCurrentThread();
        Tick(1000);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Warm stationary collision simulation allocates zero bytes");
        CheckTargetQueries();
        Console.WriteLine($"PASS: {checks} production agent movement/player sweep checks");
        MonsterWorld.Current?.Dispose();
        GameManager.Instance.SetCloakingMode(false);
    }

    private static void CheckTargetQueries()
    {
        var terrain = Reset();
        var near = Spawn(terrain,new Vector3(-3,0,-2));
        var far = Spawn(terrain,new Vector3(6,0,0));
        var world = MonsterWorld.Current;
        Vector3 origin = new Vector3(0,.5f,0);
        Check(world.TryFindNearestAttackTarget(origin,8,out var found) && ReferenceEquals(found,near),
            "Tower spatial query selects the nearest living monster across negative grid cells");
        near.SetCurrentHealth(0);
        Check(world.TryFindNearestAttackTarget(origin,8,out found) && ReferenceEquals(found,far),
            "Tower spatial query skips dead monsters before their next world cleanup tick");
        Check(world.TryFindNearestAttackTarget(origin,5.8f,out found) && ReferenceEquals(found,far),
            "Tower range measures the target body surface instead of its center");
        Check(!world.TryFindNearestAttackTarget(origin,5.7f,out _), "Tower spatial query rejects out-of-range targets");
        Check(!world.TryFindNearestAttackTarget(new Vector3(0,20,0),8,out _), "Vertical separation also counts toward firing range");
        Check(!world.TryFindNearestAttackTarget(origin,float.NaN,out _) && !world.TryFindNearestAttackTarget(origin,float.PositiveInfinity,out _),
            "Invalid ranges cannot cause an unbounded spatial cell scan");
        for(int i=0;i<100;i++) world.TryFindNearestAttackTarget(origin,8,out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++) world.TryFindNearestAttackTarget(origin,8,out _);
        Check(GC.GetAllocatedBytesForCurrentThread()==before, "Warm tower enemy searches allocate zero managed bytes");
        world.Dispose();
        Check(!world.TryFindNearestAttackTarget(origin,8,out _), "Disposed monster worlds cannot supply stale targets");
    }
}
