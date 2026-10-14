using System;
using ProjectF.Monsters;
using ProjectF.Simulation.Monsters;
using UnityEngine;

internal static class SwarmChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static TerrainGenerator Reset()
    {
        MonsterWorld.Current?.Dispose();
        GameManager.Instance.Player = null;
        GameManager.Instance.SetCloakingMode(false);
        MapObjectTickManager.SimulationPaused = MapObjectTickManager.WaitingForWorldLoad = false;
        SaveManager.GameplayInputBlocked = false;
        PipeWorld.Current = null;
        ConveyorWorld.Current = null;
        return new TerrainGenerator();
    }

    private static MonsterInstance Spawn(TerrainGenerator terrain, Vector3 position, float range = 1.5f,
        float speed = 2f, float radius = .25f, float detection = 16f, float leash = 32f,
        Vector3? bodyCenter = null, float height = 1f)
    {
        var stats = MonsterStats.Default;
        stats.AttackRange = range;
        stats.MoveSpeed = speed;
        stats.AttackDamage = 1f;
        stats.DetectionRange = detection;
        stats.LeashRange = leash;
        var definition = new MonsterDefinition { Stats = stats };
        var root = FixtureMonsterPrefab.Create();
        root.transform.position = position;
        var capsule = root.GetComponent<CapsuleCollider>();
        capsule.center = bodyCenter ?? new Vector3(0f, .5f, 0f);
        capsule.radius = radius;
        capsule.height = height;
        var monster = new MonsterInstance(root, definition, 1);
        MonsterWorld.Register(monster, definition, terrain);
        return monster;
    }

    private static Player Player()
    {
        var player = new Player { Health = new CombatHealth(100000f) };
        GameManager.Instance.Player = player;
        return player;
    }

    private static void Tick(int count)
    {
        for (int i = 0; i < count; i++) MonsterWorld.Current.ManagedUpdateTick(1f / 60f);
    }

    internal static void Run()
    {
        ExpandedDetection();
        RaidPriorities();
        RaidCleanup();
        PriorityThreePathBlockers();
        RaidContinuation();
        BuildingDiscovery();
        BlockedTargetOverflow();
        InfrastructureTargetIdentity();
        ConveyorRaid();
        var terrain = Reset();
        var player = Player();
        var swarm = new MonsterInstance[12];
        for (int i = 0; i < swarm.Length; i++)
            swarm[i] = Spawn(terrain, new Vector3(-5f - i % 2, 0f, -2.75f + i * .5f));
        for (int tick = 0; tick < 900; tick++)
        {
            Tick(1);
            for (int i = 0; i < swarm.Length; i++)
            for (int j = i + 1; j < swarm.Length; j++)
                Check(Vector3.Distance(swarm[i].Root.transform.position, swarm[j].Root.transform.position) >= .4998f,
                    "Swarm steering preserves collision separation");
        }
        int farSide = 0;
        for (int i = 0; i < swarm.Length; i++)
        {
            Check(swarm[i].Root.AttackAnimations > 0, "Every actor finds an attack position around the player: " + i);
            if (swarm[i].Root.transform.position.x > .1f) farSide++;
        }
        Check(farSide >= 2, "Actors arriving from one side spread around to the opposite side");
        Tick(120);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        Tick(600);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(allocated == 0, "Settled swarm attack and reservation reuse allocate no per-tick GC: " + allocated);
        player.transform.position = new Vector3(4f, 0f, 0f);
        Tick(900);
        foreach (var actor in swarm)
            Check(Vector3.Distance(actor.Root.transform.position, player.transform.position) <= 1.51f,
                "Moving target invalidates old approach reservations");

        terrain = Reset();
        Vector2Int machineKey = new(0, 0);
        terrain.AddBuilding(machineKey, 3000L, machineKey, new Vector2Int(1, 0),
            new Vector2Int(0, 1), new Vector2Int(1, 1)).BoundItemDefinition.maxHealth = 100000f;
        var machineSwarm = new MonsterInstance[16];
        for (int i = 0; i < machineSwarm.Length; i++)
            machineSwarm[i] = Spawn(terrain, new Vector3(-5f - (i % 4) * .75f, 0f, -1.5f + (i / 4)), .75f);
        Tick(1200);
        farSide = 0;
        foreach (var actor in machineSwarm)
        {
            Check(actor.Root.AttackAnimations > 0, "Actors attack around a multi-cell installation footprint");
            if (actor.Root.transform.position.x > 1.5f) farSide++;
        }
        Check(farSide > 0, "Installation crowd uses the far perimeter instead of stacking at the nearest edge");

        terrain = Reset();
        player = Player();
        var guards = new MonsterInstance[5];
        for (int i = 0; i < guards.Length; i++)
        {
            double angle = i * Math.PI * 2 / guards.Length;
            guards[i] = Spawn(terrain, new Vector3((float)Math.Cos(angle) * .4515f, 0f,
                (float)Math.Sin(angle) * .4515f), .1f, 0f);
        }
        Vector2Int highKey = new(-3, 3), lowKey = new(-4, -3);
        var high = terrain.AddBuilding(highKey, 3100L, highKey);
        high.BoundItemDefinition.monsterAttackPriority = 2;
        high.BoundItemDefinition.maxHealth = 100000f;
        var low = terrain.AddBuilding(lowKey, 3101L, lowKey);
        low.BoundItemDefinition.monsterAttackPriority = 1;
        low.BoundItemDefinition.maxHealth = 100000f;
        var overflow = Spawn(terrain, new Vector3(-5f, 0f, 0f), .5f);
        Tick(600);
        Check(terrain.Store.States[highKey].hasCombatHealth && !terrain.Store.States[lowKey].hasCombatHealth,
            "Full player perimeter redirects excess actors to the highest-priority available building");
        Check(overflow.Root.AttackAnimations > 0, "Overflow actor attacks an alternative instead of waiting forever");
        guards[0].SetCurrentHealth(0f);
        Tick(900);
        Check(Vector3.Distance(overflow.Root.transform.position, player.transform.position) <= .51f,
            "Death releases a reservation and rejected targets become eligible again");

        terrain = Reset();
        highKey = new Vector2Int(3, 0);
        lowKey = new Vector2Int(-6, 3);
        high = terrain.AddBuilding(highKey, 3200L, highKey);
        high.BoundItemDefinition.monsterAttackPriority = 3;
        terrain.AddBuilding(lowKey, 3201L, lowKey).BoundItemDefinition.maxHealth = 100000f;
        for (int z = -1; z <= 1; z++)
        for (int x = 2; x <= 4; x++)
            if (x != 3 || z != 0) terrain.Walls.Add(new Vector2Int(x, z));
        Spawn(terrain, Vector3.zero, .5f);
        Tick(600);
        Check(!terrain.Store.States[highKey].hasCombatHealth && terrain.Store.States[lowKey].hasCombatHealth,
            "An enclosed priority-three target does not starve a reachable priority-two target");

        terrain = Reset();
        highKey = new Vector2Int(6, 0);
        lowKey = new Vector2Int(-6, 3);
        high = terrain.AddBuilding(highKey, 3300L, highKey);
        high.BoundItemDefinition.monsterAttackPriority = 3;
        terrain.AddBuilding(lowKey, 3301L, lowKey).BoundItemDefinition.maxHealth = 100000f;
        for (int x = 0; x <= 64; x++)
        {
            terrain.Walls.Add(new Vector2Int(x, -1));
            terrain.Walls.Add(new Vector2Int(x, 1));
        }
        Spawn(terrain, new Vector3(1f, 0f, 0f), speed: 0f, radius: .45f, detection: 0f);
        var blocked = Spawn(terrain, Vector3.zero, .5f);
        for (int tick = 0; tick < 900 && !terrain.Store.States[highKey].hasCombatHealth
            && !terrain.Store.States[lowKey].hasCombatHealth; tick++) Tick(1);
        Check(!terrain.Store.States[highKey].hasCombatHealth && terrain.Store.States[lowKey].hasCombatHealth,
            "Sustained body blockage releases the approach and redirects to a different target");
        Tick(900);
        Check(blocked.Root.AttackAnimations > 0, "Rejected paths retry without permanently freezing the actor");

        MonsterWorld.Current.Dispose();
        Console.WriteLine($"PASS: {checks} production swarm approach/retarget checks");
    }

    private static void PriorityThreePathBlockers()
    {
        foreach (int priority in new[] { 1, 2 })
        {
            var terrain = Reset();
            var destinationKey = new Vector2Int(8, 0);
            var blockerKey = new Vector2Int(3, 0);
            var footprint = new Vector2Int[7];
            for (int i = 0; i < footprint.Length; i++) footprint[i] = new Vector2Int(3, i - 3);
            var blocker = terrain.AddBuilding(blockerKey, 5910L, footprint);
            blocker.BoundItemDefinition.monsterAttackPriority = priority;
            blocker.BoundItemDefinition.maxHealth = 4f;
            terrain.AddBuilding(destinationKey, 5911L, destinationKey).BoundItemDefinition.monsterAttackPriority = 3;
            Check(AnimalGridPathfinder.FindPath(terrain, Vector3.zero, new Vector3(7f, 0f, 0f),
                new Vector3(4f, 0f, 0f), 32f, true, new Vector3[128], terrain.CanMonsterMoveTo) > 0,
                "Priority-three blocker scenario has a walkable detour");
            Spawn(terrain, Vector3.zero, .5f, detection: 100f);
            for (int i = 0; i < 1200 && blocker.Active
                && !terrain.Store.States[destinationKey].hasCombatHealth; i++) Tick(1);
            Check(!blocker.Active && !terrain.Store.States[destinationKey].hasCombatHealth,
                $"Priority {priority} forcing a detour is destroyed before reaching priority three");
            Tick(600);
            Check(terrain.Store.States[destinationKey].hasCombatHealth,
                $"Destroying a priority-{priority} blocker resumes the priority-three destination");
        }

        var openTerrain = Reset();
        var offPathKey = new Vector2Int(3, 3);
        var goalKey = new Vector2Int(8, 0);
        openTerrain.AddBuilding(offPathKey, 5912L, offPathKey).BoundItemDefinition.monsterAttackPriority = 1;
        openTerrain.AddBuilding(goalKey, 5913L, goalKey).BoundItemDefinition.monsterAttackPriority = 3;
        Spawn(openTerrain, Vector3.zero, .5f, detection: 100f);
        Tick(600);
        Check(!openTerrain.Store.States[offPathKey].hasCombatHealth && openTerrain.Store.States[goalKey].hasCombatHealth,
            "An off-path priority-one structure is ignored even inside the encounter radius");

        var chainTerrain = Reset();
        var firstKey = new Vector2Int(3, 0);
        var secondKey = new Vector2Int(6, 0);
        goalKey = new Vector2Int(10, 0);
        foreach (var key in new[] { firstKey, secondKey })
        {
            var blocker = chainTerrain.AddBuilding(key, 5920L + key.x, key);
            blocker.BoundItemDefinition.monsterAttackPriority = 2;
            blocker.BoundItemDefinition.maxHealth = 2f;
        }
        chainTerrain.AddBuilding(goalKey, 5921L, goalKey).BoundItemDefinition.monsterAttackPriority = 3;
        Spawn(chainTerrain, Vector3.zero, .5f, detection: 100f);
        Tick(1500);
        Check(!chainTerrain.Store.Buildings[firstKey].Active && !chainTerrain.Store.Buildings[secondKey].Active
            && chainTerrain.Store.States[goalKey].hasCombatHealth,
            "Consecutive priority-two blockers are cleared in turn before reaching priority three");

        var excludedTerrain = Reset();
        var excluded = excludedTerrain.AddBuilding(firstKey, 5930L, firstKey);
        excluded.BoundItemDefinition.monsterAttackPriority = 2;
        excluded.BoundItemDefinition.excludeFromMonsterTargets = true;
        excludedTerrain.AddBuilding(goalKey, 5931L, goalKey).BoundItemDefinition.monsterAttackPriority = 3;
        Spawn(excludedTerrain, Vector3.zero, .5f, detection: 100f);
        Tick(900);
        Check(!excludedTerrain.Store.States[firstKey].hasCombatHealth && excludedTerrain.Store.States[goalKey].hasCombatHealth,
            "An excluded priority-two route blocker is bypassed, never attacked to reach priority three");
    }

    private static void RaidPriorities()
    {
        foreach (int direction in new[] { -1, 1 })
        foreach (var scenario in new[] {
            (Lower: 5, Higher: 15, ChooseThree: false),
            (Lower: 5, Higher: 40, ChooseThree: false),
            (Lower: 20, Higher: 35, ChooseThree: true),
            (Lower: 20, Higher: 45, ChooseThree: true),
            (Lower: 5, Higher: 5, ChooseThree: false),
            (Lower: 6, Higher: 15, ChooseThree: true) })
        {
            var terrain = Reset();
            var lowerKey = new Vector2Int(0, -scenario.Lower * direction);
            var higherKey = new Vector2Int(scenario.Higher * direction, 0);
            var lower = terrain.AddBuilding(lowerKey, 5900L, lowerKey);
            lower.BoundItemDefinition.monsterAttackPriority = 2;
            var higher = terrain.AddBuilding(higherKey, 5901L, higherKey);
            higher.BoundItemDefinition.monsterAttackPriority = 3;
            Spawn(terrain, Vector3.zero, .5f, detection: 100f);
            for (int i = 0; i < 1800 && !terrain.Store.States[lowerKey].hasCombatHealth
                && !terrain.Store.States[higherKey].hasCombatHealth; i++) Tick(1);
            Check(terrain.Store.States[higherKey].hasCombatHealth == scenario.ChooseThree
                && terrain.Store.States[lowerKey].hasCombatHealth != scenario.ChooseThree,
                $"Nearby priority two always interrupts priority three regardless of scan order/destination distance: {scenario}, {direction}");
            if (!scenario.ChooseThree)
            {
                Check(terrain.TryGetInstallationCombatTarget(lowerKey, out var target), "Nearby priority two remains a valid target");
                terrain.DamageInstallation(target, 100f);
                Tick(1800);
                Check(terrain.Store.States[higherKey].hasCombatHealth,
                    "After destroying nearby priority two, the distant priority-three target is pursued");
            }
        }

        var continuationTerrain = Reset();
        var nearbyKey = new Vector2Int(-5, 0);
        var distantKey = new Vector2Int(0, 40);
        var nearby = continuationTerrain.AddBuilding(nearbyKey, 5903L, nearbyKey);
        nearby.BoundItemDefinition.monsterAttackPriority = 2;
        nearby.BoundItemDefinition.maxHealth = 1f;
        continuationTerrain.AddBuilding(distantKey, 5904L, distantKey).BoundItemDefinition.monsterAttackPriority = 3;
        Spawn(continuationTerrain, Vector3.zero, .5f, detection: 100f);
        Tick(450);
        Check(!nearby.Active && !continuationTerrain.Store.States[distantKey].hasCombatHealth,
            "The monster itself finishes nearby priority two before damaging distant priority three");
        Tick(1800);
        Check(continuationTerrain.Store.States[distantKey].hasCombatHealth,
            "Raid continues to priority three after the monster destroys nearby priority two");

        var aloneTerrain = Reset();
        var aloneKey = new Vector2Int(40, 0);
        aloneTerrain.AddBuilding(aloneKey, 5902L, aloneKey).BoundItemDefinition.monsterAttackPriority = 3;
        Spawn(aloneTerrain, Vector3.zero, .5f, detection: 100f);
        Tick(1800);
        Check(aloneTerrain.Store.States[aloneKey].hasCombatHealth,
            "A distant priority-three target is still pursued when no lower-priority work remains");

        var encounterTerrain = Reset();
        var routeKey = new Vector2Int(15, 3);
        var originalKey = new Vector2Int(30, 0);
        var alternateKey = new Vector2Int(29, 12);
        var encountered = encounterTerrain.AddBuilding(routeKey, 5940L, routeKey);
        encountered.BoundItemDefinition.maxHealth = 2f;
        var original = encounterTerrain.AddBuilding(originalKey, 5941L, originalKey);
        original.BoundItemDefinition.monsterAttackPriority = 3;
        encounterTerrain.AddBuilding(alternateKey, 5942L, alternateKey).BoundItemDefinition.monsterAttackPriority = 3;
        Spawn(encounterTerrain, Vector3.zero, .5f, detection: 100f);
        Tick(1);
        Check(((TerrainGenerator.InstallationCombatTarget)GetActorField(0, "BuildingTarget")).Target == original,
            "A distant off-path priority two does not replace the priority-three destination");
        for (int i = 0; i < 900 && encountered.Active; i++) Tick(1);
        Check(!encountered.Active && !encounterTerrain.Store.States[originalKey].hasCombatHealth
            && !encounterTerrain.Store.States[alternateKey].hasCombatHealth,
            "A priority two encountered along the journey is destroyed before either priority three");
        Check(((TerrainGenerator.InstallationCombatTarget)GetActorField(0, "RaidDestination")).Target == original,
            "Encounter destruction keeps the original raid destination");
        // Make a different three closer after the interruption; resumption must not reselect it.
        var closerKey = new Vector2Int(15, 7);
        encounterTerrain.AddBuilding(closerKey, 5943L, closerKey).BoundItemDefinition.monsterAttackPriority = 3;
        Tick(900);
        Check(encounterTerrain.Store.States[originalKey].hasCombatHealth
            && !encounterTerrain.Store.States[closerKey].hasCombatHealth,
            "The original three is resumed even when another three is now closer");

        var cleanupTerrain = Reset();
        var remainingKey = new Vector2Int(2, 0);
        cleanupTerrain.AddBuilding(remainingKey, 5944L, remainingKey).BoundItemDefinition.monsterAttackPriority = 1;
        Spawn(cleanupTerrain, Vector3.zero, .5f);
        Tick(180);
        Check(cleanupTerrain.Store.States[remainingKey].hasCombatHealth,
            "Priority one alone is cleaned up when no priority-three destination exists");
    }

    private static void RaidCleanup()
    {
        foreach (int direction in new[] { -1, 1 })
        foreach (bool externalDestruction in new[] { false, true })
        {
            var terrain = Reset();
            var destinationKey = new Vector2Int(8 * direction, 0);
            var destination = terrain.AddBuilding(destinationKey, 5950L, destinationKey);
            destination.BoundItemDefinition.monsterAttackPriority = 3;
            destination.BoundItemDefinition.maxHealth = 2f;
            var twos = new FixtureBuilding[2];
            var ones = new FixtureBuilding[2];
            var twoKeys = new[] { new Vector2Int(-12 * direction, 8), new Vector2Int(5 * direction, 15) };
            var oneKeys = new[] { new Vector2Int(0, 3), new Vector2Int(16 * direction, -10) };
            for (int i = 0; i < 2; i++)
            {
                twos[i] = terrain.AddBuilding(twoKeys[i], 5951L + i, twoKeys[i]);
                twos[i].BoundItemDefinition.maxHealth = 1f;
                ones[i] = terrain.AddBuilding(oneKeys[i], 5953L + i, oneKeys[i]);
                ones[i].BoundItemDefinition.monsterAttackPriority = 1;
                ones[i].BoundItemDefinition.maxHealth = 1f;
            }
            var excludedKey = new Vector2Int(2 * direction, -8);
            var excluded = terrain.AddBuilding(excludedKey, 5955L, excludedKey);
            excluded.BoundItemDefinition.monsterAttackPriority = 1;
            excluded.BoundItemDefinition.excludeFromMonsterTargets = true;
            var zeroKey = new Vector2Int(-20 * direction, -10);
            var zero = terrain.AddBuilding(zeroKey, 5956L, zeroKey);
            zero.BoundItemDefinition.monsterAttackPriority = 0;
            var raider = Spawn(terrain, Vector3.zero, .5f, detection: 30f, leash: 6f);
            Tick(1);
            Check(((TerrainGenerator.InstallationCombatTarget)GetActorField(0, "BuildingTarget")).Target == destination,
                "Cleanup scenario first acquires priority three, ignoring off-route one and distant two");
            if (externalDestruction)
            {
                terrain.TryGetInstallationCombatTarget(destinationKey, out var target);
                terrain.DamageInstallation(target, 100f);
            }
            for (int tick = 0; tick < 900 && destination.Active; tick++) Tick(1);
            Check(!destination.Active && twos[0].Active && twos[1].Active && ones[0].Active && ones[1].Active,
                "Destroying the final three leaves both cleanup priority classes alive");
            for (int tick = 0; tick < 6000 && (twos[0].Active || twos[1].Active || ones[0].Active || ones[1].Active); tick++)
            {
                Tick(1);
                if (twos[0].Active || twos[1].Active)
                    Check(!terrain.Store.States[oneKeys[0]].hasCombatHealth && !terrain.Store.States[oneKeys[1]].hasCombatHealth,
                        "Distant priority-two cleanup precedes closer off-path priority one");
                if (twos[0].Active || twos[1].Active || ones[0].Active || ones[1].Active)
                    Check(!(bool)GetActorField(0, "Returning"),
                        "A raider does not return while detected priority-two or priority-one structures remain");
            }
            Check(!twos[0].Active && !twos[1].Active && !ones[0].Active && !ones[1].Active,
                $"Raid cleans every remaining two and one after final-three destruction: external={externalDestruction}, direction={direction}");
            Check(excluded.Active && !terrain.Store.States[excludedKey].hasCombatHealth
                && zero.Active && !terrain.Store.States[zeroKey].hasCombatHealth,
                "Cleanup never overrides absolute exclusion or the priority-zero obstacle-only rule");
            Tick(1800);
            Check(raider.Root.transform.position.sqrMagnitude < .05f,
                "A raider returns home only after finishing surrounding eligible installations");
        }

        var retryTerrain = Reset();
        var threeKey = new Vector2Int(8, 0);
        var three = retryTerrain.AddBuilding(threeKey, 5960L, threeKey);
        three.BoundItemDefinition.monsterAttackPriority = 3;
        var oneKey = new Vector2Int(0, 3);
        retryTerrain.AddBuilding(oneKey, 5961L, oneKey).BoundItemDefinition.monsterAttackPriority = 1;
        Spawn(retryTerrain, Vector3.zero, .5f, detection: 30f);
        SetActorField(0, "RejectedTargets", new System.Collections.Generic.Dictionary<object, float> { [three] = 4f });
        Tick(120);
        Check(!retryTerrain.Store.States[oneKey].hasCombatHealth && !(bool)GetActorField(0, "Returning"),
            "A temporarily rejected three is not treated as destroyed to start priority-one cleanup or return home");
        Tick(600);
        Check(retryTerrain.Store.States[threeKey].hasCombatHealth && !retryTerrain.Store.States[oneKey].hasCombatHealth,
            "Expired rejection resumes the remaining three instead of prematurely cleaning one");
    }

    private static void ExpandedDetection()
    {
        Check(MonsterStats.Default.DetectionRange == 100f, "New monster data defaults to a detection radius of one hundred");
        Check(MonsterStats.Default.AttackRange == 1.5f && MonsterStats.Default.LeashRange == 12f,
            "Expanded detection does not change attack reach or the player's pursuit leash");
        var terrain = Reset();
        var home = new Vector3(-45f, 0f, 0f);
        var key = new Vector2Int(45, 0);
        terrain.AddBuilding(key, 5800L, key);
        Spawn(terrain, home, .5f, detection: MonsterStats.Default.DetectionRange, leash: 12f);
        Tick(3000);
        Check(terrain.Store.States[key].hasCombatHealth,
            "Expanded detection acquires and attacks a building ninety cells from the spawn point using local navigation legs");

        terrain = Reset();
        key = new Vector2Int(58, 0);
        terrain.AddBuilding(key, 5801L, key);
        Spawn(terrain, home, .5f, detection: MonsterStats.Default.DetectionRange, leash: 12f);
        Tick(1);
        var target = (TerrainGenerator.InstallationCombatTarget)GetActorField(0, "BuildingTarget");
        Check(target.Target == null, "Expanded detection still ignores buildings outside the new radius");
    }

    private static FixtureBuilding AddInfrastructure(TerrainGenerator terrain, Vector2Int coordinate,
        long sequence, MapObject prototype, bool pipe)
    {
        FixtureBuilding target;
        if (pipe)
        {
            var record = new PipeRuntimeRecord { Prototype = (InstallationObject)prototype };
            PipeWorld.Current ??= new PipeWorld();
            PipeWorld.Current.Records[coordinate] = record;
            target = record;
        }
        else
        {
            var record = new ConveyorRuntimeRecord { Prototype = (ConveyorBelt)prototype };
            ConveyorWorld.Current ??= new ConveyorWorld();
            ConveyorWorld.Current.Records[coordinate] = record;
            target = record;
        }
        target.SimulationId = sequence;
        target.WorldPosition = new Vector3(coordinate.x, 0f, coordinate.y);
        target.BoundItemDefinition.mapObject = prototype;
        GameManager.Instance.ItemManger.Definitions[target.BoundItemDefinition.id] = target.BoundItemDefinition;
        terrain.Store.States[coordinate] = new BlockStateStore.InstallationSaveState {
            placementSequence = sequence, anchorCoordinate = coordinate,
            occupiedCoordinates = new System.Collections.Generic.List<Vector2Int> { coordinate } };
        terrain.Store.Buildings[coordinate] = target;
        terrain.Store.Coordinates[coordinate] = coordinate;
        terrain.Blocks[coordinate] = new Block { MapObject = prototype };
        terrain.InvalidateAnimalNavigation();
        return target;
    }

    private static void InfrastructureTargetIdentity()
    {
        for (int type = 0; type < 2; type++)
        {
            var terrain = Reset();
            bool pipe = type == 1;
            MapObject prototype = pipe ? new InstallationObject() : new ConveyorBelt();
            var key = new Vector2Int(2, 0);
            AddInfrastructure(terrain, key, 5700L, prototype, pipe);
            Spawn(terrain, Vector3.zero, .5f);
            Tick(180);
            Check(terrain.Store.States[key].hasCombatHealth,
                "A non-traversable infrastructure prototype is recognized as its runtime attack target: " + type);

            terrain = Reset();
            var front = new Vector2Int(1, 0);
            AddInfrastructure(terrain, front, 5701L, prototype, pipe);
            var behind = new Vector2Int(3, 0);
            var target = AddInfrastructure(terrain, behind, 5702L, prototype, pipe);
            Spawn(terrain, Vector3.zero, .5f);
            var method = typeof(MonsterWorld).GetMethod("HasAttackLine", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
            bool clear = (bool)method.Invoke(MonsterWorld.Current,
                new object[] { Vector3.zero, new Vector3(2.5f, 0f, 0f), target });
            Check(!clear, "A different installation with the same prototype still blocks attacks: " + type);
        }
    }

    private static void ConveyorRaid()
    {
        var terrain = Reset();
        var prototype = new ConveyorBelt();
        for (int x = 0; x <= 6; x++)
        {
            var key = new Vector2Int(x, 0);
            var building = AddInfrastructure(terrain, key, 5600L + x, prototype, false);
            building.BoundItemDefinition.maxHealth = 3f;
        }
        for (int i = 0; i < 12; i++)
            Spawn(terrain, new Vector3(-3f - i % 3, 0f, -2f + i / 3), .5f, radius: .18824f,
                bodyCenter: new Vector3(-.122798f, .2583545f, 0f), height: .516709f);
        Tick(1800);
        Check(terrain.Store.States.Count == 0,
            "A swarm using authored slime body offsets destroys successive ECS conveyor cells");
    }

    private static void BlockedTargetOverflow()
    {
        var terrain = Reset();
        for (int x = 3; x <= 6; x += 3)
        for (int z = -3; z <= 3; z += 3)
        {
            var key = new Vector2Int(x, z);
            var building = terrain.AddBuilding(key, 5000L + x * 10 + z, key);
            building.BoundItemDefinition.monsterAttackPriority = 3;
            building.BoundItemDefinition.maxHealth = 100000f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dz != 0) terrain.Walls.Add(new Vector2Int(x + dx, z + dz));
        }
        var alternative = new Vector2Int(-6, 0);
        terrain.AddBuilding(alternative, 5100L, alternative).BoundItemDefinition.maxHealth = 100000f;
        Spawn(terrain, Vector3.zero, .5f);
        Tick(1200);
        Check(terrain.Store.States[alternative].hasCombatHealth,
            "More than four blocked priority-three buildings cannot starve a reachable priority-two building");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        Tick(600);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(allocated == 0, "Warmed blocked-target retries allocate no per-tick GC: " + allocated);

        terrain = Reset();
        for (int z = -2; z <= 2; z += 4)
        {
            var key = new Vector2Int(4, z);
            var building = terrain.AddBuilding(key, 5200L + z, key);
            building.BoundItemDefinition.monsterAttackPriority = 3;
            building.BoundItemDefinition.maxHealth = 100000f;
        }
        for (int z = -5; z <= 5; z++)
        for (int x = 2; x <= 7; x++)
            if (x == 2 || x == 7 || z == -5 || z == 5) terrain.Walls.Add(new Vector2Int(x, z));
        terrain.AddBuilding(alternative, 5300L, alternative).BoundItemDefinition.maxHealth = 100000f;
        Spawn(terrain, Vector3.zero, .5f);
        Tick(1800);
        Check(terrain.Store.States[alternative].hasCombatHealth,
            "Slow path failures cannot keep recycling expired high-priority targets before trying a reachable alternative");

        terrain = Reset();
        for (int x = 6; x <= 9; x += 3)
        {
            var key = new Vector2Int(x, 0);
            var building = terrain.AddBuilding(key, 5400L + x, key);
            building.BoundItemDefinition.monsterAttackPriority = 3;
            building.BoundItemDefinition.maxHealth = 100000f;
        }
        alternative = new Vector2Int(-6, 3);
        terrain.AddBuilding(alternative, 5500L, alternative).BoundItemDefinition.maxHealth = 100000f;
        for (int x = 0; x <= 64; x++)
        {
            terrain.Walls.Add(new Vector2Int(x, -1));
            terrain.Walls.Add(new Vector2Int(x, 1));
        }
        Spawn(terrain, new Vector3(1f, 0f, 0f), speed: 0f, radius: .45f, detection: 0f);
        Spawn(terrain, Vector3.zero, .5f);
        Tick(1800);
        Check(terrain.Store.States[alternative].hasCombatHealth,
            "Multiple body-blocked high-priority targets cannot recycle before an untried alternative");
    }

    private static void RaidContinuation()
    {
        var terrain = Reset();
        for (int x = 4; x <= 24; x += 5)
        {
            var coordinate = new Vector2Int(x, 0);
            terrain.AddBuilding(coordinate, 3800L + x, coordinate).BoundItemDefinition.maxHealth = 1f;
        }
        var raider = Spawn(terrain, Vector3.zero, .5f, detection: 6f, leash: 6f);
        Tick(1800);
        Check(terrain.Store.States.Count == 0,
            "A raid destroys successive nearby buildings beyond its original spawn leash");
        Tick(900);
        Check(raider.Root.transform.position.sqrMagnitude < .05f,
            "A raid returns to its original home only after nearby eligible buildings are gone");

        terrain = Reset();
        for (int x = -16; x <= 59; x += 5)
        {
            var coordinate = new Vector2Int(x, 0);
            terrain.AddBuilding(coordinate, 4000L + x, coordinate).BoundItemDefinition.maxHealth = 1f;
        }
        for (int z = -2; z <= 2; z++) terrain.Walls.Add(new Vector2Int(10, z));
        var home = new Vector3(-20f, 0f, 0f);
        raider = Spawn(terrain, home, .5f, detection: 6f, leash: 6f);
        Tick(6000);
        Check(terrain.Store.States.Count == 0,
            "A long raid can detour around terrain outside its original home leash");
        Check(Vector3.Distance(raider.Root.transform.position, home) < .23f,
            "A long raid returns through obstacles using bounded navigation legs");

        terrain = Reset();
        var player = Player();
        player.transform.position = new Vector3(3f, 0f, 0f);
        raider = Spawn(terrain, Vector3.zero, .5f, detection: 8f, leash: 4f);
        Tick(75);
        player.transform.position = new Vector3(30f, 0f, 0f);
        Tick(1);
        var key = new Vector2Int(6, 0);
        terrain.AddBuilding(key, 3900L, key);
        Tick(300);
        Check(terrain.Store.States[key].hasCombatHealth,
            "A returning monster interrupts its retreat for a newly discovered nearby building");

        terrain = Reset();
        key = new Vector2Int(7, 0);
        terrain.AddBuilding(key, 3901L, key);
        var waiting = new MonsterInstance[6];
        for (int i = 0; i < waiting.Length; i++)
        {
            waiting[i] = Spawn(terrain, new Vector3(4f, 0f, i - 2f), .5f);
            SetActorField(i, "Home", Vector3.zero);
        }
        Tick(1);
        for (int i = 0; i < waiting.Length; i++)
            Check(waiting[i].Root.transform.position.x >= 4f,
                "A search-budget wait is not interpreted as a reason to retreat: " + i);

        terrain = Reset();
        key = new Vector2Int(4, 0);
        var building = terrain.AddBuilding(key, 3902L, key);
        raider = Spawn(terrain, new Vector3(2f, 0f, 0f), .5f);
        SetActorField(0, "Home", Vector3.zero);
        SetActorField(0, "RejectedTargets", new System.Collections.Generic.Dictionary<object, float> { [building] = 4f });
        Tick(1);
        Check(raider.Root.transform.position.x >= 2f,
            "A temporarily unavailable building keeps the monster nearby until its retry");
        Tick(360);
        Check(terrain.Store.States[key].hasCombatHealth,
            "A temporarily unavailable building is attacked after its retry expires");
    }

    private static object GetActor(int index)
    {
        var actors = (System.Collections.IList)typeof(MonsterWorld).GetField("actors",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(MonsterWorld.Current);
        return actors[index];
    }

    private static object GetActorField(int index, string name)
    {
        object actor = GetActor(index);
        return actor.GetType().GetField(name, System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic).GetValue(actor);
    }

    private static void SetActorField(int index, string name, object value)
    {
        object actor = GetActor(index);
        actor.GetType().GetField(name, System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic).SetValue(actor, value);
    }

    private static void BuildingDiscovery()
    {
        var terrain = Reset();
        Vector2Int key = new(4, 0), alternative = new(-2, 3);
        terrain.AddBuilding(key, 3400L, key).BoundItemDefinition.monsterAttackPriority = 2;
        terrain.AddBuilding(alternative, 3401L, alternative).BoundItemDefinition.monsterAttackPriority = 1;
        for (int z = -1; z <= 1; z++)
        for (int x = 2; x <= 4; x++)
            if (x == 2 || z != 0) terrain.Walls.Add(new Vector2Int(x, z));
        Spawn(terrain, Vector3.zero, .5f);
        Tick(600);
        Check(terrain.Store.States[key].hasCombatHealth && !terrain.Store.States[alternative].hasCombatHealth,
            "Unreachable nearest attack position does not hide the building's reachable opposite edge");

        terrain = Reset();
        key = new Vector2Int(2, 0);
        var owner = terrain.AddBuilding(key, 3500L, key);
        owner.Traversable = true;
        owner.BoundItemDefinition.monsterAttackPriority = 0;
        Vector2Int pipeKey = new(3, 0);
        var pipe = new PipeRuntimeRecord { SimulationId = 3501L, WorldPosition = new Vector3(2f, 0f, 0f), Traversable = true };
        terrain.Store.States[pipeKey] = new BlockStateStore.InstallationSaveState { placementSequence = 3501L,
            anchorCoordinate = pipeKey, occupiedCoordinates = new System.Collections.Generic.List<Vector2Int> { key } };
        terrain.Store.Buildings[pipeKey] = pipe;
        PipeWorld.Current = new PipeWorld();
        PipeWorld.Current.Records[key] = pipe;
        Spawn(terrain, Vector3.zero, .5f);
        Tick(240);
        Check(terrain.Store.States[pipeKey].hasCombatHealth && !terrain.Store.States[key].hasCombatHealth,
            "Priority-zero I/O owner does not hide an eligible pipe in the same coordinate");

        terrain = Reset();
        key = new Vector2Int(2, 0);
        terrain.AddBuilding(key, 3600L, key);
        Spawn(terrain, new Vector3(.5f, 0f, 0f), .5f, detection: 1f);
        Tick(180);
        Check(terrain.Store.States[key].hasCombatHealth,
            "A footprint edge exactly on detection range is not lost by rounded cell bounds or strict comparisons");

        terrain = Reset();
        var player = Player();
        player.transform.position = new Vector3(3f, 0f, 0f);
        var moving = Spawn(terrain, Vector3.zero, .5f, detection: 8f, leash: 4f);
        Tick(90);
        Check(moving.Root.transform.position.x > 1.5f, "Leash regression first moves away from its home");
        GameManager.Instance.SetCloakingMode(true);
        key = new Vector2Int(-3, 0);
        terrain.AddBuilding(key, 3601L, key);
        SetActorField(0, "SearchTimer", 0f);
        Tick(1);
        var acquired = (TerrainGenerator.InstallationCombatTarget)GetActorField(0, "BuildingTarget");
        Check(acquired.Sequence == 3601L, "Opposite-side building is found before the actor returns toward home");
        Tick(300);
        Check(terrain.Store.States[key].hasCombatHealth,
            "Building detection remains centered on the actor after losing the player");

        terrain = Reset();
        key = Vector2Int.zero;
        terrain.AddBuilding(key, 3700L, key);
        for (int i = 0; i < 32; i++)
        {
            double angle = i * Math.PI * 2 / 32;
            float x = (float)Math.Sin(angle), z = (float)Math.Cos(angle);
            float outerRadius = .5f / Math.Max(Math.Abs(x), Math.Abs(z)) + 1.5f * .85f;
            Vector2Int outer = new(Mathf.RoundToInt(x * outerRadius), Mathf.RoundToInt(z * outerRadius));
            if (outer != new Vector2Int(-2, 0)) terrain.Walls.Add(outer);
        }
        Spawn(terrain, new Vector3(-4f, 0f, 0f));
        Tick(1);
        var inner = Spawn(terrain, new Vector3(-1.775f, 0f, 0f));
        Tick(120);
        Check(inner.Root.AttackAnimations > 0,
            "A reserved outer ring does not hide a closer walkable attack position in a narrow space");
    }
}
