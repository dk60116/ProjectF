using System;

namespace ProjectF.Simulation
{
    // A settled prefix followed by one regularly spaced emission batch. No item objects are required.
    public struct OutputStackBatch
    {
        public int ItemId, Count, MovingCount;
        public float StartTime, Interval, Duration;
        public bool IsEmpty => Count == 0;
        public float LastArrivalTime => StartTime + Math.Max(0, MovingCount - 1) * Interval + Duration;

        public bool TryAppend(int itemId, float now, float delay, float duration)
        {
            if (Count > 0 && ItemId != itemId) return false;
            if (MovingCount > 0 && now >= LastArrivalTime) MovingCount = 0;
            float start = now + Math.Max(0f, delay);
            if (MovingCount == 0)
            { StartTime = start; Interval = 0; Duration = duration; }
            else
            {
                float interval = MovingCount == 1 ? start - StartTime : Interval;
                if (interval < 0 || Math.Abs(duration - Duration) > 0.00001f
                    || Math.Abs(start - (StartTime + MovingCount * interval)) > 0.00001f) return false;
                Interval = interval;
            }
            ItemId = itemId; Count++; MovingCount++; return true;
        }

        public bool TryRemoveTop(int expectedItemId, out int itemId)
        {
            itemId = -1;
            if (Count == 0 || expectedItemId >= 0 && ItemId != expectedItemId) return false;
            itemId = ItemId; Count--; if (MovingCount > 0) MovingCount--;
            if (Count == 0) this = default;
            return true;
        }

        public bool TryPeekBottom(float now, out float launchTime, out bool moving)
        {
            launchTime = StartTime; moving = false;
            if (Count == 0) return false;
            moving = Count == MovingCount && now < StartTime + Duration;
            return true;
        }

        public void RemoveBottom()
        {
            if (Count == 0) return;
            if (Count == MovingCount) { MovingCount--; StartTime += Interval; }
            Count--;
            if (Count == 0) this = default;
        }
    }
}
