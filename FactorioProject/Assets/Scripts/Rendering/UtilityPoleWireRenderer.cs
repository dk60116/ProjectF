using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectF.Rendering
{
    // Numerical wire requests replace one GameObject/LineRenderer per connection.
    internal sealed class UtilityPoleWire
    {
        internal Vector3 Start, End;
        internal float Width, Sag;
        internal int Segments;
        internal Color Color;
        internal bool Visible;
        internal UtilityPoleWire() { UtilityPoleWireRenderer.Register(this); }
        internal void Set(Vector3 start, Vector3 end, float width, float sag, int segments, Color color, bool visible)
        {
            segments = Mathf.Clamp(segments, 1, 63); sag = Mathf.Max(0, sag);
            if (Start == start && End == end && Width == width && Sag == sag && Segments == segments && Color == color && Visible == visible) return;
            Start = start; End = end; Width = width; Sag = sag; Segments = segments; Color = color; Visible = visible;
            UtilityPoleWireRenderer.Invalidate();
        }
        internal void SetVisible(bool visible) { if (Visible == visible) return; Visible = visible; UtilityPoleWireRenderer.Invalidate(); }
        internal void Dispose() { Visible = false; UtilityPoleWireRenderer.Unregister(this); }
        internal Vector3 Point(int index)
        {
            float t = (float)index / Segments;
            return Vector3.Lerp(Start, End, t) + Vector3.down * (4f * t * (1f - t) * Sag);
        }
    }

    [DefaultExecutionOrder(1100)]
    public sealed class UtilityPoleWireRenderer : MonoBehaviour
    {
        private sealed class Chunk
        {
            internal readonly List<UtilityPoleWire> Wires = new List<UtilityPoleWire>();
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Color32> Colors = new List<Color32>();
            internal readonly List<Vector2> Uvs = new List<Vector2>();
            internal readonly List<int> Indices = new List<int>();
            internal readonly Mesh Mesh = new Mesh { name = "Utility pole wire batch", indexFormat = IndexFormat.UInt32 };
            internal Bounds Bounds;
            internal bool Dirty = true;
            internal Vector3 CameraPosition;
            internal Quaternion CameraRotation;
            internal bool Orthographic;
            internal Chunk() { Mesh.MarkDynamic(); }
            internal void Add(UtilityPoleWire wire)
            {
                var bounds = new Bounds((wire.Start + wire.End) * .5f, new Vector3(Mathf.Abs(wire.Start.x - wire.End.x), Mathf.Abs(wire.Start.y - wire.End.y) + wire.Sag * 2, Mathf.Abs(wire.Start.z - wire.End.z)));
                bounds.Expand(wire.Width * 2);
                if (Wires.Count == 0) Bounds = bounds; else Bounds.Encapsulate(bounds);
                Wires.Add(wire); Dirty = true;
            }
            internal void Rebuild(Camera camera)
            {
                Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
                if (!Dirty && Orthographic == camera.orthographic && CameraRotation == rotation && (camera.orthographic || CameraPosition == position)) return;
                Dirty = false; CameraPosition = position; CameraRotation = rotation; Orthographic = camera.orthographic;
                Vertices.Clear(); Colors.Clear(); Uvs.Clear(); Indices.Clear();
                foreach (var wire in Wires)
                {
                    int first = Vertices.Count;
                    for (int i = 0; i <= wire.Segments; i++)
                    {
                        Vector3 point = wire.Point(i);
                        Vector3 tangent = wire.Point(Mathf.Min(i + 1, wire.Segments)) - wire.Point(Mathf.Max(i - 1, 0));
                        Vector3 view = camera.orthographic ? -camera.transform.forward : position - point;
                        Vector3 side = Vector3.Cross(tangent, view).normalized * (wire.Width * .5f);
                        if (side.sqrMagnitude < .00000001f) side = camera.transform.right * (wire.Width * .5f);
                        Vertices.Add(point - side); Vertices.Add(point + side);
                        Colors.Add(wire.Color); Colors.Add(wire.Color);
                        float t = (float)i / wire.Segments; Uvs.Add(new Vector2(t,0)); Uvs.Add(new Vector2(t,1));
                        if (i == 0) continue;
                        int n = first + i * 2;
                        Indices.Add(n - 2); Indices.Add(n); Indices.Add(n - 1);
                        Indices.Add(n - 1); Indices.Add(n); Indices.Add(n + 1);
                    }
                    AppendCap(wire, camera, false);
                    AppendCap(wire, camera, true);
                }
                Mesh.Clear(); Mesh.SetVertices(Vertices); Mesh.SetColors(Colors); Mesh.SetUVs(0,Uvs); Mesh.SetTriangles(Indices,0,false); Mesh.bounds = Bounds;
            }
            private void AppendCap(UtilityPoleWire wire, Camera camera, bool atEnd)
            {
                Vector3 point = atEnd ? wire.End : wire.Start;
                Vector3 tangent = (atEnd ? wire.End - wire.Point(wire.Segments - 1) : wire.Start - wire.Point(1)).normalized;
                Vector3 view = camera.orthographic ? -camera.transform.forward : camera.transform.position - point;
                Vector3 side = Vector3.Cross(tangent, view).normalized;
                if (side.sqrMagnitude < .00000001f) side = camera.transform.right;
                int center = Vertices.Count; Vertices.Add(point); Colors.Add(wire.Color); Uvs.Add(new Vector2(.5f,.5f));
                // Match the old LineRenderer's two cap vertices with a three-triangle half disk.
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * Mathf.PI / 3f;
                    Vertices.Add(point + (side * Mathf.Cos(angle) + tangent * Mathf.Sin(angle)) * (wire.Width * .5f));
                    Colors.Add(wire.Color); Uvs.Add(new Vector2(.5f,.5f));
                    if (i > 0) { Indices.Add(center); Indices.Add(center + i); Indices.Add(center + i + 1); }
                }
            }
        }
        private static readonly HashSet<UtilityPoleWire> wires = new HashSet<UtilityPoleWire>();
        private static UtilityPoleWireRenderer instance;
        private static bool dirty = true;
        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly CameraRenderCulling culling = new CameraRenderCulling();
        private readonly List<Vector2Int> emptyChunks = new List<Vector2Int>();
        private Material material;
        private int visibleChunks, visibleWires;
        internal static void Register(UtilityPoleWire wire)
        {
            wires.Add(wire); dirty = true;
            if (instance != null || !Application.isPlaying) return;
            var host = new GameObject(nameof(UtilityPoleWireRenderer)); instance = host.AddComponent<UtilityPoleWireRenderer>();
        }
        internal static void Unregister(UtilityPoleWire wire) { if (wires.Remove(wire)) dirty = true; }
        internal static void Invalidate() { dirty = true; }
        private void LateUpdate()
        {
            if (MapObjectTickManager.WaitingForWorldLoad) return;
            if (ProjectF.Power.UtilityPoleWorld.Current?.Terrain.IsBenchmarkPlacementInProgress == true) return;
            var camera = Camera.main; if (camera == null) return;
            if (dirty)
            {
                dirty = false; foreach (var chunk in chunks.Values) { chunk.Wires.Clear(); chunk.Dirty = true; }
                foreach (var wire in wires)
                {
                    if (!wire.Visible) continue;
                    Vector3 midpoint = (wire.Start + wire.End) * .5f;
                    var cell = new Vector2Int(Mathf.FloorToInt(midpoint.x / 32f), Mathf.FloorToInt(midpoint.z / 32f));
                    if (!chunks.TryGetValue(cell,out var chunk)) chunks.Add(cell,chunk = new Chunk());
                    chunk.Add(wire);
                }
                emptyChunks.Clear();
                foreach (var pair in chunks) if (pair.Value.Wires.Count == 0) emptyChunks.Add(pair.Key);
                for (int i = 0; i < emptyChunks.Count; i++) { Destroy(chunks[emptyChunks[i]].Mesh); chunks.Remove(emptyChunks[i]); }
            }
            if (material == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if (shader == null) return;
                material = new Material(shader) { name = "Utility Pole Line Material", hideFlags = HideFlags.HideAndDontSave };
            }
            culling.Update(camera);
            visibleChunks = visibleWires = 0;
            if (culling.TryGetVisibleCellRange(32,2,out var min,out var max)
                && CameraRenderCulling.GetCellCount(min,max) <= chunks.Count * 2L)
            {
                for (int y = min.y; y <= max.y; y++) for (int x = min.x; x <= max.x; x++)
                    if (chunks.TryGetValue(new Vector2Int(x,y),out var chunk)) Draw(chunk,camera);
            }
            else foreach (var chunk in chunks.Values) Draw(chunk,camera);
        }
        private void Draw(Chunk chunk, Camera camera)
        {
            if (chunk.Wires.Count == 0 || !culling.Intersects(chunk.Bounds)) return;
            chunk.Rebuild(camera); visibleChunks++; visibleWires += chunk.Wires.Count;
            Graphics.DrawMesh(chunk.Mesh, Matrix4x4.identity, material, 0, camera, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
        }
        internal static void AppendProfilerCounters()
        {
            MapObjectTickProfiler.AddRuntimeCounter("UtilityPoleECS", "WireGameObjects", instance != null ? 1 : 0);
            MapObjectTickProfiler.AddRuntimeCounter("UtilityPoleECS", "WireBatches", instance != null ? instance.visibleChunks : 0);
            MapObjectTickProfiler.AddRuntimeCounter("UtilityPoleECS", "VisibleWires", instance != null ? instance.visibleWires : 0);
        }
        private void OnDestroy()
        {
            foreach (var chunk in chunks.Values) Destroy(chunk.Mesh);
            chunks.Clear(); if (material != null) Destroy(material);
            if (instance == this) { instance = null; wires.Clear(); dirty = true; }
        }
    }
}
