using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One renderer on the placement controller; markers are data, never GameObjects.</summary>
public sealed class AreaMarkerRenderer : MonoBehaviour
{
    public static AreaMarkerRenderer Current { get; private set; }
    private const int ChunkSize = 32;
    private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("AreaMarker.Visibility");
    private static readonly ProfilerMarker RebuildMarker = new ProfilerMarker("AreaMarker.RebuildMeshes");
    private static readonly ProfilerMarker DrawMarker = new ProfilerMarker("AreaMarker.SubmitBatches");
    private readonly List<InputOutputModuleAreaMarkerController> owners = new List<InputOutputModuleAreaMarkerController>();
    private readonly Dictionary<InputOutputModuleAreaMarkerController, OwnerRegistration> ownerRegistrations =
        new Dictionary<InputOutputModuleAreaMarkerController, OwnerRegistration>();
    private readonly Dictionary<Vector2Int, List<InputOutputModuleAreaMarkerController>> ownersByCell =
        new Dictionary<Vector2Int, List<InputOutputModuleAreaMarkerController>>();
    private readonly HashSet<InputOutputModuleAreaMarkerController> continuousVisibilityOwners =
        new HashSet<InputOutputModuleAreaMarkerController>();
    private readonly HashSet<InputOutputModuleAreaMarkerController> visibleOwners =
        new HashSet<InputOutputModuleAreaMarkerController>();
    private readonly HashSet<InputOutputModuleAreaMarkerController> visibilityCandidateSet =
        new HashSet<InputOutputModuleAreaMarkerController>();
    private readonly List<InputOutputModuleAreaMarkerController> visibilityCandidates =
        new List<InputOutputModuleAreaMarkerController>();
    private readonly Dictionary<Sprite, SpriteGeometry> sprites = new Dictionary<Sprite, SpriteGeometry>();
    private readonly Dictionary<MaterialKey, Material> materials = new Dictionary<MaterialKey, Material>();
    private readonly Dictionary<BatchKey, MarkerBatch> batches = new Dictionary<BatchKey, MarkerBatch>();
    private readonly List<BatchKey> emptyBatches = new List<BatchKey>();
    private VisualLayer[] layers;
    private Material sourceMaterial;
    private bool staticDirty = true;
    private bool movingDirty = true;
    private RobotArmWorld lastArmWorld;
    private int registeredOwnerMarkerCount;
    private int visibleOwnerMarkerCount;
    private float maximumIndexedVisibleRange;
    private bool maximumIndexedVisibleRangeDirty;

    public int RegisteredMarkerCount { get; private set; }
    public int VisibleMarkerCount { get; private set; }
    public int VisibilityCandidateOwnerCount { get; private set; }
    public int IndexedOwnerCellCount => ownersByCell.Count;
    public int BatchCount => batches.Count;
    public int MeshRebuildCount { get; private set; }

    private void Awake()
    {
        Current = this;
    }

