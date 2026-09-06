using System;

namespace ProjectF.FluidTransport
{
    // One-second rolling output volume, grouped into 50 ms buckets without per-sample allocations.
    public sealed class FluidOutputRateMeter
    {
        private const int BucketCount = 20;
        private readonly long[] bucketTicks = new long[BucketCount];
        private readonly float[] bucketLiters = new float[BucketCount];
        private int fluidItemId = -1;
        private double lastObservedTime = double.NegativeInfinity;

        public void Reset()
        {
            Array.Clear(bucketLiters, 0, bucketLiters.Length);
            fluidItemId = -1;
            lastObservedTime = double.NegativeInfinity;
        }

        public void Record(int itemId, float liters, double now)
        {
            if (itemId < 0 || liters <= 0f || float.IsNaN(liters) || float.IsInfinity(liters))
            {
                return;
            }

            if (fluidItemId != itemId || now < lastObservedTime)
            {
                Reset();
            }

            fluidItemId = itemId;
            lastObservedTime = now;
            long tick = (long)Math.Floor(now * BucketCount);
            int index = (int)(tick % BucketCount);
            if (bucketTicks[index] != tick)
            {
                bucketTicks[index] = tick;
                bucketLiters[index] = 0f;
            }

            bucketLiters[index] += liters;
        }

        public float GetLitersPerSecond(int itemId, double now)
        {
            if (now < lastObservedTime)
            {
                Reset();
            }

            lastObservedTime = now;
            if (itemId < 0 || itemId != fluidItemId)
            {
                return 0f;
            }

            long tick = (long)Math.Floor(now * BucketCount);
            float liters = 0f;
            for (int i = 0; i < BucketCount; i++)
            {
                long age = tick - bucketTicks[i];
                if (age >= 0 && age < BucketCount)
                {
                    liters += bucketLiters[i];
                }
            }

            return liters;
        }
    }
}
