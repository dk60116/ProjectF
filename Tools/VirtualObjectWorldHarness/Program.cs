using System;
using System.Collections.Generic;
using ProjectF.MapObjects;
using UnityEngine;

internal static partial class Program
{
    private static int checks;

    private static void Main()
    {
        CheckManagedLifetime();
        CheckInstallationViewBinding();
        CheckCoordinateIndexAndReplacement();
        CheckCoordinateRelocation();
        CheckInstallationPresentationVersioning();
        CheckLiveInstallationMovement();
        CheckDataOnlyInstallationIndex();
        CheckInstallationOnlyCopy();
        CheckBulkLoadIndexBuild();
#if VEHICLE_MOVEMENT_PROBE
        CheckVehicleStoreMovement();
#endif
        CheckServiceReplacementInvalidatesOldHandles();
        Console.WriteLine($"PASS {checks} virtual-object world checks; no Unity scene or GameObject created");
    }

    private static void CheckManagedLifetime()
    {
        Require(VirtualObjectWorld.Current == null, "world starts detached from a scene");
        VirtualObjectWorld world = VirtualObjectWorld.Ensure();
        Require(ReferenceEquals(world, VirtualObjectWorld.Current), "Ensure publishes managed service");
        Require(ReferenceEquals(world, VirtualObjectWorld.Ensure()), "Ensure is idempotent");
    }

    private static void CheckInstallationViewBinding()
    {
        VirtualObjectWorld world = VirtualObjectWorld.Current;
        BlockStateStore.InstallationSaveState state = CreateState(31, 7001L);
        MapObjectHandle handle = world.UpsertInstallationHandle(
            state,
            VirtualObjectResidency.Virtual);
        Require(handle.IsValid, "data-only installation gets stable handle");
        Require(world.TryGetRecord(handle, out VirtualObjectRecord dataRecord), "data-only handle resolves");
        Require(!dataRecord.HasAttachedView, "data entity does not require a view instance");
        Require(dataRecord.residency == VirtualObjectResidency.Virtual, "data entity has virtual residency");

        InstallationObject view = new InstallationObject(91);
        view.transform.position = new Vector3(4f, 2f, 9f);
        MapObjectHandle attached = world.AttachInstallationView(
            state,
            view.GetInstanceID(),
            view.transform.position,
            view.transform.rotation);
        Require(attached == handle, "attaching view preserves simulation identity");
        Require(world.TryGetRecord(handle, out VirtualObjectRecord liveRecord) && liveRecord.HasAttachedView,
            "attached view is presentation metadata only");
        Require(!world.UpdateAttachedInstallationViewPose(
                state.storageKey,
                view.GetInstanceID() + 1,
                new Vector3(99f, 0f, 99f),
                Quaternion.identity),
            "unrelated view cannot move an entity");
        Require(world.UpdateAttachedInstallationViewPose(
                state.storageKey,
                view.GetInstanceID(),
                new Vector3(6f, 2f, 10f),
                Quaternion.identity),
            "attached view pose update crosses an explicit value boundary");

        MapObjectHandle detached = world.UpsertInstallationHandle(state, VirtualObjectResidency.Virtual);
        Require(detached == handle, "detaching view preserves simulation identity");
        Require(world.TryGetRecord(handle, out VirtualObjectRecord detachedRecord) && !detachedRecord.HasAttachedView,
            "detaching view keeps authoritative entity alive");
    }

    private static void CheckCoordinateIndexAndReplacement()
    {
        VirtualObjectWorld world = VirtualObjectWorld.Current;
        BlockStateStore.InstallationSaveState state = CreateState(44, 8101L);
        MapObjectHandle first = world.UpsertInstallationHandle(state);
        var handles = new List<MapObjectHandle>();
        world.CopyMapObjectHandlesAtCoordinate(new Vector2Int(5, 8), handles);
        Require(handles.Contains(first), "occupied-coordinate index is data-backed");

        state.placementSequence = 8102L;
        MapObjectHandle replacement = world.UpsertInstallationHandle(state);
        Require(replacement != first, "logical replacement changes generation-safe identity");
        Require(!world.IsHandleAlive(first), "replaced handle becomes stale");
        Require(world.IsHandleAlive(replacement), "replacement handle resolves");
    }

