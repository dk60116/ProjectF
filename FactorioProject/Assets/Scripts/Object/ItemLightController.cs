using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public interface IItemLightWorkStateProvider
{
    bool IsWorkingForItemLight { get; }
}

public interface IItemLightPowerStateProvider
{
    bool HasPowerForItemLight { get; }
}

[DisallowMultipleComponent]
public sealed class ItemLightController : MonoBehaviour
{
    private const string RuntimeLightName = "_ItemLight";
    private const float DefaultIntensity = 2f;
    private const float DefaultLightRange = 6f;
    private const float DefaultLightHeight = 1.5f;
    private const float RuntimeStatePollIntervalSeconds = 0.1f;
    private const int MaxDisplayLightCount = 8;
    private static readonly int DisplayLightPositionAndInvRangeSqrId =
        Shader.PropertyToID("_BoxDisplayLightPositionAndInvRangeSqr");
    private static readonly int DisplayLightColorAndIntensityId =
        Shader.PropertyToID("_BoxDisplayLightColorAndIntensity");
    private static readonly int DisplayLightCountId =
        Shader.PropertyToID("_BoxDisplayLightCount");
    private static readonly HashSet<ItemLightController> LiveControllers =
        new HashSet<ItemLightController>();
    private static readonly List<ItemLightController> ActiveLightControllers =
        new List<ItemLightController>();
    private static readonly bool UsesLinearColorSpace =
        QualitySettings.activeColorSpace == ColorSpace.Linear;
    private static readonly Vector4[] DisplayLightPositionAndInvRangeSqr =
        new Vector4[MaxDisplayLightCount];
    private static readonly Vector4[] DisplayLightColorAndIntensity =
        new Vector4[MaxDisplayLightCount];
    private static readonly Vector4[] LastDisplayLightPositionAndInvRangeSqr =
        new Vector4[MaxDisplayLightCount];
    private static readonly Vector4[] LastDisplayLightColorAndIntensity =
        new Vector4[MaxDisplayLightCount];
    private static readonly float[] DisplayLightSelectionScores =
        new float[MaxDisplayLightCount];
    private static readonly WaitForSeconds RuntimeStatePollDelay =
        new WaitForSeconds(RuntimeStatePollIntervalSeconds);
    private static int lastDisplayLightUpdateFrame = -1;
    private static int lastDisplayLightCount = -1;
    private static bool displayLightGlobalsInitialized;

    private Light runtimeLight;
    private ItemDefinition.ItemLightMode lightMode;
    private float lightRange = 6f;
    private float lightIntensityMultiplier = 1f;
    private int itemId = -1;
    private bool toggled;
    private bool working;
    private bool powered = true;
    private IItemLightWorkStateProvider workStateProvider;
    private IItemLightPowerStateProvider powerStateProvider;
    private Coroutine runtimeStateRoutine;
    private int activeLightControllerIndex = -1;
    private Color cachedDisplayLightSourceColor;
    private Color cachedDisplayLightColor;
    private bool hasCachedDisplayLightColor;

    public bool IsToggled => toggled;
    public bool IsLightActive => runtimeLight != null && runtimeLight.enabled;
    public ItemDefinition.ItemLightMode Mode => lightMode;
    public event System.Action<bool> LightActiveStateChanged;

