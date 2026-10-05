using ProjectF.Power;
using System.Collections;
using System.Collections.Generic;
using ProjectF.Benchmark;
using UnityEngine;

namespace ProjectF.MapObjects
{
    /// <summary>
    /// Synchronizes installation handles into one presentation host GameObject per item type.
    /// Live model submission belongs to InstallationBatchRenderer; data-only entities are rendered
    /// directly from their authoritative record without creating a per-entity GameObject.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(1001)]
    public sealed class StaticMapObjectBatchRenderer : MonoBehaviour
    {
        [SerializeField, Min(1f)]
        private float batchCellSize = 16f;

        private readonly List<VirtualObjectRecord> dataOnlyInstallations = new List<VirtualObjectRecord>(256);
        private readonly Dictionary<int, StaticMapObjectTypeHost> hostsByItemId =
            new Dictionary<int, StaticMapObjectTypeHost>();
        private readonly List<StaticMapObjectTypeHost> hostScratch =
            new List<StaticMapObjectTypeHost>(32);
        private readonly HashSet<int> rejectedTypeIds = new HashSet<int>();
        private readonly Dictionary<int, ItemDefinition> supportedDefinitionsByItemId = new Dictionary<int, ItemDefinition>();
        private readonly Dictionary<int, int> synchronizedTypeVersions = new Dictionary<int, int>();
        private readonly List<KeyValuePair<int, int>> typeVersions = new List<KeyValuePair<int, int>>(32);
        private readonly List<KeyValuePair<int, int>> changedTypeVersions = new List<KeyValuePair<int, int>>(32);

        private VirtualObjectWorld virtualWorld;
        private ItemManager itemManager;
        private Camera mainCamera;
        private int cachedDataOnlyInstallationVersion = -1;
        private int synchronizationCount;
        private int lastSynchronizationFrame = -1;
        private int lastSynchronizedDataOnlyInstallationCount;
        internal long BenchmarkSyncDone { get; private set; }
        internal long BenchmarkSyncTotal { get; private set; }

        internal IEnumerator PrepareBenchmarkPresentation()
        {
            ResolveDependencies();
            if (virtualWorld == null || itemManager == null) yield break;
            int revision = virtualWorld.DataOnlyInstallationVersion;
            CollectChangedTypes();
            var work = SynchronizeHostsCore(true);
            bool completed = false;
            try
            {
                while (work.MoveNext()) yield return null;
                completed = true;
                cachedDataOnlyInstallationVersion = revision;
            }
            finally
            {
                (work as System.IDisposable)?.Dispose();
                if (!completed)
                {
                    foreach (var host in hostsByItemId.Values) if (host != null) host.AbortSynchronization();
                    InvalidateSyncVersions();
                }
            }
        }

        public int ActiveTypeCount => hostsByItemId.Count;
        public int UnsupportedActiveTypeCount => rejectedTypeIds.Count;

        public int ActiveInstanceCount
        {
            get
            {
                int count = 0;
                foreach (StaticMapObjectTypeHost host in hostsByItemId.Values)
                {
                    if (host != null)
                    {
                        count += host.InstanceCount;
                    }
                }

                return count;
            }
        }

        public int ActiveBatchCount => SumHostValue(HostValue.BatchCount);
        public int ActiveMatrixCount => SumHostValue(HostValue.MatrixCount);
        public int EstimatedDrawCallCount => SumHostValue(HostValue.DrawCallCount);
        public int LastVisibleBatchCount => SumHostValue(HostValue.VisibleBatchCount);
        public int LastCulledBatchCount => SumHostValue(HostValue.CulledBatchCount);
        public int LastCandidateBatchCount => SumHostValue(HostValue.CandidateBatchCount);
        public int LastCandidateCellCount => SumHostValue(HostValue.CandidateCellCount);
        public int LastLegacySubmittedMatrixCount => SumHostValue(HostValue.LegacySubmittedMatrixCount);
        public int LastLegacyDrawCallCount => SumHostValue(HostValue.LegacyDrawCallCount);
        public int LastBatchRendererGroupBatchCount => SumHostValue(HostValue.BatchRendererGroupBatchCount);
        public int LastBatchRendererGroupMatrixCount => SumHostValue(HostValue.BatchRendererGroupMatrixCount);
        public int SynchronizationCount => synchronizationCount;
        public int LastSynchronizationFrame => lastSynchronizationFrame;
        public int LastSynchronizedDataOnlyInstallationCount => lastSynchronizedDataOnlyInstallationCount;