    private static void CheckServiceReplacementInvalidatesOldHandles()
    {
        VirtualObjectWorld firstWorld = VirtualObjectWorld.Current;
        BlockStateStore.InstallationSaveState state = CreateState(52, 9101L);
        MapObjectHandle oldHandle = firstWorld.UpsertInstallationHandle(state);
        firstWorld.Dispose();
        Require(VirtualObjectWorld.Current == null, "Dispose unpublishes managed service");

        VirtualObjectWorld replacementWorld = VirtualObjectWorld.Ensure();
        MapObjectHandle newHandle = replacementWorld.UpsertInstallationHandle(state);
        Require(newHandle != oldHandle, "new world epoch cannot revive an old handle");
        Require(!replacementWorld.IsHandleAlive(oldHandle), "old world handle remains stale");
        replacementWorld.Dispose();
    }

    private static void CheckBulkLoadIndexBuild()
    {
        VirtualObjectWorld world = VirtualObjectWorld.Current;
        Vector2Int resourceCoordinate = new Vector2Int(20, 21);
        BlockStateStore.InstallationSaveState installation = CreateState(48, 8301L);
        installation.anchorCoordinate = new Vector2Int(22, 23);
        installation.storageKey = installation.anchorCoordinate;
        installation.occupiedCoordinates.Clear();
        installation.occupiedCoordinates.Add(installation.anchorCoordinate);

        world.BeginBulkLoad(0, 1, 1);
        world.UpsertResource(resourceCoordinate, 12, new Resource.ResourceSaveState { resourceCount = 5 });
        MapObjectHandle installationHandle = world.UpsertInstallationHandle(installation);
        world.CompleteBulkLoad();

        var handles = new List<MapObjectHandle>();
        world.CopyMapObjectHandlesAtCoordinate(resourceCoordinate, handles);
        Require(handles.Count == 1, "bulk load builds the resource coordinate index once complete");
        world.CopyMapObjectHandlesAtCoordinate(installation.anchorCoordinate, handles);
        Require(handles.Contains(installationHandle), "bulk load builds the installation coordinate index once complete");
    }

    private static void CheckCoordinateRelocation()
    {
        VirtualObjectWorld world = VirtualObjectWorld.Current;
        BlockStateStore.InstallationSaveState state = CreateState(47, 8201L);
        MapObjectHandle handle = world.UpsertInstallationHandle(state);
        Vector2Int previousCoordinate = new Vector2Int(5, 8);
        Vector2Int nextCoordinate = new Vector2Int(6, 9);

        state.occupiedCoordinates.Clear();
        state.occupiedCoordinates.Add(state.anchorCoordinate);
        state.occupiedCoordinates.Add(nextCoordinate);
        MapObjectHandle updatedHandle = world.UpsertInstallationHandle(state);
        Require(updatedHandle == handle, "footprint update preserves installation identity");

        var handles = new List<MapObjectHandle>();
        world.CopyMapObjectHandlesAtCoordinate(previousCoordinate, handles);
        Require(!handles.Contains(handle), "footprint update removes the previous coordinate mapping");
        world.CopyMapObjectHandlesAtCoordinate(nextCoordinate, handles);
        Require(handles.Contains(handle), "footprint update registers the new coordinate mapping");
    }

    private static void CheckInstallationPresentationVersioning()
    {
        VirtualObjectWorld world = VirtualObjectWorld.Current;
        BlockStateStore.InstallationSaveState state = CreateState(49, 8401L);
        world.UpsertInstallationHandle(state);
        int initialVersion = world.DataOnlyInstallationVersion;

        world.UpsertInstallationHandle(state);
        Require(world.DataOnlyInstallationVersion == initialVersion,
            "state-only installation upsert preserves render version");

        state.worldPosition = new Vector3(8f, 0f, 7f);
        world.UpsertInstallationHandle(state);
        Require(world.DataOnlyInstallationVersion == initialVersion + 1,
            "installation pose change advances render version");
    }

