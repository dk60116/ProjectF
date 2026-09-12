using System;
using UnityEngine;

namespace ProjectF.Animals
{
    // Integer simulation units. Unity vectors/quaternions are boundary/presentation values.
    internal static class AnimalSimulationMath
    {
        internal const long UnitsPerCell = 4096;
        internal const int TicksPerSecond = 60;
        private static readonly int[] ArcAngles = { 8192, 4836, 2555, 1297, 651, 326, 163, 81, 41, 20, 10, 5, 3, 1, 1 };
        internal static int Angle(float degrees) => (int)Math.Round((double)degrees * 65536 / 360, MidpointRounding.ToEven) & 65535;
        internal static float Degrees(int angle) => angle * (360f / 65536f);
        internal static int AngleDelta(int from, int to) => ((to - from + 32768) & 65535) - 32768;

        // Integer CORDIC: no platform-dependent trigonometry in target selection/steering.
        internal static Vector3 Direction(int angle)
        {
            int turn = AngleDelta(0, angle), sign = 1;
            if (turn > 16384) { turn -= 32768; sign = -1; }
            else if (turn < -16384) { turn += 32768; sign = -1; }
            long x = 652032874, y = 0;
            for (int i = 0; i < ArcAngles.Length; i++)
            {
                int d = turn >= 0 ? 1 : -1;
                long nextX = x - d * (y >> i);
                y += d * (x >> i); x = nextX; turn -= d * ArcAngles[i];
            }
            return new Vector3(Scalar(sign * y * UnitsPerCell / (1L << 30)), 0f,
                Scalar(sign * x * UnitsPerCell / (1L << 30)));
        }

        internal static int Yaw(Vector3 direction)
        {
            long x = Units(direction.z) << 16, y = Units(direction.x) << 16;
            if (x == 0 && y == 0) return 0;
            int angle = 0;
            if (x < 0) { x = -x; y = -y; angle = 32768; }
            for (int i = 0; i < ArcAngles.Length; i++)
            {
                int d = y >= 0 ? 1 : -1;
                long nextX = x + d * (y >> i);
                y -= d * (x >> i); x = nextX; angle += d * ArcAngles[i];
            }
            return angle & 65535;
        }

        internal static int Turn(int current, Vector3 direction, float speed, float seconds)
        {
            int delta = AngleDelta(current, Yaw(direction));
            int step = (int)Math.Round((double)speed * 65536 / 360) * (int)Ticks(seconds) / TicksPerSecond;
            return (current + Math.Max(-step, Math.Min(step, delta))) & 65535;
        }

        internal static Vector3 Rotate(Vector3 direction, float degrees)
        {
            Vector3 rotation = Direction(Angle(degrees));
            long sin = Units(rotation.x), cos = Units(rotation.z), x = Units(direction.x), z = Units(direction.z);
            return new Vector3(Scalar((x * cos + z * sin) / UnitsPerCell), direction.y,
                Scalar((z * cos - x * sin) / UnitsPerCell));
        }

        internal static Vector3 RandomDisk(uint radial, uint angular, float radius)
        {
            long distance = (long)Sqrt((ulong)(radial & 0xFFFFFFu) << 8) * Units(radius) / 65536;
            Vector3 direction = Direction((int)(angular & 65535));
            return new Vector3(Scalar(Units(direction.x) * distance / UnitsPerCell), 0f,
                Scalar(Units(direction.z) * distance / UnitsPerCell));
        }
        internal static long Units(float value) => (long)Math.Round((double)value * UnitsPerCell, MidpointRounding.ToEven);
        internal static float Scalar(long value) => value / (float)UnitsPerCell;
        internal static long Ticks(float seconds) => (long)Math.Round((double)seconds * TicksPerSecond, MidpointRounding.ToEven);
        internal static float Seconds(long ticks) => ticks / (float)TicksPerSecond;
        internal static Vector3 Quantize(Vector3 value) => new Vector3(Scalar(Units(value.x)), Scalar(Units(value.y)), Scalar(Units(value.z)));
        internal static Vector2Int Cell(Vector3 value) => new Vector2Int(RoundCell(Units(value.x)), RoundCell(Units(value.z)));
        private static int RoundCell(long value)
        {
            long whole = value / UnitsPerCell;
            long remainder = value % UnitsPerCell;
            long magnitude = Math.Abs(remainder);
            if (magnitude > UnitsPerCell / 2 || magnitude == UnitsPerCell / 2 && (whole & 1) != 0)
                whole += Math.Sign(remainder);
            return (int)whole;
        }

