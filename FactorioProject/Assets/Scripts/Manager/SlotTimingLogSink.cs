using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ProjectF.Persistence
{
    internal static class SlotTimingLogSink
    {
        private static readonly object Sync = new object();
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        internal static Func<long> TimestampProvider = System.Diagnostics.Stopwatch.GetTimestamp;
        internal static Func<string> LogDirectoryProvider = ResolveLogDirectory;
        internal static Action<string> InfoSink = UnityEngine.Debug.Log;
        internal static Action<string> WarningSink = UnityEngine.Debug.LogWarning;

        internal static void Append(
            int slotIndex,
            string operation,
            string header,
            string line,
            string status,
            double totalMilliseconds)
        {
            try
            {
                string directory = LogDirectoryProvider();
                Directory.CreateDirectory(directory);
                string path = Path.Combine(
                    directory,
                    $"slot_{slotIndex + 1:00}_{operation}_times.log");
                lock (Sync)
                {
                    if (!File.Exists(path) || new FileInfo(path).Length == 0L)
                        File.AppendAllText(path, header + Environment.NewLine, Utf8WithoutBom);
                    File.AppendAllText(path, line + Environment.NewLine, Utf8WithoutBom);
                }
                InfoSink?.Invoke(
                    $"[SaveManager] Slot {slotIndex + 1} {operation} timing: "
                    + $"status={status} totalMs={totalMilliseconds:F1} log={path}");
            }
            catch (Exception exception)
            {
                WarningSink?.Invoke(
                    $"[SaveManager] Slot {slotIndex + 1} {operation} timing log failed: {exception.Message}");
            }
        }

        internal static string Sanitize(string value)
            => string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private static string ResolveLogDirectory()
        {
            string fromDataPath = FindWorkspaceLogDirectory(Application.dataPath);
            if (!string.IsNullOrEmpty(fromDataPath)) return fromDataPath;
            string fromWorkingDirectory = FindWorkspaceLogDirectory(Environment.CurrentDirectory);
            if (!string.IsNullOrEmpty(fromWorkingDirectory)) return fromWorkingDirectory;
            return Path.Combine(Application.persistentDataPath, "Tools", "Log");
        }

        private static string FindWorkspaceLogDirectory(string startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath)) return null;
            DirectoryInfo directory;
            try { directory = new DirectoryInfo(Path.GetFullPath(startPath)); }
            catch (Exception) { return null; }

            for (int depth = 0; directory != null && depth < 8; depth++, directory = directory.Parent)
            {
                string tools = Path.Combine(directory.FullName, "Tools");
                if (Directory.Exists(tools)
                    && (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                        || Directory.Exists(Path.Combine(directory.FullName, "FactorioProject"))))
                    return Path.Combine(tools, "Log");
            }
            return null;
        }
    }

    internal sealed class SlotTimingStageMetrics
    {
        private sealed class Metric
        {
            public double ActiveMilliseconds;
            public double MaxStepMilliseconds;
            public int Steps;
        }

        private readonly Dictionary<string, Metric> metrics =
            new Dictionary<string, Metric>(StringComparer.Ordinal);
        private readonly List<string> order = new List<string>(12);

        internal void Record(string stage, double elapsedMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(stage))
            {
                return;
            }

            elapsedMilliseconds = Math.Max(0d, elapsedMilliseconds);
            if (!metrics.TryGetValue(stage, out Metric metric))
            {
                metric = new Metric();
                metrics.Add(stage, metric);
                order.Add(stage);
            }

            metric.ActiveMilliseconds += elapsedMilliseconds;
            metric.MaxStepMilliseconds = Math.Max(metric.MaxStepMilliseconds, elapsedMilliseconds);
            metric.Steps++;
        }

        internal string AppendToDetail(string detail)
        {
            if (order.Count <= 0)
            {
                return detail;
            }

            var builder = new StringBuilder(detail ?? string.Empty);
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append("stages=");
            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                string stage = order[i];
                Metric metric = metrics[stage];
                builder.Append(SlotTimingLogSink.Sanitize(stage))
                    .Append(':')
                    .Append(metric.ActiveMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                    .Append('/')
                    .Append(metric.MaxStepMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                    .Append('/')
                    .Append(metric.Steps.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
