using System.Collections.Generic;
using UnityEngine;

internal sealed class PlayerRangeCullingIndex
{
    private const int MaxBucketCellsPerComponent = 256;

    private readonly struct Registration
    {
        public readonly Vector2Int minCoordinate;
        public readonly Vector2Int maxCoordinate;
        public readonly bool usesSpatialBuckets;
        public readonly bool isDynamic;

        public Registration(
            Vector2Int minCoordinate,
            Vector2Int maxCoordinate,
            bool usesSpatialBuckets,
            bool isDynamic)
        {
            this.minCoordinate = minCoordinate;
            this.maxCoordinate = maxCoordinate;
            this.usesSpatialBuckets = usesSpatialBuckets;
            this.isDynamic = isDynamic;
        }
    }

    private struct ColliderRegistration
    {
        public Vector2Int minCoordinate;
        public Vector2Int maxCoordinate;
        public bool usesSpatialBuckets;
        public bool isDynamic;
        public Bounds bounds;
        public Vector3 trackedPosition;
    }

    private readonly Dictionary<Renderer, Registration> registrations =
        new Dictionary<Renderer, Registration>();
    private readonly Dictionary<Renderer, bool> hiddenRendererPreviousStates =
        new Dictionary<Renderer, bool>();
    private readonly Dictionary<Vector2Int, List<Renderer>> renderersByCoordinate =
        new Dictionary<Vector2Int, List<Renderer>>();
    private readonly List<Renderer> unbucketedStaticRenderers = new List<Renderer>();
    private readonly List<Renderer> dynamicRenderers = new List<Renderer>();
    private readonly HashSet<Renderer> refreshSet = new HashSet<Renderer>();
    private readonly List<Renderer> cleanupBuffer = new List<Renderer>();
    private readonly Dictionary<Collider, ColliderRegistration> colliderRegistrations =
        new Dictionary<Collider, ColliderRegistration>();
    private readonly Dictionary<Collider, bool> disabledColliderPreviousStates =
        new Dictionary<Collider, bool>();
    private readonly Dictionary<Vector2Int, List<Collider>> collidersByCoordinate =
        new Dictionary<Vector2Int, List<Collider>>();
    private readonly List<Collider> unbucketedStaticColliders = new List<Collider>();
    private readonly List<Collider> dynamicColliders = new List<Collider>();
    private readonly HashSet<Collider> colliderRefreshSet = new HashSet<Collider>();
    private readonly List<Collider> colliderCleanupBuffer = new List<Collider>();

    private Vector2Int center;
    private int radius;
    private bool hasRange;
    private int dynamicRendererCursor;
    private int dynamicColliderCursor;

    public void SetRange(Vector2Int nextCenter, int nextRadius)
    {
        nextRadius = Mathf.Max(0, nextRadius);
        if (!hasRange || radius != nextRadius)
        {
            center = nextCenter;
            radius = nextRadius;
            hasRange = true;
            RefreshAllRegisteredRenderers();
            RefreshAllRegisteredColliders();
            return;
        }

        if (center == nextCenter)
        {
            return;
        }

        Vector2Int previousCenter = center;
        center = nextCenter;
        RefreshMovedRangeBoundary(previousCenter, nextCenter);
    }

    public bool Intersects(Bounds bounds)
    {
        if (!hasRange)
        {
            return true;
        }

        float range = radius + 0.5f;
        float minX = center.x - range;
        float maxX = center.x + range;
        float minZ = center.y - range;
        float maxZ = center.y + range;
        return bounds.max.x >= minX
               && bounds.min.x <= maxX
               && bounds.max.z >= minZ
               && bounds.min.z <= maxZ;
    }

    public void Register(Renderer targetRenderer)
    {
        if (targetRenderer == null)
        {
            return;
        }

        if (registrations.ContainsKey(targetRenderer))
        {
            Unregister(targetRenderer, true);
        }

        Bounds bounds = targetRenderer.bounds;
        Vector2Int minCoordinate = GetMinimumCoordinate(bounds);
        Vector2Int maxCoordinate = GetMaximumCoordinate(bounds);
        bool isDynamic = IsDynamicRenderer(targetRenderer);
        bool usesSpatialBuckets = CanUseSpatialBuckets(minCoordinate, maxCoordinate)
                                  && !isDynamic;

        registrations[targetRenderer] = new Registration(
            minCoordinate,
            maxCoordinate,
            usesSpatialBuckets,
            isDynamic);
        if (usesSpatialBuckets)
        {
            AddToSpatialBuckets(
                targetRenderer,
                minCoordinate,
                maxCoordinate,
                renderersByCoordinate);
        }
        else if (isDynamic)
        {
            dynamicRenderers.Add(targetRenderer);
        }
        else
        {
            unbucketedStaticRenderers.Add(targetRenderer);
        }

        ApplyVisibility(targetRenderer);
    }

