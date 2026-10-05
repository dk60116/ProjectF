using System;
using System.Collections.Generic;
using ProjectF.Simulation;

// One scheduler entry owns all oil producers. IO still commits in placement order.
internal sealed class OilDrillingBatch : IMapObjectUpdateTick, IMapObjectUpdateTickInterval,
    IMapObjectSimulationIdentity, IFacilityPowerEvaluationTarget, IDisposable
{
    private readonly ProductionWorld world;
    private readonly List<ProductionFacilityInstance> active = new List<ProductionFacilityInstance>();
    private readonly List<ProductionFacilityInstance> pending = new List<ProductionFacilityInstance>();
    private static readonly Comparison<ProductionFacilityInstance> order = Compare;
    private bool membershipDirty, applying, disposed;
    private int registered, scheduled, fastCount;
    private long fastUnitsPerTick, lastFastTick;
    internal int ScheduledCount => scheduled;
    internal int LastProcessedCount { get; private set; }
    internal long SortCount { get; private set; }
    public float ManagedUpdateTickIntervalSeconds => SimulationTickWorld.FixedSimulationDeltaSeconds;
    public long SimulationId => long.MaxValue - 31;
    public bool RequiresFacilityPowerEvaluation => !ProjectF.Benchmark.BenchmarkRuntime.ForceWorking;

    internal OilDrillingBatch(ProductionWorld world)
    {
        this.world = world; lastFastTick = MapObjectTickManager.CurrentSimulationTick;
        FacilitySimulationWorld.Register(this, false);
    }
    internal void Register(ProductionFacilityInstance facility)
    {
        if (facility.OilRegistered) return;
        facility.OilRegistered = true; registered++;
    }
    internal void Wake(ProductionFacilityInstance facility)
    {
        Register(facility); LeaveFast(facility); KeepScheduled(facility);
    }
    internal void KeepScheduled(ProductionFacilityInstance facility)
    {
        if (!facility.OilScheduled)
        {
            facility.OilScheduled = true;
            if (scheduled++ == 0)
            { lastFastTick = MapObjectTickManager.CurrentSimulationTick; FacilitySimulationWorld.SetScheduled(this, true); }
        }
        if (facility.OilActiveIndex >= 0 || facility.OilQueued || facility.OilFastForced) return;
        facility.OilQueued = true; pending.Add(facility); membershipDirty = true;
    }
    internal void Sleep(ProductionFacilityInstance facility)
    {
        LeaveFast(facility);
        if (facility.OilScheduled) { facility.OilScheduled = false; scheduled--; membershipDirty = true; }
        if (!applying && scheduled == 0) Stop();
    }
    internal void Unregister(ProductionFacilityInstance facility)
    {
        Sleep(facility);
        if (facility.OilRegistered) { facility.OilRegistered = false; registered--; }
    }
    internal void EnterFast(ProductionFacilityInstance facility)
    {
        KeepScheduled(facility);
        if (facility.OilFastForced) return;
        facility.OilFastForced = true; facility.OilFastUnitsPerTick = facility.OilRateUnitsPerTick;
        fastUnitsPerTick += facility.OilFastUnitsPerTick; fastCount++; membershipDirty = true;
    }
    internal void LeaveFast(ProductionFacilityInstance facility)
    {
        if (!facility.OilFastForced) return;
        // If a topology wake arrives after the clock advances but before this batch,
        // account for this producer's last forced interval before removing its rate.
        long ticks = Math.Max(0, MapObjectTickManager.CurrentSimulationTick - lastFastTick);
        if (ticks > 0) RecordFastSpill(facility.OilFastUnitsPerTick, ticks);
        fastUnitsPerTick -= facility.OilFastUnitsPerTick; fastCount--; facility.OilFastForced = false;
    }
    public void ManagedUpdateTick(float deltaTime)
    {
        if (disposed) return;
        applying = true;
        try
        {
            long now = MapObjectTickManager.CurrentSimulationTick;
            using (MapObjectTickProfiler.SampleNamed("OilDrillingECS", nameof(OilDrillingBatch), "Oil Forced Empty Output"))
            {
                long ticks = Math.Max(0, now - lastFastTick);
                if (ticks > 0 && fastCount > 0) { RecordFastSpill(fastUnitsPerTick, ticks); world.ProcessedUpdates += (long)fastCount * ticks; }
                lastFastTick = now;
            }
            using (MapObjectTickProfiler.SampleNamed("OilDrillingECS", nameof(OilDrillingBatch), "Oil Scheduling")) FlushMembership();
            LastProcessedCount = fastCount;
            using (MapObjectTickProfiler.SampleNamed("OilDrillingECS", nameof(OilDrillingBatch), "Oil Apply"))
                for (int i = 0; i < active.Count; i++)
                {
                    var facility = active[i];
                    if (!facility.OilScheduled || facility.OilFastForced) continue;
                    facility.ManagedUpdateTick(deltaTime); LastProcessedCount++;
                }
            // Removals preserve order. Newly woken producers are merged on the next tick.
            CompactActive();
        }
        finally
        {
            applying = false;
            if (scheduled == 0) Stop();
        }
    }
    private static void RecordFastSpill(long rate, long ticks)
    {
        double liters = (double)rate * ticks / DeterministicSimulationUnits.UnitsPerWhole;
        ProjectF.Benchmark.BenchmarkRuntime.RecordSpill((float)liters);
    }
    private void Stop()
    {
        FacilitySimulationWorld.SetScheduled(this, false);
        for (int i = 0; i < active.Count; i++) active[i].OilActiveIndex = -1;
        for (int i = 0; i < pending.Count; i++) pending[i].OilQueued = false;
        active.Clear(); pending.Clear(); membershipDirty = false;
    }
    private void FlushMembership()
    {
        if (!membershipDirty) return;
        CompactActive(); bool added = false;
        for (int i = 0; i < pending.Count; i++)
        {
            var facility = pending[i]; facility.OilQueued = false;
            if (!facility.OilScheduled || facility.OilFastForced || !facility.OilRegistered || facility.OilActiveIndex >= 0) continue;
            facility.OilActiveIndex = active.Count; active.Add(facility); added = true;
        }
        pending.Clear();
        if (added)
        {
            active.Sort(order); SortCount++;
            for (int i = 0; i < active.Count; i++) active[i].OilActiveIndex = i;
        }
        membershipDirty = false;
    }
    private void CompactActive()
    {
        if (!membershipDirty) return;
        int write = 0;
        for (int i = 0; i < active.Count; i++)
        {
            var facility = active[i];
            if (!facility.OilScheduled || facility.OilFastForced || !facility.OilRegistered) { facility.OilActiveIndex = -1; continue; }
            active[write] = facility; facility.OilActiveIndex = write++;
        }
        if (write < active.Count) active.RemoveRange(write, active.Count - write);
    }
    private static int Compare(ProductionFacilityInstance a, ProductionFacilityInstance b)
    { int value = a.SimulationId.CompareTo(b.SimulationId); return value != 0 ? value : a.Index.CompareTo(b.Index); }
    internal void AppendProfilerCounters()
    {
        MapObjectTickProfiler.AddRuntimeCounter("OilDrillingECS", "RegisteredEntities", registered);
        MapObjectTickProfiler.AddRuntimeCounter("OilDrillingECS", "ScheduledEntities", scheduled);
        MapObjectTickProfiler.AddRuntimeCounter("OilDrillingECS", "LastProcessedEntities", LastProcessedCount);
        MapObjectTickProfiler.AddRuntimeCounter("OilDrillingECS", "ForcedEmptyOutputEntities", fastCount);
        MapObjectTickProfiler.AddRuntimeCounter("OilDrillingECS", "MembershipSorts", SortCount);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; FacilitySimulationWorld.Unregister(this); active.Clear(); pending.Clear();
    }
}
