using System;
using System.Collections;
using System.Diagnostics;

namespace ProjectF.Persistence
{
    /// <summary>
    /// Snapshot iterators yield null at small work checkpoints. Consume as many
    /// checkpoints as fit in one time slice, sharing the budget across all stages.
    /// A single checkpoint is indivisible and can exceed the configured budget.
    /// </summary>
    public sealed class SaveSnapshotScheduler : IDisposable
    {
        private readonly IEnumerator work;
        private readonly Func<double> clock;
        private readonly double budgetMilliseconds;
        private bool finished;
        private bool disposed;

        public int SliceCount { get; private set; }
        public int CheckpointCount { get; private set; }
        public double ActiveMilliseconds { get; private set; }
        public double MaxSliceMilliseconds { get; private set; }

        public SaveSnapshotScheduler(
            IEnumerator work,
            double budgetMilliseconds,
            Func<double> clockMilliseconds = null)
        {
            this.work = work ?? throw new ArgumentNullException(nameof(work));
            if (double.IsNaN(budgetMilliseconds) || double.IsInfinity(budgetMilliseconds)
                || budgetMilliseconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            this.budgetMilliseconds = budgetMilliseconds;
            clock = clockMilliseconds ?? ReadMilliseconds;
        }

        /// <returns>True when more work must resume on the next frame.</returns>
        public bool RunSlice()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SaveSnapshotScheduler));
            if (finished) return false;

            double startedAt = clock();
            SliceCount++;
            try
            {
                do
                {
                    if (!work.MoveNext())
                    {
                        finished = true;
                        return false;
                    }

                    // A real Unity wait must never be silently treated as a checkpoint.
                    if (work.Current != null)
                        throw new InvalidOperationException("Snapshot work may only yield null checkpoints.");
                    CheckpointCount++;
                }
                while (clock() - startedAt < budgetMilliseconds);
                return true;
            }
            catch
            {
                finished = true;
                throw;
            }
            finally
            {
                double elapsed = Math.Max(0d, clock() - startedAt);
                ActiveMilliseconds += elapsed;
                MaxSliceMilliseconds = Math.Max(MaxSliceMilliseconds, elapsed);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            (work as IDisposable)?.Dispose();
        }

        private static double ReadMilliseconds()
            => Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency);
    }
}
