using System;

namespace ProjectF.Simulation
{
    public readonly struct WorldClockSnapshot
    {
        public readonly double ElapsedSeconds, PlantDaylightSeconds;
        public readonly float TimeScale, RealSecondsPerDay;
        public readonly bool Paused;
        public readonly int DaysPerSeason;
        public WorldClockSnapshot(double elapsed, double daylight, float scale, float dayDuration, bool paused, int seasonDays)
        { ElapsedSeconds = elapsed; PlantDaylightSeconds = daylight; TimeScale = scale; RealSecondsPerDay = dayDuration; Paused = paused; DaysPerSeason = seasonDays; }
    }

    /// <summary>Authoritative calendar. Lighting and scene bindings only observe Changed.</summary>
    public sealed class WorldClock : IMapObjectUpdateTick, IMapObjectUpdateTickInterval, IMapObjectSimulationIdentity
    {
        public const int HoursPerDay = 24, MinutesPerHour = 60, SecondsPerMinute = 60;
        public const int SunriseHour = 6, SunsetHour = 18, DefaultStartHour = 8;
        public const float DefaultRealSecondsPerDay = 24f * 60f;
        public const double GameSecondsPerHour = MinutesPerHour * SecondsPerMinute;
        public const double GameSecondsPerDay = HoursPerDay * GameSecondsPerHour;
        public const float MinimumTimeScale = 0.01f, MaximumTimeScale = 1000f;
        private double elapsedGameSeconds = DefaultStartHour * GameSecondsPerHour;
        private double elapsedPlantGrowthDaylightSeconds;
        private float worldTimeScale = 1f;
        private bool paused;
        public float RealSecondsPerDay { get; private set; } = DefaultRealSecondsPerDay;
        public int DaysPerSeason { get; private set; } = 30;
        public long SimulationId => long.MinValue + 1L;
        public float ManagedUpdateTickIntervalSeconds => SimulationTickWorld.FixedSimulationDeltaSeconds;
        public event Action Changed;
        public event Action<int> DayStarted, Sunrise, Sunset;
        public event Action<int, int> SeasonStarted;
        public int DayIndex => Math.Max(1, (int)Math.Floor(elapsedGameSeconds / GameSecondsPerDay) + 1);
        public double SecondsOfDay => NormalizeSecondsOfDay(elapsedGameSeconds);
        public int Hour => Math.Max(0, Math.Min(HoursPerDay - 1, (int)(SecondsOfDay / GameSecondsPerHour)));
        public int Minute => Math.Max(0, Math.Min(MinutesPerHour - 1, (int)((SecondsOfDay % GameSecondsPerHour) / SecondsPerMinute)));
        public float NormalizedDayTime => (float)(SecondsOfDay / GameSecondsPerDay);
        public bool IsDay => IsDayAtSeconds(SecondsOfDay);
        public double PlantGrowthDaylightSeconds => Math.Max(0, elapsedPlantGrowthDaylightSeconds);
        public bool Paused => paused;
        public float TimeScale => worldTimeScale;
        public int SeasonIndex => ((DayIndex - 1) / DaysPerSeason) % 4;
        public int DayOfSeason => ((DayIndex - 1) % DaysPerSeason) + 1;
        public int YearIndex => ((DayIndex - 1) / (DaysPerSeason * 4)) + 1;

        public void Configure(float realSecondsPerDay, int daysPerSeason)
        {
            RealSecondsPerDay = float.IsNaN(realSecondsPerDay) || float.IsInfinity(realSecondsPerDay)
                ? DefaultRealSecondsPerDay : Math.Max(1f, realSecondsPerDay);
            DaysPerSeason = Math.Max(1, Math.Min(int.MaxValue / 4, daysPerSeason));
        }

        public void ManagedUpdateTick(float deltaTime)
        {
            if (!paused && worldTimeScale > 0f && deltaTime > 0f && !float.IsInfinity(deltaTime))
            {
                // Keep the existing start-of-tick daylight accounting rule for plant saves.
                if (IsDay) elapsedPlantGrowthDaylightSeconds += deltaTime;
                AdvanceGameSeconds(deltaTime * (GameSecondsPerDay / RealSecondsPerDay) * worldTimeScale, true);
            }
            else Changed?.Invoke();
        }

