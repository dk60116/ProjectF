using System;
using System.Collections.Generic;
using ProjectF.MapObjects;
using UnityEngine;

internal static class Program
{
    private static int checks;

    private static void Main()
    {
        CheckManagedLifetime();
        CheckInstallationViewBinding();
        CheckCoordinateIndexAndReplacement();
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
