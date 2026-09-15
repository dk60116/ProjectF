using System;
using System.Globalization;
using UnityEngine;
using ProjectF.Simulation;

[DisallowMultipleComponent]
public sealed class WorldTimeService : MonoBehaviour
{
    public const int HoursPerDay = WorldClock.HoursPerDay;
    public const int MinutesPerHour = WorldClock.MinutesPerHour;
    public const int SecondsPerMinute = WorldClock.SecondsPerMinute;
    public const int SunriseHour = WorldClock.SunriseHour;
    public const int SunsetHour = WorldClock.SunsetHour;
    public const int DefaultStartHour = WorldClock.DefaultStartHour;
    public const float DefaultRealSecondsPerDay = WorldClock.DefaultRealSecondsPerDay;
    public const double GameSecondsPerHour = WorldClock.GameSecondsPerHour;
    public const double GameSecondsPerDay = WorldClock.GameSecondsPerDay;
    private const float DefaultLightTransitionMinutes = 30f;
    private const float NightWaterBrightness = 0.06f;
    private static readonly int WorldWaterBrightnessId = Shader.PropertyToID("_WorldWaterBrightness");
    private readonly WorldClock clock = new WorldClock();

    public static WorldTimeService Active { get; private set; }
    public static event Action<WorldTimeService> ActiveChanged;
    public static event Action<float, float, bool> GlobalTimeStateChanged;
    public static event Action<bool> GlobalDayStateChanged;
    public event Action<int> DayStarted { add => clock.DayStarted += value; remove => clock.DayStarted -= value; }
    public event Action<int> Sunrise { add => clock.Sunrise += value; remove => clock.Sunrise -= value; }
    public event Action<int> Sunset { add => clock.Sunset += value; remove => clock.Sunset -= value; }
    public event Action<int, int> SeasonStarted { add => clock.SeasonStarted += value; remove => clock.SeasonStarted -= value; }

    [Header("Clock")]
    [SerializeField, Min(1f)]
    private float realSecondsPerDay = DefaultRealSecondsPerDay;
    [SerializeField, Min(1)]
    private int daysPerSeason = 30;
    [SerializeField, Range(-90f, 90f)]
    private float latitudeDegrees;

    [Header("Simple Lighting")]
    [SerializeField]
    private Light directionalLight;
    [SerializeField, Min(0f)]
    private float dayLightIntensity = 1f;
    [SerializeField, Min(0f)]
    private float nightLightIntensity = 0.3f;
    [SerializeField]
    private Color dayLightColor = Color.white;
    [SerializeField]
    private Color nightLightColor = new Color(0.55f, 0.62f, 0.8f, 1f);
    [SerializeField, Range(0f, 1f)]
    private float nightAmbientMultiplier = 0.5f;
    [SerializeField, Min(0f)]
    private float lightTransitionMinutes = DefaultLightTransitionMinutes;

    private bool lightingDefaultsCaptured, dayStateBroadcastInitialized, lastBroadcastDayState;
    private float defaultAmbientIntensity = 1f;
    private Quaternion defaultLightRotation = Quaternion.identity;
    public WorldClock Simulation => clock;
    public int DayIndex => clock.DayIndex;
    public double SecondsOfDay => clock.SecondsOfDay;
    public int Hour => clock.Hour;
    public int Minute => clock.Minute;
    public float NormalizedDayTime => clock.NormalizedDayTime;
    public bool IsDay => clock.IsDay;
    public double PlantGrowthDaylightSeconds => clock.PlantGrowthDaylightSeconds;
    public float DaylightFactor => WorldClock.ResolveDaylightFactor(SecondsOfDay, lightTransitionMinutes);
    public bool Paused => clock.Paused;
    public float TimeScale => clock.TimeScale;
    public float RealSecondsPerDay => clock.RealSecondsPerDay;
    public int DaysPerSeason => clock.DaysPerSeason;
    public int SeasonIndex => clock.SeasonIndex;
    public int DayOfSeason => clock.DayOfSeason;
    public int YearIndex => clock.YearIndex;
    public float LatitudeDegrees => Mathf.Clamp(latitudeDegrees, -90f, 90f);
    public long SimulationId => clock.SimulationId;
    public float ManagedUpdateTickIntervalSeconds => clock.ManagedUpdateTickIntervalSeconds;
    public string ClockText => string.Format(CultureInfo.InvariantCulture, "Day {0} {1:00}:{2:00}", DayIndex, Hour, Minute);

