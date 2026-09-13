using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// One scene host per resource prefab; independent state lives in generation-checked array slots.
public sealed partial class ResourceTypeWorld : MonoBehaviour
{
    private const float GrowthBucketSize = 16f;
    private static readonly Dictionary<(TerrainGenerator Terrain, Resource Prefab), ResourceTypeWorld> Hosts =
        new Dictionary<(TerrainGenerator, Resource), ResourceTypeWorld>();
    private static readonly Dictionary<Collider, ResourceInstance> ColliderOwners = new Dictionary<Collider, ResourceInstance>();
    private readonly HashSet<ResourceInstance> instances = new HashSet<ResourceInstance>();
    private readonly Dictionary<Vector2Int, GrowthBucket> growthBuckets =
        new Dictionary<Vector2Int, GrowthBucket>();
    private readonly List<Part> parts = new List<Part>();
    private readonly List<ColliderPart> colliderParts = new List<ColliderPart>();
    private Resource prototype;
    internal PortableObject PortableTemplate { get; private set; }
    private Vector3 rootScale;
    private Vector3 bodyPosition;
    private Quaternion bodyRotation;
    private Vector3 bodyScale;
    private ResourceGrowthPresentation growthPresentation;

    internal sealed class GrowthBucket
    {
        internal readonly HashSet<ResourceInstance> Resources = new HashSet<ResourceInstance>();
        internal Bounds Bounds;
        internal bool HasBounds;

        internal void Add(ResourceInstance resource)
        {
            Resources.Add(resource);
            Bounds pointBounds = new Bounds(resource.WorldPosition, Vector3.one * 2f);
            if (!HasBounds)
            {
                Bounds = pointBounds;
                HasBounds = true;
            }
            else
            {
                Bounds.Encapsulate(pointBounds);
            }
        }
    }

    internal readonly struct Part
    {
        internal readonly Mesh Mesh;
        internal readonly Material[] Materials;
        internal readonly Matrix4x4 Local;
        internal readonly bool Body, Apple;
        internal readonly int Layer;
        internal readonly ShadowCastingMode Shadows;
        internal readonly bool ReceiveShadows;
        internal Part(MeshFilter filter, MeshRenderer renderer, Matrix4x4 local, bool body, bool apple)
        {
            Mesh = filter.sharedMesh;
            Materials = renderer.sharedMaterials;
            Local = local;
            Body = body;
            Apple = apple;
            Layer = renderer.gameObject.layer;
            Shadows = renderer.shadowCastingMode;
            ReceiveShadows = renderer.receiveShadows;
            foreach (Material material in Materials)
                if (material != null) material.enableInstancing = true;
        }
    }

    private readonly struct ColliderPart
    {
        internal readonly Collider Source;
        internal readonly Matrix4x4 Local;
        internal readonly bool Body;
        internal ColliderPart(Collider source, Matrix4x4 local, bool body)
        { Source = source; Local = local; Body = body; }
    }

    internal sealed class CollisionInstance
    {
        internal Collider Collider;
        internal Mesh Mesh;
        internal Vector3[] SourceVertices, Vertices;
        internal Matrix4x4 LastMatrix;
        internal bool HasMatrix;
    }

    public int ResourceCount => instances.Count;
    internal int PartCount => parts.Count;
    internal Resource Prototype => prototype;
    internal Resource.HarvestMode HarvestMode { get; private set; }
    internal TerrainGenerator Terrain { get; private set; }
    internal ResourceBatchRenderer BatchRenderer { get; private set; }

