using System;
using System.Collections.Generic;
using UnityEngine;

static class Checks
{
    static int passed;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        passed++;
    }
    static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    static void Main()
    {
        var red = new Color32 { r = 230, g = 20, b = 15, a = 3 };
        var blue = new Color32 { r = 10, g = 80, b = 240, a = 255 };
        var green = new Color32 { r = 30, g = 220, b = 70, a = 255 };
        var terrainColor = new Color32 { r = 50, g = 90, b = 30, a = 255 };
        var definition = new ResourceDefinition { mapColor = red };
        Check(definition.MarkerSize == MapMarkerSize.Middle, "Default marker size must be Middle");
        definition.mapMarkerSize = MapMarkerSize.Small;
        Check(!definition.TryGetMapColor32(out _), "Default must be None");
        definition.mapColorMode = ResourceDefinition.MapColorMode.Custom;
        Check(definition.TryGetMapColor32(out var opaque) && opaque.a == 255 && opaque.r == red.r, "Custom color must be opaque");
        definition.mapColorMode = ResourceDefinition.MapColorMode.None;
        Check(!definition.TryGetMapColor32(out _) && definition.mapColor.r == red.r, "None must retain the selected color");
        definition.mapColorMode = ResourceDefinition.MapColorMode.Custom;

        var coordinate = new Vector2Int(-2, 4);
        var terrain = new TerrainGenerator();
        var prefab = new Resource { Definition = definition, ResourceCount = 12, ItemId = 7 };
        var live = new Resource { Definition = definition, ResourceCount = 12, ItemId = 7 };
        terrain.loadedBlocks[coordinate] = new Block { Resource = live };
        Check(terrain.TryGetMapResourceColor32At(coordinate, out var actual) && Same(actual, opaque), "Live resource color");
        Check(!terrain.TryGetMapResourceColor32At(new Vector2Int(1000, 1000), out _), "Unknown coordinate must not create a resource");

        GameManager.Instance = new GameManager();
        GameManager.Instance.ItemManger.items[7] = new ItemDefinition { mapObject = prefab };
        terrain.oreResources.Add(new TerrainGenerator.ResourceEntry { prefab = prefab, definition = definition });
        terrain.resourceStateStore.states[coordinate] = (7, new Resource.ResourceSaveState { resourceCount = 12 });
        live.ResourceCount = 0;
        Check(!terrain.TryGetMapResourceColor32At(coordinate, out _), "Live depletion must override old saved quantity");
        terrain.loadedBlocks.Clear();
        Check(terrain.TryGetMapResourceColor32At(coordinate, out _), "Unloaded saved resource must remain visible");
        terrain.resourceStateStore.states[coordinate] = (7, new Resource.ResourceSaveState { resourceCount = 0 });
        Check(!terrain.TryGetMapResourceColor32At(coordinate, out _), "Saved depletion must hide the marker");
        terrain.resourceStateStore.states[coordinate] = (7, new Resource.ResourceSaveState { resourceCount = 12 });
        var planted = new ResourceDefinition { mapColorMode = ResourceDefinition.MapColorMode.Custom, mapColor = blue };
        terrain.planted[coordinate] = new ItemDefinition { seedTargetResource = planted };
        Check(terrain.TryGetMapResourceColor32At(coordinate, out actual) && Same(actual, blue), "Planted definition must override natural resource identity");
        terrain.planted.Clear();
        prefab.Definition = null;
        Check(terrain.TryGetMapResourceColor32At(coordinate, out actual) && Same(actual, opaque), "Terrain definition must support legacy prefabs");

        var machineCoordinate = new Vector2Int(5, -3);
        var machineDefinition = new ItemDefinition { useMapColor = true, mapColor = blue };
        var machineSize = new MapObject.MapObjectStatus
        {
            mapSizeX = 2,
            mapSizeY = 3,
            centerCellX = 0,
            centerCellY = 1
        };
        var machinePrefab = new InstallationObject { ItemId = 20, Status = machineSize };
        machineDefinition.mapObject = machinePrefab;
        var liveMachine = new InstallationObject
        {
            BoundItemDefinition = machineDefinition,
            ItemId = 20,
            RuntimeAnchorCoordinate = machineCoordinate,
            RuntimeQuarterTurns = 0,
            Status = machineSize
        };
        terrain.loadedBlocks[machineCoordinate] = new Block { MapObject = liveMachine };
        var liveMapSizeCoordinate = new Vector2Int(6, -2);
        var liveItemAreaCoordinate = new Vector2Int(7, -3);
        terrain.resourceStateStore.liveInstallations[machineCoordinate] =
            (liveMachine, new BlockStateStore.InstallationSaveState
            {
                anchorCoordinate = machineCoordinate,
                itemId = 20
            });
        terrain.resourceStateStore.installationAnchors[liveMapSizeCoordinate] = machineCoordinate;
        terrain.resourceStateStore.installationAnchors[liveItemAreaCoordinate] = machineCoordinate;
        Check(machineDefinition.TryGetMapColor32(out actual) && Same(actual, blue), "MapObject item color");
        Check(terrain.TryGetMapMarkerColor32At(machineCoordinate, out actual) && Same(actual, blue), "Live MapObject anchor marker");
        Check(terrain.TryGetMapMarkerColor32At(liveMapSizeCoordinate, out actual) && Same(actual, blue), "Live MapObject must fill MapSize");
        Check(!terrain.TryGetMapMarkerColor32At(liveItemAreaCoordinate, out _), "Live MapObject must exclude ItemArea outside MapSize");
        terrain.loadedBlocks.Remove(machineCoordinate);
        terrain.resourceStateStore.liveInstallations.Clear();
        terrain.resourceStateStore.installationAnchors.Clear();
        GameManager.Instance.ItemManger.items[20] = machineDefinition;
        terrain.resourceStateStore.installations[machineCoordinate] = new BlockStateStore.InstallationSaveState
        {
            anchorCoordinate = machineCoordinate,
            itemId = 20,
            quarterTurns = 1
        };
        terrain.resourceStateStore.installationAnchors[machineCoordinate] = machineCoordinate;
        Check(terrain.TryGetMapMarkerColor32At(machineCoordinate, out actual) && Same(actual, blue), "Saved MapObject marker");
        var rotatedMapSizeCoordinate = new Vector2Int(6, -4);
        terrain.resourceStateStore.installationAnchors[rotatedMapSizeCoordinate] = machineCoordinate;
        Check(terrain.TryGetMapMarkerColor32At(rotatedMapSizeCoordinate, out actual) && Same(actual, blue), "Saved MapObject must rotate and fill MapSize");
        var savedItemAreaCoordinate = new Vector2Int(7, -3);
        terrain.resourceStateStore.installationAnchors[savedItemAreaCoordinate] = machineCoordinate;
        Check(!terrain.TryGetMapMarkerColor32At(savedItemAreaCoordinate, out _), "Saved MapObject must exclude ItemArea outside MapSize");
        machineDefinition.useMapColor = false;
        Check(!terrain.TryGetMapMarkerColor32At(machineCoordinate, out _), "Disabled MapObject color must hide marker");
        machineDefinition.useMapColor = true;
        machineDefinition.mapObject = null;
        Check(!machineDefinition.TryGetMapColor32(out _), "Item without MapObject must not create a marker");
        machineDefinition.mapObject = machinePrefab;

        var objectPaper = new MapPaper(terrain, machineCoordinate, terrainColor);
        machineDefinition.mapMarkerSize = MapMarkerSize.Small;
        objectPaper.Refresh(machineCoordinate);
        Check(Same(objectPaper.Pixel(2, 2), blue) && Same(objectPaper.Pixel(3, 2), blue), "Small must scale the rotated 3x2 MapSize to 2x1");
        Check(Same(objectPaper.Pixel(4, 3), terrainColor), "Small must be smaller than the MapSize footprint");
        machineDefinition.mapMarkerSize = MapMarkerSize.Middle;
        objectPaper.Refresh(machineCoordinate);
        Check(Same(objectPaper.Pixel(2, 2), blue) && Same(objectPaper.Pixel(4, 3), blue), "Middle must match the complete rotated 3x2 MapSize");
        Check(Same(objectPaper.Pixel(1, 2), terrainColor), "Middle must not expand beyond MapSize");
        machineDefinition.mapMarkerSize = MapMarkerSize.Large;
        objectPaper.Refresh(machineCoordinate);
        Check(Same(objectPaper.Pixel(1, 2), blue) && Same(objectPaper.Pixel(5, 4), blue), "Large must scale the rotated 3x2 MapSize to 5x3");
        Check(Same(objectPaper.Pixel(0, 2), terrainColor), "Large must stop at the rounded 1.5x bounds");

        var railAnchor = new Vector2Int(0, 0);
        var railCoordinates = new List<Vector2Int>
        {
            railAnchor,
            new Vector2Int(1, 0),
            new Vector2Int(2, 0),
            new Vector2Int(2, 1)
        };
        var railOccupiedCoordinates = new List<Vector2Int>(railCoordinates)
        {
            new Vector2Int(1, 1),
            new Vector2Int(1, -1)
        };
        var railVisualPathPoints = new List<Vector2>
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(2f, 0f),
            new Vector2(2f, 1f)
        };
        var railPrefab = new Railload { ItemId = 30 };
        var railDefinition = new ItemDefinition
        {
            useMapColor = true,
            mapColor = blue,
            mapMarkerSize = MapMarkerSize.Middle,
            mapObject = railPrefab
        };
        var liveRail = new Railload
        {
            BoundItemDefinition = railDefinition,
            ItemId = 30,
            RuntimeAnchorCoordinate = railAnchor
        };
        liveRail.runtimeOccupiedCoordinates.AddRange(railOccupiedCoordinates);
        liveRail.runtimeVisualPathPoints.AddRange(railVisualPathPoints);
        foreach (Vector2Int railCoordinate in railOccupiedCoordinates)
        {
            terrain.loadedBlocks[railCoordinate] = new Block { MapObject = liveRail };
        }

        Check(terrain.TryGetMapMarkerColor32At(railCoordinates[3], out actual) && Same(actual, blue), "Live rail must use every runtime path coordinate");
        Check(!terrain.TryGetMapMarkerColor32At(new Vector2Int(1, 1), out _), "Live rail map marker must exclude thick curve footprint cells away from its center path");
        var railPaper = new MapPaper(terrain, new Vector2Int(1, 0), terrainColor);
        railPaper.Refresh(new Vector2Int(1, 0));
        Check(Same(railPaper.Pixel(2, 3), blue) && Same(railPaper.Pixel(4, 4), blue), "Live rail map marker must preserve the complete bent path");
        Check(Same(railPaper.Pixel(3, 4), terrainColor), "Rail map marker must not fill the path bounding rectangle");
        foreach (Vector2Int railCoordinate in railOccupiedCoordinates)
        {
            terrain.loadedBlocks.Remove(railCoordinate);
            terrain.resourceStateStore.installationAnchors[railCoordinate] = railAnchor;
        }

        GameManager.Instance.ItemManger.items[30] = railDefinition;
        terrain.resourceStateStore.installations[railAnchor] = new BlockStateStore.InstallationSaveState
        {
            anchorCoordinate = railAnchor,
            itemId = 30,
            occupiedCoordinates = new List<Vector2Int>(railOccupiedCoordinates),
            railVisualPathPoints = new List<Vector2>(railVisualPathPoints)
        };
        railPaper.Refresh(new Vector2Int(1, 0));
        Check(Same(railPaper.Pixel(2, 3), blue) && Same(railPaper.Pixel(4, 4), blue), "Saved rail map marker must preserve the complete bent path");
        railDefinition.mapMarkerSize = MapMarkerSize.Small;
        railPaper.Refresh(new Vector2Int(1, 0));
        Check(Same(railPaper.Pixel(2, 3), blue) && Same(railPaper.Pixel(3, 3), blue)
              && Same(railPaper.Pixel(4, 3), blue) && Same(railPaper.Pixel(4, 4), blue),
            "Small rail must keep its one-cell-wide path connected");

        definition.mapMarkerSize = MapMarkerSize.Middle;
        terrain.resourceStateStore.states[railAnchor] =
            (7, new Resource.ResourceSaveState { resourceCount = 12 });
        railDefinition.mapMarkerSize = MapMarkerSize.Middle;
        var trainDefinition = new ItemDefinition
        {
            useMapColor = true,
            mapColor = green,
            mapMarkerSize = MapMarkerSize.Middle,
            mapObject = new Train { ItemId = 31 }
        };
        GameManager.Instance.ItemManger.items[31] = trainDefinition;
        var trainStorageKey = new Vector2Int(100, 100);
        terrain.resourceStateStore.installations[trainStorageKey] = new BlockStateStore.InstallationSaveState
        {
            anchorCoordinate = railAnchor,
            itemId = 31,
            occupiedCoordinates = new List<Vector2Int> { railAnchor }
        };
        railPaper.Refresh(new Vector2Int(1, 0));
        Check(Same(railPaper.Pixel(2, 3), green), "Train marker must render above rail and resource markers");
        trainDefinition.useMapColor = false;
        railPaper.Refresh(new Vector2Int(1, 0));
        Check(Same(railPaper.Pixel(2, 3), blue), "Rail marker must render above a resource when train color is disabled");
        railDefinition.useMapColor = false;
        railPaper.Refresh(new Vector2Int(1, 0));
        Check(Same(railPaper.Pixel(2, 3), opaque), "Resource marker must remain below disabled rail and train markers");

        railDefinition.useMapColor = true;
        trainDefinition.useMapColor = true;
        var movingTrain = new Train
        {
            BoundItemDefinition = trainDefinition,
            ItemId = 31,
            RuntimeAnchorCoordinate = railAnchor
        };
        movingTrain.runtimeOccupiedCoordinates.Add(railAnchor);
        movingTrain.transform.position = new Vector3(0f, 0f, 0f);
        Train.activeRuntimeTrains.Add(movingTrain);
        terrain.resourceStateStore.liveInstallations[trainStorageKey] =
            (movingTrain, terrain.resourceStateStore.installations[trainStorageKey]);
        railPaper.Refresh(new Vector2Int(1, 0));
        Check(Same(railPaper.Pixel(2, 3), green), "Active train must render from its current Transform");
        movingTrain.transform.position = new Vector3(1f, 0f, 0f);
        railPaper.Refresh(new Vector2Int(1, 0));
        Check(Same(railPaper.Pixel(2, 3), blue), "Moving train must clear its stale stored coordinate and reveal the rail");
        Check(Same(railPaper.Pixel(3, 3), green), "Moving train must render at its new Transform coordinate");
        Train.activeRuntimeTrains.Clear();
        terrain.resourceStateStore.liveInstallations.Clear();

        // Exercise the production raster composition, including a stationary update.
        definition.mapMarkerSize = MapMarkerSize.Small;
        var adjacentResourceCoordinate = new Vector2Int(coordinate.x + 1, coordinate.y);
        terrain.resourceStateStore.states[adjacentResourceCoordinate] =
            (7, new Resource.ResourceSaveState { resourceCount = 12 });
        var paper = new MapPaper(terrain, coordinate, terrainColor);
        paper.Refresh(coordinate);
        Check(Same(paper.Pixel(3, 3), opaque), "Resource must occupy its exact map pixel at negative coordinates");
        Check(Same(paper.Pixel(4, 3), terrainColor), "Small must thin adjacent resource cells with a stable sparse pattern");
        Check(Same(paper.Pixel(0, 0), terrainColor), "Other pixels must retain biome color");
        int uploads = paper.mapTexture.Uploads;
        paper.Refresh(coordinate);
        Check(paper.mapTexture.Uploads == uploads, "Unchanged markers must not upload the texture again");
        definition.mapColor = blue;
        paper.Refresh(coordinate);
        Check(Same(paper.Pixel(3, 3), blue), "Color edits must update while standing still");
        definition.mapMarkerSize = MapMarkerSize.Middle;
        paper.Refresh(coordinate);
        Check(Same(paper.Pixel(3, 3), blue), "Middle marker must match its exact source cell");
        Check(Same(paper.Pixel(2, 2), terrainColor), "Middle marker must not expand beyond its source cell");
        definition.mapMarkerSize = MapMarkerSize.Large;
        paper.Refresh(coordinate);
        Check(Same(paper.Pixel(3, 3), blue) && Same(paper.Pixel(4, 4), blue), "Large single-cell marker must round 1.5x to 2x2 pixels");
        Check(Same(paper.Pixel(2, 2), terrainColor), "Large single-cell marker must stop at its scaled bounds");
        definition.mapColorMode = ResourceDefinition.MapColorMode.None;
        paper.Refresh(coordinate);
        Check(Same(paper.Pixel(3, 3), terrainColor), "None must restore biome color, not transparent or black");
        definition.mapColorMode = ResourceDefinition.MapColorMode.Custom;
        definition.mapMarkerSize = MapMarkerSize.Small;
        paper.Refresh(coordinate);
        terrain.resourceStateStore.states[coordinate] = (7, new Resource.ResourceSaveState { resourceCount = 0 });
        paper.Refresh(coordinate);
        Check(Same(paper.Pixel(3, 3), terrainColor), "Depletion must clear an already rendered dot");
        Check(Same(paper.Pixel(4, 3), blue), "An isolated Small resource must remain visible regardless of sparse-pattern parity");
        Console.WriteLine($"PASS: {passed} map marker checks");
    }
}