    public static WorldTimeService EnsureFor(GameObject owner)
    {
        if (Active != null)
        {
            return Active;
        }

        if (owner == null)
        {
            return null;
        }

        WorldTimeService service = owner.GetComponent<WorldTimeService>();
        return service != null ? service : owner.AddComponent<WorldTimeService>();
    }

    private void Awake()
    {
        if (Active != null && Active != this) { Destroy(this); return; }
        Active = this;
        NormalizeSettings();
        clock.Changed += ApplyEnvironment;
        CaptureLightingDefaults();
        ApplyEnvironment();
        ActiveChanged?.Invoke(this);
    }
    private void OnEnable()
    {
        if (Active != this) return;
        NormalizeSettings();
        CaptureLightingDefaults();
        ApplyEnvironment();
        // Register the data clock, never the lighting component as a Tick target.
        MapObjectTickManager.RegisterUpdateTick(clock);
    }
    private void OnDisable()
    {
        // Presentation disable is not a calendar pause. Explicit SetPaused controls that.
        if (Active == this) Shader.SetGlobalFloat(WorldWaterBrightnessId, 1f);
    }
    private void OnDestroy()
    {
        clock.Changed -= ApplyEnvironment;
        MapObjectTickManager.UnregisterUpdateTick(clock);
        if (Active != this) return;
        Active = null;
        Shader.SetGlobalFloat(WorldWaterBrightnessId, 1f);
        ActiveChanged?.Invoke(null);
    }