        public void ResetToDefault()
        {
            int previousSeason = SeasonIndex;
            elapsedGameSeconds = DefaultStartHour * GameSecondsPerHour;
            elapsedPlantGrowthDaylightSeconds = 0; worldTimeScale = 1; paused = false;
            Changed?.Invoke();
            if (SeasonIndex != previousSeason) SeasonStarted?.Invoke(YearIndex, SeasonIndex);
        }
        public void SetPaused(bool value) { paused = value; Changed?.Invoke(); }
        public void SetTimeScale(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = 1;
            worldTimeScale = Math.Max(MinimumTimeScale, Math.Min(MaximumTimeScale, value));
        }
        public bool TrySetTimeOfDay(int hour, int minute)
        {
            if (hour < 0 || hour >= HoursPerDay || minute < 0 || minute >= MinutesPerHour) return false;
            SetTime(DayIndex, hour, minute); return true;
        }
        public void SetTime(int day, int hour, int minute)
        {
            elapsedGameSeconds = (Math.Max(1, day) - 1d) * GameSecondsPerDay
                + Math.Max(0, Math.Min(HoursPerDay - 1, hour)) * GameSecondsPerHour
                + Math.Max(0, Math.Min(MinutesPerHour - 1, minute)) * SecondsPerMinute;
            Changed?.Invoke();
        }
        public void AdvanceToNextSunrise()
        {
            double sunrise = SunriseHour * GameSecondsPerHour, now = SecondsOfDay;
            AdvanceGameSeconds(now < sunrise ? sunrise - now : GameSecondsPerDay - now + sunrise, true);
        }
        public void AdvanceGameSeconds(double seconds, bool raiseBoundaryEvents)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) { Changed?.Invoke(); return; }
            double previous = elapsedGameSeconds;
            int previousSeason = SeasonIndex;
            elapsedGameSeconds = Math.Max(0, elapsedGameSeconds + seconds);
            if (raiseBoundaryEvents)
            {
                RaiseBoundaryEvents(previous, elapsedGameSeconds);
                if (SeasonIndex != previousSeason) SeasonStarted?.Invoke(YearIndex, SeasonIndex);
            }
            Changed?.Invoke();
        }
        public WorldClockSnapshot Capture() => new WorldClockSnapshot(elapsedGameSeconds,
            elapsedPlantGrowthDaylightSeconds, worldTimeScale, RealSecondsPerDay, paused, DaysPerSeason);
        public void Restore(WorldClockSnapshot snapshot)
        {
            Configure(snapshot.RealSecondsPerDay, snapshot.DaysPerSeason);
            elapsedGameSeconds = double.IsNaN(snapshot.ElapsedSeconds) || double.IsInfinity(snapshot.ElapsedSeconds)
                ? DefaultStartHour * GameSecondsPerHour : Math.Max(0, snapshot.ElapsedSeconds);
            elapsedPlantGrowthDaylightSeconds = double.IsNaN(snapshot.PlantDaylightSeconds) || double.IsInfinity(snapshot.PlantDaylightSeconds)
                ? 0 : Math.Max(0, snapshot.PlantDaylightSeconds);
            SetTimeScale(snapshot.TimeScale); paused = snapshot.Paused; Changed?.Invoke();
        }
        // Current disk DTO intentionally stores calendar time only, preserving its old reset semantics.
        public void RestoreCalendar(int day, double secondsOfDay)
        {
            elapsedGameSeconds = (Math.Max(1, day) - 1d) * GameSecondsPerDay + NormalizeSecondsOfDay(secondsOfDay);
            elapsedPlantGrowthDaylightSeconds = 0; worldTimeScale = 1; paused = false; Changed?.Invoke();
        }
        public static double NormalizeSecondsOfDay(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return DefaultStartHour * GameSecondsPerHour;
            double normalized = value % GameSecondsPerDay;
            return normalized < 0 ? normalized + GameSecondsPerDay : normalized;
        }
        public static bool IsDayAtSeconds(double seconds)
        {
            double normalized = NormalizeSecondsOfDay(seconds);
            return normalized >= SunriseHour * GameSecondsPerHour && normalized < SunsetHour * GameSecondsPerHour;
        }
        public static float ResolveDaylightFactor(double seconds, float transitionMinutes)
        {
            double normalized = NormalizeSecondsOfDay(seconds);
            double sunrise = SunriseHour * GameSecondsPerHour, sunset = SunsetHour * GameSecondsPerHour;
            double transition = Math.Max(0, transitionMinutes * SecondsPerMinute);
            if (transition <= 0.001) return IsDayAtSeconds(normalized) ? 1f : 0f;
            if (normalized < sunrise || normalized >= sunset) return 0;
            if (normalized < Math.Min(sunset, sunrise + transition))
                return Smooth((float)((normalized - sunrise) / transition));
            if (normalized >= Math.Max(sunrise, sunset - transition))
                return 1f - Smooth((float)((normalized - Math.Max(sunrise, sunset - transition)) / transition));
            return 1;
        }
        private static float Smooth(float value)
        {
            float t = Math.Max(0, Math.Min(1, value));
            return -2f * t * t * t + 3f * t * t;
        }
        private void RaiseBoundaryEvents(double previous, double current)
        {
            long first = Math.Max(0, (long)Math.Floor(previous / GameSecondsPerDay));
            long last = Math.Max(first, (long)Math.Floor(current / GameSecondsPerDay));
            for (long day = first; day <= last; day++)
            {
                double start = day * GameSecondsPerDay;
                int eventDay = day >= int.MaxValue ? int.MaxValue : (int)day + 1;
                if (Crossed(previous, current, start + SunriseHour * GameSecondsPerHour)) Sunrise?.Invoke(eventDay);
                if (Crossed(previous, current, start + SunsetHour * GameSecondsPerHour)) Sunset?.Invoke(eventDay);
                if (Crossed(previous, current, start + GameSecondsPerDay)) DayStarted?.Invoke(eventDay == int.MaxValue ? eventDay : eventDay + 1);
            }
        }
        private static bool Crossed(double previous, double current, double boundary) => boundary > previous && boundary <= current;
    }
}
