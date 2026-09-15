using ProjectF.Simulation;

internal static class Phase34Checks
{
    internal static void Run(Action<bool, string> require)
    {
        Production(require);
        Calendar(require);
        Motion(require);
        Rail(require);
    }
    private static void Production(Action<bool, string> require)
    {
        const long unit = DeterministicSimulationUnits.UnitsPerWhole;
        var power = new PowerSupplySnapshot { HasPowerSource = true, ProductionWatts = 30, RequiredWatts = 60 };
        require(power.SupplyRatio == .5f && power.GetConsumerRatio(60, true) == .5f, "half-power demand publication");
        require(power.GetConsumerRatio(60, false) == .25f, "new demand is counted only once");
        var craft = ProductionProcess.Empty;
        craft.Begin(3, 12, 4, 60);
        long grant = power.GrantEnergy(unit, 60);
        require(grant == unit / 2, "integer energy grant retains partial power");
        for (int i = 0; i < 60; i++) craft.Advance(1, true, grant, 60 * unit, 60 * unit);
        require(craft.Active && !craft.WaitingForOutput && craft.RemainingTicks == 30, "half power preserves partial production progress");
        var beforeOutage = craft;
        for (int i = 0; i < 120; i++) craft.Advance(1, true, 0, 60 * unit, 60 * unit);
        require(craft.Equals(beforeOutage), "power loss consumes no progress or pending output");
        var restored = craft;
        for (int i = 0; i < 60; i++)
        {
            craft.Advance(1, true, grant, 60 * unit, 60 * unit);
            restored.Advance(1, true, grant, 60 * unit, 60 * unit);
            require(craft.Equals(restored), "production checkpoint resumes identically without any scene");
        }
        require(craft.WaitingForOutput && craft.RemainingTicks == 0 && craft.OutputCount == 4, "completed batch retains its output reservation");
        var blocked = craft;
        craft.Advance(9999, true, unit * 9999, unit * 60, unit * 60);
        require(craft.Equals(blocked), "blocked output cannot advance or consume energy again");
        craft.Clear();
        require(!craft.Active && craft.OutputItemId == -1 && craft.OutputCount == 0, "explicit successful output clears the batch once");
        craft.Begin(0, 10, 1, 6);
        for (int i = 0; i < 5; i++) require(!craft.Advance(1, false, 0, 0, 0), "time-only recipe waits for all its ticks");
        require(craft.Advance(1, false, 0, 0, 0), "time-only recipe completes at the exact tick");
        craft.Begin(0, -1, 1, 6); require(!craft.Active, "invalid output cannot start a craft");
        power.HasPowerSource = false;
        require(power.GrantEnergy(unit, 60) == 0 && power.SupplyRatio == 0, "disconnected power cannot grant energy");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) { craft.Begin(0, 10, 1, 1); craft.Advance(1, false, 0, 0, 0); craft.Clear(); }
        require(GC.GetAllocatedBytesForCurrentThread() == allocated, "production state loop allocates no managed memory");
    }
    private static void Calendar(Action<bool, string> require)
    {
        var clock = new WorldClock();
        using var scheduler = new SimulationTickWorld(); scheduler.Register(clock);
        require(clock.DayIndex == 1 && clock.Hour == 8 && clock.IsDay, "world clock starts without a lighting object");
        for (int i = 0; i < 60; i++) scheduler.Step();
        require(Math.Abs(clock.SecondsOfDay - (8 * 3600 + 60)) < .001 && Math.Abs(clock.PlantGrowthDaylightSeconds - 1) < .00001,
            "one simulation second advances one game minute and one growth second");
        clock.SetPaused(true); var paused = clock.Capture(); scheduler.Step();
        require(clock.Capture().Equals(paused), "calendar pause freezes daylight growth as well as time");
        clock.SetPaused(false); clock.Configure(1440, 1); clock.SetTime(1, 5, 59);
        var boundaries = new List<string>();
        clock.Sunrise += day => boundaries.Add("sunrise" + day);
        clock.Sunset += day => boundaries.Add("sunset" + day);
        clock.DayStarted += day => boundaries.Add("day" + day);
        clock.SeasonStarted += (year, season) => boundaries.Add($"season{year}:{season}");
        clock.AdvanceGameSeconds(18 * 3600 + 60, true);
        require(string.Join(",", boundaries) == "sunrise1,sunset1,day2,season1:1", "sunrise/sunset/day/season retain original event ordering");
        boundaries.Clear(); clock.AdvanceGameSeconds(1, true);
        require(boundaries.Count == 0, "boundary events fire once");
        clock.SetTimeScale(3.5f);
        var resumed = new WorldClock(); resumed.Restore(clock.Capture());
        for (int i = 0; i < 1000; i++) { clock.ManagedUpdateTick(1f / 60f); resumed.ManagedUpdateTick(1f / 60f); }
        require(clock.Capture().Equals(resumed.Capture()), "calendar checkpoint preserves controls, daylight growth and phase");
        int observed = 0; Action view = () => observed++;
        clock.Changed += view; clock.ManagedUpdateTick(.1f); clock.Changed -= view;
        double before = clock.SecondsOfDay; clock.ManagedUpdateTick(.1f);
        require(observed == 1 && clock.SecondsOfDay > before, "detaching the observer cannot stop the calendar");
        require(WorldClock.IsDayAtSeconds(6 * 3600) && !WorldClock.IsDayAtSeconds(18 * 3600), "daylight endpoint convention preserved");
        require(WorldClock.ResolveDaylightFactor(6 * 3600 + 900, 30) == .5f
            && WorldClock.ResolveDaylightFactor(18 * 3600 - 900, 30) == .5f, "light transition is a read-only calculation");
        clock.RestoreCalendar(3, -60);
        require(clock.DayIndex == 3 && clock.Hour == 23 && clock.Minute == 59 && clock.TimeScale == 1 && !clock.Paused,
            "legacy calendar save reset and negative time normalization preserved");
    }
    private static void Motion(Action<bool, string> require)
    {
        var motion = new VehicleMotionState();
        require(motion.Advance(1, .25f, 2, 4, 2) == 1, "data vehicle accelerates without Transform");
        require(motion.Advance(1, 1f, 2, 4, 2) == 2, "vehicle speed reaches and respects its cap");
        require(motion.Advance(-1, .25f, 2, 4, 2) == 1.5f, "reversing brakes before accelerating backward");
        require(motion.Advance(0, 1f, 2, 4, 2) == 0, "release input decelerates to rest");
        motion.Advance(-1, .25f, 2, 4, 2); var restored = motion;
        for (int i = 0; i < 120; i++)
        {
            float axis = i < 30 ? -1 : i < 70 ? 1 : 0;
            motion.Advance(axis, 1f / 60f, 2, 4, 2); restored.Advance(axis, 1f / 60f, 2, 4, 2);
            require(motion.Equals(restored), "motion checkpoint resumes identically");
        }
        motion.SignedSpeed = 10; motion.Advance(0, 0, 2, 4, 2, false);
        require(motion.SignedSpeed == 10, "unclamped mode preserves existing consist inertia");
        motion.Clamp(2); require(motion.SignedSpeed == 2, "explicit load-speed clamp applies without view state");
    }

    private readonly struct PathPoints : IRailPathPoints
    {
        private readonly RailPoint[] points;
        internal PathPoints(params RailPoint[] points) { this.points = points; }
        public int Count => points?.Length ?? 0;
        public RailPoint GetPoint(int index) => points[index];
    }
    private static void Rail(Action<bool, string> require)
    {
        var points = new PathPoints(new(0, 0), new(3, 0), new(3, 4));
        float[] distances = { 0, 3, 7 };
        require(RailPathSampling.TrySample(points, distances, 7, 5, out var p, out var t)
            && p.X == 3 && p.Y == 2 && t.X == 0 && t.Y == 1, "rail distance samples the correct segment without a rail object");
        require(RailPathSampling.TrySample(points, distances, 7, 3, out p, out t)
            && p.X == 3 && p.Y == 0 && t.X == 1 && t.Y == 0, "rail joint retains the incoming segment tangent");
        require(RailPathSampling.TrySample(points, distances, 7, -4, out p, out t) && p.X == 0 && p.Y == 0,
            "negative rail distance clamps at the path start");
        require(RailPathSampling.TrySample(points, distances, 7, 99, out p, out t) && p.X == 3 && p.Y == 4,
            "rail overflow clamps at the path end");
        var duplicate = new PathPoints(new(0, 0), new(0, 0), new(3, 0));
        require(RailPathSampling.TrySample(duplicate, new float[] { 0, 0, 3 }, 3, 0, out p, out t) && t.X == 1,
            "zero-length leading rail segment uses the next valid segment");
        duplicate = new PathPoints(new(0, 0), new(3, 0), new(3, 0));
        require(RailPathSampling.TrySample(duplicate, new float[] { 0, 3, 3 }, 4, 4, out p, out t)
            && p.X == 3 && t.X == 1, "zero-length trailing segment falls back without invalid tangent");
        require(!RailPathSampling.TrySample(default(PathPoints), distances, 7, 1, out _, out _)
            && !RailPathSampling.TrySample(points, new float[] { 0 }, 7, 1, out _, out _), "invalid rail arrays reject sampling");
        require(!RailPathSampling.TrySample(new PathPoints(new(1, 1), new(1, 1)), new float[] { 0, 0 }, 0, 0, out _, out _),
            "fully collapsed rail cannot produce motion");
        for (int i = 0; i < 100; i++) RailPathSampling.TrySample(points, distances, 7, i % 8, out _, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) RailPathSampling.TrySample(points, distances, 7, i % 8, out _, out _);
        require(GC.GetAllocatedBytesForCurrentThread() == allocated, "rail sampling does not allocate or box the point adapter");
    }
}
