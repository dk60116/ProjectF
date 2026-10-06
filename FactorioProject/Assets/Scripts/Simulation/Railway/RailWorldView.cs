using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectF.Railway
{
    // One presentation host, with static rail meshes grouped by spatial cell.
    // Placement changes rebuild only their cell; simulation never reads this view.
    public sealed class RailWorldView : MonoBehaviour
    {
        private const float CellSize = 32;
        private readonly Dictionary<Vector2Int, Cell> cells = new Dictionary<Vector2Int, Cell>();
        private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
        private readonly List<Vector3> railVertices = new List<Vector3>();
        private readonly List<int> railIndices = new List<int>();
        private readonly List<Vector3> sleeperVertices = new List<Vector3>();
        private readonly List<int> sleeperIndices = new List<int>();
        private readonly List<Vector3> path = new List<Vector3>();
        private readonly Dictionary<InstallationObject, StationTemplate> stationTemplates = new Dictionary<InstallationObject, StationTemplate>();
        private readonly Dictionary<TrainStationInstance, SphereCollider> stationColliders = new Dictionary<TrainStationInstance, SphereCollider>();
        private bool dirty;
        internal static RailWorldView Create(RailWorld world, TerrainGenerator terrain)
        {
            var host = new GameObject(nameof(RailWorldView)); host.transform.SetParent(terrain.transform, false);
            host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return host.AddComponent<RailWorldView>();
        }
        private static Vector2Int Key(RailwayInstance instance) => new Vector2Int(Mathf.FloorToInt(instance.WorldPosition.x / CellSize), Mathf.FloorToInt(instance.WorldPosition.z / CellSize));
        internal void Add(RailwayInstance instance)
        {
            var key = Key(instance);
            if (!cells.TryGetValue(key, out var cell)) cells.Add(key, cell = new Cell(key));
            cell.Instances.Add(instance); cell.Dirty = dirty = true;
        }
        internal void Remove(RailwayInstance instance)
        {
            if (cells.TryGetValue(Key(instance), out var cell)) { cell.Instances.Remove(instance); cell.Dirty = dirty = true; }
            if (instance is TrainStationInstance station && stationColliders.Remove(station, out var collider) && collider != null) Destroy(collider);
        }
        internal void Invalidate(RailwayInstance instance)
        {
            if (cells.TryGetValue(Key(instance), out var cell)) cell.Dirty = dirty = true;
            if (instance is TrainStationInstance station && stationColliders.TryGetValue(station, out var collider) && collider != null)
                collider.enabled = !station.PresentationSuppressed;
        }
        private void LateUpdate()
        {
            if (MapObjectTickManager.WaitingForWorldLoad) { batches.SuspendRendering(); return; }
            if (dirty) Rebuild();
            // Long paths can cross their anchor cell. Use actual batch bounds,
            // not the coarse anchor-cell index, to keep distant path ends visible.
            batches.RenderBatches(Camera.main);
        }
        private void Rebuild()
        {
            batches.Clear();
            foreach (var cell in cells.Values)
            {
                if (cell.Dirty)
                {
                    railVertices.Clear(); railIndices.Clear(); sleeperVertices.Clear(); sleeperIndices.Clear();
                    for (int i = 0; i < cell.Instances.Count; i++)
                        if (cell.Instances[i] is RailInstance rail && !rail.PresentationSuppressed) ((Railload)rail.Prototype).AppendDataVisual(rail, cell.Origin, railVertices, railIndices, sleeperVertices, sleeperIndices, path);
                    Apply(cell.Rails, railVertices, railIndices); Apply(cell.Sleepers, sleeperVertices, sleeperIndices);
                    cell.Dirty = false;
                }
                AddMesh(cell.Rails, Railload.DataRailMaterial, cell);
                AddMesh(cell.Sleepers, Railload.DataSleeperMaterial, cell);
                for (int i = 0; i < cell.Instances.Count; i++)
                    if (cell.Instances[i] is TrainStationInstance station && !station.PresentationSuppressed) AddStation(station, cell);
            }
            dirty = false;
        }
        private void AddMesh(Mesh mesh, Material material, Cell cell)
        {
            if (mesh.vertexCount == 0) return;
            batches.AddMatrix(new VirtualRenderBatchKey(mesh, material, gameObject.layer, 0, ShadowCastingMode.Off, false, false, batchCellX: cell.Key.x, batchCellZ: cell.Key.y), Matrix4x4.Translate(cell.Origin));
        }
        private void AddStation(TrainStationInstance station, Cell cell)
        {
            var prototype = station.Prototype;
            var root = Matrix4x4.TRS(station.WorldPosition, station.State.worldRotation, prototype.transform.localScale * station.PresentationScale);
            if (!stationTemplates.TryGetValue(prototype, out var template))
                stationTemplates.Add(prototype, template = new StationTemplate(prototype));
            for (int i = 0; i < template.Parts.Count; i++)
            {
                var part = template.Parts[i];
                var key = part.Key;
                batches.AddMatrix(new VirtualRenderBatchKey(key.Mesh, key.Material, key.Layer, key.SubmeshIndex,
                    key.ShadowCastingMode, key.ReceiveShadows, false, batchCellX: cell.Key.x, batchCellZ: cell.Key.y,
                    renderingLayerMask: key.RenderingLayerMask), root * part.Matrix);
            }
            if (template.Collider != null)
            {
                if (!stationColliders.TryGetValue(station, out var collider))
                {
                    collider = gameObject.AddComponent<SphereCollider>(); stationColliders.Add(station, collider);
                    var source = template.Collider;
                    collider.sharedMaterial = source.sharedMaterial; collider.isTrigger = source.isTrigger;
                    collider.includeLayers = source.includeLayers; collider.excludeLayers = source.excludeLayers;
                    collider.layerOverridePriority = source.layerOverridePriority; collider.providesContacts = source.providesContacts;
                }
                collider.center = root.MultiplyPoint3x4(template.ColliderMatrix.MultiplyPoint3x4(template.Collider.center));
                var scale = prototype.transform.localScale;
                collider.radius = template.Collider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                collider.enabled = template.Collider.enabled && !station.PresentationSuppressed;
            }
        }
        private static void Apply(Mesh mesh, List<Vector3> vertices, List<int> indices)
        { mesh.Clear(); mesh.indexFormat = IndexFormat.UInt32; mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); }
        internal void Release() { gameObject.SetActive(false); if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject); }
        private void OnDisable() => batches.SuspendRendering();
        private void OnDestroy()
        {
            batches.Dispose();
            foreach (var cell in cells.Values)
            {
                if (Application.isPlaying) { Destroy(cell.Rails); Destroy(cell.Sleepers); }
                else { DestroyImmediate(cell.Rails); DestroyImmediate(cell.Sleepers); }
            }
        }
        private sealed class Cell
        {
            internal readonly Vector2Int Key;
            internal readonly Vector3 Origin;
            internal readonly List<RailwayInstance> Instances = new List<RailwayInstance>();
            internal readonly Mesh Rails = new Mesh { name = "Rail cell", hideFlags = HideFlags.HideAndDontSave };
            internal readonly Mesh Sleepers = new Mesh { name = "Sleeper cell", hideFlags = HideFlags.HideAndDontSave };
            internal bool Dirty;
            internal Cell(Vector2Int key) { Key = key; Origin = new Vector3(key.x * CellSize, 0, key.y * CellSize); }
        }
        private sealed class StationTemplate
        {
            internal readonly List<(VirtualRenderBatchKey Key, Matrix4x4 Matrix)> Parts = new List<(VirtualRenderBatchKey, Matrix4x4)>();
            internal readonly SphereCollider Collider;
            internal readonly Matrix4x4 ColliderMatrix;
            internal StationTemplate(InstallationObject prototype)
            {
                foreach (var renderer in prototype.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.enabled || !renderer.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
                    var mesh = filter.sharedMesh; var materials = renderer.sharedMaterials;
                    var matrix = prototype.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                        if (materials[sub] != null) Parts.Add((new VirtualRenderBatchKey(mesh, materials[sub], renderer.gameObject.layer, sub,
                            renderer.shadowCastingMode, renderer.receiveShadows, false, renderingLayerMask: renderer.renderingLayerMask), matrix));
                }
                Collider = prototype.GetComponentInChildren<SphereCollider>(true);
                if (Collider != null) ColliderMatrix = prototype.transform.worldToLocalMatrix * Collider.transform.localToWorldMatrix;
            }
        }
    }
}
