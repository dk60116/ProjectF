using System.Collections.Generic;
using ProjectF.Conveyors;
using UnityEngine;
using UnityEngine.Rendering;

public partial class TerrainGenerator
{
    private readonly BeltSplitGraph beltSplitGraph = new BeltSplitGraph();
    private readonly List<Block> beltSplitBlocks = new List<Block>();
    private readonly List<(Block block, int lane)> beltSplitLanes = new List<(Block, int)>();
    private readonly Dictionary<(Block block, int lane), int> beltSplitIndices = new Dictionary<(Block, int), int>();
    private readonly List<(Block block, int lane)> beltSplitConnections = new List<(Block, int)>(2);
    private readonly List<Vector3> beltSplitSegments = new List<Vector3>(6);
    private readonly List<Vector3> beltSplitVertices = new List<Vector3>();
    private readonly List<Color32> beltSplitColors = new List<Color32>();
    private readonly List<int> beltSplitTriangles = new List<int>();
    private readonly Dictionary<Vector2Int, Mesh> beltSplitMeshes = new Dictionary<Vector2Int, Mesh>();
    private readonly Dictionary<Vector2Int, List<int>> beltSplitChunkBlocks = new Dictionary<Vector2Int, List<int>>();
    private Material beltSplitMaterial;
    private bool beltSplitDirty = true;
    private bool beltSplitVisualsDirty = true;

    public int BeltSplitGroupCount { get { EnsureBeltSplitGroups(); return beltSplitGraph.GroupCount; } }

    // Keys include the lane: independent paths can cross at the same coordinate.
    // These are transport groups, not thread-safe ownership boundaries for Block.
    public bool TryGetBeltSplitGroup(Block block, int lane, out Vector3Int group)
    {
        EnsureBeltSplitGroups();
        if (block != null && beltSplitIndices.TryGetValue((block, lane), out int index))
        {
            group = GetBeltSplitGroupKey(index);
            return true;
        }
        group = default;
        return false;
    }

    public void CopyBeltSplitGroupLanes(Vector3Int group, List<(Block block, int lane)> results)
    {
        EnsureBeltSplitGroups();
        results.Clear();
        for (int i = 0; i < beltSplitLanes.Count; i++)
            if (GetBeltSplitGroupKey(i) == group) results.Add(beltSplitLanes[i]);
    }

    private Vector3Int GetBeltSplitGroupKey(int index)
    {
        var node = beltSplitLanes[beltSplitGraph.Representative(index)];
        return new Vector3Int(node.block.Coordinate.x, node.block.Coordinate.y, node.lane);
    }

    private void EnsureBeltSplitGroups()
    {
        if (!beltSplitDirty) return;
        beltSplitBlocks.Clear();
        beltSplitIndices.Clear();
        beltSplitLanes.Clear();
        foreach (KeyValuePair<Vector2Int, Block> entry in loadedBlocks)
            if (entry.Value != null && entry.Value.IsRuntimeConveyor)
                beltSplitBlocks.Add(entry.Value);
        beltSplitBlocks.Sort(CompareBeltSplitBlocks);
        foreach (Block block in beltSplitBlocks)
            for (int lane = 0; lane < block.BeltSplitLaneLimit; lane++)
                if (block.IsBeltSplitLane(lane))
                {
                    beltSplitIndices.Add((block, lane), beltSplitLanes.Count);
                    beltSplitLanes.Add((block, lane));
                }
        beltSplitGraph.Reset(beltSplitLanes.Count);
        for (int i = 0; i < beltSplitLanes.Count; i++)
        {
            beltSplitConnections.Clear();
            var node = beltSplitLanes[i];
            node.block.AppendBeltSplitConnections(node.lane, beltSplitConnections);
            for (int j = 0; j < beltSplitConnections.Count; j++)
                if (beltSplitIndices.TryGetValue(beltSplitConnections[j], out int neighbor))
                    beltSplitGraph.Connect(i, neighbor);
        }
        beltSplitConnections.Clear();
        beltSplitDirty = false;
        beltSplitVisualsDirty = true;
    }

    private static int CompareBeltSplitBlocks(Block a, Block b)
    {
        int x = a.Coordinate.x.CompareTo(b.Coordinate.x);
        return x != 0 ? x : a.Coordinate.y.CompareTo(b.Coordinate.y);
    }