    public void Register(Collider targetCollider)
    {
        if (targetCollider == null)
        {
            return;
        }

        if (colliderRegistrations.ContainsKey(targetCollider))
        {
            Unregister(targetCollider, true);
        }

        Bounds bounds = targetCollider.bounds;
        Vector2Int minCoordinate = GetMinimumCoordinate(bounds);
        Vector2Int maxCoordinate = GetMaximumCoordinate(bounds);
        bool isDynamic = IsDynamicCollider(targetCollider);
        bool usesSpatialBuckets = CanUseSpatialBuckets(minCoordinate, maxCoordinate)
                                  && !isDynamic;
        ColliderRegistration registration = new ColliderRegistration
        {
            minCoordinate = minCoordinate,
            maxCoordinate = maxCoordinate,
            usesSpatialBuckets = usesSpatialBuckets,
            isDynamic = isDynamic,
            bounds = bounds,
            trackedPosition = targetCollider.transform.position
        };
        colliderRegistrations[targetCollider] = registration;

        if (usesSpatialBuckets)
        {
            AddToSpatialBuckets(
                targetCollider,
                minCoordinate,
                maxCoordinate,
                collidersByCoordinate);
        }
        else if (!isDynamic)
        {
            unbucketedStaticColliders.Add(targetCollider);
        }

        if (isDynamic)
        {
            dynamicColliders.Add(targetCollider);
        }

        ApplyColliderVisibility(targetCollider);
    }

    public void RemoveMissing(HashSet<Renderer> retainedRenderers)
    {
        cleanupBuffer.Clear();
        foreach (KeyValuePair<Renderer, Registration> pair in registrations)
        {
            Renderer targetRenderer = pair.Key;
            if (targetRenderer == null
                || retainedRenderers == null
                || !retainedRenderers.Contains(targetRenderer))
            {
                cleanupBuffer.Add(targetRenderer);
            }
        }

        for (int i = 0; i < cleanupBuffer.Count; i++)
        {
            Unregister(cleanupBuffer[i], true);
        }

        cleanupBuffer.Clear();
    }

    public void RemoveMissing(HashSet<Collider> retainedColliders)
    {
        colliderCleanupBuffer.Clear();
        foreach (KeyValuePair<Collider, ColliderRegistration> pair in colliderRegistrations)
        {
            Collider targetCollider = pair.Key;
            if (targetCollider == null
                || retainedColliders == null
                || !retainedColliders.Contains(targetCollider))
            {
                colliderCleanupBuffer.Add(targetCollider);
            }
        }

        for (int i = 0; i < colliderCleanupBuffer.Count; i++)
        {
            Unregister(colliderCleanupBuffer[i], true);
        }

        colliderCleanupBuffer.Clear();
    }

    public void Unregister(Renderer targetRenderer, bool restoreVisibility)
    {
        if (!registrations.TryGetValue(targetRenderer, out Registration registration))
        {
            RestoreHiddenState(targetRenderer, restoreVisibility);
            return;
        }

        if (registration.usesSpatialBuckets)
        {
            RemoveFromSpatialBuckets(
                targetRenderer,
                registration.minCoordinate,
                registration.maxCoordinate,
                renderersByCoordinate);
        }
        else if (registration.isDynamic)
        {
            dynamicRenderers.Remove(targetRenderer);
            ClampDynamicCursors();
        }
        else
        {
            unbucketedStaticRenderers.Remove(targetRenderer);
        }

        registrations.Remove(targetRenderer);
        RestoreHiddenState(targetRenderer, restoreVisibility);
    }

    public void Unregister(Collider targetCollider, bool restoreEnabledState)
    {
        if (!colliderRegistrations.TryGetValue(
                targetCollider,
                out ColliderRegistration registration))
        {
            RestoreDisabledState(targetCollider, restoreEnabledState);
            return;
        }

        if (registration.usesSpatialBuckets)
        {
            RemoveFromSpatialBuckets(
                targetCollider,
                registration.minCoordinate,
                registration.maxCoordinate,
                collidersByCoordinate);
        }
        else if (!registration.isDynamic)
        {
            unbucketedStaticColliders.Remove(targetCollider);
        }

        if (registration.isDynamic)
        {
            dynamicColliders.Remove(targetCollider);
            ClampDynamicCursors();
        }

        colliderRegistrations.Remove(targetCollider);
        RestoreDisabledState(targetCollider, restoreEnabledState);
    }

