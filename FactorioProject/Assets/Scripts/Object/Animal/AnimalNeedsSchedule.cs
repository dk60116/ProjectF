namespace ProjectF.Animals
{
    // One-second buckets staggered by stable identity, independent of rendering and wall time.
    internal struct AnimalNeedsSchedule
    {
        private const int IntervalTicks = 60;
        private long lastTick;
        private long nextTick;
        private int phase;

        internal void Initialize(long tick, long identity)
        {
            phase = (int)((unchecked((ulong)identity) ^ (unchecked((ulong)identity) >> 32)) % IntervalTicks);
            lastTick = tick;
            nextTick = NextDue(tick);
        }

        internal long TakeElapsedTicks(long tick, bool lowFrequency)
        {
            if (tick <= lastTick || lowFrequency && tick < nextTick) return 0L;
            long elapsed = tick - lastTick;
            lastTick = tick;
            nextTick = NextDue(tick);
            return elapsed;
        }

        private long NextDue(long tick)
            => tick + 1L + (phase - (tick + 1L) % IntervalTicks + IntervalTicks) % IntervalTicks;
    }
}
