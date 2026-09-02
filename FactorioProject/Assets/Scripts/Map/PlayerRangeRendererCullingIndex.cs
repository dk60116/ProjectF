using System.Collections.Generic;
using UnityEngine;

internal sealed class PlayerRangeRendererCullingIndex
{
    private const int MaxBucketCellsPerRenderer = 256;

    private readonly struct Registration
    {
        public readonly Vector2Int minCoordinate;
        public readonly Vector2Int maxCoordinate;
        public readonly bool usesSpatialBuckets;

        public Registration(
            Vector2Int minCoordinate,
            Vector2Int maxCoordinate,
            bool usesSpatialBuckets)
        {
            this.minCoordinate = minCoordinate;
            this.maxCoordinate = maxCoordinate;
            this.usesSpatialBuckets = usesSpatialBuckets;
        }
    }

    private readonly Dictionary<Renderer, Registration> registrations =
        new Dictionary<Renderer, Registration>();
    private readonly Dictionary<Renderer, bool> hiddenRendererPreviousStates =
        new Dictionary<Renderer, bool>();
    private readonly Dictionary<Vector2Int, List<Renderer>> renderersByCoordinate =
        new Dictionary<Vector2Int, List<Renderer>>();
    private readonly List<Renderer> unbucketedRenderers = new List<Renderer>();
    private readonly HashSet<Renderer> refreshSet = new HashSet<Renderer>();
    private readonly List<Renderer> cleanupBuffer = new List<Renderer>();

    private Vector2Int center;
    private int radius;
    private bool hasRange;

    public void SetRange(Vector2Int nextCenter, int nextRadius)
    {
        nextRadius = Mathf.Max(0, nextRadius);
        if (!hasRange || radius != nextRadius)
        {
            center = nextCenter;
            radius = nextRadius;
            hasRange = true;
            RefreshAllRegisteredRenderers();
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
        Vector2Int minCoordinate = new Vector2Int(
            Mathf.CeilToInt(bounds.min.x - 0.5f),
            Mathf.CeilToInt(bounds.min.z - 0.5f));
        Vector2Int maxCoordinate = new Vector2Int(
            Mathf.FloorToInt(bounds.max.x + 0.5f),
            Mathf.FloorToInt(bounds.max.z + 0.5f));
        long width = (long)maxCoordinate.x - minCoordinate.x + 1L;
        long height = (long)maxCoordinate.y - minCoordinate.y + 1L;
        bool usesSpatialBuckets = width > 0L
                                  && height > 0L
                                  && width <= MaxBucketCellsPerRenderer
                                  && height <= MaxBucketCellsPerRenderer
                                  && width * height <= MaxBucketCellsPerRenderer
                                  && !IsDynamicRenderer(targetRenderer);

        registrations[targetRenderer] = new Registration(
            minCoordinate,
            maxCoordinate,
            usesSpatialBuckets);
        if (usesSpatialBuckets)
        {
            AddToSpatialBuckets(targetRenderer, minCoordinate, maxCoordinate);
        }
        else
        {
            unbucketedRenderers.Add(targetRenderer);
        }

        ApplyVisibility(targetRenderer);
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

    public void Unregister(Renderer targetRenderer, bool restoreVisibility)
    {
        if (!registrations.TryGetValue(targetRenderer, out Registration registration))
        {
            RestoreHiddenState(targetRenderer, restoreVisibility);
            return;
        }

        if (registration.usesSpatialBuckets)
        {
            RemoveFromSpatialBuckets(targetRenderer, registration);
        }
        else
        {
            unbucketedRenderers.Remove(targetRenderer);
        }

        registrations.Remove(targetRenderer);
        RestoreHiddenState(targetRenderer, restoreVisibility);
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
        unbucketedRenderers.Clear();
        refreshSet.Clear();
        cleanupBuffer.Clear();
        hasRange = false;
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

    private void RefreshMovedRangeBoundary(Vector2Int previousCenter, Vector2Int nextCenter)
    {
        refreshSet.Clear();
        CollectRangeDifference(previousCenter, nextCenter);
        CollectRangeDifference(nextCenter, previousCenter);

        for (int i = 0; i < unbucketedRenderers.Count; i++)
        {
            refreshSet.Add(unbucketedRenderers[i]);
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
            if (!renderersByCoordinate.TryGetValue(new Vector2Int(x, y), out List<Renderer> renderers))
            {
                continue;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                refreshSet.Add(renderers[i]);
            }
        }
    }

    private void AddToSpatialBuckets(
        Renderer targetRenderer,
        Vector2Int minCoordinate,
        Vector2Int maxCoordinate)
    {
        for (int y = minCoordinate.y; y <= maxCoordinate.y; y++)
        {
            for (int x = minCoordinate.x; x <= maxCoordinate.x; x++)
            {
                Vector2Int coordinate = new Vector2Int(x, y);
                if (!renderersByCoordinate.TryGetValue(coordinate, out List<Renderer> renderers))
                {
                    renderers = new List<Renderer>(4);
                    renderersByCoordinate.Add(coordinate, renderers);
                }

                renderers.Add(targetRenderer);
            }
        }
    }

    private void RemoveFromSpatialBuckets(Renderer targetRenderer, Registration registration)
    {
        for (int y = registration.minCoordinate.y; y <= registration.maxCoordinate.y; y++)
        {
            for (int x = registration.minCoordinate.x; x <= registration.maxCoordinate.x; x++)
            {
                Vector2Int coordinate = new Vector2Int(x, y);
                if (!renderersByCoordinate.TryGetValue(coordinate, out List<Renderer> renderers))
                {
                    continue;
                }

                renderers.Remove(targetRenderer);
                if (renderers.Count == 0)
                {
                    renderersByCoordinate.Remove(coordinate);
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

    private static bool IsDynamicRenderer(Renderer targetRenderer)
    {
        return targetRenderer is SkinnedMeshRenderer
               || targetRenderer.GetComponentInParent<Rigidbody>() != null
               || targetRenderer.GetComponentInParent<AnimalAIController>() != null
               || targetRenderer.GetComponentInParent<Vehicle>() != null;
    }
}