    public static void AppendProfilerCounters()
    {
        AreaMarkerRenderer renderer = Current;
        MapObjectTickProfiler.AddRuntimeCounter("AreaMarker", "RegisteredMarkers",
            renderer != null ? renderer.RegisteredMarkerCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("AreaMarker", "VisibleMarkers",
            renderer != null ? renderer.VisibleMarkerCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("AreaMarker", "VisibilityCandidateOwners",
            renderer != null ? renderer.VisibilityCandidateOwnerCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("AreaMarker", "IndexedOwnerCells",
            renderer != null ? renderer.IndexedOwnerCellCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("AreaMarker", "Batches",
            renderer != null ? renderer.BatchCount : 0);
    }

    internal void Register(InputOutputModuleAreaMarkerController owner)
    {
        if (owner == null || ownerRegistrations.ContainsKey(owner)) return;
        owners.Add(owner);
        OwnerRegistration registration = new OwnerRegistration(
            GetChunk(owner.VisibilityWorldPosition),
            owner.MarkerCount,
            owner.UsesMovingBatches);
        ownerRegistrations.Add(owner, registration);
        registeredOwnerMarkerCount += registration.MarkerCount;
        if (!registration.Moving)
        {
            AddOwnerToCell(registration.Cell, owner);
            maximumIndexedVisibleRange = Mathf.Max(maximumIndexedVisibleRange, owner.VisibleRange);
        }
        if (owner.RequiresContinuousVisibilityRefresh) continuousVisibilityOwners.Add(owner);
        MarkDirty(owner.UsesMovingBatches);
    }

    internal void Unregister(InputOutputModuleAreaMarkerController owner)
    {
        if (ReferenceEquals(owner, null)
            || !ownerRegistrations.TryGetValue(owner, out OwnerRegistration registration)) return;
        owners.Remove(owner);
        ownerRegistrations.Remove(owner);
        continuousVisibilityOwners.Remove(owner);
        visibilityCandidateSet.Remove(owner);
        visibleOwners.Remove(owner);
        registeredOwnerMarkerCount = Mathf.Max(0, registeredOwnerMarkerCount - registration.MarkerCount);
        if (registration.Visible)
            visibleOwnerMarkerCount = Mathf.Max(0, visibleOwnerMarkerCount - registration.MarkerCount);
        if (!registration.Moving)
        {
            RemoveOwnerFromCell(registration.Cell, owner);
            if (owner.VisibleRange >= maximumIndexedVisibleRange) maximumIndexedVisibleRangeDirty = true;
        }
        MarkDirty(registration.Moving);
    }

    internal void NotifyVisibilityOverrideChanged(InputOutputModuleAreaMarkerController owner)
    {
        if (owner == null || !ownerRegistrations.ContainsKey(owner)) return;
        if (owner.RequiresContinuousVisibilityRefresh) continuousVisibilityOwners.Add(owner);
        else continuousVisibilityOwners.Remove(owner);
    }

    private void MarkDirty(bool moving)
    {
        if (moving) movingDirty = true;
        else staticDirty = true;
    }

    private bool InitializeTemplate()
    {
        if (layers != null) return true;
        AreaMarker prefab = Resources.Load<AreaMarker>("Prefab/Enviroment/Block/AreaMarker");
        sourceMaterial = Resources.Load<Material>("Materials/AreaMarkerLateRender");
        if (prefab == null || sourceMaterial == null) return false;
        SpriteRenderer[] renderers = prefab.GetComponentsInChildren<SpriteRenderer>(true);
        System.Array.Sort(renderers, (a, b) => a.sortingOrder.CompareTo(b.sortingOrder));
        layers = new VisualLayer[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            layers[i] = new VisualLayer
            {
                Sprite = renderer.sprite,
                IsIcon = renderer == prefab.Icon,
                Color = renderer.color,
                Transform = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix,
                BeforeIconRotation = prefab.transform.worldToLocalMatrix
                    * (renderer.transform.parent != null ? renderer.transform.parent.localToWorldMatrix : Matrix4x4.identity)
                    * Matrix4x4.TRS(renderer.transform.localPosition, renderer.transform.localRotation, Vector3.one),
                Scale = renderer.transform.localScale,
                FlipX = renderer.flipX,
                FlipY = renderer.flipY,
                SortingOrder = renderer.sortingOrder,
                Layer = renderer.gameObject.layer
            };
        }
        return true;
    }

    private void LateUpdate()
    {
        using var sample = MapObjectTickProfiler.SampleNamed(
            "Render",
            "Area Markers",
            "Area Marker Render (inclusive)");
        if (!InitializeTemplate()) return;
        using (UpdateMarker.Auto())
        {
            AreaMarkerVisibilityContext context = AreaMarkerVisibilityContext.Capture();
            RobotArmWorld arms = RobotArmWorld.Current;
            if (!ReferenceEquals(lastArmWorld, arms)) { lastArmWorld = arms; staticDirty = true; }
            if (arms != null && arms.RefreshAreaMarkers(context)) staticDirty = true;
            RemoveInvalidOwners();
            BuildVisibilityCandidates(context);
            for (int i = 0; i < visibilityCandidates.Count; i++)
            {
                RefreshOwnerVisibility(visibilityCandidates[i], context);
            }
            VisibilityCandidateOwnerCount = visibilityCandidates.Count
                + (arms != null ? arms.MarkerVisibilityCandidateCount : 0);
            RegisteredMarkerCount = registeredOwnerMarkerCount + (arms != null ? arms.Count * 2 : 0);
            VisibleMarkerCount = visibleOwnerMarkerCount + (arms != null ? arms.VisibleMarkerCount : 0);
        }

        if (staticDirty || movingDirty)
        {
            using (RebuildMarker.Auto()) RebuildMeshes();
        }
        using (DrawMarker.Auto())
        {
            foreach (KeyValuePair<BatchKey, MarkerBatch> pair in batches)
            {
                MarkerBatch batch = pair.Value;
                RenderParams parameters = new RenderParams(batch.Material)
                {
                    layer = pair.Key.Material.Layer,
                    rendererPriority = pair.Key.Material.SortingOrder,
                    worldBounds = batch.Mesh.bounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion
                };
                Graphics.RenderMesh(parameters, batch.Mesh, 0, Matrix4x4.identity);
            }
        }
    }

    private void BuildVisibilityCandidates(in AreaMarkerVisibilityContext context)
    {
        visibilityCandidateSet.Clear();
        visibilityCandidates.Clear();
        if (context.ShowAll)
        {
            for (int i = 0; i < owners.Count; i++) AddVisibilityCandidate(owners[i]);
            return;
        }

        foreach (InputOutputModuleAreaMarkerController owner in visibleOwners)
            AddVisibilityCandidate(owner);
        foreach (InputOutputModuleAreaMarkerController owner in continuousVisibilityOwners)
            AddVisibilityCandidate(owner);
        if (!context.HasPlayer || ownersByCell.Count == 0) return;

        RefreshMaximumIndexedVisibleRange();
        Vector2Int center = GetChunk(context.PlayerPosition);
        int radius = Mathf.CeilToInt(Mathf.Max(0f, maximumIndexedVisibleRange) / ChunkSize);
        for (int z = center.y - radius; z <= center.y + radius; z++)
        {
            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                if (!ownersByCell.TryGetValue(
                        new Vector2Int(x, z),
                        out List<InputOutputModuleAreaMarkerController> cellOwners)) continue;
                for (int i = 0; i < cellOwners.Count; i++) AddVisibilityCandidate(cellOwners[i]);
            }
        }
    }

    private void AddVisibilityCandidate(InputOutputModuleAreaMarkerController owner)
    {
        if (owner != null && visibilityCandidateSet.Add(owner)) visibilityCandidates.Add(owner);
    }

    private void RefreshOwnerVisibility(
        InputOutputModuleAreaMarkerController owner,
        in AreaMarkerVisibilityContext context)
    {
        if (owner == null || !ownerRegistrations.TryGetValue(owner, out OwnerRegistration registration)) return;
        bool wasVisible = owner.IsVisible;
        if (owner.RefreshVisibility(context)) MarkDirty(registration.Moving);
        if (wasVisible == owner.IsVisible) return;
        registration.Visible = owner.IsVisible;
        ownerRegistrations[owner] = registration;
        if (owner.IsVisible)
        {
            visibleOwners.Add(owner);
            visibleOwnerMarkerCount += registration.MarkerCount;
        }
        else
        {
            visibleOwners.Remove(owner);
            visibleOwnerMarkerCount = Mathf.Max(0, visibleOwnerMarkerCount - registration.MarkerCount);
        }
    }

    private void RemoveInvalidOwners()
    {
        for (int i = owners.Count - 1; i >= 0; i--)
        {
            InputOutputModuleAreaMarkerController owner = owners[i];
            if (owner != null) continue;
            if (ownerRegistrations.TryGetValue(owner, out OwnerRegistration registration))
            {
                ownerRegistrations.Remove(owner);
                registeredOwnerMarkerCount = Mathf.Max(0, registeredOwnerMarkerCount - registration.MarkerCount);
                if (registration.Visible)
                    visibleOwnerMarkerCount = Mathf.Max(0, visibleOwnerMarkerCount - registration.MarkerCount);
                if (!registration.Moving) RemoveOwnerFromCell(registration.Cell, owner);
            }
            owners.RemoveAt(i);
            continuousVisibilityOwners.Remove(owner);
            visibleOwners.Remove(owner);
            staticDirty = movingDirty = true;
            maximumIndexedVisibleRangeDirty = true;
        }
    }

    private void AddOwnerToCell(Vector2Int cell, InputOutputModuleAreaMarkerController owner)
    {
        if (!ownersByCell.TryGetValue(cell, out List<InputOutputModuleAreaMarkerController> cellOwners))
        {
            cellOwners = new List<InputOutputModuleAreaMarkerController>(4);
            ownersByCell.Add(cell, cellOwners);
        }
        cellOwners.Add(owner);
    }

    private void RemoveOwnerFromCell(Vector2Int cell, InputOutputModuleAreaMarkerController owner)
    {
        if (!ownersByCell.TryGetValue(cell, out List<InputOutputModuleAreaMarkerController> cellOwners)) return;
        cellOwners.Remove(owner);
        if (cellOwners.Count == 0) ownersByCell.Remove(cell);
    }

    private void RefreshMaximumIndexedVisibleRange()
    {
        if (!maximumIndexedVisibleRangeDirty) return;
        maximumIndexedVisibleRange = 0f;
        foreach (KeyValuePair<InputOutputModuleAreaMarkerController, OwnerRegistration> pair in ownerRegistrations)
        {
            if (!pair.Value.Moving && pair.Key != null)
                maximumIndexedVisibleRange = Mathf.Max(maximumIndexedVisibleRange, pair.Key.VisibleRange);
        }
        maximumIndexedVisibleRangeDirty = false;
    }

    private static Vector2Int GetChunk(Vector3 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt(position.x / ChunkSize),
            Mathf.FloorToInt(position.z / ChunkSize));
    }

    private void RebuildMeshes()
    {
        foreach (KeyValuePair<BatchKey, MarkerBatch> pair in batches)
        {
            if (IsDirty(pair.Key.Moving)) pair.Value.Clear();
        }
        for (int i = 0; i < owners.Count; i++)
        {
            InputOutputModuleAreaMarkerController owner = owners[i];
            if (owner != null && owner.IsVisible && IsDirty(owner.UsesMovingBatches)) owner.AppendMarkers(this);
        }
        emptyBatches.Clear();
        if (staticDirty) RobotArmWorld.Current?.AppendAreaMarkers(this);
        foreach (KeyValuePair<BatchKey, MarkerBatch> pair in batches)
        {
            if (!IsDirty(pair.Key.Moving)) continue;
            if (pair.Value.VertexCount == 0)
            {
                pair.Value.Dispose();
                emptyBatches.Add(pair.Key);
            }
            else pair.Value.Upload();
        }
        for (int i = 0; i < emptyBatches.Count; i++) batches.Remove(emptyBatches[i]);
        staticDirty = movingDirty = false;
        MeshRebuildCount++;
    }

    private bool IsDirty(bool moving) => moving ? movingDirty : staticDirty;

    internal void Append(AreaMarkerSpawnRequest request, Matrix4x4 markerMatrix,
        int sortingOffset, bool onTop, bool moving)
    {
        Vector3 position = markerMatrix.MultiplyPoint3x4(Vector3.zero);
        Vector2Int chunk = new Vector2Int(Mathf.FloorToInt(position.x / ChunkSize), Mathf.FloorToInt(position.z / ChunkSize));
        for (int i = 0; i < layers.Length; i++)
        {
            VisualLayer layer = layers[i];
            Sprite sprite = layer.IsIcon ? request.Icon : layer.Sprite;
            if (sprite == null) continue;
            if (!sprites.TryGetValue(sprite, out SpriteGeometry geometry))
            {
                geometry = new SpriteGeometry(sprite);
                sprites.Add(sprite, geometry);
            }
            MaterialKey materialKey = new MaterialKey(sprite.texture, layer.SortingOrder + sortingOffset,
                layer.Layer, onTop);
            BatchKey key = new BatchKey(materialKey, chunk, moving);
            if (!batches.TryGetValue(key, out MarkerBatch batch))
            {
                if (!materials.TryGetValue(materialKey, out Material material))
                {
                    // Mesh submissions have no SpriteRenderer sortingOrder. Explicit queues keep
                    // template layers ordered, elevated station markers after ordinary markers,
                    // and depth-independent placement previews last (queue must stay <= 5000).
                    int queue = onTop ? 5000 - layers.Length + 1 + i
                        : 3000 + Mathf.Clamp(sortingOffset, 0, 1000) + i;
                    material = new Material(sourceMaterial)
                    {
                        name = "AreaMarkerBatch",
                        hideFlags = HideFlags.HideAndDontSave,
                        mainTexture = sprite.texture,
                        renderQueue = queue
                    };
                    material.SetInt("_ZTest", (int)(onTop ? CompareFunction.Always : CompareFunction.LessEqual));
                    materials.Add(materialKey, material);
                }
                batch = new MarkerBatch(material);
                batches.Add(key, batch);
            }
            Matrix4x4 matrix = markerMatrix * (layer.IsIcon && request.IconRotationZ != 0f
                ? layer.BeforeIconRotation * Matrix4x4.TRS(Vector3.zero,
                    Quaternion.Euler(0f, 0f, request.IconRotationZ), layer.Scale)
                : layer.Transform);
            batch.Append(geometry, matrix, layer.Color, layer.FlipX, layer.FlipY);
        }
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        foreach (MarkerBatch batch in batches.Values) batch.Dispose();
        foreach (Material material in materials.Values) ReleaseResource(material);
        batches.Clear();
        materials.Clear();
        sprites.Clear();
        owners.Clear();
        ownerRegistrations.Clear();
        ownersByCell.Clear();
        continuousVisibilityOwners.Clear();
        visibleOwners.Clear();
        visibilityCandidateSet.Clear();
        visibilityCandidates.Clear();
    }

    private static void ReleaseResource(Object resource)
    {
        if (Application.isPlaying) Destroy(resource);
        else DestroyImmediate(resource);
    }

    private struct VisualLayer
    {
        public Sprite Sprite;
        public bool IsIcon;
        public Color Color;
        public Matrix4x4 Transform;
        public Matrix4x4 BeforeIconRotation;
        public Vector3 Scale;
        public bool FlipX, FlipY;
        public int SortingOrder, Layer;
    }

    private struct OwnerRegistration
    {
        public readonly Vector2Int Cell;
        public readonly int MarkerCount;
        public readonly bool Moving;
        public bool Visible;

        public OwnerRegistration(Vector2Int cell, int markerCount, bool moving)
        {
            Cell = cell;
            MarkerCount = markerCount;
            Moving = moving;
            Visible = false;
        }
    }

    private sealed class SpriteGeometry
    {
        public readonly Vector2[] Vertices;
        public readonly Vector2[] UV;
        public readonly ushort[] Triangles;
        public SpriteGeometry(Sprite sprite)
        {
            // Cache once: these Sprite accessors return new arrays. Using the original geometry
            // also preserves tight meshes, atlas UVs and non-central sprite pivots.
            Vertices = sprite.vertices;
            UV = sprite.uv;
            Triangles = sprite.triangles;
        }
    }

    private readonly struct MaterialKey : System.IEquatable<MaterialKey>
    {
        public readonly Texture Texture;
        public readonly int SortingOrder, Layer;
        public readonly bool OnTop;
        public MaterialKey(Texture texture, int order, int layer, bool onTop)
        { Texture = texture; SortingOrder = order; Layer = layer; OnTop = onTop; }
        public bool Equals(MaterialKey other) => Texture == other.Texture && SortingOrder == other.SortingOrder
            && Layer == other.Layer && OnTop == other.OnTop;
        public override bool Equals(object obj) => obj is MaterialKey other && Equals(other);
        public override int GetHashCode() => unchecked(((Texture.GetHashCode() * 397 ^ SortingOrder) * 397 ^ Layer) * 397 ^ (OnTop ? 1 : 0));
    }

    private readonly struct BatchKey : System.IEquatable<BatchKey>
    {
        public readonly MaterialKey Material;
        public readonly Vector2Int Chunk;
        public readonly bool Moving;
        public BatchKey(MaterialKey material, Vector2Int chunk, bool moving)
        { Material = material; Chunk = chunk; Moving = moving; }
        public bool Equals(BatchKey other) => Material.Equals(other.Material) && Chunk == other.Chunk && Moving == other.Moving;
        public override bool Equals(object obj) => obj is BatchKey other && Equals(other);
        public override int GetHashCode() => unchecked((Material.GetHashCode() * 397 ^ Chunk.GetHashCode()) * 397 ^ (Moving ? 1 : 0));
    }

    private sealed class MarkerBatch
    {
        public readonly Mesh Mesh;
        public readonly Material Material;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();
        public int VertexCount => vertices.Count;

        public MarkerBatch(Material material)
        {
            Material = material;
            Mesh = new Mesh { name = "AreaMarkerBatch", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            Mesh.MarkDynamic();
        }
        public void Clear()
        {
            vertices.Clear(); uv.Clear(); colors.Clear(); triangles.Clear();
        }
        public void Append(SpriteGeometry geometry, Matrix4x4 matrix, Color color, bool flipX, bool flipY)
        {
            int start = vertices.Count;
            for (int i = 0; i < geometry.Vertices.Length; i++)
            {
                Vector2 vertex = geometry.Vertices[i];
                vertices.Add(matrix.MultiplyPoint3x4(new Vector3(flipX ? -vertex.x : vertex.x, flipY ? -vertex.y : vertex.y, 0f)));
                uv.Add(geometry.UV[i]);
                colors.Add(color);
            }
            for (int i = 0; i < geometry.Triangles.Length; i++) triangles.Add(start + geometry.Triangles[i]);
        }
        public void Upload()
        {
            Mesh.Clear();
            Mesh.SetVertices(vertices);
            Mesh.SetUVs(0, uv);
            Mesh.SetColors(colors);
            Mesh.SetTriangles(triangles, 0, true);
        }
        public void Dispose() => ReleaseResource(Mesh);
    }
}
