using System.Collections.Generic;
using ProjectF.Animals;
using UnityEngine;

public partial class TerrainGenerator
{
    // Terrain/occupancy data, never Renderer, activeInHierarchy, or PhysX state.
    private readonly Dictionary<Vector2Int, bool> animalWalkableCells = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<Vector2Int, bool> animalShoreCells = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<Vector2Int, int> animalAdditionalObstacleCounts = new Dictionary<Vector2Int, int>();
    private System.Func<int, int, bool> animalObstacleCellPredicate;
    public long AnimalNavigationRevision { get; private set; }

    // Data registration for static obstacles which are not installations. Balanced
    // registrations support overlapping footprints without inspecting scene colliders.
    internal void ChangeAnimalNavigationObstacle(RectInt footprint, bool add)
    {
        for (int z = footprint.yMin; z < footprint.yMax; z++)
            for (int x = footprint.xMin; x < footprint.xMax; x++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                animalAdditionalObstacleCounts.TryGetValue(cell, out int count);
                int next = Mathf.Max(0, count + (add ? 1 : -1));
                if (next == 0) animalAdditionalObstacleCounts.Remove(cell);
                else animalAdditionalObstacleCounts[cell] = next;
                if ((count == 0) != (next == 0)) InvalidateAnimalNavigation(cell);
            }
    }

    internal void InvalidateAnimalNavigation(Vector2Int coordinate)
    {
        if (animalWalkableCells.Remove(coordinate)) AnimalNavigationRevision++;
    }

    internal void InvalidateAnimalNavigation()
    {
        animalWalkableCells.Clear();
        animalShoreCells.Clear();
        AnimalNavigationRevision++;
    }

    public bool CanAnimalMoveTo(Vector3 worldPosition, bool requireLoadedBlock)
    {
        // Kept for existing callers. A view loading request does not change traversability.
        Vector2Int coordinate = AnimalSimulationMath.Cell(worldPosition);
        if (animalWalkableCells.TryGetValue(coordinate, out bool walkable))
        {
            AnimalAIProfiler.Add(AnimalAIProfiler.Counter.WalkableCacheHits);
            return walkable;
        }
        AnimalAIProfiler.Add(AnimalAIProfiler.Counter.WalkableCacheMisses);
        walkable = ReadAnimalWalkableCell(coordinate);
        animalWalkableCells.Add(coordinate, walkable);
        return walkable;
    }

    private bool ReadAnimalWalkableCell(Vector2Int coordinate)
    {
        if (animalAdditionalObstacleCounts.ContainsKey(coordinate)) return false;
        if (!IsCoordinateInsideMapBounds(coordinate) || GetTileBiome(coordinate) == TerrainBiome.Water)
            return false;
        if (TryGetLoadedBlock(coordinate, out Block block) && block != null)
            return block.MapObject == null || block.MapObject.AllowsAnimalTraversal;

        // Unloaded cells use the same persisted/generated occupancy as chunk restoration.
        if (resourceStateStore != null
            && resourceStateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int key))
        {
            if (resourceStateStore.TryGetLiveInstallationReadOnly(key, out InstallationObject live, out _)
                && live != null) return live.AllowsAnimalTraversal;
            if (resourceStateStore.TryGetInstallationStateReadOnly(key, out BlockStateStore.InstallationSaveState state))
            {
                MapObject prototype = ResolveInstallationSourcePrefab(state);
                return prototype != null && prototype.AllowsAnimalTraversal;
            }
            return false;
        }
        if (resourceStateStore != null && resourceStateStore.IsDepleted(coordinate)) return true;
        return !TryGetResourcePrefab(coordinate, out Resource resource)
               || !CanSpawnResourceAtGeneratedCoordinate(coordinate, resource)
               || resource.AllowsAnimalTraversal;
    }

    internal bool IsAnimalShoreCell(Vector2Int coordinate)
    {
        if (!animalShoreCells.TryGetValue(coordinate, out bool shore))
        {
            shore = TryGetAnimalDrinkDirection(new Vector3(coordinate.x, 0f, coordinate.y), out _);
            animalShoreCells.Add(coordinate, shore);
        }
        return shore;
    }

    internal bool IsAnimalObstaclePositionClear(Vector3 origin, Vector3 position, float radius, bool allowEscape)
    {
        using var sample = AnimalAIProfiler.Sample("Animal Grid Collision");
        AnimalAIProfiler.Add(AnimalAIProfiler.Counter.GridCollisionQueries);
        if (animalObstacleCellPredicate == null) animalObstacleCellPredicate = CanAnimalOccupyObstacleCell;
        return AnimalSimulationMath.IsGridPositionClear(origin, position, radius, allowEscape, animalObstacleCellPredicate);
    }

    private bool CanAnimalOccupyObstacleCell(int x, int z)
    {
        Vector2Int coordinate = new Vector2Int(x, z);
        // Water is blocked by the actual movement/path test, not the predictive body probe.
        // This permits approaching a shoreline to drink.
        return GetTileBiome(coordinate) == TerrainBiome.Water
               || CanAnimalMoveTo(new Vector3(x, 0f, z), false);
    }
}
