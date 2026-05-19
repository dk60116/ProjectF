using System;
using System.Collections.Generic;
using UnityEngine;

public enum VirtualObjectKind : byte
{
    None = 0,
    ItemStack = 1,
    Resource = 2,
    Installation = 3,
    MapObject = 4,
    ConveyorItem = 5
}

public enum VirtualObjectResidency : byte
{
    Virtual = 0,
    Live = 1,
    Hybrid = 2
}

[Serializable]
public readonly struct VirtualObjectId : IEquatable<VirtualObjectId>
{
    public VirtualObjectId(int value)
    {
        this.value = value;
    }

    [SerializeField]
    private readonly int value;

    public int Value => value;
    public bool IsValid => value > 0;

    public bool Equals(VirtualObjectId other)
    {
        return value == other.value;
    }

    public override bool Equals(object obj)
    {
        return obj is VirtualObjectId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return value;
    }

    public override string ToString()
    {
        return value.ToString();
    }
}

public sealed class VirtualItemStackState
{
    private readonly int[] rawItems;
    private readonly IntRun[] compressedRuns;
    private readonly int itemCount;

    private readonly struct IntRun
    {
        public readonly int value;
        public readonly int count;

        public IntRun(int value, int count)
        {
            this.value = value;
            this.count = count;
        }
    }

    private VirtualItemStackState(int[] rawItems, IntRun[] compressedRuns, int itemCount)
    {
        this.rawItems = rawItems;
        this.compressedRuns = compressedRuns;
        this.itemCount = itemCount;
    }

    public int Count => itemCount;
    public bool IsEmpty => itemCount <= 0;

    public static VirtualItemStackState FromItems(IReadOnlyList<int> itemIds)
    {
        if (itemIds == null || itemIds.Count <= 0)
        {
            return null;
        }

        int count = itemIds.Count;
        int runCount = 1;
        int previousValue = itemIds[0];
        for (int i = 1; i < count; i++)
        {
            int value = itemIds[i];
            if (value == previousValue)
            {
                continue;
            }

            runCount++;
            previousValue = value;
        }

        if (runCount * 2 >= count)
        {
            int[] rawCopy = new int[count];
            for (int i = 0; i < count; i++)
            {
                rawCopy[i] = itemIds[i];
            }

            return new VirtualItemStackState(rawCopy, null, count);
        }

        IntRun[] runs = new IntRun[runCount];
        int runIndex = 0;
        int currentValue = itemIds[0];
        int currentCount = 1;
        for (int i = 1; i < count; i++)
        {
            int value = itemIds[i];
            if (value == currentValue)
            {
                currentCount++;
                continue;
            }

            runs[runIndex++] = new IntRun(currentValue, currentCount);
            currentValue = value;
            currentCount = 1;
        }

        runs[runIndex] = new IntRun(currentValue, currentCount);
        return new VirtualItemStackState(null, runs, count);
    }

    public List<int> ToList()
    {
        List<int> itemIds = new List<int>(itemCount);
        CopyTo(itemIds);
        return itemIds;
    }

    public void CopyTo(List<int> itemIds)
    {
        if (itemIds == null)
        {
            return;
        }

        if (rawItems != null)
        {
            itemIds.AddRange(rawItems);
            return;
        }

        if (compressedRuns == null)
        {
            return;
        }

        for (int runIndex = 0; runIndex < compressedRuns.Length; runIndex++)
        {
            IntRun run = compressedRuns[runIndex];
            for (int i = 0; i < run.count; i++)
            {
                itemIds.Add(run.value);
            }
        }
    }
}

public sealed class VirtualObjectRecord
{
    public VirtualObjectId id;
    public VirtualObjectKind kind;
    public VirtualObjectResidency residency;
    public int itemId = -1;
    public int count;
    public Vector2Int anchorCoordinate;
    public Vector3 worldPosition;
    public Quaternion worldRotation = Quaternion.identity;
    public int quarterTurns;
    public long sequence;
    public int resourceCount;
    public int maxGauge;
    public int currentGauge;
    public int initialResourceCount;
    public int liveInstanceId;
    public readonly List<Vector2Int> occupiedCoordinates = new List<Vector2Int>();
    public VirtualItemStackState itemStack;
    public Resource.ResourceSaveState resourceState;
    public BlockStateStore.InstallationSaveState installationState;

