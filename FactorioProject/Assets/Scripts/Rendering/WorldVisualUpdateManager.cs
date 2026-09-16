using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Rendering
{
    // One camera decision and one visual loop, after camera movement and before batch rendering.
    [DefaultExecutionOrder(850), DisallowMultipleComponent]
    public sealed class WorldVisualUpdateManager : MonoBehaviour
    {
        // Unity owns actual renderer frustum culling. This coarser check only pauses
        // script visuals, Animators, and particles. A coarse spatial index keeps
        // off-screen installations out of the exact visibility loop.
        private const float SpatialCellSize = 32f;
        private const int SpatialPaddingCells = 2;
        private static WorldVisualUpdateManager instance;
        private readonly List<InstallationVisualState> targets = new List<InstallationVisualState>();
        private readonly Dictionary<Vector2Int, List<InstallationVisualState>> targetsByCell =
            new Dictionary<Vector2Int, List<InstallationVisualState>>();
        private readonly HashSet<InstallationVisualState> pendingVisibility = new HashSet<InstallationVisualState>();
        private readonly HashSet<InstallationVisualState> visibleTargets = new HashSet<InstallationVisualState>();
        private readonly HashSet<InstallationVisualState> candidateSet = new HashSet<InstallationVisualState>();
        private readonly List<InstallationVisualState> candidates = new List<InstallationVisualState>();
        private readonly CameraRenderCulling culling = new CameraRenderCulling();

        public int RegisteredCount => targets.Count;
        public int VisibleCount { get; private set; }
        public int CulledCount { get; private set; }
        public int LastTickedCount { get; private set; }
        public int LastVisualUpdateCount { get; private set; }
        public int LastDeferredCulledCount { get; private set; }
        public int LastCandidateCount { get; private set; }
        public int LastCandidateCellCount { get; private set; }

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
            MapObjectTickProfiler.AddRuntimeCounter("InstallationVisuals", "Candidates",
                instance != null ? instance.LastCandidateCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationVisuals", "CandidateCells",
                instance != null ? instance.LastCandidateCellCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationVisuals", "IndexedCells",
                instance != null ? instance.targetsByCell.Count : 0);
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
            instance.AddToSpatialIndex(target);
            instance.pendingVisibility.Add(target);
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
                instance.RemoveFromSpatialIndex(target);
                instance.targets[index] = moved;
                moved.Index = index;
                instance.targets.RemoveAt(last);
                instance.pendingVisibility.Remove(target);
                instance.visibleTargets.Remove(target);
                instance.candidateSet.Remove(target);
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
            if (MapObjectTickManager.WaitingForWorldLoad)
            {
                VisibleCount = 0;
                CulledCount = targets.Count;
                LastTickedCount = 0;
                LastVisualUpdateCount = 0;
                LastDeferredCulledCount = 0;
                LastCandidateCount = 0;
                LastCandidateCellCount = 0;
                return;
            }

            culling.Update(Camera.main);
            VisibleCount = 0;
            CulledCount = 0;
            LastTickedCount = 0;
            LastVisualUpdateCount = 0;
            LastDeferredCulledCount = 0;
            BuildCandidates();
            LastCandidateCount = candidates.Count;
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                InstallationVisualState target = candidates[i];
                if (target.Owner == null || !target.Owner.isActiveAndEnabled)
                {
                    Unregister(target);
                    continue;
                }

                RefreshSpatialIndex(target);
                if (target.Tick(culling, Time.deltaTime, true))
                {
                    LastVisualUpdateCount++;
                }

                LastTickedCount++;
                pendingVisibility.Remove(target);
                if (target.Visible) visibleTargets.Add(target);
                else visibleTargets.Remove(target);
            }
            VisibleCount = visibleTargets.Count;
            CulledCount = Mathf.Max(0, targets.Count - VisibleCount);
            LastDeferredCulledCount = Mathf.Max(0, targets.Count - LastTickedCount);
        }

        private void BuildCandidates()
        {
            candidateSet.Clear();
            candidates.Clear();
            LastCandidateCellCount = 0;
            if (!culling.TryGetVisibleCellRange(SpatialCellSize, SpatialPaddingCells,
                    out Vector2Int minimum, out Vector2Int maximum))
            {
                for (int i = 0; i < targets.Count; i++) AddCandidate(targets[i]);
                return;
            }

            long cellCount = CameraRenderCulling.GetCellCount(minimum, maximum);
            LastCandidateCellCount = cellCount <= int.MaxValue ? (int)cellCount : int.MaxValue;
            if (cellCount >= (long)Mathf.Max(1, targets.Count) * 2L)
            {
                for (int i = 0; i < targets.Count; i++) AddCandidate(targets[i]);
                return;
            }

            foreach (InstallationVisualState target in pendingVisibility) AddCandidate(target);
            foreach (InstallationVisualState target in visibleTargets) AddCandidate(target);
            for (int y = minimum.y; y <= maximum.y; y++)
            for (int x = minimum.x; x <= maximum.x; x++)
                if (targetsByCell.TryGetValue(new Vector2Int(x, y), out List<InstallationVisualState> cellTargets))
                    for (int i = 0; i < cellTargets.Count; i++) AddCandidate(cellTargets[i]);
        }

        private void AddCandidate(InstallationVisualState target)
        { if (target != null && candidateSet.Add(target)) candidates.Add(target); }

        private static Vector2Int GetSpatialCell(InstallationVisualState target)
        {
            Vector3 position = target.Owner != null ? target.Owner.transform.position : Vector3.zero;
            return new Vector2Int(Mathf.FloorToInt(position.x / SpatialCellSize),
                Mathf.FloorToInt(position.z / SpatialCellSize));
        }

        private void AddToSpatialIndex(InstallationVisualState target)
        {
            Vector2Int cell = GetSpatialCell(target);
            target.SpatialCell = cell;
            if (!targetsByCell.TryGetValue(cell, out List<InstallationVisualState> values))
                targetsByCell.Add(cell, values = new List<InstallationVisualState>(4));
            values.Add(target);
        }

        private void RemoveFromSpatialIndex(InstallationVisualState target)
        {
            if (!targetsByCell.TryGetValue(target.SpatialCell, out List<InstallationVisualState> values)) return;
            values.Remove(target);
            if (values.Count == 0) targetsByCell.Remove(target.SpatialCell);
        }

        private void RefreshSpatialIndex(InstallationVisualState target)
        {
            Vector2Int cell = GetSpatialCell(target);
            if (cell == target.SpatialCell) return;
            RemoveFromSpatialIndex(target);
            AddToSpatialIndex(target);
        }

        private void OnDisable()
        {
            if (ProjectFApplicationLifecycle.IsQuitting) return;

            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].SetVisible(true);
                pendingVisibility.Add(targets[i]);
            }
            visibleTargets.Clear();
            VisibleCount = targets.Count;
            CulledCount = LastTickedCount = LastVisualUpdateCount = LastDeferredCulledCount = 0;
            LastCandidateCount = LastCandidateCellCount = 0;
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;
            if (ProjectFApplicationLifecycle.IsQuitting)
            {
                instance = null;
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].Index = -1;
                targets[i].Release();
            }
            targets.Clear();
            targetsByCell.Clear(); pendingVisibility.Clear(); visibleTargets.Clear();
            candidateSet.Clear(); candidates.Clear();
            instance = null;
        }
    }
}

