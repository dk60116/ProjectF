using System;

namespace ProjectF.Benchmark
{
    public static class BenchmarkLayout
    {
        public const int MaximumRings = 1000;
        public const int MaximumObjects = 1000000;
        public const int WorkSliceMilliseconds = 10;
        public const int SpawnRowsPerBatch = 8;
        public static bool IsWorkSliceExpired(long started, long now, long frequency)
            => now - started >= (frequency * WorkSliceMilliseconds + 999L) / 1000L;

        public readonly struct Cell
        {
            public readonly int X, Y;
            public Cell(int x, int y) { X = x; Y = y; }
        }

        // Ring r has radius r and perimeter 8r; neighbouring rings touch.
        public static long BeltCount(int rings) => checked(4L * rings * (rings + 1L));
        public static int RingLength(int ring) => checked(8 * ring);
        public static Cell RingCell(int ring, int index)
        {
            int radius = ring, side = checked(radius * 2), length = checked(side * 4);
            if (ring < 1 || index < 0 || index >= length) throw new ArgumentOutOfRangeException();
            int segment = index / side, offset = index % side;
            switch (segment)
            {
                case 0: return new Cell(-radius + offset, -radius);
                case 1: return new Cell(radius, -radius + offset);
                case 2: return new Cell(radius - offset, radius);
                default: return new Cell(-radius, radius - offset);
            }
        }
        public static int GridColumns(int count) => (int)Math.Ceiling(Math.Sqrt(count));
        public static long FilledSlotCount(long slots, double percent) => (long)Math.Floor(slots * percent / 100d);
        // Cumulative apportioning spreads fractional occupancy over the entire layout.
        public static int FillForBelt(long slotsBefore, int beltSlots, double percent)
            => (int)(FilledSlotCount(slotsBefore + beltSlots, percent) - FilledSlotCount(slotsBefore, percent));
    }
}
