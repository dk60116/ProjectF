using ProjectF.Benchmark;

// Target membership is doubled; runtime dispatch and receiver command branch are actual sources.
public class InstallationObject : IBenchmarkWorkProgressTarget
{
    public static readonly List<InstallationObject> Active = new();
    public bool Running = true;
    public int Calls;
    public Random SeenRandom;
    public bool TryRandomizeWorkProgress(Random random) { SeenRandom = random; Calls++; return Running; }
    public static void CopyActiveInstances(List<InstallationObject> result) { result.Clear(); result.AddRange(Active); }
}
public class MiningMachineInstance : InstallationObject { }
public class ProductionFacilityInstance : InstallationObject { }
public class RobotArmInstance : InstallationObject { }
public class MiningWorld { public static MiningWorld Current; public IReadOnlyList<MiningMachineInstance> Instances; }
public class ProductionWorld { public static ProductionWorld Current; public IReadOnlyList<ProductionFacilityInstance> Instances; }
public class RobotArmWorld { public static RobotArmWorld Current; public IReadOnlyList<RobotArmInstance> Instances; }
namespace ProjectF.Benchmark
{
    public static partial class BenchmarkRuntime
    {
        private static readonly List<InstallationObject> installations = new();
        public static bool ForceWorking;
        public static int ScratchCount => installations.Count;
    }
}
public readonly record struct ToolResult(string Message)
{
    public static ToolResult Success(int a, int b, int c, int d, int e, int f, string message, string status) => new(message);
}
internal static class ProgressChecks
{
    public static void Main()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        var native = new InstallationObject(); var idle = new InstallationObject { Running = false };
        var miner = new MiningMachineInstance(); var production = new ProductionFacilityInstance(); var arm = new RobotArmInstance();
        InstallationObject.Active.AddRange(new[] { native, idle });
        MiningWorld.Current = new() { Instances = new[] { miner } };
        ProductionWorld.Current = new() { Instances = new[] { production } };
        RobotArmWorld.Current = new() { Instances = new[] { arm } };
        Check(BenchmarkRuntime.RandomizeWorkProgress() == 4, "dispatch includes native/mining/production/robot active work and counts success only");
        Check(ReferenceEquals(native.SeenRandom, miner.SeenRandom) && ReferenceEquals(miner.SeenRandom, arm.SeenRandom), "all targets share one random generator per invocation");
        Check(BenchmarkRuntime.ScratchCount == 0 && !BenchmarkRuntime.ForceWorking, "dispatch releases native references without changing force mode");
        Check(BenchmarkCommand.TryParse("benchmark randomizeprogress".Split(' '), out var command, out _), "command needs no item selection");
        var result = new ReceiverProgressProbe().Run(command);
        Check(result.Message.Contains("4 active operations") && miner.Calls == 2 && arm.Calls == 2, "actual receiver branch invokes all target worlds and reports changed count");
        BenchmarkRuntime.ForceWorking = true;
        Check(BenchmarkRuntime.RandomizeWorkProgress() == 4 && BenchmarkRuntime.ForceWorking, "same button works under force mode");
        InstallationObject.Active.Clear(); MiningWorld.Current = null; ProductionWorld.Current = null; RobotArmWorld.Current = null;
        Check(BenchmarkRuntime.RandomizeWorkProgress() == 0, "empty world is a no-op");
        Console.WriteLine($"PASS {checks} benchmark progress dispatch/receiver checks");
    }
}
