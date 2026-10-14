using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ProjectF.Monsters;
using ProjectF.Simulation.Monsters;

internal static class Checks
{
    private static int checks;
    private static void Require(bool value, string name)
    {
        checks++;
        if (!value) throw new InvalidOperationException(name);
    }

    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
    }

    private static void Run()
    {
        CheckCombat();
        Require(!RuntimeHelpers.IsReferenceOrContainsReferences<MonsterStats>(), "Stats unmanaged");
        Require(!RuntimeHelpers.IsReferenceOrContainsReferences<MonsterArchetypeData>(), "Archetype unmanaged");
        Require(!RuntimeHelpers.IsReferenceOrContainsReferences<MonsterDropData>(), "Drops unmanaged");
        MonsterStats common = MonsterStats.Default;
        MonsterStats overrides = new MonsterStats
        {
            MaxHealth = 200f, MoveSpeed = 4f, TurnSpeed = 90f, AttackDamage = 50f,
            AttackRange = 6f, AttackInterval = 2f, DetectionRange = 16f, LeashRange = 24f
        };
        FieldInfo[] fields = typeof(MonsterStats).GetFields(BindingFlags.Instance | BindingFlags.Public);
        Require(fields.Length == 8, "Eight core stats");
        for (int mask = 0; mask <= (int)MonsterStat.All; mask++)
        {
            MonsterStats resolved = MonsterStats.Resolve(common, overrides, (MonsterStat)mask);
            Require(resolved.IsValid, "Resolved stats valid");
            foreach (FieldInfo field in fields)
            {
                MonsterStat flag = Enum.Parse<MonsterStat>(field.Name);
                float expected = (float)field.GetValue(((MonsterStat)mask & flag) != 0 ? overrides : common);
                Require((float)field.GetValue(resolved) == expected, "Override mask " + mask + "/" + field.Name);
            }
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
        {
            MonsterStats invalidStats = new MonsterStats
            {
                MaxHealth = invalid, MoveSpeed = invalid, TurnSpeed = invalid, AttackDamage = invalid,
                AttackRange = invalid, AttackInterval = invalid, DetectionRange = invalid, LeashRange = invalid
            };
            Require(!invalidStats.IsValid, "Reject invalid stats");
            invalidStats.Normalize();
            Require(invalidStats.IsValid, "Normalize invalid stats");
        }
        MonsterDefinition definition = new MonsterDefinition();
        ItemDefinition item = new ItemDefinition { id = 29 };
        List<MonsterDropEntry> commonDrops = new List<MonsterDropEntry>
        {
            new MonsterDropEntry { item = item, amount = 3, chance = 0.25f }
        };
        MonsterVariant inherited = new MonsterVariant { id = 7, variantName = "Green" };
        MonsterVariant custom = new MonsterVariant
        {
            id = 42, variantName = "Red", overrideMask = MonsterStat.AttackDamage,
            stats = overrides, overrideDrops = true,
            drops = new List<MonsterDropEntry> { new MonsterDropEntry { item = item, amount = 5, chance = 1f } }
        };
        MonsterAnimations clips = new MonsterAnimations
        {
            idle = new UnityEngine.AnimationClip(), move = new UnityEngine.AnimationClip(),
            attack = new UnityEngine.AnimationClip(), death = new UnityEngine.AnimationClip()
        };
        definition.SetAuthoringData(12, "Slime", common, clips, commonDrops,
            new List<MonsterVariant> { custom, inherited });
        Require(definition.TryGetRuntimeData(7, out MonsterArchetypeData inheritedData), "Lookup by ID, not index");
        Require(inheritedData.SpeciesId == 12 && inheritedData.VariantId == 7, "Stable runtime IDs");
        Require(inheritedData.Stats.AttackDamage == common.AttackDamage, "Common stat inherited");
        Require(definition.TryGetRuntimeData(42, out MonsterArchetypeData customData)
            && customData.Stats.AttackDamage == overrides.AttackDamage, "Custom stat resolved");
        Require(customData.Stats.MoveSpeed == common.MoveSpeed, "Other custom stats inherited");
        Require(!definition.TryGetRuntimeData(999, out _), "Unknown variant rejected");
        Require(ReferenceEquals(definition.GetDrops(7), commonDrops), "Inherited drops have no allocation");
        Require(definition.GetDrops(999).Count == 0, "Unknown variant has no drops");
        MonsterDropData[] dropData = definition.CreateDropData(7);
        Require(dropData.Length == 1 && dropData[0].ItemId == 29 && dropData[0].Amount == 3
            && dropData[0].Chance == 0.25f, "Numeric common drop data");
        Require(definition.CreateDropData(42)[0].Amount == 5, "Numeric override drops");
        custom.drops.Clear();
        Require(definition.GetDrops(42).Count == 0, "Empty override suppresses common drops");
        Require(definition.CreateDropData(42).Length == 0, "Empty numeric drops");
        common.AttackDamage = 25f;
        definition.SetAuthoringData(12, "Slime", common, clips, commonDrops,
            new List<MonsterVariant> { inherited, custom });
        definition.TryGetRuntimeData(7, out inheritedData);
        definition.TryGetRuntimeData(42, out customData);
        Require(inheritedData.Stats.AttackDamage == 25f && customData.Stats.AttackDamage == 50f,
            "Common edits affect only inherited stats across reorder");
        Require(clips.GetClip(MonsterAnimationState.Hit) == null, "Missing Slime hit stays empty");
        Require(clips.GetClip(MonsterAnimationState.Death) == clips.death, "Death clip mapping");
        int allocatedId = definition.AllocateVariantId();
        definition.SetAuthoringData(12, "Slime", common, clips, commonDrops,
            new List<MonsterVariant> { inherited });
        Require(definition.AllocateVariantId() > allocatedId, "Removed variant IDs are not reused");
        definition.ReserveVariantIdsThrough(100);
        Require(definition.AllocateVariantId() == 100, "JSON/reload preserves ID reservation");
        commonDrops.Add(new MonsterDropEntry { item = new ItemDefinition { id = -1 } });
        commonDrops.Add(new MonsterDropEntry { item = item, amount = -5, chance = float.NaN });
        dropData = definition.CreateDropData(7);
        Require(dropData.Length == 2 && dropData[1].Amount == 1 && dropData[1].Chance == 0f,
            "Numeric drop projection rejects invalid IDs and nonfinite probabilities");

        int attempts = 0;
        Require(MonsterSpawnSearch.TryFindNear(10, -20, (x, z) => { attempts++; return true; }, out int spawnX, out int spawnZ)
            && attempts == 1, "One spawn request stops at its first free tile");
        Require((spawnX - 10) * (spawnX - 10) + (spawnZ + 20) * (spawnZ + 20) >= 4,
            "Spawn is not on the player's tile");
        HashSet<(int, int)> visited = new HashSet<(int, int)>();
        bool bounded = true;
        bool unique = true;
        Require(!MonsterSpawnSearch.TryFindNear(-30, 50, (x, z) =>
        {
            int dx = x + 30, dz = z - 50;
            bounded &= dx * dx + dz * dz >= 4 && dx * dx + dz * dz <= 64;
            unique &= visited.Add((x, z));
            return false;
        }, out _, out _), "No free ground fails without fallback spawn");
        Require(bounded && unique, "Spawn search is bounded and visits each tile once");
        foreach ((int x, int z) in visited)
        {
            Require(MonsterSpawnSearch.TryFindNear(-30, 50, (cx, cz) => cx == x && cz == z,
                out spawnX, out spawnZ) && spawnX == x && spawnZ == z,
                "Can reach each free tile when all other tiles are occupied");
        }
        Require(MonsterSpawnSearch.TryFindNear(0, 0, (x, z) => x == 8 && z == 0, out spawnX, out spawnZ)
            && spawnX == 8 && spawnZ == 0, "Outer spawn radius is included");
        Require(!MonsterSpawnSearch.TryFindNear(0, 0, (x, z) => x == 1 && z == 0, out _, out _),
            "Player clearance is excluded");
        Require(!MonsterSpawnSearch.TryFindNear(0, 0, (x, z) => x == 8 && z == 8, out _, out _),
            "Diagonal tiles beyond radius are excluded");

        for (int i = 0; i < 1000; i++) definition.TryGetRuntimeData(7, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) definition.TryGetRuntimeData(7, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"Runtime lookup allocated bytes: {allocated}");
        Require(allocated == 0, "Runtime stat lookup allocates zero bytes");
        Console.WriteLine($"PASS: {checks} Monster Data logic checks");
    }

    private static void CheckCombat()
    {
        Require(!RuntimeHelpers.IsReferenceOrContainsReferences<CombatHealth>(), "Health is unmanaged");
        Require(!RuntimeHelpers.IsReferenceOrContainsReferences<MonsterAttackCycle>(), "Attack cycle is unmanaged");
        CombatHealth health = new CombatHealth(100f);
        Require(health.Current == 100f && !health.IsDead, "Health initializes alive");
        Require(health.Damage(30f) == 30f && health.Current == 70f, "Actual damage applied");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 0f })
            Require(health.Damage(invalid) == 0f && health.Current == 70f, "Invalid damage rejected");
        Require(health.Damage(999f) == 70f && health.IsDead && health.Current == 0f, "Lethal damage clamps to zero");
        for (int i = 0; i < 1000; i++)
            Require(health.Damage(10f) == 0f && health.IsDead, "Dead state never automatically revives");
        Require(health.Revive() && health.Current == 100f && !health.IsDead, "Manual revive restores health");
        Require(!health.Revive(), "Living player cannot spam revive to heal");
        Require(new CombatHealth(100f, 0f).IsDead, "Saved zero health restores dead");
        Require(new CombatHealth(100f, 35f).Current == 35f, "Saved partial health restores");
        Require(new CombatHealth(100f, -5f).IsDead, "Negative saved health clamps dead");
        Require(new CombatHealth(100f, 1000f).Current == 100f, "Saved health clamps to maximum");
        Require(new CombatHealth(float.NaN).Current == 100f, "Invalid health maximum is safe");
        Require(new CombatHealth(100f, float.NaN).Current == 100f, "Invalid saved health is safe");

        MonsterAttackCycle cycle = default;
        const long dt = 1;
        Require(cycle.Tick(dt, true, 60) == MonsterAttackEvent.Started, "First swing starts without instant damage");
        Require(!cycle.CanRetarget(1) && cycle.CanRetarget(60),
            "Target changes wait for the attack boundary, not the next target-scan timer");
        for (int i = 0; i < 20; i++)
            Require(cycle.Tick(dt, true, 60) == MonsterAttackEvent.None, "No damage before wind-up");
        int hits = 0, starts = 1;
        for (int i = 0; i < 590; i++)
        {
            MonsterAttackEvent result = cycle.Tick(dt, true, 60);
            if ((result & MonsterAttackEvent.Hit) != 0) hits++;
            if ((result & MonsterAttackEvent.Started) != 0) starts++;
        }
        Require(hits == 10 && starts == 11, "60 Hz combat produces one hit per interval, not per frame");
        cycle = default;
        cycle.Tick(dt, true, 60);
        cycle.CancelHit();
        Require(!cycle.IsAttacking && cycle.CanRetarget(1),
            "Target cancellation releases its pose and targeting lock, but retains cooldown");
        for (int i = 0; i < 30; i++)
            Require(cycle.Tick(dt, false, 60) == MonsterAttackEvent.None, "Lost target cancels pending damage");
        Require(cycle.Tick(dt, true, 60) == MonsterAttackEvent.None, "Target switch cannot bypass cooldown");
        for (int i = 0; i < 30; i++) cycle.Tick(dt, false, 60);
        Require(cycle.Tick(dt, true, 60) == MonsterAttackEvent.Started, "Ready actor can attack a new target");
        Require(cycle.Tick(0, true, 60) == MonsterAttackEvent.None, "Paused simulation cannot deal damage");
        Require(cycle.Tick(-1, true, 60) == MonsterAttackEvent.None, "Invalid time cannot corrupt cycle");

        cycle = default;
        cycle.Tick(1, true, 60);
        for (int i = 0; i < 59; i++)
            Require((cycle.Tick(1, true, 60) & MonsterAttackEvent.Started) == 0,
                "One-second attack cannot restart before 60 simulation ticks");
        Require((cycle.Tick(1, true, 60) & MonsterAttackEvent.Started) != 0,
            "One-second attack restarts at exactly 60 ticks without floating point drift");

        cycle = default;
        for (int i = 0; i < 1000; i++) cycle.Tick(dt, true, 60);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) cycle.Tick(dt, true, 60);
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "Combat tick allocates zero bytes");
    }
}
