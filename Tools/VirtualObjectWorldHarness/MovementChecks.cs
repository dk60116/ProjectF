using System;
using System.Collections.Generic;
using ProjectF.MapObjects;
using UnityEngine;

internal static partial class Program
{
    private static void CheckVehicleStoreMovement()
    {
        using var world = new VirtualObjectWorld();
        var store = new BlockStateStore(world);
        foreach (Vehicle vehicle in new Vehicle[] { new Train(201), new Vehicle(202) })
        {
            var initial = CreateState(vehicle.ItemId, vehicle.GetInstanceID());
            initial.occupiedCoordinates.RemoveAt(1);
            vehicle.State = initial.Clone();
            store.Seed(vehicle, initial);
            var handle = vehicle.RuntimeMapObjectHandle;
            int markerVersion = store.MarkerVersion;
            int renderVersion = world.DataOnlyInstallationVersion;
            foreach (int x in new[] { 4, 5, 32, -33, -34, 3 })
            {
                var previousKey = store.KeyFor(vehicle);
                var previousCoordinate = vehicle.State.anchorCoordinate;
                vehicle.State.anchorCoordinate = new Vector2Int(x, 7);
                vehicle.State.hasStorageKey = false;
                vehicle.State.occupiedCoordinates[0] = vehicle.State.anchorCoordinate;
                vehicle.State.storedInstallationItemId = 99;
                vehicle.transform.position = new Vector3(x + 0.25f, 0, 7);
                vehicle.BindRuntimeMapObjectHandle(default);
                Require(store.MoveLiveVehicle(vehicle), "store relocates a registered vehicle after placement clears its handle");
                Require(vehicle.RuntimeMapObjectHandle == handle && world.IsHandleAlive(handle), "store rebinds the same live entity handle");
                Require(world.DataOnlyInstallationVersion == renderVersion, "vehicle cell crossing never invalidates static installation batches");
                if (!(vehicle is Train)) markerVersion++;
                Require(store.MarkerVersion == markerVersion, "only non-train movement invalidates static map markers");
                Require(store.LookupAt(vehicle.State.anchorCoordinate, vehicle) && !store.LookupAt(previousCoordinate, vehicle),
                    "saved and live coordinate mappings move together");
                Require(store.KeyFor(vehicle) != previousKey && !store.HasSavedKey(previousKey), "old saved storage key is released");
                Require(store.CurrentState(vehicle).worldPosition.Equals(vehicle.transform.position)
                    && store.CurrentState(vehicle).storedInstallationItemId == 99,
                    "movement persists the current pose and freshly captured cargo state");
                Require(store.ItemCount(vehicle.ItemId) == 1 && store.CargoCount(99) >= 1, "movement keeps saved installation counts consistent");
            }
            // A rail or another entity can share the physical coordinate, not the storage key.
            var obstacle = CreateState(80, 9900 + vehicle.GetInstanceID());
            obstacle.anchorCoordinate = obstacle.storageKey = new Vector2Int(40, 7);
            obstacle.occupiedCoordinates.Clear(); obstacle.occupiedCoordinates.Add(obstacle.anchorCoordinate);
            store.SeedDataOnly(obstacle);
            renderVersion = world.DataOnlyInstallationVersion;
            vehicle.State.anchorCoordinate = obstacle.anchorCoordinate;
            vehicle.State.occupiedCoordinates[0] = obstacle.anchorCoordinate;
            Require(store.MoveLiveVehicle(vehicle), "vehicle moves onto an occupied physical coordinate using a synthetic storage key");
            Require(store.KeyFor(vehicle) != obstacle.storageKey && world.TryGetInstallationHandle(obstacle.storageKey, out _),
                "overlapping movement leaves the other installation intact");
            Require(world.DataOnlyInstallationVersion == renderVersion, "synthetic-key movement preserves static presentation cache");
            Require(world.RemoveInstallation(handle), "original handle removes vehicle after repeated storage-key relocations");
            Require(!store.MoveLiveVehicle(vehicle), "stale entity cannot be revived by movement");
        }
        Require(!store.MoveLiveVehicle(new Train(301)) && !store.MoveLiveVehicle(null), "unregistered or missing vehicles use caller registration fallback");
    }
}

public class Vehicle : InstallationObject
{
    public Vehicle(int id) : base(id) { ItemId = id; }
    public int ItemId;
    public BlockStateStore.InstallationSaveState State;
    public long RuntimePlacementSequence => State?.placementSequence ?? 0;
    public MapObjectHandle RuntimeMapObjectHandle;
    public void BindRuntimeMapObjectHandle(MapObjectHandle handle) => RuntimeMapObjectHandle = handle;
}
public sealed class Train : Vehicle { public Train(int id) : base(id) { } }