    public bool HasLiveObject => liveInstanceId != 0;

    public VirtualObjectRecord Clone()
    {
        VirtualObjectRecord clone = new VirtualObjectRecord
        {
            id = id,
            kind = kind,
            residency = residency,
            itemId = itemId,
            count = count,
            anchorCoordinate = anchorCoordinate,
            worldPosition = worldPosition,
            worldRotation = worldRotation,
            quarterTurns = quarterTurns,
            sequence = sequence,
            resourceCount = resourceCount,
            maxGauge = maxGauge,
            currentGauge = currentGauge,
            initialResourceCount = initialResourceCount,
            liveInstanceId = liveInstanceId,
            itemStack = itemStack,
            resourceState = resourceState,
            installationState = installationState != null ? installationState.Clone() : null
        };

        clone.occupiedCoordinates.AddRange(occupiedCoordinates);
        return clone;
    }
}

[DisallowMultipleComponent]
public sealed class VirtualObjectWorld : MonoBehaviour
{
    private static VirtualObjectWorld current;

    private readonly Dictionary<int, VirtualObjectRecord> recordsById = new Dictionary<int, VirtualObjectRecord>();
    private readonly Dictionary<Vector2Int, List<int>> recordIdsByCoordinate = new Dictionary<Vector2Int, List<int>>();
    private readonly Dictionary<Vector2Int, int> floorStackRecordByCoordinate = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, int> resourceRecordByCoordinate = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, int> installationRecordByAnchor = new Dictionary<Vector2Int, int>();
    private int nextId = 1;
    private int version;

    public static VirtualObjectWorld Current
    {
        get
        {
            if (current != null)
            {
                return current;
            }

            current = FindObjectOfType<VirtualObjectWorld>();
            return current;
        }
    }

    public int Count => recordsById.Count;
    public int Version => version;

    public static VirtualObjectWorld EnsureFor(GameObject host)
    {
        if (current != null)
        {
            return current;
        }

        if (host == null)
        {
            GameObject worldObject = new GameObject("VirtualObjectWorld");
            current = worldObject.AddComponent<VirtualObjectWorld>();
            return current;
        }

        VirtualObjectWorld world = host.GetComponent<VirtualObjectWorld>();
        if (world == null)
        {
            world = host.AddComponent<VirtualObjectWorld>();
        }

        current = world;
        return current;
    }

    public bool TryGetRecord(VirtualObjectId id, out VirtualObjectRecord record)
    {
        if (!id.IsValid || !recordsById.TryGetValue(id.Value, out VirtualObjectRecord storedRecord))
        {
            record = null;
            return false;
        }

        record = storedRecord.Clone();
        return true;
    }

    public bool TryGetFloorItemStack(Vector2Int coordinate, out List<int> itemIds)
    {
        if (floorStackRecordByCoordinate.TryGetValue(coordinate, out int recordId)
            && recordsById.TryGetValue(recordId, out VirtualObjectRecord record)
            && record?.itemStack != null)
        {
            itemIds = record.itemStack.ToList();
            return true;
        }

        itemIds = null;
        return false;
    }

    public bool TryGetResourceState(Vector2Int coordinate, out Resource.ResourceSaveState state)
    {
        if (resourceRecordByCoordinate.TryGetValue(coordinate, out int recordId)
            && recordsById.TryGetValue(recordId, out VirtualObjectRecord record)
            && record != null)
        {
            state = record.resourceState;
            return true;
        }

        state = default;
        return false;
    }

    public bool TryGetInstallationState(Vector2Int anchorCoordinate, out BlockStateStore.InstallationSaveState state)
    {
        if (installationRecordByAnchor.TryGetValue(anchorCoordinate, out int recordId)
            && recordsById.TryGetValue(recordId, out VirtualObjectRecord record)
            && record?.installationState != null)
        {
            state = record.installationState.Clone();
            return true;
        }

        state = null;
        return false;
    }

