using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.MapObjects
{
    /// <summary>
    /// Synchronizes active installation handles into one runtime host GameObject per item type.
    /// The hosts own visual instance slots and rendering; source objects remain as temporary
    /// gameplay/collider shells until their type is migrated to data-only simulation.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(1001)]
    public sealed class StaticMapObjectBatchRenderer : MonoBehaviour
    {
        [SerializeField, Min(1f)]
        private float batchCellSize = 16f;

        private readonly List<InstallationObject> activeInstallations = new List<InstallationObject>(256);
        private readonly Dictionary<int, StaticMapObjectTypeHost> hostsByItemId =
            new Dictionary<int, StaticMapObjectTypeHost>();
        private readonly List<StaticMapObjectTypeHost> hostScratch =
            new List<StaticMapObjectTypeHost>(32);
        private readonly List<int> emptyHostItemIds = new List<int>(16);
        private readonly HashSet<int> rejectedTypeIds = new HashSet<int>();

        private VirtualObjectWorld virtualWorld;
        private ItemManager itemManager;
        private Camera mainCamera;
        private int cachedInstallationVersion = -1;
        private int cachedActiveInstanceVersion = -1;

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

        public void Configure(VirtualObjectWorld world, ItemManager manager)
        {
            ReleaseHosts();
            virtualWorld = world;
            itemManager = manager;
            InvalidateSyncVersions();
        }

        public bool TryGetTypeHost(int itemId, out StaticMapObjectTypeHost host)
        {
            return hostsByItemId.TryGetValue(itemId, out host) && host != null;
        }

        public bool SupportsItemType(int itemId)
        {
            return TryGetSupportedArchetype(itemId, out _);
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
            SuspendHosts();
        }

        private void OnDestroy()
        {
            ReleaseHosts();
        }

        private void LateUpdate()
        {
            ResolveDependencies();
            if (virtualWorld == null || itemManager == null)
            {
                SuspendHosts();
                return;
            }

            int installationVersion = virtualWorld.InstallationVersion;
            int activeVersion = InstallationObject.ActiveInstanceVersion;
            if (cachedInstallationVersion != installationVersion
                || cachedActiveInstanceVersion != activeVersion)
            {
                SynchronizeHosts();
                cachedInstallationVersion = installationVersion;
                cachedActiveInstanceVersion = activeVersion;
            }

            RenderHosts();
        }

        private void ResolveDependencies()
        {
            if (virtualWorld == null)
            {
                virtualWorld = GetComponent<VirtualObjectWorld>();
                if (virtualWorld == null)
                {
                    virtualWorld = VirtualObjectWorld.Current;
                }
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

        private void SynchronizeHosts()
        {
            CopyHostsToScratch();
            for (int i = 0; i < hostScratch.Count; i++)
            {
                hostScratch[i].BeginSynchronization();
            }

            InstallationObject.CopyActiveInstances(activeInstallations);
            rejectedTypeIds.Clear();
            for (int i = 0; i < activeInstallations.Count; i++)
            {
                InstallationObject installationObject = activeInstallations[i];
                MapObjectHandle handle = installationObject != null
                    ? installationObject.RuntimeMapObjectHandle
                    : default;
                if (!handle.IsValid
                    || !installationObject.isActiveAndEnabled
                    || !virtualWorld.IsHandleAlive(handle)
                    || rejectedTypeIds.Contains(handle.TypeId))
                {
                    continue;
                }

                if (!TryGetOrCreateHost(handle.TypeId, out StaticMapObjectTypeHost host))
                {
                    rejectedTypeIds.Add(handle.TypeId);
                    continue;
                }

                if (!host.SynchronizeInstance(installationObject, handle))
                {
                    host.AbortSynchronization();
                    rejectedTypeIds.Add(handle.TypeId);
                }
            }

            CopyHostsToScratch();
            emptyHostItemIds.Clear();
            for (int i = 0; i < hostScratch.Count; i++)
            {
                StaticMapObjectTypeHost host = hostScratch[i];
                host.CompleteSynchronization();
                if (host.InstanceCount == 0)
                {
                    emptyHostItemIds.Add(host.ItemId);
                }
            }

            for (int i = 0; i < emptyHostItemIds.Count; i++)
            {
                RemoveHost(emptyHostItemIds[i]);
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
            host.BeginSynchronization();
            hostsByItemId.Add(itemId, host);
            return true;
        }

        private bool TryGetSupportedArchetype(int itemId, out ItemDefinition definition)
        {
            definition = null;
            return itemManager != null
                   && itemManager.TryGetItemDefinitionById(itemId, out definition)
                   && definition != null
                   && StaticMapObjectTypeHost.IsSupportedArchetype(definition.MapObjectArchetype);
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
            foreach (StaticMapObjectTypeHost host in hostsByItemId.Values)
            {
                if (host != null)
                {
                    host.Suspend();
                }
            }

            InvalidateSyncVersions();
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
            emptyHostItemIds.Clear();
            rejectedTypeIds.Clear();
            activeInstallations.Clear();
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
                }
            }

            return total;
        }

        private void InvalidateSyncVersions()
        {
            cachedInstallationVersion = -1;
            cachedActiveInstanceVersion = -1;
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
            DrawCallCount
        }
    }
}
