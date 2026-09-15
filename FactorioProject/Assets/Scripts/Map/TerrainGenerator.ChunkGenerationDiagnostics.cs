using System;
using System.Runtime.CompilerServices;
using System.Text;
using ProjectF.Persistence;
using UnityEngine;

public partial class TerrainGenerator
{
    private enum ChunkGenerationDiagnosticStage
    {
        Preparation,
        EntityGeneration,
        InstallationRestore,
        BlockStateRestore,
        AnimalSpawn,
        RuntimeViewRefresh,
        ConveyorItemRestore,
        EmptyEntityRelease,
        SurfaceBuildSchedule,
        SurfaceBuildComplete,
        SurfaceMeshDataSchedule,
        SurfaceMeshDataApply,
        SurfaceFallbackBuild,
        SurfaceAssignment,
        Count
    }

    private readonly long[] chunkGenerationAllocatedBytesByStage =
        new long[(int)ChunkGenerationDiagnosticStage.Count];
    private readonly double[] chunkGenerationMillisecondsByStage =
        new double[(int)ChunkGenerationDiagnosticStage.Count];
    private readonly int[] chunkGenerationCallsByStage =
        new int[(int)ChunkGenerationDiagnosticStage.Count];
    private readonly StringBuilder chunkGenerationDiagnosticsBuilder = new StringBuilder(1024);
    private bool chunkGenerationDiagnosticsActive;
    private bool chunkGenerationDiagnosticsTrackAllocations;
    private bool chunkGenerationDiagnosticsReportToSlotLog;
    private Vector2Int chunkGenerationDiagnosticsCoordinate;
    private double chunkGenerationDiagnosticsStartTime;
    private int chunkGenerationDiagnosticsGen0Start;
    private int chunkGenerationDiagnosticsGen1Start;
    private int chunkGenerationDiagnosticsGen2Start;
    private int lastChunkGenerationGen0Collections;
    private int lastChunkGenerationGen1Collections;
    private int lastChunkGenerationGen2Collections;
    private int lastChunkGenerationPendingCount;

    public long LastChunkGenerationManagedAllocationBytes { get; private set; }
    public double LastChunkGenerationActiveMilliseconds { get; private set; }
    public double LastChunkGenerationWallMilliseconds { get; private set; }

