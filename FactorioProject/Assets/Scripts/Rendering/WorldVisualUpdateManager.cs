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
        private sealed class SpatialCellBucket
        {
            internal readonly List<InstallationVisualState> Targets = new List<InstallationVisualState>(4);
            internal Bounds WorldBounds;
            internal bool HasWorldBounds;
            internal bool BoundsDirty = true;
        }

        private readonly Dictionary<Vector2Int, SpatialCellBucket> targetsByCell =
            new Dictionary<Vector2Int, SpatialCellBucket>();
        private readonly HashSet<InstallationVisualState> pendingVisibility = new HashSet<InstallationVisualState>();
        private readonly HashSet<InstallationVisualState> visibleTargets = new HashSet<InstallationVisualState>();
        private readonly HashSet<InstallationVisualState> continuousVisibilityTargets =
            new HashSet<InstallationVisualState>();
        private readonly HashSet<InstallationVisualState> candidateSet = new HashSet<InstallationVisualState>();
        private readonly List<InstallationVisualState> candidates = new List<InstallationVisualState>();
        private readonly CameraRenderCulling culling = new CameraRenderCulling();
        private bool candidateCacheDirty = true;
        private long candidateRebuildCount;
        private long candidateCacheHitCount;
        private int lastCameraScanCandidateCount;
        private int lastCameraScanRejectedCount;
        private int lastIntersectingCandidateCellCount;

        public int RegisteredCount => targets.Count;
        public int VisibleCount { get; private set; }
        public int CulledCount { get; private set; }
        public int LastTickedCount { get; private set; }
        public int LastVisualUpdateCount { get; private set; }
        public int LastDeferredCulledCount { get; private set; }
        public int LastCandidateCount { get; private set; }
        public int LastCandidateCellCount { get; private set; }
        public int LastVisibilityRefreshCount { get; private set; }

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
            MapObjectTickProfiler.AddRuntimeCounter("InstallationVisuals", "ContinuousTargets",
                instance != null ? instance.continuousVisibilityTargets.Count : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationVisuals", "VisibilityChecks",
                instance != null ? instance.LastVisibilityRefreshCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationVisuals", "CandidateRebuilds",
                instance != null ? instance.candidateRebuildCount : 0L);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationVisuals", "CandidateCacheHits",
                instance != null ? instance.candidateCacheHitCount : 0L);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationVisuals", "CameraScanCandidates",
                instance != null ? instance.lastCameraScanCandidateCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationVisuals", "CameraScanRejected",
                instance != null ? instance.lastCameraScanRejectedCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationVisuals", "IntersectingCandidateCells",
                instance != null ? instance.lastIntersectingCandidateCellCount : 0);
        }

        internal static void Register(InstallationVisualState target)
        {
            if (!Application.isPlaying || target.Index >= 0)
                return;
            EnsureExists();
            target.Index = instance.targets.Count;
            instance.targets.Add(target);
            instance.AddToSpatialIndex(target);
            instance.pendingVisibility.Add(target);
            if (target.RequiresContinuousVisibilityRefresh)
            {
                instance.continuousVisibilityTargets.Add(target);
            }
            instance.candidateCacheDirty = true;
        }

        internal static void InvalidateVisibility(
            InstallationVisualState target,
            bool includeContinuousTarget = false)
        {
            if (instance == null || target == null || target.Index < 0
                || (!includeContinuousTarget && target.RequiresContinuousVisibilityRefresh))
            {
                return;
            }

            if (includeContinuousTarget)
            {
                if (target.RequiresContinuousVisibilityRefresh)
                {
                    instance.continuousVisibilityTargets.Add(target);
                }
                else
                {
                    instance.continuousVisibilityTargets.Remove(target);
                }
            }
            instance.pendingVisibility.Add(target);
            instance.candidateCacheDirty = true;
        }

        internal static void EnsureExists()
        {
            if (!Application.isPlaying || instance != null) return;
            var host = new GameObject(nameof(WorldVisualUpdateManager));
            instance = host.AddComponent<WorldVisualUpdateManager>();
            DontDestroyOnLoad(host);
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
                instance.continuousVisibilityTargets.Remove(target);
                instance.candidateSet.Remove(target);
                instance.candidateCacheDirty = true;
            }
            target.Index = -1;
            target.Release();
        }

        private void LateUpdate()
        {
            using var callerSample = MapObjectTickProfiler.SampleLateUpdateCaller<WorldVisualUpdateManager>();
            UtilityPole.FlushDeferredVisualRefreshes();
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
                LastVisibilityRefreshCount = 0;
                lastCameraScanCandidateCount = 0;
                lastCameraScanRejectedCount = 0;
                lastIntersectingCandidateCellCount = 0;
                return;
            }

            bool cameraChanged = culling.Update(Camera.main);
            VisibleCount = 0;
            CulledCount = 0;
            LastTickedCount = 0;
            LastVisualUpdateCount = 0;
            LastDeferredCulledCount = 0;
            LastVisibilityRefreshCount = 0;
            bool visibilityPrecomputed = false;
            if (cameraChanged || candidateCacheDirty)
            {
                candidateCacheDirty = false;
                BuildCandidates(cameraChanged);
                if (cameraChanged)
                {
                    visibilityPrecomputed = PrefilterCameraCandidates();
                }
                candidateRebuildCount++;
            }
            else
            {
                candidateCacheHitCount++;
            }
            LastCandidateCount = candidates.Count;
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                InstallationVisualState target = candidates[i];
                if (target.Owner == null || !target.Owner.isActiveAndEnabled)
                {
                    Unregister(target);
                    continue;
                }

                bool pendingRefresh = pendingVisibility.Contains(target);
                bool continuousRefresh = target.RequiresContinuousVisibilityRefresh;
                bool refreshVisibility = !visibilityPrecomputed
                                         && (cameraChanged || pendingRefresh || continuousRefresh);
                if (pendingRefresh || continuousRefresh)
                {
                    if (!visibilityPrecomputed)
                    {
                        RefreshSpatialIndex(target);
                    }
                }

                bool wasVisible = target.Visible;
                if (target.Tick(culling, Time.deltaTime, refreshVisibility))
                {
                    LastVisualUpdateCount++;
                }
                if (refreshVisibility)
                {
                    LastVisibilityRefreshCount++;
                }

                LastTickedCount++;
                pendingVisibility.Remove(target);
                if (target.Visible) visibleTargets.Add(target);
                else visibleTargets.Remove(target);
                if (wasVisible != target.Visible && !continuousRefresh)
                {
                    // Rebuild once after a static target crosses the frustum so the
                    // steady-state cache contains only visible, pending, and mobile targets.
                    candidateCacheDirty = true;
                }
                else if (pendingRefresh && !target.Visible)
                {
                    // A pending target that remains culled is needed for this refresh only.
                    candidateCacheDirty = true;
                }
            }
            VisibleCount = visibleTargets.Count;
            CulledCount = Mathf.Max(0, targets.Count - VisibleCount);
            LastDeferredCulledCount = Mathf.Max(0, targets.Count - LastTickedCount);
        }

        private void BuildCandidates(bool scanVisibleCells)
        {
            candidateSet.Clear();
            candidates.Clear();
            foreach (InstallationVisualState target in pendingVisibility) AddCandidate(target);
            foreach (InstallationVisualState target in visibleTargets) AddCandidate(target);
            foreach (InstallationVisualState target in continuousVisibilityTargets) AddCandidate(target);

            if (!scanVisibleCells)
            {
                return;
            }

            LastCandidateCellCount = 0;
            lastIntersectingCandidateCellCount = 0;
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

            for (int y = minimum.y; y <= maximum.y; y++)
            for (int x = minimum.x; x <= maximum.x; x++)
                if (targetsByCell.TryGetValue(new Vector2Int(x, y), out SpatialCellBucket bucket)
                    && CellMayBeVisible(bucket))
                {
                    lastIntersectingCandidateCellCount++;
                    for (int i = 0; i < bucket.Targets.Count; i++) AddCandidate(bucket.Targets[i]);
                }
        }

        private void AddCandidate(InstallationVisualState target)
        { if (target != null && candidateSet.Add(target)) candidates.Add(target); }

        private bool PrefilterCameraCandidates()
        {
            lastCameraScanCandidateCount = candidates.Count;
            lastCameraScanRejectedCount = 0;
            for (int candidateIndex = candidates.Count - 1; candidateIndex >= 0; candidateIndex--)
            {
                InstallationVisualState target = candidates[candidateIndex];
                if (target == null || target.Owner == null || !target.Owner.isActiveAndEnabled)
                {
                    continue;
                }

                bool pendingRefresh = pendingVisibility.Contains(target);
                bool continuousRefresh = target.RequiresContinuousVisibilityRefresh;
                if (pendingRefresh || continuousRefresh)
                {
                    RefreshSpatialIndex(target);
                }

                bool visible = target.RefreshVisibility(culling);
                LastVisibilityRefreshCount++;
                if (visible)
                {
                    visibleTargets.Add(target);
                    continue;
                }

                visibleTargets.Remove(target);
                if (pendingRefresh || continuousRefresh)
                {
                    continue;
                }

                candidateSet.Remove(target);
                int lastIndex = candidates.Count - 1;
                candidates[candidateIndex] = candidates[lastIndex];
                candidates.RemoveAt(lastIndex);
                lastCameraScanRejectedCount++;
            }

            return true;
        }

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
            if (!targetsByCell.TryGetValue(cell, out SpatialCellBucket bucket))
                targetsByCell.Add(cell, bucket = new SpatialCellBucket());
            bucket.Targets.Add(target);
            bucket.BoundsDirty = true;
        }

        private void RemoveFromSpatialIndex(InstallationVisualState target)
        {
            if (!targetsByCell.TryGetValue(target.SpatialCell, out SpatialCellBucket bucket)) return;
            bucket.Targets.Remove(target);
            bucket.BoundsDirty = true;
            if (bucket.Targets.Count == 0) targetsByCell.Remove(target.SpatialCell);
        }

        private void RefreshSpatialIndex(InstallationVisualState target)
        {
            Vector2Int cell = GetSpatialCell(target);
            if (cell == target.SpatialCell)
            {
                if (targetsByCell.TryGetValue(cell, out SpatialCellBucket bucket))
                    bucket.BoundsDirty = true;
                return;
            }
            RemoveFromSpatialIndex(target);
            AddToSpatialIndex(target);
        }

        private bool CellMayBeVisible(SpatialCellBucket bucket)
        {
            if (bucket.BoundsDirty)
            {
                bucket.HasWorldBounds = false;
                for (int i = 0; i < bucket.Targets.Count; i++)
                {
                    InstallationVisualState target = bucket.Targets[i];
                    if (target == null || !target.TryGetWorldBounds(out Bounds targetBounds))
                    {
                        continue;
                    }

                    if (bucket.HasWorldBounds) bucket.WorldBounds.Encapsulate(targetBounds);
                    else
                    {
                        bucket.WorldBounds = targetBounds;
                        bucket.HasWorldBounds = true;
                    }
                }
                bucket.BoundsDirty = false;
            }

            // Missing bounds are kept conservative so stale/null entries can still unregister.
            return !bucket.HasWorldBounds || culling.Intersects(bucket.WorldBounds);
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
            candidateCacheDirty = true;
            VisibleCount = targets.Count;
            CulledCount = LastTickedCount = LastVisualUpdateCount = LastDeferredCulledCount = 0;
            LastCandidateCount = LastCandidateCellCount = LastVisibilityRefreshCount = 0;
            lastCameraScanCandidateCount = lastCameraScanRejectedCount = 0;
            lastIntersectingCandidateCellCount = 0;
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
            continuousVisibilityTargets.Clear();
            candidateSet.Clear(); candidates.Clear();
            instance = null;
        }
    }
}