        internal static ulong Sqrt(ulong value)
        {
            ulong result = 0, bit = 1UL << 62;
            while (bit > value) bit >>= 2;
            while (bit != 0)
            {
                if (value >= result + bit) { value -= result + bit; result = (result >> 1) + bit; }
                else result >>= 1;
                bit >>= 2;
            }
            return result;
        }

        internal static Vector3 Normalize(Vector3 value)
        {
            long x = Units(value.x), y = Units(value.y), z = Units(value.z);
            long length = (long)Sqrt((ulong)(x * x + y * y + z * z));
            return length == 0 ? Vector3.zero : new Vector3(Scalar(x * UnitsPerCell / length), Scalar(y * UnitsPerCell / length), Scalar(z * UnitsPerCell / length));
        }

        internal static float Magnitude(Vector3 value)
        {
            long x = Units(value.x), y = Units(value.y), z = Units(value.z);
            return Scalar((long)Sqrt((ulong)(x * x + y * y + z * z)));
        }

        internal static Vector3 ClampMagnitude(Vector3 value, float maximum)
            => Magnitude(value) > maximum ? Quantize(Normalize(value) * maximum) : Quantize(value);

        internal static Vector3 Advance(Vector3 origin, Vector3 direction, float speed, float seconds)
        {
            long distance = Units(speed) * Ticks(seconds) / TicksPerSecond;
            return new Vector3(Scalar(Units(origin.x) + Units(direction.x) * distance / UnitsPerCell),
                origin.y, Scalar(Units(origin.z) + Units(direction.z) * distance / UnitsPerCell));
        }

        internal static bool IsGridPositionClear(Vector3 origin, Vector3 position, float radius, bool allowEscape, Func<int, int, bool> walkable)
        {
            long px = Units(position.x), pz = Units(position.z), ox = Units(origin.x), oz = Units(origin.z);
            long r = Math.Max(1, Units(radius)), half = UnitsPerCell / 2;
            int minX = (int)Math.Ceiling((double)(px - r - half) / UnitsPerCell);
            int maxX = (int)Math.Floor((double)(px + r + half) / UnitsPerCell);
            int minZ = (int)Math.Ceiling((double)(pz - r - half) / UnitsPerCell);
            int maxZ = (int)Math.Floor((double)(pz + r + half) / UnitsPerCell);
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    long cx = x * UnitsPerCell, cz = z * UnitsPerCell;
                    long next = BoxDistanceSquared(px, pz, cx, cz, half);
                    if (next >= r * r || walkable(x, z)) continue;
                    if (allowEscape)
                    {
                        long before = BoxDistanceSquared(ox, oz, cx, cz, half);
                        if (before < r * r && (next > before
                            || next == 0 && before == 0 && ((px - ox) * (ox - cx) + (pz - oz) * (oz - cz) > 0
                                || ox == cx && oz == cz && (px != ox || pz != oz)))) continue;
                    }
                    return false;
                }
            return true;
        }

        private static long BoxDistanceSquared(long x, long z, long cx, long cz, long half)
        {
            long dx = Math.Max(0, Math.Abs(x - cx) - half), dz = Math.Max(0, Math.Abs(z - cz) - half);
            return dx * dx + dz * dz;
        }
    }

    internal readonly struct AnimalFixedPosition
    {
        internal readonly long X, Y, Z;
        internal AnimalFixedPosition(Vector3 value)
        { X = AnimalSimulationMath.Units(value.x); Y = AnimalSimulationMath.Units(value.y); Z = AnimalSimulationMath.Units(value.z); }
        internal Vector3 Value => new Vector3(AnimalSimulationMath.Scalar(X), AnimalSimulationMath.Scalar(Y), AnimalSimulationMath.Scalar(Z));
    }

    internal readonly struct AnimalTickTimer
    {
        internal readonly long Ticks;
        private AnimalTickTimer(long ticks) { Ticks = ticks; }
        public static implicit operator AnimalTickTimer(float seconds) => new AnimalTickTimer(AnimalSimulationMath.Ticks(seconds));
        public static implicit operator float(AnimalTickTimer timer) => AnimalSimulationMath.Seconds(timer.Ticks);
        public static AnimalTickTimer operator +(AnimalTickTimer timer, float seconds) => new AnimalTickTimer(timer.Ticks + AnimalSimulationMath.Ticks(seconds));
        public static AnimalTickTimer operator -(AnimalTickTimer timer, float seconds) => new AnimalTickTimer(timer.Ticks - AnimalSimulationMath.Ticks(seconds));
    }
}
