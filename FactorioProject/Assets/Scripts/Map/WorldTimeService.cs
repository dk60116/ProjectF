using System;
using System.Globalization;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldTimeService : MonoBehaviour
{
    public const int HoursPerDay = 24;
    public const int MinutesPerHour = 60;
    public const int SecondsPerMinute = 60;
    public const int SunriseHour = 6;
    public const int SunsetHour = 18;
    public const int DefaultStartHour = 8;
    public const float DefaultRealSecondsPerDay = 24f * 60f;
    public const double GameSecondsPerHour = MinutesPerHour * SecondsPerMinute;
    public const double GameSecondsPerDay = HoursPerDay * GameSecondsPerHour;

    private const float MinimumTimeScale = 0.01f;
    private const float MaximumTimeScale = 1000f;
    private const float DefaultLightTransitionMinutes = 30f;
    private const float NightWaterBrightness = 0.06f;
    private static readonly int WorldWaterBrightnessId =
        Shader.PropertyToID("_WorldWaterBrightness");

    public static WorldTimeService Active { get; private set; }
    public static event Action<WorldTimeService> ActiveChanged;
    public static event Action<float, float, bool> GlobalTimeStateChanged;
    public static event Action<bool> GlobalDayStateChanged;

    public event Action<int> DayStarted;
    public event Action<int> Sunrise;
    public event Action<int> Sunset;
    public event Action<int, int> SeasonStarted;

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

    private double elapsedGameSeconds;
    private double elapsedPlantGrowthDaylightSeconds;
    private float worldTimeScale = 1f;
    private bool paused;
    private bool lightingDefaultsCaptured;
    private bool dayStateBroadcastInitialized;
    private bool lastBroadcastDayState;
    private float defaultAmbientIntensity = 1f;
    private Quaternion defaultLightRotation = Quaternion.identity;

    public int DayIndex => Mathf.Max(1, (int)Math.Floor(elapsedGameSeconds / GameSecondsPerDay) + 1);
    public double SecondsOfDay => NormalizeSecondsOfDay(elapsedGameSeconds);
    public int Hour => Mathf.Clamp((int)(SecondsOfDay / GameSecondsPerHour), 0, HoursPerDay - 1);
    public int Minute => Mathf.Clamp(
        (int)((SecondsOfDay % GameSecondsPerHour) / SecondsPerMinute),
        0,
        MinutesPerHour - 1);
    public float NormalizedDayTime => (float)(SecondsOfDay / GameSecondsPerDay);
    public bool IsDay => IsDayAtSeconds(SecondsOfDay);
    public double PlantGrowthDaylightSeconds => Math.Max(0d, elapsedPlantGrowthDaylightSeconds);
    public float DaylightFactor => ResolveDaylightFactor(SecondsOfDay, lightTransitionMinutes);
    public bool Paused => paused;
    public float TimeScale => worldTimeScale;
    public float RealSecondsPerDay => Mathf.Max(1f, realSecondsPerDay);
    public int DaysPerSeason => Mathf.Max(1, daysPerSeason);
    public int SeasonIndex => ((DayIndex - 1) / DaysPerSeason) % 4;
    public int DayOfSeason => ((DayIndex - 1) % DaysPerSeason) + 1;
    public int YearIndex => ((DayIndex - 1) / (DaysPerSeason * 4)) + 1;
    public float LatitudeDegrees => Mathf.Clamp(latitudeDegrees, -90f, 90f);
    public string ClockText => string.Format(
        CultureInfo.InvariantCulture,
        "Day {0} {1:00}:{2:00}",
        DayIndex,
        Hour,
        Minute);

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
        if (Active != null && Active != this)
        {
            Destroy(this);
            return;
        }

        Active = this;
        NormalizeSettings();
        if (elapsedGameSeconds <= 0d)
        {
            elapsedGameSeconds = DefaultStartHour * GameSecondsPerHour;
        }

        CaptureLightingDefaults();
        ApplyEnvironment();
        ActiveChanged?.Invoke(this);
    }

    private void OnEnable()
    {
        if (Active == null)
        {
            Active = this;
            ActiveChanged?.Invoke(this);
        }

        NormalizeSettings();
        CaptureLightingDefaults();
        ApplyEnvironment();
    }

    private void OnDisable()
    {
        if (Active != this)
        {
            return;
        }

        Active = null;
        Shader.SetGlobalFloat(WorldWaterBrightnessId, 1f);
        ActiveChanged?.Invoke(null);
    }

    private void Update()
    {
        if (!paused && worldTimeScale > 0f && Time.unscaledDeltaTime > 0f)
        {
            if (IsDay && Time.deltaTime > 0f)
            {
                elapsedPlantGrowthDaylightSeconds += Time.deltaTime;
            }

            double gameSecondsPerRealSecond = GameSecondsPerDay / RealSecondsPerDay;
            AdvanceGameSeconds(
                Time.unscaledDeltaTime * gameSecondsPerRealSecond * worldTimeScale,
                true);
            return;
        }

        ApplyEnvironment();
    }

    public void ResetToDefault()
    {
        int previousSeason = SeasonIndex;
        elapsedGameSeconds = DefaultStartHour * GameSecondsPerHour;
        elapsedPlantGrowthDaylightSeconds = 0d;
        worldTimeScale = 1f;
        paused = false;
        ApplyEnvironment();
        if (SeasonIndex != previousSeason)
        {
            SeasonStarted?.Invoke(YearIndex, SeasonIndex);
        }
    }

    public void SetPaused(bool value)
    {
        paused = value;
        ApplyEnvironment();
    }

    public void SetTimeScale(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            value = 1f;
        }

        worldTimeScale = Mathf.Clamp(value, MinimumTimeScale, MaximumTimeScale);
    }

    public bool TrySetTimeOfDay(int hour, int minute)
    {
        if (hour < 0 || hour >= HoursPerDay || minute < 0 || minute >= MinutesPerHour)
        {
            return false;
        }

        SetTime(DayIndex, hour, minute);
        return true;
    }

    public void SetTime(int dayIndex, int hour, int minute)
    {
        int normalizedDay = Mathf.Max(1, dayIndex);
        int normalizedHour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
        int normalizedMinute = Mathf.Clamp(minute, 0, MinutesPerHour - 1);
        elapsedGameSeconds =
            ((normalizedDay - 1d) * GameSecondsPerDay)
            + (normalizedHour * GameSecondsPerHour)
            + (normalizedMinute * SecondsPerMinute);
        ApplyEnvironment();
    }

    public void RefreshEnvironmentBindings()
    {
        directionalLight = null;
        lightingDefaultsCaptured = false;
        CaptureLightingDefaults();
        ApplyEnvironment();
    }

    public void AdvanceToNextSunrise()
    {
        double sunriseSeconds = SunriseHour * GameSecondsPerHour;
        double currentSeconds = SecondsOfDay;
        double delta = currentSeconds < sunriseSeconds
            ? sunriseSeconds - currentSeconds
            : (GameSecondsPerDay - currentSeconds) + sunriseSeconds;
        AdvanceGameSeconds(delta, true);
    }

    public void AdvanceGameSeconds(double gameSeconds, bool raiseBoundaryEvents)
    {
        if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds) || gameSeconds <= 0d)
        {
            ApplyEnvironment();
            return;
        }

        double previousElapsed = elapsedGameSeconds;
        int previousSeason = SeasonIndex;
        elapsedGameSeconds = Math.Max(0d, elapsedGameSeconds + gameSeconds);

        if (raiseBoundaryEvents)
        {
            RaiseBoundaryEvents(previousElapsed, elapsedGameSeconds);
            if (SeasonIndex != previousSeason)
            {
                SeasonStarted?.Invoke(YearIndex, SeasonIndex);
            }
        }

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
        if (state == null || !state.hasTime)
        {
            ResetToDefault();
            return;
        }

        int normalizedDay = Mathf.Max(1, state.dayIndex);
        double normalizedSeconds = NormalizeSecondsOfDay(state.secondsOfDay);
        elapsedGameSeconds = ((normalizedDay - 1d) * GameSecondsPerDay) + normalizedSeconds;
        elapsedPlantGrowthDaylightSeconds = 0d;
        worldTimeScale = 1f;
        paused = false;
        ApplyEnvironment();
    }

    public static bool IsDayAtSeconds(double secondsOfDay)
    {
        double normalized = NormalizeSecondsOfDay(secondsOfDay);
        return normalized >= SunriseHour * GameSecondsPerHour
               && normalized < SunsetHour * GameSecondsPerHour;
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

        if (worldTimeScale < MinimumTimeScale || worldTimeScale > MaximumTimeScale)
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

    public static float ResolveDaylightFactor(double secondsOfDay, float transitionMinutes)
    {
        double normalized = NormalizeSecondsOfDay(secondsOfDay);
        double sunrise = SunriseHour * GameSecondsPerHour;
        double sunset = SunsetHour * GameSecondsPerHour;
        double transitionSeconds = Math.Max(0d, transitionMinutes * SecondsPerMinute);
        if (transitionSeconds <= 0.001d)
        {
            return IsDayAtSeconds(normalized) ? 1f : 0f;
        }

        if (normalized < sunrise || normalized >= sunset)
        {
            return 0f;
        }

        double sunriseEnd = Math.Min(sunset, sunrise + transitionSeconds);
        if (normalized < sunriseEnd)
        {
            return Mathf.SmoothStep(0f, 1f, (float)((normalized - sunrise) / transitionSeconds));
        }

        double sunsetStart = Math.Max(sunrise, sunset - transitionSeconds);
        if (normalized >= sunsetStart)
        {
            return Mathf.SmoothStep(1f, 0f, (float)((normalized - sunsetStart) / transitionSeconds));
        }

        return 1f;
    }

    private void RaiseBoundaryEvents(double previousElapsed, double currentElapsed)
    {
        if (currentElapsed <= previousElapsed)
        {
            return;
        }

        long firstDayOffset = Math.Max(0L, (long)Math.Floor(previousElapsed / GameSecondsPerDay));
        long lastDayOffset = Math.Max(firstDayOffset, (long)Math.Floor(currentElapsed / GameSecondsPerDay));
        for (long dayOffset = firstDayOffset; dayOffset <= lastDayOffset; dayOffset++)
        {
            double dayStart = dayOffset * GameSecondsPerDay;
            int eventDayIndex = dayOffset >= int.MaxValue
                ? int.MaxValue
                : (int)dayOffset + 1;
            double sunriseBoundary = dayStart + (SunriseHour * GameSecondsPerHour);
            if (WasBoundaryCrossed(previousElapsed, currentElapsed, sunriseBoundary))
            {
                Sunrise?.Invoke(eventDayIndex);
            }

            double sunsetBoundary = dayStart + (SunsetHour * GameSecondsPerHour);
            if (WasBoundaryCrossed(previousElapsed, currentElapsed, sunsetBoundary))
            {
                Sunset?.Invoke(eventDayIndex);
            }

            double nextDayStart = dayStart + GameSecondsPerDay;
            if (WasBoundaryCrossed(previousElapsed, currentElapsed, nextDayStart))
            {
                int nextDayIndex = eventDayIndex >= int.MaxValue
                    ? int.MaxValue
                    : eventDayIndex + 1;
                DayStarted?.Invoke(nextDayIndex);
            }
        }
    }

    private static bool WasBoundaryCrossed(
        double previousElapsed,
        double currentElapsed,
        double boundary)
    {
        return boundary > previousElapsed && boundary <= currentElapsed;
    }

    private void ApplyEnvironment()
    {
        float daylightFactor = DaylightFactor;
        bool isDay = IsDay;
        Shader.SetGlobalFloat(
            WorldWaterBrightnessId,
            Mathf.Lerp(NightWaterBrightness, 1f, daylightFactor));
        ApplyLighting(daylightFactor);
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
        worldTimeScale = Mathf.Clamp(worldTimeScale, MinimumTimeScale, MaximumTimeScale);
    }

    private static double NormalizeSecondsOfDay(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultStartHour * GameSecondsPerHour;
        }

        double normalized = value % GameSecondsPerDay;
        return normalized < 0d ? normalized + GameSecondsPerDay : normalized;
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
