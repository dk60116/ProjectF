using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class TerrainGenerator : MonoBehaviour
{
    internal const string FarmlandVisualName = "FarmlandSurface";
    private const float FarmlandSurfaceOffset = 0.008f;
    private const float FarmlandVisualHalfExtent = 1f;
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
    private readonly Dictionary<Vector2Int, int> plantedSeedItemIds =
        new Dictionary<Vector2Int, int>();
    private Mesh farmlandVisualMesh;
    private Material farmlandVisualMaterial;
    private MaterialPropertyBlock farmlandVisualPropertyBlock;

    public bool IsFarmlandAt(Vector2Int coordinate)
    {
        return farmlandCoordinates.Contains(coordinate);
    }

    public bool CanPlantSeed(Block block, ItemDefinition seedDefinition)
    {
        Resource resource = block != null ? block.Resource : null;
        return block != null
               && block.Type == Block.BlockType.Ground
               && farmlandCoordinates.Contains(block.Coordinate)
               && block.MapObject == null
               && (resource == null || !resource.gameObject.activeInHierarchy)
               && !block.HasDroppedFloorObjects
               && ItemDefinition.IsPlantableSeedDefinition(seedDefinition);
    }

    public bool TryPlantSeed(Block block, ItemDefinition seedDefinition)
    {
        if (!CanPlantSeed(block, seedDefinition))
        {
            return false;
        }

        EnsureResourceStateStore();
        resourceStateStore?.RemoveResource(block.Coordinate);
        Resource depletedResource = block.Resource;
        if (depletedResource != null && !depletedResource.gameObject.activeInHierarchy)
        {
            Destroy(depletedResource.gameObject);
        }

        plantedSeedItemIds[block.Coordinate] = seedDefinition.id;
        Resource spawnedResource = SpawnResourceOnBlock(
            block,
            seedDefinition.seedTargetResource.prefab,
            block.Coordinate);
        if (spawnedResource == null)
        {
            plantedSeedItemIds.Remove(block.Coordinate);
            return false;
        }

        InitializePlantedResourceGrowth(spawnedResource, true);
        resourceStateStore?.Save(block.Coordinate, spawnedResource);
        return true;
    }

    private bool TrySpawnPlantedResourceOnBlock(Block block, Vector2Int coordinate)
    {
        if (!plantedSeedItemIds.TryGetValue(coordinate, out int seedItemId))
        {
            return false;
        }

        if (!farmlandCoordinates.Contains(coordinate)
            || !TryResolvePlantedSeedDefinition(seedItemId, out ItemDefinition seedDefinition))
        {
            plantedSeedItemIds.Remove(coordinate);
            return false;
        }

        EnsureResourceStateStore();
        bool hasSavedState = resourceStateStore != null
                             && resourceStateStore.TryGet(coordinate, out _);
        Resource spawnedResource = SpawnResourceOnBlock(
            block,
            seedDefinition.seedTargetResource.prefab,
            coordinate);
        if (spawnedResource != null)
        {
            InitializePlantedResourceGrowth(
                spawnedResource,
                !hasSavedState);
        }

        // 고갈된 리소스도 같은 좌표에 자연 생성 리소스가 덮어쓰지 않도록
        // 심은 좌표를 처리된 것으로 간주한다.
        return true;
    }

    private static bool TryResolvePlantedSeedDefinition(
        int seedItemId,
        out ItemDefinition seedDefinition)
    {
        seedDefinition = null;
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        return itemManager != null
               && itemManager.TryGetItemDefinitionById(seedItemId, out seedDefinition)
               && ItemDefinition.IsPlantableSeedDefinition(seedDefinition);
    }

    private static void InitializePlantedResourceGrowth(
        Resource resource,
        bool initializeGrowth)
    {
        if (initializeGrowth && resource is ProjectF.MapObjects.Tree tree)
        {
            tree.SetGrowth(ResourceDefinition.MinGrowth);
        }
    }

    public bool TryToggleFarmland(Block block)
    {
        Resource resource = block != null ? block.Resource : null;
        if (block == null
            || block.Type != Block.BlockType.Ground
            || !IsFarmableGroundBiomeAt(block.Coordinate)
            || block.MapObject != null
            || (resource != null && resource.gameObject.activeInHierarchy)
            || block.HasDroppedFloorObjects)
        {
            return false;
        }

        if (farmlandCoordinates.Remove(block.Coordinate))
        {
            plantedSeedItemIds.Remove(block.Coordinate);
            EnsureResourceStateStore();
            resourceStateStore?.RemoveResource(block.Coordinate);
        }
        else
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
        AddFarmlandQuad(
            vertices,
            uvs,
            triangles,
            -FarmlandVisualHalfExtent,
            FarmlandVisualHalfExtent,
            -FarmlandVisualHalfExtent,
            FarmlandVisualHalfExtent,
            0f);

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

        mapSaveData.plantedResources ??= new List<PlantedResourceSaveEntry>();
        mapSaveData.plantedResources.Clear();
        foreach (KeyValuePair<Vector2Int, int> pair in plantedSeedItemIds)
        {
            mapSaveData.plantedResources.Add(new PlantedResourceSaveEntry
            {
                coordinate = pair.Key,
                seedItemId = pair.Value
            });
        }
    }

    private void ApplyFarmlandSaveState(MapSaveData mapSaveData)
    {
        farmlandCoordinates.Clear();
        plantedSeedItemIds.Clear();
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

        if (mapSaveData.plantedResources == null)
        {
            return;
        }

        for (int i = 0; i < mapSaveData.plantedResources.Count; i++)
        {
            PlantedResourceSaveEntry entry = mapSaveData.plantedResources[i];
            if (entry != null
                && entry.seedItemId >= 0
                && farmlandCoordinates.Contains(entry.coordinate))
            {
                plantedSeedItemIds[entry.coordinate] = entry.seedItemId;
            }
        }
    }

    private void ClearFarmlandPersistentState()
    {
        farmlandCoordinates.Clear();
        plantedSeedItemIds.Clear();
    }
}
