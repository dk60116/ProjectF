using System;
using System.Collections.Generic;

namespace ProjectF.Benchmark
{
    public static partial class BenchmarkRuntime
    {
        public static int RandomizeWorkProgress()
        {
            var random = new Random();
            int changed = 0;
            InstallationObject.CopyActiveInstances(installations);
            try
            {
                for (int i = 0; i < installations.Count; i++)
                    if (installations[i] is IBenchmarkWorkProgressTarget target && target.TryRandomizeWorkProgress(random)) changed++;
            }
            finally { installations.Clear(); }
            changed += Randomize(MiningWorld.Current?.Instances, random);
            changed += Randomize(ProductionWorld.Current?.Instances, random);
            changed += Randomize(ProjectF.MapObjects.ForestryWorld.Current?.Instances, random);
            changed += Randomize(RobotArmWorld.Current?.Instances, random);
            return changed;
        }

        private static int Randomize<T>(IReadOnlyList<T> targets, Random random) where T : IBenchmarkWorkProgressTarget
        {
            int changed = 0;
            for (int i = 0; targets != null && i < targets.Count; i++)
                if (targets[i].TryRandomizeWorkProgress(random)) changed++;
            return changed;
        }
    }
}