    public static void UpdateDisplayLightGlobals(Vector3 focusWorldPosition)
    {
        if (!Application.isPlaying || lastDisplayLightUpdateFrame == Time.frameCount)
        {
            return;
        }

        lastDisplayLightUpdateFrame = Time.frameCount;
        for (int i = 0; i < MaxDisplayLightCount; i++)
        {
            DisplayLightPositionAndInvRangeSqr[i] = Vector4.zero;
            DisplayLightColorAndIntensity[i] = Vector4.zero;
            DisplayLightSelectionScores[i] = float.MaxValue;
        }

        int selectedLightCount = 0;

        for (int i = 0; i < ActiveLightControllers.Count; i++)
        {
            ItemLightController controller = ActiveLightControllers[i];
            Light light = controller != null ? controller.runtimeLight : null;
            if (light == null || !light.enabled || !light.gameObject.activeInHierarchy)
            {
                continue;
            }

            float range = Mathf.Max(0.1f, light.range);
            Vector3 lightPosition = light.transform.position;
            float rangeSqr = range * range;
            float offsetX = lightPosition.x - focusWorldPosition.x;
            float offsetZ = lightPosition.z - focusWorldPosition.z;
            Color lightColor = controller.ResolveDisplayLightColor(light.color);
            float lightStrength = Mathf.Max(
                0.01f,
                Mathf.Max(0f, light.intensity)
                * Mathf.Max(lightColor.r, Mathf.Max(lightColor.g, lightColor.b)));
            float selectionScore = ((offsetX * offsetX) + (offsetZ * offsetZ))
                / (rangeSqr * lightStrength);
            Vector4 positionAndInvRangeSqr = new Vector4(
                lightPosition.x,
                lightPosition.y,
                lightPosition.z,
                1f / rangeSqr);
            Vector4 colorAndIntensity = new Vector4(
                lightColor.r,
                lightColor.g,
                lightColor.b,
                Mathf.Max(0f, light.intensity));
            InsertDisplayLight(
                selectionScore,
                positionAndInvRangeSqr,
                colorAndIntensity,
                ref selectedLightCount);
        }

        ApplyDisplayLightGlobalsIfChanged(selectedLightCount);
    }

    private Color ResolveDisplayLightColor(Color sourceColor)
    {
        if (!hasCachedDisplayLightColor || cachedDisplayLightSourceColor != sourceColor)
        {
            cachedDisplayLightSourceColor = sourceColor;
            cachedDisplayLightColor = UsesLinearColorSpace ? sourceColor.linear : sourceColor;
            hasCachedDisplayLightColor = true;
        }

        return cachedDisplayLightColor;
    }

    private static void InsertDisplayLight(
        float selectionScore,
        Vector4 positionAndInvRangeSqr,
        Vector4 colorAndIntensity,
        ref int selectedLightCount)
    {
        int insertionIndex;
        if (selectedLightCount < MaxDisplayLightCount)
        {
            insertionIndex = selectedLightCount;
            selectedLightCount++;
        }
        else
        {
            insertionIndex = MaxDisplayLightCount - 1;
            if (selectionScore >= DisplayLightSelectionScores[insertionIndex])
            {
                return;
            }
        }

        while (insertionIndex > 0
               && selectionScore < DisplayLightSelectionScores[insertionIndex - 1])
        {
            DisplayLightSelectionScores[insertionIndex] =
                DisplayLightSelectionScores[insertionIndex - 1];
            DisplayLightPositionAndInvRangeSqr[insertionIndex] =
                DisplayLightPositionAndInvRangeSqr[insertionIndex - 1];
            DisplayLightColorAndIntensity[insertionIndex] =
                DisplayLightColorAndIntensity[insertionIndex - 1];
            insertionIndex--;
        }

        DisplayLightSelectionScores[insertionIndex] = selectionScore;
        DisplayLightPositionAndInvRangeSqr[insertionIndex] = positionAndInvRangeSqr;
        DisplayLightColorAndIntensity[insertionIndex] = colorAndIntensity;
    }

    private static void ApplyDisplayLightGlobalsIfChanged(int selectedLightCount)
    {
        bool changed = !displayLightGlobalsInitialized
                       || lastDisplayLightCount != selectedLightCount;
        for (int i = 0; i < MaxDisplayLightCount && !changed; i++)
        {
            changed = !Approximately(
                          DisplayLightPositionAndInvRangeSqr[i],
                          LastDisplayLightPositionAndInvRangeSqr[i])
                      || !Approximately(
                          DisplayLightColorAndIntensity[i],
                          LastDisplayLightColorAndIntensity[i]);
        }

        if (!changed)
        {
            return;
        }

        displayLightGlobalsInitialized = true;
        lastDisplayLightCount = selectedLightCount;
        Shader.SetGlobalInt(DisplayLightCountId, selectedLightCount);
        Shader.SetGlobalVectorArray(
            DisplayLightPositionAndInvRangeSqrId,
            DisplayLightPositionAndInvRangeSqr);
        Shader.SetGlobalVectorArray(
            DisplayLightColorAndIntensityId,
            DisplayLightColorAndIntensity);
        for (int i = 0; i < MaxDisplayLightCount; i++)
        {
            LastDisplayLightPositionAndInvRangeSqr[i] =
                DisplayLightPositionAndInvRangeSqr[i];
            LastDisplayLightColorAndIntensity[i] = DisplayLightColorAndIntensity[i];
        }
    }

