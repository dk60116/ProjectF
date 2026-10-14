using System;

namespace ProjectF.Simulation.Monsters
{
    [Flags]
    public enum MonsterStat
    {
        None = 0,
        MaxHealth = 1 << 0,
        MoveSpeed = 1 << 1,
        TurnSpeed = 1 << 2,
        AttackDamage = 1 << 3,
        AttackRange = 1 << 4,
        AttackInterval = 1 << 5,
        DetectionRange = 1 << 6,
        LeashRange = 1 << 7,
        All = (1 << 8) - 1
    }

    [Serializable]
    public struct MonsterStats
    {
        public float MaxHealth;
        public float MoveSpeed;
        public float TurnSpeed;
        public float AttackDamage;
        public float AttackRange;
        public float AttackInterval;
        public float DetectionRange;
        public float LeashRange;

        public static MonsterStats Default => new MonsterStats
        {
            MaxHealth = 100f,
            MoveSpeed = 2f,
            TurnSpeed = 180f,
            AttackDamage = 10f,
            AttackRange = 1.5f,
            AttackInterval = 1f,
            DetectionRange = 100f,
            LeashRange = 12f
        };

        public bool IsValid =>
            Valid(MaxHealth, 1f) && Valid(MoveSpeed, 0f)
            && Valid(TurnSpeed, 0f) && Valid(AttackDamage, 0f)
            && Valid(AttackRange, 0f) && Valid(AttackInterval, 0.01f)
            && Valid(DetectionRange, 0f) && Valid(LeashRange, 0f);

        public void Normalize()
        {
            MonsterStats fallback = Default;
            MaxHealth = Normalize(MaxHealth, 1f, fallback.MaxHealth);
            MoveSpeed = Normalize(MoveSpeed, 0f, fallback.MoveSpeed);
            TurnSpeed = Normalize(TurnSpeed, 0f, fallback.TurnSpeed);
            AttackDamage = Normalize(AttackDamage, 0f, fallback.AttackDamage);
            AttackRange = Normalize(AttackRange, 0f, fallback.AttackRange);
            AttackInterval = Normalize(AttackInterval, 0.01f, fallback.AttackInterval);
            DetectionRange = Normalize(DetectionRange, 0f, fallback.DetectionRange);
            LeashRange = Normalize(LeashRange, 0f, fallback.LeashRange);
        }

        public static MonsterStats Resolve(
            MonsterStats common, MonsterStats overrides, MonsterStat mask)
        {
            MonsterStats result = common;
            if ((mask & MonsterStat.MaxHealth) != 0) result.MaxHealth = overrides.MaxHealth;
            if ((mask & MonsterStat.MoveSpeed) != 0) result.MoveSpeed = overrides.MoveSpeed;
            if ((mask & MonsterStat.TurnSpeed) != 0) result.TurnSpeed = overrides.TurnSpeed;
            if ((mask & MonsterStat.AttackDamage) != 0) result.AttackDamage = overrides.AttackDamage;
            if ((mask & MonsterStat.AttackRange) != 0) result.AttackRange = overrides.AttackRange;
            if ((mask & MonsterStat.AttackInterval) != 0) result.AttackInterval = overrides.AttackInterval;
            if ((mask & MonsterStat.DetectionRange) != 0) result.DetectionRange = overrides.DetectionRange;
            if ((mask & MonsterStat.LeashRange) != 0) result.LeashRange = overrides.LeashRange;
            result.Normalize();
            return result;
        }

        private static bool Valid(float value, float minimum) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum;

        private static float Normalize(float value, float minimum, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(minimum, value);
    }

    // Numeric authoring output for the project's data-oriented simulation, not a per-actor behaviour.
    public readonly struct MonsterArchetypeData
    {
        public readonly int SpeciesId;
        public readonly int VariantId;
        public readonly MonsterStats Stats;

        public MonsterArchetypeData(int speciesId, int variantId, MonsterStats stats)
        {
            SpeciesId = speciesId;
            VariantId = variantId;
            Stats = stats;
        }
    }

    public readonly struct MonsterDropData
    {
        public readonly int ItemId;
        public readonly int Amount;
        public readonly float Chance;

        public MonsterDropData(int itemId, int amount, float chance)
        {
            ItemId = itemId;
            Amount = amount;
            Chance = chance;
        }
    }
}
