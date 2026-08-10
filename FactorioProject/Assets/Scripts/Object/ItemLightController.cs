using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public interface IItemLightWorkStateProvider
{
    bool IsWorkingForItemLight { get; }
}

[DisallowMultipleComponent]
public sealed class ItemLightController : MonoBehaviour
{
    private const string RuntimeLightName = "_ItemLight";
    private const float DefaultIntensity = 2f;
    private const float WorkingPollIntervalSeconds = 0.1f;
    private static readonly HashSet<ItemLightController> LiveControllers =
        new HashSet<ItemLightController>();
    private static readonly WaitForSeconds WorkingPollDelay =
        new WaitForSeconds(WorkingPollIntervalSeconds);

    private Light runtimeLight;
    private ItemDefinition.ItemLightMode lightMode;
    private float lightRange = 6f;
    private int itemId = -1;
    private bool toggled;
    private bool working;
    private IItemLightWorkStateProvider workStateProvider;
    private Coroutine workingStateRoutine;

    public bool IsToggled => toggled;
    public ItemDefinition.ItemLightMode Mode => lightMode;

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
            toggled);
    }

    public static ItemLightController Configure(
        GameObject owner,
        int itemId,
        ItemDefinition.ItemLightMode mode,
        float range,
        bool toggled = false)
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

        controller?.ApplyConfiguration(itemId, mode, range, toggled);
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
                    controller.toggled);
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
        RefreshWorkingStateMonitoring();
        RefreshLightState();
    }

    private void OnDisable()
    {
        StopWorkingStateMonitoring();
        LiveControllers.Remove(this);
        WorldTimeService.GlobalDayStateChanged -= HandleDayStateChanged;
    }

    private void OnDestroy()
    {
        LiveControllers.Remove(this);
        WorldTimeService.GlobalDayStateChanged -= HandleDayStateChanged;
    }

    private void ApplyConfiguration(
        int configuredItemId,
        ItemDefinition.ItemLightMode mode,
        float range,
        bool toggleState)
    {
        itemId = configuredItemId;
        lightMode = mode;
        lightRange = Mathf.Max(0.1f, range);
        toggled = toggleState;
        if (lightMode != ItemDefinition.ItemLightMode.Working)
        {
            working = false;
        }

        if (lightMode != ItemDefinition.ItemLightMode.None)
        {
            EnsureRuntimeLight();
            runtimeLight.range = lightRange;
        }

        RefreshWorkingStateMonitoring();
        RefreshLightState();
    }

    private void RefreshWorkingStateMonitoring()
    {
        StopWorkingStateMonitoring();
        if (!isActiveAndEnabled || lightMode != ItemDefinition.ItemLightMode.Working)
        {
            return;
        }

        workStateProvider = GetComponent<IItemLightWorkStateProvider>();
        RefreshWorkingState();
        workingStateRoutine = StartCoroutine(MonitorWorkingState());
    }

    private void StopWorkingStateMonitoring()
    {
        if (workingStateRoutine != null)
        {
            StopCoroutine(workingStateRoutine);
            workingStateRoutine = null;
        }

        workStateProvider = null;
    }

    private IEnumerator MonitorWorkingState()
    {
        while (lightMode == ItemDefinition.ItemLightMode.Working)
        {
            yield return WorkingPollDelay;
            RefreshWorkingState();
        }

        workingStateRoutine = null;
    }

    private void RefreshWorkingState()
    {
        bool nextWorking = workStateProvider != null
                           && workStateProvider.IsWorkingForItemLight;
        if (working == nextWorking)
        {
            return;
        }

        working = nextWorking;
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
            lightObject.transform.localPosition = Vector3.up * 0.35f;
        }

        runtimeLight ??= lightObject.AddComponent<Light>();
        runtimeLight.type = LightType.Point;
        runtimeLight.color = Color.white;
        runtimeLight.intensity = DefaultIntensity;
        runtimeLight.shadows = LightShadows.None;
        runtimeLight.renderMode = LightRenderMode.Auto;
        runtimeLight.lightShadowCasterMode = LightShadowCasterMode.Default;
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
        runtimeLight.enabled = shouldLight;
    }
}
