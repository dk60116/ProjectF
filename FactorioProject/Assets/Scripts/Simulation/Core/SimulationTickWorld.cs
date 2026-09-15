using System;
using System.Collections.Generic;

namespace ProjectF.Simulation
{
    public interface ISimulationTickObserver
    {
        void OnActiveTargets(ICollection<IMapObjectUpdateTick> targets);
        long BeginSample();
        void EndSample(IMapObjectUpdateTick target, long started);
    }

    public interface ISimulationCommand
    {
        void Execute(SimulationTickWorld world);
    }

    /// <summary>Owns fixed-tick scheduling independently of scene objects and render time.</summary>
    public sealed class SimulationTickWorld : IDisposable
    {
        private static readonly Comparison<UpdateTickEntry> EntryComparison = CompareUpdateTickEntries;
        public const int DefaultSimulationTicksPerSecond = 60;
        public const float FixedSimulationDeltaSeconds = 1f / DefaultSimulationTicksPerSecond;
        private readonly List<UpdateTickBucket> updateTickBuckets = new List<UpdateTickBucket>(4);
        private readonly Dictionary<int, UpdateTickBucket> updateTickBucketsByIntervalKey =
            new Dictionary<int, UpdateTickBucket>(4);
        private readonly HashSet<IMapObjectUpdateTick> updateTickSet = new HashSet<IMapObjectUpdateTick>();
        private readonly HashSet<IMapObjectUpdateTick> updateTickEntrySet = new HashSet<IMapObjectUpdateTick>();
        private readonly Dictionary<IMapObjectUpdateTick, UpdateTickEntry> updateTickEntriesByTick =
            new Dictionary<IMapObjectUpdateTick, UpdateTickEntry>();
        private readonly List<UpdateTickEntry> dueUpdateTickEntries = new List<UpdateTickEntry>(64);
        private readonly Queue<ISimulationCommand> commands = new Queue<ISimulationCommand>();
        private long simulationTick;
        private bool tickingUpdateObjects;
        private bool updateTicksDirty;
        private bool stepping;
        private bool disposed;
        private float updateTickIntervalSeconds = FixedSimulationDeltaSeconds;
        public long CurrentTick => simulationTick;
        public int RegisteredCount => updateTickSet.Count;
        // Pending commands are not part of the current disk DTO; never silently omit them.
        public bool CanCaptureCheckpoint => !disposed && !stepping && commands.Count == 0;
        public bool Paused { get; set; }
        public ISimulationTickObserver Observer { get; set; }
        public float DefaultIntervalSeconds { get => updateTickIntervalSeconds; set => updateTickIntervalSeconds = value; }

        public void Enqueue(ISimulationCommand command)
        {
            ThrowIfDisposed();
            if (command == null) throw new ArgumentNullException(nameof(command));
            commands.Enqueue(command);
        }

        public bool Step()
        {
            ThrowIfDisposed();
            if (stepping) throw new InvalidOperationException("Simulation Step is not reentrant.");
            if (Paused) return false;
            stepping = true;
            try
            {
                simulationTick = checked(simulationTick + 1);
                int count = commands.Count;
                for (int i = 0; i < count; i++) commands.Dequeue().Execute(this);
                TickUpdateObjects();
                return true;
            }
            finally { stepping = false; }
        }

        public void RestoreTick(long tick)
        {
            ThrowIfDisposed();
            if (stepping) throw new InvalidOperationException("Cannot restore the clock during a tick.");
            simulationTick = Math.Max(0, tick);
            ResetUpdateTickBucketState();
        }

