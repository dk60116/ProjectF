using System;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

public partial class TerrainGenerator
{
    private enum ChunkGenerationDiagnosticStage
    {
        Preparation,
        RuntimeProxyGeneration,
        InstallationRestore,
        BlockStateRestore,
        AnimalSpawn,
        RuntimeViewRefresh,
        ConveyorItemRestore,
        EmptyProxyRelease,
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
        if (!enableChunkGenerationDiagnostics)
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
        chunkGenerationDiagnosticsGen0Start = GC.CollectionCount(0);
        chunkGenerationDiagnosticsGen1Start = GC.CollectionCount(1);
        chunkGenerationDiagnosticsGen2Start = GC.CollectionCount(2);
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
        return GC.GetAllocatedBytesForCurrentThread();
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
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesAtStart;
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

        chunkGenerationDiagnosticsActive = false;
        long totalAllocatedBytes = 0L;
        double totalActiveMilliseconds = 0d;
        for (int i = 0; i < (int)ChunkGenerationDiagnosticStage.Count; i++)
        {
            totalAllocatedBytes += chunkGenerationAllocatedBytesByStage[i];
            totalActiveMilliseconds += chunkGenerationMillisecondsByStage[i];
        }

        LastChunkGenerationManagedAllocationBytes = totalAllocatedBytes;
        LastChunkGenerationActiveMilliseconds = totalActiveMilliseconds;
        LastChunkGenerationWallMilliseconds =
            (Time.realtimeSinceStartupAsDouble - chunkGenerationDiagnosticsStartTime) * 1000d;
        lastChunkGenerationGen0Collections =
            GC.CollectionCount(0) - chunkGenerationDiagnosticsGen0Start;
        lastChunkGenerationGen1Collections =
            GC.CollectionCount(1) - chunkGenerationDiagnosticsGen1Start;
        lastChunkGenerationGen2Collections =
            GC.CollectionCount(2) - chunkGenerationDiagnosticsGen2Start;
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
    }
}
