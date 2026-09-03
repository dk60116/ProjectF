using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class TerrainGenerator : MonoBehaviour
{
    internal const string FarmlandVisualName = "FarmlandSurface";
    private const string FarmlandMaterialResourcePath = "Materials/M_FarmlandSurface";
    private const float FarmlandSurfaceOffset = 0.008f;
    private const float FarmlandVisualHalfExtent = 1f;
    private const float DefaultFarmlandFertilizerCapacityPerTile = 100f;
    private const float FarmlandFertilizerEpsilon = 0.0001f;
    private const float FarmlandFertilizerAbsorptionInterval = 0.5f;
    private static readonly Vector2Int[] FarmlandConnectionDirections =
    {
        Vector2Int.left,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.up
    };
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
    private static readonly System.Predicate<int> FertilizerItemFilter =
        IsFertilizerItemId;
    private readonly HashSet<Vector2Int> farmlandCoordinates = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, float> farmlandFertilizerEnergyByCoordinate =
        new Dictionary<Vector2Int, float>();
    private readonly Dictionary<Vector2Int, int> plantedSeedItemIds =
        new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, Transform> farmlandVisuals =
        new Dictionary<Vector2Int, Transform>();
    private readonly Queue<Vector2Int> farmlandNetworkQueue = new Queue<Vector2Int>(32);
    private readonly HashSet<Vector2Int> farmlandNetworkVisited = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> farmlandAbsorptionVisited = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> farmlandNetworkCoordinates = new List<Vector2Int>(32);
    private readonly List<Vector2Int> farmlandNotificationCoordinates = new List<Vector2Int>(32);
    [Header("Farmland Fertilizer")]
    [SerializeField, Min(0.01f)]
    private float farmlandFertilizerCapacityPerTile =
        DefaultFarmlandFertilizerCapacityPerTile;
    private float nextFarmlandFertilizerAbsorptionTime;
    private Mesh farmlandVisualMesh;
    private Material farmlandVisualMaterial;
    private MaterialPropertyBlock farmlandVisualPropertyBlock;

    public bool IsFarmlandAt(Vector2Int coordinate)
    {
        return farmlandCoordinates.Contains(coordinate);
    }

    public bool TryCollectConnectedFarmlandCoordinates(
        Vector2Int startCoordinate,
        List<Vector2Int> results)
    {
        return results != null
               && CollectConnectedFarmland(startCoordinate, results);
    }

    public float FarmlandFertilizerCapacityPerTile => Mathf.Max(
        0.01f,
        farmlandFertilizerCapacityPerTile);

    public bool TryGetFarmlandFertilizerNetworkStatus(
        Vector2Int coordinate,
        out float storedEnergy,
        out float capacity,
        out int connectedTileCount)
    {
        storedEnergy = 0f;
        capacity = 0f;
        connectedTileCount = 0;
        if (!CollectConnectedFarmland(coordinate, farmlandNetworkCoordinates))
        {
            return false;
        }

        connectedTileCount = farmlandNetworkCoordinates.Count;
        float capacityPerTile = FarmlandFertilizerCapacityPerTile;
        capacity = connectedTileCount * capacityPerTile;
        for (int i = 0; i < farmlandNetworkCoordinates.Count; i++)
        {
            if (farmlandFertilizerEnergyByCoordinate.TryGetValue(
                    farmlandNetworkCoordinates[i],
                    out float tileEnergy))
            {
                storedEnergy += Mathf.Clamp(tileEnergy, 0f, capacityPerTile);
            }
        }

        storedEnergy = Mathf.Min(storedEnergy, capacity);
        return true;
    }

    public bool CanAbsorbDroppedFarmlandFertilizer(
        Vector2Int coordinate,
        int itemId)
    {
        return TryResolveFertilizerEnergy(itemId, out _)
               && CollectConnectedFarmland(coordinate, farmlandNetworkCoordinates)
               && GetAvailableFarmlandFertilizerCapacity(farmlandNetworkCoordinates)
               > FarmlandFertilizerEpsilon;
    }

    public bool IsFarmlandFertilizerItemAt(
        Vector2Int coordinate,
        int itemId)
    {
        return farmlandCoordinates.Contains(coordinate)
               && TryResolveFertilizerEnergy(itemId, out _);
    }

    public bool TryAbsorbDroppedFarmlandFertilizer(
        Vector2Int coordinate,
        int itemId)
    {
        if (!TryResolveFertilizerEnergy(itemId, out float fertilizerEnergy)
            || !CollectConnectedFarmland(coordinate, farmlandNetworkCoordinates)
            || GetAvailableFarmlandFertilizerCapacity(farmlandNetworkCoordinates)
            <= FarmlandFertilizerEpsilon)
        {
            return false;
        }

        CopyFarmlandNetworkForNotification();
        float stored = StoreFertilizerInCollectedNetwork(
            farmlandNetworkCoordinates,
            fertilizerEnergy);
        if (stored <= FarmlandFertilizerEpsilon)
        {
            return false;
        }

        RefreshLoadedPlantsForFarmlandNetwork(farmlandNotificationCoordinates);
        return true;
    }

    public bool TryConsumeFarmlandFertilizer(
        Vector2Int coordinate,
        float requestedEnergy,
        out float consumedEnergy)
    {
        consumedEnergy = 0f;
        if (requestedEnergy <= FarmlandFertilizerEpsilon
            || !CollectConnectedFarmland(coordinate, farmlandNetworkCoordinates))
        {
            return false;
        }

        float remaining = requestedEnergy;
        for (int i = 0;
             i < farmlandNetworkCoordinates.Count
             && remaining > FarmlandFertilizerEpsilon;
             i++)
        {
            Vector2Int networkCoordinate = farmlandNetworkCoordinates[i];
            if (!farmlandFertilizerEnergyByCoordinate.TryGetValue(
                    networkCoordinate,
                    out float storedEnergy)
                || storedEnergy <= FarmlandFertilizerEpsilon)
            {
                continue;
            }

            float consumed = Mathf.Min(remaining, storedEnergy);
            float updatedEnergy = Mathf.Max(0f, storedEnergy - consumed);
            if (updatedEnergy <= FarmlandFertilizerEpsilon)
            {
                farmlandFertilizerEnergyByCoordinate.Remove(networkCoordinate);
            }
            else
            {
                farmlandFertilizerEnergyByCoordinate[networkCoordinate] = updatedEnergy;
            }

            remaining -= consumed;
        }

        consumedEnergy = requestedEnergy - remaining;
        if (consumedEnergy > FarmlandFertilizerEpsilon)
        {
            nextFarmlandFertilizerAbsorptionTime = Mathf.Min(
                nextFarmlandFertilizerAbsorptionTime,
                Time.time);
        }

        return consumedEnergy > FarmlandFertilizerEpsilon;
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
        RefreshTerrainRangeCulling(spawnedResource.transform);
        return true;
    }

    public bool CanPlantSeedAt(Vector2Int coordinate, ItemDefinition seedDefinition)
    {
        if (!ItemDefinition.IsPlantableSeedDefinition(seedDefinition)
            || !farmlandCoordinates.Contains(coordinate))
        {
            return false;
        }

        if (TryGetLoadedBlock(coordinate, out Block loadedBlock) && loadedBlock != null)
        {
            return CanPlantSeed(loadedBlock, seedDefinition);
        }

        EnsureResourceStateStore();
        return resourceStateStore != null
               && resourceStateStore.IsSavedCoordinateEmptyGround(coordinate);
    }

    public bool TryPlantSeedAt(Vector2Int coordinate, ItemDefinition seedDefinition)
    {
        if (TryGetLoadedBlock(coordinate, out Block loadedBlock) && loadedBlock != null)
        {
            return TryPlantSeed(loadedBlock, seedDefinition);
        }

        if (!CanPlantSeedAt(coordinate, seedDefinition))
        {
            return false;
        }

        Resource resourcePrefab = seedDefinition.seedTargetResource.prefab;
        int resourceItemId = resourcePrefab.ResolveItemId();
        if (resourceItemId < 0)
        {
            return false;
        }

        Resource.ResourceSaveState state = resourcePrefab.CaptureState();
        state.resourceCount = Mathf.Max(1, state.resourceCount);
        state.initialResourceCount = Mathf.Max(1, state.initialResourceCount, state.resourceCount);
        state.maxGauge = Mathf.Max(1, state.maxGauge);
        state.currentGauge = Mathf.Clamp(state.currentGauge, 1, state.maxGauge);
        if (resourcePrefab is ProjectF.MapObjects.Tree)
        {
            state.hasGrowth = true;
            state.growth = ResourceDefinition.MinGrowth;
            state.hasPlantGrowthState = true;
            state.growthWaterLiters = 0f;
            state.growthFertilizerAmount = 0f;
            state.growthElapsedSeconds = 0f;
        }

        plantedSeedItemIds[coordinate] = seedDefinition.id;
        resourceStateStore.UpdateSavedResourceState(coordinate, resourceItemId, state);
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

    public bool TryGetPlantedSeedDefinitionAt(
        Vector2Int coordinate,
        out ItemDefinition seedDefinition)
    {
        seedDefinition = null;
        return plantedSeedItemIds.TryGetValue(coordinate, out int seedItemId)
               && TryResolvePlantedSeedDefinition(seedItemId, out seedDefinition);
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
            farmlandFertilizerEnergyByCoordinate.Remove(block.Coordinate);
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

    private bool CollectConnectedFarmland(
        Vector2Int startCoordinate,
        List<Vector2Int> results)
    {
        results.Clear();
        farmlandNetworkQueue.Clear();
        farmlandNetworkVisited.Clear();
        if (!farmlandCoordinates.Contains(startCoordinate))
        {
            return false;
        }

        farmlandNetworkVisited.Add(startCoordinate);
        farmlandNetworkQueue.Enqueue(startCoordinate);
        while (farmlandNetworkQueue.Count > 0)
        {
            Vector2Int coordinate = farmlandNetworkQueue.Dequeue();
            results.Add(coordinate);
            for (int i = 0; i < FarmlandConnectionDirections.Length; i++)
            {
                Vector2Int neighbor = coordinate + FarmlandConnectionDirections[i];
                if (farmlandCoordinates.Contains(neighbor)
                    && farmlandNetworkVisited.Add(neighbor))
                {
                    farmlandNetworkQueue.Enqueue(neighbor);
                }
            }
        }

        farmlandNetworkQueue.Clear();
        farmlandNetworkVisited.Clear();
        return results.Count > 0;
    }

    private float GetAvailableFarmlandFertilizerCapacity(
        IReadOnlyList<Vector2Int> networkCoordinates)
    {
        if (networkCoordinates == null || networkCoordinates.Count <= 0)
        {
            return 0f;
        }

        float capacityPerTile = FarmlandFertilizerCapacityPerTile;
        float available = 0f;
        for (int i = 0; i < networkCoordinates.Count; i++)
        {
            farmlandFertilizerEnergyByCoordinate.TryGetValue(
                networkCoordinates[i],
                out float storedEnergy);
            available += Mathf.Max(0f, capacityPerTile - storedEnergy);
        }

        return available;
    }

    private float StoreFertilizerInCollectedNetwork(
        IReadOnlyList<Vector2Int> networkCoordinates,
        float fertilizerEnergy)
    {
        if (networkCoordinates == null
            || networkCoordinates.Count <= 0
            || fertilizerEnergy <= FarmlandFertilizerEpsilon)
        {
            return 0f;
        }

        float capacityPerTile = FarmlandFertilizerCapacityPerTile;
        float remaining = fertilizerEnergy;
        for (int i = 0;
             i < networkCoordinates.Count && remaining > FarmlandFertilizerEpsilon;
             i++)
        {
            Vector2Int coordinate = networkCoordinates[i];
            farmlandFertilizerEnergyByCoordinate.TryGetValue(
                coordinate,
                out float storedEnergy);
            float accepted = Mathf.Min(
                remaining,
                Mathf.Max(0f, capacityPerTile - storedEnergy));
            if (accepted <= FarmlandFertilizerEpsilon)
            {
                continue;
            }

            farmlandFertilizerEnergyByCoordinate[coordinate] = storedEnergy + accepted;
            remaining -= accepted;
        }

        return fertilizerEnergy - remaining;
    }

    private void CopyFarmlandNetworkForNotification()
    {
        farmlandNotificationCoordinates.Clear();
        farmlandNotificationCoordinates.AddRange(farmlandNetworkCoordinates);
    }

    private void RefreshLoadedPlantsForFarmlandNetwork(
        IReadOnlyList<Vector2Int> networkCoordinates)
    {
        if (networkCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < networkCoordinates.Count; i++)
        {
            if (TryGetLoadedBlock(networkCoordinates[i], out Block block)
                && block?.Resource is ProjectF.MapObjects.Tree tree
                && tree.gameObject.activeInHierarchy)
            {
                tree.RefreshFarmlandFertilizerConsumption();
            }
        }
    }

    private static bool TryResolveFertilizerEnergy(int itemId, out float energy)
    {
        energy = 0f;
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (itemManager == null
            || itemId < 0
            || !itemManager.TryGetItemDefinitionById(
                itemId,
                out ItemDefinition definition)
            || !ItemDefinition.IsFertilizerEnergyItemDefinition(definition))
        {
            return false;
        }

        energy = Mathf.Max(0f, definition.energyAmount);
        return energy > FarmlandFertilizerEpsilon;
    }

    private static bool IsFertilizerItemId(int itemId)
    {
        return TryResolveFertilizerEnergy(itemId, out _);
    }

    private void TickFarmlandFertilizerAbsorption()
    {
        if (!Application.isPlaying
            || Time.time < nextFarmlandFertilizerAbsorptionTime)
        {
            return;
        }

        nextFarmlandFertilizerAbsorptionTime =
            Time.time + FarmlandFertilizerAbsorptionInterval;
        if (farmlandCoordinates.Count <= 0)
        {
            return;
        }

        EnsureResourceStateStore();
        farmlandAbsorptionVisited.Clear();
        foreach (Vector2Int startCoordinate in farmlandCoordinates)
        {
            if (farmlandAbsorptionVisited.Contains(startCoordinate)
                || !CollectConnectedFarmland(
                    startCoordinate,
                    farmlandNetworkCoordinates))
            {
                continue;
            }

            for (int i = 0; i < farmlandNetworkCoordinates.Count; i++)
            {
                farmlandAbsorptionVisited.Add(farmlandNetworkCoordinates[i]);
            }

            float availableCapacity = GetAvailableFarmlandFertilizerCapacity(
                farmlandNetworkCoordinates);
            if (availableCapacity <= FarmlandFertilizerEpsilon)
            {
                continue;
            }

            bool absorbedAny = false;
            for (int coordinateIndex = 0;
                 coordinateIndex < farmlandNetworkCoordinates.Count
                 && availableCapacity > FarmlandFertilizerEpsilon;
                 coordinateIndex++)
            {
                Vector2Int coordinate = farmlandNetworkCoordinates[coordinateIndex];
                while (availableCapacity > FarmlandFertilizerEpsilon
                       && TryTakeSettledFertilizerAt(
                           coordinate,
                           out int fertilizerItemId)
                       && TryResolveFertilizerEnergy(
                           fertilizerItemId,
                           out float fertilizerEnergy))
                {
                    float stored = StoreFertilizerInCollectedNetwork(
                        farmlandNetworkCoordinates,
                        fertilizerEnergy);
                    if (stored <= FarmlandFertilizerEpsilon)
                    {
                        break;
                    }

                    absorbedAny = true;
                    availableCapacity = Mathf.Max(0f, availableCapacity - stored);
                }
            }

            if (!absorbedAny)
            {
                continue;
            }

            CopyFarmlandNetworkForNotification();
            RefreshLoadedPlantsForFarmlandNetwork(farmlandNotificationCoordinates);
        }

        farmlandAbsorptionVisited.Clear();
    }

    private bool TryTakeSettledFertilizerAt(
        Vector2Int coordinate,
        out int fertilizerItemId)
    {
        fertilizerItemId = -1;
        if (TryGetLoadedBlock(coordinate, out Block block)
            && block != null
            && !IsFloorObjectCoordinateVirtualized(coordinate))
        {
            return block.TryTakeSettledFloorObject(
                FertilizerItemFilter,
                out fertilizerItemId);
        }

        return resourceStateStore != null
               && resourceStateStore.TryTakeSavedFloorItem(
                   coordinate,
                   FertilizerItemFilter,
                   out fertilizerItemId);
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

        Vector2Int coordinate = block.Coordinate;
        farmlandVisuals.TryGetValue(coordinate, out Transform visual);
        bool visible = farmlandCoordinates.Contains(block.Coordinate);
        if (!visible)
        {
            ReleaseFarmlandVisual(coordinate);
            return;
        }

        if (visual == null)
        {
            GameObject visualObject = new GameObject(FarmlandVisualName);
            visual = visualObject.transform;
            visual.SetParent(transform, true);
            visualObject.AddComponent<MeshFilter>();
            visualObject.AddComponent<MeshRenderer>();
            farmlandVisuals[coordinate] = visual;
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
            block.WorldPosition.x,
            surfaceY + FarmlandSurfaceOffset,
            block.WorldPosition.z);
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

        Material includedMaterial = Resources.Load<Material>(
            FarmlandMaterialResourcePath);
        Shader shader = includedMaterial != null
            ? includedMaterial.shader
            : Shader.Find("ProjectF/Farmland Surface");
        if (shader == null)
        {
            Debug.LogError(
                $"Farmland material is missing at Resources/{FarmlandMaterialResourcePath}.");
            return null;
        }

        farmlandVisualMaterial = includedMaterial != null
            ? new Material(includedMaterial)
            : new Material(shader);
        farmlandVisualMaterial.name = "GeneratedFarmlandMaterial";
        farmlandVisualMaterial.hideFlags = HideFlags.DontSave;
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

        mapSaveData.farmlandFertilizer ??= new List<FarmlandFertilizerSaveEntry>();
        mapSaveData.farmlandFertilizer.Clear();
        foreach (KeyValuePair<Vector2Int, float> pair in farmlandFertilizerEnergyByCoordinate)
        {
            if (pair.Value <= FarmlandFertilizerEpsilon
                || !farmlandCoordinates.Contains(pair.Key))
            {
                continue;
            }

            mapSaveData.farmlandFertilizer.Add(new FarmlandFertilizerSaveEntry
            {
                coordinate = pair.Key,
                fertilizerEnergy = Mathf.Min(
                    FarmlandFertilizerCapacityPerTile,
                    pair.Value)
            });
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
        farmlandFertilizerEnergyByCoordinate.Clear();
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

        if (mapSaveData.farmlandFertilizer != null)
        {
            for (int i = 0; i < mapSaveData.farmlandFertilizer.Count; i++)
            {
                FarmlandFertilizerSaveEntry entry =
                    mapSaveData.farmlandFertilizer[i];
                if (entry == null
                    || entry.fertilizerEnergy <= FarmlandFertilizerEpsilon
                    || !farmlandCoordinates.Contains(entry.coordinate))
                {
                    continue;
                }

                farmlandFertilizerEnergyByCoordinate[entry.coordinate] = Mathf.Min(
                    FarmlandFertilizerCapacityPerTile,
                    entry.fertilizerEnergy);
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

    private void ReleaseFarmlandVisual(Vector2Int coordinate)
    {
        if (!farmlandVisuals.TryGetValue(coordinate, out Transform visual))
        {
            return;
        }

        farmlandVisuals.Remove(coordinate);
        if (visual == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(visual.gameObject);
        }
        else
        {
            DestroyImmediate(visual.gameObject);
        }
    }

    private void ClearFarmlandPersistentState()
    {
        foreach (KeyValuePair<Vector2Int, Transform> pair in farmlandVisuals)
        {
            if (pair.Value == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(pair.Value.gameObject);
            }
            else
            {
                DestroyImmediate(pair.Value.gameObject);
            }
        }

        farmlandVisuals.Clear();
        farmlandCoordinates.Clear();
        farmlandFertilizerEnergyByCoordinate.Clear();
        plantedSeedItemIds.Clear();
        farmlandNetworkQueue.Clear();
        farmlandNetworkVisited.Clear();
        farmlandAbsorptionVisited.Clear();
        farmlandNetworkCoordinates.Clear();
        farmlandNotificationCoordinates.Clear();
        nextFarmlandFertilizerAbsorptionTime = 0f;
    }
}
