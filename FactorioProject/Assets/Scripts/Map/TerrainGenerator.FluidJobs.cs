using System;
using System.Collections.Generic;
using ProjectF.Fluids;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

public partial class TerrainGenerator
{
    private static readonly ProfilerMarker FluidJobsBakeMarker = new ProfilerMarker("Fluid Jobs.Bake");
    private static readonly ProfilerMarker FluidJobsScheduleMarker = new ProfilerMarker("Fluid Jobs.Schedule");
    private static readonly ProfilerMarker FluidJobsCompleteMarker = new ProfilerMarker("Fluid Jobs.Complete");
    private static readonly ProfilerMarker FluidDisplayResolveMarker =
        new ProfilerMarker("Fluid Jobs.Resolve Display");

    private FluidSimulationBuffers fluidJobBuffers;
    private readonly List<PipeRuntimeRecord> fluidJobRecordOrder = new List<PipeRuntimeRecord>();
    private readonly Dictionary<PipeRuntimeRecord, int> fluidJobIndices =
        new Dictionary<PipeRuntimeRecord, int>();
    private readonly List<FluidNetworkRange> fluidJobNetworkBuild = new List<FluidNetworkRange>();
    private readonly List<FluidPipeTopology> fluidJobPipeBuild = new List<FluidPipeTopology>();
    private readonly List<FluidPipeCoordinate> fluidJobCoordinateBuild = new List<FluidPipeCoordinate>();
    private readonly List<int> fluidJobEdgeBuild = new List<int>();
    private readonly List<int> fluidJobEdgeScratch = new List<int>(4);
    private readonly List<Vector2Int> fluidJobCoordinateScratch = new List<Vector2Int>(2);
    private PipeWorld fluidJobPipeWorld;
    private int fluidJobTopologyVersion = -1;
    private int fluidJobRebuildCount;
    private JobHandle fluidShadowJobHandle;
    private bool fluidShadowJobScheduled;
    private ulong fluidShadowChecksum;
    private long fluidShadowCompletedTick = -1L;
    private int fluidJobResolvedDisplayStateVersion = -1;
    private int fluidJobDisplayResolveCount;
    private int fluidJobLastChangedDisplayPipeCount;

    public int FluidJobNetworkCount => fluidJobNetworkBuild.Count;
    public int FluidJobPipeCount => fluidJobRecordOrder.Count;
    public ulong FluidShadowChecksum => fluidShadowChecksum;

    internal bool TryRefreshFluidJobDisplayStates(
        PipeWorld world,
        List<PipeRuntimeRecord> changedRecords)
    {
        if (changedRecords == null)
        {
            return false;
        }

        changedRecords.Clear();
        EnsureFluidSimulationBuffers();
        if (!ReferenceEquals(fluidJobPipeWorld, world))
        {
            return false;
        }

        int displayStateVersion = Pipe.FluidDisplayStateVersion;
        if (fluidJobResolvedDisplayStateVersion == displayStateVersion)
        {
            return true;
        }

        CompleteFluidSimulationShadow();
        if (fluidJobBuffers == null || fluidJobNetworkBuild.Count <= 0)
        {
            fluidJobResolvedDisplayStateVersion = displayStateVersion;
            fluidJobLastChangedDisplayPipeCount = 0;
            return true;
        }

        using (FluidDisplayResolveMarker.Auto())
        {
            long start = MapObjectTickProfiler.IsEnabled ? MapObjectTickProfiler.BeginSample() : 0L;
            for (int i = 0; i < fluidJobRecordOrder.Count; i++)
            {
                PipeRuntimeRecord record = fluidJobRecordOrder[i];
                int itemId = -1;
                int priority = 0;
                bool foundSource = record != null
                                   && record.HasValidPrototype
                                   && record.Prototype.TryGetDirectFluidDisplaySource(
                                       record,
                                       out itemId,
                                       out priority);
                fluidJobBuffers.DisplaySources[i] = new FluidPipeDisplaySource
                {
                    ItemId = foundSource ? itemId : -1,
                    Priority = foundSource ? priority : 0
                };
            }

            JobHandle resolveHandle = fluidJobBuffers.DisplayResolveJob.Schedule(
                fluidJobNetworkBuild.Count,
                1);
            resolveHandle.Complete();

            for (int networkIndex = 0; networkIndex < fluidJobNetworkBuild.Count; networkIndex++)
            {
                int displayItemId = fluidJobBuffers.NetworkDisplayItemIds[networkIndex];
                FluidNetworkRange range = fluidJobNetworkBuild[networkIndex];
                int end = range.PipeStart + range.PipeCount;
                for (int pipeIndex = range.PipeStart; pipeIndex < end; pipeIndex++)
                {
                    PipeRuntimeRecord record = fluidJobRecordOrder[pipeIndex];
                    fluidJobBuffers.States[pipeIndex] = new FluidPipeState
                    {
                        DisplayedFluidItemId = displayItemId
                    };
                    if (record == null || record.DisplayedFluidItemId == displayItemId)
                    {
                        continue;
                    }

                    record.DisplayedFluidItemId = displayItemId;
                    changedRecords.Add(record);
                }
            }

            fluidJobResolvedDisplayStateVersion = displayStateVersion;
            fluidJobDisplayResolveCount++;
            fluidJobLastChangedDisplayPipeCount = changedRecords.Count;
            if (MapObjectTickProfiler.IsEnabled)
            {
                MapObjectTickProfiler.EndNamedSample(
                    "Fluid",
                    "FluidJobs",
                    "Fluid Jobs Display Resolve",
                    start);
            }
        }

        return true;
    }

