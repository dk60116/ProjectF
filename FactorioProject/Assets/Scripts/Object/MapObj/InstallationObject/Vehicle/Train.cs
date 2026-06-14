using System.Collections.Generic;
using UnityEngine;

public class Train : Vehicle
{
    private const float DefaultAutoConnectDistance = 1.15f;
    private const float AutoConnectForwardMinDot = 0.55f;
    private const float ConnectionVisualBrightness = 1.8f;
    private const float ConnectionVisualMinBrightness = 0.42f;
    private const float ConnectionVisualContrast = 2.65f;
    private const float ConnectionVisualAlpha = 0.95f;
    private const float ConnectionVisualRimStrength = 0.8f;
    private const float ConnectionVisualRimPower = 2.2f;
    private static readonly Color[] ConnectionColorPalette =
    {
        new Color(0.18f, 0.72f, 1f, 0.95f),
        new Color(1f, 0.62f, 0.14f, 0.95f),
        new Color(0.42f, 0.9f, 0.36f, 0.95f),
        new Color(1f, 0.32f, 0.48f, 0.95f),
        new Color(0.72f, 0.45f, 1f, 0.95f),
        new Color(1f, 0.92f, 0.22f, 0.95f),
        new Color(0.15f, 0.9f, 0.78f, 0.95f),
        new Color(0.95f, 0.42f, 0.94f, 0.95f)
    };

    private static readonly HashSet<Train> ActiveTrains = new HashSet<Train>();
    private static readonly List<Train> ConnectionBuildList = new List<Train>(32);
    private static readonly Queue<Train> ConnectionQueue = new Queue<Train>(16);
    private static readonly int BlueprintPreviewPropertyId = Shader.PropertyToID("_BlueprintPreview");
    private static readonly int BlueprintTintPropertyId = Shader.PropertyToID("_BlueprintTint");
    private static readonly int BlueprintBrightnessPropertyId = Shader.PropertyToID("_BlueprintBrightness");
    private static readonly int BlueprintMinBrightnessPropertyId = Shader.PropertyToID("_BlueprintMinBrightness");
    private static readonly int BlueprintContrastPropertyId = Shader.PropertyToID("_BlueprintContrast");
    private static readonly int BlueprintAlphaPropertyId = Shader.PropertyToID("_BlueprintAlpha");
    private static readonly int BlueprintRimColorPropertyId = Shader.PropertyToID("_BlueprintRimColor");
    private static readonly int BlueprintRimStrengthPropertyId = Shader.PropertyToID("_BlueprintRimStrength");
    private static readonly int BlueprintRimPowerPropertyId = Shader.PropertyToID("_BlueprintRimPower");

    [SerializeField, Min(0.05f)]
    private float autoConnectDistance = DefaultAutoConnectDistance;

    private Rigidbody cachedTrainRigidbody;
    private Railload currentRail;
    private float currentRailDistance;
    private Vector2 currentRailTangent;
    private readonly List<Train> connectedTrains = new List<Train>(2);
    private Color blueprintConnectionColor = ConnectionColorPalette[0];
    private int connectionGroupSeed;
    private Renderer[] connectionColorRenderers;
    private MaterialPropertyBlock connectionColorPropertyBlock;
    private bool connectionVisualApplied;
    private Color connectionVisualColor;
    private bool connectionVisualOverrideActive;
    private Color connectionVisualOverrideColor;

    public IReadOnlyList<Train> ConnectedTrains => connectedTrains;
    public bool HasTrainConnections => connectedTrains.Count > 0;
    public Color BlueprintConnectionColor => blueprintConnectionColor;
    public int ConnectionGroupSeed => connectionGroupSeed;
    public float AutoConnectDistance => Mathf.Max(0.05f, autoConnectDistance);

    protected override void OnEnable()
    {
        base.OnEnable();
        ActiveTrains.Add(this);
        RefreshAllConnections();
    }

    protected override void OnDisable()
    {
        ActiveTrains.Remove(this);
        connectionVisualOverrideActive = false;
        ClearConnections();
        RefreshAllConnections();
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        ActiveTrains.Remove(this);
        connectionVisualOverrideActive = false;
        ClearConnections();
        currentRail = null;
        currentRailDistance = 0f;
        currentRailTangent = Vector2.zero;
        base.PrepareForPool();
    }

    protected override void OnPlacementRuntimeChanged()
    {
        base.OnPlacementRuntimeChanged();
        RefreshAllConnections();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        ClearConnections();
        base.OnPlacementRuntimeCleared();
        RefreshAllConnections();
    }