    private void BeginChunkGenerationDiagnostics(Vector2Int chunkCoordinate)
    {
        bool reportToSlotLog = SlotLoadTimingLog.HasActiveSession;
        if (!enableChunkGenerationDiagnostics && !reportToSlotLog)
        {
            return;
        }

        Array.Clear(
            chunkGenerationAllocatedBytesByStage,
            0,
            chunkGenerationAllocatedBytesByStage.Length);
        Array.Clear(
            chunkGenerationMillisecondsByStage,
            0,
            chunkGenerationMillisecondsByStage.Length);
        Array.Clear(
            chunkGenerationCallsByStage,
            0,
            chunkGenerationCallsByStage.Length);
        chunkGenerationDiagnosticsCoordinate = chunkCoordinate;
        chunkGenerationDiagnosticsStartTime = Time.realtimeSinceStartupAsDouble;
        chunkGenerationDiagnosticsTrackAllocations = enableChunkGenerationDiagnostics;
        chunkGenerationDiagnosticsReportToSlotLog = reportToSlotLog;
        chunkGenerationDiagnosticsGen0Start = enableChunkGenerationDiagnostics
            ? GC.CollectionCount(0)
            : 0;
        chunkGenerationDiagnosticsGen1Start = enableChunkGenerationDiagnostics
            ? GC.CollectionCount(1)
            : 0;
        chunkGenerationDiagnosticsGen2Start = enableChunkGenerationDiagnostics
            ? GC.CollectionCount(2)
            : 0;
        chunkGenerationDiagnosticsActive = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long BeginChunkGenerationDiagnosticStage(out double startTime)
    {
        if (!chunkGenerationDiagnosticsActive)
        {
            startTime = 0d;
            return 0L;
        }

        startTime = Time.realtimeSinceStartupAsDouble;
        return chunkGenerationDiagnosticsTrackAllocations
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EndChunkGenerationDiagnosticStage(
        ChunkGenerationDiagnosticStage stage,
        long allocatedBytesAtStart,
        double startTime)
    {
        if (!chunkGenerationDiagnosticsActive)
        {
            return;
        }

        int stageIndex = (int)stage;
        long allocatedBytes = chunkGenerationDiagnosticsTrackAllocations
            ? GC.GetAllocatedBytesForCurrentThread() - allocatedBytesAtStart
            : 0L;
        chunkGenerationAllocatedBytesByStage[stageIndex] += Math.Max(0L, allocatedBytes);
        chunkGenerationMillisecondsByStage[stageIndex] +=
            Math.Max(0d, Time.realtimeSinceStartupAsDouble - startTime) * 1000d;
        chunkGenerationCallsByStage[stageIndex]++;
    }

    private void EndChunkGenerationDiagnostics()
    {
        if (!chunkGenerationDiagnosticsActive)
        {
            return;
        }

        bool trackAllocations = chunkGenerationDiagnosticsTrackAllocations;
        bool reportToSlotLog = chunkGenerationDiagnosticsReportToSlotLog;
        chunkGenerationDiagnosticsActive = false;
        chunkGenerationDiagnosticsTrackAllocations = false;
        chunkGenerationDiagnosticsReportToSlotLog = false;
        long totalAllocatedBytes = 0L;
        double totalActiveMilliseconds = 0d;
        for (int i = 0; i < (int)ChunkGenerationDiagnosticStage.Count; i++)
        {
            totalAllocatedBytes += chunkGenerationAllocatedBytesByStage[i];
            totalActiveMilliseconds += chunkGenerationMillisecondsByStage[i];
            if (reportToSlotLog && chunkGenerationCallsByStage[i] > 0)
            {
                SlotLoadTimingLog.RecordStageWork(
                    GetChunkLoadStageName((ChunkGenerationDiagnosticStage)i),
                    chunkGenerationMillisecondsByStage[i]);
            }
        }

        LastChunkGenerationManagedAllocationBytes = totalAllocatedBytes;
        LastChunkGenerationActiveMilliseconds = totalActiveMilliseconds;
        LastChunkGenerationWallMilliseconds =
            (Time.realtimeSinceStartupAsDouble - chunkGenerationDiagnosticsStartTime) * 1000d;
        lastChunkGenerationGen0Collections = trackAllocations
            ? GC.CollectionCount(0) - chunkGenerationDiagnosticsGen0Start
            : 0;
        lastChunkGenerationGen1Collections = trackAllocations
            ? GC.CollectionCount(1) - chunkGenerationDiagnosticsGen1Start
            : 0;
        lastChunkGenerationGen2Collections = trackAllocations
            ? GC.CollectionCount(2) - chunkGenerationDiagnosticsGen2Start
            : 0;
        lastChunkGenerationPendingCount = chunkStreamingScheduler?.PendingCount ?? 0;

        if (logChunkGenerationDiagnostics)
        {
            LogLastChunkGenerationDiagnostics();
        }
    }

    [ContextMenu("Log Last Chunk Generation Diagnostics")]
    private void LogLastChunkGenerationDiagnostics()
    {
        StringBuilder builder = chunkGenerationDiagnosticsBuilder;
        builder.Clear();
        builder.Append("Chunk generation diagnostics ")
            .Append(chunkGenerationDiagnosticsCoordinate)
            .Append(": managed=")
            .Append(LastChunkGenerationManagedAllocationBytes)
            .Append(" B, active=")
            .Append(LastChunkGenerationActiveMilliseconds.ToString("F3"))
            .Append(" ms, wall=")
            .Append(LastChunkGenerationWallMilliseconds.ToString("F3"))
            .Append(" ms, GC collections=")
            .Append(lastChunkGenerationGen0Collections)
            .Append('/')
            .Append(lastChunkGenerationGen1Collections)
            .Append('/')
            .Append(lastChunkGenerationGen2Collections)
            .Append(", pending=")
            .Append(lastChunkGenerationPendingCount);

        for (int i = 0; i < (int)ChunkGenerationDiagnosticStage.Count; i++)
        {
            if (chunkGenerationCallsByStage[i] == 0)
            {
                continue;
            }

            builder.AppendLine()
                .Append("  ")
                .Append((ChunkGenerationDiagnosticStage)i)
                .Append(": ")
                .Append(chunkGenerationAllocatedBytesByStage[i])
                .Append(" B, ")
                .Append(chunkGenerationMillisecondsByStage[i].ToString("F3"))
                .Append(" ms, calls=")
                .Append(chunkGenerationCallsByStage[i]);
        }

        Debug.Log(builder.ToString(), this);
    }

    private void CancelChunkGenerationDiagnostics()
    {
        chunkGenerationDiagnosticsActive = false;
        chunkGenerationDiagnosticsTrackAllocations = false;
        chunkGenerationDiagnosticsReportToSlotLog = false;
    }

    private static string GetChunkLoadStageName(ChunkGenerationDiagnosticStage stage)
    {
        switch (stage)
        {
            case ChunkGenerationDiagnosticStage.Preparation: return "chunk-preparation";
            case ChunkGenerationDiagnosticStage.EntityGeneration: return "chunk-entities";
            case ChunkGenerationDiagnosticStage.InstallationRestore: return "chunk-installations";
            case ChunkGenerationDiagnosticStage.BlockStateRestore: return "chunk-block-state";
            case ChunkGenerationDiagnosticStage.AnimalSpawn: return "chunk-animals";
            case ChunkGenerationDiagnosticStage.RuntimeViewRefresh: return "chunk-runtime-views";
            case ChunkGenerationDiagnosticStage.ConveyorItemRestore: return "chunk-items";
            case ChunkGenerationDiagnosticStage.EmptyEntityRelease: return "chunk-empty-release";
            case ChunkGenerationDiagnosticStage.SurfaceBuildSchedule: return "chunk-surface-schedule";
            case ChunkGenerationDiagnosticStage.SurfaceBuildComplete: return "chunk-surface-complete";
            case ChunkGenerationDiagnosticStage.SurfaceMeshDataSchedule: return "chunk-mesh-schedule";
            case ChunkGenerationDiagnosticStage.SurfaceMeshDataApply: return "chunk-mesh-apply";
            case ChunkGenerationDiagnosticStage.SurfaceFallbackBuild: return "chunk-surface-fallback";
            case ChunkGenerationDiagnosticStage.SurfaceAssignment: return "chunk-surface-assign";
            default: return "chunk-other";
        }
    }
}
