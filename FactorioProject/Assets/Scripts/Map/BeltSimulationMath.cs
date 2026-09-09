using System;

namespace ProjectF.Conveyors
{
    public static class BeltSimulationMath
    {
        // Exact conversion from an IEEE-754 input bit pattern, with integer rounding.
        // Configuration and geometry are quantized at the main-thread bake boundary.
        public static long QuantizePositive(float value, int scale)
        {
            uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
            int exponent = (int)((bits >> 23) & 255);
            if ((bits & 0x80000000u) != 0 || exponent == 255 || exponent == 0) return 0;
            long mantissa = ((bits & 0x7fffffu) | 0x800000u) * (long)scale;
            int shift = exponent - 127 - 23;
            if (shift >= 0) return shift > 30 || mantissa > (long.MaxValue >> shift) ? long.MaxValue : mantissa << shift;
            shift = -shift;
            return shift >= 63 ? 0 : (mantissa + (1L << (shift - 1))) >> shift;
        }

        public static long Duration(float length, float speed)
        {
            long millimeters = Math.Max(1, QuantizePositive(length, 1000));
            long speedMillimeters = QuantizePositive(speed, 1000);
            if (speedMillimeters <= 0) return 0;
            long numerator = Math.Min(millimeters, 1000000000) * BeltSimulationJob.TickUnits * BeltSimulationJob.TickRate;
            return Math.Max(1, numerator / speedMillimeters + (numerator % speedMillimeters != 0 ? 1 : 0));
        }

        public static long Seconds(float seconds) => QuantizePositive(seconds,
            (int)(BeltSimulationJob.TickUnits * BeltSimulationJob.TickRate));
    }
}
