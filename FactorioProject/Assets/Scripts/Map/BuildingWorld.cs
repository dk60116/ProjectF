using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectF.Simulation;

/// <summary>
/// Data-only identity for an installed Building. Authoring prefabs stay immutable;
/// all installed buildings share BuildingWorldView for rendering and collision.
/// </summary>
public sealed class BuildingRuntimeRecord : IMapObjectTarget, IMapObjectSimulationIdentity,
    IFenceDoorTarget, IVirtualRenderBatchOwner
{
    private readonly Vector2Int[] occupiedCoordinates;
    private readonly Bounds focusBounds;
    private readonly List<VirtualRenderBatchEntry> batchEntries = new List<VirtualRenderBatchEntry>(4);
    private float currentDoorAngle;
    private float targetDoorAngle;
    private float doorAnimationStartAngle;
    private float doorAnimationElapsed;
    private bool isOpen;
    private BuildingWorld.BuildingTemplate previewTemplate;
    private Vector3 previewWorldPosition;
    private Quaternion previewWorldRotation;

    internal BuildingRuntimeRecord(
        BuildingWorld world,
        BlockStateStore.InstallationSaveState state,
        Building prototype,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Vector3 worldScale,
        BuildingWorld.BuildingTemplate template)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        State = state != null ? state.Clone() : throw new ArgumentNullException(nameof(state));
        Prototype = prototype != null ? prototype : throw new ArgumentNullException(nameof(prototype));
        Template = template ?? throw new ArgumentNullException(nameof(template));
        StorageKey = BlockStateStore.GetInstallationStorageKey(State);
        AnchorCoordinate = State.anchorCoordinate;
        QuarterTurns = ((State.quarterTurns % 4) + 4) % 4;
        PlacementSequence = State.placementSequence;
        WorldPosition = worldPosition;
        WorldRotation = worldRotation;
        WorldScale = worldScale;
        occupiedCoordinates = State.occupiedCoordinates != null && State.occupiedCoordinates.Count > 0
            ? State.occupiedCoordinates.ToArray()
            : new[] { AnchorCoordinate };
        focusBounds = BuildFocusBounds();
    }

    internal BuildingWorld World { get; }
    internal BuildingWorld.BuildingTemplate Template { get; }
    public BlockStateStore.InstallationSaveState State { get; }
    public Building Prototype { get; }
    public Vector2Int StorageKey { get; }
    public Vector2Int AnchorCoordinate { get; }
    public int QuarterTurns { get; }
    public long PlacementSequence { get; }
    public Vector3 WorldPosition { get; }
    public Quaternion WorldRotation { get; }
    public Vector3 WorldScale { get; }
    public IReadOnlyList<Vector2Int> OccupiedCoordinates => occupiedCoordinates;
    public bool IsRuntimeActive => World != null && World.Contains(this);
    public bool IsTargetActive => IsRuntimeActive;
    public MapObject SceneObject => null;
    public string ObjectName => Prototype.ObjectName;
    public bool AllowsFocus => Prototype.AllowsFocus;
    public bool AllowsAnimalTraversal => Template.IsDoor ? isOpen : Prototype.AllowsAnimalTraversal;
    public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
    public MapObject.MapObjectStatus Status => Prototype.Status;
    public ItemDefinition BoundItemDefinition => InputOutputModule.ResolveItemDefinition(State.itemId);
    public int ResolveItemId() => State.itemId;
    public int ResolvedItemId => ResolveItemId();
    public int ID => ResolveItemId();
    public long SimulationId => PlacementSequence;
    public bool IsOpen => isOpen;
    public bool IsDoor => Template.IsDoor;
    internal bool PlacementPresentationSuppressed { get; set; }
    internal float PlacementPresentationScale { get; set; } = 1f;
    internal bool HasPreviewVariant => previewTemplate != null;
    internal BuildingWorld.BuildingTemplate PresentationTemplate => previewTemplate ?? Template;
    internal Vector3 PresentationWorldPosition => previewTemplate != null ? previewWorldPosition : WorldPosition;
    internal Quaternion PresentationWorldRotation => previewTemplate != null ? previewWorldRotation : WorldRotation;
    internal List<VirtualRenderBatchEntry> BatchEntries => batchEntries;
    public int BatchEntryCount => batchEntries.Count;

    public void UpdateBatchEntryMatrixIndex(int entryIndex, int matrixIndex)
    {
        if ((uint)entryIndex >= (uint)batchEntries.Count) return;
        VirtualRenderBatchEntry entry = batchEntries[entryIndex];
        entry.MatrixIndex = matrixIndex;
        batchEntries[entryIndex] = entry;
    }

    public bool Covers(Vector2Int coordinate)
    {
        for (int i = 0; i < occupiedCoordinates.Length; i++)
        {
            if (occupiedCoordinates[i] == coordinate) return true;
        }

        return false;
    }

    public void ToggleOpenState(Vector3 interactorWorldPosition)
    {
        if (!Template.IsDoor || !IsRuntimeActive) return;
        SetOpenState(!isOpen, interactorWorldPosition);
    }

    internal void SetOpenState(bool value, Vector3 interactorWorldPosition)
    {
        if (!Template.IsDoor) return;
        if (value)
        {
            targetDoorAngle = ResolveOpenAngle(interactorWorldPosition);
        }
        else
        {
            targetDoorAngle = 0f;
        }

        if (isOpen == value && Mathf.Approximately(currentDoorAngle, targetDoorAngle)) return;
        isOpen = value;
        doorAnimationStartAngle = currentDoorAngle;
        doorAnimationElapsed = 0f;
        World.NotifyDoorStateChanged(this, true);
    }

    internal bool UpdateDoorPresentation(float deltaTime)
    {
        if (!Template.IsDoor || Mathf.Approximately(currentDoorAngle, targetDoorAngle)) return false;
        float duration = Mathf.Max(0.01f, Template.DoorTweenDuration);
        doorAnimationElapsed = Mathf.Min(duration, doorAnimationElapsed + Mathf.Max(0f, deltaTime));
        float t = doorAnimationElapsed / duration;
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        currentDoorAngle = Mathf.LerpUnclamped(doorAnimationStartAngle, targetDoorAngle, eased);
        if (doorAnimationElapsed >= duration) currentDoorAngle = targetDoorAngle;
        return true;
    }

    internal Matrix4x4 GetRootMatrix() => Matrix4x4.TRS(WorldPosition, WorldRotation, WorldScale);

    internal Matrix4x4 GetPresentationRootMatrix() => Matrix4x4.TRS(
        PresentationWorldPosition,
        PresentationWorldRotation,
        WorldScale * PlacementPresentationScale);

    internal void SetPreviewVariant(
        BuildingWorld.BuildingTemplate template,
        Vector3 worldPosition,
        Quaternion worldRotation)
    {
        previewTemplate = template;
        previewWorldPosition = worldPosition;
        previewWorldRotation = worldRotation;
    }

    internal bool PreviewVariantMatches(
        BuildingWorld.BuildingTemplate template,
        Vector3 worldPosition,
        Quaternion worldRotation)
    {
        return ReferenceEquals(previewTemplate, template)
               && (previewWorldPosition - worldPosition).sqrMagnitude <= 0.0001f
               && Mathf.Abs(Quaternion.Dot(previewWorldRotation, worldRotation)) >= 0.9999f;
    }

    internal void ClearPreviewVariant()
    {
        previewTemplate = null;
    }

    internal Matrix4x4 ApplyDoorLeafRotation(Matrix4x4 localToRoot)
    {
        if (!Template.IsDoor || Mathf.Approximately(currentDoorAngle, 0f)) return localToRoot;
        Matrix4x4 hinge = Template.HingeLocalToRoot;
        return hinge
               * Matrix4x4.Rotate(Quaternion.Euler(0f, currentDoorAngle, 0f))
               * hinge.inverse
               * localToRoot;
    }

    internal bool IncludeCollider(BuildingWorld.ColliderPart part)
    {
        return !part.IsDoorLeaf || !Template.DisableDoorColliderWhenOpen || !isOpen;
    }

    internal bool TryRaycast(Ray ray, float maxDistance, out float distance)
    {
        distance = float.MaxValue;
        if (!focusBounds.IntersectRay(ray, out float hitDistance)
            || hitDistance < 0f
            || hitDistance > maxDistance)
        {
            return false;
        }

        distance = hitDistance;
        return true;
    }

    internal void EncapsulateFocusHeight(ref float minimumY, ref float maximumY)
    {
        minimumY = Mathf.Min(minimumY, focusBounds.min.y);
        maximumY = Mathf.Max(maximumY, focusBounds.max.y);
    }

    private float ResolveOpenAngle(Vector3 interactorWorldPosition)
    {
        Matrix4x4 root = GetRootMatrix();
        Vector3 hingePosition = root.MultiplyPoint3x4(Template.HingeLocalToRoot.MultiplyPoint3x4(Vector3.zero));
        Vector3 toInteractor = interactorWorldPosition - hingePosition;
        toInteractor.y = 0f;
        if (toInteractor.sqrMagnitude <= 0.0001f) return targetDoorAngle == 0f ? 90f : targetDoorAngle;

        Vector3 doorForward = WorldRotation * Vector3.forward;
        doorForward.y = 0f;
        if (doorForward.sqrMagnitude <= 0.0001f) return targetDoorAngle == 0f ? 90f : targetDoorAngle;

        float direction = Vector3.Dot(toInteractor.normalized, doorForward.normalized) >= 0f ? -1f : 1f;
        if (Template.InvertDoorOpenDirection) direction *= -1f;
        return 90f * direction;
    }

    private Bounds BuildFocusBounds()
    {
        Matrix4x4 root = GetRootMatrix();
        bool found = false;
        Bounds result = default;
        BuildingWorld.VisualPart[] parts = Template.VisualParts;
        for (int i = 0; i < parts.Length; i++)
        {
            BuildingWorld.VisualPart part = parts[i];
            if (part.Mesh == null) continue;
            Bounds bounds = TransformBounds(part.Mesh.bounds, root * part.LocalToRoot);
            if (found) result.Encapsulate(bounds); else { result = bounds; found = true; }
        }

        if (!found)
        {
            result = new Bounds(WorldPosition + Vector3.up * 0.5f, new Vector3(0.9f, 1f, 0.9f));
        }

        result.Expand(0.06f);
        return result;
    }

    private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
    {
        Vector3 extents = localBounds.extents;
        Vector3 x = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 y = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 z = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
        Vector3 worldExtents = new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(matrix.MultiplyPoint3x4(localBounds.center), worldExtents * 2f);
    }
}