    private void ScheduleFluidSimulationShadow()
    {
        if (!MapObjectTickProfiler.IsEnabled)
        {
            CompleteFluidSimulationShadow();
            return;
        }

        EnsureFluidSimulationBuffers();
        if (fluidJobBuffers == null || fluidJobNetworkBuild.Count <= 0)
        {
            return;
        }

        CompleteFluidSimulationShadow();
        for (int i = 0; i < fluidJobRecordOrder.Count; i++)
        {
            PipeRuntimeRecord record = fluidJobRecordOrder[i];
            fluidJobBuffers.States[i] = new FluidPipeState
            {
                DisplayedFluidItemId = record != null ? record.DisplayedFluidItemId : -1
            };
        }

        using (FluidJobsScheduleMarker.Auto())
        {
            long start = MapObjectTickProfiler.BeginSample();
            fluidShadowJobHandle = fluidJobBuffers.ShadowJob.Schedule(fluidJobNetworkBuild.Count, 1);
            fluidShadowJobScheduled = true;
            MapObjectTickProfiler.EndNamedSample(
                "Fluid",
                "FluidJobs",
                "Fluid Jobs Shadow Schedule",
                start);
        }
    }

    private void CompleteFluidSimulationShadow()
    {
        if (!fluidShadowJobScheduled)
        {
            return;
        }

        using (FluidJobsCompleteMarker.Auto())
        {
            long start = MapObjectTickProfiler.IsEnabled ? MapObjectTickProfiler.BeginSample() : 0L;
            fluidShadowJobHandle.Complete();
            fluidShadowJobScheduled = false;
            fluidShadowChecksum = ComputeFluidShadowChecksum();
            fluidShadowCompletedTick = MapObjectTickManager.CurrentSimulationTick;
            if (MapObjectTickProfiler.IsEnabled)
            {
                MapObjectTickProfiler.EndNamedSample(
                    "Fluid",
                    "FluidJobs",
                    "Fluid Jobs Shadow Complete",
                    start);
            }
        }
    }

    private void EnsureFluidSimulationBuffers()
    {
        EnsurePipeSplitGroups();
        if (ReferenceEquals(fluidJobPipeWorld, pipeSplitWorld)
            && fluidJobTopologyVersion == pipeSplitTopologyVersion)
        {
            return;
        }

        using (FluidJobsBakeMarker.Auto())
        {
            long start = MapObjectTickProfiler.IsEnabled ? MapObjectTickProfiler.BeginSample() : 0L;
            RebuildFluidSimulationBuffers();
            if (MapObjectTickProfiler.IsEnabled)
            {
                MapObjectTickProfiler.EndNamedSample(
                    "Fluid",
                    "FluidJobs",
                    "Fluid Jobs Topology Bake",
                    start);
            }
        }
    }

