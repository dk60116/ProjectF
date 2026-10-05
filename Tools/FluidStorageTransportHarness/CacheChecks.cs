using System;
using UnityEngine;

// Scene/ECS ownership is a boundary double; query, selection and revision methods
// are extracted unchanged from the production module by Run.ps1.
public sealed class ProductionFacilityInstance
{
    internal long FluidStorageStateRevision;
    public readonly ProductionWorld World = new();
    public float Capacity = 50, Stored;
    public bool Active = true;
    public long SimulationId;
    public long FluidUnits(int id) => DeterministicSimulationUnits.FromFloat(Stored);
}

public sealed class ProductionWorld
{
    public static ProductionWorld Current => null;
    public bool TryGetOutputDirection(Vector2Int c, out Vector2Int direction)
    { direction = default; return false; }
    public bool TryGetFluidReceiver(Vector2Int c, Vector2Int d, out ProductionFacilityInstance receiver)
    { receiver = null; return false; }
    public void RegisterNativeProducer(ProductionFacilityInstance receiver, InputOutputModule source) { }
    public float AvailableInput(ProductionFacilityInstance receiver, Vector2Int c, int id)
        => receiver.Active ? Math.Max(0, receiver.Capacity - receiver.Stored) : 0;
    public bool Receive(ProductionFacilityInstance receiver, Vector2Int c, int id, float amount, out float accepted)
    {
        accepted = Math.Min(amount, AvailableInput(receiver, c, id));
        if (accepted <= 0) return false;
        receiver.Stored += accepted;
        InputOutputModule.NotifyDataFluidStorageChanged(receiver);
        return true;
    }
}

public partial class InputOutputModule
{
    private long cachedFluidOutputNetworkObservedVersion = -1, cachedFluidOutputNetworkStateVersion;
    private long cachedFluidOutputAvailabilityTick = long.MinValue, cachedFluidOutputAvailabilityStateVersion;
    private int cachedFluidOutputAvailabilityTopologyVersion, cachedFluidOutputAvailabilityItemId;
    private float cachedFluidOutputAvailabilityMaximumLiters, cachedFluidOutputAvailabilityLiters;
    private bool cachedFluidOutputAvailabilityBlocked;
    private static long fluidOutputAvailabilityCacheHitCount, fluidOutputAvailabilityCacheMissCount;
    public static void StorageChanged(InstallationObject storage) => AdvanceFluidStorageStateVersion(storage);
    public static long AvailabilityMisses => fluidOutputAvailabilityCacheMissCount;
    public static long RetentionMisses => fluidOutputRetentionCacheMissCount;
    public static long SelectionMisses => fluidOutputSelectionCacheMissCount;
    public float Available(float max) { TryGetFluidOutputAvailableLiters(1, max, out float value); return value; }
    public InstallationObject Selected()
    {
        EnsureFluidOutputStorageCache();
        TrySelectFluidOutputConnectionWithAnySpaceFromCache(1, out var connection);
        return connection.Storage;
    }
    public bool SelectedData()
    { TrySelectFluidOutputConnectionWithAnySpaceFromCache(1, out var connection); return connection.DataStorage != null; }
    public void UseData(ProductionFacilityInstance receiver)
    {
        EnsureFluidOutputStorageCache();
        cachedFluidOutputConnections.Add(new FluidOutputConnection(receiver, default, 2, null));
        ClearFluidOutputTickQueryCaches();
    }
    public void SearchPort(Vector2Int c) => EnsureFluidOutputStorageCache(new[] { c });
    public static void TopologyChanged() => fluidTopologyVersion++;
}

