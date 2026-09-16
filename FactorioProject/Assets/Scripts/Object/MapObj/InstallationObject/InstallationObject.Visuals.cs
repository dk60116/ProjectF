using ProjectF.Rendering;
using UnityEngine;

public partial class InstallationObject
{
    private InstallationVisualState managedVisualState;
    private bool managedVisualRootMotionExpected;
    protected virtual bool UsesManagedVisualUpdates => false;
    protected virtual bool RequiresManagedVisualUpdate => false;
    protected virtual bool ManagedVisualRootCanMove => managedVisualRootMotionExpected;
    internal bool RequiresContinuousManagedVisibilityRefresh => ManagedVisualRootCanMove;
    protected bool ShouldUpdateVisuals => managedVisualState == null || managedVisualState.Visible;

    private void RegisterManagedVisualUpdates()
    {
        if (!Application.isPlaying || !UsesManagedVisualUpdates)
            return;
        if (managedVisualState == null)
            managedVisualState = new InstallationVisualState(this);
        WorldVisualUpdateManager.Register(managedVisualState);
    }

    private void UnregisterManagedVisualUpdates()
    {
        WorldVisualUpdateManager.Unregister(managedVisualState);
    }

    private void InvalidateManagedVisualVisibility()
    {
        WorldVisualUpdateManager.InvalidateVisibility(managedVisualState);
    }

    internal void SetManagedVisualRootMotionExpected(bool expected)
    {
        if (managedVisualRootMotionExpected == expected)
        {
            return;
        }

        managedVisualRootMotionExpected = expected;
        WorldVisualUpdateManager.InvalidateVisibility(managedVisualState, true);
    }

    internal bool RunManagedVisualUpdate(float deltaTime)
    {
        if (!RequiresManagedVisualUpdate)
            return false;

        TickManagedVisuals(deltaTime);
        return true;
    }
    internal void RefreshManagedVisualState() => OnManagedVisualsResumed();
    protected virtual void TickManagedVisuals(float deltaTime) { }
    protected virtual void OnManagedVisualsResumed() { }

    protected void SetVisualParticleActive(ParticleSystem effect, bool active,
        float speed = 1f, bool clear = false)
    {
        if (effect == null)
            return;
        // OnEnable may request an effect before the installation has registered.
        if (Application.isPlaying && UsesManagedVisualUpdates)
        {
            if (managedVisualState == null)
                managedVisualState = new InstallationVisualState(this);
            managedVisualState.SetParticle(effect, active, speed, clear);
            return;
        }
        if (active)
        {
            if (!effect.isEmitting)
                effect.Play(true);
        }
        else
            effect.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting);
    }
}

