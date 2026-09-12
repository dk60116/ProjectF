using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Data-only runtime representation of an installed pipe. Pipe prefabs remain immutable
/// definitions; installed pipes do not keep a scene GameObject or MonoBehaviour.
/// </summary>
public sealed class PipeRuntimeRecord
{
    private readonly Vector2Int[] occupiedCoordinates;
    private readonly Bounds[] focusBounds;

    internal PipeRuntimeRecord(
        BlockStateStore.InstallationSaveState state,
        Pipe prototype,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Vector3 worldScale,
        PipeWorld.VisualPart[] visualParts)
    {
        State = state != null ? state.Clone() : throw new ArgumentNullException(nameof(state));
        Prototype = prototype != null ? prototype : throw new ArgumentNullException(nameof(prototype));
        StorageKey = BlockStateStore.GetInstallationStorageKey(State);
        AnchorCoordinate = State.anchorCoordinate;
        QuarterTurns = ((State.quarterTurns % 4) + 4) % 4;
        PlacementSequence = State.placementSequence;
        ItemId = State.itemId;
        WorldPosition = worldPosition;
        WorldRotation = worldRotation;
        WorldScale = worldScale;
        VisualParts = visualParts ?? Array.Empty<PipeWorld.VisualPart>();
        occupiedCoordinates = State.occupiedCoordinates != null && State.occupiedCoordinates.Count > 0
            ? State.occupiedCoordinates.ToArray()
            : new[] { AnchorCoordinate };
        focusBounds = BuildFocusBounds();
    }

    public BlockStateStore.InstallationSaveState State { get; }
    public Pipe Prototype { get; }
    public Vector2Int StorageKey { get; }
    public Vector2Int AnchorCoordinate { get; }
    public int QuarterTurns { get; }
    public long PlacementSequence { get; }
    public int ItemId { get; }
    public Vector3 WorldPosition { get; }
    public Quaternion WorldRotation { get; }
    public Vector3 WorldScale { get; }
    public IReadOnlyList<Vector2Int> OccupiedCoordinates => occupiedCoordinates;
    public bool HasValidPrototype => Prototype != null;
    public bool IsUnderground => Prototype is UndergroundPipe;
    internal PipeWorld.VisualPart[] VisualParts { get; }
    internal int DisplayedFluidItemId { get; set; } = -1;