    private void RebuildFluidSimulationBuffers()
    {
        CompleteFluidSimulationShadow();
        fluidJobBuffers?.Dispose();
        fluidJobBuffers = null;
        fluidJobRecordOrder.Clear();
        fluidJobIndices.Clear();
        fluidJobNetworkBuild.Clear();
        fluidJobPipeBuild.Clear();
        fluidJobCoordinateBuild.Clear();
        fluidJobEdgeBuild.Clear();
        fluidJobPipeWorld = pipeSplitWorld;
        fluidJobTopologyVersion = pipeSplitTopologyVersion;
        fluidShadowChecksum = 0UL;
        fluidJobResolvedDisplayStateVersion = -1;

        if (pipeSplitWorld == null || pipeSplitRecords.Count <= 0)
        {
            return;
        }

        fluidJobRecordOrder.AddRange(pipeSplitRecords);
        fluidJobRecordOrder.Sort(CompareFluidJobRecords);
        for (int i = 0; i < fluidJobRecordOrder.Count; i++)
        {
            fluidJobIndices.Add(fluidJobRecordOrder[i], i);
        }

        int currentRepresentative = -1;
        for (int i = 0; i < fluidJobRecordOrder.Count; i++)
        {
            PipeRuntimeRecord record = fluidJobRecordOrder[i];
            int representative = GetFluidJobRepresentative(record);
            if (representative != currentRepresentative)
            {
                fluidJobNetworkBuild.Add(new FluidNetworkRange
                {
                    PipeStart = i,
                    PipeCount = 0
                });
                currentRepresentative = representative;
            }

            int networkIndex = fluidJobNetworkBuild.Count - 1;
            FluidNetworkRange range = fluidJobNetworkBuild[networkIndex];
            range.PipeCount++;
            fluidJobNetworkBuild[networkIndex] = range;

            fluidJobCoordinateScratch.Clear();
            IReadOnlyList<Vector2Int> occupiedCoordinates = record.OccupiedCoordinates;
            for (int coordinateIndex = 0; coordinateIndex < occupiedCoordinates.Count; coordinateIndex++)
            {
                fluidJobCoordinateScratch.Add(occupiedCoordinates[coordinateIndex]);
            }

            fluidJobCoordinateScratch.Sort(CompareFluidCoordinates);
            int coordinateStart = fluidJobCoordinateBuild.Count;
            for (int coordinateIndex = 0; coordinateIndex < fluidJobCoordinateScratch.Count; coordinateIndex++)
            {
                Vector2Int coordinate = fluidJobCoordinateScratch[coordinateIndex];
                fluidJobCoordinateBuild.Add(new FluidPipeCoordinate
                {
                    X = coordinate.x,
                    Z = coordinate.y
                });
            }

            fluidJobPipeBuild.Add(new FluidPipeTopology
            {
                SimulationId = record.PlacementSequence,
                ItemId = record.ItemId,
                QuarterTurns = record.QuarterTurns,
                IsUnderground = record.IsUnderground ? 1 : 0,
                CoordinateStart = coordinateStart,
                CoordinateCount = fluidJobCoordinateScratch.Count
            });
        }

        for (int i = 0; i < fluidJobRecordOrder.Count; i++)
        {
            PipeRuntimeRecord record = fluidJobRecordOrder[i];
            fluidJobEdgeScratch.Clear();
            IReadOnlyList<Vector2Int> coordinates = record.OccupiedCoordinates;
            for (int coordinateIndex = 0; coordinateIndex < coordinates.Count; coordinateIndex++)
            {
                Vector2Int coordinate = coordinates[coordinateIndex];
                for (int directionIndex = 0; directionIndex < PipeSplitDirections.Length; directionIndex++)
                {
                    Vector2Int direction = PipeSplitDirections[directionIndex];
                    if (!record.HasConnectionTowardsAt(coordinate, direction)
                        || !pipeSplitWorld.TryGetAtCoordinate(coordinate + direction, out PipeRuntimeRecord neighbor)
                        || ReferenceEquals(record, neighbor)
                        || !neighbor.HasConnectionTowardsAt(coordinate + direction, -direction)
                        || !fluidJobIndices.TryGetValue(neighbor, out int neighborIndex)
                        || fluidJobEdgeScratch.Contains(neighborIndex))
                    {
                        continue;
                    }

                    fluidJobEdgeScratch.Add(neighborIndex);
                }
            }

            fluidJobEdgeScratch.Sort(CompareFluidJobIndices);
            FluidPipeTopology topology = fluidJobPipeBuild[i];
            topology.EdgeStart = fluidJobEdgeBuild.Count;
            topology.EdgeCount = fluidJobEdgeScratch.Count;
            fluidJobPipeBuild[i] = topology;
            fluidJobEdgeBuild.AddRange(fluidJobEdgeScratch);
        }

        fluidJobBuffers = new FluidSimulationBuffers(
            fluidJobNetworkBuild.Count,
            fluidJobPipeBuild.Count,
            fluidJobCoordinateBuild.Count,
            fluidJobEdgeBuild.Count);
        for (int i = 0; i < fluidJobNetworkBuild.Count; i++)
        {
            fluidJobBuffers.Networks[i] = fluidJobNetworkBuild[i];
        }

        for (int i = 0; i < fluidJobPipeBuild.Count; i++)
        {
            fluidJobBuffers.Pipes[i] = fluidJobPipeBuild[i];
            fluidJobBuffers.States[i] = new FluidPipeState
            {
                DisplayedFluidItemId = fluidJobRecordOrder[i].DisplayedFluidItemId
            };
        }

        for (int i = 0; i < fluidJobCoordinateBuild.Count; i++)
        {
            fluidJobBuffers.Coordinates[i] = fluidJobCoordinateBuild[i];
        }

        for (int i = 0; i < fluidJobEdgeBuild.Count; i++)
        {
            fluidJobBuffers.Edges[i] = fluidJobEdgeBuild[i];
        }

        fluidJobRebuildCount++;
    }

