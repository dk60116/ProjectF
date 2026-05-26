using System;
using UnityEngine;

public class MapClimate : MonoBehaviour
{
    public const float DefaultCurrentTemperatureCelsius = 20f;
    public const float DefaultWaterTemperatureCelsius = 15f;
    private const float WaterTemperatureAirResponse = 0.5f;

    public static MapClimate Active { get; private set; }

    [SerializeField]
    private float currentTemperatureCelsius = DefaultCurrentTemperatureCelsius;

    public event Action<float> CurrentTemperatureChanged;
    public static event Action<float> ActiveCurrentTemperatureChanged;

    public static float CurrentTemperatureCelsius
    {
        get
        {
            return Active != null
                ? Active.InstanceCurrentTemperatureCelsius
                : DefaultCurrentTemperatureCelsius;
        }
    }

    public static float CurrentWaterTemperatureCelsius
    {
        get
        {
            return Active != null
                ? Active.InstanceWaterTemperatureCelsius
                : DefaultWaterTemperatureCelsius;
        }
    }

    public float InstanceCurrentTemperatureCelsius => currentTemperatureCelsius;
    public float InstanceWaterTemperatureCelsius => ResolveWaterTemperatureCelsius(currentTemperatureCelsius);
    public int RoundedCurrentTemperatureCelsius => Mathf.RoundToInt(currentTemperatureCelsius);
    public int RoundedWaterTemperatureCelsius => Mathf.RoundToInt(InstanceWaterTemperatureCelsius);

    private void Awake()
    {
        RegisterActiveIfNeeded();
        NormalizeCurrentTemperature();
    }

    private void OnEnable()
    {
        RegisterActiveIfNeeded();
        NormalizeCurrentTemperature();
    }

    private void OnDisable()
    {
        if (Active == this)
        {
            Active = null;
        }
    }

    public void SetCurrentTemperatureCelsius(float temperatureCelsius)
    {
        temperatureCelsius = NormalizeTemperatureValue(temperatureCelsius);
        if (Mathf.Approximately(currentTemperatureCelsius, temperatureCelsius))
        {
            return;
        }

        currentTemperatureCelsius = temperatureCelsius;
        CurrentTemperatureChanged?.Invoke(currentTemperatureCelsius);
        if (Active == this)
        {
            ActiveCurrentTemperatureChanged?.Invoke(currentTemperatureCelsius);
        }
    }

    private void RegisterActiveIfNeeded()
    {
        if (Active != null && Active != this)
        {
            return;
        }

        Active = this;
    }

    private void NormalizeCurrentTemperature()
    {
        currentTemperatureCelsius = NormalizeTemperatureValue(currentTemperatureCelsius);
    }

    private static float NormalizeTemperatureValue(float temperatureCelsius)
    {
        return float.IsNaN(temperatureCelsius) || float.IsInfinity(temperatureCelsius)
            ? DefaultCurrentTemperatureCelsius
            : temperatureCelsius;
    }

    private static float ResolveWaterTemperatureCelsius(float airTemperatureCelsius)
    {
        return NormalizeTemperatureValue(
            DefaultWaterTemperatureCelsius
            + ((NormalizeTemperatureValue(airTemperatureCelsius) - DefaultCurrentTemperatureCelsius)
               * WaterTemperatureAirResponse));
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        NormalizeCurrentTemperature();
    }
#endif
}