        public void Configure(VirtualObjectWorld world, ItemManager manager)
        {
            ReleaseHosts();
            virtualWorld = world;
            itemManager = manager;
            InvalidateSyncVersions();
        }

        public void SynchronizeForWorldPresentation()
        {
            if (TerrainGenerator.Active != null && TerrainGenerator.Active.IsBenchmarkPlacementInProgress) return;
            ResolveDependencies();
            if (virtualWorld == null || itemManager == null)
            {
                return;
            }

            SynchronizeHostsIfNeeded();
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            InvalidateSyncVersions();
        }

        private void OnDisable()
        {
            if (!ProjectFApplicationLifecycle.IsQuitting) SuspendHosts();
        }

        private void OnDestroy()
        {
            if (!ProjectFApplicationLifecycle.IsQuitting) ReleaseHosts();
        }

        private void LateUpdate()
        {
            using var callerSample = MapObjectTickProfiler.SampleLateUpdateCaller<StaticMapObjectBatchRenderer>();
            using var sample = MapObjectTickProfiler.SampleNamed(
                "Render",
                "Static Installation Render",
                "Static Installation Render (inclusive)");
            // Presentation revisions change repeatedly while saved chunks are restored. Preserve
            // the stale cached versions so the first ready frame performs one complete sync.
            if (MapObjectTickManager.WaitingForWorldLoad
                || TerrainGenerator.Active != null && TerrainGenerator.Active.IsBenchmarkPlacementInProgress)
            {
                SuspendHostRendering();
                return;
            }

            ResolveDependencies();
            if (virtualWorld == null || itemManager == null)
            {
                SuspendHosts();
                return;
            }

            SynchronizeHostsIfNeeded();

            using (MapObjectTickProfiler.SampleNamed(
                       "Render Detail",
                       "Static Installation Render",
                       "Static Installation Submit"))
            {
                RenderHosts();
            }
        }

        private void ResolveDependencies()
        {
            if (virtualWorld == null)
            {
                virtualWorld = VirtualObjectWorld.Current;
            }

            if (itemManager == null && GameManager.Instance != null)
            {
                itemManager = GameManager.Instance.ItemManger;
            }

            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }
        }

        private void SynchronizeHostsIfNeeded()
        {
            if (cachedDataOnlyInstallationVersion == virtualWorld.DataOnlyInstallationVersion)
            {
                return;
            }
            int revision = virtualWorld.DataOnlyInstallationVersion;
            CollectChangedTypes();
            if (changedTypeVersions.Count == 0)
            {
                cachedDataOnlyInstallationVersion = revision;
                return;
            }

            using (MapObjectTickProfiler.SampleNamed(
                       "Render Detail",
                       "Static Installation Render",
                       "Static Installation Synchronize"))
            {
                SynchronizeHosts();
            }
            cachedDataOnlyInstallationVersion = revision;
        }

        private void CollectChangedTypes()
        {
            changedTypeVersions.Clear();
            virtualWorld.CopyDataOnlyInstallationTypeVersions(typeVersions);
            for (int i = 0; i < typeVersions.Count; i++)
            {
                KeyValuePair<int, int> type = typeVersions[i];
                if (synchronizedTypeVersions.TryGetValue(type.Key, out int synchronizedVersion)
                    && synchronizedVersion == type.Value) continue;
                if (TryGetSupportedArchetype(type.Key, out _)) changedTypeVersions.Add(type);
                else
                {
                    if (virtualWorld.GetDataOnlyInstallationCount(type.Key) == 0) rejectedTypeIds.Remove(type.Key);
                    else rejectedTypeIds.Add(type.Key);
                    synchronizedTypeVersions[type.Key] = type.Value;
                }
            }
        }

        private void SynchronizeHosts()
        {
            var work = SynchronizeHostsCore(false);
            using (work as System.IDisposable) { while (work.MoveNext()) { } }
        }