    public void ResetToDefault() => clock.ResetToDefault();
    public void SetPaused(bool value) => clock.SetPaused(value);
    public void SetTimeScale(float value) => clock.SetTimeScale(value);
    public bool TrySetTimeOfDay(int hour, int minute) => clock.TrySetTimeOfDay(hour, minute);
    public void SetTime(int dayIndex, int hour, int minute) => clock.SetTime(dayIndex, hour, minute);
    public void AdvanceToNextSunrise() => clock.AdvanceToNextSunrise();
    public void AdvanceGameSeconds(double seconds, bool raiseBoundaryEvents) => clock.AdvanceGameSeconds(seconds, raiseBoundaryEvents);
    public static bool IsDayAtSeconds(double seconds) => WorldClock.IsDayAtSeconds(seconds);
    public static float ResolveDaylightFactor(double seconds, float minutes) => WorldClock.ResolveDaylightFactor(seconds, minutes);
    private static double NormalizeSecondsOfDay(double value) => WorldClock.NormalizeSecondsOfDay(value);
    public void RefreshEnvironmentBindings()
    {
        directionalLight = null;
        lightingDefaultsCaptured = false;
        CaptureLightingDefaults();
        ApplyEnvironment();
    }
    public WorldTimeSaveData CaptureSaveState()
    {
        return new WorldTimeSaveData
        {
            dayIndex = DayIndex,
            secondsOfDay = SecondsOfDay
        };
    }
    public void ApplySaveState(WorldTimeSaveData state)
    {
        if (state == null || !state.hasTime) { clock.ResetToDefault(); return; }
        clock.RestoreCalendar(state.dayIndex, state.secondsOfDay);
    }
    public bool TryValidateState(out string firstIssue)
    {
        if (DayIndex < 1)
        {
            firstIssue = "invalid_day_index";
            return false;
        }

        if (SecondsOfDay < 0d || SecondsOfDay >= GameSecondsPerDay)
        {
            firstIssue = "invalid_seconds_of_day";
            return false;
        }

        if (Hour < 0 || Hour >= HoursPerDay || Minute < 0 || Minute >= MinutesPerHour)
        {
            firstIssue = "invalid_clock";
            return false;
        }

        if (clock.TimeScale < WorldClock.MinimumTimeScale || clock.TimeScale > WorldClock.MaximumTimeScale)
        {
            firstIssue = "invalid_time_scale";
            return false;
        }

        firstIssue = string.Empty;
        return true;
    }
    public static bool RunCalculationSelfCheck(out string firstIssue)
    {
        if (!IsDayAtSeconds(SunriseHour * GameSecondsPerHour))
        {
            firstIssue = "sunrise_not_day";
            return false;
        }

        if (IsDayAtSeconds(SunsetHour * GameSecondsPerHour))
        {
            firstIssue = "sunset_not_night";
            return false;
        }

        double tenDaysAfterStart =
            (DefaultStartHour * GameSecondsPerHour) + (10d * GameSecondsPerDay);
        int resultingDay = (int)Math.Floor(tenDaysAfterStart / GameSecondsPerDay) + 1;
        if (resultingDay != 11
            || Math.Abs(NormalizeSecondsOfDay(tenDaysAfterStart)
                        - (DefaultStartHour * GameSecondsPerHour)) > 0.001d)
        {
            firstIssue = "ten_day_progression_mismatch";
            return false;
        }

        firstIssue = string.Empty;
        return true;
    }
    private void ApplyEnvironment()
    {
        float daylightFactor = DaylightFactor;
        bool isDay = IsDay;
        if (isActiveAndEnabled)
        {
            Shader.SetGlobalFloat(WorldWaterBrightnessId, Mathf.Lerp(NightWaterBrightness, 1f, daylightFactor));
            ApplyLighting(daylightFactor);
        }
        GlobalTimeStateChanged?.Invoke(NormalizedDayTime, daylightFactor, isDay);
        if (!dayStateBroadcastInitialized || lastBroadcastDayState != isDay)
        {
            dayStateBroadcastInitialized = true;
            lastBroadcastDayState = isDay;
            GlobalDayStateChanged?.Invoke(isDay);
        }
    }
    private void ApplyLighting(float daylightFactor)
    {
        CaptureLightingDefaults();
        if (directionalLight != null)
        {
            directionalLight.intensity = Mathf.Lerp(
                Mathf.Max(0f, nightLightIntensity),
                Mathf.Max(0f, dayLightIntensity),
                daylightFactor);
            directionalLight.color = Color.Lerp(nightLightColor, dayLightColor, daylightFactor);

            float dayProgress = Mathf.InverseLerp(
                SunriseHour,
                SunsetHour,
                (float)(SecondsOfDay / GameSecondsPerHour));
            float elevation = 25f + (Mathf.Sin(dayProgress * Mathf.PI) * 45f * daylightFactor);
            Vector3 defaultEuler = defaultLightRotation.eulerAngles;
            directionalLight.transform.rotation = Quaternion.Euler(
                elevation,
                defaultEuler.y,
                defaultEuler.z);
        }

        RenderSettings.ambientIntensity =
            defaultAmbientIntensity * Mathf.Lerp(nightAmbientMultiplier, 1f, daylightFactor);
    }
    private void CaptureLightingDefaults()
    {
        if (lightingDefaultsCaptured && directionalLight != null)
        {
            return;
        }

        if (directionalLight == null)
        {
            directionalLight = RenderSettings.sun;
        }

        if (directionalLight == null)
        {
            Light[] lights = FindObjectsByType<Light>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional)
                {
                    directionalLight = lights[i];
                    break;
                }
            }
        }

        defaultAmbientIntensity = RenderSettings.ambientIntensity;
        defaultLightRotation = directionalLight != null
            ? directionalLight.transform.rotation
            : Quaternion.identity;
        lightingDefaultsCaptured = true;
    }
    private void NormalizeSettings()
    {
        realSecondsPerDay = Mathf.Max(1f, realSecondsPerDay);
        daysPerSeason = Mathf.Max(1, daysPerSeason);
        latitudeDegrees = Mathf.Clamp(latitudeDegrees, -90f, 90f);
        dayLightIntensity = Mathf.Max(0f, dayLightIntensity);
        nightLightIntensity = Mathf.Max(0f, nightLightIntensity);
        nightAmbientMultiplier = Mathf.Clamp01(nightAmbientMultiplier);
        lightTransitionMinutes = Mathf.Max(0f, lightTransitionMinutes);
        clock.Configure(realSecondsPerDay, daysPerSeason);
    }
#if UNITY_EDITOR
    private void OnValidate()
    {
        NormalizeSettings();
        if (Application.isPlaying)
        {
            lightingDefaultsCaptured = false;
            CaptureLightingDefaults();
            ApplyEnvironment();
        }
    }
#endif
}