/// <summary>Owns all installed Building entities and their single scene presentation host.</summary>
public sealed class BuildingWorld : IDisposable
{
    internal readonly struct VisualPart
    {
        public VisualPart(Mesh mesh, Material material, Matrix4x4 localToRoot, int layer,
            int subMeshIndex, bool isDoorLeaf, ShadowCastingMode shadows, bool receiveShadows,
            uint renderingLayerMask)
        {
            Mesh = mesh;
            Material = material;
            LocalToRoot = localToRoot;
            Layer = layer;
            SubMeshIndex = subMeshIndex;
            IsDoorLeaf = isDoorLeaf;
            Shadows = shadows;
            ReceiveShadows = receiveShadows;
            RenderingLayerMask = renderingLayerMask;
        }

        public Mesh Mesh { get; }
        public Material Material { get; }
        public Matrix4x4 LocalToRoot { get; }
        public int Layer { get; }
        public int SubMeshIndex { get; }
        public bool IsDoorLeaf { get; }
        public ShadowCastingMode Shadows { get; }
        public bool ReceiveShadows { get; }
        public uint RenderingLayerMask { get; }
    }

    internal readonly struct ColliderPart
    {
        public ColliderPart(Matrix4x4 localToRoot, Vector3 center, Vector3 size, bool isDoorLeaf)
        {
            LocalToRoot = localToRoot;
            Center = center;
            Size = size;
            IsDoorLeaf = isDoorLeaf;
        }

        public Matrix4x4 LocalToRoot { get; }
        public Vector3 Center { get; }
        public Vector3 Size { get; }
        public bool IsDoorLeaf { get; }
    }