public static partial class Checks
{
    private static void RunCacheChecks()
    {
        World.Reset();
        var producer = new InputOutputModule(); producer.Output(default, Vector2Int.right);
        Pipes(default, Vector2Int.right, 2);
        var first = new Fluidtank(); World.Place(first, Vector2Int.right * 2);
        first.TryAddFluidLiters(1, 40, 20, out _);
        var unrelated = new Fluidtank(); World.Place(unrelated, new Vector2Int(100, 100));
        Check(producer.Available(100), 10, "query reads connected capacity");
        Check(producer.TransportRetention(1), .99f, "query reads connected route retention");
        producer.Selected();
        long availability = InputOutputModule.AvailabilityMisses;
        long retention = InputOutputModule.RetentionMisses;
        long selection = InputOutputModule.SelectionMisses;
        unrelated.TryAddFluidLiters(1, 10, 20, out _);
        Check(producer.Available(100), 10, "unrelated native storage keeps capacity cache");
        producer.TransportRetention(1); producer.Selected();
        Check(InputOutputModule.AvailabilityMisses - availability, 0, "unrelated mutation causes no capacity search");
        Check(InputOutputModule.RetentionMisses - retention, 0, "unrelated mutation causes no retention search");
        Check(InputOutputModule.SelectionMisses - selection, 0, "unrelated mutation causes no selection search");
        var unrelatedData = new ProductionFacilityInstance();
        InputOutputModule.NotifyDataFluidStorageChanged(unrelatedData);
        producer.Available(100); producer.TransportRetention(1); producer.Selected();
        Check(InputOutputModule.AvailabilityMisses - availability, 0, "unrelated ECS mutation keeps capacity cache");
        Check(InputOutputModule.RetentionMisses - retention, 0, "unrelated ECS mutation keeps retention cache");
        Check(InputOutputModule.SelectionMisses - selection, 0, "unrelated ECS mutation keeps selection cache");

        first.TryAddFluidLiters(1, 10, 20, out _);
        Check(producer.Available(100), 0, "related fill invalidates capacity immediately");
        Check(producer.TransportRetention(1), 1, "full network invalidates retention immediately");
        Check(producer.Selected() == null ? 1 : 0, 1, "full network invalidates selected receiver");
        first.TryConsumeFluidLiters(1, 5, out _);
        Check(producer.Available(100), 5, "related drain invalidates blocked result in same tick");
        Check(producer.TransportRetention(1), .99f, "drained endpoint restores route in same tick");
        Check(producer.Selected() == first ? 1 : 0, 1, "drained endpoint restores selection");
        Check(producer.Available(2), 2, "smaller request reuses capped availability");
        availability = InputOutputModule.AvailabilityMisses;
        Check(producer.Available(200), 5, "larger request recomputes availability");
        Check(InputOutputModule.AvailabilityMisses - availability, 1, "larger request causes one search");

        var second = new Fluidtank(); World.Place(second, new Vector2Int(0, 11));
        second.TryAddFluidLiters(1, 10, 20, out _);
        producer.Output(new Vector2Int(0, 10), Vector2Int.up);
        Pipes(new Vector2Int(0, 10), Vector2Int.up, 1);
        InputOutputModule.TopologyChanged();
        Check(producer.Selected() == second ? 1 : 0, 1, "selection considers all route endpoints");
        first.TryConsumeFluidLiters(1, 50, out _);
        Check(producer.Selected() == first ? 1 : 0, 1, "non-selected endpoint changes can switch winner");
        second.TryAddFluidLiters(1, 35, 20, out _);
        producer.TransportRetention(1);
        producer.SearchPort(new Vector2Int(0, 10));
        Check(producer.Available(200), 55, "port-scoped rebuild cannot poison whole-module capacity");
        Check(producer.TransportRetention(1), .99f, "port-scoped rebuild cannot reuse other route retention");
        Check(producer.Selected() == first ? 1 : 0, 1, "whole-module selection restores its seed set");

        World.Reset();
        producer = new InputOutputModule(); producer.Output(default, Vector2Int.right);
        var data = new ProductionFacilityInstance(); producer.UseData(data);
        Check(producer.Available(100), 50, "ECS endpoint capacity participates in query cache");
        Check(producer.SelectedData() ? 1 : 0, 1, "ECS endpoint participates in selected route");
        data.Stored = 50; InputOutputModule.NotifyDataFluidStorageChanged(data);
        Check(producer.Available(100), 0, "related ECS fill invalidates same-tick capacity");
        Check(producer.SelectedData() ? 1 : 0, 0, "related ECS fill invalidates selected route");
        data.Stored = 20; InputOutputModule.NotifyDataFluidStorageChanged(data);
        Check(producer.Available(100), 30, "related ECS drain invalidates blocked result");
        availability = InputOutputModule.AvailabilityMisses;
        MapObjectTickManager.CurrentSimulationTick++;
        data.Active = false;
        Check(producer.Available(100), 0, "new tick rechecks unversioned receiver activity");
        Check(InputOutputModule.AvailabilityMisses - availability, 1, "activity still has conservative tick guard");
        Check(producer.Emit(5), 0, "actual transfer revalidates ECS acceptance");

        World.Reset(); producer = new InputOutputModule(); producer.Output(default, Vector2Int.right);
        Pipes(default, Vector2Int.right, 2);
        Check(producer.Available(100), 0, "empty route caches no receiver");
        first = new Fluidtank(); World.Place(first, Vector2Int.right * 2);
        InputOutputModule.TopologyChanged();
        Check(producer.Available(100), 50, "topology addition invalidates empty route");
        producer.TransportRetention(1); producer.Selected();
        unrelated = new Fluidtank();
        for (int i = 0; i < 100; i++)
        { InputOutputModule.StorageChanged(unrelated); producer.Available(100); producer.TransportRetention(1); producer.Selected(); }
        availability = InputOutputModule.AvailabilityMisses;
        retention = InputOutputModule.RetentionMisses;
        selection = InputOutputModule.SelectionMisses;
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        { InputOutputModule.StorageChanged(unrelated); producer.Available(100); producer.TransportRetention(1); producer.Selected(); }
        Check(GC.GetAllocatedBytesForCurrentThread() - bytes, 0, "warmed unrelated-change validation allocates no GC");
        Check(InputOutputModule.AvailabilityMisses - availability, 0, "10000 unrelated changes cause no capacity search");
        Check(InputOutputModule.RetentionMisses - retention, 0, "10000 unrelated changes cause no retention search");
        Check(InputOutputModule.SelectionMisses - selection, 0, "10000 unrelated changes cause no selection search");
        first.TryAddFluidLiters(1, 50, 20, out _);
        Check(producer.Emit(5), 0, "actual native transfer rejects full storage after cached query");
    }
}