    private void DrawBeltSplitGroups()
    {
        if (GameManager.Instance == null || !GameManager.Instance.ShowBeltSplit) return;
        EnsureBeltSplitGroups();
        if (beltSplitMaterial == null)
        {
            Material template = Resources.Load<Material>("Materials/AreaMarkerLateRender");
            if (template == null) return;
            beltSplitMaterial = new Material(template) { name = "Belt Split", hideFlags = HideFlags.HideAndDontSave };
            beltSplitMaterial.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
        }
        if (beltSplitVisualsDirty) RebuildBeltSplitMeshes();
        conveyorDebugCameraCulling.Update(Camera.main);
        if (!conveyorDebugCameraCulling.IsLayerVisible(gameObject.layer)) return;
        foreach (Mesh mesh in beltSplitMeshes.Values)
            if (conveyorDebugCameraCulling.Intersects(mesh.bounds))
                Graphics.DrawMesh(mesh, Matrix4x4.identity, beltSplitMaterial, gameObject.layer,
                    null, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
    }

    private void RebuildBeltSplitMeshes()
    {
        ReleaseBeltSplitMeshes();
        // Small spatial batches keep debug overlays under normal camera culling.
        for (int i = 0; i < beltSplitLanes.Count; i++)
        {
            Vector2Int cell = beltSplitLanes[i].block.Coordinate;
            Vector2Int chunk = new Vector2Int(Mathf.FloorToInt(cell.x / 16f), Mathf.FloorToInt(cell.y / 16f));
            if (!beltSplitChunkBlocks.TryGetValue(chunk, out List<int> indices))
            {
                indices = new List<int>();
                beltSplitChunkBlocks.Add(chunk, indices);
            }
            indices.Add(i);
        }
        foreach (KeyValuePair<Vector2Int, List<int>> chunk in beltSplitChunkBlocks)
        {
            beltSplitVertices.Clear();
            beltSplitColors.Clear();
            beltSplitTriangles.Clear();
            foreach (int index in chunk.Value)
            {
                Vector3Int key = GetBeltSplitGroupKey(index);
                // Stable across enumeration order and edits to unrelated groups.
                uint hash = unchecked((uint)key.x * 73856093u ^ (uint)key.y * 19349663u ^ (uint)key.z * 83492791u);
                hash ^= hash >> 16;
                Color color = Color.HSVToRGB((hash % 65521u) / 65521f, 0.75f, 1f);
                color.a = 0.65f;
                beltSplitSegments.Clear();
                var node = beltSplitLanes[index];
                node.block.AppendBeltSplitVisualSegments(node.lane, beltSplitSegments);
                for (int j = 0; j < beltSplitSegments.Count; j += 2)
                    AddBeltSplitStrip(beltSplitSegments[j], beltSplitSegments[j + 1], color);
            }
            Mesh mesh = new Mesh { name = "Belt Split " + chunk.Key, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(beltSplitVertices);
            mesh.SetColors(beltSplitColors);
            mesh.SetTriangles(beltSplitTriangles, 0);
            mesh.RecalculateBounds();
            beltSplitMeshes.Add(chunk.Key, mesh);
        }
        beltSplitVisualsDirty = false;
    }

    private void AddBeltSplitStrip(Vector3 from, Vector3 to, Color32 color)
    {
        Vector3 direction = (to - from).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * 0.23f;
        from += Vector3.up * 0.055f - direction * 0.18f;
        to += Vector3.up * 0.055f + direction * 0.18f;
        int start = beltSplitVertices.Count;
        beltSplitVertices.Add(from - side);
        beltSplitVertices.Add(from + side);
        beltSplitVertices.Add(to + side);
        beltSplitVertices.Add(to - side);
        for (int i = 0; i < 4; i++) beltSplitColors.Add(color);
        beltSplitTriangles.Add(start); beltSplitTriangles.Add(start + 1); beltSplitTriangles.Add(start + 2);
        beltSplitTriangles.Add(start); beltSplitTriangles.Add(start + 2); beltSplitTriangles.Add(start + 3);
    }

    private void ReleaseBeltSplitMeshes()
    {
        foreach (Mesh mesh in beltSplitMeshes.Values) Destroy(mesh);
        beltSplitMeshes.Clear();
        beltSplitChunkBlocks.Clear();
    }

    private void ClearBeltSplitState()
    {
        ReleaseBeltSplitMeshes();
        if (beltSplitMaterial != null) Destroy(beltSplitMaterial);
        beltSplitMaterial = null;
        beltSplitBlocks.Clear();
        beltSplitIndices.Clear();
        beltSplitLanes.Clear();
        beltSplitConnections.Clear();
        beltSplitGraph.Reset(0);
        beltSplitDirty = beltSplitVisualsDirty = true;
    }
}
