using System.Globalization;
using ProjectF.Diagnostics;
using UnityEngine.LowLevel;

static class Checks
{
    sealed class Root { } sealed class Update { } sealed class Scripts { } sealed class ThirdParty { }
    static int calls, assertions;
    static void Require(bool value, string label) { if (!value) throw new Exception(label); assertions++; }
    static void Execute(PlayerLoopSystem system)
    {
        system.updateDelegate?.Invoke();
        if (system.subSystemList != null) foreach (var child in system.subSystemList) Execute(child);
    }
    static int Count(PlayerLoopSystem system)
    {
        int result = 1;
        if (system.subSystemList != null) foreach (var child in system.subSystemList) result += Count(child);
        return result;
    }
    static void Main()
    {
        PlayerLoop.SetPlayerLoop(new PlayerLoopSystem { type = typeof(Root), subSystemList = new[] {
            new PlayerLoopSystem { type = typeof(Update), subSystemList = new[] {
                new PlayerLoopSystem { type = typeof(Scripts), updateDelegate = () => calls++ },
                new PlayerLoopSystem { type = typeof(ThirdParty), updateDelegate = () => calls++ }
            } }
        } });
        FramePhaseProfiler.SetEnabled(true);
        int wrappedCount = Count(PlayerLoop.GetCurrentPlayerLoop());
        FramePhaseProfiler.SetEnabled(true);
        Require(Count(PlayerLoop.GetCurrentPlayerLoop()) == wrappedCount, "enable is idempotent");
        Execute(PlayerLoop.GetCurrentPlayerLoop());
        FramePhaseProfiler.AppendCounters();
        Require(calls == 2, "existing native/third-party entries execute once");
        Require(MapObjectTickProfiler.Counters["CompletedFrames"].Value == "1", "only completed frames recorded");
        Require(MapObjectTickProfiler.Counters.ContainsKey("Update.ScriptsMs"), "script phase available without Unity recorders");
        for (int i = 0; i < 200; i++) Execute(PlayerLoop.GetCurrentPlayerLoop());
        FramePhaseProfiler.AppendCounters();
        Require(MapObjectTickProfiler.Counters["CompletedFrames"].Value == "128", "rolling window bounded");
        Require(MapObjectTickProfiler.Counters["PlayerLoopMs"].Detail.Contains("p95="), "percentiles supplied");
        MapObjectTickProfiler.IsDetailedEnabled = false;
        Execute(PlayerLoop.GetCurrentPlayerLoop()); FramePhaseProfiler.AppendCounters();
        Require(MapObjectTickProfiler.Counters["CompletedFrames"].Value == "1", "baseline mode resets mixed window");
        Require(MapObjectTickProfiler.Counters["DetailedTimers"].Value == "False", "baseline still records phases");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) Execute(PlayerLoop.GetCurrentPlayerLoop());
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "phase callbacks allocate zero bytes");
        FramePhaseProfiler.SetEnabled(false);
        Require(Count(PlayerLoop.GetCurrentPlayerLoop()) == 4, "disable removes only owned boundaries");
        int beforeCalls = calls; Execute(PlayerLoop.GetCurrentPlayerLoop());
        Require(calls == beforeCalls + 2, "original loop survives disable");
        FramePhaseProfiler.SetEnabled(true); Execute(PlayerLoop.GetCurrentPlayerLoop());
        FramePhaseProfiler.AppendCounters();
        Require(MapObjectTickProfiler.Counters["CompletedFrames"].Value == "1", "re-enable resets old session");
        FramePhaseProfiler.SetEnabled(false);
        Console.WriteLine($"PASS {assertions} frame phase checks (production source, mocked PlayerLoop)");
    }
}

static class MapObjectTickProfiler
{
    internal static bool IsEnabled = true, IsDetailedEnabled = true;
    internal static readonly Dictionary<string, (string Value, string Detail)> Counters = new();
    public static void AddRuntimeCounter(string group, string name, int value) => AddRuntimeCounter(group, name, value.ToString(CultureInfo.InvariantCulture));
    public static void AddRuntimeCounter(string group, string name, bool value) => AddRuntimeCounter(group, name, value.ToString());
    public static void AddRuntimeCounter(string group, string name, string value, string detail = "") => Counters[name] = (value, detail);
}
namespace ProjectF.Diagnostics
{
    static class Stopwatch
    {
        internal const long Frequency = 1000;
        static long now;
        internal static long GetTimestamp() => ++now;
    }
}
namespace UnityEngine
{
    enum RuntimeInitializeLoadType { SubsystemRegistration }
    sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
}
namespace UnityEngine.LowLevel
{
    public struct PlayerLoopSystem
    {
        public delegate void UpdateFunction();
        public Type type;
        public UpdateFunction updateDelegate;
        public PlayerLoopSystem[] subSystemList;
    }
    public static class PlayerLoop
    {
        static PlayerLoopSystem current;
        public static PlayerLoopSystem GetCurrentPlayerLoop() => current;
        public static void SetPlayerLoop(PlayerLoopSystem loop) => current = loop;
    }
}