    internal sealed class BuildingTemplate
    {
        public BuildingTemplate(Building source, VisualPart[] visualParts, ColliderPart[] colliderParts,
            Matrix4x4 hingeLocalToRoot)
        {
            VisualParts = visualParts;
            ColliderParts = colliderParts;
            HingeLocalToRoot = hingeLocalToRoot;
            if (source is FenceDoor door)
            {
                IsDoor = true;
                DoorTweenDuration = door.HingeTweenDuration;
                DisableDoorColliderWhenOpen = door.DisableColliderWhenOpen;
                InvertDoorOpenDirection = door.InvertOpenDirection;
            }
        }

        public VisualPart[] VisualParts { get; }
        public ColliderPart[] ColliderParts { get; }
        public Matrix4x4 HingeLocalToRoot { get; }
        public bool IsDoor { get; }
        public float DoorTweenDuration { get; }
        public bool DisableDoorColliderWhenOpen { get; }
        public bool InvertDoorOpenDirection { get; }
    }

    private const string HostName = "BuildingWorld";
    private const float BatchCellSize = 16f;
    private static readonly int[] BoxTriangles =
    {
        0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
        0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
        2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
    };

    public static BuildingWorld Current { get; private set; }
    internal TerrainGenerator Owner { get; private set; }
    private readonly Dictionary<Vector2Int, BuildingRuntimeRecord> byStorageKey =
        new Dictionary<Vector2Int, BuildingRuntimeRecord>();
    private readonly Dictionary<Vector2Int, List<BuildingRuntimeRecord>> byCoordinate =
        new Dictionary<Vector2Int, List<BuildingRuntimeRecord>>();
    private readonly Dictionary<Building, BuildingTemplate> templates =
        new Dictionary<Building, BuildingTemplate>();
    private readonly List<Material> materialScratch = new List<Material>(4);
    private readonly HashSet<BuildingRuntimeRecord> raycastCandidates = new HashSet<BuildingRuntimeRecord>();
    private readonly List<BuildingRuntimeRecord> animatingDoors = new List<BuildingRuntimeRecord>();
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private BuildingWorldView view;
    private Camera mainCamera;
    private bool collisionDirty = true;
    private bool focusHeightDirty = true;
    private float minimumFocusY;
    private float maximumFocusY;
    private bool disposed;