        public void Dispose()
        {
            if (stepping) throw new InvalidOperationException("Cannot dispose during a tick.");
            commands.Clear(); updateTickSet.Clear(); updateTickEntrySet.Clear();
            updateTickEntriesByTick.Clear(); updateTickBuckets.Clear(); updateTickBucketsByIntervalKey.Clear();
            dueUpdateTickEntries.Clear(); Observer = null; disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SimulationTickWorld));
        }

        private static int RoundInterval(float value)
        {
            if (float.IsNaN(value) || value <= 1) return 1;
            return value >= int.MaxValue ? int.MaxValue : (int)Math.Round(value, MidpointRounding.ToEven);
        }

        public void Register(IMapObjectUpdateTick tick)
        {
            ThrowIfDisposed();
            if (updateTicksDirty && !tickingUpdateObjects)
            {
                CompactUpdateTicks();
            }

            if (tick == null)
            {
                return;
            }

            if (updateTickSet.Contains(tick))
            {
                if (Contains(tick))
                {
                    return;
                }

                // The active marker can survive after its bucket entry is lost.
                // Remove the incomplete registration so it can be rebuilt below.
                updateTickSet.Remove(tick);
                updateTicksDirty = true;
                if (tickingUpdateObjects)
                {
                    return;
                }

                CompactUpdateTicks();
            }

            if (updateTickEntrySet.Contains(tick))
            {
                updateTickSet.Add(tick);
                if (updateTickEntriesByTick.TryGetValue(tick, out UpdateTickEntry entry))
                {
                    entry.ResetSchedule(simulationTick);
                }

                return;
            }

            updateTickSet.Add(tick);
            updateTickEntrySet.Add(tick);
            UpdateTickBucket bucket = GetOrCreateUpdateTickBucket(ResolveUpdateTickIntervalTicks(tick));
            UpdateTickEntry newEntry = new UpdateTickEntry(tick, bucket.IntervalTicks, simulationTick);
            updateTickEntriesByTick[tick] = newEntry;
            bucket.Entries.Add(newEntry);
            bucket.OrderDirty = true;
        }

        public void Unregister(IMapObjectUpdateTick tick)
        {
            if (tick == null || !updateTickSet.Remove(tick))
            {
                return;
            }

            updateTicksDirty = true;
        }

        private void TickUpdateObjects()
        {
            if (updateTicksDirty)
            {
                CompactUpdateTicks();
            }

            int count = updateTickSet.Count;
            ISimulationTickObserver observer = Observer;
            if (observer != null)
            {
                observer.OnActiveTargets(updateTickSet);
            }
            if (count <= 0)
            {
                ResetUpdateTickBucketState();
                return;
            }

            tickingUpdateObjects = true;
            try
            {
                dueUpdateTickEntries.Clear();
                for (int bucketIndex = 0; bucketIndex < updateTickBuckets.Count; bucketIndex++)
                {
                    CollectDueUpdateEntries(updateTickBuckets[bucketIndex]);
                }

                dueUpdateTickEntries.Sort(EntryComparison);
                PlanStagedUpdateEntries();
                ApplyDueUpdateEntries(observer);
                dueUpdateTickEntries.Clear();
            }
            finally
            {
                tickingUpdateObjects = false;
            }

            if (updateTicksDirty)
            {
                CompactUpdateTicks();
            }

        }

        private void CollectDueUpdateEntries(UpdateTickBucket bucket)
        {
            if (bucket == null)
            {
                return;
            }

            int count = bucket.Entries.Count;
            if (count <= 0)
            {
                return;
            }

            if (bucket.OrderDirty)
            {
                bucket.Entries.Sort(EntryComparison);
                bucket.OrderDirty = false;
            }

            List<UpdateTickEntry> entries = bucket.Entries;
            for (int entryIndex = 0; entryIndex < count; entryIndex++)
            {
                UpdateTickEntry entry = entries[entryIndex];
                if (entry == null)
                {
                    updateTicksDirty = true;
                    continue;
                }

                IMapObjectUpdateTick tick = entry.Tick;
                if (tick == null)
                {
                    updateTicksDirty = true;
                    continue;
                }

                if (updateTicksDirty && !updateTickSet.Contains(tick))
                {
                    continue;
                }

                if (entry.NextDueTick > simulationTick)
                {
                    continue;
                }

                long elapsedTicks = Math.Max(1L, simulationTick - entry.LastExecutedTick);
                entry.PendingDeltaTime = elapsedTicks * FixedSimulationDeltaSeconds;
                entry.MarkExecuted(simulationTick);
                dueUpdateTickEntries.Add(entry);
            }
        }

        private void PlanStagedUpdateEntries()
        {
            for (int i = 0; i < dueUpdateTickEntries.Count; i++)
            {
                UpdateTickEntry entry = dueUpdateTickEntries[i];
                if (entry?.Tick is IMapObjectStagedUpdateTick stagedTick && updateTickSet.Contains(entry.Tick))
                {
                    stagedTick.PlanManagedUpdateTick(entry.PendingDeltaTime);
                }
            }
        }

        private void ApplyDueUpdateEntries(ISimulationTickObserver observer)
        {
            for (int i = 0; i < dueUpdateTickEntries.Count; i++)
            {
                UpdateTickEntry entry = dueUpdateTickEntries[i];
                IMapObjectUpdateTick tick = entry?.Tick;
                if (tick == null || updateTicksDirty && !updateTickSet.Contains(tick))
                {
                    continue;
                }

                long startTimestamp = observer != null ? observer.BeginSample() : 0L;
                if (tick is IMapObjectStagedUpdateTick stagedTick)
                {
                    stagedTick.ApplyManagedUpdateTick();
                }
                else
                {
                    tick.ManagedUpdateTick(entry.PendingDeltaTime);
                }

                if (observer != null)
                {
                    observer.EndSample(tick, startTimestamp);
                }
            }
        }

        public bool Contains(IMapObjectUpdateTick tick)
        {
            return tick != null
                   && updateTickSet.Contains(tick)
                   && updateTickEntrySet.Contains(tick)
                   && updateTickEntriesByTick.TryGetValue(tick, out UpdateTickEntry entry)
                   && entry != null
                   && ReferenceEquals(entry.Tick, tick);
        }

        private void CompactUpdateTicks()
        {
            updateTickEntrySet.Clear();
            updateTickEntriesByTick.Clear();
            for (int bucketIndex = updateTickBuckets.Count - 1; bucketIndex >= 0; bucketIndex--)
            {
                UpdateTickBucket bucket = updateTickBuckets[bucketIndex];
                if (bucket == null)
                {
                    updateTickBuckets.RemoveAt(bucketIndex);
                    continue;
                }

                List<UpdateTickEntry> entries = bucket.Entries;
                int writeIndex = 0;
                for (int readIndex = 0; readIndex < entries.Count; readIndex++)
                {
                    UpdateTickEntry entry = entries[readIndex];
                    IMapObjectUpdateTick tick = entry != null ? entry.Tick : null;
                    if (tick == null || !updateTickSet.Contains(tick))
                    {
                        if (tick != null)
                        {
                            updateTickSet.Remove(tick);
                        }

                        continue;
                    }

                    if (!updateTickEntrySet.Add(tick))
                    {
                        continue;
                    }

                    updateTickEntriesByTick[tick] = entry;
                    entries[writeIndex] = entry;
                    writeIndex++;
                }

                if (writeIndex < entries.Count)
                {
                    entries.RemoveRange(writeIndex, entries.Count - writeIndex);
                }

                if (entries.Count <= 0)
                {
                    updateTickBucketsByIntervalKey.Remove(bucket.IntervalTicks);
                    updateTickBuckets.RemoveAt(bucketIndex);
                    continue;
                }

                bucket.OrderDirty = true;
            }

            updateTicksDirty = false;
        }

        private UpdateTickBucket GetOrCreateUpdateTickBucket(int intervalTicks)
        {
            intervalTicks = Math.Max(1, intervalTicks);
            if (updateTickBucketsByIntervalKey.TryGetValue(intervalTicks, out UpdateTickBucket bucket))
            {
                return bucket;
            }

            bucket = new UpdateTickBucket(intervalTicks);
            updateTickBucketsByIntervalKey.Add(intervalTicks, bucket);
            updateTickBuckets.Add(bucket);
            updateTickBuckets.Sort((left, right) => left.IntervalTicks.CompareTo(right.IntervalTicks));
            return bucket;
        }

        private int ResolveUpdateTickIntervalTicks(IMapObjectUpdateTick tick)
        {
            float intervalSeconds = updateTickIntervalSeconds;
            if (tick is IMapObjectUpdateTickInterval intervalProvider)
            {
                intervalSeconds = intervalProvider.ManagedUpdateTickIntervalSeconds;
            }

            return Math.Max(1, RoundInterval(
                Math.Max(FixedSimulationDeltaSeconds, intervalSeconds)
                / FixedSimulationDeltaSeconds));
        }

        private static int CompareUpdateTickEntries(UpdateTickEntry left, UpdateTickEntry right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            long leftId = ResolveSimulationId(left.Tick);
            long rightId = ResolveSimulationId(right.Tick);
            int result = leftId.CompareTo(rightId);
            if (result != 0)
            {
                return result;
            }

            string leftType = left.Tick?.GetType().FullName ?? string.Empty;
            string rightType = right.Tick?.GetType().FullName ?? string.Empty;
            return string.CompareOrdinal(leftType, rightType);
        }

        private static long ResolveSimulationId(IMapObjectUpdateTick tick)
        {
            return tick is IMapObjectSimulationIdentity identity
                ? identity.SimulationId
                : 0L;
        }

        private void ResetUpdateTickBucketState()
        {
            for (int i = 0; i < updateTickBuckets.Count; i++)
            {
                UpdateTickBucket bucket = updateTickBuckets[i];
                if (bucket == null)
                {
                    continue;
                }

                for (int entryIndex = 0; entryIndex < bucket.Entries.Count; entryIndex++)
                {
                    bucket.Entries[entryIndex]?.ResetSchedule(simulationTick);
                }
            }
        }

        private sealed class UpdateTickEntry
        {
            public readonly IMapObjectUpdateTick Tick;
            public readonly int IntervalTicks;
            public long LastExecutedTick;
            public long NextDueTick;
            public float PendingDeltaTime;

            public UpdateTickEntry(IMapObjectUpdateTick tick, int intervalTicks, long currentTick)
            {
                Tick = tick;
                IntervalTicks = Math.Max(1, intervalTicks);
                ResetSchedule(currentTick);
            }

            public void ResetSchedule(long currentTick)
            {
                LastExecutedTick = currentTick;
                NextDueTick = currentTick + 1L;
            }

            public void MarkExecuted(long currentTick)
            {
                LastExecutedTick = currentTick;
                NextDueTick = currentTick + IntervalTicks;
            }
        }

        private sealed class UpdateTickBucket
        {
            public readonly int IntervalTicks;
            public readonly List<UpdateTickEntry> Entries = new List<UpdateTickEntry>();
            public bool OrderDirty;

            public UpdateTickBucket(int intervalTicks)
            {
                IntervalTicks = intervalTicks;
            }
        }
    }
}
