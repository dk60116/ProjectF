using System;
using System.Globalization;
using System.IO;
using Unity.Profiling.Memory;
using UnityEngine;
using UnityEngine.Networking.PlayerConnection;

namespace ProjectF.Diagnostics
{
    internal static class ManagedMemoryDiagnostics
    {
        private const double BytesPerMegabyte = 1024d * 1024d;
        private const double SnapshotCallbackWarningSeconds = 180d;
        private static readonly object snapshotStateLock = new object();

        private static bool trendInitialized;
        private static int trendSampleCount;
        private static long baselineManagedBytes;
        private static long previousManagedBytes;
        private static long peakManagedBytes;
        private static double baselineTimeSeconds;

        private static bool snapshotInProgress;
        private static string snapshotStatus = "never";
        private static string requestedSnapshotPath = string.Empty;
        private static string lastSnapshotPath = string.Empty;
        private static string lastSnapshotError = string.Empty;
        private static double snapshotRequestedAtSeconds;

        internal static long ResetTrend()
        {
            long managedBytes = Math.Max(0L, GC.GetTotalMemory(false));
            trendInitialized = true;
            trendSampleCount = 0;
            baselineManagedBytes = managedBytes;
            previousManagedBytes = managedBytes;
            peakManagedBytes = managedBytes;
            baselineTimeSeconds = Time.realtimeSinceStartupAsDouble;
            return managedBytes;
        }

        internal static void AppendProfilerCounters()
        {
            long currentManagedBytes = Math.Max(0L, GC.GetTotalMemory(false));
            double nowSeconds = Time.realtimeSinceStartupAsDouble;
            if (!trendInitialized)
            {
                ResetTrend();
                currentManagedBytes = baselineManagedBytes;
                nowSeconds = baselineTimeSeconds;
            }

            long previousDeltaBytes = currentManagedBytes - previousManagedBytes;
            long baselineDeltaBytes = currentManagedBytes - baselineManagedBytes;
            double elapsedSeconds = Math.Max(0d, nowSeconds - baselineTimeSeconds);
            double growthMegabytesPerMinute = elapsedSeconds > 0.001d
                ? baselineDeltaBytes / BytesPerMegabyte * 60d / elapsedSeconds
                : 0d;

            trendSampleCount++;
            peakManagedBytes = Math.Max(peakManagedBytes, currentManagedBytes);
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemoryTrend", "Samples", trendSampleCount,
                "One sample is recorded whenever a perf snapshot is requested.");
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemoryTrend", "CurrentMB", FormatMegabytes(currentManagedBytes));
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemoryTrend", "BaselineMB", FormatMegabytes(baselineManagedBytes));
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemoryTrend", "PeakMB", FormatMegabytes(peakManagedBytes));
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemoryTrend", "DeltaFromPreviousMB", FormatMegabytes(previousDeltaBytes));
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemoryTrend", "DeltaFromBaselineMB", FormatMegabytes(baselineDeltaBytes));
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemoryTrend", "ElapsedSeconds", FormatNumber(elapsedSeconds));
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemoryTrend", "GrowthMBPerMinute", FormatNumber(growthMegabytesPerMinute),
                "Positive sustained growth across snapshots is a leak candidate, not proof by itself.");
            previousManagedBytes = currentManagedBytes;

            bool inProgress;
            string status;
            string requestedPath;
            string completedPath;
            string error;
            double captureElapsedSeconds;
            bool callbackStalled = false;
            lock (snapshotStateLock)
            {
                captureElapsedSeconds = snapshotInProgress
                    ? Math.Max(0d, nowSeconds - snapshotRequestedAtSeconds)
                    : 0d;
                if (snapshotInProgress
                    && snapshotStatus == "capturing"
                    && captureElapsedSeconds >= SnapshotCallbackWarningSeconds)
                {
                    snapshotStatus = "stalled";
                    lastSnapshotError = "Unity did not invoke the snapshot completion callback. Restart the Player before retrying.";
                    callbackStalled = true;
                }

                inProgress = snapshotInProgress;
                status = snapshotStatus;
                requestedPath = requestedSnapshotPath;
                completedPath = lastSnapshotPath;
                error = lastSnapshotError;
            }

