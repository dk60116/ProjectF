using System.Collections.Generic;
using ProjectF.Rendering;
using UnityEngine;

public partial class InstallationObject : IWorldColliderCullingTarget
{
    private struct ManagedColliderState
    {
        internal Collider Collider;
        internal bool DisabledByCulling;
    }

    // Capture runs only from the main-thread culling manager. Reusing these buffers avoids
    // one GetComponentsInChildren array allocation per installation during warm-up.
    private static readonly List<Collider> ColliderCaptureBuffer = new List<Collider>(8);
    private static readonly List<Rigidbody> RigidbodyCaptureBuffer = new List<Rigidbody>(4);
    private List<ManagedColliderState> managedColliderStates;
    private bool managedCollidersCaptured;
    private bool managedRigidbodiesChecked;
    private bool hasManagedDynamicRigidbody;
    private bool managedCollidersCulled;
    private int colliderCullingAccountedCount;
    private int colliderCullingRegistryIndex = -1;
    private Vector2Int colliderCullingCell;
    private int colliderCullingCellIndex = -1;
    private int colliderCullingPendingIndex = -1;
    private int colliderCullingActiveIndex = -1;

    private void RegisterManagedColliderCulling()
    {
        if (Application.isPlaying) WorldColliderCullingManager.Register(this);
    }

    private void RefreshManagedColliderCullingSpatialRegistration() =>
        WorldColliderCullingManager.RefreshSpatialRegistration(this);

    private void UnregisterManagedColliderCulling() =>
        WorldColliderCullingManager.Unregister(this);

    bool IWorldColliderCullingTarget.ColliderCullingAlive => this != null && isActiveAndEnabled;
    bool IWorldColliderCullingTarget.ColliderCullingExempt => SimulationId <= 0L
        || this is Vehicle
        || HasManagedDynamicRigidbody();
    bool IWorldColliderCullingTarget.ColliderCullingCulled => managedCollidersCulled;
    Vector3 IWorldColliderCullingTarget.ColliderCullingPosition => transform.position;
    float IWorldColliderCullingTarget.ColliderCullingRadius => Mathf.Max(2f, FocusActivationRadius);
    int IWorldColliderCullingTarget.ColliderCullingManagedCount => managedColliderStates?.Count ?? 0;
    int IWorldColliderCullingTarget.ColliderCullingAccountedCount
    {
        get => colliderCullingAccountedCount;
        set => colliderCullingAccountedCount = value;
    }
    int IWorldColliderCullingTarget.ColliderCullingRegistryIndex
    {
        get => colliderCullingRegistryIndex;
        set => colliderCullingRegistryIndex = value;
    }
    Vector2Int IWorldColliderCullingTarget.ColliderCullingCell
    {
        get => colliderCullingCell;
        set => colliderCullingCell = value;
    }
    int IWorldColliderCullingTarget.ColliderCullingCellIndex
    {
        get => colliderCullingCellIndex;
        set => colliderCullingCellIndex = value;
    }
    int IWorldColliderCullingTarget.ColliderCullingPendingIndex
    {
        get => colliderCullingPendingIndex;
        set => colliderCullingPendingIndex = value;
    }
    int IWorldColliderCullingTarget.ColliderCullingActiveIndex
    {
        get => colliderCullingActiveIndex;
        set => colliderCullingActiveIndex = value;
    }

    void IWorldColliderCullingTarget.ApplyColliderCulling(bool culled)
    {
        if (!culled && !managedCollidersCulled) return;
        CaptureManagedColliders();
        managedCollidersCulled = culled;
        if (managedColliderStates == null) return;

        for (int i = 0; i < managedColliderStates.Count; i++)
        {
            ManagedColliderState state = managedColliderStates[i];
            Collider collider = state.Collider;
            if (collider == null) continue;
            if (culled)
            {
                if (!collider.enabled) continue;
                state.DisabledByCulling = true;
                collider.enabled = false;
                managedColliderStates[i] = state;
            }
            else if (state.DisabledByCulling)
            {
                state.DisabledByCulling = false;
                collider.enabled = true;
                managedColliderStates[i] = state;
            }
        }
    }

    void IWorldColliderCullingTarget.ReleaseColliderCulling()
    {
        managedCollidersCulled = false;
        if (managedColliderStates == null) return;
        for (int i = 0; i < managedColliderStates.Count; i++)
        {
            ManagedColliderState state = managedColliderStates[i];
            if (state.Collider != null && state.DisabledByCulling)
                state.Collider.enabled = true;
            state.DisabledByCulling = false;
            managedColliderStates[i] = state;
        }
    }

    private void CaptureManagedColliders()
    {
        if (managedCollidersCaptured || this == null) return;
        managedCollidersCaptured = true;
        ColliderCaptureBuffer.Clear();
        GetComponentsInChildren(true, ColliderCaptureBuffer);
        for (int i = 0; i < ColliderCaptureBuffer.Count; i++)
        {
            Collider collider = ColliderCaptureBuffer[i];
            if (collider == null || collider.GetComponentInParent<InstallationObject>() != this) continue;
            managedColliderStates ??= new List<ManagedColliderState>(ColliderCaptureBuffer.Count);
            managedColliderStates.Add(new ManagedColliderState { Collider = collider });
        }
        ColliderCaptureBuffer.Clear();
    }

    private bool HasManagedDynamicRigidbody()
    {
        if (managedRigidbodiesChecked) return hasManagedDynamicRigidbody;
        managedRigidbodiesChecked = true;
        RigidbodyCaptureBuffer.Clear();
        GetComponentsInChildren(true, RigidbodyCaptureBuffer);
        for (int i = 0; i < RigidbodyCaptureBuffer.Count; i++)
        {
            Rigidbody body = RigidbodyCaptureBuffer[i];
            if (body == null || body.isKinematic) continue;
            hasManagedDynamicRigidbody = true;
            break;
        }
        RigidbodyCaptureBuffer.Clear();
        return hasManagedDynamicRigidbody;
    }
}
