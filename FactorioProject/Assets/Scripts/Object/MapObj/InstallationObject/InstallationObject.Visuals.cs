using ProjectF.Rendering;
using UnityEngine;

public partial class InstallationObject
{
    private InstallationVisualState managedVisualState;
    protected virtual bool UsesManagedVisualUpdates => false;
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

    internal void RunManagedVisualUpdate(float deltaTime) => TickManagedVisuals(deltaTime);
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