    private int CompareFluidJobRecords(PipeRuntimeRecord left, PipeRuntimeRecord right)
    {
        int leftRepresentative = GetFluidJobRepresentative(left);
        int rightRepresentative = GetFluidJobRepresentative(right);
        int comparison = leftRepresentative.CompareTo(rightRepresentative);
        return comparison != 0 ? comparison : ComparePipeSplitRecords(left, right);
    }

    private int GetFluidJobRepresentative(PipeRuntimeRecord record)
    {
        return pipeSplitIndices.TryGetValue(record, out int index)
            ? pipeSplitGraph.Representative(index)
            : int.MaxValue;
    }

    private int CompareFluidJobIndices(int left, int right)
    {
        return ComparePipeSplitRecords(fluidJobRecordOrder[left], fluidJobRecordOrder[right]);
    }

    private static int CompareFluidCoordinates(Vector2Int left, Vector2Int right)
    {
        int comparison = left.x.CompareTo(right.x);
        return comparison != 0 ? comparison : left.y.CompareTo(right.y);
    }

    private ulong ComputeFluidShadowChecksum()
    {
        if (fluidJobBuffers == null)
        {
            return 0UL;
        }

        ulong hash = 14695981039346656037UL;
        unchecked
        {
            for (int i = 0; i < fluidJobBuffers.Checksums.Length; i++)
            {
                hash = (hash ^ fluidJobBuffers.Checksums[i]) * 1099511628211UL;
            }
        }

        return hash;
    }

    private void AppendFluidJobRuntimeProfilerCounters()
    {
        MapObjectTickProfiler.AddRuntimeCounter("FluidJobs", "ShadowMode", true);
        MapObjectTickProfiler.AddRuntimeCounter("FluidJobs", "Authoritative", false);
        MapObjectTickProfiler.AddRuntimeCounter("FluidJobs", "DisplayAuthoritative", true);
        MapObjectTickProfiler.AddRuntimeCounter("FluidJobs", "Networks", FluidJobNetworkCount);
        MapObjectTickProfiler.AddRuntimeCounter("FluidJobs", "Pipes", FluidJobPipeCount);
        MapObjectTickProfiler.AddRuntimeCounter("FluidJobs", "Edges", fluidJobEdgeBuild.Count);
        MapObjectTickProfiler.AddRuntimeCounter("FluidJobs", "TopologyRebuilds", fluidJobRebuildCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FluidJobs",
            "DisplayResolves",
            fluidJobDisplayResolveCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FluidJobs",
            "LastChangedDisplayPipes",
            fluidJobLastChangedDisplayPipeCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FluidJobs",
            "LastCompletedTick",
            fluidShadowCompletedTick);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FluidJobs",
            "Checksum",
            fluidShadowChecksum.ToString("X16"));
    }

    private void ClearFluidSimulationJobState()
    {
        CompleteFluidSimulationShadow();
        fluidJobBuffers?.Dispose();
        fluidJobBuffers = null;
        fluidJobRecordOrder.Clear();
        fluidJobIndices.Clear();
        fluidJobNetworkBuild.Clear();
        fluidJobPipeBuild.Clear();
        fluidJobCoordinateBuild.Clear();
        fluidJobEdgeBuild.Clear();
        fluidJobEdgeScratch.Clear();
        fluidJobCoordinateScratch.Clear();
        fluidJobPipeWorld = null;
        fluidJobTopologyVersion = -1;
        fluidJobResolvedDisplayStateVersion = -1;
        fluidJobLastChangedDisplayPipeCount = 0;
        fluidShadowChecksum = 0UL;
        fluidShadowCompletedTick = -1L;
    }
}
