using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Rendering
{
    public interface IWorldColliderCullingTarget
    {
        bool ColliderCullingAlive { get; }
        bool ColliderCullingExempt { get; }
        bool ColliderCullingCulled { get; }
        Vector3 ColliderCullingPosition { get; }
        float ColliderCullingRadius { get; }
        int ColliderCullingManagedCount { get; }
        int ColliderCullingAccountedCount { get; set; }
        int ColliderCullingRegistryIndex { get; set; }
        Vector2Int ColliderCullingCell { get; set; }
        int ColliderCullingCellIndex { get; set; }
        int ColliderCullingPendingIndex { get; set; }
        int ColliderCullingActiveIndex { get; set; }
        void ApplyColliderCulling(bool culled);
        void ReleaseColliderCulling();
    }

    /// <summary>
    /// Keeps static MapObject colliders only around the interaction origin. Targets live in
    /// spatial cells, so a stationary player pays no world-size scan after the initial sweep.
    /// </summary>
    [DefaultExecutionOrder(840), DisallowMultipleComponent]
    public sealed class WorldColliderCullingManager : MonoBehaviour
    {
        internal const float EnableDistance = 36f;
        internal const float DisableDistance = 44f;
        internal const float CellSize = 16f;
        internal const int InitialEvaluationBudget = 6144;
        private const float OriginRefreshDistance = 4f;
        private const float OriginRefreshDistanceSquared =
            OriginRefreshDistance * OriginRefreshDistance;

        private static WorldColliderCullingManager instance;
        private readonly List<IWorldColliderCullingTarget> targets =
            new List<IWorldColliderCullingTarget>(1024);
        private readonly Dictionary<Vector2Int, List<IWorldColliderCullingTarget>> targetsByCell =
            new Dictionary<Vector2Int, List<IWorldColliderCullingTarget>>();
        private readonly Stack<List<IWorldColliderCullingTarget>> recycledCellLists =
            new Stack<List<IWorldColliderCullingTarget>>();
        private readonly List<IWorldColliderCullingTarget> pendingTargets =
            new List<IWorldColliderCullingTarget>(1024);
        private readonly List<IWorldColliderCullingTarget> activeTargets =
            new List<IWorldColliderCullingTarget>(256);
        private int culledTargetCount;
        private int managedColliderCount;
        private int lastCheckedTargetCount;
        private int lastSpatialCandidateCount;
        private int spatialRefreshCount;
        private int initialSweepCount;
        private float maximumTargetRadius;
        private Vector3 lastOrigin;
        private bool hasOrigin;
        private bool cullingActive;

        public int RegisteredTargetCount => targets.Count;
        public int CulledTargetCount => culledTargetCount;
        public int ManagedColliderCount => managedColliderCount;
        public int LastCheckedTargetCount => lastCheckedTargetCount;
        public int LastSpatialCandidateCount => lastSpatialCandidateCount;
        public int PendingTargetCount => pendingTargets.Count;
        public int ActiveTargetCount => activeTargets.Count;
        public int SpatialCellCount => targetsByCell.Count;
        public int SpatialRefreshCount => spatialRefreshCount;
        public int InitialSweepCount => initialSweepCount;

        internal static void Register(IWorldColliderCullingTarget target)
        {
            if (target == null
                || !Application.isPlaying
                || ProjectFApplicationLifecycle.IsQuitting
                || target.ColliderCullingRegistryIndex >= 0)
                return;
            Ensure().AddTarget(target);
        }

        internal static void RefreshSpatialRegistration(IWorldColliderCullingTarget target)
        {
            if (target == null
                || ProjectFApplicationLifecycle.IsQuitting
                || instance == null
                || target.ColliderCullingRegistryIndex < 0)
                return;
            instance.RefreshSpatialTarget(target);
        }

        internal static void Unregister(IWorldColliderCullingTarget target)
        {
            if (target == null || ProjectFApplicationLifecycle.IsQuitting) return;
            if (instance != null && target.ColliderCullingRegistryIndex >= 0)
            {
                instance.RemoveTarget(target, true);
                return;
            }

            target.ReleaseColliderCulling();
            ResetTargetIndices(target);
        }

        public static void AppendProfilerCounters()
        {
            WorldColliderCullingManager manager = instance;
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "RegisteredTargets",
                manager != null ? manager.RegisteredTargetCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "CulledTargets",
                manager != null ? manager.culledTargetCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "ManagedColliders",
                manager != null ? manager.managedColliderCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "ActiveTargets",
                manager != null ? manager.activeTargets.Count : 0);
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "PendingTargets",
                manager != null ? manager.pendingTargets.Count : 0);
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "SpatialCells",
                manager != null ? manager.targetsByCell.Count : 0);
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "LastCheckedTargets",
                manager != null ? manager.lastCheckedTargetCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "LastSpatialCandidates",
                manager != null ? manager.lastSpatialCandidateCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "SpatialRefreshes",
                manager != null ? manager.spatialRefreshCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "InitialSweeps",
                manager != null ? manager.initialSweepCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("ColliderCulling", "CellSize", CellSize);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance?.ReleaseAll();
            instance = null;
        }

        private static WorldColliderCullingManager Ensure()
        {
            if (instance != null) return instance;
            GameObject host = new GameObject(nameof(WorldColliderCullingManager));
            instance = host.AddComponent<WorldColliderCullingManager>();
            DontDestroyOnLoad(host);
            return instance;
        }

        private void AddTarget(IWorldColliderCullingTarget target)
        {
            target.ColliderCullingRegistryIndex = targets.Count;
            targets.Add(target);
            AddTargetToCell(target, ResolveCell(target.ColliderCullingPosition));
            target.ColliderCullingAccountedCount = target.ColliderCullingManagedCount;
            managedColliderCount += target.ColliderCullingAccountedCount;
            if (target.ColliderCullingCulled) culledTargetCount++;
            maximumTargetRadius = Mathf.Max(maximumTargetRadius, target.ColliderCullingRadius);
            QueueTargetEvaluation(target);
        }

        private void RefreshSpatialTarget(IWorldColliderCullingTarget target)
        {
            Vector2Int nextCell = ResolveCell(target.ColliderCullingPosition);
            if (target.ColliderCullingCell != nextCell)
            {
                RemoveTargetFromCell(target);
                AddTargetToCell(target, nextCell);
            }

            maximumTargetRadius = Mathf.Max(maximumTargetRadius, target.ColliderCullingRadius);
            QueueTargetEvaluation(target);
        }

        private void RemoveTarget(IWorldColliderCullingTarget target, bool release)
        {
            if (target == null) return;
            int registryIndex = target.ColliderCullingRegistryIndex;
            if (registryIndex < 0
                || registryIndex >= targets.Count
                || !ReferenceEquals(targets[registryIndex], target))
            {
                return;
            }

            RemovePendingTarget(target);
            RemoveActiveTarget(target);
            RemoveTargetFromCell(target);
            managedColliderCount -= target.ColliderCullingAccountedCount;
            if (target.ColliderCullingCulled) culledTargetCount--;

            int lastIndex = targets.Count - 1;
            IWorldColliderCullingTarget moved = targets[lastIndex];
            targets[registryIndex] = moved;
            moved.ColliderCullingRegistryIndex = registryIndex;
            targets.RemoveAt(lastIndex);
            ResetTargetIndices(target);
            if (release) target.ReleaseColliderCulling();
        }

        private void LateUpdate()
        {
            using var callerSample = MapObjectTickProfiler.SampleLateUpdateCaller<WorldColliderCullingManager>();
            GameManager gameManager = GameManager.Instance;
            Player player = gameManager != null ? gameManager.Player : null;
            if (CameraRenderCulling.Disabled || player == null)
            {
                lastCheckedTargetCount = 0;
                lastSpatialCandidateCount = 0;
                if (cullingActive) RestoreAll();
                return;
            }

            Transform playerTransform = player.BodyTransform != null ? player.BodyTransform : player.transform;
            Vector3 origin = playerTransform.position;
            if (gameManager.FreeCamera && !gameManager.FreeCameraPlayerCulling)
            {
                Camera freeCamera = Camera.main;
                if (freeCamera != null) origin = freeCamera.transform.position;
            }

            lastCheckedTargetCount = 0;
            lastSpatialCandidateCount = 0;
            using var sample = MapObjectTickProfiler.SampleNamed(
                "Physics", nameof(WorldColliderCullingManager), "MapObject Collider Culling");
            if (!cullingActive)
            {
                cullingActive = true;
                initialSweepCount++;
                QueueAllTargetsForEvaluation();
                RefreshNearbyTargets(origin);
                lastOrigin = origin;
                hasOrigin = true;
            }
            else if (!hasOrigin || HorizontalDistanceSquared(lastOrigin, origin) >= OriginRefreshDistanceSquared)
            {
                RefreshNearbyTargets(origin);
                lastOrigin = origin;
                hasOrigin = true;
            }

            ProcessPendingTargets(origin, InitialEvaluationBudget);
        }

        private void ProcessPendingTargets(Vector3 origin, int budget)
        {
            int processed = 0;
            while (pendingTargets.Count > 0 && processed < budget)
            {
                IWorldColliderCullingTarget target = pendingTargets[pendingTargets.Count - 1];
                RemovePendingTarget(target);
                if (!target.ColliderCullingAlive)
                {
                    RemoveTarget(target, true);
                }
                else
                {
                    EvaluateTarget(target, origin);
                }
                processed++;
            }
        }

        private void RefreshNearbyTargets(Vector3 origin)
        {
            spatialRefreshCount++;
            for (int i = activeTargets.Count - 1; i >= 0; i--)
            {
                IWorldColliderCullingTarget target = activeTargets[i];
                if (!target.ColliderCullingAlive)
                {
                    RemoveTarget(target, true);
                    continue;
                }
                EvaluateTarget(target, origin);
            }

            float queryDistance = EnableDistance + maximumTargetRadius;
            int minimumX = Mathf.FloorToInt((origin.x - queryDistance) / CellSize);
            int maximumX = Mathf.FloorToInt((origin.x + queryDistance) / CellSize);
            int minimumZ = Mathf.FloorToInt((origin.z - queryDistance) / CellSize);
            int maximumZ = Mathf.FloorToInt((origin.z + queryDistance) / CellSize);
            for (int z = minimumZ; z <= maximumZ; z++)
            {
                for (int x = minimumX; x <= maximumX; x++)
                {
                    if (!targetsByCell.TryGetValue(
                            new Vector2Int(x, z),
                            out List<IWorldColliderCullingTarget> cellTargets))
                    {
                        continue;
                    }

                    for (int i = cellTargets.Count - 1; i >= 0; i--)
                    {
                        IWorldColliderCullingTarget target = cellTargets[i];
                        lastSpatialCandidateCount++;
                        if (!target.ColliderCullingAlive)
                        {
                            RemoveTarget(target, true);
                            continue;
                        }

                        if (target.ColliderCullingPendingIndex >= 0)
                        {
                            RemovePendingTarget(target);
                            EvaluateTarget(target, origin);
                        }
                        else if (target.ColliderCullingCulled)
                        {
                            EvaluateTarget(target, origin);
                        }
                        else if (!target.ColliderCullingExempt)
                        {
                            AddActiveTarget(target);
                        }
                    }
                }
            }
        }

        private void EvaluateTarget(IWorldColliderCullingTarget target, Vector3 origin)
        {
            lastCheckedTargetCount++;
            bool wasCulled = target.ColliderCullingCulled;
            bool exempt = target.ColliderCullingExempt;
            bool shouldCull = !exempt && IsOutsideRange(target, origin);
            if (wasCulled != shouldCull) target.ApplyColliderCulling(shouldCull);

            if (!exempt && !target.ColliderCullingCulled) AddActiveTarget(target);
            else RemoveActiveTarget(target);

            int currentManagedCount = target.ColliderCullingManagedCount;
            managedColliderCount += currentManagedCount - target.ColliderCullingAccountedCount;
            target.ColliderCullingAccountedCount = currentManagedCount;
            if (wasCulled != target.ColliderCullingCulled)
                culledTargetCount += target.ColliderCullingCulled ? 1 : -1;
        }

        private static bool IsOutsideRange(IWorldColliderCullingTarget target, Vector3 origin)
        {
            float baseDistance = target.ColliderCullingCulled ? EnableDistance : DisableDistance;
            float distance = Mathf.Max(0f, baseDistance + target.ColliderCullingRadius);
            return HorizontalDistanceSquared(target.ColliderCullingPosition, origin) > distance * distance;
        }

        private static float HorizontalDistanceSquared(Vector3 left, Vector3 right)
        {
            float x = left.x - right.x;
            float z = left.z - right.z;
            return x * x + z * z;
        }

        private void AddTargetToCell(IWorldColliderCullingTarget target, Vector2Int cell)
        {
            if (!targetsByCell.TryGetValue(cell, out List<IWorldColliderCullingTarget> cellTargets))
            {
                cellTargets = recycledCellLists.Count > 0
                    ? recycledCellLists.Pop()
                    : new List<IWorldColliderCullingTarget>(8);
                targetsByCell.Add(cell, cellTargets);
            }

            target.ColliderCullingCell = cell;
            target.ColliderCullingCellIndex = cellTargets.Count;
            cellTargets.Add(target);
        }

        private void RemoveTargetFromCell(IWorldColliderCullingTarget target)
        {
            Vector2Int cell = target.ColliderCullingCell;
            int index = target.ColliderCullingCellIndex;
            if (index < 0
                || !targetsByCell.TryGetValue(cell, out List<IWorldColliderCullingTarget> cellTargets)
                || index >= cellTargets.Count
                || !ReferenceEquals(cellTargets[index], target))
            {
                return;
            }

            int lastIndex = cellTargets.Count - 1;
            IWorldColliderCullingTarget moved = cellTargets[lastIndex];
            cellTargets[index] = moved;
            moved.ColliderCullingCellIndex = index;
            cellTargets.RemoveAt(lastIndex);
            target.ColliderCullingCellIndex = -1;
            if (cellTargets.Count > 0) return;
            targetsByCell.Remove(cell);
            recycledCellLists.Push(cellTargets);
        }

        private void QueueAllTargetsForEvaluation()
        {
            for (int i = 0; i < targets.Count; i++) QueueTargetEvaluation(targets[i]);
        }

        private void QueueTargetEvaluation(IWorldColliderCullingTarget target)
        {
            if (target.ColliderCullingPendingIndex >= 0) return;
            target.ColliderCullingPendingIndex = pendingTargets.Count;
            pendingTargets.Add(target);
        }

        private void RemovePendingTarget(IWorldColliderCullingTarget target)
        {
            int index = target.ColliderCullingPendingIndex;
            if (index < 0
                || index >= pendingTargets.Count
                || !ReferenceEquals(pendingTargets[index], target))
            {
                return;
            }

            int lastIndex = pendingTargets.Count - 1;
            IWorldColliderCullingTarget moved = pendingTargets[lastIndex];
            pendingTargets[index] = moved;
            moved.ColliderCullingPendingIndex = index;
            pendingTargets.RemoveAt(lastIndex);
            target.ColliderCullingPendingIndex = -1;
        }

        private void AddActiveTarget(IWorldColliderCullingTarget target)
        {
            if (target.ColliderCullingActiveIndex >= 0) return;
            target.ColliderCullingActiveIndex = activeTargets.Count;
            activeTargets.Add(target);
        }

        private void RemoveActiveTarget(IWorldColliderCullingTarget target)
        {
            int index = target.ColliderCullingActiveIndex;
            if (index < 0
                || index >= activeTargets.Count
                || !ReferenceEquals(activeTargets[index], target))
            {
                return;
            }

            int lastIndex = activeTargets.Count - 1;
            IWorldColliderCullingTarget moved = activeTargets[lastIndex];
            activeTargets[index] = moved;
            moved.ColliderCullingActiveIndex = index;
            activeTargets.RemoveAt(lastIndex);
            target.ColliderCullingActiveIndex = -1;
        }

        private void RestoreAll()
        {
            pendingTargets.Clear();
            activeTargets.Clear();
            managedColliderCount = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                IWorldColliderCullingTarget target = targets[i];
                target.ColliderCullingPendingIndex = -1;
                target.ColliderCullingActiveIndex = -1;
                target.ApplyColliderCulling(false);
                target.ColliderCullingAccountedCount = target.ColliderCullingManagedCount;
                managedColliderCount += target.ColliderCullingAccountedCount;
            }
            culledTargetCount = 0;
            hasOrigin = false;
            cullingActive = false;
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                IWorldColliderCullingTarget target = targets[i];
                if (target == null) continue;
                target.ReleaseColliderCulling();
                ResetTargetIndices(target);
            }
            targets.Clear();
            targetsByCell.Clear();
            recycledCellLists.Clear();
            pendingTargets.Clear();
            activeTargets.Clear();
            culledTargetCount = managedColliderCount = lastCheckedTargetCount = 0;
            lastSpatialCandidateCount = spatialRefreshCount = initialSweepCount = 0;
            maximumTargetRadius = 0f;
            hasOrigin = false;
            cullingActive = false;
        }

        private void OnDisable()
        {
            if (!ProjectFApplicationLifecycle.IsQuitting) RestoreAll();
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            if (!ProjectFApplicationLifecycle.IsQuitting) ReleaseAll();
            instance = null;
        }

        private static Vector2Int ResolveCell(Vector3 position) => new Vector2Int(
            Mathf.FloorToInt(position.x / CellSize),
            Mathf.FloorToInt(position.z / CellSize));

        private static void ResetTargetIndices(IWorldColliderCullingTarget target)
        {
            target.ColliderCullingAccountedCount = 0;
            target.ColliderCullingRegistryIndex = -1;
            target.ColliderCullingCellIndex = -1;
            target.ColliderCullingPendingIndex = -1;
            target.ColliderCullingActiveIndex = -1;
        }
    }
}