    private static bool Approximately(Vector4 left, Vector4 right)
    {
        return (left - right).sqrMagnitude <= 0.000001f;
    }

    public static ItemLightController Configure(
        GameObject owner,
        ItemDefinition definition,
        bool toggled = false)
    {
        if (definition == null)
        {
            return Configure(
                owner,
                -1,
                ItemDefinition.ItemLightMode.None,
                0f,
                false);
        }

        return Configure(
            owner,
            definition.id,
            definition.lightMode,
            definition.LightRange,
            toggled,
            definition.LightIntensityMultiplier);
    }

    public static ItemLightController Configure(
        GameObject owner,
        int itemId,
        ItemDefinition.ItemLightMode mode,
        float range,
        bool toggled = false,
        float intensityMultiplier = 1f)
    {
        if (owner == null)
        {
            return null;
        }

        ItemLightController controller = owner.GetComponent<ItemLightController>();
        if (controller == null && mode != ItemDefinition.ItemLightMode.None)
        {
            controller = owner.AddComponent<ItemLightController>();
        }

        controller?.ApplyConfiguration(itemId, mode, range, toggled, intensityMultiplier);
        return controller;
    }

    public static void RefreshDefinition(ItemDefinition definition)
    {
        if (definition == null)
        {
            return;
        }

        foreach (ItemLightController controller in LiveControllers)
        {
            if (controller != null && controller.itemId == definition.id)
            {
                controller.ApplyConfiguration(
                    definition.id,
                    definition.lightMode,
                    definition.LightRange,
                    controller.toggled,
                    definition.LightIntensityMultiplier);
            }
        }
    }

    public void SetToggled(bool active)
    {
        toggled = active;
        RefreshLightState();
    }

    public bool Toggle()
    {
        if (lightMode != ItemDefinition.ItemLightMode.Toggle)
        {
            return false;
        }

        SetToggled(!toggled);
        return true;
    }

    private void OnEnable()
    {
        LiveControllers.Add(this);
        WorldTimeService.GlobalDayStateChanged += HandleDayStateChanged;
        RefreshRuntimeStateMonitoring();
        RefreshLightState();
    }

    private void OnDisable()
    {
        StopRuntimeStateMonitoring();
        SetActiveLightRegistration(false);
        LiveControllers.Remove(this);
        WorldTimeService.GlobalDayStateChanged -= HandleDayStateChanged;
    }

    private void OnDestroy()
    {
        SetActiveLightRegistration(false);
        LiveControllers.Remove(this);
        WorldTimeService.GlobalDayStateChanged -= HandleDayStateChanged;
    }

    private void ApplyConfiguration(
        int configuredItemId,
        ItemDefinition.ItemLightMode mode,
        float range,
        bool toggleState,
        float intensityMultiplier)
    {
        itemId = configuredItemId;
        lightMode = mode;
        lightRange = Mathf.Max(0.1f, range);
        lightIntensityMultiplier = intensityMultiplier > 0f ? intensityMultiplier : 1f;
        toggled = toggleState;
        if (lightMode != ItemDefinition.ItemLightMode.Working)
        {
            working = false;
        }

        if (lightMode != ItemDefinition.ItemLightMode.None)
        {
            EnsureRuntimeLight();
            runtimeLight.range = lightRange;
            runtimeLight.intensity = ResolveIntensityForRange(lightRange) * lightIntensityMultiplier;
            runtimeLight.transform.localPosition = Vector3.up * ResolveHeightForRange(lightRange);
        }

        RefreshRuntimeStateMonitoring();
        RefreshLightState();
    }

    private void RefreshRuntimeStateMonitoring()
    {
        StopRuntimeStateMonitoring();
        workStateProvider = lightMode == ItemDefinition.ItemLightMode.Working
            ? GetComponent<IItemLightWorkStateProvider>()
            : null;
        powerStateProvider = GetComponent<IItemLightPowerStateProvider>();
        RefreshRuntimeState();

        if (!isActiveAndEnabled
            || (workStateProvider == null && powerStateProvider == null))
        {
            return;
        }

        runtimeStateRoutine = StartCoroutine(MonitorRuntimeState());
    }