    internal static void AppendProfilerCounters()
    {
        int hostCount = 0, instanceCount = 0, growthBucketCount = 0;
        int growthVisibleBucketCount = 0, growthCandidateCount = 0;
        long remainingUnits = 0;
        ResourceBatchRenderer sharedBatchRenderer = null;
        foreach (ResourceTypeWorld host in Hosts.Values)
        {
            if (host == null) continue;
            hostCount++;
            sharedBatchRenderer ??= host.BatchRenderer;
            growthBucketCount += host.growthBuckets.Count;
            if (host.growthPresentation != null)
            {
                growthVisibleBucketCount += host.growthPresentation.LastVisibleBucketCount;
                growthCandidateCount += host.growthPresentation.LastCandidateCount;
            }
            foreach (ResourceInstance resource in host.instances)
            {
                if (resource == null || !resource.IsRuntimeActive) continue;
                instanceCount++;
                remainingUnits += resource.ResourceCount;
            }
        }
        MapObjectTickProfiler.AddRuntimeCounter("ResourceWorld", "TypeGameObjects", hostCount);
        MapObjectTickProfiler.AddRuntimeCounter("ResourceWorld", "ResourceInstances", instanceCount);
        MapObjectTickProfiler.AddRuntimeCounter("ResourceWorld", "RemainingResourceUnits", remainingUnits);
        MapObjectTickProfiler.AddRuntimeCounter("ResourceGrowth", "Buckets", growthBucketCount);
        MapObjectTickProfiler.AddRuntimeCounter("ResourceGrowth", "VisibleBuckets", growthVisibleBucketCount);
        MapObjectTickProfiler.AddRuntimeCounter("ResourceGrowth", "Candidates", growthCandidateCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "Registered",
            sharedBatchRenderer != null ? sharedBatchRenderer.RegisteredResourceCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "Batches",
            sharedBatchRenderer != null ? sharedBatchRenderer.ActiveBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "Matrices",
            sharedBatchRenderer != null ? sharedBatchRenderer.ActiveMatrixCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "VisibleBatches",
            sharedBatchRenderer != null ? sharedBatchRenderer.LastVisibleBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "CulledBatches",
            sharedBatchRenderer != null ? sharedBatchRenderer.LastCulledBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "SubmittedMatrices",
            sharedBatchRenderer != null ? sharedBatchRenderer.LastSubmittedMatrixCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "DrawCalls",
            sharedBatchRenderer != null ? sharedBatchRenderer.LastDrawCallCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "PendingAdds",
            sharedBatchRenderer != null ? sharedBatchRenderer.PendingAddCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "DirtyResources",
            sharedBatchRenderer != null ? sharedBatchRenderer.DirtyResourceCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "LastPendingAdds",
            sharedBatchRenderer != null ? sharedBatchRenderer.LastPendingAdds : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ResourceRender",
            "LastIncrementalUpdates",
            sharedBatchRenderer != null ? sharedBatchRenderer.LastDirtyResourceUpdates : 0);
    }

    public static ResourceInstance Spawn(TerrainGenerator terrain, Resource prefab, Vector3 position)
    {
        if (terrain == null || prefab == null) return null;
        var key = (terrain, prefab);
        if (!Hosts.TryGetValue(key, out ResourceTypeWorld host) || host == null)
        {
            GameObject root = new GameObject("Resources: " + prefab.name);
            root.transform.SetParent(terrain.transform, false);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            root.layer = prefab.gameObject.layer;
            host = root.AddComponent<ResourceTypeWorld>();
            host.Initialize(terrain, prefab);
            Hosts[key] = host;
        }

        ResourceHandle handle = host.Allocate(prefab, position);
        ResourceInstance resource = prefab is ProjectF.MapObjects.Tree
            ? new ProjectF.MapObjects.TreeInstance(host, handle) : new ResourceInstance(host, handle);
        host.slots[handle.Index] = resource;
        host.instances.Add(resource);
        host.AddGrowthResource(resource);
        resource.Activate();
        return resource;
    }

    private void Initialize(TerrainGenerator terrain, Resource source)
    {
        Terrain = terrain;
        prototype = source;
        HarvestMode = source.ResolvedHarvestMode;
        BatchRenderer = terrain.GetComponent<ResourceBatchRenderer>();
        if (BatchRenderer == null) BatchRenderer = terrain.gameObject.AddComponent<ResourceBatchRenderer>();
        if (source is ProjectF.MapObjects.Tree) growthPresentation = new ResourceGrowthPresentation();
        PortableTemplate = source.GetComponentInChildren<PortableObject>(true);
        Transform root = source.transform;
        Transform body = root.Find("Body") ?? root;
        Transform extra = root.Find("_ResourceBodyExtraRenderers");
        rootScale = root.localScale;
        bodyPosition = body == root ? Vector3.zero : body.localPosition;
        bodyRotation = body == root ? Quaternion.identity : body.localRotation;
        bodyScale = body == root ? Vector3.one : body.localScale;
        foreach (MeshRenderer renderer in source.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || renderer.GetComponentInParent<PortableObject>() != null)
                continue;
            bool apple = HasAppleAncestor(renderer.transform, root);
            if (!apple && !IsAuthoredActive(renderer.transform, root)) continue;
            Transform partRoot = renderer.transform.IsChildOf(body) ? body
                : extra != null && renderer.transform.IsChildOf(extra) ? extra : root;
            bool followsBody = partRoot == body || partRoot == extra;
            parts.Add(new Part(filter, renderer, LocalToRoot(renderer.transform, partRoot), followsBody, apple));
        }

