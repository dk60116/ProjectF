using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;

namespace ProjectF.Persistence
{
    /// <summary>Records snapshot and background file-write costs for one slot save.</summary>
    public static class SlotSaveTimingLog
    {
        private const string Header =
            "completedUtc\tslot\tstatus\tsource\ttotalMs\tsnapshotWallMs\tsnapshotActiveMs"
            + "\tmaxSliceMs\tsnapshotFrames\tcheckpoints\twriteMs\tdetail";

        private sealed class Session
        {
            public int SlotIndex;
            public string Source;
            public long StartedAt;
            public long SnapshotCompletedAt;
            public double SnapshotActiveMilliseconds;
            public double MaxSliceMilliseconds;
            public int SnapshotFrames;
            public int Checkpoints;
            public readonly SlotTimingStageMetrics Stages = new SlotTimingStageMetrics();
        }

        private static readonly object Sync = new object();
        private static Session active;

        public static void Reset()
        {
            lock (Sync) { active = null; }
        }

        public static void Begin(int slotIndex, string source)
        {
            CancelActive("replaced-by-new-slot-save");
            lock (Sync)
            {
                active = new Session
                {
                    SlotIndex = slotIndex,
                    Source = SlotTimingLogSink.Sanitize(source),
                    StartedAt = SlotTimingLogSink.TimestampProvider()
                };
            }
        }

        public static bool IsActiveFor(int slotIndex)
        {
            lock (Sync) { return active != null && active.SlotIndex == slotIndex; }
        }

        public static void RecordStageWork(string stage, double elapsedMilliseconds)
        {
            lock (Sync)
            {
                active?.Stages.Record(stage, elapsedMilliseconds);
            }
        }

        public static IEnumerator TrackStage(string stage, IEnumerator work)
        {
            if (work == null) yield break;
            try
            {
                while (true)
                {
                    long startedAt = SlotTimingLogSink.TimestampProvider();
                    bool hasNext;
                    try { hasNext = work.MoveNext(); }
                    finally
                    {
                        RecordStageWork(stage, ElapsedMilliseconds(
                            startedAt,
                            SlotTimingLogSink.TimestampProvider()));
                    }

                    if (!hasNext) yield break;
                    yield return work.Current;
                }
            }
            finally
            {
                (work as IDisposable)?.Dispose();
            }
        }

        public static void MarkSnapshotComplete(
            int slotIndex,
            double activeMilliseconds,
            double maxSliceMilliseconds,
            int snapshotFrames,
            int checkpoints)
        {
            lock (Sync)
            {
                if (active == null || active.SlotIndex != slotIndex || active.SnapshotCompletedAt != 0L) return;
                active.SnapshotCompletedAt = SlotTimingLogSink.TimestampProvider();
                active.SnapshotActiveMilliseconds = Math.Max(0d, activeMilliseconds);
                active.MaxSliceMilliseconds = Math.Max(0d, maxSliceMilliseconds);
                active.SnapshotFrames = Math.Max(0, snapshotFrames);
                active.Checkpoints = Math.Max(0, checkpoints);
            }
        }

        public static void Complete(int slotIndex, string detail = "save-published")
            => Finish(slotIndex, "Completed", detail);

        public static void Fail(int slotIndex, string detail)
            => Finish(slotIndex, "Failed", detail);

        public static void CancelActive(string detail)
        {
            Session session;
            lock (Sync) { session = active; active = null; }
            if (session != null) Write(session, "Cancelled", detail, SlotTimingLogSink.TimestampProvider());
        }

        private static void Finish(int slotIndex, string status, string detail)
        {
            Session session;
            lock (Sync)
            {
                if (active == null || active.SlotIndex != slotIndex) return;
                session = active;
                active = null;
            }
            Write(session, status, detail, SlotTimingLogSink.TimestampProvider());
        }

        private static void Write(Session session, string status, string detail, long finishedAt)
        {
            double total = ElapsedMilliseconds(session.StartedAt, finishedAt);
            string snapshotWall = session.SnapshotCompletedAt > 0L
                ? ElapsedMilliseconds(session.StartedAt, session.SnapshotCompletedAt).ToString("F1", CultureInfo.InvariantCulture)
                : string.Empty;
            string write = session.SnapshotCompletedAt > 0L
                ? ElapsedMilliseconds(session.SnapshotCompletedAt, finishedAt).ToString("F1", CultureInfo.InvariantCulture)
                : string.Empty;
            string line = string.Join("\t",
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                (session.SlotIndex + 1).ToString(CultureInfo.InvariantCulture),
                status,
                session.Source,
                total.ToString("F1", CultureInfo.InvariantCulture),
                snapshotWall,
                session.SnapshotActiveMilliseconds.ToString("F1", CultureInfo.InvariantCulture),
                session.MaxSliceMilliseconds.ToString("F1", CultureInfo.InvariantCulture),
                session.SnapshotFrames.ToString(CultureInfo.InvariantCulture),
                session.Checkpoints.ToString(CultureInfo.InvariantCulture),
                write,
                SlotTimingLogSink.Sanitize(session.Stages.AppendToDetail(detail)));
            SlotTimingLogSink.Append(
                session.SlotIndex, "save", Header, line, status, total);
        }

        private static double ElapsedMilliseconds(long start, long end)
            => Math.Max(0L, end - start) * (1000d / Stopwatch.Frequency);
    }
}