            if (callbackStalled)
            {
                Debug.LogError($"Managed memory snapshot stalled: {error} Requested path: {requestedPath}");
            }

            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemorySnapshot", "DevelopmentBuild", Debug.isDebugBuild ? 1 : 0);
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemorySnapshot", "EditorConnected", IsEditorConnected() ? 1 : 0);
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemorySnapshot", "InProgress", inProgress ? 1 : 0);
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemorySnapshot", "CaptureElapsedSeconds", FormatNumber(captureElapsedSeconds));
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemorySnapshot", "Status", status);
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemorySnapshot", "RequestedPath", requestedPath);
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemorySnapshot", "LastCompletedPath", completedPath);
            MapObjectTickProfiler.AddRuntimeCounter(
                "ManagedMemorySnapshot", "LastError", error);
        }

        internal static bool TryCaptureSnapshot(out string path, out string error)
        {
            path = string.Empty;
            error = string.Empty;
            string capturePath;
            lock (snapshotStateLock)
            {
                if (snapshotInProgress)
                {
                    path = requestedSnapshotPath;
                    error = "a managed memory snapshot is already in progress";
                    return false;
                }

                if (!Debug.isDebugBuild)
                {
                    snapshotStatus = "unavailable";
                    requestedSnapshotPath = string.Empty;
                    error = "memory snapshot requires a Development Build; use ProjectF > Diagnostics > Build Memory Snapshot Player in Unity";
                    lastSnapshotError = error;
                    return false;
                }

                if (IsEditorConnected())
                {
                    snapshotStatus = "unavailable";
                    requestedSnapshotPath = string.Empty;
                    error = "Unity Editor is connected to the Player; disconnect the Editor Profiler to save the snapshot on this PC";
                    lastSnapshotError = error;
                    return false;
                }

                try
                {
                    string directory = Path.Combine(
                        Application.persistentDataPath,
                        "MemorySnapshots");
                    Directory.CreateDirectory(directory);
                    capturePath = Path.Combine(
                        directory,
                        $"ProjectF_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_f{Time.frameCount}.tmpsnap");
                    capturePath = Path.GetFullPath(capturePath);
                    path = Path.ChangeExtension(capturePath, ".snap");
                    requestedSnapshotPath = path;
                    snapshotInProgress = true;
                    snapshotStatus = "capturing";
                    snapshotRequestedAtSeconds = Time.realtimeSinceStartupAsDouble;
                    lastSnapshotError = string.Empty;
                }
                catch (Exception exception)
                {
                    snapshotStatus = "failed";
                    lastSnapshotError = exception.Message;
                    error = exception.Message;
                    return false;
                }
            }

            try
            {
                Debug.Log($"Managed memory snapshot requested: {capturePath}");
                MemoryProfiler.TakeSnapshot(
                    capturePath,
                    CompleteSnapshot,
                    CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects);
                return true;
            }
            catch (Exception exception)
            {
                lock (snapshotStateLock)
                {
                    snapshotInProgress = false;
                    snapshotStatus = "failed";
                    lastSnapshotError = exception.Message;
                }

                error = exception.Message;
                return false;
            }
        }

        private static void CompleteSnapshot(string snapshotPath, bool success)
        {
            string completedPath = string.Empty;
            string error = string.Empty;
            if (success)
            {
                try
                {
                    string sourcePath = Path.GetFullPath(snapshotPath);
                    string finalPath = Path.ChangeExtension(sourcePath, ".snap");
                    if (!string.Equals(sourcePath, finalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(sourcePath, finalPath);
                    }

                    completedPath = finalPath;
                }
                catch (Exception exception)
                {
                    success = false;
                    error = exception.Message;
                }
            }
            else
            {
                error = "Unity memory snapshot capture failed";
            }

            lock (snapshotStateLock)
            {
                snapshotInProgress = false;
                snapshotStatus = success ? "completed" : "failed";
                lastSnapshotPath = completedPath;
                lastSnapshotError = error;
            }

            if (success)
            {
                Debug.Log($"Managed memory snapshot saved: {completedPath}");
            }
            else
            {
                Debug.LogError($"Managed memory snapshot failed: {error}");
            }
        }

        private static string FormatMegabytes(long bytes)
        {
            return FormatNumber(bytes / BytesPerMegabyte);
        }

        private static bool IsEditorConnected()
        {
            return Debug.isDebugBuild && !Application.isEditor && PlayerConnection.instance.isConnected;
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