        foreach (Collider collider in source.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || !IsAuthoredActive(collider.transform, root)
                || collider.GetComponentInParent<PortableObject>() != null) continue;
            Transform partRoot = collider.transform.IsChildOf(body) ? body
                : extra != null && collider.transform.IsChildOf(extra) ? extra : root;
            bool followsBody = partRoot == body || partRoot == extra;
            colliderParts.Add(new ColliderPart(collider, LocalToRoot(collider.transform, partRoot), followsBody));
        }
    }

    internal Matrix4x4 GetRootMatrix(ResourceInstance resource) => Matrix4x4.TRS(resource.WorldPosition, Quaternion.identity, rootScale);
    internal Matrix4x4 GetBodyMatrix(ResourceInstance resource) => GetRootMatrix(resource) * Matrix4x4.TRS(
        bodyPosition, bodyRotation * Quaternion.Euler(0f, resource.SharedYawDegrees, 0f), bodyScale * resource.SharedBodyScale);

    internal bool GetPart(ResourceInstance resource, int index, out Mesh mesh, out Material[] materials,
        out Matrix4x4 matrix, out Vector3 position, out int layer, out ShadowCastingMode shadows, out bool receiveShadows)
    {
        Part part = parts[index];
        mesh = part.Mesh;
        materials = part.Materials;
        matrix = (part.Body ? GetBodyMatrix(resource) : GetRootMatrix(resource)) * part.Local;
        position = matrix.MultiplyPoint3x4(Vector3.zero);
        layer = part.Layer;
        shadows = part.Shadows;
        receiveShadows = part.ReceiveShadows;
        return !part.Apple || resource is ProjectF.MapObjects.TreeInstance tree && tree.ShouldShowSharedApples;
    }

    internal Bounds GetBounds(ResourceInstance resource)
    {
        Bounds result = new Bounds(resource.WorldPosition, Vector3.one * 0.1f);
        bool found = false;
        for (int i = 0; i < parts.Count; i++)
        {
            if (!GetPart(resource, i, out Mesh mesh, out _, out Matrix4x4 matrix, out _, out _, out _, out _)) continue;
            Bounds bounds = TransformBounds(mesh.bounds, matrix);
            if (!found) { result = bounds; found = true; }
            else result.Encapsulate(bounds);
        }
        return result;
    }

    internal void UpdateColliders(ResourceInstance resource)
    {
        if (resource.SharedColliders == null)
        {
            resource.SharedColliders = new CollisionInstance[colliderParts.Count];
            for (int i = 0; i < colliderParts.Count; i++)
            {
                Collider source = colliderParts[i].Source;
                Collider collider = (Collider)gameObject.AddComponent(source.GetType());
                collider.sharedMaterial = source.sharedMaterial;
                collider.isTrigger = source.isTrigger;
                collider.enabled = false;
                ColliderOwners[collider] = resource;
                resource.SharedColliders[i] = new CollisionInstance { Collider = collider };
            }
        }
        bool visible = resource.IsRuntimeActive && resource.SharedBodyVisible
            && (!(resource is ProjectF.MapObjects.TreeInstance tree) || tree.Growth > 0.0001f);
        for (int i = 0; i < colliderParts.Count; i++)
        {
            CollisionInstance instance = resource.SharedColliders[i];
            ColliderPart part = colliderParts[i];
            Collider target = instance.Collider;
            if (target == null) continue;
            target.enabled = visible;
            if (!visible) continue;
            Matrix4x4 matrix = transform.worldToLocalMatrix * (part.Body ? GetBodyMatrix(resource) : GetRootMatrix(resource)) * part.Local;
            if (instance.HasMatrix && instance.LastMatrix == matrix) continue;
            instance.LastMatrix = matrix;
            instance.HasMatrix = true;
            Vector3 scale = new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude, matrix.GetColumn(2).magnitude);
            if (part.Source is SphereCollider sphere && target is SphereCollider targetSphere)
            {
                targetSphere.center = matrix.MultiplyPoint3x4(sphere.center);
                targetSphere.radius = sphere.radius * Mathf.Max(scale.x, scale.y, scale.z);
            }
            else if (part.Source is BoxCollider box && target is BoxCollider targetBox)
            {
                Bounds bounds = TransformBounds(new Bounds(box.center, box.size), matrix);
                targetBox.center = bounds.center;
                targetBox.size = bounds.size;
            }
            else if (part.Source is CapsuleCollider capsule && target is CapsuleCollider targetCapsule)
            {
                targetCapsule.center = matrix.MultiplyPoint3x4(capsule.center);
                targetCapsule.direction = capsule.direction;
                targetCapsule.height = capsule.height * scale[capsule.direction];
                targetCapsule.radius = capsule.radius * Mathf.Max(scale[(capsule.direction + 1) % 3], scale[(capsule.direction + 2) % 3]);
            }
            else if (part.Source is MeshCollider mesh && target is MeshCollider targetMesh && mesh.sharedMesh != null)
            {
                if (instance.Mesh == null)
                {
                    instance.Mesh = Instantiate(mesh.sharedMesh);
                    instance.Mesh.name = "Resource collision " + resource.SimulationId;
                    instance.SourceVertices = instance.Mesh.vertices;
                    instance.Vertices = new Vector3[instance.SourceVertices.Length];
                }
                for (int vertex = 0; vertex < instance.Vertices.Length; vertex++)
                    instance.Vertices[vertex] = matrix.MultiplyPoint3x4(instance.SourceVertices[vertex]);
                instance.Mesh.vertices = instance.Vertices;
                instance.Mesh.RecalculateBounds();
                targetMesh.convex = mesh.convex;
                targetMesh.sharedMesh = instance.Mesh;
            }
        }
    }

    internal void Remove(ResourceInstance resource)
    {
        instances.Remove(resource);
        RemoveGrowthResource(resource);
        Free(resource.Handle);
        if (resource.SharedColliders == null) return;
        foreach (CollisionInstance instance in resource.SharedColliders)
        {
            if (!ReferenceEquals(instance.Collider, null)) ColliderOwners.Remove(instance.Collider);
            if (instance.Collider != null)
            {
                instance.Collider.enabled = false;
                Destroy(instance.Collider);
            }
            if (instance.Mesh != null) Destroy(instance.Mesh);
        }
        resource.SharedColliders = null;
    }

    public static bool IsSharedCollider(Collider collider) => collider != null && ColliderOwners.ContainsKey(collider);

    public static IMapObjectTarget ResolveColliderTarget(Collider collider)
    {
        if (collider == null) return null;
        if (ColliderOwners.TryGetValue(collider, out ResourceInstance resource))
            return resource != null && resource.IsRuntimeActive ? resource : null;
        return collider.GetComponentInParent<MapObject>();
    }

    private void OnDestroy()
    {
        growthPresentation?.Dispose();
        var key = (Terrain, prototype);
        if (Hosts.TryGetValue(key, out ResourceTypeWorld host) && host == this) Hosts.Remove(key);
        while (instances.Count > 0)
        {
            ResourceInstance first = null;
            foreach (ResourceInstance resource in instances) { first = resource; break; }
            first.ReleaseRuntime();
        }
        instances.Clear();
        growthBuckets.Clear();
    }

    private void LateUpdate()
    {
        if (growthPresentation == null)
        {
            return;
        }

        using var sample = MapObjectTickProfiler.SampleNamed(
            "Render",
            "Resource Growth",
            "Resource Growth Presentation");
        growthPresentation.Render(growthBuckets, gameObject.layer);
    }

    private void AddGrowthResource(ResourceInstance resource)
    {
        if (growthPresentation == null || resource == null)
        {
            return;
        }

        Vector2Int key = ResolveGrowthBucketKey(resource.WorldPosition);
        if (!growthBuckets.TryGetValue(key, out GrowthBucket bucket))
        {
            bucket = new GrowthBucket();
            growthBuckets.Add(key, bucket);
        }

        bucket.Add(resource);
    }

    private void RemoveGrowthResource(ResourceInstance resource)
    {
        if (growthPresentation == null || resource == null)
        {
            return;
        }

        Vector2Int key = ResolveGrowthBucketKey(resource.WorldPosition);
        if (!growthBuckets.TryGetValue(key, out GrowthBucket bucket)
            || !bucket.Resources.Remove(resource))
        {
            return;
        }

        if (bucket.Resources.Count <= 0)
        {
            growthBuckets.Remove(key);
        }
    }

    private static Vector2Int ResolveGrowthBucketKey(Vector3 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt(position.x / GrowthBucketSize),
            Mathf.FloorToInt(position.z / GrowthBucketSize));
    }

    private static bool IsAuthoredActive(Transform node, Transform root)
    {
        for (; node != null && node != root; node = node.parent)
            if (!node.gameObject.activeSelf) return false;
        return true;
    }
    private static bool HasAppleAncestor(Transform node, Transform root)
    {
        for (; node != null && node != root; node = node.parent)
            if (string.Equals(node.name, "Apple", StringComparison.OrdinalIgnoreCase)
                || node.name.StartsWith("Apple (", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    internal static Matrix4x4 LocalToRoot(Transform node, Transform root)
    {
        Matrix4x4 matrix = Matrix4x4.identity;
        for (; node != null && node != root; node = node.parent)
            matrix = Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale) * matrix;
        return matrix;
    }
    internal static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
    {
        Vector3 x = matrix.MultiplyVector(Vector3.right * bounds.extents.x);
        Vector3 y = matrix.MultiplyVector(Vector3.up * bounds.extents.y);
        Vector3 z = matrix.MultiplyVector(Vector3.forward * bounds.extents.z);
        return new Bounds(matrix.MultiplyPoint3x4(bounds.center), new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2f);
    }
}