    public bool Covers(Vector2Int coordinate)
    {
        for (int i = 0; i < occupiedCoordinates.Length; i++)
        {
            if (occupiedCoordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasConnectionTowardsAt(Vector2Int coordinate, Vector2Int direction)
    {
        if (!IsUnderground)
        {
            return Prototype.HasConnectionTowardsAt(coordinate, WorldRotation, direction);
        }

        return TryGetUndergroundOutwardDirection(coordinate, out Vector2Int outward)
               && outward == direction;
    }

    public bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remoteCoordinate)
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

    public bool TryGetPairCoordinates(out Vector2Int first, out Vector2Int second)
    {
        if (IsUnderground && occupiedCoordinates.Length == 2)
        {
            first = occupiedCoordinates[0];
            second = occupiedCoordinates[1];
            Vector2Int delta = second - first;
            return delta != Vector2Int.zero && (delta.x == 0 || delta.y == 0);
        }

        first = default;
        second = default;
        return false;
    }

    public bool TryGetObjectInfoFluidInfo(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius,
        out float pressureLitersPerSecond,
        bool includePressure = true)
    {
        return Prototype.TryGetObjectInfoFluidInfoAtCoordinate(
            coordinate,
            out fluidItemId,
            out temperatureCelsius,
            out pressureLitersPerSecond,
            includePressure);
    }

    public bool TryGetConnectedFluidItemIdIgnoringStorageCoordinate(
        Vector2Int coordinate,
        Vector2Int ignoredStorageCoordinate,
        out int fluidItemId)
    {
        return Prototype.TryGetConnectedFluidItemIdIgnoringStorageCoordinateAt(
            coordinate,
            ignoredStorageCoordinate,
            out fluidItemId);
    }

    internal Matrix4x4 GetRootMatrix(int endpointIndex)
    {
        if (!IsUnderground || endpointIndex <= 0 || !TryGetPairCoordinates(out _, out Vector2Int second))
        {
            return Matrix4x4.TRS(WorldPosition, WorldRotation, WorldScale);
        }

        Vector3 secondPosition = new Vector3(second.x, WorldPosition.y, second.y);
        return Matrix4x4.TRS(
            secondPosition,
            WorldRotation * Quaternion.Euler(0f, 180f, 0f),
            WorldScale);
    }

    internal bool TryRaycast(Ray ray, float maxDistance, out Vector2Int coordinate, out float distance)
    {
        coordinate = default;
        distance = float.MaxValue;
        for (int endpoint = 0; endpoint < focusBounds.Length; endpoint++)
        {
            if (!focusBounds[endpoint].IntersectRay(ray, out float candidateDistance)
                || candidateDistance < 0f
                || candidateDistance > maxDistance
                || candidateDistance >= distance)
            {
                continue;
            }

            distance = candidateDistance;
            coordinate = IsUnderground && endpoint < occupiedCoordinates.Length
                ? occupiedCoordinates[endpoint]
                : AnchorCoordinate;
        }

        return distance < float.MaxValue;
    }

    internal void EncapsulateFocusHeight(ref float minimumY, ref float maximumY)
    {
        for (int i = 0; i < focusBounds.Length; i++)
        {
            minimumY = Mathf.Min(minimumY, focusBounds[i].min.y);
            maximumY = Mathf.Max(maximumY, focusBounds[i].max.y);
        }
    }

    private Bounds[] BuildFocusBounds()
    {
        int endpointCount = IsUnderground && TryGetPairCoordinates(out _, out _) ? 2 : 1;
        Bounds[] result = new Bounds[endpointCount];
        for (int endpoint = 0; endpoint < endpointCount; endpoint++)
        {
            Matrix4x4 rootMatrix = GetRootMatrix(endpoint);
            bool foundBody = false;
            Bounds endpointBounds = default;
            for (int i = 0; i < VisualParts.Length; i++)
            {
                PipeWorld.VisualPart part = VisualParts[i];
                if (part.IsFluid || part.Mesh == null)
                {
                    continue;
                }

                Bounds partBounds = TransformBounds(part.Mesh.bounds, rootMatrix * part.LocalToRoot);
                if (foundBody)
                {
                    endpointBounds.Encapsulate(partBounds);
                }
                else
                {
                    endpointBounds = partBounds;
                    foundBody = true;
                }
            }

            if (!foundBody)
            {
                Vector3 fallbackCenter = rootMatrix.MultiplyPoint3x4(new Vector3(0f, 0.3f, 0f));
                endpointBounds = new Bounds(fallbackCenter, new Vector3(0.9f, 0.7f, 0.9f));
            }

            // A very thin authored mesh should still be practical to point at without
            // recreating a Collider for every installed pipe.
            endpointBounds.Expand(0.06f);
            result[endpoint] = endpointBounds;
        }

        return result;
    }

    private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
    {
        Vector3 localExtents = localBounds.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(localExtents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, localExtents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, localExtents.z));
        Vector3 worldExtents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        return new Bounds(matrix.MultiplyPoint3x4(localBounds.center), worldExtents * 2f);
    }

    private bool TryGetUndergroundOutwardDirection(Vector2Int coordinate, out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        if (!TryGetPairCoordinates(out Vector2Int first, out Vector2Int second))
        {
            return false;
        }

        Vector2Int tunnel;
        if (coordinate == first)
        {
            tunnel = second - first;
        }
        else if (coordinate == second)
        {
            tunnel = first - second;
        }
        else
        {
            return false;
        }

        tunnel = new Vector2Int(Math.Sign(tunnel.x), Math.Sign(tunnel.y));
        direction = -tunnel;
        return true;
    }
}

/// <summary>
/// The single scene component that owns all installed pipe records and renders their child
/// meshes in instanced batches.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(999)]
public sealed class PipeWorld : MonoBehaviour
{
    internal readonly struct VisualPart
    {
        public VisualPart(
            Mesh mesh,
            Material material,
            Matrix4x4 localToRoot,
            int layer,
            int subMeshIndex,
            bool isFluid,
            ShadowCastingMode shadowCastingMode,
            bool receiveShadows,
            uint renderingLayerMask)
        {
            Mesh = mesh;
            Material = material;
            LocalToRoot = localToRoot;
            Layer = layer;
            SubMeshIndex = subMeshIndex;
            IsFluid = isFluid;
            ShadowCastingMode = shadowCastingMode;
            ReceiveShadows = receiveShadows;
            RenderingLayerMask = renderingLayerMask;
        }

        public Mesh Mesh { get; }
        public Material Material { get; }
        public Matrix4x4 LocalToRoot { get; }
        public int Layer { get; }
        public int SubMeshIndex { get; }
        public bool IsFluid { get; }
        public ShadowCastingMode ShadowCastingMode { get; }
        public bool ReceiveShadows { get; }
        public uint RenderingLayerMask { get; }
    }

