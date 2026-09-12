using System;
using UnityEngine;
using ProjectF.Animals;

public static partial class AnimalGridPathfinder
{
    // A bounded cache shared by herd members. Search scratch remains synchronous.
    private const int RegionCacheCapacity = 32;
    private static readonly Region[] regions = new Region[RegionCacheCapacity];
    private static long regionAccess;
    public static long SchedulingWorkCount { get; private set; }

    private sealed class Region
    {
        internal TerrainGenerator terrain;
        internal Vector3 center;
        internal float radius;
        internal long revision, lastUse;
        internal int[] component = Array.Empty<int>(), members = Array.Empty<int>(), starts = Array.Empty<int>(), counts = Array.Empty<int>(), shoreTarget = Array.Empty<int>();
        internal int count;
    }

    public static void ClearRegionCache()
    {
        for (int i = 0; i < regions.Length; i++) regions[i] = null;
        regionAccess = 0;
    }

    private static Region GetRegion(TerrainGenerator terrain, Vector3 center, float radius, float y)
    {
        Region region = null;
        int replacement = 0;
        long oldest = long.MaxValue;
        for (int i = 0; i < regions.Length; i++)
        {
            Region candidate = regions[i];
            if (candidate != null && candidate.terrain == terrain && candidate.center.x == center.x
                && candidate.center.z == center.z && candidate.radius == radius)
            { region = candidate; break; }
            long access = candidate != null ? candidate.lastUse : long.MinValue;
            if (access < oldest) { oldest = access; replacement = i; }
        }
        if (region != null && region.revision == terrain.AnimalNavigationRevision)
        {
            region.lastUse = ++regionAccess;
            AnimalAIProfiler.Add(AnimalAIProfiler.Counter.RegionCacheHits);
            return region;
        }
        if (region == null) region = regions[replacement] ?? (regions[replacement] = new Region());
        region.terrain = terrain;
        region.center = center;
        region.radius = radius;
        region.revision = terrain.AnimalNavigationRevision;
        region.lastUse = ++regionAccess;
        BuildRegion(region, y);
        AnimalAIProfiler.Add(AnimalAIProfiler.Counter.RegionCacheBuilds);
        return region;
    }

    private static void BuildRegion(Region region, float y)
    {
        using var scope = AnimalAIProfiler.Sample("Animal Connected Area Build");
        int size = gridWidth * gridHeight;
        if (region.component.Length < size)
        {
            region.component = new int[size]; region.members = new int[size];
            region.starts = new int[size]; region.counts = new int[size]; region.shoreTarget = new int[size];
        }
        for (int i = 0; i < size; i++)
        {
            GetCoordinate(i, out int x, out int z);
            region.component[i] = IsInsideArea(x, z, region.center, region.radius)
                && IsWalkable(region.terrain, x, z, y, false) ? -1 : -2;
            region.shoreTarget[i] = -1;
        }
        region.count = 0;
        int componentId = 0;
        for (int i = 0; i < size; i++)
        {
            if (region.component[i] != -1) continue;
            int start = region.count;
            int head = start;
            region.members[region.count++] = i;
            region.component[i] = componentId;
            while (head < region.count)
            {
                int current = region.members[head++];
                ExpandedNodeCount++;
                GetCoordinate(current, out int x, out int z);
                for (int n = 0; n < NeighborX.Length; n++)
                {
                    if (!TryGetIndex(x + NeighborX[n], z + NeighborZ[n], out int next)
                        || region.component[next] != -1
                        || !RegionStepClear(region, x, z, n)) continue;
                    region.component[next] = componentId;
                    region.members[region.count++] = next;
                }
            }
            region.starts[componentId] = start;
            region.counts[componentId++] = region.count - start;
        }

        // Multi-source BFS gives every connected cell its nearest shoreline in steps.
        // Shore seeds and neighbor visits have explicit coordinate order.
        int shoreHead = 0, shoreTail = 0;
        for (int i = 0; i < size; i++)
        {
            if (region.component[i] < 0) continue;
            GetCoordinate(i, out int x, out int z);
            if (!region.terrain.IsAnimalShoreCell(new Vector2Int(x, z))) continue;
            region.shoreTarget[i] = i;
            reversePath[shoreTail++] = i;
        }
        while (shoreHead < shoreTail)
        {
            int current = reversePath[shoreHead++];
            ExpandedNodeCount++;
            GetCoordinate(current, out int x, out int z);
            for (int n = 0; n < NeighborX.Length; n++)
            {
                if (!TryGetIndex(x + NeighborX[n], z + NeighborZ[n], out int next)
                    || region.component[next] < 0 || region.shoreTarget[next] >= 0
                    || !RegionStepClear(region, x, z, n)) continue;
                region.shoreTarget[next] = region.shoreTarget[current];
                reversePath[shoreTail++] = next;
            }
        }
    }

