using System.Collections.Generic;
using UnityEngine;

public partial class TerrainGenerator
{
    public enum MapMarkerLayer : byte
    {
        Resource = 1,
        Object = 2,
        Rail = 3,
        Train = 4
    }

    public struct MapMarkerSample
    {
        public Color32 color;
        public MapMarkerSize size;
        public Vector2Int footprintMinimum;
        public Vector2Int footprintMaximum;
        public MapMarkerLayer layer;
    }

    private readonly HashSet<Vector2Int> mapMarkerStorageKeyScratch = new HashSet<Vector2Int>();
    private readonly List<Train> liveTrainMapScratch = new List<Train>(8);

    public bool TryGetMapMarkerColor32At(Vector2Int coordinate, out Color32 color)
    {
        return TryGetMapMarkerColor32At(coordinate, out color, out _);
    }

    public bool TryGetMapMarkerColor32At(
        Vector2Int coordinate,
        out Color32 color,
        out MapMarkerSize markerSize)
    {
        if (TryGetMapMarkerSampleAt(coordinate, out MapMarkerSample marker))
        {
            color = marker.color;
            markerSize = marker.size;
            return true;
        }

        color = default;
        markerSize = MapMarkerSize.Middle;
        return false;
    }

    public bool TryGetMapMarkerSampleAt(Vector2Int coordinate, out MapMarkerSample marker)
    {
        if (TryGetMapResourceColor32At(coordinate, out Color32 color, out MapMarkerSize markerSize))
        {
            marker = CreateMapMarkerSample(
                color,
                markerSize,
                coordinate,
                coordinate,
                MapMarkerLayer.Resource);
            return true;
        }

        return TryGetMapObjectMarkerAt(coordinate, out marker);
    }

    public int CollectMapMarkerSamplesAt(
        Vector2Int coordinate,
        List<MapMarkerSample> markers)
    {
        if (markers == null)
        {
            return 0;
        }

        int initialCount = markers.Count;
        if (TryGetMapResourceColor32At(coordinate, out Color32 color, out MapMarkerSize markerSize))
        {
            markers.Add(CreateMapMarkerSample(
                color,
                markerSize,
                coordinate,
                coordinate,
                MapMarkerLayer.Resource));
        }

        int installationMarkerCount = 0;
        mapMarkerStorageKeyScratch.Clear();
        resourceStateStore?.CollectInstallationStorageKeysAtCoordinate(
            coordinate,
            mapMarkerStorageKeyScratch);
        foreach (Vector2Int storageKey in mapMarkerStorageKeyScratch)
        {
            if (!TryGetStoredMapObjectMarkerAt(storageKey, coordinate, out MapMarkerSample marker))
            {
                continue;
            }

            markers.Add(marker);
            installationMarkerCount++;
        }

        mapMarkerStorageKeyScratch.Clear();
        if (installationMarkerCount == 0
            && TryGetMapObjectMarkerAt(coordinate, out MapMarkerSample fallbackMarker))
        {
            markers.Add(fallbackMarker);
        }

        return markers.Count - initialCount;
    }

    public int CollectLiveTrainMapMarkers(List<MapMarkerSample> markers)
    {
        if (markers == null)
        {
            return 0;
        }

        int initialCount = markers.Count;
        liveTrainMapScratch.Clear();
        Train.CollectActiveRuntimeTrains(liveTrainMapScratch);
        for (int i = 0; i < liveTrainMapScratch.Count; i++)
        {
            Train train = liveTrainMapScratch[i];
            ItemDefinition definition = train.BoundItemDefinition
                                        ?? ResolveMapItemDefinition(train.ResolveItemId());
            if (!TryGetItemMapMarker(definition, out Color32 color, out MapMarkerSize markerSize))
            {
                continue;
            }

            Vector3 position = train.transform.position;
            Vector2Int coordinate = new Vector2Int(
                Mathf.RoundToInt(position.x),
                Mathf.RoundToInt(position.z));
            GetMapSizeBounds(
                train,
                coordinate,
                train.RuntimeQuarterTurns,
                out Vector2Int minimum,
                out Vector2Int maximum);
            markers.Add(CreateMapMarkerSample(
                color,
                markerSize,
                minimum,
                maximum,
                MapMarkerLayer.Train));
        }

        liveTrainMapScratch.Clear();
        return markers.Count - initialCount;
    }