    private sealed class BatchOwner : IVirtualRenderBatchOwner
    {
        public readonly List<VirtualRenderBatchEntry> Entries = new List<VirtualRenderBatchEntry>(512);
        public int BatchEntryCount => Entries.Count;

        public void UpdateBatchEntryMatrixIndex(int entryIndex, int matrixIndex)
        {
            if ((uint)entryIndex >= (uint)Entries.Count)
            {
                return;
            }

            VirtualRenderBatchEntry entry = Entries[entryIndex];
            entry.MatrixIndex = matrixIndex;
            Entries[entryIndex] = entry;
        }
    }

    private readonly struct FluidMaterialKey : IEquatable<FluidMaterialKey>
    {
        public FluidMaterialKey(Material source, int itemId)
        {
            Source = source;
            ItemId = itemId;
        }

        public readonly Material Source;
        public readonly int ItemId;

        public bool Equals(FluidMaterialKey other) => Source == other.Source && ItemId == other.ItemId;
        public override bool Equals(object obj) => obj is FluidMaterialKey other && Equals(other);
        public override int GetHashCode() => ((Source != null ? Source.GetInstanceID() : 0) * 397) ^ ItemId;
    }

    private const string HostName = "PipeWorld";
    private const float BatchCellSize = 16f;
    private const float FluidRefreshInterval = 0.2f;
    private static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorShaderId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorShaderId = Shader.PropertyToID("_EmissionColor");
    private static PipeWorld current;

    private readonly Dictionary<Vector2Int, PipeRuntimeRecord> recordsByStorageKey =
        new Dictionary<Vector2Int, PipeRuntimeRecord>();
    private readonly Dictionary<Vector2Int, List<PipeRuntimeRecord>> recordsByCoordinate =
        new Dictionary<Vector2Int, List<PipeRuntimeRecord>>();
    private readonly Dictionary<Pipe, VisualPart[]> visualPartsByPrototype =
        new Dictionary<Pipe, VisualPart[]>();
    private readonly Dictionary<FluidMaterialKey, Material> fluidMaterials =
        new Dictionary<FluidMaterialKey, Material>();
    private readonly HashSet<PipeRuntimeRecord> raycastCandidates = new HashSet<PipeRuntimeRecord>();
    private readonly List<Material> materialScratch = new List<Material>(4);
    private readonly VirtualRenderBatchCollection bodyBatches = new VirtualRenderBatchCollection();
    private readonly VirtualRenderBatchCollection fluidBatches = new VirtualRenderBatchCollection();
    private readonly BatchOwner bodyOwner = new BatchOwner();
    private readonly BatchOwner fluidOwner = new BatchOwner();
    private Camera mainCamera;
    private bool bodyDirty = true;
    private bool fluidDirty = true;
    private float nextFluidRefreshTime;
    private float minimumFocusY;
    private float maximumFocusY;
    private bool focusHeightDirty = true;