    public int Count => byStorageKey.Count;
    public int SceneGameObjectCount => view != null ? 1 : 0;
    public int SceneMonoBehaviourCount => view != null ? 1 : 0;
    public int MatrixCount => batches.ActiveMatrixCount;

    public static BuildingWorld EnsureFor(TerrainGenerator terrain)
    {
        if (terrain == null) return null;
        if (Current != null && Current.Owner == terrain) return Current;
        Current?.Dispose();
        Current = new BuildingWorld { Owner = terrain };
        Current.AttachView();
        return Current;
    }

    public static void AppendProfilerCounters()
    {
        BuildingWorld world = Current;
        if (world == null) return;
        MapObjectTickProfiler.AddRuntimeCounter("BuildingWorld", "GameObjects", world.SceneGameObjectCount);
        MapObjectTickProfiler.AddRuntimeCounter("BuildingWorld", "MonoBehaviours", world.SceneMonoBehaviourCount);
        MapObjectTickProfiler.AddRuntimeCounter("BuildingWorld", "Entities", world.Count);
        MapObjectTickProfiler.AddRuntimeCounter("BuildingWorld", "Matrices", world.MatrixCount);
    }

    public void AttachView()
    {
        if (disposed) throw new ObjectDisposedException(nameof(BuildingWorld));
        if (view == null) view = BuildingWorldView.Create(this, Owner.transform, HostName);
    }

    internal void OnViewDestroyed(BuildingWorldView previous)
    {
        if (ReferenceEquals(view, previous)) view = null;
    }