        private IEnumerator SynchronizeHostsCore(bool spreadAcrossFrames)
        {
            BenchmarkSyncDone = 0;
            BenchmarkSyncTotal = 0;
            for (int i = 0; i < changedTypeVersions.Count; i++)
            {
                BenchmarkSyncTotal += virtualWorld.GetDataOnlyInstallationCount(changedTypeVersions[i].Key);
            }
            if (changedTypeVersions.Count == 0) yield break;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            synchronizationCount++;
            lastSynchronizationFrame = Time.frameCount;
            lastSynchronizedDataOnlyInstallationCount = 0;
            // Synchronize only changed supported types; unchanged hosts retain their matrices.
            for (int typeIndex = 0; typeIndex < changedTypeVersions.Count; typeIndex++)
            {
                KeyValuePair<int, int> type = changedTypeVersions[typeIndex];
                virtualWorld.CopyDataOnlyInstallationRecords(type.Key, dataOnlyInstallations);
                lastSynchronizedDataOnlyInstallationCount += dataOnlyInstallations.Count;
                if (dataOnlyInstallations.Count == 0 && !hostsByItemId.ContainsKey(type.Key))
                {
                    rejectedTypeIds.Remove(type.Key);
                    synchronizedTypeVersions[type.Key] = type.Value;
                    continue;
                }
                if (!TryGetOrCreateHost(type.Key, out StaticMapObjectTypeHost host))
                {
                    synchronizedTypeVersions[type.Key] = type.Value;
                    continue;
                }
                host.BeginSynchronization();
                rejectedTypeIds.Remove(type.Key);
                for (int i = 0; i < dataOnlyInstallations.Count; i++)
                {
                    BenchmarkSyncDone++;
                    if (spreadAcrossFrames && BenchmarkLayout.IsWorkSliceExpired(started,
                        System.Diagnostics.Stopwatch.GetTimestamp(), System.Diagnostics.Stopwatch.Frequency))
                    { yield return null; started = System.Diagnostics.Stopwatch.GetTimestamp(); }
                    VirtualObjectRecord record = dataOnlyInstallations[i];
                    if (record == null
                        || record.HasAttachedView
                        || record.installationState != null && UtilityPoleWorld.Current != null
                            && UtilityPoleWorld.Current.TryGet(BlockStateStore.GetInstallationStorageKey(record.installationState), out _)
                        || record.installationState != null && ProductionWorld.Current != null
                            && ProductionWorld.Current.TryGet(BlockStateStore.GetInstallationStorageKey(record.installationState), out _)
                        || record.installationState != null && MiningWorld.Current != null
                            && MiningWorld.Current.TryGet(BlockStateStore.GetInstallationStorageKey(record.installationState), out _)) continue;
                    if (!host.SynchronizeRecord(record))
                    {
                        host.AbortSynchronization();
                        rejectedTypeIds.Add(type.Key);
                        BenchmarkSyncDone += dataOnlyInstallations.Count - i - 1;
                        break;
                    }
                }
                host.CompleteSynchronization();
                if (host.InstanceCount == 0) RemoveHost(type.Key);
                synchronizedTypeVersions[type.Key] = type.Value;
            }
        }

        private bool TryGetOrCreateHost(int itemId, out StaticMapObjectTypeHost host)
        {
            if (hostsByItemId.TryGetValue(itemId, out host))
            {
                if (host != null)
                {
                    return true;
                }

                hostsByItemId.Remove(itemId);
            }

            if (!TryGetSupportedArchetype(itemId, out ItemDefinition definition))
            {
                host = null;
                return false;
            }

            GameObject hostObject = new GameObject(BuildHostName(itemId, definition.itemName));
            hostObject.transform.SetParent(transform, false);
            host = hostObject.AddComponent<StaticMapObjectTypeHost>();
            host.Configure(itemId, definition.MapObjectArchetype, batchCellSize);
            hostsByItemId.Add(itemId, host);
            return true;
        }

        private bool TryGetSupportedArchetype(int itemId, out ItemDefinition definition)
        {
            if (supportedDefinitionsByItemId.TryGetValue(itemId, out definition)) return definition != null;
            definition = null;
            bool supported = itemManager != null
                             && itemManager.TryGetItemDefinitionById(itemId, out definition)
                             && definition != null
                             && StaticMapObjectTypeHost.IsSupportedArchetype(definition.MapObjectArchetype);
            if (!supported)
            {
                definition = null;
                rejectedTypeIds.Add(itemId);
            }
            supportedDefinitionsByItemId[itemId] = definition;
            return supported;
        }