    public void Clear(bool restoreVisibility)
    {
        if (restoreVisibility)
        {
            foreach (KeyValuePair<Renderer, bool> pair in hiddenRendererPreviousStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.forceRenderingOff = pair.Value;
                }
            }
        }

        registrations.Clear();
        hiddenRendererPreviousStates.Clear();
        renderersByCoordinate.Clear();
        unbucketedStaticRenderers.Clear();
        dynamicRenderers.Clear();
        refreshSet.Clear();
        cleanupBuffer.Clear();

        if (restoreVisibility)
        {
            foreach (KeyValuePair<Collider, bool> pair in disabledColliderPreviousStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.enabled = pair.Value;
                }
            }
        }

        colliderRegistrations.Clear();
        disabledColliderPreviousStates.Clear();
        collidersByCoordinate.Clear();
        unbucketedStaticColliders.Clear();
        dynamicColliders.Clear();
        colliderRefreshSet.Clear();
        colliderCleanupBuffer.Clear();
        dynamicRendererCursor = 0;
        dynamicColliderCursor = 0;
        hasRange = false;
    }

    public void RefreshDynamicComponents(int rendererBudget, int colliderBudget)
    {
        RefreshDynamicRenderers(rendererBudget);
        RefreshDynamicColliders(colliderBudget);
    }

    private void RefreshDynamicRenderers(int budget)
    {
        int targetCount = Mathf.Min(Mathf.Max(0, budget), dynamicRenderers.Count);
        for (int processed = 0; processed < targetCount && dynamicRenderers.Count > 0; processed++)
        {
            if (dynamicRendererCursor >= dynamicRenderers.Count)
            {
                dynamicRendererCursor = 0;
            }

            Renderer targetRenderer = dynamicRenderers[dynamicRendererCursor];
            if (targetRenderer == null)
            {
                int previousCount = dynamicRenderers.Count;
                Unregister(targetRenderer, false);
                if (dynamicRenderers.Count == previousCount)
                {
                    dynamicRenderers.RemoveAt(dynamicRendererCursor);
                    ClampDynamicCursors();
                }

                continue;
            }

            ApplyVisibility(targetRenderer);
            dynamicRendererCursor++;
        }
    }

    private void RefreshDynamicColliders(int budget)
    {
        int targetCount = Mathf.Min(Mathf.Max(0, budget), dynamicColliders.Count);
        for (int processed = 0; processed < targetCount && dynamicColliders.Count > 0; processed++)
        {
            if (dynamicColliderCursor >= dynamicColliders.Count)
            {
                dynamicColliderCursor = 0;
            }

            Collider targetCollider = dynamicColliders[dynamicColliderCursor];
            if (targetCollider == null)
            {
                int previousCount = dynamicColliders.Count;
                Unregister(targetCollider, false);
                if (dynamicColliders.Count == previousCount)
                {
                    dynamicColliders.RemoveAt(dynamicColliderCursor);
                    ClampDynamicCursors();
                }

                continue;
            }

            ApplyColliderVisibility(targetCollider);
            dynamicColliderCursor++;
        }
    }

    private void RefreshAllRegisteredRenderers()
    {
        cleanupBuffer.Clear();
        foreach (KeyValuePair<Renderer, Registration> pair in registrations)
        {
            Renderer targetRenderer = pair.Key;
            if (targetRenderer == null)
            {
                cleanupBuffer.Add(targetRenderer);
                continue;
            }

            ApplyVisibility(targetRenderer);
        }

        for (int i = 0; i < cleanupBuffer.Count; i++)
        {
            Unregister(cleanupBuffer[i], false);
        }

        cleanupBuffer.Clear();
    }

    private void RefreshAllRegisteredColliders()
    {
        colliderRefreshSet.Clear();
        foreach (KeyValuePair<Collider, ColliderRegistration> pair in colliderRegistrations)
        {
            colliderRefreshSet.Add(pair.Key);
        }

        colliderCleanupBuffer.Clear();
        foreach (Collider targetCollider in colliderRefreshSet)
        {
            if (targetCollider == null)
            {
                colliderCleanupBuffer.Add(targetCollider);
                continue;
            }

            ApplyColliderVisibility(targetCollider);
        }

        for (int i = 0; i < colliderCleanupBuffer.Count; i++)
        {
            Unregister(colliderCleanupBuffer[i], false);
        }

        colliderCleanupBuffer.Clear();
        colliderRefreshSet.Clear();
    }

    private void RefreshMovedRangeBoundary(Vector2Int previousCenter, Vector2Int nextCenter)
    {
        refreshSet.Clear();
        colliderRefreshSet.Clear();
        CollectRangeDifference(previousCenter, nextCenter);
        CollectRangeDifference(nextCenter, previousCenter);

        for (int i = 0; i < unbucketedStaticRenderers.Count; i++)
        {
            refreshSet.Add(unbucketedStaticRenderers[i]);
        }

        for (int i = 0; i < unbucketedStaticColliders.Count; i++)
        {
            colliderRefreshSet.Add(unbucketedStaticColliders[i]);
        }

        cleanupBuffer.Clear();
        foreach (Renderer targetRenderer in refreshSet)
        {
            if (targetRenderer == null)
            {
                cleanupBuffer.Add(targetRenderer);
                continue;
            }

            ApplyVisibility(targetRenderer);
        }

        for (int i = 0; i < cleanupBuffer.Count; i++)
        {
            Unregister(cleanupBuffer[i], false);
        }

        cleanupBuffer.Clear();
        refreshSet.Clear();

        colliderCleanupBuffer.Clear();
        foreach (Collider targetCollider in colliderRefreshSet)
        {
            if (targetCollider == null)
            {
                colliderCleanupBuffer.Add(targetCollider);
                continue;
            }

            ApplyColliderVisibility(targetCollider);
        }

        for (int i = 0; i < colliderCleanupBuffer.Count; i++)
        {
            Unregister(colliderCleanupBuffer[i], false);
        }

        colliderCleanupBuffer.Clear();
        colliderRefreshSet.Clear();
    }

    private void ClampDynamicCursors()
    {
        if (dynamicRendererCursor >= dynamicRenderers.Count)
        {
            dynamicRendererCursor = 0;
        }

        if (dynamicColliderCursor >= dynamicColliders.Count)
        {
            dynamicColliderCursor = 0;
        }
    }

    private void CollectRangeDifference(Vector2Int sourceCenter, Vector2Int excludedCenter)
    {
        int sourceMinX = sourceCenter.x - radius;
        int sourceMaxX = sourceCenter.x + radius;
        int sourceMinY = sourceCenter.y - radius;
        int sourceMaxY = sourceCenter.y + radius;
        int excludedMinX = excludedCenter.x - radius;
        int excludedMaxX = excludedCenter.x + radius;
        int excludedMinY = excludedCenter.y - radius;
        int excludedMaxY = excludedCenter.y + radius;

        for (int y = sourceMinY; y <= sourceMaxY; y++)
        {
            if (y < excludedMinY || y > excludedMaxY)
            {
                CollectRow(sourceMinX, sourceMaxX, y);
                continue;
            }

            CollectRow(sourceMinX, Mathf.Min(sourceMaxX, excludedMinX - 1), y);
            CollectRow(Mathf.Max(sourceMinX, excludedMaxX + 1), sourceMaxX, y);
        }
    }

    private void CollectRow(int minX, int maxX, int y)
    {
        for (int x = minX; x <= maxX; x++)
        {
            Vector2Int coordinate = new Vector2Int(x, y);
            if (renderersByCoordinate.TryGetValue(coordinate, out List<Renderer> renderers))
            {
                for (int i = 0; i < renderers.Count; i++)
                {
                    refreshSet.Add(renderers[i]);
                }
            }

            if (collidersByCoordinate.TryGetValue(coordinate, out List<Collider> colliders))
            {
                for (int i = 0; i < colliders.Count; i++)
                {
                    colliderRefreshSet.Add(colliders[i]);
                }
            }
        }
    }

    private static void AddToSpatialBuckets<T>(
        T component,
        Vector2Int minCoordinate,
        Vector2Int maxCoordinate,
        Dictionary<Vector2Int, List<T>> componentsByCoordinate)
        where T : Component
    {
        for (int y = minCoordinate.y; y <= maxCoordinate.y; y++)
        {
            for (int x = minCoordinate.x; x <= maxCoordinate.x; x++)
            {
                Vector2Int coordinate = new Vector2Int(x, y);
                if (!componentsByCoordinate.TryGetValue(coordinate, out List<T> components))
                {
                    components = new List<T>(4);
                    componentsByCoordinate.Add(coordinate, components);
                }

                components.Add(component);
            }
        }
    }

    private static void RemoveFromSpatialBuckets<T>(
        T component,
        Vector2Int minCoordinate,
        Vector2Int maxCoordinate,
        Dictionary<Vector2Int, List<T>> componentsByCoordinate)
        where T : Component
    {
        for (int y = minCoordinate.y; y <= maxCoordinate.y; y++)
        {
            for (int x = minCoordinate.x; x <= maxCoordinate.x; x++)
            {
                Vector2Int coordinate = new Vector2Int(x, y);
                if (!componentsByCoordinate.TryGetValue(coordinate, out List<T> components))
                {
                    continue;
                }

                components.Remove(component);
                if (components.Count == 0)
                {
                    componentsByCoordinate.Remove(coordinate);
                }
            }
        }
    }

    private void ApplyVisibility(Renderer targetRenderer)
    {
        if (Intersects(targetRenderer.bounds))
        {
            RestoreHiddenState(targetRenderer, true);
            return;
        }

        if (!hiddenRendererPreviousStates.ContainsKey(targetRenderer))
        {
            hiddenRendererPreviousStates.Add(targetRenderer, targetRenderer.forceRenderingOff);
        }

        targetRenderer.forceRenderingOff = true;
    }

    private void RestoreHiddenState(Renderer targetRenderer, bool restoreVisibility)
    {
        if (!hiddenRendererPreviousStates.TryGetValue(targetRenderer, out bool previousState))
        {
            return;
        }

        if (restoreVisibility && targetRenderer != null)
        {
            targetRenderer.forceRenderingOff = previousState;
        }

        hiddenRendererPreviousStates.Remove(targetRenderer);
    }

    private void ApplyColliderVisibility(Collider targetCollider)
    {
        if (!colliderRegistrations.TryGetValue(
                targetCollider,
                out ColliderRegistration registration))
        {
            return;
        }

        Bounds bounds = ResolveColliderBounds(targetCollider, ref registration);
        colliderRegistrations[targetCollider] = registration;
        if (Intersects(bounds))
        {
            RestoreDisabledState(targetCollider, true);
            return;
        }

        if (!disabledColliderPreviousStates.ContainsKey(targetCollider))
        {
            disabledColliderPreviousStates.Add(targetCollider, targetCollider.enabled);
        }

        targetCollider.enabled = false;
    }

    private Bounds ResolveColliderBounds(
        Collider targetCollider,
        ref ColliderRegistration registration)
    {
        Vector3 currentPosition = targetCollider.transform.position;
        if (targetCollider.enabled && targetCollider.gameObject.activeInHierarchy)
        {
            registration.bounds = targetCollider.bounds;
        }
        else if (registration.isDynamic)
        {
            registration.bounds.center += currentPosition - registration.trackedPosition;
        }

        registration.trackedPosition = currentPosition;
        return registration.bounds;
    }

    private void RestoreDisabledState(Collider targetCollider, bool restoreEnabledState)
    {
        if (!disabledColliderPreviousStates.TryGetValue(
                targetCollider,
                out bool previousState))
        {
            return;
        }

        if (restoreEnabledState && targetCollider != null)
        {
            targetCollider.enabled = previousState;
        }

        disabledColliderPreviousStates.Remove(targetCollider);
    }

    private static Vector2Int GetMinimumCoordinate(Bounds bounds)
    {
        return new Vector2Int(
            Mathf.CeilToInt(bounds.min.x - 0.5f),
            Mathf.CeilToInt(bounds.min.z - 0.5f));
    }

    private static Vector2Int GetMaximumCoordinate(Bounds bounds)
    {
        return new Vector2Int(
            Mathf.FloorToInt(bounds.max.x + 0.5f),
            Mathf.FloorToInt(bounds.max.z + 0.5f));
    }

    private static bool CanUseSpatialBuckets(
        Vector2Int minCoordinate,
        Vector2Int maxCoordinate)
    {
        long width = (long)maxCoordinate.x - minCoordinate.x + 1L;
        long height = (long)maxCoordinate.y - minCoordinate.y + 1L;
        return width > 0L
               && height > 0L
               && width <= MaxBucketCellsPerComponent
               && height <= MaxBucketCellsPerComponent
               && width * height <= MaxBucketCellsPerComponent;
    }

    private static bool IsDynamicRenderer(Renderer targetRenderer)
    {
        return targetRenderer is SkinnedMeshRenderer
               || targetRenderer.GetComponentInParent<Rigidbody>() != null
               || targetRenderer.GetComponentInParent<AnimalAIController>() != null
               || targetRenderer.GetComponentInParent<Vehicle>() != null;
    }

    private static bool IsDynamicCollider(Collider targetCollider)
    {
        return targetCollider.GetComponentInParent<Rigidbody>() != null
               || targetCollider.GetComponentInParent<AnimalAIController>() != null
               || targetCollider.GetComponentInParent<Vehicle>() != null;
    }
}