    private void StopRuntimeStateMonitoring()
    {
        if (runtimeStateRoutine != null)
        {
            StopCoroutine(runtimeStateRoutine);
            runtimeStateRoutine = null;
        }

        workStateProvider = null;
        powerStateProvider = null;
    }

    private IEnumerator MonitorRuntimeState()
    {
        while (workStateProvider != null || powerStateProvider != null)
        {
            yield return RuntimeStatePollDelay;
            RefreshRuntimeState();
        }

        runtimeStateRoutine = null;
    }

    private void RefreshRuntimeState()
    {
        bool nextWorking = workStateProvider != null
                           && workStateProvider.IsWorkingForItemLight;
        bool nextPowered = powerStateProvider == null
                           || powerStateProvider.HasPowerForItemLight;
        if (working == nextWorking && powered == nextPowered)
        {
            return;
        }

        working = nextWorking;
        powered = nextPowered;
        RefreshLightState();
    }

    private void EnsureRuntimeLight()
    {
        if (runtimeLight != null)
        {
            return;
        }

        Transform existing = transform.Find(RuntimeLightName);
        GameObject lightObject;
        if (existing != null)
        {
            lightObject = existing.gameObject;
            runtimeLight = lightObject.GetComponent<Light>();
        }
        else
        {
            lightObject = new GameObject(RuntimeLightName);
            lightObject.transform.SetParent(transform, false);
        }

        runtimeLight ??= lightObject.AddComponent<Light>();
        runtimeLight.type = LightType.Point;
        runtimeLight.color = Color.white;
        runtimeLight.shadows = LightShadows.None;
        runtimeLight.renderMode = LightRenderMode.Auto;
        runtimeLight.lightShadowCasterMode = LightShadowCasterMode.Default;
    }

    private static float ResolveIntensityForRange(float range)
    {
        float rangeRatio = Mathf.Max(0.1f, range) / DefaultLightRange;
        return DefaultIntensity * rangeRatio * rangeRatio;
    }

    private static float ResolveHeightForRange(float range)
    {
        // Keep the source brightness stable while the squared intensity scaling widens the footprint.
        float rangeRatio = Mathf.Max(0.1f, range) / DefaultLightRange;
        return DefaultLightHeight * rangeRatio;
    }

    private void HandleDayStateChanged(bool isDay)
    {
        if (lightMode == ItemDefinition.ItemLightMode.NightOnly)
        {
            RefreshLightState();
        }
    }

    private void RefreshLightState()
    {
        if (runtimeLight == null)
        {
            return;
        }

        bool shouldLight = lightMode switch
        {
            ItemDefinition.ItemLightMode.Always => true,
            ItemDefinition.ItemLightMode.Toggle => toggled,
            ItemDefinition.ItemLightMode.NightOnly =>
                WorldTimeService.Active != null && !WorldTimeService.Active.IsDay,
            ItemDefinition.ItemLightMode.Working => working,
            _ => false
        };
        bool nextActive = shouldLight && powered;
        bool activeStateChanged = runtimeLight.enabled != nextActive;
        runtimeLight.enabled = nextActive;
        SetActiveLightRegistration(nextActive && isActiveAndEnabled);
        if (activeStateChanged)
        {
            LightActiveStateChanged?.Invoke(nextActive);
        }
    }

    private void SetActiveLightRegistration(bool active)
    {
        if (active)
        {
            if (activeLightControllerIndex >= 0)
            {
                return;
            }

            activeLightControllerIndex = ActiveLightControllers.Count;
            ActiveLightControllers.Add(this);
            return;
        }

        if (activeLightControllerIndex < 0)
        {
            return;
        }

        int removeIndex = activeLightControllerIndex;
        int lastIndex = ActiveLightControllers.Count - 1;
        ItemLightController lastController = ActiveLightControllers[lastIndex];
        ActiveLightControllers[removeIndex] = lastController;
        ActiveLightControllers.RemoveAt(lastIndex);
        activeLightControllerIndex = -1;
        if (lastController != null && lastController != this)
        {
            lastController.activeLightControllerIndex = removeIndex;
        }
    }
}