    public static PipeWorld Current => current;
    public int InstalledPipeCount => recordsByStorageKey.Count;
    public int SceneGameObjectCount => 1;
    public int SceneMonoBehaviourCount => 1;
    public int BodyInstanceCount => bodyOwner.Entries.Count;
    public int FluidInstanceCount => fluidOwner.Entries.Count;
    public int EstimatedDrawCallCount =>
        bodyBatches.EstimatedDrawCallCount + fluidBatches.EstimatedDrawCallCount;

    public static void AppendProfilerCounters()
    {
        PipeWorld world = Current;
        if (world == null)
        {
            return;
        }

        MapObjectTickProfiler.AddRuntimeCounter("PipeWorld", "GameObjects", 1);
        MapObjectTickProfiler.AddRuntimeCounter("PipeWorld", "MonoBehaviours", 1);
        MapObjectTickProfiler.AddRuntimeCounter("PipeWorld", "Entities", world.InstalledPipeCount);
        MapObjectTickProfiler.AddRuntimeCounter("PipeWorld", "BodyMatrices", world.BodyInstanceCount);
        MapObjectTickProfiler.AddRuntimeCounter("PipeWorld", "FluidMatrices", world.FluidInstanceCount);
        MapObjectTickProfiler.AddRuntimeCounter("PipeWorld", "EstimatedDrawCalls", world.EstimatedDrawCallCount);
    }

    public static PipeWorld EnsureFor(TerrainGenerator terrain)
    {
        if (terrain == null)
        {
            return null;
        }

        if (current != null && current.transform.parent == terrain.transform)
        {
            return current;
        }

        Transform child = terrain.transform.Find(HostName);
        GameObject host = child != null ? child.gameObject : new GameObject(HostName);
        if (child == null)
        {
            host.transform.SetParent(terrain.transform, false);
        }

        current = host.GetComponent<PipeWorld>();
        if (current == null)
        {
            current = host.AddComponent<PipeWorld>();
        }

        return current;
    }

    public PipeRuntimeRecord Register(
        BlockStateStore.InstallationSaveState state,
        Pipe prototype,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Vector3 worldScale)
    {
        if (state == null || prototype == null || prototype.gameObject.scene.IsValid())
        {
            return null;
        }

        Vector2Int storageKey = BlockStateStore.GetInstallationStorageKey(state);
        Remove(storageKey);
        if (!visualPartsByPrototype.TryGetValue(prototype, out VisualPart[] visualParts))
        {
            visualParts = CaptureVisualParts(prototype);
            visualPartsByPrototype.Add(prototype, visualParts);
        }

        PipeRuntimeRecord record = new PipeRuntimeRecord(
            state,
            prototype,
            worldPosition,
            worldRotation,
            worldScale,
            visualParts);
        recordsByStorageKey.Add(storageKey, record);
        AddCoordinateMappings(record);
        InputOutputModule.NotifyRuntimePipeTopologyChanged(record.OccupiedCoordinates);
        Pipe.InvalidateFluidDisplayNetworkCache();
        bodyDirty = true;
        fluidDirty = true;
        focusHeightDirty = true;
        return record;
    }

    public bool Remove(Vector2Int storageKey)
    {
        if (!recordsByStorageKey.TryGetValue(storageKey, out PipeRuntimeRecord record))
        {
            return false;
        }

        recordsByStorageKey.Remove(storageKey);
        RemoveCoordinateMappings(record);
        InputOutputModule.NotifyRuntimePipeTopologyChanged(record.OccupiedCoordinates);
        Pipe.InvalidateFluidDisplayNetworkCache();
        bodyDirty = true;
        fluidDirty = true;
        focusHeightDirty = true;
        return true;
    }

    public void ClearRecords()
    {
        recordsByStorageKey.Clear();
        recordsByCoordinate.Clear();
        InputOutputModule.NotifyRuntimePipeTopologyChanged(null);
        Pipe.InvalidateFluidDisplayNetworkCache();
        bodyDirty = true;
        fluidDirty = true;
        focusHeightDirty = true;
    }