    private static void CheckLiveInstallationMovement()
    {
        using var world = new VirtualObjectWorld();
        var state = CreateState(60, 8501L);
        var handle = world.AttachInstallationView(state, 101, state.worldPosition, state.worldRotation);
        int initialVersion = world.DataOnlyInstallationVersion;
        int initialLookupVersion = world.Version;
        var next = state.Clone();
        next.anchorCoordinate = next.storageKey = new Vector2Int(32, -33);
        next.quarterTurns = 2;
        next.occupiedCoordinates.Clear();
        next.occupiedCoordinates.Add(next.anchorCoordinate);
        var position = new Vector3(32.25f, 0f, -33.1f);
        var rotation = Quaternion.Euler(0, 180, 0);
        Require(!world.MoveAttachedInstallationView(handle, next, 102, position, rotation),
            "an unrelated view cannot relocate a live installation");
        Require(!world.MoveAttachedInstallationView(default, next, 101, position, rotation),
            "an invalid handle cannot relocate a live installation");
        var replacement = next.Clone(); replacement.placementSequence++;
        Require(!world.MoveAttachedInstallationView(handle, replacement, 101, position, rotation),
            "movement cannot replace entity identity");
        Require(world.MoveAttachedInstallationView(handle, next, 101, position, rotation),
            "live installation can cross a cell and chunk boundary");
        Require(world.DataOnlyInstallationVersion == initialVersion && world.Version == initialLookupVersion + 1,
            "live movement updates lookup version without rebuilding static installation batches");
        Require(world.IsHandleAlive(handle) && world.TryGetInstallationHandle(next.storageKey, out var moved) && moved == handle,
            "cell crossing preserves the original generation-safe handle");
        Require(!world.TryGetInstallationHandle(state.storageKey, out _), "old storage key is released");
        Require(world.TryGetRecord(handle, out var record) && record.worldPosition.Equals(position)
            && record.worldRotation.Equals(rotation) && record.quarterTurns == 2,
            "moved entity has current world pose and facing");
        var handles = new List<MapObjectHandle>();
        world.CopyMapObjectHandlesAtCoordinate(state.anchorCoordinate, handles);
        Require(!handles.Contains(handle), "old occupied coordinate no longer contains the vehicle");
        world.CopyMapObjectHandlesAtCoordinate(next.anchorCoordinate, handles);
        Require(handles.Contains(handle), "new occupied coordinate contains the vehicle");
        var obstacle = CreateState(61, 8502L);
        world.UpsertInstallationHandle(obstacle);
        initialVersion = world.DataOnlyInstallationVersion;
        var collision = next.Clone(); collision.storageKey = obstacle.storageKey;
        Require(!world.MoveAttachedInstallationView(handle, collision, 101, position, rotation),
            "movement cannot overwrite another installation storage key");
        Require(world.DataOnlyInstallationVersion == initialVersion && world.TryGetInstallationHandle(next.storageKey, out _),
            "failed movement leaves presentation version and indices intact");
        world.UpsertInstallationHandle(next);
        Require(world.DataOnlyInstallationVersion == initialVersion + 1, "view detachment still invalidates static presentation");
        Require(!world.MoveAttachedInstallationView(handle, state, 101, position, rotation),
            "movement cannot relocate a detached data-only installation");
        Require(world.RemoveInstallation(handle) && !world.IsHandleAlive(handle),
            "moved installation remains removable using its original handle");
        Require(world.DataOnlyInstallationVersion == initialVersion + 2, "actual removal still invalidates static presentation");
    }

