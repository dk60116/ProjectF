using System.Collections.Generic;
using ProjectF.Conveyors;
using UnityEngine;
using UnityEngine.Rendering;

public partial class TerrainGenerator
{
    private static readonly Vector2Int[] PipeSplitDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private readonly struct PipeSplitVisualNode
    {
        internal PipeSplitVisualNode(PipeRuntimeRecord record, Vector2Int coordinate)
        {
            Record = record;
            Coordinate = coordinate;
        }

        internal PipeRuntimeRecord Record { get; }
        internal Vector2Int Coordinate { get; }
    }

    private readonly BeltSplitGraph pipeSplitGraph = new BeltSplitGraph();
    private readonly List<PipeRuntimeRecord> pipeSplitRecords = new List<PipeRuntimeRecord>();
    private readonly Dictionary<PipeRuntimeRecord, int> pipeSplitIndices =
        new Dictionary<PipeRuntimeRecord, int>();
    private readonly List<PipeSplitVisualNode> pipeSplitVisualNodes = new List<PipeSplitVisualNode>();
    private readonly List<Vector3> pipeSplitSegments = new List<Vector3>(8);
    private readonly List<Vector3> pipeSplitVertices = new List<Vector3>();
    private readonly List<Color32> pipeSplitColors = new List<Color32>();
    private readonly List<int> pipeSplitTriangles = new List<int>();
    private readonly Dictionary<Vector2Int, Mesh> pipeSplitMeshes = new Dictionary<Vector2Int, Mesh>();
    private readonly Dictionary<Vector2Int, List<int>> pipeSplitChunkNodes =
        new Dictionary<Vector2Int, List<int>>();
    private PipeWorld pipeSplitWorld;
    private int pipeSplitTopologyVersion = -1;
    private bool pipeSplitVisualsDirty = true;

    public int PipeSplitGroupCount
    {
        get
        {
            EnsurePipeSplitGroups();
            return pipeSplitGraph.GroupCount;
        }
    }

    public bool TryGetPipeSplitGroup(Vector2Int coordinate, out Vector2Int group)
    {
        EnsurePipeSplitGroups();
        if (pipeSplitWorld != null
            && pipeSplitWorld.TryGetAtCoordinate(coordinate, out PipeRuntimeRecord record)
            && pipeSplitIndices.TryGetValue(record, out int index))
        {
            group = GetPipeSplitGroupKey(index);
            return true;
        }

        group = default;
        return false;
    }

    public void CopyPipeSplitGroupRecords(Vector2Int group, List<PipeRuntimeRecord> results)
    {
        EnsurePipeSplitGroups();
        results.Clear();
        for (int i = 0; i < pipeSplitRecords.Count; i++)
        {
            if (GetPipeSplitGroupKey(i) == group)
            {
                results.Add(pipeSplitRecords[i]);
            }
        }
    }

    private void EnsurePipeSplitGroups()
    {
        PipeWorld world = PipeWorld.Current;
        int topologyVersion = world != null ? world.TopologyVersion : 0;
        if (ReferenceEquals(pipeSplitWorld, world) && pipeSplitTopologyVersion == topologyVersion)
        {
            return;
        }

        pipeSplitWorld = world;
        pipeSplitTopologyVersion = topologyVersion;
        pipeSplitRecords.Clear();
        pipeSplitIndices.Clear();
        pipeSplitVisualNodes.Clear();
        if (world == null)
        {
            pipeSplitGraph.Reset(0);
            pipeSplitVisualsDirty = true;
            return;
        }

        world.CopyRecords(pipeSplitRecords);
        pipeSplitRecords.Sort(ComparePipeSplitRecords);
        for (int i = 0; i < pipeSplitRecords.Count; i++)
        {
            PipeRuntimeRecord record = pipeSplitRecords[i];
            pipeSplitIndices.Add(record, i);
            IReadOnlyList<Vector2Int> coordinates = record.OccupiedCoordinates;
            for (int coordinateIndex = 0; coordinateIndex < coordinates.Count; coordinateIndex++)
            {
                pipeSplitVisualNodes.Add(new PipeSplitVisualNode(record, coordinates[coordinateIndex]));
            }
        }

        pipeSplitGraph.Reset(pipeSplitRecords.Count);
        for (int i = 0; i < pipeSplitRecords.Count; i++)
        {
            PipeRuntimeRecord record = pipeSplitRecords[i];
            IReadOnlyList<Vector2Int> coordinates = record.OccupiedCoordinates;
            for (int coordinateIndex = 0; coordinateIndex < coordinates.Count; coordinateIndex++)
            {
                Vector2Int coordinate = coordinates[coordinateIndex];
                for (int directionIndex = 0; directionIndex < PipeSplitDirections.Length; directionIndex++)
                {
                    Vector2Int direction = PipeSplitDirections[directionIndex];
                    if (!record.HasConnectionTowardsAt(coordinate, direction)
                        || !world.TryGetAtCoordinate(coordinate + direction, out PipeRuntimeRecord neighbor)
                        || ReferenceEquals(record, neighbor)
                        || !neighbor.HasConnectionTowardsAt(coordinate + direction, -direction)
                        || !pipeSplitIndices.TryGetValue(neighbor, out int neighborIndex))
                    {
                        continue;
                    }

                    pipeSplitGraph.Connect(i, neighborIndex);
                }
            }
        }

        pipeSplitVisualsDirty = true;
    }