    public bool TryGetAtCoordinate(Vector2Int coordinate, out PipeRuntimeRecord record)
    {
        record = null;
        if (!recordsByCoordinate.TryGetValue(coordinate, out List<PipeRuntimeRecord> candidates))
        {
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            PipeRuntimeRecord candidate = candidates[i];
            if (candidate != null
                && candidate.HasValidPrototype
                && candidate.Covers(coordinate)
                && (record == null || candidate.PlacementSequence > record.PlacementSequence))
            {
                record = candidate;
            }
        }

        return record != null;
    }

    public bool TryGetMatchingAtCoordinate(Vector2Int coordinate, Pipe prototype, out PipeRuntimeRecord record)
    {
        if (TryGetAtCoordinate(coordinate, out record)
            && ReferenceEquals(record.Prototype, prototype))
        {
            return true;
        }

        record = null;
        return false;
    }

    public bool TryGetByStorageKey(Vector2Int storageKey, out PipeRuntimeRecord record)
    {
        return recordsByStorageKey.TryGetValue(storageKey, out record)
               && record != null
               && record.HasValidPrototype;
    }

    public bool TryRaycast(
        Ray ray,
        float maxDistance,
        out PipeRuntimeRecord record,
        out Vector2Int coordinate,
        out float distance)
    {
        record = null;
        coordinate = default;
        distance = float.MaxValue;
        if (recordsByStorageKey.Count == 0 || Mathf.Abs(ray.direction.y) < 0.0001f)
        {
            return false;
        }

        RefreshFocusHeightRange();
        float firstDistance = (minimumFocusY - ray.origin.y) / ray.direction.y;
        float secondDistance = (maximumFocusY - ray.origin.y) / ray.direction.y;
        float nearDistance = Mathf.Max(0f, Mathf.Min(firstDistance, secondDistance));
        float farDistance = Mathf.Min(maxDistance, Mathf.Max(firstDistance, secondDistance));
        if (farDistance < nearDistance)
        {
            return false;
        }

        Vector3 nearPoint = ray.GetPoint(nearDistance);
        Vector3 farPoint = ray.GetPoint(farDistance);
        int minimumX = Mathf.RoundToInt(Mathf.Min(nearPoint.x, farPoint.x)) - 1;
        int maximumX = Mathf.RoundToInt(Mathf.Max(nearPoint.x, farPoint.x)) + 1;
        int minimumZ = Mathf.RoundToInt(Mathf.Min(nearPoint.z, farPoint.z)) - 1;
        int maximumZ = Mathf.RoundToInt(Mathf.Max(nearPoint.z, farPoint.z)) + 1;

        raycastCandidates.Clear();
        for (int z = minimumZ; z <= maximumZ; z++)
        {
            for (int x = minimumX; x <= maximumX; x++)
            {
                if (!recordsByCoordinate.TryGetValue(new Vector2Int(x, z), out List<PipeRuntimeRecord> candidates))
                {
                    continue;
                }

                for (int i = 0; i < candidates.Count; i++)
                {
                    raycastCandidates.Add(candidates[i]);
                }
            }
        }

        foreach (PipeRuntimeRecord candidate in raycastCandidates)
        {
            if (candidate == null
                || !candidate.HasValidPrototype
                || !candidate.TryRaycast(ray, maxDistance, out Vector2Int candidateCoordinate, out float candidateDistance)
                || candidateDistance >= distance)
            {
                continue;
            }

            record = candidate;
            coordinate = candidateCoordinate;
            distance = candidateDistance;
        }

        raycastCandidates.Clear();
        return record != null;
    }

    public bool HasOverlappingUndergroundRoute(Vector2Int first, Vector2Int second)
    {
        foreach (PipeRuntimeRecord record in recordsByStorageKey.Values)
        {
            if (record != null
                && record.TryGetPairCoordinates(out Vector2Int otherFirst, out Vector2Int otherSecond)
                && UndergroundPipe.SegmentsOverlapCollinearly(
                    first,
                    second,
                    otherFirst,
                    otherSecond))
            {
                return true;
            }
        }

        return false;
    }

    private void Awake()
    {
        current = this;
    }

    private void OnDestroy()
    {
        if (current == this)
        {
            current = null;
        }

        bodyBatches.Dispose();
        fluidBatches.Dispose();
        foreach (Material material in fluidMaterials.Values)
        {
            DestroyRuntimeObject(material);
        }

        fluidMaterials.Clear();
    }

