using System;

namespace ProjectF.Animals
{
    // Diagnostics only. Simulation budgets use cache-independent SchedulingWorkCount.
    internal static class AnimalAIProfiler
    {
        internal enum Counter
        {
            SpatialUpdates, SpatialCellChanges, HerdRefreshes, NeedsUpdates,
            AStarCalls, ReachableCalls, PathNodes, LineChecks, LineSamples,
            AvoidanceProbes, Presentations, CulledPresentations, PathBudgetDeferrals,
            PathBudgetOverruns, PathBudgetMaxWorkPerTick,
            WalkableCacheHits, WalkableCacheMisses, RegionCacheHits, RegionCacheBuilds, GridCollisionQueries,
            Count
        }

        private static readonly int[] current = new int[(int)Counter.Count];
        private static readonly int[] completed = new int[(int)Counter.Count];
        private static readonly string[] names = Enum.GetNames(typeof(Counter));

        internal static MapObjectTickProfiler.NamedSampleScope Sample(string name)
            => MapObjectTickProfiler.SampleNamed("AI Detail", "AnimalAI", name);

        private static int searchScopeDepth;

        internal readonly struct SearchScope : IDisposable
        {
            private readonly bool enabled;
            private readonly bool line;
            private readonly bool countNodes;
            private readonly long before;
            private readonly MapObjectTickProfiler.NamedSampleScope sample;

            internal SearchScope(bool reachable, bool line = false)
            {
                enabled = MapObjectTickProfiler.IsEnabled;
                this.line = line;
                countNodes = !line && searchScopeDepth++ == 0;
                before = line ? AnimalGridPathfinder.WalkableLineSampleCount : AnimalGridPathfinder.ExpandedNodeCount;
                sample = Sample(line ? "Animal Path Line Checks" : reachable ? "Animal Reachable Target" : "Animal Path AStar");
                Add(line ? Counter.LineChecks : reachable ? Counter.ReachableCalls : Counter.AStarCalls);
            }

            public void Dispose()
            {
                sample.Dispose();
                if (!line) searchScopeDepth--;
                if (!enabled || !line && !countNodes) return;
                long after = line ? AnimalGridPathfinder.WalkableLineSampleCount : AnimalGridPathfinder.ExpandedNodeCount;
                Add(line ? Counter.LineSamples : Counter.PathNodes, (int)(after - before));
            }
        }

        internal static void Add(Counter counter, int count = 1)
        {
            if (MapObjectTickProfiler.IsEnabled) current[(int)counter] += count;
        }

        internal static void Max(Counter counter, int count)
        {
            if (MapObjectTickProfiler.IsEnabled)
                current[(int)counter] = Math.Max(current[(int)counter], count);
        }

        internal static void CompleteFrame()
        {
            Array.Copy(current, completed, current.Length);
            Array.Clear(current, 0, current.Length);
        }

        internal static void Reset()
        {
            Array.Clear(current, 0, current.Length);
            Array.Clear(completed, 0, completed.Length);
        }

        internal static void AppendCounters()
        {
            for (int i = 0; i < completed.Length; i++)
                MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", names[i], completed[i]);
        }
    }
}
