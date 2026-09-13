using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Rendering
{
    // One camera decision and one visual loop, after camera movement and before batch rendering.
    [DefaultExecutionOrder(850), DisallowMultipleComponent]
    public sealed class WorldVisualUpdateManager : MonoBehaviour
    {
        // Visible machines animate every frame. Hidden machines only need a bounded
        // visibility recheck; Unity still owns renderer frustum culling meanwhile.
        private const int CulledRecheckIntervalFrames = 4;
        private static WorldVisualUpdateManager instance;
        private readonly List<InstallationVisualState> targets = new List<InstallationVisualState>();
        private readonly CameraRenderCulling culling = new CameraRenderCulling();

        public int RegisteredCount => targets.Count;
        public int VisibleCount { get; private set; }
        public int CulledCount { get; private set; }
        public int LastTickedCount { get; private set; }
        public int LastVisualUpdateCount { get; private set; }
        public int LastDeferredCulledCount { get; private set; }

        public static void AppendProfilerCounters()
        {
            MapObjectTickProfiler.AddRuntimeCounter(
                "InstallationVisuals",
                "Registered",
                instance != null ? instance.RegisteredCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter(
                "InstallationVisuals",
                "Visible",
                instance != null ? instance.VisibleCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter(
                "InstallationVisuals",
                "Culled",
                instance != null ? instance.CulledCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter(
                "InstallationVisuals",
                "Ticked",
                instance != null ? instance.LastTickedCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter(
                "InstallationVisuals",
                "VisualUpdates",
                instance != null ? instance.LastVisualUpdateCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter(
                "InstallationVisuals",
                "DeferredCulled",
                instance != null ? instance.LastDeferredCulledCount : 0);
        }

        internal static void Register(InstallationVisualState target)
        {
            if (!Application.isPlaying || target.Index >= 0)
                return;
            if (instance == null)
            {
                var host = new GameObject(nameof(WorldVisualUpdateManager));
                instance = host.AddComponent<WorldVisualUpdateManager>();
                DontDestroyOnLoad(host);
            }
            target.Index = instance.targets.Count;
            instance.targets.Add(target);
        }

        internal static void Unregister(InstallationVisualState target)
        {
            if (target == null)
                return;
            if (instance != null && target.Index >= 0)
            {
                int index = target.Index;
                int last = instance.targets.Count - 1;
                InstallationVisualState moved = instance.targets[last];
                instance.targets[index] = moved;
                moved.Index = index;
                instance.targets.RemoveAt(last);
            }
            target.Index = -1;
            target.Release();
        }

        private void LateUpdate()
        {
            using var sample = MapObjectTickProfiler.SampleNamed(
                "Render",
                "Installation Visuals",
                "Installation Visual Update");
            culling.Update(Camera.main);
            VisibleCount = 0;
            CulledCount = 0;
            LastTickedCount = 0;
            LastVisualUpdateCount = 0;
            LastDeferredCulledCount = 0;
            int recheckPhase = Time.frameCount % CulledRecheckIntervalFrames;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                InstallationVisualState target = targets[i];
                if (target.Owner == null || !target.Owner.isActiveAndEnabled)
                {
                    Unregister(target);
                    continue;
                }

                bool shouldTick = target.Visible
                    || !culling.Enabled
                    || i % CulledRecheckIntervalFrames == recheckPhase;
                if (shouldTick)
                {
                    if (target.Tick(culling, Time.deltaTime))
                    {
                        LastVisualUpdateCount++;
                    }
                    LastTickedCount++;
                }
                else
                {
                    LastDeferredCulledCount++;
                }

                if (target.Visible) VisibleCount++;
                else CulledCount++;
            }

        }

        private void OnDisable()
        {
            for (int i = 0; i < targets.Count; i++)
                targets[i].SetVisible(true);
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;
            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].Index = -1;
                targets[i].Release();
            }
            targets.Clear();
            instance = null;
        }
    }
}

