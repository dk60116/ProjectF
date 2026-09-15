using System;
using System.Collections.Generic;
using ProjectF.MapObjects;
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

    public static VirtualItemStackState FromOwnedRawItems(int[] itemIds)
    {
        return itemIds != null && itemIds.Length > 0
            ? new VirtualItemStackState(itemIds, null, itemIds.Length)
            : null;
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
    public MapObjectHandle mapObjectHandle;
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

    public bool HasAttachedView => liveInstanceId != 0;

    public VirtualObjectRecord Clone()
    {
        VirtualObjectRecord clone = new VirtualObjectRecord
        {
            id = id,
            mapObjectHandle = mapObjectHandle,
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

/// <summary>
/// Simulation-facing world-object identity and spatial index. This is an ordinary managed service: its lifetime is
/// owned by GameManager and never follows a presentation GameObject's enabled state.
/// </summary>
public sealed class VirtualObjectWorld : IDisposable
{
    private static VirtualObjectWorld current;
    private static uint nextGlobalMapObjectGeneration = 1;

    private readonly Dictionary<int, VirtualObjectRecord> recordsById = new Dictionary<int, VirtualObjectRecord>();
    private readonly Dictionary<Vector2Int, List<int>> recordIdsByCoordinate = new Dictionary<Vector2Int, List<int>>();
    private readonly Dictionary<Vector2Int, int> floorStackRecordByCoordinate = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, int> resourceRecordByCoordinate = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, int> installationRecordByAnchor = new Dictionary<Vector2Int, int>();
    private bool coordinateIndexBuildDeferred;
    private int nextId = 1;
    private int version;
    private int itemStackVersion;
    private int installationVersion;

    public static VirtualObjectWorld Current => current;

    public int Count => recordsById.Count;
    public int Version => version;
    public int ItemStackVersion => itemStackVersion;
    public int InstallationVersion => installationVersion;

    public static VirtualObjectWorld Ensure()
    {
        if (current != null)
        {
            return current;
        }

        current = new VirtualObjectWorld();
        return current;
    }

    public void BeginBulkLoad(int floorStackCount, int resourceCount, int installationCount)
    {
        if (coordinateIndexBuildDeferred)
        {
            throw new InvalidOperationException("A virtual-object bulk load is already active.");
        }

        floorStackCount = Math.Max(0, floorStackCount);
        resourceCount = Math.Max(0, resourceCount);
        installationCount = Math.Max(0, installationCount);
        int totalCount = (int)Math.Min(
            int.MaxValue,
            (long)floorStackCount + resourceCount + installationCount);
        recordsById.EnsureCapacity(Math.Max(recordsById.Count, totalCount));
        floorStackRecordByCoordinate.EnsureCapacity(floorStackCount);
        resourceRecordByCoordinate.EnsureCapacity(resourceCount);
        installationRecordByAnchor.EnsureCapacity(installationCount);
        recordIdsByCoordinate.Clear();
        recordIdsByCoordinate.EnsureCapacity(Math.Max(recordIdsByCoordinate.Count, totalCount));
        coordinateIndexBuildDeferred = true;
    }

    public void CompleteBulkLoad()
    {
        if (!coordinateIndexBuildDeferred)
        {
            return;
        }

        coordinateIndexBuildDeferred = false;
        recordIdsByCoordinate.Clear();
        foreach (KeyValuePair<int, VirtualObjectRecord> pair in recordsById)
        {
            RegisterCoordinateMappings(pair.Value);
        }
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

    public bool TryGetRecord(MapObjectHandle handle, out VirtualObjectRecord record)
    {
        if (!TryResolveRecord(handle, out VirtualObjectRecord storedRecord))
        {
            record = null;
            return false;
        }

        record = storedRecord.Clone();
        return true;
    }

    public bool IsHandleAlive(MapObjectHandle handle)
    {
        return TryResolveRecord(handle, out _);
    }

    public bool TryGetInstallationHandle(Vector2Int storageKey, out MapObjectHandle handle)
    {
        if (installationRecordByAnchor.TryGetValue(storageKey, out int recordId)
            && recordsById.TryGetValue(recordId, out VirtualObjectRecord record)
            && record != null
            && record.kind == VirtualObjectKind.Installation
            && record.mapObjectHandle.IsValid)
        {
            handle = record.mapObjectHandle;
            return true;
        }

        handle = default;
        return false;
    }

    public bool TryGetResourceHandle(Vector2Int coordinate, out MapObjectHandle handle)
    {
        if (resourceRecordByCoordinate.TryGetValue(coordinate, out int recordId)
            && recordsById.TryGetValue(recordId, out VirtualObjectRecord record)
            && record != null
            && record.kind == VirtualObjectKind.Resource
            && record.mapObjectHandle.IsValid)
        {
            handle = record.mapObjectHandle;
            return true;
        }

        handle = default;
        return false;
    }

    public void CopyMapObjectHandlesAtCoordinate(
        Vector2Int coordinate,
        List<MapObjectHandle> destination)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        if (!recordIdsByCoordinate.TryGetValue(coordinate, out List<int> recordIds))
        {
            return;
        }

        for (int i = 0; i < recordIds.Count; i++)
        {
            if (recordsById.TryGetValue(recordIds[i], out VirtualObjectRecord record)
                && record != null
                && record.mapObjectHandle.IsValid)
            {
                destination.Add(record.mapObjectHandle);
            }
        }
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

    public void CopyInstallationRecords(List<VirtualObjectRecord> results, bool includeLiveRecords = false)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        foreach (KeyValuePair<Vector2Int, int> pair in installationRecordByAnchor)
        {
            if (!recordsById.TryGetValue(pair.Value, out VirtualObjectRecord record)
                || record == null
                || record.kind != VirtualObjectKind.Installation
                || (!includeLiveRecords && record.residency == VirtualObjectResidency.Live))
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

        return UpsertFloorItemStackState(
            coordinate,
            itemIds[0],
            itemIds.Count,
            VirtualItemStackState.FromItems(itemIds),
            residency);
    }

    public VirtualObjectId UpsertFloorItemStackRaw(
        Vector2Int coordinate,
        int[] itemIds,
        VirtualObjectResidency residency = VirtualObjectResidency.Virtual)
    {
        if (itemIds == null || itemIds.Length <= 0)
        {
            RemoveFloorItemStack(coordinate);
            return default;
        }

        return UpsertFloorItemStackState(
            coordinate,
            itemIds[0],
            itemIds.Length,
            VirtualItemStackState.FromOwnedRawItems(itemIds),
            residency);
    }

    private VirtualObjectId UpsertFloorItemStackState(
        Vector2Int coordinate,
        int firstItemId,
        int itemCount,
        VirtualItemStackState itemStack,
        VirtualObjectResidency residency)
    {
        if (itemStack == null || itemCount <= 0)
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
        record.itemId = firstItemId;
        record.count = itemCount;
        record.itemStack = itemStack;
        record.liveInstanceId = 0;
        UpdateCoordinateMappings(record, coordinate);
        StoreRecord(record);

        return record.id;
    }

    public VirtualObjectId UpsertResource(
        Vector2Int coordinate,
        int itemId,
        Resource.ResourceSaveState state)
    {
        VirtualObjectRecord record = GetOrCreateIndexedRecord(
            resourceRecordByCoordinate,
            coordinate,
            VirtualObjectKind.Resource);

        record.residency = VirtualObjectResidency.Virtual;
        record.anchorCoordinate = coordinate;
        record.worldPosition = new Vector3(coordinate.x, 0f, coordinate.y);
        record.worldRotation = Quaternion.identity;
        record.itemId = itemId;
        record.count = Mathf.Max(0, state.resourceCount);
        record.resourceCount = Mathf.Max(0, state.resourceCount);
        record.maxGauge = Mathf.Max(1, state.maxGauge);
        record.currentGauge = Mathf.Max(0, state.currentGauge);
        record.initialResourceCount = Mathf.Max(1, state.initialResourceCount);
        record.resourceState = state;
        EnsureMapObjectHandle(record, itemId, record.id.Value);
        record.liveInstanceId = 0;
        UpdateCoordinateMappings(record, coordinate);
        StoreRecord(record);
        return record.id;
    }

    public VirtualObjectId UpsertInstallation(
        BlockStateStore.InstallationSaveState state,
        VirtualObjectResidency residency = VirtualObjectResidency.Virtual)
    {
        MapObjectHandle handle = UpsertInstallationHandle(state, residency);
        return handle.IsValid ? new VirtualObjectId(handle.Slot) : default;
    }

    public MapObjectHandle UpsertInstallationHandle(
        BlockStateStore.InstallationSaveState state,
        VirtualObjectResidency residency = VirtualObjectResidency.Virtual)
    {
        return UpsertInstallationRecord(state, residency, 0, default, default, false);
    }

    public MapObjectHandle AttachInstallationView(
        BlockStateStore.InstallationSaveState state,
        int viewInstanceId,
        Vector3 worldPosition,
        Quaternion worldRotation)
    {
        if (viewInstanceId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewInstanceId));
        }

        return UpsertInstallationRecord(
            state,
            VirtualObjectResidency.Live,
            viewInstanceId,
            worldPosition,
            worldRotation,
            true);
    }

    private MapObjectHandle UpsertInstallationRecord(
        BlockStateStore.InstallationSaveState state,
        VirtualObjectResidency residency,
        int viewInstanceId,
        Vector3 attachedWorldPosition,
        Quaternion attachedWorldRotation,
        bool hasAttachedPose)
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
        VirtualObjectResidency targetResidency = hasAttachedPose ? VirtualObjectResidency.Live : residency;
        Vector3 targetWorldPosition = hasAttachedPose
            ? attachedWorldPosition
            : state.hasWorldPose
                ? state.worldPosition
            : new Vector3(state.anchorCoordinate.x, 0f, state.anchorCoordinate.y);
        Quaternion targetWorldRotation = hasAttachedPose
            ? attachedWorldRotation
            : state.hasWorldPose
                ? state.worldRotation
            : Quaternion.Euler(0f, state.quarterTurns * 90f, 0f);
        int targetQuarterTurns = ((state.quarterTurns % 4) + 4) % 4;
        bool presentationChanged = !record.mapObjectHandle.IsValid
                                   || record.residency != targetResidency
                                   || record.anchorCoordinate != state.anchorCoordinate
                                   || !record.worldPosition.Equals(targetWorldPosition)
                                   || !record.worldRotation.Equals(targetWorldRotation)
                                   || record.quarterTurns != targetQuarterTurns
                                   || record.itemId != state.itemId
                                   || record.sequence != state.placementSequence
                                   || record.liveInstanceId != viewInstanceId
                                   || !CoordinatesMatch(record.occupiedCoordinates, state.occupiedCoordinates);

        record.residency = targetResidency;
        record.anchorCoordinate = state.anchorCoordinate;
        record.worldPosition = targetWorldPosition;
        record.worldRotation = targetWorldRotation;
        record.quarterTurns = targetQuarterTurns;
        record.itemId = state.itemId;
        record.count = 1;
        record.sequence = state.placementSequence;
        // BlockStateStore owns this state. Keep the same object internally so a View binding
        // cannot introduce a second runtime state owner; public reads still return clones.
        record.installationState = state;
        EnsureMapObjectHandle(record, state.itemId, state.placementSequence);
        record.liveInstanceId = viewInstanceId;
        UpdateCoordinateMappings(record, state.occupiedCoordinates);
        StoreRecord(record, presentationChanged);
        return record.mapObjectHandle;
    }

    public bool UpdateAttachedInstallationViewPose(
        Vector2Int storageKey,
        int viewInstanceId,
        Vector3 worldPosition,
        Quaternion worldRotation)
    {
        if (viewInstanceId == 0
            || !installationRecordByAnchor.TryGetValue(storageKey, out int recordId)
            || !recordsById.TryGetValue(recordId, out VirtualObjectRecord record)
            || record == null
            || record.liveInstanceId != viewInstanceId)
        {
            return false;
        }

        record.worldPosition = worldPosition;
        record.worldRotation = worldRotation;
        if (record.installationState != null)
        {
            record.installationState.hasWorldPose = true;
            record.installationState.worldPosition = worldPosition;
            record.installationState.worldRotation = worldRotation;
        }

        // 이 버전은 VirtualItemStackRenderer의 전체 캐시 갱신 기준이다.
        // 라이브 차량의 자세만 바뀐 경우에는 인덱스나 가상 아이템이
        // 달라지지 않으므로 버전을 올리지 않는다.
        return true;
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

    public bool RemoveInstallation(MapObjectHandle handle)
    {
        if (!TryResolveRecord(handle, out VirtualObjectRecord record)
            || record.kind != VirtualObjectKind.Installation
            || record.installationState == null)
        {
            return false;
        }

        Vector2Int storageKey = BlockStateStore.GetInstallationStorageKey(record.installationState);
        if (!installationRecordByAnchor.TryGetValue(storageKey, out int recordId)
            || recordId != handle.Slot)
        {
            return false;
        }

        RemoveIndexedRecord(installationRecordByAnchor, storageKey);
        return true;
    }

    public bool RemoveResource(MapObjectHandle handle)
    {
        if (!TryResolveRecord(handle, out VirtualObjectRecord record)
            || record.kind != VirtualObjectKind.Resource
            || !resourceRecordByCoordinate.TryGetValue(record.anchorCoordinate, out int recordId)
            || recordId != handle.Slot)
        {
            return false;
        }

        RemoveIndexedRecord(resourceRecordByCoordinate, record.anchorCoordinate);
        return true;
    }

    public void Clear()
    {
        coordinateIndexBuildDeferred = false;
        recordsById.Clear();
        recordIdsByCoordinate.Clear();
        floorStackRecordByCoordinate.Clear();
        resourceRecordByCoordinate.Clear();
        installationRecordByAnchor.Clear();
        nextId = 1;
        version++;
        itemStackVersion++;
        installationVersion++;
    }

    public void Dispose()
    {
        Clear();
        if (ReferenceEquals(current, this))
        {
            current = null;
        }
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

    private void StoreRecord(VirtualObjectRecord record, bool installationPresentationChanged = true)
    {
        if (record == null || !record.id.IsValid)
        {
            return;
        }

        recordsById[record.id.Value] = record;
        version++;
        if (record.kind == VirtualObjectKind.ItemStack)
        {
            itemStackVersion++;
        }
        else if (record.kind == VirtualObjectKind.Installation && installationPresentationChanged)
        {
            installationVersion++;
        }
    }

    private static bool CoordinatesMatch(
        IReadOnlyList<Vector2Int> current,
        IReadOnlyList<Vector2Int> next)
    {
        int currentCount = current != null ? current.Count : 0;
        int nextCount = next != null ? next.Count : 0;
        if (currentCount != nextCount)
        {
            return false;
        }

        for (int i = 0; i < currentCount; i++)
        {
            if (current[i] != next[i])
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateCoordinateMappings(VirtualObjectRecord record, Vector2Int coordinate)
    {
        if (record.occupiedCoordinates.Count == 1
            && record.occupiedCoordinates[0] == coordinate)
        {
            return;
        }

        if (!coordinateIndexBuildDeferred && record.occupiedCoordinates.Count > 0)
        {
            RemoveCoordinateMappings(record);
        }

        ReplaceOccupiedCoordinates(record, coordinate);
        if (!coordinateIndexBuildDeferred)
        {
            RegisterCoordinateMappings(record);
        }
    }

    private void UpdateCoordinateMappings(
        VirtualObjectRecord record,
        IReadOnlyList<Vector2Int> coordinates)
    {
        if (HasSameOccupiedCoordinates(record, coordinates))
        {
            return;
        }

        if (!coordinateIndexBuildDeferred && record.occupiedCoordinates.Count > 0)
        {
            RemoveCoordinateMappings(record);
        }

        ReplaceOccupiedCoordinates(record, coordinates);
        if (!coordinateIndexBuildDeferred)
        {
            RegisterCoordinateMappings(record);
        }
    }

    private static bool HasSameOccupiedCoordinates(
        VirtualObjectRecord record,
        IReadOnlyList<Vector2Int> coordinates)
    {
        int coordinateCount = coordinates != null && coordinates.Count > 0
            ? coordinates.Count
            : 1;
        if (record.occupiedCoordinates.Count != coordinateCount)
        {
            return false;
        }

        if (coordinates == null || coordinates.Count <= 0)
        {
            return record.occupiedCoordinates[0] == record.anchorCoordinate;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            if (record.occupiedCoordinates[i] != coordinates[i])
            {
                return false;
            }
        }

        return true;
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

        bool removesItemStack = ReferenceEquals(index, floorStackRecordByCoordinate);
        bool removesInstallation = ReferenceEquals(index, installationRecordByAnchor);
        index.Remove(key);
        if (recordsById.TryGetValue(recordId, out VirtualObjectRecord record) && record != null)
        {
            RemoveCoordinateMappings(record);
        }
        else
        {
            RemoveCoordinateMappings(recordId);
        }

        recordsById.Remove(recordId);
        version++;
        if (removesItemStack)
        {
            itemStackVersion++;
        }
        else if (removesInstallation)
        {
            installationVersion++;
        }
    }

    private bool TryResolveRecord(MapObjectHandle handle, out VirtualObjectRecord record)
    {
        if (!handle.IsValid
            || !recordsById.TryGetValue(handle.Slot, out record)
            || record == null
            || record.mapObjectHandle != handle)
        {
            record = null;
            return false;
        }

        return true;
    }

    private void EnsureMapObjectHandle(
        VirtualObjectRecord record,
        int typeId,
        long simulationId)
    {
        MapObjectHandle currentHandle = record.mapObjectHandle;
        if (currentHandle.IsValid
            && currentHandle.TypeId == typeId
            && currentHandle.SimulationId == simulationId)
        {
            return;
        }

        record.mapObjectHandle = new MapObjectHandle(
            typeId,
            record.id.Value,
            AllocateMapObjectGeneration(),
            simulationId);
    }

    private static uint AllocateMapObjectGeneration()
    {
        uint generation = nextGlobalMapObjectGeneration++;
        if (generation != 0)
        {
            return generation;
        }

        generation = nextGlobalMapObjectGeneration++;
        return generation != 0 ? generation : 1u;
    }

    private void RemoveCoordinateMappings(VirtualObjectRecord record)
    {
        if (record == null)
        {
            return;
        }

        int recordId = record.id.Value;
        for (int i = 0; i < record.occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = record.occupiedCoordinates[i];
            if (!recordIdsByCoordinate.TryGetValue(coordinate, out List<int> recordIds))
            {
                continue;
            }

            recordIds.Remove(recordId);
            if (recordIds.Count <= 0)
            {
                recordIdsByCoordinate.Remove(coordinate);
            }
        }
    }
}