        private void RenderHosts()
        {
            foreach (StaticMapObjectTypeHost host in hostsByItemId.Values)
            {
                if (host != null)
                {
                    host.Render(mainCamera);
                }
            }
        }

        private void SuspendHosts()
        {
            SuspendHostRendering();
            InvalidateSyncVersions();
        }

        private void SuspendHostRendering()
        {
            foreach (StaticMapObjectTypeHost host in hostsByItemId.Values)
            {
                if (host != null)
                {
                    host.Suspend();
                }
            }
        }

        private void ReleaseHosts()
        {
            CopyHostsToScratch();
            hostsByItemId.Clear();
            for (int i = 0; i < hostScratch.Count; i++)
            {
                StaticMapObjectTypeHost host = hostScratch[i];
                if (host == null)
                {
                    continue;
                }

                host.Release();
                DestroyRuntimeObject(host.gameObject);
            }

            hostScratch.Clear();
            rejectedTypeIds.Clear();
            supportedDefinitionsByItemId.Clear();
            synchronizedTypeVersions.Clear();
            typeVersions.Clear();
            changedTypeVersions.Clear();
            dataOnlyInstallations.Clear();
        }

        private void RemoveHost(int itemId)
        {
            if (!hostsByItemId.TryGetValue(itemId, out StaticMapObjectTypeHost host))
            {
                return;
            }

            hostsByItemId.Remove(itemId);
            if (host != null)
            {
                host.Release();
                DestroyRuntimeObject(host.gameObject);
            }
        }

        private void CopyHostsToScratch()
        {
            hostScratch.Clear();
            foreach (StaticMapObjectTypeHost host in hostsByItemId.Values)
            {
                if (host != null)
                {
                    hostScratch.Add(host);
                }
            }
        }

        private int SumHostValue(HostValue value)
        {
            int total = 0;
            foreach (StaticMapObjectTypeHost host in hostsByItemId.Values)
            {
                if (host == null)
                {
                    continue;
                }

                switch (value)
                {
                    case HostValue.BatchCount:
                        total += host.ActiveBatchCount;
                        break;
                    case HostValue.MatrixCount:
                        total += host.ActiveMatrixCount;
                        break;
                    case HostValue.DrawCallCount:
                        total += host.EstimatedDrawCallCount;
                        break;
                    case HostValue.VisibleBatchCount:
                        total += host.LastVisibleBatchCount;
                        break;
                    case HostValue.CulledBatchCount:
                        total += host.LastCulledBatchCount;
                        break;
                    case HostValue.CandidateBatchCount:
                        total += host.LastCandidateBatchCount;
                        break;
                    case HostValue.CandidateCellCount:
                        total += host.LastCandidateCellCount;
                        break;
                    case HostValue.LegacySubmittedMatrixCount:
                        total += host.LastLegacySubmittedMatrixCount;
                        break;
                    case HostValue.LegacyDrawCallCount:
                        total += host.LastLegacyDrawCallCount;
                        break;
                    case HostValue.BatchRendererGroupBatchCount:
                        total += host.LastBatchRendererGroupBatchCount;
                        break;
                    case HostValue.BatchRendererGroupMatrixCount:
                        total += host.LastBatchRendererGroupMatrixCount;
                        break;
                }
            }

            return total;
        }

        private void InvalidateSyncVersions()
        {
            cachedDataOnlyInstallationVersion = -1;
            synchronizedTypeVersions.Clear();
            supportedDefinitionsByItemId.Clear();
            rejectedTypeIds.Clear();
        }

        private static string BuildHostName(int itemId, string itemName)
        {
            return string.IsNullOrEmpty(itemName)
                ? $"MapObject Type {itemId}"
                : $"MapObject Type {itemId} - {itemName}";
        }

        private static void DestroyRuntimeObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            target.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private enum HostValue : byte
        {
            BatchCount,
            MatrixCount,
            DrawCallCount,
            VisibleBatchCount,
            CulledBatchCount,
            CandidateBatchCount,
            CandidateCellCount,
            LegacySubmittedMatrixCount,
            LegacyDrawCallCount,
            BatchRendererGroupBatchCount,
            BatchRendererGroupMatrixCount
        }
    }
}
