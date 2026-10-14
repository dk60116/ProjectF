using System;

namespace ProjectF.Simulation.Monsters
{
    public struct CombatHealth
    {
        public float Maximum { get; private set; }
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;

        public CombatHealth(float maximum)
        {
            Maximum = IsFinite(maximum) && maximum >= 1f ? maximum : 100f;
            Current = Maximum;
        }

        public CombatHealth(float maximum, float current) : this(maximum)
        {
            if (IsFinite(current)) Current = Math.Max(0f, Math.Min(Maximum, current));
        }

        public float Damage(float amount)
        {
            if (!IsFinite(amount) || amount <= 0f || IsDead) return 0f;
            float applied = Math.Min(Current, amount);
            Current -= applied;
            return applied;
        }

        public bool Revive()
        {
            if (!IsDead || Maximum <= 0f) return false;
            Current = Maximum;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [Flags]
    public enum MonsterAttackEvent { None = 0, Started = 1, Hit = 2 }

    // Damage is emitted once during the wind-up, never once per simulation tick.
    public struct MonsterAttackCycle
    {
        private long cooldown;
        private long windup;
        private bool pendingHit;
        private bool cancelled;
        public bool IsAttacking => cooldown > 0L && !cancelled;

        public bool CanRetarget(long elapsedTicks) =>
            !IsAttacking || cooldown <= Math.Max(0L, elapsedTicks);

        public MonsterAttackEvent Tick(long elapsedTicks, bool canStart, long intervalTicks)
        {
            if (elapsedTicks <= 0L) return MonsterAttackEvent.None;
            intervalTicks = Math.Max(1L, intervalTicks);
            cooldown = Math.Max(0L, cooldown - elapsedTicks);
            MonsterAttackEvent result = MonsterAttackEvent.None;
            if (pendingHit)
            {
                windup -= elapsedTicks;
                if (windup <= 0L)
                {
                    pendingHit = false;
                    result = MonsterAttackEvent.Hit;
                }
            }
            if (cooldown <= 0L && canStart)
            {
                cooldown = intervalTicks;
                windup = Math.Max(1L, intervalTicks / 100L * 35L
                    + (intervalTicks % 100L * 35L + 99L) / 100L);
                pendingHit = true;
                cancelled = false;
                result |= MonsterAttackEvent.Started;
            }
            return result;
        }

        // End the old attack pose as well as its hit, without bypassing cooldown.
        public void CancelHit()
        {
            pendingHit = false;
            cancelled = true;
        }
    }
}
