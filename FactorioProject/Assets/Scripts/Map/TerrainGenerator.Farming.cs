using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class TerrainGenerator : MonoBehaviour
{
    private const string FarmlandVisualName = "FarmlandSurface";
    private const float FarmlandSurfaceOffset = 0.008f;
    private static readonly Vector2Int[] FarmlandNeighborDirections =
    {
        Vector2Int.left,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.up,
        new Vector2Int(-1, -1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 1),
        new Vector2Int(1, 1)
    };
    private readonly HashSet<Vector2Int> farmlandCoordinates = new HashSet<Vector2Int>();
    private Mesh farmlandVisualMesh;
    private Material farmlandVisualMaterial;
    private MaterialPropertyBlock farmlandVisualPropertyBlock;

    public bool IsFarmlandAt(Vector2Int coordinate)
    {
        return farmlandCoordinates.Contains(coordinate);
    }

    public bool TryToggleFarmland(Block block)
    {
        if (block == null
            || block.Type != Block.BlockType.Ground
            || !IsFarmableGroundBiomeAt(block.Coordinate)
            || block.MapObject != null
            || block.Resource != null
            || block.HasDroppedFloorObjects)
        {
            return false;
        }

        if (!farmlandCoordinates.Remove(block.Coordinate))
        {
            farmlandCoordinates.Add(block.Coordinate);
        }

        RefreshFarmlandVisual(block);
        RefreshLoadedFarmlandNeighbors(block.Coordinate);
        return true;
    }

    private void RefreshLoadedFarmlandNeighbors(Vector2Int coordinate)
    {
        for (int i = 0; i < FarmlandNeighborDirections.Length; i++)
        {
            if (TryGetLoadedBlock(coordinate + FarmlandNeighborDirections[i], out Block neighborBlock)
                && neighborBlock != null
                && farmlandCoordinates.Contains(neighborBlock.Coordinate))
            {
                RefreshFarmlandVisual(neighborBlock);
            }
        }
    }

    private void RefreshFarmlandVisual(Block block)
    {
        if (block == null)
        {
            return;
        }

        Transform visual = block.transform.Find(FarmlandVisualName);
        bool visible = farmlandCoordinates.Contains(block.Coordinate);
        if (!visible)
        {
            if (visual != null)
            {
                visual.gameObject.SetActive(false);
            }

            return;
        }

        if (visual == null)
        {
            GameObject visualObject = new GameObject(FarmlandVisualName);
            visual = visualObject.transform;
            visual.SetParent(block.transform, false);
            visualObject.AddComponent<MeshFilter>();
            visualObject.AddComponent<MeshRenderer>();
        }

        MeshFilter meshFilter = visual.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = visual.GetComponent<MeshRenderer>();
        if (meshFilter == null || meshRenderer == null)
        {
            return;
        }

        meshFilter.sharedMesh = ResolveFarmlandVisualMesh();
        meshRenderer.sharedMaterial = ResolveFarmlandVisualMaterial();
        ApplyFarmlandNeighborMask(meshRenderer, block.Coordinate);
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = true;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        float surfaceY = GetBiomeSurfaceY(GetTileBiome(block.Coordinate));
        visual.position = new Vector3(
            block.transform.position.x,
            surfaceY + FarmlandSurfaceOffset,
            block.transform.position.z);
        visual.rotation = Quaternion.identity;
        visual.localScale = Vector3.one;
        visual.gameObject.SetActive(true);
    }

    private Mesh ResolveFarmlandVisualMesh()
    {
        if (farmlandVisualMesh != null)
        {
            return farmlandVisualMesh;
        }

        List<Vector3> vertices = new List<Vector3>(4);
        List<Vector2> uvs = new List<Vector2>(4);
        List<int> triangles = new List<int>(6);
        AddFarmlandQuad(vertices, uvs, triangles, -0.5f, 0.5f, -0.5f, 0.5f, 0f);

        farmlandVisualMesh = new Mesh
        {
            name = "GeneratedFarmlandTile",
            hideFlags = HideFlags.DontSave
        };
        farmlandVisualMesh.SetVertices(vertices);
        farmlandVisualMesh.SetUVs(0, uvs);
        farmlandVisualMesh.SetTriangles(triangles, 0);
        farmlandVisualMesh.RecalculateNormals();
        farmlandVisualMesh.RecalculateBounds();
        return farmlandVisualMesh;
    }

    private static void AddFarmlandQuad(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<int> triangles,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float y)
    {
        int start = vertices.Count;
        vertices.Add(new Vector3(minX, y, minZ));
        vertices.Add(new Vector3(minX, y, maxZ));
        vertices.Add(new Vector3(maxX, y, maxZ));
        vertices.Add(new Vector3(maxX, y, minZ));
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private Material ResolveFarmlandVisualMaterial()
    {
        if (farmlandVisualMaterial != null)
        {
            return farmlandVisualMaterial;
        }

        Shader shader = Shader.Find("ProjectF/Farmland Surface")
                        ?? Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Standard");
        farmlandVisualMaterial = new Material(shader)
        {
            name = "GeneratedFarmlandMaterial",
            hideFlags = HideFlags.DontSave
        };
        Texture baseTexture = generatedSurfaceBlendDirtTexture != null
            ? generatedSurfaceBlendDirtTexture
            : Texture2D.whiteTexture;
        if (farmlandVisualMaterial.HasProperty("_BaseMap"))
        {
            farmlandVisualMaterial.SetTexture("_BaseMap", baseTexture);
            farmlandVisualMaterial.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f));
        }

        if (farmlandVisualMaterial.HasProperty("_BaseColor"))
        {
            farmlandVisualMaterial.SetColor("_BaseColor", new Color(0.88f, 0.72f, 0.56f, 0.94f));
        }
        else if (farmlandVisualMaterial.HasProperty("_Color"))
        {
            farmlandVisualMaterial.color = new Color(0.88f, 0.72f, 0.56f, 0.94f);
        }

        if (farmlandVisualMaterial.HasProperty("_MainTex"))
        {
            farmlandVisualMaterial.mainTexture = baseTexture;
            farmlandVisualMaterial.mainTextureScale = new Vector2(0.5f, 0.5f);
        }
        if (farmlandVisualMaterial.HasProperty("_EdgeFeather"))
        {
            farmlandVisualMaterial.SetFloat("_EdgeFeather", 0.16f);
        }

        return farmlandVisualMaterial;
    }

    private void ApplyFarmlandNeighborMask(MeshRenderer meshRenderer, Vector2Int coordinate)
    {
        if (meshRenderer == null)
        {
            return;
        }

        farmlandVisualPropertyBlock ??= new MaterialPropertyBlock();
        farmlandVisualPropertyBlock.Clear();
        farmlandVisualPropertyBlock.SetVector(
            "_NeighborMask",
            new Vector4(
                farmlandCoordinates.Contains(coordinate + Vector2Int.left) ? 1f : 0f,
                farmlandCoordinates.Contains(coordinate + Vector2Int.right) ? 1f : 0f,
                farmlandCoordinates.Contains(coordinate + Vector2Int.down) ? 1f : 0f,
                farmlandCoordinates.Contains(coordinate + Vector2Int.up) ? 1f : 0f));
        farmlandVisualPropertyBlock.SetVector(
            "_DiagonalMask",
            new Vector4(
                farmlandCoordinates.Contains(coordinate + Vector2Int.left + Vector2Int.down) ? 1f : 0f,
                farmlandCoordinates.Contains(coordinate + Vector2Int.right + Vector2Int.down) ? 1f : 0f,
                farmlandCoordinates.Contains(coordinate + Vector2Int.left + Vector2Int.up) ? 1f : 0f,
                farmlandCoordinates.Contains(coordinate + Vector2Int.right + Vector2Int.up) ? 1f : 0f));
        meshRenderer.SetPropertyBlock(farmlandVisualPropertyBlock);
    }

    private void CaptureFarmlandSaveState(MapSaveData mapSaveData)
    {
        if (mapSaveData == null)
        {
            return;
        }

        mapSaveData.farmlandCoordinates.Clear();
        foreach (Vector2Int coordinate in farmlandCoordinates)
        {
            mapSaveData.farmlandCoordinates.Add(coordinate);
        }
    }

    private void ApplyFarmlandSaveState(MapSaveData mapSaveData)
    {
        farmlandCoordinates.Clear();
        if (mapSaveData?.farmlandCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < mapSaveData.farmlandCoordinates.Count; i++)
        {
            Vector2Int coordinate = mapSaveData.farmlandCoordinates[i];
            if (IsFarmableGroundBiomeAt(coordinate))
            {
                farmlandCoordinates.Add(coordinate);
            }
        }
    }

    private void ClearFarmlandPersistentState()
    {
        farmlandCoordinates.Clear();
    }
}