public partial class ResourceDefinition
{
    public MapColorMode mapColorMode;
    public Color32 mapColor;
    public MapMarkerSize mapMarkerSize = MapMarkerSize.Middle;
    public MapMarkerSize MarkerSize => mapMarkerSize;
}
public class MapObject
{
    public struct MapObjectStatus
    {
        public byte mapSizeX, mapSizeY, centerCellX, centerCellY;
    }
    public ItemDefinition BoundItemDefinition;
    public int ItemId;
    public MapObjectStatus Status;
    public readonly Transform transform = new();
    public Vector2Int PlacementCenterCell
    {
        get
        {
            int sizeX = Math.Max(1, (int)Status.mapSizeX);
            int sizeY = Math.Max(1, (int)Status.mapSizeY);
            return new Vector2Int(
                Math.Clamp(Status.centerCellX, 0, sizeX - 1),
                Math.Clamp(Status.centerCellY, 0, sizeY - 1));
        }
    }
    public virtual int ResolveItemId() => ItemId;
}
public class Resource : MapObject
{
    public ResourceDefinition Definition;
    public int ResourceCount;
    public struct ResourceSaveState { public int resourceCount; }
}
public class InstallationObject : MapObject
{
    public Vector2Int RuntimeAnchorCoordinate;
    public int RuntimeQuarterTurns;
    public readonly List<Vector2Int> runtimeOccupiedCoordinates = new();
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => runtimeOccupiedCoordinates;
}
public class Railload : InstallationObject
{
    public readonly List<Vector2> runtimeVisualPathPoints = new();
    public IReadOnlyList<Vector2> RuntimeVisualPathPoints => runtimeVisualPathPoints;
}
public class Train : InstallationObject
{
    public static readonly List<Train> activeRuntimeTrains = new();
    public static void CollectActiveRuntimeTrains(ICollection<Train> results)
    {
        foreach (Train train in activeRuntimeTrains) results.Add(train);
    }
}
public class Block { public Resource Resource; public MapObject MapObject; }
public partial class ItemDefinition
{
    public MapObject mapObject;
    public ResourceDefinition seedTargetResource;
    public bool useMapColor;
    public Color32 mapColor;
    public MapMarkerSize mapMarkerSize = MapMarkerSize.Middle;
    public MapMarkerSize MarkerSize => mapMarkerSize;
}
public class ItemManager
{
    public readonly Dictionary<int, ItemDefinition> items = new();
    public bool TryGetItemDefinitionById(int id, out ItemDefinition item) => items.TryGetValue(id, out item);
}
public class GameManager { public static GameManager Instance; public ItemManager ItemManger = new(); }
public static class InputOutputModule
{
    public static Vector2Int RotateRectGridOffset(Vector2Int offset, int quarterTurns)
    {
        int normalized = ((quarterTurns % 4) + 4) % 4;
        return normalized switch
        {
            1 => new Vector2Int(offset.y, -offset.x),
            2 => new Vector2Int(-offset.x, -offset.y),
            3 => new Vector2Int(-offset.y, offset.x),
            _ => offset
        };
    }
}
public class BlockStateStore
{
    public sealed class InstallationSaveState
    {
        public Vector2Int anchorCoordinate;
        public int itemId;
        public int quarterTurns;
        public List<Vector2Int> occupiedCoordinates = new();
        public List<Vector2> railVisualPathPoints = new();
    }
    public readonly Dictionary<Vector2Int, (int, Resource.ResourceSaveState)> states = new();
    public readonly Dictionary<Vector2Int, InstallationSaveState> installations = new();
    public readonly Dictionary<Vector2Int, (InstallationObject installation, InstallationSaveState state)> liveInstallations = new();
    public readonly Dictionary<Vector2Int, Vector2Int> installationAnchors = new();
    public bool TryGetSavedResourceState(Vector2Int coordinate, out int id, out Resource.ResourceSaveState state)
    {
        bool found = states.TryGetValue(coordinate, out var entry); id = entry.Item1; state = entry.Item2; return found;
    }
    public bool TryGetInstallationAnchorAtCoordinate(Vector2Int coordinate, out Vector2Int key)
    {
        return installationAnchors.TryGetValue(coordinate, out key);
    }
    public void CollectInstallationStorageKeysAtCoordinate(Vector2Int coordinate, ISet<Vector2Int> storageKeys)
    {
        if (installationAnchors.TryGetValue(coordinate, out Vector2Int mappedKey)) storageKeys.Add(mappedKey);
        foreach (var pair in installations)
        {
            if (pair.Value?.occupiedCoordinates != null && pair.Value.occupiedCoordinates.Contains(coordinate)) storageKeys.Add(pair.Key);
        }
        foreach (var pair in liveInstallations)
        {
            if (pair.Value.state?.occupiedCoordinates != null && pair.Value.state.occupiedCoordinates.Contains(coordinate)) storageKeys.Add(pair.Key);
        }
    }
    public bool TryGetInstallationStateReadOnly(Vector2Int key, out InstallationSaveState state) =>
        installations.TryGetValue(key, out state);
    public bool TryGetLiveInstallationReadOnly(
        Vector2Int key,
        out InstallationObject installation,
        out InstallationSaveState state)
    {
        bool found = liveInstallations.TryGetValue(key, out var entry);
        installation = entry.installation;
        state = entry.state;
        return found;
    }
}
public partial class TerrainGenerator
{
    public readonly Dictionary<Vector2Int, Block> loadedBlocks = new();
    public readonly BlockStateStore resourceStateStore = new();
    public readonly Dictionary<Vector2Int, ItemDefinition> planted = new();
    public struct ResourceEntry { public Resource prefab; public ResourceDefinition definition; }
    public readonly List<ResourceEntry> oreResources = new(), oilResources = new(), treeResources = new(), reedResources = new();
    public bool TryGetPlantedSeedDefinitionAt(Vector2Int coordinate, out ItemDefinition item) => planted.TryGetValue(coordinate, out item);
    static bool TryGetMatchingResourceEntry(Resource prefab, List<ResourceEntry> entries, out ResourceEntry entry, out int index)
    {
        for (int i = 0; i < entries.Count; i++) if (entries[i].prefab == prefab) { entry = entries[i]; index = i; return true; }
        entry = default; index = -1; return false;
    }
}
public partial class MapPaper
{
    readonly struct MapMarkerStampKey : IEquatable<MapMarkerStampKey>
    {
        readonly Vector2Int coordinate;
        readonly TerrainGenerator.MapMarkerLayer layer;
        public MapMarkerStampKey(Vector2Int coordinate, TerrainGenerator.MapMarkerLayer layer)
        {
            this.coordinate = coordinate;
            this.layer = layer;
        }
        public bool Equals(MapMarkerStampKey other) => coordinate == other.coordinate && layer == other.layer;
        public override bool Equals(object obj) => obj is MapMarkerStampKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(coordinate, layer);
    }
    readonly TerrainGenerator boundTerrain;
    readonly Vector2Int lastTextureSize = new(7, 7), viewRadius = new(3, 3);
    readonly int texturePadding = 0;
    readonly Color32[] pixelBuffer = new Color32[49], biomePixelBuffer = new Color32[49];
    readonly Color32[] composedPixelBuffer = new Color32[49];
    readonly int[] markerDistanceBuffer = new int[49];
    readonly byte[] markerLayerBuffer = new byte[49];
    readonly Color32[] smallResourceColorBuffer = new Color32[49];
    readonly byte[] smallResourceSourceBuffer = new byte[49];
    readonly List<TerrainGenerator.MapMarkerSample> mapMarkerSampleScratch = new(4);
    readonly HashSet<MapMarkerStampKey> stampedMarkerKeys = new();
    public readonly Texture2D mapTexture = new();
    float nextMarkerRefreshTime;
    const float MarkerRefreshInterval = .5f;
    public MapPaper(TerrainGenerator terrain, Vector2Int center, Color32 biome)
    {
        boundTerrain = terrain;
        Array.Fill(biomePixelBuffer, biome);
    }
    public void Refresh(Vector2Int center) => RefreshMapMarkers(center);
    public Color32 Pixel(int x, int y) => pixelBuffer[y * 7 + x];
}
namespace UnityEngine
{
    public struct Color32 { public byte r, g, b, a; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public class Transform { public Vector3 position; }
    public readonly struct Vector2
    {
        public readonly float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 operator -(Vector2 left, Vector2 right) =>
            new(left.x - right.x, left.y - right.y);
    }
    public readonly struct Vector2Int : IEquatable<Vector2Int>
    {
        public readonly int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object other) => other is Vector2Int value && Equals(value);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static bool operator ==(Vector2Int left, Vector2Int right) => left.Equals(right);
        public static bool operator !=(Vector2Int left, Vector2Int right) => !left.Equals(right);
        public static Vector2Int operator -(Vector2Int left, Vector2Int right) =>
            new(left.x - right.x, left.y - right.y);
        public static Vector2Int operator +(Vector2Int left, Vector2Int right) =>
            new(left.x + right.x, left.y + right.y);
    }
    public static class Mathf
    {
        public static int Max(int left, int right) => Math.Max(left, right);
        public static int Min(int left, int right) => Math.Min(left, right);
        public static int Abs(int value) => Math.Abs(value);
        public static int Clamp(int value, int minimum, int maximum) => Math.Clamp(value, minimum, maximum);
        public static int RoundToInt(float value) => (int)MathF.Round(value);
        public static int CeilToInt(float value) => (int)MathF.Ceiling(value);
        public static float Max(float left, float right) => MathF.Max(left, right);
        public static float Min(float left, float right) => MathF.Min(left, right);
        public static float Abs(float value) => MathF.Abs(value);
    }
    public static class Time { public static float unscaledTime; }
    public class Texture2D
    {
        public int Uploads;
        public void SetPixels32(Color32[] pixels) { Uploads++; }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
    }
}