    // Read only known runtime/saved resources. Looking at the map must not generate
    // chunks, instantiate block proxies, or evaluate resource noise for every pixel.
    public bool TryGetMapResourceColor32At(Vector2Int coordinate, out Color32 color)
    {
        return TryGetMapResourceColor32At(coordinate, out color, out _);
    }

    private bool TryGetMapResourceColor32At(
        Vector2Int coordinate,
        out Color32 color,
        out MapMarkerSize markerSize)
    {
        color = default;
        markerSize = MapMarkerSize.Middle;
        if (loadedBlocks.TryGetValue(coordinate, out Block block) && block != null)
        {
            Resource resource = block.Resource;
            if (resource != null)
            {
                ResourceDefinition definition = resource.Definition;
                if (definition == null)
                {
                    definition = ResolveMapResourceDefinition(resource.ResolveItemId());
                }

                if (resource.ResourceCount <= 0
                    || definition == null
                    || !definition.TryGetMapColor32(out color))
                {
                    return false;
                }

                markerSize = definition.MarkerSize;
                return true;
            }
        }

        if (resourceStateStore == null
            || !resourceStateStore.TryGetSavedResourceState(coordinate, out int itemId, out Resource.ResourceSaveState state)
            || state.resourceCount <= 0)
        {
            return false;
        }

        // A planted resource may differ from the natural resource at this position.
        ResourceDefinition savedDefinition = TryGetPlantedSeedDefinitionAt(coordinate, out ItemDefinition seed)
            ? seed.seedTargetResource
            : ResolveMapResourceDefinition(itemId);
        if (savedDefinition == null || !savedDefinition.TryGetMapColor32(out color))
        {
            return false;
        }

        markerSize = savedDefinition.MarkerSize;
        return true;
    }

    private ResourceDefinition ResolveMapResourceDefinition(int itemId)
    {
        ItemManager manager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (manager == null
            || !manager.TryGetItemDefinitionById(itemId, out ItemDefinition item)
            || !(item.mapObject is Resource prefab))
        {
            return null;
        }

        // Terrain entries carry the authoritative ResourceDefinition even when a
        // legacy prefab has not yet been assigned its own definition reference.
        if (TryGetMatchingResourceEntry(prefab, oreResources, out ResourceEntry entry, out _)
            || TryGetMatchingResourceEntry(prefab, oilResources, out entry, out _)
            || TryGetMatchingResourceEntry(prefab, treeResources, out entry, out _)
            || TryGetMatchingResourceEntry(prefab, reedResources, out entry, out _))
        {
            return entry.definition != null ? entry.definition : prefab.Definition;
        }

        return prefab.Definition;
    }

    private bool TryGetMapObjectMarkerAt(Vector2Int coordinate, out MapMarkerSample marker)
    {
        marker = default;
        if (loadedBlocks.TryGetValue(coordinate, out Block block) && block != null)
        {
            MapObject mapObject = block.MapObject;
            if (mapObject != null && !(mapObject is Resource))
            {
                if (mapObject is Train)
                {
                    // Moving trains are rendered from their current Transform in one
                    // batched pass, rather than from a stale block/state coordinate.
                    return false;
                }

                InstallationObject installation = mapObject as InstallationObject;
                if (installation == null
                    || installation is Railload
                    || IsWithinMapSize(
                        mapObject,
                        installation.RuntimeAnchorCoordinate,
                        installation.RuntimeQuarterTurns,
                        coordinate))
                {
                    ItemDefinition liveDefinition = mapObject.BoundItemDefinition
                                                    ?? ResolveMapItemDefinition(mapObject.ResolveItemId());
                    if (!TryGetItemMapMarker(liveDefinition, out Color32 color, out MapMarkerSize markerSize))
                    {
                        return false;
                    }

                    if (installation is Railload)
                    {
                        Railload rail = (Railload)installation;
                        if (!IsRailMapPathCoordinate(rail.RuntimeVisualPathPoints, coordinate))
                        {
                            return false;
                        }

                        marker = CreateMapMarkerSample(
                            color,
                            markerSize,
                            coordinate,
                            coordinate,
                            MapMarkerLayer.Rail);
                        return true;
                    }

                    GetMapSizeBounds(
                        mapObject,
                        installation != null ? installation.RuntimeAnchorCoordinate : coordinate,
                        installation != null ? installation.RuntimeQuarterTurns : 0,
                        out Vector2Int minimum,
                        out Vector2Int maximum);
                    marker = CreateMapMarkerSample(
                        color,
                        markerSize,
                        minimum,
                        maximum,
                        ResolveMapMarkerLayer(mapObject));
                    return true;
                }
            }
        }

        if (resourceStateStore == null
            || !resourceStateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int storageKey))
        {
            return false;
        }

