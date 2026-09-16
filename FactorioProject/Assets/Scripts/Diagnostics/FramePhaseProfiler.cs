using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using UnityEngine.LowLevel;

namespace ProjectF.Diagnostics
{
    // Wall-clock boundaries work even when native ProfilerRecorder markers are stripped.
    // Nested phase values are inclusive and must not be summed with their parents.
    internal static class FramePhaseProfiler
    {
        private const int Capacity = 128;
        private sealed class Boundary { }
        private sealed class Phase
        {
            internal readonly string Name;
            internal readonly double[] Samples = new double[Capacity];
            internal long Started, Elapsed;
            internal Phase(string name) { Name = name; }
            internal void Begin() { if (collecting) Started = Stopwatch.GetTimestamp(); }
            internal void End()
            {
                if (!collecting || Started == 0L) return;
                Elapsed += Stopwatch.GetTimestamp() - Started;
                Started = 0L;
            }
        }

        private static readonly List<Phase> phases = new List<Phase>();
        private static readonly double[] scratch = new double[Capacity];
        private static bool installed, collecting;
        private static int sampleCount, nextSample;
        private static long frameStarted;
        private static bool detailedMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { SetEnabled(false); }

        internal static void SetEnabled(bool enabled)
        {
            if (enabled == installed) return;
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveBoundaries(ref loop);
            phases.Clear();
            sampleCount = nextSample = 0;
            collecting = false;
            installed = enabled;
            if (enabled)
            {
                detailedMode = MapObjectTickProfiler.IsDetailedEnabled;
                phases.Add(new Phase("PlayerLoop"));
                loop.subSystemList = Instrument(loop.subSystemList, null);
                var children = new List<PlayerLoopSystem>(loop.subSystemList);
                children.Insert(0, MakeBoundary(BeginFrame));
                children.Add(MakeBoundary(EndFrame));
                loop.subSystemList = children.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static PlayerLoopSystem MakeBoundary(PlayerLoopSystem.UpdateFunction callback)
            => new PlayerLoopSystem { type = typeof(Boundary), updateDelegate = callback };

        private static PlayerLoopSystem[] Instrument(PlayerLoopSystem[] systems, string parent)
        {
            if (systems == null) return Array.Empty<PlayerLoopSystem>();
            var result = new List<PlayerLoopSystem>(systems.Length * 2);
            foreach (PlayerLoopSystem original in systems)
            {
                PlayerLoopSystem system = original;
                string name = system.type != null ? system.type.Name : "Unknown";
                // All top-level phases, plus every direct child: scripts, physics,
                // rendering, waits, audio, etc. Future Unity phases are included too.
                var phase = new Phase(parent == null ? name : parent + "." + name);
                phases.Add(phase);
                if (parent == null) system.subSystemList = Instrument(system.subSystemList, name);
                result.Add(MakeBoundary(phase.Begin));
                result.Add(system);
                result.Add(MakeBoundary(phase.End));
            }
            return result.ToArray();
        }

        private static void RemoveBoundaries(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null) return;
            var children = new List<PlayerLoopSystem>(system.subSystemList.Length);
            foreach (PlayerLoopSystem original in system.subSystemList)
            {
                if (original.type == typeof(Boundary)) continue;
                PlayerLoopSystem child = original;
                RemoveBoundaries(ref child);
                children.Add(child);
            }
            system.subSystemList = children.ToArray();
        }

        private static void BeginFrame()
        {
            collecting = installed && MapObjectTickProfiler.IsEnabled;
            if (!collecting) return;
            bool mode = MapObjectTickProfiler.IsDetailedEnabled;
            if (mode != detailedMode)
            {
                detailedMode = mode;
                sampleCount = nextSample = 0;
            }
            for (int i = 0; i < phases.Count; i++) phases[i].Started = phases[i].Elapsed = 0L;
            frameStarted = Stopwatch.GetTimestamp();
        }

        private static void EndFrame()
        {
            if (!collecting || !installed) return;
            // Discard a frame which changed measurement mode midway through Update.
            if (detailedMode != MapObjectTickProfiler.IsDetailedEnabled)
            { collecting = false; return; }
            phases[0].Elapsed = Stopwatch.GetTimestamp() - frameStarted;
            double toMs = 1000d / Stopwatch.Frequency;
            for (int i = 0; i < phases.Count; i++)
                phases[i].Samples[nextSample] = phases[i].Elapsed * toMs;
            nextSample = (nextSample + 1) % Capacity;
            sampleCount = Math.Min(Capacity, sampleCount + 1);
            collecting = false;
        }

        internal static void AppendCounters()
        {
            MapObjectTickProfiler.AddRuntimeCounter("FramePhases", "CompletedFrames", sampleCount);
            MapObjectTickProfiler.AddRuntimeCounter("FramePhases", "DetailedTimers", detailedMode);
            MapObjectTickProfiler.AddRuntimeCounter("FramePhases", "Scope", "recent 128 completed PlayerLoops",
                "wall-clock inclusive; parent/child overlap; excludes time outside PlayerLoop; includes instrumentation overhead");
            if (sampleCount == 0) return;
            for (int i = 0; i < phases.Count; i++)
            {
                Phase phase = phases[i];
                double sum = 0d;
                for (int j = 0; j < sampleCount; j++) { scratch[j] = phase.Samples[j]; sum += scratch[j]; }
                Array.Sort(scratch, 0, sampleCount);
                double max = scratch[sampleCount - 1];
                // Keep the snapshot readable; never hide top-level boundaries.
                if (phase.Name.IndexOf('.') >= 0 && max < 0.05d) continue;
                double median = sampleCount % 2 == 0
                    ? (scratch[sampleCount / 2 - 1] + scratch[sampleCount / 2]) * 0.5d
                    : scratch[sampleCount / 2];
                double p95 = scratch[(int)Math.Ceiling(sampleCount * 0.95d) - 1];
                MapObjectTickProfiler.AddRuntimeCounter("FramePhases", phase.Name + "Ms",
                    (sum / sampleCount).ToString("0.###", CultureInfo.InvariantCulture),
                    string.Format(CultureInfo.InvariantCulture, "median={0:0.###} p95={1:0.###} max={2:0.###} samples={3}",
                        median, p95, max, sampleCount));
            }
        }
    }
}
