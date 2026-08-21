using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class UndergroundPipe : Pipe
{
    private const int MinimumPairDistance = 2;
    private const string GeneratedExitVisualName = "__UndergroundPipeExit";
    private const string GeneratedRouteVisualName = "__UndergroundPipeRoute";
    private static readonly List<UndergroundPipe> ActivePipes = new List<UndergroundPipe>();

    [SerializeField]
    private Transform primaryEndpointVisual;
    [SerializeField, Min(0f)]
    private float previewRouteHeight = 0.12f;
    [SerializeField, Min(0.01f)]
    private float previewRouteWidth = 0.08f;

    private Transform secondaryEndpointVisual;
    private LineRenderer previewRouteRenderer;
    private Material previewRouteMaterial;
    private Vector2Int previewFirstCoordinate;
    private Vector2Int previewSecondCoordinate;
    private bool previewPairConfigured;
    private bool previewPairCommitted;
    private Quaternion initialPreviewRootRotation = Quaternion.identity;
    private bool hasInitialPreviewRootRotation;

    public bool HasCompletePair => TryGetPairCoordinates(out _, out _);
    public bool HasPreviewCandidate => previewPairConfigured;
    public bool IsPreviewPairCommitted => previewPairConfigured && previewPairCommitted;

    public int MaxPairDistance
    {
        get
        {
            ItemDefinition definition = ResolveItemDefinition();
            return definition != null ? definition.UndergroundPipeMaxDistance : 5;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!ActivePipes.Contains(this))
        {
            ActivePipes.Add(this);
        }
        if (Application.isPlaying && HasCompletePair)
        {
            RefreshEndpointVisuals(false);
        }
    }

    protected override void OnDisable()
    {
        ActivePipes.Remove(this);
        base.OnDisable();
    }

    public bool TryGetPairCoordinates(out Vector2Int firstCoordinate, out Vector2Int secondCoordinate)
    {
        if (previewPairConfigured && previewPairCommitted)
        {
            firstCoordinate = previewFirstCoordinate;
            secondCoordinate = previewSecondCoordinate;
            return IsValidPairGeometry(firstCoordinate, secondCoordinate, int.MaxValue);
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        if (occupiedCoordinates != null && occupiedCoordinates.Count == 2)
        {
            firstCoordinate = occupiedCoordinates[0];
            secondCoordinate = occupiedCoordinates[1];
            return IsValidPairGeometry(firstCoordinate, secondCoordinate, int.MaxValue);
        }

        firstCoordinate = default;
        secondCoordinate = default;
        return false;
    }

    public bool TryGetPreviewCandidateCoordinates(
        out Vector2Int firstCoordinate,
        out Vector2Int secondCoordinate)
    {
        firstCoordinate = previewFirstCoordinate;
        secondCoordinate = previewSecondCoordinate;
        return previewPairConfigured;
    }

    public bool ContainsEndpoint(Vector2Int coordinate)
    {
        return TryGetPairCoordinates(out Vector2Int first, out Vector2Int second)
               && (coordinate == first || coordinate == second);
    }

    public bool TryGetOutwardDirection(Vector2Int coordinate, out Vector2Int outwardDirection)
    {
        outwardDirection = Vector2Int.zero;
        if (!TryGetPairCoordinates(out Vector2Int first, out Vector2Int second))
        {
            return false;
        }

        Vector2Int tunnelDirection;
        if (coordinate == first)
        {
            tunnelDirection = NormalizeCardinal(second - first);
        }
        else if (coordinate == second)
        {
            tunnelDirection = NormalizeCardinal(first - second);
        }
        else
        {
            return false;
        }

        outwardDirection = -tunnelDirection;
        return outwardDirection != Vector2Int.zero;
    }

    public override bool HasConnectionTowards(Quaternion rotation, Vector2Int direction)
    {
        if (TryGetPairCoordinates(out Vector2Int first, out _))
        {
            return HasConnectionTowardsAt(first, rotation, direction);
        }

        return base.HasConnectionTowards(rotation, direction);
    }

    public override bool HasConnectionTowardsAt(
        Vector2Int coordinate,
        Quaternion rotation,
        Vector2Int direction)
    {
        return TryGetOutwardDirection(coordinate, out Vector2Int outwardDirection)
               && direction == outwardDirection;
    }

    public override bool TryGetRemoteConnectionCoordinate(
        Vector2Int coordinate,
        out Vector2Int remoteCoordinate)
    {
        remoteCoordinate = default;
        if (!TryGetPairCoordinates(out Vector2Int first, out Vector2Int second))
        {
            return false;
        }

        if (coordinate == first)
        {
            remoteCoordinate = second;
            return true;
        }

        if (coordinate == second)
        {
            remoteCoordinate = first;
            return true;
        }

        return false;
    }

    public void ConfigurePreviewPair(
        Vector2Int firstCoordinate,
        Vector2Int secondCoordinate,
        Vector3 firstWorldPosition,
        Vector3 secondWorldPosition,
        bool isValid,
        bool commitPair = false)
    {
        previewFirstCoordinate = firstCoordinate;
        previewSecondCoordinate = secondCoordinate;
        previewPairConfigured = firstCoordinate != secondCoordinate;
        previewPairCommitted = previewPairConfigured && commitPair && isValid;
        if (!hasInitialPreviewRootRotation)
        {
            initialPreviewRootRotation = transform.rotation;
            hasInitialPreviewRootRotation = true;
        }

        if (!previewPairConfigured)
        {
            transform.SetPositionAndRotation(firstWorldPosition, initialPreviewRootRotation);
            SetSecondaryEndpointVisible(false);
            SetPreviewRouteVisible(false);
            return;
        }

        if (!isValid)
        {
            transform.SetPositionAndRotation(firstWorldPosition, initialPreviewRootRotation);
            ApplyInvalidCandidateEndpointPose(firstWorldPosition, secondWorldPosition);
            SetPreviewRouteVisible(false);
            return;
        }

        ApplyEndpointPoses(firstWorldPosition, secondWorldPosition);
        ConfigurePreviewRoute(firstWorldPosition, secondWorldPosition);
    }

    public void ClearPreviewPair()
    {
        previewPairConfigured = false;
        previewPairCommitted = false;
        previewFirstCoordinate = default;
        previewSecondCoordinate = default;
        initialPreviewRootRotation = transform.rotation;
        hasInitialPreviewRootRotation = true;
        SetSecondaryEndpointVisible(false);
        SetPreviewRouteVisible(false);
    }

    public static bool RouteOverlapsCollinearPair(
        Vector2Int firstCoordinate,
        Vector2Int secondCoordinate,
        UndergroundPipe ignoredPipe)
    {
        for (int i = ActivePipes.Count - 1; i >= 0; i--)
        {
            UndergroundPipe candidate = ActivePipes[i];
            if (candidate == null)
            {
                ActivePipes.RemoveAt(i);
                continue;
            }

            if (candidate == ignoredPipe
                || !candidate.TryGetPairCoordinates(out Vector2Int otherFirst, out Vector2Int otherSecond))
            {
                continue;
            }
            if (SegmentsOverlapCollinearly(
                    firstCoordinate,
                    secondCoordinate,
                    otherFirst,
                    otherSecond))
            {
                return true;
            }
        }

        return false;
    }

    public static bool SegmentsOverlapCollinearly(
        Vector2Int firstCoordinate,
        Vector2Int secondCoordinate,
        Vector2Int otherFirst,
        Vector2Int otherSecond)
    {
        bool firstHorizontal = firstCoordinate.y == secondCoordinate.y;
        bool otherHorizontal = otherFirst.y == otherSecond.y;
        if (firstHorizontal != otherHorizontal)
        {
            return false;
        }

        if (firstHorizontal)
        {
            return firstCoordinate.y == otherFirst.y
                   && RangesOverlap(
                       firstCoordinate.x,
                       secondCoordinate.x,
                       otherFirst.x,
                       otherSecond.x);
        }

        return firstCoordinate.x == otherFirst.x
               && RangesOverlap(
                   firstCoordinate.y,
                   secondCoordinate.y,
                   otherFirst.y,
                   otherSecond.y);
    }

    public static bool IsValidPairGeometry(
        Vector2Int firstCoordinate,
        Vector2Int secondCoordinate,
        int maxInclusiveDistance)
    {
        Vector2Int delta = secondCoordinate - firstCoordinate;
        if ((delta.x == 0) == (delta.y == 0))
        {
            return false;
        }

        int inclusiveDistance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y) + 1;
        return inclusiveDistance >= MinimumPairDistance
               && inclusiveDistance <= Mathf.Max(MinimumPairDistance, maxInclusiveDistance);
    }

    protected override void OnPlacementRuntimeChanged()
    {
        previewPairConfigured = false;
        previewPairCommitted = false;
        hasInitialPreviewRootRotation = false;
        base.OnPlacementRuntimeChanged();
        RefreshEndpointVisuals(false);
    }

    protected override void OnPlacementRuntimeCleared()
    {
        previewPairConfigured = false;
        previewPairCommitted = false;
        hasInitialPreviewRootRotation = false;
        SetSecondaryEndpointVisible(false);
        SetPreviewRouteVisible(false);
        base.OnPlacementRuntimeCleared();
    }

    public override void PrepareForPool()
    {
        previewPairConfigured = false;
        previewPairCommitted = false;
        hasInitialPreviewRootRotation = false;
        SetSecondaryEndpointVisible(false);
        SetPreviewRouteVisible(false);
        base.PrepareForPool();
    }

    private void RefreshEndpointVisuals(bool showRoute)
    {
        if (!TryGetPairCoordinates(out Vector2Int firstCoordinate, out Vector2Int secondCoordinate))
        {
            SetSecondaryEndpointVisible(false);
            SetPreviewRouteVisible(false);
            return;
        }

        Vector3 firstWorldPosition = ResolveCoordinateWorldPosition(firstCoordinate, transform.position.y);
        Vector3 secondWorldPosition = ResolveCoordinateWorldPosition(secondCoordinate, transform.position.y);
        ApplyEndpointPoses(firstWorldPosition, secondWorldPosition);
        if (showRoute)
        {
            ConfigurePreviewRoute(firstWorldPosition, secondWorldPosition);
        }
        else
        {
            SetPreviewRouteVisible(false);
        }
    }

    private void ApplyEndpointPoses(Vector3 firstWorldPosition, Vector3 secondWorldPosition)
    {
        Vector3 tunnelDirection = secondWorldPosition - firstWorldPosition;
        tunnelDirection.y = 0f;
        if (tunnelDirection.sqrMagnitude <= 0.0001f)
        {
            SetSecondaryEndpointVisible(false);
            return;
        }

        Quaternion firstRotation = Quaternion.LookRotation(tunnelDirection.normalized, Vector3.up);
        transform.SetPositionAndRotation(firstWorldPosition, firstRotation);

        ApplySecondaryEndpointPose(
            secondWorldPosition,
            firstRotation * Quaternion.Euler(0f, 180f, 0f));
    }

    private void ApplyInvalidCandidateEndpointPose(
        Vector3 firstWorldPosition,
        Vector3 secondWorldPosition)
    {
        Vector3 tunnelDirection = secondWorldPosition - firstWorldPosition;
        tunnelDirection.y = 0f;
        if (tunnelDirection.sqrMagnitude <= 0.0001f)
        {
            SetSecondaryEndpointVisible(false);
            return;
        }

        Quaternion candidateFirstRotation = Quaternion.LookRotation(
            tunnelDirection.normalized,
            Vector3.up);
        ApplySecondaryEndpointPose(
            secondWorldPosition,
            candidateFirstRotation * Quaternion.Euler(0f, 180f, 0f));
    }

    private void ApplySecondaryEndpointPose(
        Vector3 secondWorldPosition,
        Quaternion exitRootRotation)
    {

        Transform exitVisual = EnsureSecondaryEndpointVisual();
        if (exitVisual == null)
        {
            return;
        }

        exitVisual.gameObject.SetActive(true);
        // The generated exit is a clone of Body, not another root object. Preserve
        // Body's prefab-local rotation and only reverse its facing around the root Y axis.
        // Assigning a root-style world rotation here loses Body's built-in roll.
        Transform primaryVisual = ResolvePrimaryEndpointVisual();
        Vector3 endpointLocalPosition = primaryVisual != null
            ? primaryVisual.localPosition
            : Vector3.zero;
        Quaternion endpointLocalRotation = primaryVisual != null
            ? primaryVisual.localRotation
            : Quaternion.identity;
        exitVisual.SetPositionAndRotation(
            secondWorldPosition + exitRootRotation * endpointLocalPosition,
            exitRootRotation * endpointLocalRotation);
    }

    private Transform EnsureSecondaryEndpointVisual()
    {
        if (secondaryEndpointVisual != null)
        {
            return secondaryEndpointVisual;
        }

        Transform existing = transform.Find(GeneratedExitVisualName);
        if (existing != null)
        {
            secondaryEndpointVisual = existing;
            return secondaryEndpointVisual;
        }

        Transform source = ResolvePrimaryEndpointVisual();
        if (source == null)
        {
            return null;
        }

        secondaryEndpointVisual = Instantiate(source.gameObject, transform).transform;
        secondaryEndpointVisual.name = GeneratedExitVisualName;
        RemoveNestedUndergroundPipeComponents(secondaryEndpointVisual.gameObject);
        return secondaryEndpointVisual;
    }

    private Transform ResolvePrimaryEndpointVisual()
    {
        if (primaryEndpointVisual != null)
        {
            return primaryEndpointVisual;
        }

        primaryEndpointVisual = transform.Find("Body");
        if (primaryEndpointVisual != null)
        {
            return primaryEndpointVisual;
        }

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null
                && child.name != GeneratedExitVisualName
                && child.name != GeneratedRouteVisualName)
            {
                primaryEndpointVisual = child;
                break;
            }
        }

        return primaryEndpointVisual;
    }

    private static void RemoveNestedUndergroundPipeComponents(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        UndergroundPipe[] nestedPipes = root.GetComponentsInChildren<UndergroundPipe>(true);
        for (int i = 0; i < nestedPipes.Length; i++)
        {
            if (nestedPipes[i] != null)
            {
                Destroy(nestedPipes[i]);
            }
        }
    }

    private void ConfigurePreviewRoute(Vector3 start, Vector3 end)
    {
        LineRenderer route = EnsurePreviewRouteRenderer();
        if (route == null)
        {
            return;
        }

        start.y += previewRouteHeight;
        end.y += previewRouteHeight;
        route.startColor = route.endColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        route.positionCount = 2;
        route.SetPosition(0, start);
        route.SetPosition(1, end);
        route.enabled = true;
    }

    private LineRenderer EnsurePreviewRouteRenderer()
    {
        if (previewRouteRenderer != null)
        {
            return previewRouteRenderer;
        }

        Transform existing = transform.Find(GeneratedRouteVisualName);
        GameObject routeObject = existing != null
            ? existing.gameObject
            : new GameObject(GeneratedRouteVisualName);
        routeObject.transform.SetParent(transform, false);
        previewRouteRenderer = routeObject.GetComponent<LineRenderer>();
        if (previewRouteRenderer == null)
        {
            previewRouteRenderer = routeObject.AddComponent<LineRenderer>();
        }

        previewRouteRenderer.useWorldSpace = true;
        previewRouteRenderer.alignment = LineAlignment.View;
        previewRouteRenderer.textureMode = LineTextureMode.Stretch;
        previewRouteRenderer.startWidth = previewRouteWidth;
        previewRouteRenderer.endWidth = previewRouteWidth;
        previewRouteRenderer.shadowCastingMode = ShadowCastingMode.Off;
        previewRouteRenderer.receiveShadows = false;
        if (previewRouteMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                previewRouteMaterial = new Material(shader)
                {
                    name = "UndergroundPipePreviewRoute",
                    hideFlags = HideFlags.DontSave
                };
            }
        }

        previewRouteRenderer.sharedMaterial = previewRouteMaterial;
        return previewRouteRenderer;
    }

    private void SetSecondaryEndpointVisible(bool visible)
    {
        if (secondaryEndpointVisual != null)
        {
            secondaryEndpointVisual.gameObject.SetActive(visible);
        }
    }

    private void SetPreviewRouteVisible(bool visible)
    {
        if (previewRouteRenderer != null)
        {
            previewRouteRenderer.enabled = visible;
        }
    }

    private static Vector2Int NormalizeCardinal(Vector2Int direction)
    {
        if (direction.x != 0 && direction.y == 0)
        {
            return new Vector2Int(direction.x > 0 ? 1 : -1, 0);
        }

        if (direction.y != 0 && direction.x == 0)
        {
            return new Vector2Int(0, direction.y > 0 ? 1 : -1);
        }

        return Vector2Int.zero;
    }

    private static bool RangesOverlap(int firstA, int firstB, int secondA, int secondB)
    {
        int firstMin = Mathf.Min(firstA, firstB);
        int firstMax = Mathf.Max(firstA, firstB);
        int secondMin = Mathf.Min(secondA, secondB);
        int secondMax = Mathf.Max(secondA, secondB);
        return firstMin <= secondMax && secondMin <= firstMax;
    }

    private static Vector3 ResolveCoordinateWorldPosition(Vector2Int coordinate, float fallbackY)
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain != null
            && terrain.TryGetLoadedBlock(coordinate, out Block block)
            && block != null)
        {
            return block.transform.position;
        }

        return new Vector3(coordinate.x, fallbackY, coordinate.y);
    }

    private ItemDefinition ResolveItemDefinition()
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        int itemId = ResolveItemId();
        return itemManager != null && itemManager.TryGetItemDefinitionById(itemId, out ItemDefinition definition)
            ? definition
            : null;
    }

    private void OnDestroy()
    {
        if (previewRouteMaterial != null)
        {
            Destroy(previewRouteMaterial);
            previewRouteMaterial = null;
        }
    }
}