// Only engine/state-capture boundaries are doubled. Movement, storage-key resolution,
// coordinate-index maintenance, counts and VirtualObjectWorld run production code.
public partial class BlockStateStore
{
    private sealed class LiveInstallationRecord
    {
        public InstallationObject installationObject;
        public InstallationSaveState state;
        public MapObjectHandle handle;
    }
    private readonly VirtualObjectWorld world;
    private readonly Dictionary<Vector2Int, InstallationSaveState> savedInstallationStates = new();
    private readonly Dictionary<Vector2Int, LiveInstallationRecord> liveInstallationStates = new();
    private readonly Dictionary<(long, int), Vector2Int> savedInstallationStorageKeysByPlacement = new();
    private readonly Dictionary<(long, int), Vector2Int> liveInstallationStorageKeysByPlacement = new();
    private readonly Dictionary<Vector2Int, Vector2Int> savedInstallationAnchorsByCoordinate = new();
    private readonly Dictionary<Vector2Int, Vector2Int> liveInstallationAnchorsByCoordinate = new();
    private readonly Dictionary<Vector2Int, HashSet<Vector2Int>> savedInstallationStorageKeysByOccupiedCoordinate = new();
    private readonly Dictionary<Vector2Int, HashSet<Vector2Int>> savedPipeInstallationStorageKeysByOccupiedCoordinate = new();
    private readonly Dictionary<int, int> savedInstallationCountsByItemId = new();
    private readonly Dictionary<int, int> savedInstallationStoredItemCountsByItemId = new();
    private int savedInstallationItemTotal;
    private static readonly HashSet<int> trainItemIds = new();
    public int MarkerVersion;
    public BlockStateStore(VirtualObjectWorld world) { this.world = world; }
    private VirtualObjectWorld ResolveVirtualObjectWorld() => world;
    private static bool TryBuildInstallationState(Vehicle owner, out InstallationSaveState state)
    { state = owner.State?.Clone(); return state != null; }
    private static bool IsPipeInstallationState(InstallationSaveState state) => false;
    private static bool IsTrainInstallationState(InstallationSaveState state) => trainItemIds.Contains(state.itemId);
    private void RegisterSavedInteractionCoordinateMappings(object state, Vector2Int key) { }
    private void UnregisterSavedInteractionCoordinateMappings(object state, Vector2Int key) { }
    private static bool HasSameMapMarkerState(InstallationSaveState a, InstallationSaveState b)
        => a.anchorCoordinate == b.anchorCoordinate && a.quarterTurns == b.quarterTurns && a.itemId == b.itemId;
    private void MarkMapMarkersChanged() => MarkerVersion++;
    public void Seed(Vehicle vehicle, InstallationSaveState state)
    {
        if (vehicle is Train) trainItemIds.Add(state.itemId);
        SeedDataOnly(state);
        var key = GetInstallationStorageKey(state);
        var handle = world.AttachInstallationView(state, vehicle.GetInstanceID(), state.worldPosition, state.worldRotation);
        liveInstallationStates[key] = new() { installationObject = vehicle, state = state, handle = handle };
        RegisterInstallationPlacementKey(liveInstallationStorageKeysByPlacement, state, key);
        RegisterLiveCoordinateMappings(state, key);
        vehicle.BindRuntimeMapObjectHandle(handle);
    }
    public void SeedDataOnly(InstallationSaveState state)
    {
        var key = GetInstallationStorageKey(state);
        savedInstallationStates[key] = state;
        RegisterInstallationPlacementKey(savedInstallationStorageKeysByPlacement, state, key);
        RegisterSavedCoordinateMappings(state, key);
        AdjustSavedInstallationCount(state, 1);
        world.UpsertInstallationHandle(state);
    }
    public Vector2Int KeyFor(Vehicle vehicle) => liveInstallationStorageKeysByPlacement[(vehicle.RuntimePlacementSequence, vehicle.ItemId)];
    public InstallationSaveState CurrentState(Vehicle vehicle) => savedInstallationStates[KeyFor(vehicle)];
    public bool HasSavedKey(Vector2Int key) => savedInstallationStates.ContainsKey(key);
    public int ItemCount(int item) => savedInstallationCountsByItemId.GetValueOrDefault(item);
    public int CargoCount(int item) => savedInstallationStoredItemCountsByItemId.GetValueOrDefault(item);
    public bool LookupAt(Vector2Int coordinate, Vehicle vehicle)
    {
        var key = KeyFor(vehicle);
        return savedInstallationStorageKeysByOccupiedCoordinate.TryGetValue(coordinate, out var keys) && keys.Contains(key)
            && liveInstallationAnchorsByCoordinate.TryGetValue(coordinate, out var liveKey) && liveKey == key;
    }
}
