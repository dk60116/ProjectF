using UnityEngine;

public class Campfire : InputOutputModule
{
    [SerializeField]
    private ParticleSystem fireEffect;

    public override bool RequiresItemLightInteractionRange => false;

    public override void ManagedUpdateTick(float deltaTime)
    {
        if (Application.isPlaying
            && IsItemLightToggled
            && !TryConsumeOperatingEnergy(deltaTime, out _))
        {
            SetItemLightToggled(false);
        }

        base.ManagedUpdateTick(deltaTime);
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        return IsItemLightToggled || base.ShouldKeepRuntimeUpdateTickActive();
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = false;
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null)
        {
            return "No machine";
        }

        if (!IsItemLightToggled)
        {
            return string.Empty;
        }

        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            return "No energy";
        }

        isProducing = true;
        return "Working";
    }

    protected override void OnItemLightToggleStateChanged(bool active)
    {
        if (fireEffect != null)
        {
            if (active)
            {
                fireEffect.Play(true);
            }
            else
            {
                fireEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        if (active)
        {
            WakeRuntimeUpdate();
        }
    }
}
