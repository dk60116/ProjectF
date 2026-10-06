using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

static class PreviewChecks
{
    static int checks;
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }

    public static void Run()
    {
        var straight = new[] { new Vector2(-4, 2), new Vector2(4, 2) };
        Validate(straight, false, false, Vector2Int.zero);
        Validate(straight, true, false, new Vector2Int(-4, 2));
        Validate(straight, false, true, Vector2Int.zero);
        Validate(straight, true, true, Vector2Int.zero);
        Validate(new[] { new Vector2(0, 0), new Vector2(0, 3), new Vector2(1, 4), new Vector2(4, 4) }, true, true, Vector2Int.zero);
        Validate(new[] { new Vector2(0, 0), new Vector2(.2f, 0) }, false, false, Vector2Int.zero);
        Validate(new[] { new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 4) }, true, true, Vector2Int.zero);

        var preview = new RailloadInstallationController();
        preview.Preview(straight, true);
        Check(preview.Visible && preview.Mesh.Vertices.Count > 32, "Blueprint must submit sleepers together with rail strips");
        Check(preview.Tint.g > preview.Tint.r, "Valid rail and sleeper preview must use the valid tint");
        int count = preview.Mesh.Vertices.Count;
        preview.Preview(straight, false);
        Check(preview.Visible && preview.Mesh.Vertices.Count == count && preview.Tint.r > preview.Tint.g,
            "Blocked blueprint must retain complete geometry with the invalid tint");
        preview.Preview(new[] { new Vector2(0, 0) }, true);
        Check(!preview.Visible, "Incomplete blueprint must hide both rails and sleepers");
        preview.Preview(null, true);
        Check(!preview.Visible, "Cancelled blueprint must remain hidden");

        preview.Preview(new[] { new Vector2(0, 0), new Vector2(1600, 0) }, true);
        Check(preview.Mesh.Vertices.Count > ushort.MaxValue && preview.Mesh.indexFormat == IndexFormat.UInt32,
            "Long blueprints must support more than 65535 rail and sleeper vertices");

        var prefab = new Railload();
        var vertices = new List<Vector3>(); var triangles = new List<int>(); var path = new List<Vector3>();
        prefab.AppendPlacementPreviewMesh(vertices, triangles, straight, Vector2Int.zero, .16f, true, true, path);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            vertices.Clear(); triangles.Clear();
            prefab.AppendPlacementPreviewMesh(vertices, triangles, straight, Vector2Int.zero, .16f, true, true, path);
        }
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Warmed blueprint geometry must reuse all list buffers");
        typeof(Railload).GetField("sleeperLength", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(prefab, 1.4f);
        typeof(Railload).GetField("sleeperSpacing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(prefab, 1f);
        vertices.Clear(); triangles.Clear();
        prefab.AppendPlacementPreviewMesh(vertices, triangles, straight, Vector2Int.zero, .16f, false, false, path);
        Check(vertices.Count == 32 + 9 * 24 && MathF.Abs((vertices[33] - vertices[32]).magnitude - 1.4f) < .0001f,
            "Blueprint sleepers must follow customized prefab dimensions and spacing");
        vertices.Clear(); triangles.Clear();
        prefab.AppendPlacementPreviewMesh(vertices, triangles, null, Vector2Int.zero, .16f, true, true, path);
        Check(vertices.Count == 0 && triangles.Count == 0 && path.Count == 0, "Missing path must not reuse previous geometry");
        Console.WriteLine($"Rail blueprint preview passed: {checks} checks");
    }

    static void Validate(Vector2[] points, bool extendStart, bool extendEnd, Vector2Int origin)
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>(); var path = new List<Vector3>();
        new Railload().AppendPlacementPreviewMesh(vertices, triangles, points, origin, .16f, extendStart, extendEnd, path);
        float length = 0;
        for (int i = 1; i < path.Count; i++) length += (path[i] - path[i - 1]).magnitude;
        int sleepers = Math.Max(1, (int)MathF.Floor(length / .55f) + 1);
        int railVertices = 16 * path.Count;
        Check(vertices.Count == railVertices + 24 * sleepers, "Preview must contain two rails and every sleeper box");
        Check(triangles.Count == 48 * (path.Count - 1) + 36 * sleepers, "Preview must contain complete rail and sleeper faces");
        foreach (int index in triangles) Check(index >= 0 && index < vertices.Count, "Combined mesh indices must address valid vertices");
        for (int i = railVertices; i < vertices.Count; i += 24)
        {
            Check(MathF.Abs(vertices[i].y - .095f) < .00001f, "Sleeper top must overlap the rail base at the preview height");
            Check(MathF.Abs(vertices[i + 4].y - .015f) < .00001f, "Sleeper thickness must match the prefab");
            Check(MathF.Abs((vertices[i + 1] - vertices[i]).magnitude - .84f) < .0001f, "Sleeper length must match the prefab");
        }
        Check(MathF.Abs(path[0].x - (points[0].x - origin.x)) < .0001f || extendStart,
            "Preview geometry must respect its local origin");
    }
}

public partial class RailloadInstallationController
{
    readonly Railload railloadPrefab = new Railload();
    readonly List<Vector3> previewVertices = new List<Vector3>(256), previewCenterPath = new List<Vector3>(64);
    readonly List<int> previewTriangles = new List<int>(384);
    readonly Mesh previewMesh = new Mesh();
    readonly MeshRenderer previewMeshRenderer = new MeshRenderer();
    void EnsurePreviewMesh() { }
    public Mesh Mesh => previewMesh;
    public bool Visible => previewMeshRenderer.enabled;
    public Color Tint => previewMeshRenderer.sharedMaterial.color;
    public void Preview(Vector2[] points, bool valid)
    {
        RailPathPlan plan = null;
        if (points != null)
        {
            plan = new RailPathPlan { isValid = valid };
            plan.visualPathPoints.AddRange(points);
        }
        RefreshPreviewMesh(plan);
    }
}

namespace UnityEngine
{
    public sealed class Mesh
    {
        public IndexFormat indexFormat;
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();
        public void Clear() { Vertices.Clear(); Triangles.Clear(); }
        public void SetVertices(List<Vector3> values) => Vertices.AddRange(values);
        public void SetTriangles(List<int> values, int submesh) => Triangles.AddRange(values);
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
    }
    public sealed class MeshRenderer
    {
        public bool enabled;
        public readonly Material sharedMaterial = new Material(new Shader());
    }
}
namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
}
