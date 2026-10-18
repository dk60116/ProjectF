using System;
using ProjectF.Monsters;
using ProjectF.Simulation.Monsters;
using UnityEngine;

internal static class RuntimeChecks
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    private static int Main()
    {
        try
        {
            PlayerCombat();
            BuildingCombat();
            BlockerCombat();
            AttackTargetCommitment();
            PlayerCloaking();
            StructurePriorities();
            TargetSafety();
            TowerDefinitionBinding();
            PauseAndLeash();
            MultipleActors();
            ForestTraversal();
            MovementFacing();
            MovementRecovery();
            MapObjectHealth();
            WreckagePlacementChecks.Run();
            BrushChecks.Run();
            CollisionChecks.Run();
            AgentBodyChecks.Run();
            SwarmChecks.Run();
            KnifeChecks.Run();
            HealthBarChecks.Run();
            Console.WriteLine($"PASS: {checks} production monster world/grid combat checks");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally { MonsterWorld.Current?.Dispose(); }
    }

    private static TerrainGenerator Reset()
    {
        MonsterWorld.Current?.Dispose();
        MapObjectTickManager.SimulationPaused = MapObjectTickManager.WaitingForWorldLoad = false;
        SaveManager.GameplayInputBlocked = false;
        GameManager.Instance.Player = null;
        GameManager.Instance.SetCloakingMode(false);
        PipeWorld.Current = null;
        ConveyorWorld.Current = null;
        GameManager.Instance.ItemManger.Definitions.Clear();
        return new TerrainGenerator();
    }

    private static void MapObjectHealth()
    {
        var terrain = Reset();
        Vector2Int key = new(2, 0);
        var building = terrain.AddBuilding(key, 501L, key);
        Check(terrain.TryGetMapObjectHealth(building, out float current, out float maximum)
            && current == 100f && maximum == 100f, "Pristine map objects default to 100 HP");
        building.BoundItemDefinition.maxHealth = 250f;
        Check(terrain.TryGetInstallationCombatTarget(key, out var target), "Authored health target acquired");
        Check(terrain.DamageInstallation(target, 30f) == 30f
            && terrain.TryGetMapObjectHealth(building, out current, out maximum)
            && current == 220f && maximum == 250f, "Per-item authored maximum controls damage");
        Check(terrain.Store.DamagedInstallations.Count == 1, "Only damaged placements enter presentation index");
        building.BoundItemDefinition.excludeFromMonsterTargets = true;
        Check(!terrain.TryGetInstallationCombatTarget(key, out _)
            && terrain.DamageInstallation(target, 10f) == 0f, "Exclusion toggle invalidates an acquired monster target");
        Check(terrain.TryGetMapObjectHealth(building, out current, out _) && current == 220f,
            "Exclusion is not invulnerability or removal of the health concept");
        Check(!terrain.SetMapObjectHealth(building, 0f) && !terrain.SetMapObjectHealth(building, float.NaN),
            "Restore API cannot revive wrecks or write invalid health");
        Check(terrain.SetMapObjectHealth(building, 250f) && terrain.Store.DamagedInstallations.Count == 0,
            "Full HP removes overhead bar tracking");
        Check(terrain.SetMapObjectHealth(building, 37f)
            && terrain.TryGetMapObjectHealth(building, out current, out _) && current == 37f,
            "Recovered installation restores its individual HP");
        building.BoundItemDefinition.excludeFromMonsterTargets = false;
        terrain.TryGetInstallationCombatTarget(key, out target);
        Check(terrain.DamageInstallation(target, 100f) == 37f && terrain.Store.Wreckage.Count == 1,
            "Overkill applies only remaining HP and leaves one wreckage record");
        var wreck = terrain.Store.Wreckage[0];
        Check(wreck.hasCombatHealth && wreck.combatHealth == 0f && wreck.placementSequence == 501L,
            "Wreck retains dead identity and appearance, not live simulation");
        Check(terrain.Store.DamagedInstallations.Count == 0 && terrain.Blocks[key].Contents == 0,
            "Destruction removes the overhead bar and internal contents");
        Check(terrain.DamageInstallation(target, 100f) == 0f && terrain.Store.Wreckage.Count == 1,
            "Repeated damage cannot duplicate wreckage");
        Check(terrain.Store.RemoveWreckage(501L) && !terrain.Store.RemoveWreckage(501L),
            "Wreckage can be cleared exactly once without returning an installation item");
        terrain.AddBuilding(key, 502L, key);
        Check(terrain.TryGetMapObjectHealth(terrain.Blocks[key].MapObject, out current, out _) && current == 100f,
            "A replacement placement starts at full HP");

        terrain = Reset();
        terrain.Blocks[key] = new Block { MapObject = new Resource() };
        Check(!terrain.TryGetInstallationCombatTarget(key, out _), "Natural resources are not monster combat goals");
        Check(terrain.TryGetMapObjectHealth(terrain.Blocks[key].MapObject, out current, out maximum)
            && current == maximum && maximum == 100f, "Natural resource HP is independent of harvesting quantity");
        var resource = new ResourceInstance { ResourceCount = 42, CurrentHealth = 73f, MaximumHealth = 200f };
        Check(terrain.TryGetMapObjectHealth(resource, out current, out maximum) && current == 73f && maximum == 200f
            && resource.ResourceCount == 42, "Resource instances expose independent HP without changing harvest quantity");

        terrain = Reset();
        var moving = terrain.AddBuilding(key, 509L, key);
        moving.BoundItemDefinition.mapObject = new Vehicle();
        terrain.TryGetInstallationCombatTarget(key, out target);
        moving.WorldPosition = new Vector3(20f, 0f, 0f);
        Check(terrain.TryRefreshCombatTarget(target, out var refreshed) && refreshed.ClosestPoint(Vector3.zero).x == 20f,
            "Moving vehicles refresh the placement identity and attack position, not the original footprint");

        foreach (bool pipe in new[] { false, true })
        {
            terrain = Reset();
            terrain.AddBuilding(key, 510L, key);
            FixtureBuilding infrastructure = pipe ? new PipeRuntimeRecord() : new ConveyorRuntimeRecord();
            infrastructure.SimulationId = 510L;
            infrastructure.WorldPosition = new Vector3(2f, 0f, 0f);
            infrastructure.Traversable = true;
            terrain.Store.Buildings[key] = infrastructure;
            // A shared authoring prefab, rather than the individual record, is bound to the cell.
            terrain.Blocks[key].MapObject = new MapObject { Traversable = true };
            if (pipe) { PipeWorld.Current = new PipeWorld(); PipeWorld.Current.Records[key] = (PipeRuntimeRecord)infrastructure; }
            else { ConveyorWorld.Current = new ConveyorWorld(); ConveyorWorld.Current.Records[key] = (ConveyorRuntimeRecord)infrastructure; }
            Check(terrain.TryGetInstallationCombatTarget(key, out target)
                && ReferenceEquals(target.Target, infrastructure), "Infrastructure resolves its individual runtime identity");
            Check(terrain.DamageInstallation(target, 10f) == 10f && terrain.Store.States[key].combatHealth == 90f,
                "Traversable infrastructure takes damage through canonical placement health");
            terrain.Blocks[key].MapObject = pipe ? ((PipeRuntimeRecord)infrastructure).Prototype : ((ConveyorRuntimeRecord)infrastructure).Prototype;
            Check(terrain.DamageInstallation(target, 100f) == 90f && terrain.Store.Wreckage.Count == 1
                && terrain.Blocks[key].MapObject == null && terrain.Blocks[key].Contents == 0,
                "Destroyed infrastructure clears shared prototype bindings and inventory, leaving nonfunctional wreckage");
            Check(!terrain.TryGetInstallationCombatTarget(key, out _), "Destroyed infrastructure cannot be acquired again");
        }
    }

    private static MonsterInstance Spawn(TerrainGenerator terrain, MonsterStats? overrideStats = null, Vector3? position = null)
    {
        var definition = new MonsterDefinition { Stats = overrideStats ?? MonsterStats.Default };
        var root = FixtureMonsterPrefab.Create();
        root.transform.position = position ?? Vector3.zero;
        var monster = new MonsterInstance(root, definition, 1);
        MonsterWorld.Register(monster, definition, terrain);
        Check(ReferenceEquals(MapObjectTickManager.Registered, MonsterWorld.Current), "One world owns fixed tick registration");
        return monster;
    }

    private static Player PutPlayer(float x, float z = 0f)
    {
        var player = new Player();
        player.transform.position = new Vector3(x, 0f, z);
        GameManager.Instance.Player = player;
        return player;
    }

    private static void MovementFacing()
    {
        TerrainGenerator terrain = Reset();
        PutPlayer(4f);
        MonsterStats stats = MonsterStats.Default;
        stats.TurnSpeed = 90f;
        MonsterInstance monster = Spawn(terrain, stats);
        Advance(1);
        Check(MathF.Abs(monster.Root.transform.rotation.YawDegrees - 1.5f) < 0.001f,
            "Moving actor turns only once per tick, at authored turn speed");

        terrain = Reset();
        for (int z = -1; z <= 1; z++) terrain.Walls.Add(new Vector2Int(1, z));
        PutPlayer(4f);
        monster = Spawn(terrain, stats);
        int detourSteps = 0;
        for (int i = 0; i < 90; i++)
        {
            Vector3 before = monster.Root.transform.position;
            Quaternion rotation = monster.Root.transform.rotation;
            Advance(1);
            Vector3 movement = monster.Root.transform.position - before;
            if (movement.sqrMagnitude <= 0.000001f) continue;
            Quaternion expected = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(movement), 1.5f);
            Check(MathF.Abs(monster.Root.transform.rotation.YawDegrees - expected.YawDegrees) < 0.001f,
                "Obstacle detour faces actual displacement, without first turning toward the target");
            if (MathF.Abs(movement.z) > 0.001f) detourSteps++;
        }
        Check(detourSteps > 10, "Facing regression exercises an actual obstacle detour");

        terrain = Reset();
        PutPlayer(1f);
        monster = Spawn(terrain, stats);
        Advance(1);
        Check(monster.Root.transform.position.sqrMagnitude == 0f
            && MathF.Abs(monster.Root.transform.rotation.YawDegrees - 1.5f) < 0.001f,
            "Stationary attacker still faces its target");

        terrain = Reset();
        PutPlayer(4f);
        stats.MoveSpeed = 0f;
        monster = Spawn(terrain, stats);
        Advance(1);
        Check(monster.Root.transform.position.sqrMagnitude == 0f
            && monster.Root.transform.rotation.YawDegrees == 0f,
            "No displacement outside attack range preserves heading instead of tracking the target");

        foreach (int tickRate in new[] { 30, 60, 120, 240 })
        {
            terrain = Reset();
            PutPlayer(4f);
            stats.MoveSpeed = 0.5f;
            monster = Spawn(terrain, stats);
            float dt = 1f / tickRate;
            MonsterWorld.Current.ManagedUpdateTick(dt);
            Check(monster.Root.transform.position.x > 0f
                && MathF.Abs(monster.Root.transform.rotation.YawDegrees - stats.TurnSpeed * dt) < 0.001f,
                "Authored slow Bat/Slime movement still turns at " + tickRate + " ticks per second");
        }
    }

    private static void MovementRecovery()
    {
        TerrainGenerator terrain = Reset();
        for (int z = -1; z <= 1; z++) terrain.Walls.Add(new Vector2Int(2, z));
        Player player = PutPlayer(6f);
        player.Health = new CombatHealth(100000f);
        MonsterInstance monster = Spawn(terrain);
        Advance(360);
        Check(player.Hits > 0, "Recovery scenario first pursues player around an obstacle");
        player.transform.position = new Vector3(30f, 0f, 0f);
        Vector3 abandoned = monster.Root.transform.position;
        Advance(900);
        Check(Vector3.Distance(monster.Root.transform.position, abandoned) <= 4.2f,
            "Losing a target begins local roaming instead of retracing the path to spawn");
        int hits = player.Hits;
        player.transform.position = new Vector3(6f, 0f, 0f);
        Advance(360);
        Check(player.Hits > hits, "Returned actor acquires the nearby player and resumes combat");

        foreach (float speed in new[] { 0.5f, 2f })
        {
            terrain = Reset();
            Vector2Int key = new(4, 0);
            terrain.AddBuilding(key, 601L, key);
            MonsterStats stats = MonsterStats.Default;
            stats.MoveSpeed = speed;
            stats.AttackRange = 0.5f;
            stats.AttackDamage = 1f;
            Spawn(terrain, stats);
            Advance(900);
            Check(terrain.Store.States[key].hasCombatHealth,
                "Short-range attacker reaches a building and attacks at speed " + speed);

            terrain = Reset();
            player = PutPlayer(4f);
            stats.AttackRange = 0.1f;
            Spawn(terrain, stats);
            Advance(900);
            Check(player.Hits > 0,
                "Player pursuit stops at attack range, not the home-arrival distance, at speed " + speed);
        }
    }

    private static void Advance(int ticks)
    {
        for (int i = 0; i < ticks; i++) MonsterWorld.Current.ManagedUpdateTick(1f / 60f);
    }

    private static void PlayerCombat()
    {
        TerrainGenerator terrain = Reset();
        Player player = PutPlayer(4f);
        MonsterInstance monster = Spawn(terrain);
        Advance(60);
        Check(monster.Root.transform.position.x > 1f && player.Hits == 0,
            "Monster approaches player before attacking");
        Advance(900);
        Check(player.IsDead && player.Health.Current == 0f && player.Hits == 10,
            "Actual attack events kill player with exactly ten 10-damage hits");
        int hits = player.Hits;
        Advance(300);
        Check(player.IsDead && player.Hits == hits, "Dead player is not repeatedly attacked or automatically revived");
        Check(monster.Root.AttackAnimations >= hits, "Attack animation is triggered for damage cycles");
        Check(player.Health.Revive(), "Player can be manually revived");
        Advance(180);
        Check(player.Health.Current < 100f, "Manually revived player can be targeted again");
        terrain = Reset();
        player = PutPlayer(1f);
        monster = Spawn(terrain);
        Advance(1);
        Check(player.Hits == 0, "Attack begins with a wind-up");
        player.transform.position = new Vector3(20f, 0f, 0f);
        Advance(120);
        Check(player.Hits == 0, "Moving outside range before impact prevents damage");
    }

    private static void BuildingCombat()
    {
        TerrainGenerator terrain = Reset();
        Vector2Int key = new(4, 0);
        var building = terrain.AddBuilding(key, 10L, key, new Vector2Int(5, 0));
        Spawn(terrain);
        Advance(180);
        Check(terrain.Store.States[key].hasCombatHealth && terrain.Store.States[key].combatHealth < 100f,
            "Data-only multi-cell building receives actual persistent health damage");
        Advance(900);
        Check(!building.Active && terrain.Store.Removals == 1 && !terrain.Store.States.ContainsKey(key),
            "Lethal damage removes canonical installation exactly once");
        Check(terrain.Blocks[key].MapObject == null && terrain.Blocks[new(5, 0)].MapObject == null,
            "Destruction clears every occupied cell binding");
        Check(terrain.CanAnimalMoveTo(new Vector3(4f, 0f, 0f), true), "Destroyed footprint becomes traversable");

        terrain = Reset();
        building = terrain.AddBuilding(key, 11L, key);
        building.Traversable = true;
        Spawn(terrain);
        Advance(600);
        Check(terrain.Store.States[key].hasCombatHealth,
            "Traversable infrastructure is also a monster attack goal");

        terrain = Reset();
        terrain.AddBuilding(new(0, 2), 12L, new Vector2Int(0, 2));
        Player player = PutPlayer(3f);
        Spawn(terrain);
        Advance(180);
        Check(player.Hits > 0 && !terrain.Store.States[new(0, 2)].hasCombatHealth,
            "Reachable player takes priority over a closer building");
    }

    private static void BlockerCombat()
    {
        TerrainGenerator terrain = Reset();
        for (int z = -12; z <= 12; z++) if (z != 0) terrain.Walls.Add(new Vector2Int(1, z));
        var wall = terrain.AddBuilding(new(1, 0), 13L, new Vector2Int(1, 0));
        Player player = PutPlayer(3f);
        MonsterStats stats = MonsterStats.Default;
        stats.AttackDamage = 100f;
        Spawn(terrain, stats);
        Advance(600);
        Check(!wall.Active, "No path to player causes monster to attack blocking installation");
        Check(player.Hits > 0, "Monster resumes player pursuit after destroying blocker");
    }

    private static void AttackTargetCommitment()
    {
        TerrainGenerator terrain = Reset();
        Vector2Int key = new(1, 0);
        terrain.AddBuilding(key, 701L, key);
        MonsterInstance monster = Spawn(terrain);
        Advance(1);
        Player player = PutPlayer(0f, 1f);
        Advance(30);
        Check(terrain.Store.States[key].combatHealth == 90f && player.Hits == 0,
            "Player entering detection cannot steal a building attack during its wind-up");
        Check(MathF.Abs(monster.Root.transform.rotation.YawDegrees - 90f) < 0.001f,
            "Building attack and facing retain the same target throughout recovery");
        Advance(100);
        Check(player.Hits > 0 && terrain.Store.States[key].combatHealth == 90f,
            "Reachable player takes priority at the next attack boundary, without permanent target locking");

        terrain = Reset();
        terrain.AddBuilding(key, 702L, key);
        for (int x = -12; x <= 12; x++) terrain.Walls.Add(new Vector2Int(x, 1));
        monster = Spawn(terrain);
        Advance(1);
        player = PutPlayer(0f, 3f);
        Advance(150);
        Check(terrain.Store.States[key].combatHealth == 70f && player.Hits == 0,
            "Occluded player cannot repeatedly interrupt building attacks at cycle boundaries");
        Check(MathF.Abs(monster.Root.transform.rotation.YawDegrees - 90f) < 0.001f,
            "Facing does not oscillate between a building and an occluded player");
        terrain.Walls.Clear();
        terrain.InvalidateAnimalNavigation();
        Advance(180);
        Check(player.Hits > 0, "Opening the route lets a reachable player replace the building target");

        terrain = Reset();
        terrain.AddBuilding(key, 703L, key);
        monster = Spawn(terrain);
        Advance(1);
        terrain.TryGetInstallationCombatTarget(key, out var target);
        terrain.DamageInstallation(target, 100f);
        player = PutPlayer(0f, 4f);
        Advance(30);
        Check(monster.Root.transform.position.z > 0f
            && monster.Root.AnimationState == MonsterAnimationState.Move && player.Hits == 0,
            "Destroyed attack target cancels its pose before movement/facing switch, while retaining cooldown");
        Advance(180);
        Check(player.Hits > 0, "Cancelled attacker resumes combat rather than remaining locked in recovery");
    }

    private static void PlayerCloaking()
    {
        TerrainGenerator terrain = Reset();
        Player player = PutPlayer(4f);
        GameManager.Instance.SetCloakingMode(true);
        MonsterInstance monster = Spawn(terrain);
        Advance(120);
        Check(Vector3.Distance(monster.Root.transform.position, Vector3.zero) <= 4.2f && player.Hits == 0,
            "Cloaked player is ignored while the monster roams locally");
        GameManager.Instance.SetCloakingMode(false);
        Advance(60);
        Check(monster.Root.transform.position.x > 0f, "Uncloaking restores player acquisition");
        float x = monster.Root.transform.position.x;
        GameManager.Instance.SetCloakingMode(true);
        Advance(1);
        Check(monster.Root.transform.position.x == x && player.Hits == 0,
            "Cloaking drops the chase immediately and starts a local roaming pause rather than retreating");

        terrain = Reset();
        player = PutPlayer(1f);
        monster = Spawn(terrain);
        Advance(1);
        GameManager.Instance.SetCloakingMode(true);
        Advance(1);
        Check(monster.Root.AnimationState == MonsterAnimationState.Idle,
            "Cloaking cancels an acquired player's attack pose during wind-up");
        Advance(90);
        Check(player.Hits == 0 && player.Health.Current == 100f,
            "Pending monster hit is cancelled instead of damaging a newly cloaked player");
        GameManager.Instance.SetCloakingMode(false);
        Advance(90);
        Check(player.Hits > 0, "Uncloaking after cancelled wind-up resumes normal combat");

        terrain = Reset();
        player = PutPlayer(1f);
        Vector2Int key = new(0, 1);
        terrain.AddBuilding(key, 801L, key);
        GameManager.Instance.SetCloakingMode(true);
        Spawn(terrain);
        Advance(30);
        Check(terrain.Store.States[key].combatHealth == 90f && player.Hits == 0,
            "Cloaking excludes only the player; monsters still attack eligible buildings");
    }

    private static void StructurePriorities()
    {
        TerrainGenerator terrain = Reset();
        Vector2Int lowKey = new(1, 0), highKey = new(0, 3), farKey = new(0, 5);
        terrain.AddBuilding(lowKey, 901L, lowKey).BoundItemDefinition.monsterAttackPriority = 1;
        var high = terrain.AddBuilding(highKey, 902L, highKey);
        high.BoundItemDefinition.monsterAttackPriority = 2;
        var far = terrain.AddBuilding(farKey, 903L, farKey);
        far.BoundItemDefinition.monsterAttackPriority = 2;
        MonsterStats stats = MonsterStats.Default;
        stats.AttackDamage = 1f;
        Spawn(terrain, stats);
        Advance(120);
        Check(terrain.Store.States[highKey].hasCombatHealth
            && !terrain.Store.States[lowKey].hasCombatHealth && !terrain.Store.States[farKey].hasCombatHealth,
            "Priority 2 beats a nearer priority 1; equal priorities choose the nearest structure");

        terrain = Reset();
        Vector2Int zeroKey = new(0, 1);
        var zero = terrain.AddBuilding(zeroKey, 904L, zeroKey);
        zero.BoundItemDefinition.monsterAttackPriority = 0;
        MonsterInstance monster = Spawn(terrain);
        Advance(120);
        Check(!terrain.Store.States[zeroKey].hasCombatHealth && monster.Root.transform.position.sqrMagnitude <= 4.2f * 4.2f,
            "A roaming monster avoids nearby priority-zero structures without treating them as unsolicited combat goals");

        terrain = Reset();
        zeroKey = new(0, 2);
        zero = terrain.AddBuilding(zeroKey, 905L, zeroKey);
        zero.BoundItemDefinition.monsterAttackPriority = 0;
        Player player = PutPlayer(4f);
        Spawn(terrain);
        Advance(180);
        Check(!terrain.Store.States[zeroKey].hasCombatHealth && player.Hits > 0,
            "Priority-zero structure off the pursuit path is left alone");

        terrain = Reset();
        zeroKey = new(2, 0);
        zero = terrain.AddBuilding(zeroKey, 906L, zeroKey);
        zero.BoundItemDefinition.monsterAttackPriority = 0;
        player = PutPlayer(4f);
        Check(AnimalGridPathfinder.FindPath(terrain, Vector3.zero, player.transform.position,
            Vector3.zero, stats.LeashRange, true, new Vector3[128], terrain.CanMonsterMoveTo) > 0,
            "Priority-zero blocking scenario has a valid detour, not just a fully sealed route");
        monster = Spawn(terrain, stats);
        Advance(180);
        Check(terrain.Store.States[zeroKey].combatHealth < 100f && player.Hits == 0,
            "A priority-zero blocker is attacked rather than taking the available detour");
        terrain.TryGetInstallationCombatTarget(zeroKey, out var blocker);
        terrain.DamageInstallation(blocker, 100f);
        Advance(180);
        Check(player.Hits > 0, "Destroying a priority-zero blocker resumes the original player pursuit");

        terrain = Reset();
        zero = terrain.AddBuilding(zeroKey, 907L, zeroKey);
        zero.BoundItemDefinition.monsterAttackPriority = 0;
        zero.BoundItemDefinition.excludeFromMonsterTargets = true;
        player = PutPlayer(4f);
        Spawn(terrain);
        Advance(360);
        Check(!terrain.Store.States[zeroKey].hasCombatHealth && player.Hits > 0,
            "Absolute exclusion still forces a detour instead of an obstacle attack");

        terrain = Reset();
        zero = terrain.AddBuilding(zeroKey, 908L, zeroKey);
        zero.BoundItemDefinition.monsterAttackPriority = 0;
        terrain.Water.Add(new Vector2Int(1, 0));
        Check(!terrain.TryGetMonsterPathBlocker(Vector3.zero, new Vector3(4f, 0f, 0f), out _),
            "Path-blocker detection stops at water rather than attacking a structure behind it");

        terrain = Reset();
        player = PutPlayer(6f);
        player.Health = new CombatHealth(100000f);
        monster = Spawn(terrain);
        Advance(360);
        zero = terrain.AddBuilding(zeroKey, 909L, zeroKey);
        zero.BoundItemDefinition.monsterAttackPriority = 0;
        player.transform.position = new Vector3(30f, 0f, 0f);
        Vector3 abandoned = monster.Root.transform.position;
        Advance(900);
        Check(zero.Active && !terrain.Store.States[zeroKey].hasCombatHealth
            && Vector3.Distance(monster.Root.transform.position, abandoned) <= 4.2f,
            "Local roaming avoids priority-zero buildings rather than destroying them on a route back to spawn");
    }

    private static void TargetSafety()
    {
        TerrainGenerator terrain = Reset();
        Vector2Int key = new(1, 0);
        terrain.AddBuilding(key, 14L, key);
        Check(terrain.TryGetInstallationCombatTarget(key, out var old), "Combat target resolved from occupied grid cell");
        terrain.AddBuilding(key, 15L, key);
        Check(terrain.DamageInstallation(old, 100f) == 0f && terrain.Store.Removals == 0,
            "Stale target cannot destroy a replacement at the same coordinates");
        terrain.TryGetInstallationCombatTarget(key, out var current);
        Check(terrain.DamageInstallation(current, float.NaN) == 0f
            && terrain.DamageInstallation(current, float.PositiveInfinity) == 0f,
            "Invalid installation damage is rejected");
        Check(terrain.DamageInstallation(current, 20f) == 20f
            && terrain.Store.States[key].combatHealth == 80f, "Nonlethal damage updates canonical state");
        MapObjectTickManager.SimulationPaused = true;
        Check(terrain.DamageInstallation(current, 20f) == 0f
            && terrain.Store.States[key].combatHealth == 80f, "Paused installation damage is ignored");

        terrain = Reset();
        var machine = terrain.AddBuilding(key, 16L, key, new Vector2Int(2, 0));
        var pipeKey = new Vector2Int(2, 0);
        var pipe = terrain.AddBuilding(pipeKey, 17L, pipeKey);
        terrain.Store.Coordinates[key] = pipeKey;
        Check(terrain.TryGetInstallationCombatTarget(key, out current),
            "Overlapped I/O coordinate still resolves the selected installation identity");
        terrain.DamageInstallation(current, 100f);
        Check(!machine.Active && pipe.Active && terrain.Store.States.ContainsKey(pipeKey)
            && ReferenceEquals(terrain.Blocks[pipeKey].MapObject, pipe),
            "Destroying machine cannot remove overlapping pipe or its grid binding");
    }

    private sealed class TowerDefinitionTarget : IMapObjectTarget, IMapObjectSimulationIdentity
    {
        internal FixtureBuilding Owner;
        internal bool Bound;
        public long SimulationId => Owner.SimulationId;
        public bool IsTargetActive => Owner.IsTargetActive;
        public bool AllowsAnimalTraversal => false;
        public Vector3 WorldPosition => Owner.WorldPosition;
        public ItemDefinition BoundItemDefinition => Bound ? Owner.BoundItemDefinition : null;
    }

    private static void TowerDefinitionBinding()
    {
        var terrain = Reset();
        var cell = new Vector2Int(4,0);
        var owner = terrain.AddBuilding(cell,902L,cell);
        owner.BoundItemDefinition.monsterAttackPriority=3;
        var tower = new TowerDefinitionTarget { Owner=owner };
        terrain.Blocks[cell].MapObject=tower;
        Check(!terrain.TryGetInstallationCombatTarget(cell,out _),
            "A missing Tower ItemDefinition reproduces the combat target rejection despite a valid placement");
        var stats=MonsterStats.Default;
        stats.DetectionRange=20; stats.LeashRange=32; stats.AttackInterval=.2f; stats.AttackDamage=20;
        Spawn(terrain,stats);
        Advance(120);
        Check(terrain.Store.Removals==0 && !terrain.Store.States[cell].hasCombatHealth,
            "With only an unbound Tower present monsters cannot damage it");
        tower.Bound=true;
        Check(terrain.TryGetInstallationCombatTarget(cell,out var target) && ReferenceEquals(target.Target,tower),
            "Binding the canonical definition immediately makes the same placed Tower targetable");
        Advance(900);
        Check(terrain.Store.Removals==1 && !owner.Active,
            "The real monster world acquires, approaches, attacks and destroys a bound priority-three Tower without other targets");
    }

    private static void PauseAndLeash()
    {
        TerrainGenerator terrain = Reset();
        Player player = PutPlayer(4f);
        MonsterInstance monster = Spawn(terrain);
        Advance(30);
        float x = monster.Root.transform.position.x;
        MapObjectTickManager.SimulationPaused = true;
        Advance(300);
        Check(monster.Root.transform.position.x == x && player.Hits == 0,
            "Paused world freezes movement and attack timers");
        MapObjectTickManager.SimulationPaused = false;
        MapObjectTickManager.WaitingForWorldLoad = true;
        Advance(300);
        Check(monster.Root.transform.position.x == x && player.Hits == 0,
            "World loading barrier freezes combat");
        MapObjectTickManager.WaitingForWorldLoad = false;
        player.transform.position = new Vector3(30f, 0f, 0f);
        Vector3 abandoned = monster.Root.transform.position;
        Advance(600);
        Check(Vector3.Distance(monster.Root.transform.position, abandoned) <= 4.2f && player.Hits == 0,
            "Out-of-leash targets are dropped while the monster stays near its last combat location");
        monster.Root.activeInHierarchy = false;
        Advance(1);
        MonsterWorld.Current.Dispose();
        Check(MapObjectTickManager.Registered == null, "World cleanup unregisters the simulation tick");
    }

    private static void MultipleActors()
    {
        TerrainGenerator terrain = Reset();
        Player player = PutPlayer(4f);
        player.Health = new CombatHealth(100000f);
        var monsters = new MonsterInstance[80];
        var starts = new Vector3[monsters.Length];
        for (int i = 0; i < 6; i++)
        {
            Vector2Int coordinate = new(-10 + i * 4, -8);
            terrain.AddBuilding(coordinate, 2000L + i, coordinate).BoundItemDefinition.maxHealth = 100000f;
        }
        MonsterStats crowdStats = MonsterStats.Default;
        crowdStats.DetectionRange = 32f;
        crowdStats.LeashRange = 64f;
        for (int i = 0; i < monsters.Length; i++)
        {
            starts[i] = new Vector3(-12f + (i % 10) * 1.2f, 0f, -4.2f + (i / 10) * 1.2f);
            monsters[i] = Spawn(terrain, crowdStats, starts[i]);
        }
        var served = new bool[monsters.Length];
        for (int tick = 0; tick < 120; tick++)
        {
            Advance(1);
            for (int i = 0; i < monsters.Length; i++)
                served[i] |= (monsters[i].Root.transform.position - starts[i]).sqrMagnitude > .01f;
        }
        var actorList = (System.Collections.IList)typeof(MonsterWorld)
            .GetField("actors", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(MonsterWorld.Current);
        for (int i = 0; i < monsters.Length; i++)
        {
            object actor = actorList[i];
            var rejected = (System.Collections.IDictionary)actor.GetType().GetField("RejectedTargets",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(actor);
            Check(served[i] || rejected != null && rejected.Count > 0,
                "Shared budget serves every actor, including actors declining a full attack perimeter");
        }

        terrain = Reset();
        Vector2Int key = new(1, 0);
        terrain.AddBuilding(key, 20L, key);
        MonsterStats stats = MonsterStats.Default;
        stats.AttackDamage = 100f;
        for (int i = 0; i < 4; i++) Spawn(terrain, stats);
        Advance(120);
        Check(terrain.Store.Removals == 1, "Simultaneous attackers cannot destroy a building multiple times");

        terrain = Reset();
        terrain.AddBuilding(key, 21L, key);
        Spawn(terrain);
        Advance(1);
        terrain.AddBuilding(key, 22L, key);
        Advance(30);
        Check(!terrain.Store.States[key].hasCombatHealth,
            "Replacing target during wind-up cancels old hit instead of damaging replacement");
    }

    private static void ForestTraversal()
    {
        TerrainGenerator terrain = Reset();
        Vector2Int treeCell = new(1, 0);
        Vector3 treePosition = new(1, 0, 0);
        terrain.Blocks[treeCell] = new Block { MapObject = new ProjectF.MapObjects.TreeInstance() };
        Check(!terrain.CanAnimalMoveTo(treePosition, true) && terrain.CanMonsterMoveTo(treePosition),
            "Loaded ECS tree blocks animals but permits monsters, with isolated caches");
        terrain.Blocks[treeCell].MapObject = new ProjectF.MapObjects.Tree();
        terrain.InvalidateAnimalNavigation(treeCell);
        Check(terrain.CanMonsterMoveTo(treePosition) && !terrain.CanAnimalMoveTo(treePosition, true),
            "Scene tree uses the same monster-only traversal policy");
        terrain.Blocks.Remove(treeCell);
        terrain.GeneratedResources[treeCell] = new ProjectF.MapObjects.Tree();
        terrain.InvalidateAnimalNavigation(treeCell);
        Check(terrain.CanMonsterMoveTo(treePosition) && !terrain.CanAnimalMoveTo(treePosition, false),
            "Unloaded generated forest matches loaded forest policy");
        terrain.ChangeAnimalNavigationObstacle(new RectInt(1, 0, 1, 1), true);
        Check(!terrain.CanMonsterMoveTo(treePosition), "Registered static obstacle still blocks a tree cell");
        terrain.ChangeAnimalNavigationObstacle(new RectInt(1, 0, 1, 1), false);
        Check(terrain.CanMonsterMoveTo(treePosition), "Removing explicit obstacle invalidates monster cache");
        terrain.GeneratedResources[treeCell] = new Resource();
        terrain.InvalidateAnimalNavigation(treeCell);
        Check(!terrain.CanMonsterMoveTo(treePosition), "Non-tree blocking resources are not made traversable");
        terrain.Store.Depleted.Add(treeCell);
        terrain.InvalidateAnimalNavigation(treeCell);
        Check(terrain.CanMonsterMoveTo(treePosition), "Depleted resource becomes traversable");
        terrain.Water.Add(treeCell);
        terrain.InvalidateAnimalNavigation(treeCell);
        Check(!terrain.CanMonsterMoveTo(treePosition), "Water remains blocked even with depleted/tree occupancy");
        Check(!terrain.CanMonsterMoveTo(new Vector3(65, 0, 0)), "Map boundary remains blocked");

        terrain = Reset();
        terrain.Blocks[treeCell] = new Block { MapObject = new ProjectF.MapObjects.TreeInstance() };
        Check(terrain.CanMonsterMoveTo(treePosition), "Warm monster-only navigation cache");
        long revision = terrain.AnimalNavigationRevision;
        terrain.Blocks[treeCell].MapObject = new MapObject();
        terrain.InvalidateAnimalNavigation(treeCell);
        Check(terrain.AnimalNavigationRevision > revision && !terrain.CanMonsterMoveTo(treePosition),
            "Monster-only cached cells advance revision and respect newly placed walls");
        terrain = Reset();
        terrain.AddBuilding(treeCell, 31L, treeCell);
        terrain.Blocks.Remove(treeCell);
        terrain.GeneratedResources[treeCell] = new ProjectF.MapObjects.Tree();
        Check(!terrain.CanMonsterMoveTo(treePosition), "Unloaded installation takes precedence over generated tree");

        terrain = Reset();
        for (int z = -1; z <= 1; z++)
        for (int x = -1; x <= 1; x++)
            if (x != 0 || z != 0)
                terrain.Blocks[new Vector2Int(x, z)] = new Block { MapObject = new ProjectF.MapObjects.TreeInstance() };
        Player player = PutPlayer(4f);
        player.Health = new CombatHealth(100000f);
        MonsterInstance monster = Spawn(terrain);
        Advance(360);
        Check(monster.Root.transform.position.x > 1f && player.Hits > 0,
            "Monster surrounded on all eight sides by trees escapes and attacks player");
        player.transform.position = new Vector3(30, 0, 0);
        Vector3 abandoned = monster.Root.transform.position;
        Advance(600);
        Check(Vector3.Distance(monster.Root.transform.position, abandoned) <= 4.2f,
            "After forest combat the monster roams near its last position rather than returning to spawn");

        terrain = Reset();
        terrain.Blocks[treeCell] = new Block { MapObject = new ProjectF.MapObjects.TreeInstance() };
        terrain.Blocks[new(0, 1)] = new Block { MapObject = new ProjectF.MapObjects.TreeInstance() };
        Vector3 diagonal = new(1, 0, 1);
        Check(!AnimalGridPathfinder.HasWalkableLine(terrain, Vector3.zero, diagonal, Vector3.zero, 12f, true)
            && AnimalGridPathfinder.HasWalkableLine(terrain, Vector3.zero, diagonal, Vector3.zero, 12f, true, terrain.CanMonsterMoveTo),
            "Diagonal shortcuts use the selected policy, not animal-only corner checks");
        terrain.Walls.Add(treeCell);
        terrain.Walls.Add(new(0, 1));
        terrain.InvalidateAnimalNavigation();
        Check(!AnimalGridPathfinder.HasWalkableLine(terrain, Vector3.zero, diagonal, Vector3.zero, 12f, true, terrain.CanMonsterMoveTo),
            "Two touching walls still prohibit diagonal corner cutting");

        terrain = Reset();
        for (int z = -1; z <= 1; z++)
        for (int x = -1; x <= 1; x++)
            if (x != 0 || z != 0)
                terrain.Blocks[new Vector2Int(x, z)] = new Block { MapObject = new ProjectF.MapObjects.TreeInstance() };
        terrain.Walls.Add(treeCell);
        player = PutPlayer(4f);
        monster = Spawn(terrain);
        bool enteredWall = false;
        for (int i = 0; i < 360; i++)
        {
            Advance(1);
            enteredWall |= TerrainGenerator.GetWorldBlockCoordinate(monster.Root.transform.position) == treeCell;
        }
        Check(!enteredWall && player.Hits > 0,
            "Production A* and smoothing route through forest around a solid wall");

        terrain = Reset();
        terrain.Blocks[new(3, 0)] = new Block { MapObject = new ProjectF.MapObjects.TreeInstance() };
        Vector2Int buildingKey = new(4, 0);
        terrain.AddBuilding(buildingKey, 32L, buildingKey);
        Spawn(terrain);
        Advance(240);
        Check(terrain.Store.States[buildingKey].hasCombatHealth,
            "Structure approach and attack-line checks agree with forest traversal");

        terrain = Reset();
        terrain.Blocks[treeCell] = new Block { MapObject = new ProjectF.MapObjects.TreeInstance() };
        terrain.CanMonsterMoveTo(treePosition);
        terrain.CanAnimalMoveTo(treePosition, true);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        bool valid = true;
        for (int i = 0; i < 10000; i++)
            valid &= terrain.CanMonsterMoveTo(treePosition) && !terrain.CanAnimalMoveTo(treePosition, true);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(valid && allocated == 0L, "Warmed monster/animal navigation lookups allocate zero bytes");
    }
}