    private static void CheckDataOnlyInstallationIndex()
    {
        using var world = new VirtualObjectWorld();
        var a = CreateState(71, 8601);
        var b = CreateState(72, 8602);
        b.anchorCoordinate = b.storageKey = new Vector2Int(8, 9);
        var handle = world.UpsertInstallationHandle(a);
        world.UpsertInstallationHandle(b);
        var versions = new List<KeyValuePair<int, int>>();
        var records = new List<VirtualObjectRecord>();
        world.CopyDataOnlyInstallationTypeVersions(versions);
        Require(versions.Count == 2, "data-only presentation revisions are indexed by item type");
        world.CopyDataOnlyInstallationRecords(71, records);
        Require(records.Count == 1 && records[0].mapObjectHandle == handle, "type copy excludes other installation types");
        int revision = world.DataOnlyInstallationVersion;
        a.hasTrainRailSample = true;
        world.UpsertInstallationHandle(a);
        Require(world.DataOnlyInstallationVersion == revision, "nonvisual save-state changes preserve data-only presentation revision");
        a.worldPosition = new Vector3(10, 0, 11);
        world.UpsertInstallationHandle(a);
        Require(world.DataOnlyInstallationVersion == revision + 1, "data-only pose change advances presentation revision");
        world.AttachInstallationView(a, 301, a.worldPosition, a.worldRotation);
        Require(world.GetDataOnlyInstallationCount(71) == 0, "attaching a view removes its data-only presentation entry");
        revision = world.DataOnlyInstallationVersion;
        world.AttachInstallationView(a, 301, new Vector3(12, 0, 13), a.worldRotation);
        Require(world.DataOnlyInstallationVersion == revision, "live view pose upserts do not invalidate data-only presentation");
        world.UpsertInstallationHandle(a);
        Require(world.GetDataOnlyInstallationCount(71) == 1 && world.DataOnlyInstallationVersion == revision + 1,
            "detaching a view restores its data-only presentation entry");
        a.itemId = 73;
        world.UpsertInstallationHandle(a);
        Require(world.GetDataOnlyInstallationCount(71) == 0 && world.GetDataOnlyInstallationCount(73) == 1,
            "item replacement moves record between type indices");
        world.CopyDataOnlyInstallationRecords(73, records);
        Require(records.Count == 1 && world.RemoveInstallation(records[0].mapObjectHandle), "indexed data-only installation is removable");
        Require(world.GetDataOnlyInstallationCount(73) == 0, "removal clears type index membership");
        world.Clear();
        Require(world.GetDataOnlyInstallationCount(72) == 0, "world clear releases data-only type membership");
        world.CopyDataOnlyInstallationTypeVersions(versions);
        Require(versions.Count == 3, "empty type revisions survive clear so existing hosts can be removed");
        world.UpsertInstallationHandle(b);
        Require(world.GetDataOnlyInstallationCount(72) == 1, "data-only index can be repopulated after clear");
        for (int i = 0; i < 100; i++) { world.CopyDataOnlyInstallationTypeVersions(versions); world.CopyDataOnlyInstallationRecords(72, records); }
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { world.CopyDataOnlyInstallationTypeVersions(versions); world.CopyDataOnlyInstallationRecords(72, records); }
        Require(GC.GetAllocatedBytesForCurrentThread() == allocated, "warmed type revision and record copies allocate nothing");
    }

    private static void CheckInstallationOnlyCopy()
    {
        VirtualObjectWorld world = VirtualObjectWorld.Current;
        Vector2Int resourceCoordinate = new Vector2Int(31, 32);
        world.UpsertResource(resourceCoordinate, 13, new Resource.ResourceSaveState { resourceCount = 2 });
        var records = new List<VirtualObjectRecord>();
        var types = new List<KeyValuePair<int, int>>();
        world.CopyDataOnlyInstallationTypeVersions(types);
        int count = 0;
        for (int typeIndex = 0; typeIndex < types.Count; typeIndex++)
        {
            world.CopyDataOnlyInstallationRecords(types[typeIndex].Key, records);
            count += records.Count;
            for (int i = 0; i < records.Count; i++)
            {
                Require(records[i].kind == VirtualObjectKind.Installation && !records[i].HasAttachedView,
                    "typed installation copy excludes resources, item stacks and live views");
            }
        }
        Require(count > 0, "typed installation copy returns data-only installations");
    }

    private static BlockStateStore.InstallationSaveState CreateState(int itemId, long sequence)
    {
        var state = new BlockStateStore.InstallationSaveState
        {
            anchorCoordinate = new Vector2Int(3, 7),
            hasStorageKey = true,
            storageKey = new Vector2Int(3, 7),
            itemId = itemId,
            quarterTurns = 1,
            placementSequence = sequence,
            hasWorldPose = true,
            worldPosition = new Vector3(3f, 0f, 7f),
            worldRotation = Quaternion.Euler(0f, 90f, 0f)
        };
        state.occupiedCoordinates.Add(state.anchorCoordinate);
        state.occupiedCoordinates.Add(new Vector2Int(5, 8));
        return state;
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException(name);
        }

        checks++;
        Console.WriteLine("PASS " + name);
    }
}