    private static bool RegionStepClear(Region region, int x, int z, int direction)
    {
        int dx = NeighborX[direction], dz = NeighborZ[direction];
        return dx == 0 || dz == 0
            || TryGetIndex(x + dx, z, out int sideX) && region.component[sideX] != -2
            && TryGetIndex(x, z + dz, out int sideZ) && region.component[sideZ] != -2;
    }

    public static int FindReachableTargetPath(TerrainGenerator terrain, Vector3 start, Vector3 areaCenter,
        float areaRadius, bool requireLoadedGround, bool requireWaterEdge, float minimumDistance,
        uint selectionSeed, Vector3[] output, out Vector3 destination)
    {
        using var sample = new AnimalAIProfiler.SearchScope(true);
        destination = start;
        if (terrain == null || output == null || output.Length == 0) return 0;
        float radius = BeginGridSearch(areaCenter, areaRadius);
        // Logical charge is identical with cold/warm caches and independent of view loading.
        SchedulingWorkCount += gridWidth * gridHeight;
        Vector2Int startCell = AnimalSimulationMath.Cell(start);
        if (!TryGetIndex(startCell.x, startCell.y, out int startIndex)) return 0;
        Region region = GetRegion(terrain, areaCenter, radius, start.y);
        int component = region.component[startIndex];
        if (component < 0)
        {
            // A newly placed obstacle may cover the animal. Permit the same escape
            // from the blocked start cell as A*, without joining disconnected regions.
            for (int n = 0; n < NeighborX.Length; n++)
                if (TryGetIndex(startCell.x + NeighborX[n], startCell.y + NeighborZ[n], out int neighbor)
                    && region.component[neighbor] >= 0
                    && IsTraversableStep(terrain, startCell.x, startCell.y, NeighborX[n], NeighborZ[n], start.y, false))
                { component = region.component[neighbor]; startIndex = neighbor; break; }
        }
        if (component < 0) return 0;
        int offset = region.starts[component], count = region.counts[component];
        int selected = -1;
        float minSqr = Mathf.Max(0f, minimumDistance); minSqr *= minSqr;
        if (requireWaterEdge && region.shoreTarget[startIndex] >= 0)
        {
            int shore = region.shoreTarget[startIndex];
            GetCoordinate(shore, out int x, out int z);
            if ((new Vector3(x, start.y, z) - start).sqrMagnitude >= minSqr) selected = shore;
        }
        if (selected < 0)
        {
            int first = requireWaterEdge ? 0 : (int)(selectionSeed % (uint)count);
            float nearestShore = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                int index = region.members[offset + (first + i) % count];
                GetCoordinate(index, out int x, out int z);
                float distance = (new Vector3(x, start.y, z) - start).sqrMagnitude;
                if (distance < minSqr || x == startCell.x && z == startCell.y) continue;
                if (requireWaterEdge)
                {
                    if (region.shoreTarget[index] != index || distance >= nearestShore) continue;
                    nearestShore = distance;
                }
                selected = index;
                if (!requireWaterEdge) break;
            }
        }
        if (selected < 0) return 0;
        GetCoordinate(selected, out int targetX, out int targetZ);
        destination = new Vector3(targetX, start.y, targetZ);
        return FindPath(terrain, start, destination, areaCenter, radius, false, output);
    }
}
