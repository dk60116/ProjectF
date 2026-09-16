using ProjectF.Simulation;

static class Checks
{
    static int assertions;
    static void Require(bool condition, string label)
    { if (!condition) throw new Exception(label); assertions++; }
    static void Main()
    {
        using var world = new SimulationTickWorld();
        var log = new List<string>();
        var high = new Probe(20, log);
        var low = new Probe(10, log);
        world.Register(high); world.Register(low); world.Register(low);
        world.Step();
        Require(string.Join(",", log) == "P10,P20,A10,A20", "Plan all before stable-ID Apply; duplicate registration");
        Require(world.RegisteredCount == 2, "one owner per registered target");
        world.Paused = true; Require(!world.Step() && world.CurrentTick == 1, "pause preserves clock");
        world.Paused = false;
        world.Unregister(high); log.Clear(); world.Step();
        Require(string.Join(",", log) == "P10,A10", "unregistered target never executes");
        world.RestoreTick(1000); log.Clear(); world.Step();
        Require(low.LastDelta == SimulationTickWorld.FixedSimulationDeltaSeconds, "restore resets elapsed schedule");
        world.Unregister(low);
        var interval = new Probe(3, null) { Interval = 3f / 60f };
        world.Register(interval);
        for (int i = 0; i < 7; i++) world.Step();
        Require(interval.Calls == 3 && Math.Abs(interval.Elapsed - 7f / 60f) < .00001f, "interval tick preserves elapsed duration");
        var victim = new Probe(40, log);
        var remover = new Probe(30, log) { PlanAction = () => world.Unregister(victim) };
        world.Register(victim); world.Register(remover); log.Clear(); world.Step();
        Require(!log.Contains("P40") && !log.Contains("A40"), "plan-time removal cancels pending target");
        int executed = 0;
        world.Enqueue(new Command(w => { executed++; w.Enqueue(new Command(_ => executed++)); }));
        world.Step(); Require(executed == 1, "commands produced at boundary wait until next boundary");
        world.Step(); Require(executed == 2, "queued command executes once");
        bool rejected = false;
        world.Enqueue(new Command(w => { try { w.Step(); } catch (InvalidOperationException) { rejected = true; } }));
        world.Step(); Require(rejected, "reentrant Step rejected");
        using var idle = new SimulationTickWorld();
        for (int i = 0; i < 10; i++) idle.Step();
        Require(idle.CurrentTick == 10, "clock advances with no awake targets");
        foreach (int fps in new[] { 30, 60, 113, 144, 240 })
        {
            using var paced = new SimulationTickWorld(); var target = new Probe(1, null); paced.Register(target);
            double acc = 0;
            for (int f = 0; f < fps * 5; f++)
            {
                acc += 1d / fps;
                while (acc + 1e-9 >= 1d / 60d) { acc -= 1d / 60d; paced.Step(); }
            }
            Require(target.Calls == 300 && Math.Abs(target.Elapsed - 5) < .0001f, $"render pacing {fps} preserves fixed simulation");
        }
        using var hot = new SimulationTickWorld(); hot.Register(new Probe(1, null));
        for (int i = 0; i < 1000; i++) hot.Step();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) hot.Step();
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "steady-state Tick allocates zero bytes");
        var sceneRequests = new LatestSceneLoadRequest();
        ulong firstSceneRevision = sceneRequests.Enqueue(2);
        ulong replacementSceneRevision = sceneRequests.Enqueue("MainMenu");
        Require(replacementSceneRevision > firstSceneRevision,
            "scene replacement receives a newer generation");
        Require(sceneRequests.TryTake(out SceneLoadRequest replacementScene)
                && !replacementScene.UsesBuildIndex
                && replacementScene.SceneName == "MainMenu",
            "latest scene request replaces an unconsumed target");
        Require(!sceneRequests.HasPending && !sceneRequests.TryTake(out _),
            "taking a scene request clears the mailbox exactly once");
        sceneRequests.Enqueue(3);
        Require(sceneRequests.TryTake(out SceneLoadRequest inFlightScene)
                && inFlightScene.UsesBuildIndex
                && inFlightScene.BuildIndex == 3,
            "scene request preserves a build-index target");
        sceneRequests.Enqueue("Credits");
        Require(sceneRequests.HasPending
                && sceneRequests.TryTake(out SceneLoadRequest queuedAfterStart)
                && queuedAfterStart.SceneName == "Credits",
            "a replacement remains queued while the previous operation is in flight");
        var pipeCell = new GridCell(-8, 4);
        var peerCell = new GridCell(12, 4);
        var endpoints = new[] { new PipeEndpoint(pipeCell, 8), new PipeEndpoint(peerCell, 2) };
        var tunnel = new PipeConnections(endpoints, true);
        endpoints[0] = default;
        Require(tunnel.HasConnection(pipeCell, -1, 0) && !tunnel.HasConnection(pipeCell, 1, 0), "pipe owns baked direction independent of authored/view state");
        Require(tunnel.TryGetRemote(pipeCell, out var remote) && remote.Equals(peerCell), "data-only underground link crosses unloaded chunks");
        Require(!tunnel.TryGetRemote(new GridCell(0, 4), out _) && !tunnel.HasConnection(pipeCell, 1, 1), "tunnel interior and diagonal directions do not connect");
        using (var identities = new SimulationTickWorld())
        {
            var identityLog = new List<string>();
            var first = new Probe(1, identityLog); var second = new Probe(2, identityLog);
            identities.Register(first); identities.Register(second); identities.Step(); identityLog.Clear();
            second.SimulationId = -1; identities.Step();
            Require(string.Join(",", identityLog) == "P-1,P1,A-1,A1", "identity updates affect ordering without a Unity refresh hook");
            var observer = new Observer(); identities.Observer = observer;
            first.PlanAction = () => identities.Observer = null;
            identities.Step();
            Require(observer.Completed == 2, "profiling observer is stable for the current batch even when detached during Plan");
        }
        Phase34Checks.Run(Require);
        ActiveTickSetChecks.Run(Require);
        Console.WriteLine($"PASS {assertions} simulation core checks (production source, no Unity references)");
    }
    sealed class Command : ISimulationCommand
    {
        readonly Action<SimulationTickWorld> action;
        public Command(Action<SimulationTickWorld> action) => this.action = action;
        public void Execute(SimulationTickWorld world) => action(world);
    }
    sealed class Probe : IMapObjectUpdateTick, IMapObjectUpdateTickInterval, IMapObjectStagedUpdateTick, IMapObjectSimulationIdentity
    {
        readonly List<string> log;
        public long SimulationId { get; set; }
        public float Interval = 1f / 60f, LastDelta, Elapsed;
        public int Calls;
        public Action PlanAction;
        public float ManagedUpdateTickIntervalSeconds => Interval;
        public Probe(long id, List<string> log) { SimulationId = id; this.log = log; }
        public void ManagedUpdateTick(float dt) => throw new Exception("Staged target used direct tick");
        public void PlanManagedUpdateTick(float dt) { log?.Add($"P{SimulationId}"); LastDelta = dt; PlanAction?.Invoke(); }
        public void ApplyManagedUpdateTick() { log?.Add($"A{SimulationId}"); Calls++; Elapsed += LastDelta; }
    }
    sealed class Observer : ISimulationTickObserver
    {
        public int Completed;
        public void OnActiveTargets(ICollection<IMapObjectUpdateTick> targets) { }
        public long BeginSample() => 0;
        public void EndSample(IMapObjectUpdateTick target, long started) => Completed++;
    }
}