    private static int ComparePipeSplitRecords(PipeRuntimeRecord a, PipeRuntimeRecord b)
    {
        int x = a.AnchorCoordinate.x.CompareTo(b.AnchorCoordinate.x);
        if (x != 0) return x;
        int y = a.AnchorCoordinate.y.CompareTo(b.AnchorCoordinate.y);
        return y != 0 ? y : a.PlacementSequence.CompareTo(b.PlacementSequence);
    }

    private Vector2Int GetPipeSplitGroupKey(int index)
    {
        PipeRuntimeRecord record = pipeSplitRecords[pipeSplitGraph.Representative(index)];
        return record.AnchorCoordinate;
    }

    private void DrawPipeSplitGroups()
    {
        EnsurePipeSplitGroups();
        if (pipeSplitVisualsDirty)
        {
            RebuildPipeSplitMeshes();
        }

        foreach (Mesh mesh in pipeSplitMeshes.Values)
        {
            if (conveyorDebugCameraCulling.Intersects(mesh.bounds))
            {
                Graphics.DrawMesh(
                    mesh,
                    Matrix4x4.identity,
                    beltSplitMaterial,
                    gameObject.layer,
                    null,
                    0,
                    null,
                    ShadowCastingMode.Off,
                    false,
                    null,
                    LightProbeUsage.Off);
            }
        }
    }

    private void RebuildPipeSplitMeshes()
    {
        ReleasePipeSplitMeshes();
        for (int i = 0; i < pipeSplitVisualNodes.Count; i++)
        {
            Vector2Int coordinate = pipeSplitVisualNodes[i].Coordinate;
            Vector2Int chunk = new Vector2Int(
                Mathf.FloorToInt(coordinate.x / 16f),
                Mathf.FloorToInt(coordinate.y / 16f));
            if (!pipeSplitChunkNodes.TryGetValue(chunk, out List<int> indices))
            {
                indices = new List<int>();
                pipeSplitChunkNodes.Add(chunk, indices);
            }

            indices.Add(i);
        }

        foreach (KeyValuePair<Vector2Int, List<int>> chunk in pipeSplitChunkNodes)
        {
            pipeSplitVertices.Clear();
            pipeSplitColors.Clear();
            pipeSplitTriangles.Clear();
            foreach (int nodeIndex in chunk.Value)
            {
                PipeSplitVisualNode node = pipeSplitVisualNodes[nodeIndex];
                int recordIndex = pipeSplitIndices[node.Record];
                Vector2Int key = GetPipeSplitGroupKey(recordIndex);
                uint hash = unchecked((uint)key.x * 73856093u ^ (uint)key.y * 19349663u ^ 0x9e3779b9u);
                hash ^= hash >> 16;
                Color color = Color.HSVToRGB((hash % 65521u) / 65521f, 0.75f, 1f);
                color.a = 0.65f;
                pipeSplitSegments.Clear();
                node.Record.AppendSplitVisualSegments(node.Coordinate, pipeSplitSegments);
                for (int segment = 0; segment < pipeSplitSegments.Count; segment += 2)
                {
                    AddBeltPipeSplitStrip(
                        pipeSplitVertices,
                        pipeSplitColors,
                        pipeSplitTriangles,
                        pipeSplitSegments[segment],
                        pipeSplitSegments[segment + 1],
                        color,
                        0.095f,
                        0.015f);
                }
            }

            Mesh mesh = new Mesh
            {
                name = "Pipe Split " + chunk.Key,
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(pipeSplitVertices);
            mesh.SetColors(pipeSplitColors);
            mesh.SetTriangles(pipeSplitTriangles, 0);
            mesh.RecalculateBounds();
            pipeSplitMeshes.Add(chunk.Key, mesh);
        }

        pipeSplitVisualsDirty = false;
    }

    private void ReleasePipeSplitMeshes()
    {
        foreach (Mesh mesh in pipeSplitMeshes.Values)
        {
            Destroy(mesh);
        }

        pipeSplitMeshes.Clear();
        pipeSplitChunkNodes.Clear();
    }

    private void ClearPipeSplitState()
    {
        ReleasePipeSplitMeshes();
        pipeSplitRecords.Clear();
        pipeSplitIndices.Clear();
        pipeSplitVisualNodes.Clear();
        pipeSplitSegments.Clear();
        pipeSplitGraph.Reset(0);
        pipeSplitWorld = null;
        pipeSplitTopologyVersion = -1;
        pipeSplitVisualsDirty = true;
    }
}
