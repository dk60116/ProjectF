using System;
using System.Collections.Generic;
using UnityEngine;

// Only placement indexing and presentation are doubled. Registration, store writes,
// snapshot copying and miner persistence run their actual production methods.
public partial class BlockStateStore
{
    private Dictionary<Vector2Int, InstallationSaveState> savedInstallationStates => Installed;
    private readonly Dictionary<Vector2Int, LiveInstallationRecord> liveInstallationStates = new();
    private readonly object savedInstallationStorageKeysByPlacement = new(), liveInstallationStorageKeysByPlacement = new();
    public int IndexWrites;
    private sealed class LiveInstallationRecord
    { public InstallationSaveState state; public InstallationBoundary installationObject; }
    private static Vector2Int ResolveInstallationStorageKey(InstallationSaveState state, object states) => state.anchorCoordinate;
    private static void AssignInstallationStorageKey(InstallationSaveState state, Vector2Int key) { }
    private static bool HasSameMapMarkerState(InstallationSaveState a, InstallationSaveState b) => a.itemId == b.itemId;
    private void RemoveSavedInstallationStatesForSamePlacement(InstallationSaveState state, Vector2Int key) { }
    private void AdjustSavedInstallationCount(InstallationSaveState state, int count) { }
    private void UnregisterSavedCoordinateMappings(InstallationSaveState state, Vector2Int key) { }
    private void UnregisterLiveCoordinateMappings(InstallationSaveState state, Vector2Int key) { }
    private void RegisterLiveCoordinateMappings(InstallationSaveState state, Vector2Int key) { }
    private void RegisterSavedCoordinateMappings(InstallationSaveState state, Vector2Int key) => IndexWrites++;
    private void UnregisterInstallationPlacementKey(object index, InstallationSaveState state, Vector2Int key) { }
    private void RegisterInstallationPlacementKey(object index, InstallationSaveState state, Vector2Int key) { }
    private void MarkMapMarkersChanged() { }
    private void UnregisterLiveInstallation(Vector2Int key) { }
    private VirtualObjectWorld ResolveVirtualObjectWorld() => View;
    public readonly VirtualObjectWorld View = new();
}
public sealed class InstallationBoundary
{
    public readonly GaugeTransform transform = new();
    public int GetInstanceID() => 1;
}
public class StoreViewBoundary
{
    public BlockStateStore.InstallationSaveState State;
    public void UpsertInstallation(BlockStateStore.InstallationSaveState state) => State = state;
    public void UpsertInstallationHandle(BlockStateStore.InstallationSaveState state, VirtualObjectResidency residency) => State = state;
    public void AttachInstallationView(BlockStateStore.InstallationSaveState state, int id, Vector3 position, Quaternion rotation) => State = state;
}
public enum VirtualObjectResidency { Virtual }
public sealed class RobotArmWorld
{ public static RobotArmWorld Current; public void FlushSaveStates() { } }

internal static class MiningOwnershipChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        VirtualObjectWorld.Current = new();
        var world = new MiningWorld();
        MiningWorld.Current = world;
        var source = new BlockStateStore.InstallationSaveState { anchorCoordinate = new Vector2Int(50, 50) };
        source.occupiedCoordinates.Add(source.anchorCoordinate);
        source.inputOutputState.outputCoordinates.Add(new Vector2Int(50, 51));
        BlockStateStore.InstallationSaveState.CloneCount = 0;
        Check(world.Store.RegisterDataOnlyInstallationSharedState(source, out var shared), "shared registration succeeds");
        Check(BlockStateStore.InstallationSaveState.CloneCount == 1, "registration copies input once, without a runtime copy");
        Check(world.Store.TryGetInstallationStateReadOnly(source.anchorCoordinate, out var owned) && ReferenceEquals(shared, owned)
            && ReferenceEquals(shared, world.Store.View.State), "store, virtual record and runtime share one DTO graph");
        source.inputOutputState.outputCoordinates.Clear(); source.occupiedCoordinates.Clear();
        Check(shared.inputOutputState.outputCoordinates.Count == 1 && shared.occupiedCoordinates.Count == 1,
            "caller mutation cannot alter stored placement geometry");
        var miner = new MiningMachineInstance(world, 0, 1, new ProjectF.MapObjects.MapObjectHandle(100),
            new MiningMachine(), shared, new MiningRenderTemplate());
        world.SaveMiner = miner;
        world.Value.PendingHarvestedItems = 4;
        world.Value.Clock.Production.Active = true;
        miner.SetItemFilterEnabled(1, 3, false);
        Check(shared.itemFilterMaskInitialized && shared.itemFilterMaskWords.Count == 1, "filter changes reach the owned save state");
        int clones = BlockStateStore.InstallationSaveState.CloneCount, indexWrites = world.Store.IndexWrites;
        miner.Persist();
        Check(shared.inputOutputState.miningPendingHarvestedItems == 4 && shared.inputOutputState.hasActiveCraft,
            "persistence updates the shared production DTO");
        miner.Persist();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) miner.Persist();
        long allocations = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocations == 0 && BlockStateStore.InstallationSaveState.CloneCount == clones,
            "100k persist calls allocate zero bytes and clone no installation graphs");
        Check(world.Store.IndexWrites == indexWrites && ReferenceEquals(world.Store.Installed[miner.StorageKey], shared),
            "persist does not replace DTOs or rebuild placement indices");
        Check(world.Store.TryGetInstallationState(miner.StorageKey, out var read) && !ReferenceEquals(read, shared),
            "public reads retain copy isolation");
        read.inputOutputState.outputCoordinates.Clear(); read.itemFilterMaskWords.Clear();
        Check(shared.inputOutputState.outputCoordinates.Count == 1 && shared.itemFilterMaskWords.Count == 1,
            "public reads cannot mutate shared nested lists");
        var snapshots = world.Store.GetInstallationStatesSnapshot();
        var snapshot = snapshots[0];
        Check(snapshot.inputOutputState.miningPendingHarvestedItems == 4 && !ReferenceEquals(snapshot, shared),
            "save snapshot captures flushed production in an isolated DTO");
        world.Value.PendingHarvestedItems = 2; miner.Persist();
        Check(snapshot.inputOutputState.miningPendingHarvestedItems == 4 && shared.inputOutputState.miningPendingHarvestedItems == 2,
            "ongoing production cannot mutate a captured save snapshot");
        var restoredStore = new BlockStateStore();
        Check(restoredStore.RegisterDataOnlyInstallationSharedState(snapshot, out var restored)
            && restored.inputOutputState.miningPendingHarvestedItems == 4 && !ReferenceEquals(restored, snapshot),
            "restoration establishes new owned state without sharing the captured snapshot");
        world.Alive = false; world.Store.Installed.Remove(miner.StorageKey); miner.Persist();
        Check(!world.Store.Installed.ContainsKey(miner.StorageKey), "removed miner persistence cannot resurrect installation state");
        var legacyStore = new BlockStateStore();
        Check(legacyStore.RegisterDataOnlyInstallation(snapshot, out var legacy)
            && !ReferenceEquals(legacy, legacyStore.Installed[snapshot.anchorCoordinate]),
            "other installation registration keeps its existing copy contract");
        Check(!legacyStore.RegisterDataOnlyInstallationSharedState(null, out var missing) && missing == null,
            "failed registration returns no shared state");
        MiningWorld.Current = null;
        Console.WriteLine($"PASS {checks} mining shared ownership checks (actual store/persist sources; 100k saves: {allocations} B allocated)");
    }
}