    internal void SuspendRendering() => batches.SuspendRendering();

    public BuildingRuntimeRecord Register(BlockStateStore.InstallationSaveState state,
        Building prototype, Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale)
    {
        if (state == null || prototype == null || prototype.gameObject.scene.IsValid()) return null;
        Vector2Int key = BlockStateStore.GetInstallationStorageKey(state);
        Remove(key);
        if (!templates.TryGetValue(prototype, out BuildingTemplate template))
        {
            template = CaptureTemplate(prototype);
            templates.Add(prototype, template);
        }

        var record = new BuildingRuntimeRecord(this, state, prototype, worldPosition,
            worldRotation, worldScale, template);
        byStorageKey.Add(key, record);
        AddCoordinateMappings(record);
        AddRecordBatches(record);
        collisionDirty = true;
        focusHeightDirty = true;
        return record;
    }

    public bool Remove(Vector2Int storageKey)
    {
        if (!byStorageKey.TryGetValue(storageKey, out BuildingRuntimeRecord record)) return false;
        RemoveRecordBatches(record);
        byStorageKey.Remove(storageKey);
        RemoveCoordinateMappings(record);
        animatingDoors.Remove(record);
        ClearLoadedBlockBindings(record);
        collisionDirty = true;
        focusHeightDirty = true;
        return true;
    }

    public void ClearRecords()
    {
        foreach (BuildingRuntimeRecord record in byStorageKey.Values) ClearLoadedBlockBindings(record);
        batches.Clear();
        byStorageKey.Clear();
        byCoordinate.Clear();
        animatingDoors.Clear();
        collisionDirty = true;
        focusHeightDirty = true;
    }

    internal bool Contains(BuildingRuntimeRecord record)
    {
        return record != null
               && byStorageKey.TryGetValue(record.StorageKey, out BuildingRuntimeRecord current)
               && ReferenceEquals(current, record);
    }

    public bool TryGetAtCoordinate(Vector2Int coordinate, out BuildingRuntimeRecord record)
    {
        record = null;
        if (!byCoordinate.TryGetValue(coordinate, out List<BuildingRuntimeRecord> candidates)) return false;
        for (int i = 0; i < candidates.Count; i++)
        {
            BuildingRuntimeRecord candidate = candidates[i];
            if (candidate != null && candidate.IsRuntimeActive && candidate.Covers(coordinate)
                && (record == null || candidate.PlacementSequence > record.PlacementSequence))
            {
                record = candidate;
            }
        }

        return record != null;
    }

    public bool TryGetByStorageKey(Vector2Int key, out BuildingRuntimeRecord record) =>
        byStorageKey.TryGetValue(key, out record) && record != null;

    public bool TryRaycast(Ray ray, float maxDistance, out BuildingRuntimeRecord record, out float distance)
    {
        record = null;
        distance = float.MaxValue;
        if (byStorageKey.Count == 0 || Mathf.Abs(ray.direction.y) < 0.0001f) return false;
        RefreshFocusHeightRange();
        float first = (minimumFocusY - ray.origin.y) / ray.direction.y;
        float second = (maximumFocusY - ray.origin.y) / ray.direction.y;
        float near = Mathf.Max(0f, Mathf.Min(first, second));
        float far = Mathf.Min(maxDistance, Mathf.Max(first, second));
        if (far < near) return false;
        Vector3 nearPoint = ray.GetPoint(near);
        Vector3 farPoint = ray.GetPoint(far);
        int minX = Mathf.RoundToInt(Mathf.Min(nearPoint.x, farPoint.x)) - 1;
        int maxX = Mathf.RoundToInt(Mathf.Max(nearPoint.x, farPoint.x)) + 1;
        int minZ = Mathf.RoundToInt(Mathf.Min(nearPoint.z, farPoint.z)) - 1;
        int maxZ = Mathf.RoundToInt(Mathf.Max(nearPoint.z, farPoint.z)) + 1;
        raycastCandidates.Clear();
        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            if (!byCoordinate.TryGetValue(new Vector2Int(x, z), out List<BuildingRuntimeRecord> candidates)) continue;
            for (int i = 0; i < candidates.Count; i++) raycastCandidates.Add(candidates[i]);
        }