    private void LateUpdate()
    {
        using (MapObjectTickProfiler.SampleNamed("Runtime", nameof(Pipe), "Pipe Render Build"))
        {
            if (bodyDirty)
            {
                RebuildBodyBatches();
            }

            if (Time.unscaledTime >= nextFluidRefreshTime)
            {
                nextFluidRefreshTime = Time.unscaledTime + FluidRefreshInterval;
                RefreshFluidRecords();
            }

            if (fluidDirty)
            {
                RebuildFluidBatches();
            }
        }

        if (mainCamera == null || !mainCamera.isActiveAndEnabled)
        {
            mainCamera = Camera.main;
        }

        using (MapObjectTickProfiler.SampleNamed("Runtime", nameof(Pipe), "Pipe Render Submit"))
        {
            bodyBatches.RenderBatches(mainCamera);
            fluidBatches.RenderBatches(mainCamera);
        }
    }

    private void RebuildBodyBatches()
    {
        bodyDirty = false;
        bodyBatches.Clear();
        bodyOwner.Entries.Clear();
        foreach (PipeRuntimeRecord record in recordsByStorageKey.Values)
        {
            AddRecordParts(record, false, bodyBatches, bodyOwner);
        }
    }

    private void RebuildFluidBatches()
    {
        fluidDirty = false;
        fluidBatches.Clear();
        fluidOwner.Entries.Clear();
        foreach (PipeRuntimeRecord record in recordsByStorageKey.Values)
        {
            if (record.DisplayedFluidItemId >= 0)
            {
                AddRecordParts(record, true, fluidBatches, fluidOwner);
            }
        }
    }

    private void AddRecordParts(
        PipeRuntimeRecord record,
        bool fluid,
        VirtualRenderBatchCollection batches,
        BatchOwner owner)
    {
        if (record == null || !record.HasValidPrototype)
        {
            return;
        }

        int endpointCount = record.IsUnderground && record.TryGetPairCoordinates(out _, out _) ? 2 : 1;
        for (int endpoint = 0; endpoint < endpointCount; endpoint++)
        {
            Matrix4x4 rootMatrix = record.GetRootMatrix(endpoint);
            VisualPart[] parts = record.VisualParts;
            for (int i = 0; i < parts.Length; i++)
            {
                VisualPart part = parts[i];
                if (part.IsFluid != fluid || part.Mesh == null || part.Material == null)
                {
                    continue;
                }

                Material material = fluid
                    ? ResolveFluidMaterial(part.Material, record.DisplayedFluidItemId)
                    : part.Material;
                if (material == null)
                {
                    continue;
                }

                if (!material.enableInstancing)
                {
                    material.enableInstancing = true;
                }

                Matrix4x4 matrix = rootMatrix * part.LocalToRoot;
                Vector3 position = new Vector3(matrix.m03, matrix.m13, matrix.m23);
                VirtualRenderBatchKey key = new VirtualRenderBatchKey(
                    part.Mesh,
                    material,
                    part.Layer,
                    part.SubMeshIndex,
                    part.ShadowCastingMode,
                    part.ReceiveShadows,
                    false,
                    batchCellX: Mathf.FloorToInt(position.x / BatchCellSize),
                    batchCellZ: Mathf.FloorToInt(position.z / BatchCellSize),
                    renderingLayerMask: part.RenderingLayerMask);
                batches.AddOwnedMatrix(owner, owner.Entries, key, matrix);
            }
        }
    }

    private void RefreshFluidRecords()
    {
        foreach (PipeRuntimeRecord record in recordsByStorageKey.Values)
        {
            int nextItemId = record.Prototype.TryGetFluidDisplayItemIdAtCoordinate(
                record.AnchorCoordinate,
                out int fluidItemId)
                ? fluidItemId
                : -1;
            if (record.DisplayedFluidItemId == nextItemId)
            {
                continue;
            }

            record.DisplayedFluidItemId = nextItemId;
            fluidDirty = true;
        }
    }