    public List<VirtualObjectRecord> GetRecordsAtCoordinate(Vector2Int coordinate)
    {
        List<VirtualObjectRecord> records = new List<VirtualObjectRecord>();
        if (!recordIdsByCoordinate.TryGetValue(coordinate, out List<int> recordIds))
        {
            return records;
        }

        for (int i = 0; i < recordIds.Count; i++)
        {
            if (recordsById.TryGetValue(recordIds[i], out VirtualObjectRecord record) && record != null)
            {
                records.Add(record.Clone());
            }
        }

        return records;
    }

    public void CopyRecords(List<VirtualObjectRecord> results, bool includeLiveRecords = false)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        foreach (KeyValuePair<int, VirtualObjectRecord> pair in recordsById)
        {
            VirtualObjectRecord record = pair.Value;
            if (record == null)
            {
                continue;
            }

            if (!includeLiveRecords && record.residency == VirtualObjectResidency.Live)
            {
                continue;
            }

            results.Add(record);
        }
    }

    public VirtualObjectId UpsertFloorItemStack(
        Vector2Int coordinate,
        IReadOnlyList<int> itemIds,
        VirtualObjectResidency residency = VirtualObjectResidency.Virtual)
    {
        if (itemIds == null || itemIds.Count <= 0)
        {
            RemoveFloorItemStack(coordinate);
            return default;
        }

        VirtualObjectRecord record = GetOrCreateIndexedRecord(
            floorStackRecordByCoordinate,
            coordinate,
            VirtualObjectKind.ItemStack);

        record.residency = residency;
        record.anchorCoordinate = coordinate;
        record.worldPosition = new Vector3(coordinate.x, 0f, coordinate.y);
        record.worldRotation = Quaternion.identity;
        record.quarterTurns = 0;
        record.itemId = itemIds[0];
        record.count = itemIds.Count;
        record.itemStack = VirtualItemStackState.FromItems(itemIds);
        record.liveInstanceId = 0;
        ReplaceOccupiedCoordinates(record, coordinate);
        StoreRecord(record);
        return record.id;
    }

    public VirtualObjectId UpsertResource(
        Vector2Int coordinate,
        int itemId,
        Resource.ResourceSaveState state,
        Resource liveResource = null,
        VirtualObjectResidency residency = VirtualObjectResidency.Virtual)
    {
        VirtualObjectRecord record = GetOrCreateIndexedRecord(
            resourceRecordByCoordinate,
            coordinate,
            VirtualObjectKind.Resource);

        record.residency = liveResource != null ? residency : VirtualObjectResidency.Virtual;
        record.anchorCoordinate = coordinate;
        record.worldPosition = liveResource != null ? liveResource.transform.position : new Vector3(coordinate.x, 0f, coordinate.y);
        record.worldRotation = liveResource != null ? liveResource.transform.rotation : Quaternion.identity;
        record.itemId = itemId;
        record.count = Mathf.Max(0, state.resourceCount);
        record.resourceCount = Mathf.Max(0, state.resourceCount);
        record.maxGauge = Mathf.Max(1, state.maxGauge);
        record.currentGauge = Mathf.Max(0, state.currentGauge);
        record.initialResourceCount = Mathf.Max(1, state.initialResourceCount);
        record.resourceState = state;
        record.liveInstanceId = liveResource != null && residency != VirtualObjectResidency.Virtual
            ? liveResource.GetInstanceID()
            : 0;
        ReplaceOccupiedCoordinates(record, coordinate);
        StoreRecord(record);
        return record.id;
    }

    public VirtualObjectId UpsertInstallation(
        BlockStateStore.InstallationSaveState state,
        VirtualObjectResidency residency = VirtualObjectResidency.Virtual,
        InstallationObject liveInstallation = null)
    {
        if (state == null)
        {
            return default;
        }

        Vector2Int storageKey = BlockStateStore.GetInstallationStorageKey(state);
        VirtualObjectRecord record = GetOrCreateIndexedRecord(
            installationRecordByAnchor,
            storageKey,
            VirtualObjectKind.Installation);

        record.residency = liveInstallation != null ? VirtualObjectResidency.Live : residency;
        record.anchorCoordinate = state.anchorCoordinate;
        record.worldPosition = liveInstallation != null
            ? liveInstallation.transform.position
            : new Vector3(state.anchorCoordinate.x, 0f, state.anchorCoordinate.y);
        record.worldRotation = liveInstallation != null
            ? liveInstallation.transform.rotation
            : Quaternion.Euler(0f, state.quarterTurns * 90f, 0f);
        record.quarterTurns = ((state.quarterTurns % 4) + 4) % 4;
        record.itemId = state.itemId;
        record.count = 1;
        record.sequence = state.placementSequence;
        record.installationState = state.Clone();
        record.liveInstanceId = liveInstallation != null ? liveInstallation.GetInstanceID() : 0;
        ReplaceOccupiedCoordinates(record, state.occupiedCoordinates);
        StoreRecord(record);
        return record.id;
    }

    public void RemoveFloorItemStack(Vector2Int coordinate)
    {
        RemoveIndexedRecord(floorStackRecordByCoordinate, coordinate);
    }

    public void RemoveResource(Vector2Int coordinate)
    {
        RemoveIndexedRecord(resourceRecordByCoordinate, coordinate);
    }

    public void RemoveInstallation(Vector2Int anchorCoordinate)
    {
        RemoveIndexedRecord(installationRecordByAnchor, anchorCoordinate);
    }

    public void Clear()
    {
        recordsById.Clear();
        recordIdsByCoordinate.Clear();
        floorStackRecordByCoordinate.Clear();
        resourceRecordByCoordinate.Clear();
        installationRecordByAnchor.Clear();
        nextId = 1;
        version++;
    }

    private void Awake()
    {
        if (current != null && current != this)
        {
            return;
        }

        current = this;
    }

    private VirtualObjectRecord GetOrCreateIndexedRecord(
        Dictionary<Vector2Int, int> index,
        Vector2Int key,
        VirtualObjectKind kind)
    {
        if (index.TryGetValue(key, out int recordId)
            && recordsById.TryGetValue(recordId, out VirtualObjectRecord existingRecord)
            && existingRecord != null)
        {
            existingRecord.kind = kind;
            return existingRecord;
        }

        VirtualObjectRecord record = new VirtualObjectRecord
        {
            id = new VirtualObjectId(nextId++),
            kind = kind
        };

        index[key] = record.id.Value;
        recordsById[record.id.Value] = record;
        return record;
    }

    private void StoreRecord(VirtualObjectRecord record)
    {
        if (record == null || !record.id.IsValid)
        {
            return;
        }

        RemoveCoordinateMappings(record.id.Value);
        recordsById[record.id.Value] = record;
        RegisterCoordinateMappings(record);
        version++;
    }

    private void ReplaceOccupiedCoordinates(VirtualObjectRecord record, Vector2Int coordinate)
    {
        record.occupiedCoordinates.Clear();
        record.occupiedCoordinates.Add(coordinate);
    }

    private void ReplaceOccupiedCoordinates(VirtualObjectRecord record, IReadOnlyList<Vector2Int> coordinates)
    {
        record.occupiedCoordinates.Clear();
        if (coordinates == null || coordinates.Count <= 0)
        {
            record.occupiedCoordinates.Add(record.anchorCoordinate);
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            record.occupiedCoordinates.Add(coordinates[i]);
        }
    }

    private void RegisterCoordinateMappings(VirtualObjectRecord record)
    {
        for (int i = 0; i < record.occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = record.occupiedCoordinates[i];
            if (!recordIdsByCoordinate.TryGetValue(coordinate, out List<int> recordIds))
            {
                recordIds = new List<int>(2);
                recordIdsByCoordinate[coordinate] = recordIds;
            }

            if (!recordIds.Contains(record.id.Value))
            {
                recordIds.Add(record.id.Value);
            }
        }
    }

    private void RemoveCoordinateMappings(int recordId)
    {
        foreach (KeyValuePair<Vector2Int, List<int>> pair in recordIdsByCoordinate)
        {
            pair.Value.Remove(recordId);
        }
    }

    private void RemoveIndexedRecord(Dictionary<Vector2Int, int> index, Vector2Int key)
    {
        if (!index.TryGetValue(key, out int recordId))
        {
            return;
        }

        index.Remove(key);
        RemoveCoordinateMappings(recordId);
        recordsById.Remove(recordId);
        version++;
    }
}
