using System;
using System.Collections.Generic;

namespace ProjectF.FluidTransport
{
    // One-second rolling output volume per fluid, grouped into 50 ms buckets.
    public sealed class FluidOutputRateMeter
    {
        private const int BucketCount = 20;
        private sealed class FluidBuckets
        {
            public readonly long[] Ticks = new long[BucketCount];
            public readonly float[] Liters = new float[BucketCount];

            public void Reset()
            {
                Array.Clear(Ticks, 0, Ticks.Length);
                Array.Clear(Liters, 0, Liters.Length);
            }
        }

        private readonly Dictionary<int, FluidBuckets> bucketsByFluid =
            new Dictionary<int, FluidBuckets>(4);
        private double lastObservedTime = double.NegativeInfinity;

        public void Reset()
        {
            foreach (FluidBuckets buckets in bucketsByFluid.Values)
            {
                buckets.Reset();
            }
            lastObservedTime = double.NegativeInfinity;
        }

        public void Record(int itemId, float liters, double now)
        {
            if (itemId < 0 || liters <= 0f || float.IsNaN(liters) || float.IsInfinity(liters))
            {
                return;
            }

            if (now < lastObservedTime)
            {
                Reset();
            }

            lastObservedTime = now;
            if (!bucketsByFluid.TryGetValue(itemId, out FluidBuckets buckets))
            {
                buckets = new FluidBuckets();
                bucketsByFluid.Add(itemId, buckets);
            }

            long tick = (long)Math.Floor(now * BucketCount);
            int index = (int)(tick % BucketCount);
            if (buckets.Ticks[index] != tick)
            {
                buckets.Ticks[index] = tick;
                buckets.Liters[index] = 0f;
            }

            buckets.Liters[index] += liters;
        }

        public float GetLitersPerSecond(int itemId, double now)
        {
            if (now < lastObservedTime)
            {
                Reset();
            }

            lastObservedTime = now;
            if (itemId < 0 || !bucketsByFluid.TryGetValue(itemId, out FluidBuckets buckets))
            {
                return 0f;
            }

            long tick = (long)Math.Floor(now * BucketCount);
            float liters = 0f;
            for (int i = 0; i < BucketCount; i++)
            {
                long age = tick - buckets.Ticks[i];
                if (age >= 0 && age < BucketCount)
                {
                    liters += buckets.Liters[i];
                }
            }

            return liters;
        }
    }
}