    private Material ResolveFluidMaterial(Material source, int fluidItemId)
    {
        FluidMaterialKey key = new FluidMaterialKey(source, fluidItemId);
        if (fluidMaterials.TryGetValue(key, out Material material) && material != null)
        {
            return material;
        }

        material = new Material(source)
        {
            name = source.name + " (Pipe Fluid " + fluidItemId + ")",
            hideFlags = HideFlags.HideAndDontSave,
            enableInstancing = true
        };
        Color color = Pipe.ResolveFluidDisplayColor(fluidItemId);
        if (material.HasProperty(BaseColorShaderId)) material.SetColor(BaseColorShaderId, color);
        if (material.HasProperty(ColorShaderId)) material.SetColor(ColorShaderId, color);
        if (material.HasProperty(EmissionColorShaderId)) material.SetColor(EmissionColorShaderId, color);
        fluidMaterials.Add(key, material);
        return material;
    }

    private VisualPart[] CaptureVisualParts(Pipe source)
    {
        MeshRenderer[] renderers = source.GetComponentsInChildren<MeshRenderer>(true);
        List<VisualPart> parts = new List<VisualPart>(renderers.Length * 2);
        Transform root = source.transform;
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            MeshRenderer renderer = renderers[rendererIndex];
            MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            if (renderer == null || filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            bool fluid = renderer.name == "Fluid DP" || RendererUsesFluidMaterial(renderer);
            if (!fluid && !IsActiveRelativeToRoot(renderer.transform, root))
            {
                continue;
            }

            materialScratch.Clear();
            renderer.GetSharedMaterials(materialScratch);
            Matrix4x4 localToRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            int materialCount = Mathf.Min(materialScratch.Count, filter.sharedMesh.subMeshCount);
            for (int subMesh = 0; subMesh < materialCount; subMesh++)
            {
                Material material = materialScratch[subMesh];
                if (material != null)
                {
                    parts.Add(new VisualPart(
                        filter.sharedMesh,
                        material,
                        localToRoot,
                        renderer.gameObject.layer,
                        subMesh,
                        fluid,
                        renderer.shadowCastingMode,
                        renderer.receiveShadows,
                        renderer.renderingLayerMask));
                }
            }
        }

        return parts.ToArray();
    }

    private void AddCoordinateMappings(PipeRuntimeRecord record)
    {
        IReadOnlyList<Vector2Int> coordinates = record.OccupiedCoordinates;
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!recordsByCoordinate.TryGetValue(coordinate, out List<PipeRuntimeRecord> records))
            {
                records = new List<PipeRuntimeRecord>(1);
                recordsByCoordinate.Add(coordinate, records);
            }

            records.Add(record);
        }
    }

    private void RemoveCoordinateMappings(PipeRuntimeRecord record)
    {
        IReadOnlyList<Vector2Int> coordinates = record.OccupiedCoordinates;
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!recordsByCoordinate.TryGetValue(coordinate, out List<PipeRuntimeRecord> records))
            {
                continue;
            }

            records.Remove(record);
            if (records.Count == 0)
            {
                recordsByCoordinate.Remove(coordinate);
            }
        }
    }

    private void RefreshFocusHeightRange()
    {
        if (!focusHeightDirty)
        {
            return;
        }

        focusHeightDirty = false;
        minimumFocusY = float.MaxValue;
        maximumFocusY = float.MinValue;
        foreach (PipeRuntimeRecord record in recordsByStorageKey.Values)
        {
            record?.EncapsulateFocusHeight(ref minimumFocusY, ref maximumFocusY);
        }

        if (minimumFocusY == float.MaxValue)
        {
            minimumFocusY = transform.position.y;
            maximumFocusY = transform.position.y;
        }
    }

    private static bool RendererUsesFluidMaterial(Renderer renderer)
    {
        Material material = renderer != null ? renderer.sharedMaterial : null;
        return material != null && material.name == "M_Fluid";
    }

    private static bool IsActiveRelativeToRoot(Transform current, Transform root)
    {
        while (current != null && current != root)
        {
            if (!current.gameObject.activeSelf)
            {
                return false;
            }

            current = current.parent;
        }

        return current == root;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
    }
}