        return TryGetStoredMapObjectMarkerAt(storageKey, coordinate, out marker);
    }

    private bool TryGetStoredMapObjectMarkerAt(
        Vector2Int storageKey,
        Vector2Int coordinate,
        out MapMarkerSample marker)
    {
        marker = default;
        if (resourceStateStore.TryGetLiveInstallationReadOnly(
                storageKey,
                out InstallationObject liveInstallation,
                out _))
        {
            if (liveInstallation is Train)
            {
                return false;
            }

            ItemDefinition liveDefinition = liveInstallation.BoundItemDefinition
                                            ?? ResolveMapItemDefinition(liveInstallation.ResolveItemId());
            bool isLiveRail = liveInstallation is Railload;
            bool isWithinLiveMarker = isLiveRail
                ? IsRailMapPathCoordinate(((Railload)liveInstallation).RuntimeVisualPathPoints, coordinate)
                : IsWithinMapSize(
                    liveInstallation,
                    liveInstallation.RuntimeAnchorCoordinate,
                    liveInstallation.RuntimeQuarterTurns,
                    coordinate);
            if (!isWithinLiveMarker
                || !TryGetItemMapMarker(liveDefinition, out Color32 color, out MapMarkerSize markerSize))
            {
                return false;
            }

            if (isLiveRail)
            {
                marker = CreateMapMarkerSample(
                    color,
                    markerSize,
                    coordinate,
                    coordinate,
                    MapMarkerLayer.Rail);
                return true;
            }

            GetMapSizeBounds(
                liveInstallation,
                liveInstallation.RuntimeAnchorCoordinate,
                liveInstallation.RuntimeQuarterTurns,
                out Vector2Int minimum,
                out Vector2Int maximum);
            marker = CreateMapMarkerSample(
                color,
                markerSize,
                minimum,
                maximum,
                ResolveMapMarkerLayer(liveInstallation));
            return true;
        }

        if (!resourceStateStore.TryGetInstallationStateReadOnly(
                storageKey,
                out BlockStateStore.InstallationSaveState state)
            || state == null)
        {
            return false;
        }

        ItemDefinition savedDefinition = ResolveMapItemDefinition(state.itemId);
        bool isSavedRail = savedDefinition != null && savedDefinition.mapObject is Railload;
        bool isWithinSavedMarker = isSavedRail
            ? IsRailMapPathCoordinate(state.railVisualPathPoints, coordinate)
            : savedDefinition != null && IsWithinMapSize(
                savedDefinition.mapObject,
                state.anchorCoordinate,
                state.quarterTurns,
                coordinate);
        if (savedDefinition == null
            || !isWithinSavedMarker
            || !TryGetItemMapMarker(savedDefinition, out Color32 savedColor, out MapMarkerSize savedMarkerSize))
        {
            return false;
        }

        if (isSavedRail)
        {
            marker = CreateMapMarkerSample(
                savedColor,
                savedMarkerSize,
                coordinate,
                coordinate,
                MapMarkerLayer.Rail);
            return true;
        }

        GetMapSizeBounds(
            savedDefinition.mapObject,
            state.anchorCoordinate,
            state.quarterTurns,
            out Vector2Int savedMinimum,
            out Vector2Int savedMaximum);
        marker = CreateMapMarkerSample(
            savedColor,
            savedMarkerSize,
            savedMinimum,
            savedMaximum,
            ResolveMapMarkerLayer(savedDefinition.mapObject));
        return true;
    }

    private static MapMarkerSample CreateMapMarkerSample(
        Color32 color,
        MapMarkerSize size,
        Vector2Int minimum,
        Vector2Int maximum,
        MapMarkerLayer layer)
    {
        return new MapMarkerSample
        {
            color = color,
            size = size,
            footprintMinimum = minimum,
            footprintMaximum = maximum,
            layer = layer
        };
    }

    private static MapMarkerLayer ResolveMapMarkerLayer(MapObject mapObject)
    {
        if (mapObject is Train)
        {
            return MapMarkerLayer.Train;
        }

        return mapObject is Railload ? MapMarkerLayer.Rail : MapMarkerLayer.Object;
    }

    private static bool IsRailMapPathCoordinate(
        IReadOnlyList<Vector2> visualPathPoints,
        Vector2Int coordinate)
    {
        if (visualPathPoints == null || visualPathPoints.Count < 2)
        {
            // Legacy rail states may only have occupied coordinates. Their coordinate
            // index remains the best available representation in that case.
            return true;
        }

        for (int i = 0; i + 1 < visualPathPoints.Count; i++)
        {
            if (RailSegmentIntersectsMapCell(
                    visualPathPoints[i],
                    visualPathPoints[i + 1],
                    coordinate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RailSegmentIntersectsMapCell(
        Vector2 from,
        Vector2 to,
        Vector2Int coordinate)
    {
        const float HalfCell = 0.4999f;
        float enter = 0f;
        float exit = 1f;
        Vector2 delta = to - from;
        return ClipRailSegmentAxis(
                   from.x,
                   delta.x,
                   coordinate.x - HalfCell,
                   coordinate.x + HalfCell,
                   ref enter,
                   ref exit)
               && ClipRailSegmentAxis(
                   from.y,
                   delta.y,
                   coordinate.y - HalfCell,
                   coordinate.y + HalfCell,
                   ref enter,
                   ref exit);
    }

    private static bool ClipRailSegmentAxis(
        float origin,
        float delta,
        float minimum,
        float maximum,
        ref float enter,
        ref float exit)
    {
        if (Mathf.Abs(delta) <= 0.000001f)
        {
            return origin >= minimum && origin <= maximum;
        }

        float first = (minimum - origin) / delta;
        float second = (maximum - origin) / delta;
        if (first > second)
        {
            float swap = first;
            first = second;
            second = swap;
        }

        enter = Mathf.Max(enter, first);
        exit = Mathf.Min(exit, second);
        return enter <= exit;
    }

    private static bool TryGetItemMapMarker(
        ItemDefinition definition,
        out Color32 color,
        out MapMarkerSize markerSize)
    {
        markerSize = MapMarkerSize.Middle;
        if (definition != null && definition.TryGetMapColor32(out color))
        {
            markerSize = definition.MarkerSize;
            return true;
        }

        color = default;
        return false;
    }

    private static bool IsWithinMapSize(
        MapObject mapObject,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int coordinate)
    {
        if (mapObject == null)
        {
            return false;
        }

        MapObject.MapObjectStatus status = mapObject.Status;
        int sizeX = Mathf.Max(1, status.mapSizeX);
        int sizeY = Mathf.Max(1, status.mapSizeY);
        Vector2Int centerCell = mapObject.PlacementCenterCell;
        Vector2Int localOffset = InputOutputModule.RotateRectGridOffset(
            coordinate - anchorCoordinate,
            -quarterTurns);
        int cellX = localOffset.x + centerCell.x;
        int cellY = localOffset.y + centerCell.y;
        return cellX >= 0 && cellX < sizeX
               && cellY >= 0 && cellY < sizeY;
    }

    private static void GetMapSizeBounds(
        MapObject mapObject,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        out Vector2Int minimum,
        out Vector2Int maximum)
    {
        if (mapObject == null)
        {
            minimum = anchorCoordinate;
            maximum = anchorCoordinate;
            return;
        }

        MapObject.MapObjectStatus status = mapObject.Status;
        int sizeX = Mathf.Max(1, status.mapSizeX);
        int sizeY = Mathf.Max(1, status.mapSizeY);
        Vector2Int centerCell = mapObject.PlacementCenterCell;
        Vector2Int firstCorner = anchorCoordinate + InputOutputModule.RotateRectGridOffset(
            new Vector2Int(-centerCell.x, -centerCell.y),
            quarterTurns);
        Vector2Int oppositeCorner = anchorCoordinate + InputOutputModule.RotateRectGridOffset(
            new Vector2Int(sizeX - 1 - centerCell.x, sizeY - 1 - centerCell.y),
            quarterTurns);
        minimum = new Vector2Int(
            Mathf.Min(firstCorner.x, oppositeCorner.x),
            Mathf.Min(firstCorner.y, oppositeCorner.y));
        maximum = new Vector2Int(
            Mathf.Max(firstCorner.x, oppositeCorner.x),
            Mathf.Max(firstCorner.y, oppositeCorner.y));
    }

    private static ItemDefinition ResolveMapItemDefinition(int itemId)
    {
        ItemManager manager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        return manager != null && manager.TryGetItemDefinitionById(itemId, out ItemDefinition definition)
            ? definition
            : null;
    }
}
