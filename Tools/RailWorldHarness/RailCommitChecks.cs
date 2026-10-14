using System;
using System.Collections.Generic;
using ProjectF.MapObjects;
using ProjectF.Railway;
using UnityEngine;

// Inventory and persistence lifetime are substituted; commit, registration and key selection are production methods.
public partial class InstallationPlacementController
{
    public int ItemCount = 20;
    public int RemovalLimit = int.MaxValue;
    public int RemovedCount, RefundedCount;
    public int GetAvailableInstallItemCount(int id) => ItemCount;
    public int RemoveInstallItemsFromPlayer(int id, int requested)
    {
        int removed = Math.Min(Math.Min(requested, ItemCount), RemovalLimit);
        ItemCount -= removed;
        RemovedCount += removed;
        return removed;
    }
    public int RefundInstallItemsToPlayer(int id, int count) { ItemCount += count; RefundedCount += count; return count; }
    public Vector3 GetInstalledObjectWorldPosition(Vector2Int anchor, InstallationObject prototype, int turns)
        => new Vector3(anchor.x, 0, anchor.y);
}

public partial class RailloadInstallationController
{
    private InstallationPlacementController placementController;
    private ItemDefinition railloadDefinition;
    private Railload railloadPrefab;
    private TerrainGenerator terrain;
    private bool isActive;
    private RailPathPlan currentPlan;
    private void RefreshCurrentPreview() { }
    public bool TestCommit(InstallationPlacementController inventory, Railload prototype, Vector2Int end, int cost)
    {
        placementController = inventory;
        railloadPrefab = prototype;
        railloadDefinition = new ItemDefinition { id = 1, mapObject = prototype };
        isActive = true;
        currentPlan = BuildPlan(Vector2Int.zero, end, true);
        currentPlan.isValid = TestBuildableCurve(currentPlan.visualPathPoints);
        currentPlan.requiredItemCount = cost;
        return CommitCurrentPreview(false);
    }
}

public partial class BlockStateStore
{
    private readonly Dictionary<Vector2Int, InstallationSaveState> savedInstallationStates = new Dictionary<Vector2Int, InstallationSaveState>();
    private readonly Dictionary<(long, int), Vector2Int> savedInstallationStorageKeysByPlacement = new Dictionary<(long, int), Vector2Int>();
    private readonly Dictionary<(long, int), Vector2Int> liveInstallationStorageKeysByPlacement = new Dictionary<(long, int), Vector2Int>();
    private readonly RailWorld world;
    public int RegistrationCount;
    public BlockStateStore(RailWorld owner) { world = owner; }
    public static string ResolveInstallationSaveItemName(int id, InstallationObject prototype) => "Rail";
    public bool RegisterDataOnlyInstallationSharedState(InstallationSaveState state, out InstallationSaveState stored)
    {
        stored = state.Clone();
        stored.storageKey = ResolveInstallationStorageKey(stored, savedInstallationStates);
        stored.hasStorageKey = true;
        savedInstallationStates[stored.storageKey] = stored;
        savedInstallationStorageKeysByPlacement[(stored.placementSequence, stored.itemId)] = stored.storageKey;
        RegistrationCount++;
        world.UpsertSaved(stored);
        return true;
    }
    public bool TryGetInstallationHandle(Vector2Int key, out MapObjectHandle handle) { handle = default; return false; }
}

public sealed partial class TerrainGenerator
{
    private BlockStateStore resourceStateStore;
    private void EnsureResourceStateStore() { if (resourceStateStore == null) resourceStateStore = new BlockStateStore(World); }
    private void EnsureTrainStationIdentityAssigned(ITrainStationTarget station) { }
    public int RegistrationCount => resourceStateStore?.RegistrationCount ?? 0;
}

static class RailCommitChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }

    public static void Run()
    {
        var terrain = TerrainGenerator.Active = new TerrainGenerator();
        var controller = new RailloadInstallationController();
        var inventory = new InstallationPlacementController();
        var prototype = new Railload();
        Check(controller.TestCommit(inventory, prototype, new Vector2Int(4, 0), 4), "Initial rail commit succeeds");
        var first = terrain.World.LiveRails[0].Target as RailInstance;
        Check(first != null && inventory.ItemCount == 16, "Initial placement registers a rail and charges once");
        Check(controller.TestCommit(inventory, prototype, new Vector2Int(-4, 4), 4), "Branch from the same anchor commits");
        Check(terrain.World.LiveRails.Count == 2 && terrain.RegistrationCount == 2,
            "A different placement at the same anchor must register a NEW rail, not charge for the old one");
        Check(inventory.ItemCount == 12 && first.IsTargetActive, "Branch charges once and preserves the original rail");
        var second = terrain.World.LiveRails[1].Target as RailInstance;
        Check(second != null && second.StorageKey != first.StorageKey
            && second.RuntimePlacementSequence != first.RuntimePlacementSequence, "Overlapping anchors retain distinct storage and placement identities");
        Check(second.TrySampleRenderedPath(.5f, out _, out _),
            "The paid branch has a sampleable runtime path");
        Check(terrain.RegisterDataOnlyRailwayState(second.State.Clone(), prototype, out var restored)
            && ReferenceEquals(restored, second) && terrain.RegistrationCount == 2,
            "Restoring an already registered placement is idempotent");
        var branch = second.State.Clone();
        branch.hasStorageKey = false;
        Check(terrain.RegisterDataOnlyRailwayState(branch, prototype, out restored)
            && ReferenceEquals(restored, second) && terrain.RegistrationCount == 2,
            "An existing placement without an explicit key resolves its canonical key by identity");
        var overlapping = first.State.Clone();
        overlapping.hasStorageKey = false;
        overlapping.placementSequence = InstallationObject.ClaimNextPlacementSequence();
        Check(terrain.RegisterDataOnlyRailwayState(overlapping, prototype, out var third)
            && !ReferenceEquals(third, first) && third.RuntimePlacementSequence == overlapping.placementSequence,
            "New overlapping placements must not alias an existing rail");
        var invalidPrototype = new Railload();
        invalidPrototype.gameObject.scene = new SceneIdentity { Valid = true };
        terrain = TerrainGenerator.Active = new TerrainGenerator();
        int before = inventory.ItemCount;
        Check(!controller.TestCommit(inventory, invalidPrototype, new Vector2Int(4, 4), 2)
            && inventory.ItemCount == before && inventory.RemovedCount == 10 && inventory.RefundedCount == 2,
            "Registration failure refunds the entire rail cost");
        inventory.RemovalLimit = 1;
        int registrations = terrain.RegistrationCount;
        Check(!controller.TestCommit(inventory, prototype, new Vector2Int(4, 4), 2)
            && inventory.ItemCount == before && terrain.RegistrationCount == registrations
            && inventory.RemovedCount == 11 && inventory.RefundedCount == 3,
            "Partial inventory removal refunds without registering a rail");
        Console.WriteLine($"PASS rail commit/inventory transaction: {checks} checks");
    }
}