    public virtual void ApplyPlacedRailSample(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        if (facingTangent.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        facingTangent.Normalize();
        Vector3 position = transform.position;
        position.x = railPoint.x;
        position.z = railPoint.y;
        Quaternion rotation = Quaternion.LookRotation(
            new Vector3(facingTangent.x, 0f, facingTangent.y),
            Vector3.up);

        if (cachedTrainRigidbody == null)
        {
            cachedTrainRigidbody = GetComponent<Rigidbody>();
        }

        if (cachedTrainRigidbody != null)
        {
            cachedTrainRigidbody.position = position;
            cachedTrainRigidbody.rotation = rotation;
            cachedTrainRigidbody.velocity = Vector3.zero;
            cachedTrainRigidbody.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(position, rotation);
        SetCurrentRailSample(rail, distanceAlongPath, facingTangent);
        RefreshRuntimeCoordinate(position);
        RefreshAllConnections();
    }

    public bool TryGetCurrentRailSample(
        Vector2 currentPoint,
        float maxSqrDistance,
        out Railload rail,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        rail = null;
        distanceAlongPath = 0f;
        pathPoint = currentPoint;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (currentRail == null
            || !currentRail.TrySampleRenderedPath(currentRailDistance, out pathPoint, out tangent))
        {
            return false;
        }

        if (currentRailTangent.sqrMagnitude > 0.0001f
            && tangent.sqrMagnitude > 0.0001f
            && Vector2.Dot(tangent, currentRailTangent.normalized) < 0f)
        {
            tangent = -tangent;
        }

        sqrDistance = (currentPoint - pathPoint).sqrMagnitude;
        if (sqrDistance > maxSqrDistance)
        {
            return false;
        }

        rail = currentRail;
        distanceAlongPath = currentRailDistance;
        return true;
    }

    protected void SetCurrentRailSample(Railload rail, float distanceAlongPath, Vector2 tangent)
    {
        currentRail = rail;
        currentRailDistance = Mathf.Max(0f, distanceAlongPath);
        currentRailTangent = tangent;
    }

    public bool CanAutoConnectToPose(Vector3 otherPosition, Vector3 otherForward, float otherAutoConnectDistance)
    {
        if (!HasRuntimePlacement())
        {
            return false;
        }

        return CanAutoConnectTrainPoses(
            transform.position,
            transform.forward,
            AutoConnectDistance,
            otherPosition,
            otherForward,
            otherAutoConnectDistance);
    }

    public static bool CanAutoConnectTrainPoses(
        Vector3 firstPosition,
        Vector3 firstForward,
        float firstAutoConnectDistance,
        Vector3 secondPosition,
        Vector3 secondForward,
        float secondAutoConnectDistance)
    {
        float maxDistance = Mathf.Max(
            0.05f,
            Mathf.Min(
                Mathf.Max(0.05f, firstAutoConnectDistance),
                Mathf.Max(0.05f, secondAutoConnectDistance)));
        if (PlanarSqrDistance(firstPosition, secondPosition) > maxDistance * maxDistance)
        {
            return false;
        }

        Vector2 firstDirection = NormalizePlanarForward(firstForward);
        Vector2 secondDirection = NormalizePlanarForward(secondForward);
        if (firstDirection.sqrMagnitude <= 0.0001f || secondDirection.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        return Mathf.Abs(Vector2.Dot(firstDirection, secondDirection)) >= AutoConnectForwardMinDot;
    }

    public static bool TryGetAutoConnectColorNearPose(
        Vector3 position,
        Vector3 forward,
        float autoConnectDistance,
        out Color color)
    {
        return TryGetAutoConnectInfoNearPose(
            position,
            forward,
            autoConnectDistance,
            out color,
            out _,
            out _);
    }

    public static bool CollectAutoConnectTrainsNearPose(
        Vector3 position,
        Vector3 forward,
        float autoConnectDistance,
        ICollection<Train> results)
    {
        if (results == null)
        {
            return false;
        }

        bool addedAny = false;
        foreach (Train train in ActiveTrains)
        {
            if (train == null
                || !train.gameObject.activeInHierarchy
                || !train.HasRuntimePlacement()
                || !train.CanAutoConnectToPose(position, forward, autoConnectDistance)
                || results.Contains(train))
            {
                continue;
            }

            results.Add(train);
            addedAny = true;
        }

        return addedAny;
    }

    public static bool TryGetAutoConnectInfoNearPose(
        Vector3 position,
        Vector3 forward,
        float autoConnectDistance,
        out Color color,
        out int groupSeed,
        out float sqrDistance)
    {
        color = default;
        groupSeed = int.MaxValue;
        sqrDistance = float.MaxValue;
        Train bestTrain = null;
        foreach (Train train in ActiveTrains)
        {
            if (train == null
                || !train.gameObject.activeInHierarchy
                || !train.HasRuntimePlacement()
                || !train.CanAutoConnectToPose(position, forward, autoConnectDistance))
            {
                continue;
            }

            int candidateGroupSeed = train.ConnectionGroupSeed;
            float candidateSqrDistance = PlanarSqrDistance(position, train.transform.position);
            if (candidateGroupSeed > groupSeed
                || (candidateGroupSeed == groupSeed && candidateSqrDistance >= sqrDistance))
            {
                continue;
            }

            bestTrain = train;
            groupSeed = candidateGroupSeed;
            sqrDistance = candidateSqrDistance;
        }

        if (bestTrain == null)
        {
            return false;
        }

        color = bestTrain.BlueprintConnectionColor;
        return true;
    }

    public static Color GetConnectionColorForSeed(int seed)
    {
        int index = Mathf.Abs(seed) % ConnectionColorPalette.Length;
        return ConnectionColorPalette[index];
    }

    public static float GetDefaultAutoConnectDistance()
    {
        return DefaultAutoConnectDistance;
    }

    public void SetConnectionPreviewVisualOverride(Color color)
    {
        connectionVisualOverrideActive = true;
        connectionVisualOverrideColor = GetOpaqueConnectionColor(color);
        RefreshConnectionColorVisual();
    }

    public void ClearConnectionPreviewVisualOverride()
    {
        if (!connectionVisualOverrideActive)
        {
            return;
        }

        connectionVisualOverrideActive = false;
        RefreshConnectionColorVisual();
    }

    private bool CanAutoConnectTo(Train other)
    {
        if (other == null || other == this || !HasRuntimePlacement() || !other.HasRuntimePlacement())
        {
            return false;
        }

        if (!CanAutoConnectTrainPoses(
                transform.position,
                transform.forward,
                AutoConnectDistance,
                other.transform.position,
                other.transform.forward,
                other.AutoConnectDistance))
        {
            return false;
        }

        if (currentRail != null
            && currentRail == other.currentRail
            && Mathf.Abs(currentRailDistance - other.currentRailDistance) <= Mathf.Max(AutoConnectDistance, other.AutoConnectDistance))
        {
            return true;
        }

        if (currentRailTangent.sqrMagnitude <= 0.0001f || other.currentRailTangent.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        return Mathf.Abs(Vector2.Dot(currentRailTangent.normalized, other.currentRailTangent.normalized)) >= AutoConnectForwardMinDot;
    }

    private bool HasRuntimePlacement()
    {
        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        return occupiedCoordinates != null && occupiedCoordinates.Count > 0;
    }

    private void ClearConnections()
    {
        connectedTrains.Clear();
        connectionGroupSeed = 0;
        blueprintConnectionColor = ConnectionColorPalette[0];
        RefreshConnectionColorVisual();
    }

    private static void RefreshAllConnections()
    {
        ConnectionBuildList.Clear();
        foreach (Train train in ActiveTrains)
        {
            if (train == null)
            {
                continue;
            }

            if (!train.gameObject.activeInHierarchy || !train.HasRuntimePlacement())
            {
                train.ClearConnections();
                continue;
            }

            train.connectedTrains.Clear();
            train.connectionGroupSeed = Mathf.Max(1, (int)(train.RuntimePlacementSequence & 0x7fffffff));
            ConnectionBuildList.Add(train);
        }

        for (int i = 0; i < ConnectionBuildList.Count; i++)
        {
            Train first = ConnectionBuildList[i];
            for (int j = i + 1; j < ConnectionBuildList.Count; j++)
            {
                Train second = ConnectionBuildList[j];
                if (!first.CanAutoConnectTo(second))
                {
                    continue;
                }

                first.connectedTrains.Add(second);
                second.connectedTrains.Add(first);
            }
        }

        AssignConnectionGroupColors();
        ConnectionBuildList.Clear();
    }

    private static void AssignConnectionGroupColors()
    {
        HashSet<Train> visited = new HashSet<Train>();
        for (int i = 0; i < ConnectionBuildList.Count; i++)
        {
            Train root = ConnectionBuildList[i];
            if (root == null || !visited.Add(root))
            {
                continue;
            }

            int seed = root.connectionGroupSeed;
            ConnectionQueue.Clear();
            ConnectionQueue.Enqueue(root);
            while (ConnectionQueue.Count > 0)
            {
                Train current = ConnectionQueue.Dequeue();
                seed = Mathf.Min(seed, current.connectionGroupSeed);
                for (int connectionIndex = 0; connectionIndex < current.connectedTrains.Count; connectionIndex++)
                {
                    Train connected = current.connectedTrains[connectionIndex];
                    if (connected == null || !visited.Add(connected))
                    {
                        continue;
                    }

                    ConnectionQueue.Enqueue(connected);
                }
            }

            Color color = GetConnectionColorForSeed(seed);
            ApplyConnectionColor(root, seed, color);
        }
    }

    private static void ApplyConnectionColor(Train root, int seed, Color color)
    {
        if (root == null)
        {
            return;
        }

        HashSet<Train> visited = new HashSet<Train>();
        ConnectionQueue.Clear();
        ConnectionQueue.Enqueue(root);
        visited.Add(root);
        while (ConnectionQueue.Count > 0)
        {
            Train current = ConnectionQueue.Dequeue();
            current.connectionGroupSeed = seed;
            current.blueprintConnectionColor = color;
            current.RefreshConnectionColorVisual();
            for (int i = 0; i < current.connectedTrains.Count; i++)
            {
                Train connected = current.connectedTrains[i];
                if (connected == null || !visited.Add(connected))
                {
                    continue;
                }

                ConnectionQueue.Enqueue(connected);
            }
        }
    }

    private void RefreshConnectionColorVisual()
    {
        bool shouldApply = connectionVisualOverrideActive || (HasRuntimePlacement() && HasTrainConnections);
        Color targetColor = connectionVisualOverrideActive
            ? connectionVisualOverrideColor
            : shouldApply
                ? GetOpaqueConnectionColor(blueprintConnectionColor)
                : Color.white;
        if (connectionVisualApplied == shouldApply
            && (!shouldApply || connectionVisualColor == targetColor))
        {
            return;
        }

        EnsureConnectionColorRenderers();
        connectionColorPropertyBlock ??= new MaterialPropertyBlock();
        if (connectionColorRenderers != null)
        {
            for (int i = 0; i < connectionColorRenderers.Length; i++)
            {
                ApplyConnectionColorVisual(connectionColorRenderers[i], shouldApply, targetColor);
            }
        }

        connectionVisualApplied = shouldApply;
        connectionVisualColor = targetColor;
    }

    private void EnsureConnectionColorRenderers()
    {
        if (connectionColorRenderers == null || connectionColorRenderers.Length == 0)
        {
            connectionColorRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }

    private void ApplyConnectionColorVisual(Renderer renderer, bool enabled, Color color)
    {
        if (!ShouldApplyConnectionColorVisual(renderer))
        {
            return;
        }

        Material sharedMaterial = renderer.sharedMaterial;
        if (sharedMaterial == null)
        {
            return;
        }

        bool applied = false;
        connectionColorPropertyBlock.Clear();
        renderer.GetPropertyBlock(connectionColorPropertyBlock);

        if (sharedMaterial.HasProperty(BlueprintPreviewPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintPreviewPropertyId, enabled ? 1f : 0f);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintTintPropertyId))
        {
            connectionColorPropertyBlock.SetColor(BlueprintTintPropertyId, color);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintBrightnessPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintBrightnessPropertyId, ConnectionVisualBrightness);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintMinBrightnessPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintMinBrightnessPropertyId, ConnectionVisualMinBrightness);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintContrastPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintContrastPropertyId, ConnectionVisualContrast);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintAlphaPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintAlphaPropertyId, enabled ? ConnectionVisualAlpha : 1f);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintRimColorPropertyId))
        {
            connectionColorPropertyBlock.SetColor(BlueprintRimColorPropertyId, color);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintRimStrengthPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintRimStrengthPropertyId, enabled ? ConnectionVisualRimStrength : 0f);
            applied = true;
        }

        if (sharedMaterial.HasProperty(BlueprintRimPowerPropertyId))
        {
            connectionColorPropertyBlock.SetFloat(BlueprintRimPowerPropertyId, ConnectionVisualRimPower);
            applied = true;
        }

        if (applied)
        {
            renderer.SetPropertyBlock(connectionColorPropertyBlock);
        }
    }

    private static bool ShouldApplyConnectionColorVisual(Renderer renderer)
    {
        if (renderer == null
            || renderer is LineRenderer
            || renderer is ParticleSystemRenderer
            || renderer is SpriteRenderer
            || renderer.GetComponent<WorkableObjectRangeVisual>() != null
            || renderer.GetComponent<TMPro.TextMeshPro>() != null)
        {
            return false;
        }

        return renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
    }

    private static Color GetOpaqueConnectionColor(Color color)
    {
        return new Color(color.r, color.g, color.b, 1f);
    }

    private static Vector2 NormalizePlanarForward(Vector3 forward)
    {
        Vector2 direction = new Vector2(forward.x, forward.z);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
    }

    private static float PlanarSqrDistance(Vector3 first, Vector3 second)
    {
        float deltaX = first.x - second.x;
        float deltaZ = first.z - second.z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }

    private void RefreshRuntimeCoordinate(Vector3 worldPosition)
    {
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.z));
        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        if (occupiedCoordinates != null
            && occupiedCoordinates.Count == 1
            && occupiedCoordinates[0] == coordinate)
        {
            return;
        }

        ConfigurePlacementRuntime(
            coordinate,
            RuntimeQuarterTurns,
            new[] { coordinate },
            RuntimePlacementSequence);
    }
}