        foreach (BuildingRuntimeRecord candidate in raycastCandidates)
        {
            if (candidate != null && candidate.TryRaycast(ray, maxDistance, out float candidateDistance)
                && candidateDistance < distance)
            {
                record = candidate;
                distance = candidateDistance;
            }
        }

        raycastCandidates.Clear();
        return record != null;
    }

    internal void SetPlacementPresentationSuppressed(BuildingRuntimeRecord record, bool suppressed)
    {
        if (!Contains(record) || record.PlacementPresentationSuppressed == suppressed) return;
        record.PlacementPresentationSuppressed = suppressed;
        RefreshRecordBatches(record);
    }

    internal void SetPlacementPresentationScale(BuildingRuntimeRecord record, float scale)
    {
        scale = Mathf.Max(0f, scale);
        if (!Contains(record) || Mathf.Approximately(record.PlacementPresentationScale, scale)) return;
        record.PlacementPresentationScale = scale;
        RefreshRecordBatches(record);
    }

    internal void SetPreviewVariant(
        BuildingRuntimeRecord record,
        Building prototype,
        Vector3 worldPosition,
        Quaternion worldRotation)
    {
        if (!Contains(record) || prototype == null || prototype.gameObject.scene.IsValid()) return;
        if (!templates.TryGetValue(prototype, out BuildingTemplate template))
        {
            template = CaptureTemplate(prototype);
            templates.Add(prototype, template);
        }

        if (record.PreviewVariantMatches(template, worldPosition, worldRotation)) return;

        record.SetPreviewVariant(template, worldPosition, worldRotation);
        RefreshRecordBatches(record);
    }

    internal void ClearPreviewVariants(
        HashSet<Vector2Int> coordinates = null,
        HashSet<BuildingRuntimeRecord> preservedRecords = null)
    {
        foreach (BuildingRuntimeRecord record in byStorageKey.Values)
        {
            if (record == null
                || !record.HasPreviewVariant
                || coordinates != null && !coordinates.Contains(record.AnchorCoordinate)
                || preservedRecords != null && preservedRecords.Contains(record))
            {
                continue;
            }

            record.ClearPreviewVariant();
            RefreshRecordBatches(record);
        }
    }

    internal void AppendPreviewVariantAnchors(HashSet<Vector2Int> coordinates)
    {
        if (coordinates == null) return;
        foreach (BuildingRuntimeRecord record in byStorageKey.Values)
        {
            if (record != null && record.HasPreviewVariant) coordinates.Add(record.AnchorCoordinate);
        }
    }

    internal void NotifyDoorStateChanged(BuildingRuntimeRecord record, bool animate)
    {
        if (!Contains(record)) return;
        if (animate && !animatingDoors.Contains(record)) animatingDoors.Add(record);
        RefreshRecordBatches(record);
        collisionDirty = true;
        Owner?.InvalidateAnimalNavigation();
    }

    internal void Render(float deltaTime)
    {
        if (MapObjectTickManager.WaitingForWorldLoad)
        {
            batches.SuspendRendering();
            return;
        }

        for (int i = animatingDoors.Count - 1; i >= 0; i--)
        {
            BuildingRuntimeRecord record = animatingDoors[i];
            if (!Contains(record) || !record.UpdateDoorPresentation(deltaTime))
            {
                animatingDoors.RemoveAt(i);
                continue;
            }

            RefreshRecordBatches(record);
            collisionDirty = true;
        }

        if (collisionDirty)
        {
            collisionDirty = false;
            view?.RebuildCollision();
        }

        if (mainCamera == null || !mainCamera.isActiveAndEnabled) mainCamera = Camera.main;
        batches.RenderBatches(mainCamera, BatchCellSize);
    }

    internal void AppendCollisionGeometry(Matrix4x4 worldToView,
        List<Vector3> vertices, List<int> triangles)
    {
        foreach (BuildingRuntimeRecord record in byStorageKey.Values)
        {
            if (record == null || record.PlacementPresentationSuppressed) continue;
            Matrix4x4 root = record.GetRootMatrix();
            ColliderPart[] parts = record.Template.ColliderParts;
            for (int i = 0; i < parts.Length; i++)
            {
                ColliderPart part = parts[i];
                if (!record.IncludeCollider(part)) continue;
                Matrix4x4 local = part.IsDoorLeaf
                    ? record.ApplyDoorLeafRotation(part.LocalToRoot)
                    : part.LocalToRoot;
                AppendBox(worldToView * root * local, part.Center, part.Size, vertices, triangles);
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ClearRecords();
        BuildingWorldView previous = view;
        view = null;
        previous?.Release();
        batches.Dispose();
        if (ReferenceEquals(Current, this)) Current = null;
    }

    private void AddRecordBatches(BuildingRuntimeRecord record)
    {
        if (record == null || record.PlacementPresentationSuppressed) return;
        Matrix4x4 root = record.GetPresentationRootMatrix();
        VisualPart[] parts = record.PresentationTemplate.VisualParts;
        for (int i = 0; i < parts.Length; i++)
        {
            VisualPart part = parts[i];
            if (part.Mesh == null || part.Material == null) continue;
            if (!part.Material.enableInstancing) part.Material.enableInstancing = true;
            Matrix4x4 local = part.IsDoorLeaf
                ? record.ApplyDoorLeafRotation(part.LocalToRoot)
                : part.LocalToRoot;
            Matrix4x4 matrix = root * local;
            Vector3 position = new Vector3(matrix.m03, matrix.m13, matrix.m23);
            var key = new VirtualRenderBatchKey(part.Mesh, part.Material, part.Layer,
                part.SubMeshIndex, part.Shadows, part.ReceiveShadows, false,
                batchCellX: Mathf.FloorToInt(position.x / BatchCellSize),
                batchCellZ: Mathf.FloorToInt(position.z / BatchCellSize),
                invertCulling: matrix.determinant < 0f,
                renderingLayerMask: part.RenderingLayerMask);
            batches.AddOwnedMatrix(record, record.BatchEntries, key, matrix);
        }
    }

    private void RemoveRecordBatches(BuildingRuntimeRecord record)
    {
        if (record == null || record.BatchEntries.Count == 0) return;
        batches.RemoveOwnedEntries(record.BatchEntries);
    }

    private void RefreshRecordBatches(BuildingRuntimeRecord record)
    {
        RemoveRecordBatches(record);
        AddRecordBatches(record);
    }

    private BuildingTemplate CaptureTemplate(Building source)
    {
        Transform root = source.transform;
        Transform hinge = source is FenceDoor door ? door.HingeTransform : null;
        Matrix4x4 hingeLocalToRoot = hinge != null
            ? root.worldToLocalMatrix * hinge.localToWorldMatrix
            : Matrix4x4.identity;
        MeshRenderer[] renderers = source.GetComponentsInChildren<MeshRenderer>(true);
        var visualParts = new List<VisualPart>(renderers.Length * 2);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            MeshRenderer renderer = renderers[rendererIndex];
            MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            if (renderer == null || filter == null || filter.sharedMesh == null
                || !IsActiveRelativeToRoot(renderer.transform, root)) continue;
            materialScratch.Clear();
            renderer.GetSharedMaterials(materialScratch);
            Matrix4x4 localToRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            int count = Mathf.Min(materialScratch.Count, filter.sharedMesh.subMeshCount);
            bool doorLeaf = hinge != null && renderer.transform.IsChildOf(hinge);
            for (int subMesh = 0; subMesh < count; subMesh++)
            {
                Material material = materialScratch[subMesh];
                if (material == null) continue;
                visualParts.Add(new VisualPart(filter.sharedMesh, material, localToRoot,
                    renderer.gameObject.layer, subMesh, doorLeaf, renderer.shadowCastingMode,
                    renderer.receiveShadows, renderer.renderingLayerMask));
            }
        }

        BoxCollider[] colliders = source.GetComponentsInChildren<BoxCollider>(true);
        var colliderParts = new List<ColliderPart>(colliders.Length);
        for (int i = 0; i < colliders.Length; i++)
        {
            BoxCollider collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger
                || !IsActiveRelativeToRoot(collider.transform, root)) continue;
            colliderParts.Add(new ColliderPart(
                root.worldToLocalMatrix * collider.transform.localToWorldMatrix,
                collider.center, collider.size,
                hinge != null && collider.transform.IsChildOf(hinge)));
        }

        materialScratch.Clear();
        return new BuildingTemplate(source, visualParts.ToArray(), colliderParts.ToArray(), hingeLocalToRoot);
    }

    private void AddCoordinateMappings(BuildingRuntimeRecord record)
    {
        for (int i = 0; i < record.OccupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = record.OccupiedCoordinates[i];
            if (!byCoordinate.TryGetValue(coordinate, out List<BuildingRuntimeRecord> records))
            {
                records = new List<BuildingRuntimeRecord>(1);
                byCoordinate.Add(coordinate, records);
            }
            records.Add(record);
        }
    }

    private void RemoveCoordinateMappings(BuildingRuntimeRecord record)
    {
        for (int i = 0; i < record.OccupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = record.OccupiedCoordinates[i];
            if (!byCoordinate.TryGetValue(coordinate, out List<BuildingRuntimeRecord> records)) continue;
            records.Remove(record);
            if (records.Count == 0) byCoordinate.Remove(coordinate);
        }
    }

    private void ClearLoadedBlockBindings(BuildingRuntimeRecord record)
    {
        TerrainGenerator terrain = Owner;
        if (terrain == null || record == null) return;
        for (int i = 0; i < record.OccupiedCoordinates.Count; i++)
        {
            if (terrain.TryGetLoadedBlock(record.OccupiedCoordinates[i], out Block block)
                && block != null && ReferenceEquals(block.MapObject, record))
            {
                block.SetMapObject(null);
            }
        }
    }

    private void RefreshFocusHeightRange()
    {
        if (!focusHeightDirty) return;
        focusHeightDirty = false;
        minimumFocusY = float.MaxValue;
        maximumFocusY = float.MinValue;
        foreach (BuildingRuntimeRecord record in byStorageKey.Values)
            record?.EncapsulateFocusHeight(ref minimumFocusY, ref maximumFocusY);
        if (minimumFocusY == float.MaxValue)
        {
            minimumFocusY = maximumFocusY = Owner != null ? Owner.transform.position.y : 0f;
        }
    }

    private static bool IsActiveRelativeToRoot(Transform current, Transform root)
    {
        while (current != null && current != root)
        {
            if (!current.gameObject.activeSelf) return false;
            current = current.parent;
        }
        return current == root;
    }

    private static void AppendBox(Matrix4x4 matrix, Vector3 center, Vector3 size,
        List<Vector3> vertices, List<int> triangles)
    {
        int start = vertices.Count;
        Vector3 half = size * 0.5f;
        vertices.Add(matrix.MultiplyPoint3x4(center + new Vector3(-half.x, -half.y, -half.z)));
        vertices.Add(matrix.MultiplyPoint3x4(center + new Vector3( half.x, -half.y, -half.z)));
        vertices.Add(matrix.MultiplyPoint3x4(center + new Vector3( half.x, -half.y,  half.z)));
        vertices.Add(matrix.MultiplyPoint3x4(center + new Vector3(-half.x, -half.y,  half.z)));
        vertices.Add(matrix.MultiplyPoint3x4(center + new Vector3(-half.x,  half.y, -half.z)));
        vertices.Add(matrix.MultiplyPoint3x4(center + new Vector3( half.x,  half.y, -half.z)));
        vertices.Add(matrix.MultiplyPoint3x4(center + new Vector3( half.x,  half.y,  half.z)));
        vertices.Add(matrix.MultiplyPoint3x4(center + new Vector3(-half.x,  half.y,  half.z)));
        for (int i = 0; i < BoxTriangles.Length; i++) triangles.Add(start + BoxTriangles[i]);
    }
}
