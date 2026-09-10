using System;
using System.Collections.Generic;
using ProjectF.Conveyors;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Immutable installation data used by conveyor simulation.  Installed belts do not own a
/// GameObject; every belt in the world is represented by one of these records and rendered by
/// the single ConveyorWorld host.
/// </summary>
public sealed class ConveyorRuntimeRecord
{
    private const float Belt2FPathHalfLength = 1.5f;
    private const float Belt2FPathHighHalfLength = 0.5f;
    private const float Belt2FPathLowHeight = 0.13f;
    private const float Belt2FPathHighHeight = 0.806f;
    private const float Belt2FSlotLongitudinalOffset = 0.25f;
    private const float Belt2FPathSlopeItemPitchDegrees = 34.0587f;

    private readonly Vector2Int[] occupiedCoordinates;
    private SplitterRoutingPolicy splitterRouting;
    private int splitterFilterOutput;
    private int splitterWheelMask;
    private int displayedSplitterWheelMask;
    private float leftWheelTransitionTime;
    private float rightWheelTransitionTime;
    private float leftWheelAngle;
    private float rightWheelAngle;

    internal ConveyorRuntimeRecord(
        BlockStateStore.InstallationSaveState state,
        ConveyorBelt prototype,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Vector3 worldScale,
        ConveyorWorld.VisualPart[] visualParts)
    {
        State = state != null ? state.Clone() : throw new ArgumentNullException(nameof(state));
        Prototype = prototype != null ? prototype : throw new ArgumentNullException(nameof(prototype));
        StorageKey = BlockStateStore.GetInstallationStorageKey(State);
        AnchorCoordinate = State.anchorCoordinate;
        QuarterTurns = ((State.quarterTurns % 4) + 4) % 4;
        PlacementSequence = State.placementSequence;
        ItemId = State.itemId;
        WorldPosition = worldPosition;
        WorldRotation = worldRotation;
        WorldScale = worldScale;
        VisualParts = visualParts ?? Array.Empty<ConveyorWorld.VisualPart>();
        occupiedCoordinates = State.occupiedCoordinates != null && State.occupiedCoordinates.Count > 0
            ? State.occupiedCoordinates.ToArray()
            : new[] { AnchorCoordinate };
        Spliterbelt.PersistentState splitterState = State.splitterState;
        splitterRouting = new SplitterRoutingPolicy
        {
            NextInput = splitterState != null ? splitterState.nextInput & 1 : 0,
            NextOutput = splitterState != null ? splitterState.nextOutput & 1 : 1
        };
        splitterFilterOutput = splitterState != null ? Mathf.Clamp(splitterState.filterOutput, 0, 2) : 0;
        splitterWheelMask = splitterState != null ? splitterState.wheelRotationMask & 3 : 0;
        displayedSplitterWheelMask = splitterWheelMask;
    }

    public BlockStateStore.InstallationSaveState State { get; }
    public ConveyorBelt Prototype { get; }
    public Vector2Int StorageKey { get; }
    public Vector2Int AnchorCoordinate { get; }
    public int QuarterTurns { get; }
    public long PlacementSequence { get; }
    public int ItemId { get; }
    public Vector3 WorldPosition { get; }
    public Quaternion WorldRotation { get; }
    public Vector3 WorldScale { get; }
    public bool IsBelt2F => Prototype is ConvayorBelt2F;
    public bool IsSplitter => Prototype is Spliterbelt;
    public bool IsCorner => Prototype.IsCornerVariant;
    public float Speed => Prototype.ConveyorSpeed;
    public bool HasValidPrototype => Prototype != null;
    public IReadOnlyList<Vector2Int> OccupiedCoordinates => occupiedCoordinates;
    internal ConveyorWorld.VisualPart[] VisualParts { get; }

    public bool Covers(Vector2Int coordinate)
    {
        for (int i = 0; i < occupiedCoordinates.Length; i++)
        {
            if (occupiedCoordinates[i] == coordinate)
            {
                return true;
            }
        }

        if (!IsBelt2F)
        {
            return false;
        }

        Vector2Int size = new Vector2Int(
            Mathf.Max(1, Prototype.Status.mapSizeX),
            Mathf.Max(1, Prototype.Status.mapSizeY));
        if (size == Vector2Int.one)
        {
            size = new Vector2Int(1, 3);
        }

        Vector2Int center = Prototype.PlacementCenterCell;
        center = new Vector2Int(
            Mathf.Clamp(center.x, 0, size.x - 1),
            Mathf.Clamp(center.y, 0, size.y - 1));
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                Vector2Int offset = RotateFootprintOffset(
                    new Vector2Int(x - center.x, y - center.y),
                    QuarterTurns);
                if (AnchorCoordinate + offset == coordinate)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool TryGetInputDirection(out Vector2Int direction)
    {
        return Prototype.TryGetInputDirection(WorldRotation, out direction);
    }

    public bool TryGetOutputDirection(out Vector2Int direction)
    {
        return Prototype.TryGetOutputDirection(WorldRotation, out direction);
    }

    public bool IsBridgeCenter(Vector2Int coordinate) => IsBelt2F && coordinate == AnchorCoordinate;

    public bool IsInputEdge(Vector2Int coordinate)
    {
        return TryGetInputDirection(out Vector2Int direction)
               && direction != Vector2Int.zero
               && Covers(coordinate)
               && !Covers(coordinate + direction);
    }

    public bool IsOutputEdge(Vector2Int coordinate)
    {
        return TryGetOutputDirection(out Vector2Int direction)
               && direction != Vector2Int.zero
               && Covers(coordinate)
               && !Covers(coordinate + direction);
    }

    public Vector3 ApplyBelt2FPathHeight(Vector3 worldPosition)
    {
        if (!IsBelt2F)
        {
            return worldPosition;
        }

        Matrix4x4 root = Matrix4x4.TRS(WorldPosition, WorldRotation, WorldScale);
        Vector3 local = root.inverse.MultiplyPoint3x4(worldPosition);
        local = ConveyorBelt2FPath.ConformItemPosition(
            local,
            true,
            Belt2FPathHalfLength,
            Belt2FPathHighHalfLength,
            Belt2FPathLowHeight,
            Belt2FPathHighHeight);
        return root.MultiplyPoint3x4(local);
    }

    public bool TryGetBelt2FLaneWorldPosition(
        Vector2Int coordinate,
        int laneIndex,
        Vector3 fallbackWorldPosition,
        out Vector3 worldPosition)
    {
        worldPosition = fallbackWorldPosition;
        if (!IsBelt2F || !Covers(coordinate))
        {
            return false;
        }

        Matrix4x4 root = Matrix4x4.TRS(WorldPosition, WorldRotation, WorldScale);
        Vector3 local = root.inverse.MultiplyPoint3x4(fallbackWorldPosition);
        if (TryGetOutputDirection(out Vector2Int output) && output != Vector2Int.zero)
        {
            Vector2Int relative = coordinate - AnchorCoordinate;
            int longitudinalStep = relative.x * output.x + relative.y * output.y;
            bool front = laneIndex == 0 || laneIndex == 1;
            bool back = laneIndex == 2 || laneIndex == 3;
            float slotOffset = back ? Belt2FSlotLongitudinalOffset : front ? -Belt2FSlotLongitudinalOffset : 0f;
            local.z = 0f;
            local.x = Mathf.Clamp(
                -longitudinalStep + slotOffset,
                -Belt2FPathHalfLength,
                Belt2FPathHalfLength);
        }

        local = ConveyorBelt2FPath.ConformItemPosition(
            local,
            true,
            Belt2FPathHalfLength,
            Belt2FPathHighHalfLength,
            Belt2FPathLowHeight,
            Belt2FPathHighHeight);
        worldPosition = root.MultiplyPoint3x4(local);
        return true;
    }

    public Vector3 Belt2FBridgePeakWorldPosition =>
        Matrix4x4.TRS(WorldPosition, WorldRotation, WorldScale)
            .MultiplyPoint3x4(new Vector3(0f, Belt2FPathHighHeight, 0f));

    public bool IsUpperBelt2FWorldPosition(Vector3 worldPosition)
    {
        if (!IsBelt2F)
        {
            return false;
        }

        Vector3 local = Matrix4x4.TRS(WorldPosition, WorldRotation, WorldScale)
            .inverse.MultiplyPoint3x4(worldPosition);
        return local.y >= (Belt2FPathLowHeight + Belt2FPathHighHeight) * 0.5f;
    }

    public Quaternion ResolveBelt2FPathItemRotation(Vector3 worldPosition)
    {
        if (!IsBelt2F)
        {
            return Quaternion.identity;
        }

        Matrix4x4 root = Matrix4x4.TRS(WorldPosition, WorldRotation, WorldScale);
        Vector3 local = root.inverse.MultiplyPoint3x4(worldPosition);
        float absoluteCoordinate = Mathf.Abs(local.x);
        if (absoluteCoordinate <= Belt2FPathHighHalfLength + 0.0001f
            || absoluteCoordinate > Belt2FPathHalfLength + 0.0001f)
        {
            return Quaternion.identity;
        }

        float pitch = local.x > 0f
            ? Belt2FPathSlopeItemPitchDegrees
            : -Belt2FPathSlopeItemPitchDegrees;
        Vector3 worldTiltAxis = root.MultiplyVector(Vector3.forward).normalized;
        return Quaternion.AngleAxis(-pitch, worldTiltAxis);
    }

    public bool TryGetSplitterChannel(Vector2Int coordinate, out int channel)
    {
        channel = -1;
        if (!IsSplitter
            || occupiedCoordinates.Length != 2
            || !TryGetOutputDirection(out Vector2Int flow)
            || flow == Vector2Int.zero)
        {
            return false;
        }

        Vector2Int right = new Vector2Int(-flow.y, flow.x);
        Vector2Int firstOffset = occupiedCoordinates[0] - AnchorCoordinate;
        Vector2Int secondOffset = occupiedCoordinates[1] - AnchorCoordinate;
        int firstProjection = firstOffset.x * right.x + firstOffset.y * right.y;
        int secondProjection = secondOffset.x * right.x + secondOffset.y * right.y;
        Vector2Int leftCoordinate = firstProjection < secondProjection
            ? occupiedCoordinates[0]
            : occupiedCoordinates[1];
        Vector2Int rightCoordinate = leftCoordinate == occupiedCoordinates[0]
            ? occupiedCoordinates[1]
            : occupiedCoordinates[0];
        if (coordinate == leftCoordinate)
        {
            channel = 0;
            return true;
        }

        if (coordinate == rightCoordinate)
        {
            channel = 1;
            return true;
        }

        return false;
    }

    internal BeltSplitterState CaptureBeltJobRouting()
    {
        return new BeltSplitterState
        {
            NextInput = splitterRouting.NextInput,
            NextOutput = splitterRouting.NextOutput,
            WheelMask = splitterWheelMask,
            FilterOutput = splitterFilterOutput
        };
    }

    internal void ApplyBeltJobRouting(BeltSplitterState state)
    {
        int nextInput = state.NextInput & 1;
        int nextOutput = state.NextOutput & 1;
        int wheelMask = state.WheelMask & 3;
        if (splitterRouting.NextInput == nextInput
            && splitterRouting.NextOutput == nextOutput
            && splitterWheelMask == wheelMask)
        {
            return;
        }

        splitterRouting.NextInput = nextInput;
        splitterRouting.NextOutput = nextOutput;
        SetSplitterWheelMask(wheelMask);
        PersistSplitterRuntimeState();
    }

    internal float TickSplitterWheel(int channel, float now, float deltaTime)
    {
        if (!IsSplitter || channel < 0 || channel > 1)
        {
            return 0f;
        }

        int bit = 1 << channel;
        float transitionTime = channel == 0 ? leftWheelTransitionTime : rightWheelTransitionTime;
        if (now >= transitionTime)
        {
            displayedSplitterWheelMask = (displayedSplitterWheelMask & ~bit) | (splitterWheelMask & bit);
        }

        if ((displayedSplitterWheelMask & bit) != 0 && Speed > 0f)
        {
            float deltaAngle = Speed * 180f * Mathf.Max(0f, deltaTime) * (channel == 0 ? 1f : -1f);
            if (channel == 0)
            {
                leftWheelAngle = Mathf.Repeat(leftWheelAngle + deltaAngle, 360f);
            }
            else
            {
                rightWheelAngle = Mathf.Repeat(rightWheelAngle + deltaAngle, 360f);
            }
        }

        return channel == 0 ? leftWheelAngle : rightWheelAngle;
    }

    internal bool TrySelectSplitterOutput(
        int input,
        bool leftReady,
        bool rightReady,
        int leftOutputs,
        int rightOutputs,
        out int output)
    {
        return splitterRouting.TrySelect(
            input,
            leftReady,
            rightReady,
            leftOutputs,
            rightOutputs,
            out output);
    }

    internal void CommitSplitterTransfer(int input, int output)
    {
        int sourceBit = 1 << input;
        SetSplitterWheelMask(input == output
            ? splitterWheelMask | sourceBit
            : splitterWheelMask & ~sourceBit);

        splitterRouting.Commit(input, output);
        PersistSplitterRuntimeState();
    }

    private void SetSplitterWheelMask(int value)
    {
        int normalized = value & 3;
        int changedWheels = splitterWheelMask ^ normalized;
        splitterWheelMask = normalized;
        float now = Time.time;
        if ((changedWheels & 1) != 0)
        {
            leftWheelTransitionTime = now + 0.15f;
        }

        if ((changedWheels & 2) != 0)
        {
            rightWheelTransitionTime = now + 0.15f;
        }
    }

    internal int GetSplitterAllowedOutputMask(int itemId)
    {
        if (splitterFilterOutput == 0)
        {
            return 3;
        }

        bool selected = IsItemSelectedBySplitterFilter(itemId);
        int filteredChannel = splitterFilterOutput == 1 ? 0 : 1;
        return 1 << (selected ? filteredChannel : 1 - filteredChannel);
    }

    internal bool HasSplitterItemFilter =>
        State.itemFilterMaskInitialized
        && State.itemFilterMaskWords != null
        && State.itemFilterMaskWords.Count > 0;

    internal IReadOnlyList<ulong> SplitterItemFilterWords => State.itemFilterMaskWords;

    public int SelectedSplitterFilterOutput => splitterFilterOutput;

    public bool IsSplitterItemFilterEnabled(int itemId)
    {
        return IsSplitter
               && State.itemFilterMaskInitialized
               && MapObject.IsItemAllowedByFilterMask(
                   itemId,
                   true,
                   State.itemFilterMaskWords);
    }

    public void SetSplitterItemFilterEnabled(int itemId, int totalItemCount, bool enabled)
    {
        if (!IsSplitter || itemId < 0)
        {
            return;
        }

        int requiredWords = (Mathf.Max(totalItemCount, itemId + 1) + 63) >> 6;
        State.itemFilterMaskWords ??= new List<ulong>(requiredWords);
        while (State.itemFilterMaskWords.Count < requiredWords)
        {
            State.itemFilterMaskWords.Add(0UL);
        }

        State.itemFilterMaskInitialized = true;
        int wordIndex = itemId >> 6;
        ulong bit = 1UL << (itemId & 63);
        State.itemFilterMaskWords[wordIndex] = enabled
            ? State.itemFilterMaskWords[wordIndex] | bit
            : State.itemFilterMaskWords[wordIndex] & ~bit;
        if (enabled && splitterFilterOutput == 0)
        {
            splitterFilterOutput = 1;
        }

        PersistSplitterRuntimeState();
    }

    public void SetSplitterFilterOutput(int output)
    {
        if (!IsSplitter)
        {
            return;
        }

        splitterFilterOutput = Mathf.Clamp(output, 0, 2);
        PersistSplitterRuntimeState();
    }

    private bool IsItemSelectedBySplitterFilter(int itemId)
    {
        if (!State.itemFilterMaskInitialized || itemId < 0 || State.itemFilterMaskWords == null)
        {
            return false;
        }

        int wordIndex = itemId >> 6;
        return wordIndex >= 0
               && wordIndex < State.itemFilterMaskWords.Count
               && (State.itemFilterMaskWords[wordIndex] & (1UL << (itemId & 63))) != 0;
    }

    private void PersistSplitterRuntimeState()
    {
        State.splitterState ??= new Spliterbelt.PersistentState();
        State.splitterState.nextInput = splitterRouting.NextInput;
        State.splitterState.nextOutput = splitterRouting.NextOutput;
        State.splitterState.filterOutput = splitterFilterOutput;
        State.splitterState.wheelRotationMask = splitterWheelMask;
        TerrainGenerator.Active?.UpdateDataOnlyConveyorState(State);
    }

    private static Vector2Int RotateFootprintOffset(Vector2Int offset, int quarterTurns)
    {
        return (((quarterTurns % 4) + 4) % 4) switch
        {
            1 => new Vector2Int(offset.y, -offset.x),
            2 => new Vector2Int(-offset.x, -offset.y),
            3 => new Vector2Int(-offset.y, offset.x),
            _ => offset
        };
    }
}

/// <summary>
/// The only persistent scene object used by installed conveyors.  It owns simulation records,
/// coordinate lookup, child-transform matrices and all conveyor draw batches.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(999)]
public sealed class ConveyorWorld : MonoBehaviour, IVirtualRenderBatchOwner
{
    internal enum EndpointVisualKind : byte
    {
        None,
        InputEnd,
        OutputEnd,
        InputSeam,
        OutputSeam
    }

    internal readonly struct VisualPart
    {
        public VisualPart(
            Mesh mesh,
            Material material,
            Matrix4x4 localToRoot,
            int layer,
            int subMeshIndex,
            bool hasUvScroll,
            float uvScrollY,
            float uvLengthScale,
            float uvLengthOffset,
            bool hasEndpointExtension,
            Matrix4x4 startSeamLocalToRoot,
            Matrix4x4 endSeamLocalToRoot,
            Matrix4x4 bothSeamsLocalToRoot,
            EndpointVisualKind endpointKind,
            int endpointChannel,
            int wheelChannel,
            Matrix4x4 wheelPivotToRoot,
            Matrix4x4 rendererToWheelPivot)
        {
            Mesh = mesh;
            Material = material;
            LocalToRoot = localToRoot;
            Layer = layer;
            SubMeshIndex = subMeshIndex;
            HasUvScroll = hasUvScroll;
            UvScrollY = uvScrollY;
            UvLengthScale = uvLengthScale;
            UvLengthOffset = uvLengthOffset;
            HasEndpointExtension = hasEndpointExtension;
            StartSeamLocalToRoot = startSeamLocalToRoot;
            EndSeamLocalToRoot = endSeamLocalToRoot;
            BothSeamsLocalToRoot = bothSeamsLocalToRoot;
            EndpointKind = endpointKind;
            EndpointChannel = endpointChannel;
            WheelChannel = wheelChannel;
            WheelPivotToRoot = wheelPivotToRoot;
            RendererToWheelPivot = rendererToWheelPivot;
        }

        public Mesh Mesh { get; }
        public Material Material { get; }
        public Matrix4x4 LocalToRoot { get; }
        public int Layer { get; }
        public int SubMeshIndex { get; }
        public bool HasUvScroll { get; }
        public float UvScrollY { get; }
        public float UvLengthScale { get; }
        public float UvLengthOffset { get; }
        public bool HasEndpointExtension { get; }
        public Matrix4x4 StartSeamLocalToRoot { get; }
        public Matrix4x4 EndSeamLocalToRoot { get; }
        public Matrix4x4 BothSeamsLocalToRoot { get; }
        public EndpointVisualKind EndpointKind { get; }
        public int EndpointChannel { get; }
        public int WheelChannel { get; }
        public Matrix4x4 WheelPivotToRoot { get; }
        public Matrix4x4 RendererToWheelPivot { get; }
    }

    private readonly struct AnimatedVisualEntry
    {
        public AnimatedVisualEntry(ConveyorRuntimeRecord record, VisualPart part, int batchEntryIndex)
        {
            Record = record;
            Part = part;
            BatchEntryIndex = batchEntryIndex;
        }

        public ConveyorRuntimeRecord Record { get; }
        public VisualPart Part { get; }
        public int BatchEntryIndex { get; }
    }

    private const string HostName = "ConveyorWorld";
    private const float BatchCellSize = 16f;
    private static readonly int CullShaderId = Shader.PropertyToID("_Cull");
    private static ConveyorWorld current;

    private readonly Dictionary<Vector2Int, ConveyorRuntimeRecord> recordsByStorageKey =
        new Dictionary<Vector2Int, ConveyorRuntimeRecord>();
    private readonly Dictionary<Vector2Int, List<ConveyorRuntimeRecord>> recordsByCoordinate =
        new Dictionary<Vector2Int, List<ConveyorRuntimeRecord>>();
    private readonly Dictionary<Vector2Int, BoxCollider> splitterCollidersByStorageKey =
        new Dictionary<Vector2Int, BoxCollider>();
    private readonly List<VirtualRenderBatchEntry> batchEntries = new List<VirtualRenderBatchEntry>(1024);
    private readonly List<AnimatedVisualEntry> animatedVisualEntries = new List<AnimatedVisualEntry>(64);
    private readonly List<Material> materialScratch = new List<Material>(4);
    private readonly Dictionary<Material, Material> mirroredMaterials = new Dictionary<Material, Material>();
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private Camera mainCamera;
    private bool batchesDirty = true;

    public static ConveyorWorld Current => current;
    public int InstalledBeltCount => recordsByStorageKey.Count;
    public int SceneGameObjectCount => 1;
    public int BatchEntryCount => batchEntries.Count;

    public static ConveyorWorld EnsureFor(TerrainGenerator terrain)
    {
        if (terrain == null)
        {
            return null;
        }

        if (current != null && current.transform.parent == terrain.transform)
        {
            return current;
        }

        Transform child = terrain.transform.Find(HostName);
        GameObject host = child != null ? child.gameObject : new GameObject(HostName);
        if (child == null)
        {
            host.transform.SetParent(terrain.transform, false);
        }

        current = host.GetComponent<ConveyorWorld>();
        if (current == null)
        {
            current = host.AddComponent<ConveyorWorld>();
        }

        return current;
    }

    public ConveyorRuntimeRecord Register(
        BlockStateStore.InstallationSaveState state,
        ConveyorBelt prototype,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Vector3 worldScale)
    {
        if (state == null
            || prototype == null
            || prototype.gameObject.scene.IsValid())
        {
            return null;
        }

        Vector2Int storageKey = BlockStateStore.GetInstallationStorageKey(state);
        Remove(storageKey);
        ConveyorRuntimeRecord record = new ConveyorRuntimeRecord(
            state,
            prototype,
            worldPosition,
            worldRotation,
            worldScale,
            CaptureVisualParts(prototype, worldPosition, worldRotation));
        recordsByStorageKey.Add(storageKey, record);
        AddCoordinateMappings(record);
        CreateSplitterCollider(record, prototype);
        batchesDirty = true;
        return record;
    }

    public bool Remove(Vector2Int storageKey)
    {
        if (!recordsByStorageKey.TryGetValue(storageKey, out ConveyorRuntimeRecord record))
        {
            return false;
        }

        recordsByStorageKey.Remove(storageKey);
        RemoveCoordinateMappings(record);
        RemoveSplitterCollider(storageKey);
        batchesDirty = true;
        return true;
    }

    public void ClearRecords()
    {
        recordsByStorageKey.Clear();
        recordsByCoordinate.Clear();
        foreach (BoxCollider collider in splitterCollidersByStorageKey.Values)
        {
            DestroyRuntimeComponent(collider);
        }
        splitterCollidersByStorageKey.Clear();
        batchesDirty = true;
    }

    public bool TryGetAtCoordinate(Vector2Int coordinate, out ConveyorRuntimeRecord record)
    {
        record = null;
        if (!recordsByCoordinate.TryGetValue(coordinate, out List<ConveyorRuntimeRecord> candidates))
        {
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            ConveyorRuntimeRecord candidate = candidates[i];
            if (candidate == null
                || !candidate.HasValidPrototype
                || !candidate.Covers(coordinate))
            {
                continue;
            }

            if (record == null
                || (record.IsBelt2F && !candidate.IsBelt2F)
                || record.IsBelt2F == candidate.IsBelt2F
                && candidate.PlacementSequence > record.PlacementSequence)
            {
                record = candidate;
            }
        }

        return record != null;
    }

    public bool TryGetBelt2FAtCoordinate(Vector2Int coordinate, out ConveyorRuntimeRecord record)
    {
        record = null;
        if (!recordsByCoordinate.TryGetValue(coordinate, out List<ConveyorRuntimeRecord> candidates))
        {
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            ConveyorRuntimeRecord candidate = candidates[i];
            if (candidate != null
                && candidate.HasValidPrototype
                && candidate.IsBelt2F
                && candidate.Covers(coordinate)
                && (record == null || candidate.PlacementSequence > record.PlacementSequence))
            {
                record = candidate;
            }
        }

        return record != null;
    }

    public bool TryGetMatchingAtCoordinate(
        Vector2Int coordinate,
        ConveyorBelt prototype,
        out ConveyorRuntimeRecord record)
    {
        record = null;
        if (prototype == null)
        {
            return false;
        }

        bool found = prototype is ConvayorBelt2F
            ? TryGetBelt2FAtCoordinate(coordinate, out record)
            : TryGetAtCoordinate(coordinate, out record);
        if (found
            && record.HasValidPrototype
            && ReferenceEquals(record.Prototype, prototype))
        {
            return true;
        }

        record = null;
        return false;
    }

    public bool TryGetByStorageKey(Vector2Int storageKey, out ConveyorRuntimeRecord record)
    {
        return recordsByStorageKey.TryGetValue(storageKey, out record)
               && record != null
               && record.HasValidPrototype;
    }

    private void Awake()
    {
        current = this;
    }

    private void OnDestroy()
    {
        if (current == this)
        {
            current = null;
        }

        batches.Dispose();
        foreach (Material material in mirroredMaterials.Values)
        {
            if (material == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }
        mirroredMaterials.Clear();
    }

    private void LateUpdate()
    {
        if (batchesDirty)
        {
            RebuildBatches();
        }

        UpdateAnimatedPartMatrices();

        if (GameManager.Instance != null && GameManager.Instance.HideBelts)
        {
            batches.SuspendRendering();
            return;
        }

        if (mainCamera == null || !mainCamera.isActiveAndEnabled)
        {
            mainCamera = Camera.main;
        }

        batches.RenderBatches(mainCamera);
    }

    private void RebuildBatches()
    {
        batchesDirty = false;
        batches.Clear();
        batchEntries.Clear();
        animatedVisualEntries.Clear();
        foreach (ConveyorRuntimeRecord record in recordsByStorageKey.Values)
        {
            if (record == null || !record.HasValidPrototype)
            {
                continue;
            }

            Matrix4x4 rootMatrix = Matrix4x4.TRS(
                record.WorldPosition,
                record.WorldRotation,
                record.WorldScale);
            VisualPart[] parts = record.VisualParts;
            for (int i = 0; i < parts.Length; i++)
            {
                VisualPart part = parts[i];
                if (part.Mesh == null
                    || part.Material == null
                    || !ShouldRenderPart(record, part))
                {
                    continue;
                }

                Matrix4x4 matrix = rootMatrix * ResolveLocalToRoot(record, part);
                Material renderMaterial = ResolveRenderMaterial(
                    part.Material,
                    HasOddNegativeScale(matrix),
                    out bool invertCulling);
                if (!renderMaterial.enableInstancing)
                {
                    renderMaterial.enableInstancing = true;
                }

                Vector3 position = new Vector3(matrix.m03, matrix.m13, matrix.m23);
                VirtualRenderBatchKey key = new VirtualRenderBatchKey(
                    part.Mesh,
                    renderMaterial,
                    part.Layer,
                    part.SubMeshIndex,
                    ShadowCastingMode.Off,
                    false,
                    part.HasUvScroll,
                    batchCellX: Mathf.FloorToInt(position.x / BatchCellSize),
                    batchCellZ: Mathf.FloorToInt(position.z / BatchCellSize),
                    invertCulling: invertCulling);
                int batchEntryIndex = batchEntries.Count;
                batches.AddOwnedMatrix(
                    this,
                    batchEntries,
                    key,
                    matrix,
                    new Vector4(
                        0f,
                        part.HasUvScroll ? part.UvScrollY : 0f,
                        part.UvLengthScale,
                        part.UvLengthOffset));
                if (part.WheelChannel >= 0)
                {
                    animatedVisualEntries.Add(new AnimatedVisualEntry(record, part, batchEntryIndex));
                }
            }
        }
    }

    private Material ResolveRenderMaterial(
        Material source,
        bool mirrored,
        out bool invertCulling)
    {
        invertCulling = mirrored;
        if (!mirrored || source == null || !source.HasProperty(CullShaderId))
        {
            return source;
        }

        int sourceCullMode = Mathf.RoundToInt(source.GetFloat(CullShaderId));
        if (sourceCullMode == (int)CullMode.Off)
        {
            invertCulling = false;
            return source;
        }

        if (!mirroredMaterials.TryGetValue(source, out Material mirroredMaterial)
            || mirroredMaterial == null)
        {
            mirroredMaterial = new Material(source)
            {
                name = source.name + " (Mirrored Conveyor)",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };
            mirroredMaterial.SetFloat(
                CullShaderId,
                sourceCullMode == (int)CullMode.Front
                    ? (int)CullMode.Back
                    : (int)CullMode.Front);
            mirroredMaterials[source] = mirroredMaterial;
        }

        // RenderMeshInstanced does not expose a per-draw winding flag on every pipeline.
        // Swapping the material cull face gives mirrored corner bodies stable winding.
        invertCulling = false;
        return mirroredMaterial;
    }

    private VisualPart[] CaptureVisualParts(
        ConveyorBelt source,
        Vector3 targetWorldPosition,
        Quaternion targetWorldRotation)
    {
        if (source == null)
        {
            return Array.Empty<VisualPart>();
        }

        // Always read immutable prefab visuals. A newly placed scene instance can already have
        // its BeltTop children hidden by the legacy virtual renderer before this capture runs.
        MeshRenderer[] renderers = source.GetComponentsInChildren<MeshRenderer>(true);
        List<VisualPart> parts = new List<VisualPart>(renderers.Length * 2);
        Transform root = source.transform;
        source.RefreshBeltTopUvData();
        float uvPhaseOffset = 0f;
        if (source.TryGetOutputDirection(root.rotation, out Vector2Int sourceOutput)
            && source.TryGetOutputDirection(targetWorldRotation, out Vector2Int targetOutput))
        {
            Vector3 sourceFlow = new Vector3(sourceOutput.x, 0f, sourceOutput.y);
            Vector3 targetFlow = new Vector3(targetOutput.x, 0f, targetOutput.y);
            uvPhaseOffset = Vector3.Dot(targetWorldPosition, targetFlow)
                            - Vector3.Dot(root.position, sourceFlow);
        }
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            MeshRenderer renderer = renderers[rendererIndex];
            MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            TryClassifyEndpointVisual(
                renderer != null ? renderer.transform : null,
                source.transform,
                out EndpointVisualKind endpointKind,
                out int endpointChannel,
                out Transform endpointRoot);
            if (renderer == null
                || endpointKind == EndpointVisualKind.None && !IsActiveRelativeToRoot(renderer.transform, root)
                || endpointKind != EndpointVisualKind.None
                   && !IsActiveBelowEndpointRoot(renderer.transform, endpointRoot)
                || filter == null
                || filter.sharedMesh == null)
            {
                continue;
            }

            materialScratch.Clear();
            renderer.GetSharedMaterials(materialScratch);
            Mesh renderMesh = filter.sharedMesh;
            bool hasEndpointExtension = source.TryGetBeltTopEndpointLocalToRoot(
                renderer,
                false,
                false,
                out Matrix4x4 beltTopLocalToRoot);
            // Non-top parts must retain their authored transform; a failed Try call
            // returns identity, which would move bodies and seams to the belt root.
            Matrix4x4 localToRoot = hasEndpointExtension
                ? beltTopLocalToRoot
                : CalculateLocalToRoot(renderer.transform, root);
            Matrix4x4 startSeamLocalToRoot = localToRoot;
            Matrix4x4 endSeamLocalToRoot = localToRoot;
            Matrix4x4 bothSeamsLocalToRoot = localToRoot;
            if (hasEndpointExtension)
            {
                source.TryGetBeltTopEndpointLocalToRoot(renderer, true, false, out startSeamLocalToRoot);
                source.TryGetBeltTopEndpointLocalToRoot(renderer, false, true, out endSeamLocalToRoot);
                source.TryGetBeltTopEndpointLocalToRoot(renderer, true, true, out bothSeamsLocalToRoot);
            }
            Transform wheelPivot = FindNamedAncestor(renderer.transform, root, "Wheel_L", "Wheel_R");
            int wheelChannel = -1;
            Matrix4x4 wheelPivotToRoot = Matrix4x4.identity;
            Matrix4x4 rendererToWheelPivot = Matrix4x4.identity;
            if (wheelPivot != null)
            {
                wheelPivotToRoot = CalculateLocalToRoot(wheelPivot, root);
                rendererToWheelPivot = CalculateLocalToRoot(renderer.transform, wheelPivot);
                wheelChannel = wheelPivotToRoot.m03 >= 0f ? 1 : 0;
            }
            bool hasUvScroll = source.TryGetBeltTopUvData(renderer, out Vector4 uvData);
            if (hasUvScroll && Mathf.Abs(uvPhaseOffset) > 0.0001f)
            {
                uvData.w = Mathf.Repeat(uvData.w + uvPhaseOffset, 1f);
            }

            if (!source.IsCornerVariant
                && hasUvScroll
                && !source.RequiresAuthoredBeltTopMesh(renderer, renderMesh)
                && ConveyorBelt.TryCreateDedicatedBeltTopMatrix(
                    renderMesh,
                    root.localToWorldMatrix * localToRoot,
                    out Matrix4x4 dedicatedWorldMatrix))
            {
                renderMesh = ConveyorBelt.GetVirtualBeltTopMesh();
                localToRoot = root.worldToLocalMatrix * dedicatedWorldMatrix;
                if (hasEndpointExtension)
                {
                    startSeamLocalToRoot = CreateDedicatedBeltTopLocalToRoot(
                        filter.sharedMesh,
                        root,
                        startSeamLocalToRoot);
                    endSeamLocalToRoot = CreateDedicatedBeltTopLocalToRoot(
                        filter.sharedMesh,
                        root,
                        endSeamLocalToRoot);
                    bothSeamsLocalToRoot = CreateDedicatedBeltTopLocalToRoot(
                        filter.sharedMesh,
                        root,
                        bothSeamsLocalToRoot);
                }
            }

            int subMeshCount = Mathf.Max(1, renderMesh.subMeshCount);
            int passCount = Mathf.Max(subMeshCount, materialScratch.Count);
            for (int pass = 0; pass < passCount; pass++)
            {
                if (materialScratch.Count == 0)
                {
                    break;
                }

                Material material = materialScratch[Mathf.Min(pass, materialScratch.Count - 1)];
                if (material == null)
                {
                    continue;
                }

                parts.Add(new VisualPart(
                    renderMesh,
                    material,
                    localToRoot,
                    renderer.gameObject.layer,
                    Mathf.Min(pass, subMeshCount - 1),
                    hasUvScroll,
                    uvData.y,
                    uvData.z,
                    uvData.w,
                    hasEndpointExtension,
                    startSeamLocalToRoot,
                    endSeamLocalToRoot,
                    bothSeamsLocalToRoot,
                    endpointKind,
                    endpointChannel,
                    wheelChannel,
                    wheelPivotToRoot,
                    rendererToWheelPivot));
            }
        }

        return parts.ToArray();
    }

    private static Matrix4x4 CreateDedicatedBeltTopLocalToRoot(
        Mesh sourceMesh,
        Transform root,
        Matrix4x4 sourceLocalToRoot)
    {
        Matrix4x4 sourceWorld = root.localToWorldMatrix * sourceLocalToRoot;
        return ConveyorBelt.TryCreateDedicatedBeltTopMatrix(sourceMesh, sourceWorld, out Matrix4x4 dedicatedWorld)
            ? root.worldToLocalMatrix * dedicatedWorld
            : sourceLocalToRoot;
    }

    private Matrix4x4 ResolveLocalToRoot(ConveyorRuntimeRecord record, VisualPart part)
    {
        if (!part.HasEndpointExtension)
        {
            return part.LocalToRoot;
        }

        TryGetEndpointState(record, -1, true, out _, out bool startSeam);
        TryGetEndpointState(record, -1, false, out _, out bool endSeam);
        if (startSeam && endSeam)
        {
            return part.BothSeamsLocalToRoot;
        }

        if (startSeam)
        {
            return part.StartSeamLocalToRoot;
        }

        return endSeam ? part.EndSeamLocalToRoot : part.LocalToRoot;
    }

    private void UpdateAnimatedPartMatrices()
    {
        if (animatedVisualEntries.Count == 0)
        {
            return;
        }

        float now = Time.time;
        float deltaTime = Time.deltaTime;
        ConveyorRuntimeRecord previousRecord = null;
        float leftAngle = 0f;
        float rightAngle = 0f;
        for (int i = 0; i < animatedVisualEntries.Count; i++)
        {
            AnimatedVisualEntry entry = animatedVisualEntries[i];
            if (!ReferenceEquals(previousRecord, entry.Record))
            {
                previousRecord = entry.Record;
                leftAngle = entry.Record.TickSplitterWheel(0, now, deltaTime);
                rightAngle = entry.Record.TickSplitterWheel(1, now, deltaTime);
            }

            float angle = entry.Part.WheelChannel == 0 ? leftAngle : rightAngle;
            Matrix4x4 root = Matrix4x4.TRS(
                entry.Record.WorldPosition,
                entry.Record.WorldRotation,
                entry.Record.WorldScale);
            Matrix4x4 matrix = root
                               * entry.Part.WheelPivotToRoot
                               * Matrix4x4.Rotate(Quaternion.AngleAxis(angle, Vector3.right))
                               * entry.Part.RendererToWheelPivot;
            batches.TryUpdateOwnedMatrix(
                batchEntries,
                entry.BatchEntryIndex,
                batchEntries[entry.BatchEntryIndex].BatchKey,
                matrix);
        }
    }

    private bool ShouldRenderPart(ConveyorRuntimeRecord record, VisualPart part)
    {
        if (part.EndpointKind == EndpointVisualKind.None)
        {
            return true;
        }

        if (!TryGetEndpointState(
                record,
                part.EndpointChannel,
                part.EndpointKind == EndpointVisualKind.InputEnd
                || part.EndpointKind == EndpointVisualKind.InputSeam,
                out bool connected,
                out bool perpendicular))
        {
            return false;
        }

        return part.EndpointKind == EndpointVisualKind.InputSeam
               || part.EndpointKind == EndpointVisualKind.OutputSeam
            ? perpendicular
            : !connected && !perpendicular;
    }

    private bool TryGetEndpointState(
        ConveyorRuntimeRecord record,
        int channel,
        bool input,
        out bool connected,
        out bool perpendicular)
    {
        connected = false;
        perpendicular = false;
        if (record == null
            || !record.TryGetInputDirection(out Vector2Int inputDirection)
            || !record.TryGetOutputDirection(out Vector2Int outputDirection))
        {
            return false;
        }

        Vector2Int edge;
        if (record.IsSplitter)
        {
            if (channel < 0 || !TryGetSplitterCoordinate(record, channel, out edge))
            {
                return false;
            }
        }
        else
        {
            edge = FindEndpointEdge(record, input ? inputDirection : outputDirection);
        }

        Vector2Int direction = input ? inputDirection : outputDirection;
        Vector2Int endpointCoordinate = edge + direction;
        if (!TryGetEndpointNeighbor(endpointCoordinate, record, out ConveyorRuntimeRecord neighbor))
        {
            return true;
        }

        if (input)
        {
            connected = neighbor.TryGetOutputDirection(out Vector2Int neighborOutput)
                        && neighborOutput == -direction;
        }
        else
        {
            connected = neighbor.TryGetInputDirection(out Vector2Int neighborInput)
                        && neighborInput == -direction;
        }

        if (connected
            || neighbor.IsSplitter
            || !neighbor.TryGetOutputDirection(out Vector2Int neighborFlow)
            || direction.x * neighborFlow.x + direction.y * neighborFlow.y != 0)
        {
            return true;
        }

        if (record.IsBelt2F && neighbor.IsBelt2F)
        {
            return true;
        }

        if (neighbor.IsBelt2F && !neighbor.IsInputEdge(endpointCoordinate))
        {
            return true;
        }

        perpendicular = true;
        return true;
    }

    private static Vector2Int FindEndpointEdge(ConveyorRuntimeRecord record, Vector2Int direction)
    {
        Vector2Int result = record.AnchorCoordinate;
        int best = int.MinValue;
        IReadOnlyList<Vector2Int> occupied = record.OccupiedCoordinates;
        for (int i = 0; i < occupied.Count; i++)
        {
            Vector2Int offset = occupied[i] - record.AnchorCoordinate;
            int score = offset.x * direction.x + offset.y * direction.y;
            if (score > best)
            {
                best = score;
                result = occupied[i];
            }
        }

        return result;
    }

    private static bool TryGetSplitterCoordinate(
        ConveyorRuntimeRecord record,
        int channel,
        out Vector2Int coordinate)
    {
        IReadOnlyList<Vector2Int> occupied = record.OccupiedCoordinates;
        for (int i = 0; i < occupied.Count; i++)
        {
            if (record.TryGetSplitterChannel(occupied[i], out int candidateChannel)
                && candidateChannel == channel)
            {
                coordinate = occupied[i];
                return true;
            }
        }

        coordinate = default;
        return false;
    }

    private bool TryGetEndpointNeighbor(
        Vector2Int coordinate,
        ConveyorRuntimeRecord ignored,
        out ConveyorRuntimeRecord record)
    {
        record = null;
        if (!recordsByCoordinate.TryGetValue(coordinate, out List<ConveyorRuntimeRecord> candidates))
        {
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            ConveyorRuntimeRecord candidate = candidates[i];
            if (candidate == null
                || !candidate.HasValidPrototype
                || ReferenceEquals(candidate, ignored)
                || !candidate.Covers(coordinate))
            {
                continue;
            }

            if (record == null
                || record.IsBelt2F && !candidate.IsBelt2F
                || record.IsBelt2F == candidate.IsBelt2F
                   && candidate.PlacementSequence > record.PlacementSequence)
            {
                record = candidate;
            }
        }

        return record != null;
    }

    private static void TryClassifyEndpointVisual(
        Transform transform,
        Transform root,
        out EndpointVisualKind kind,
        out int channel,
        out Transform endpointRoot)
    {
        kind = EndpointVisualKind.None;
        channel = -1;
        endpointRoot = null;
        for (Transform currentTransform = transform;
             currentTransform != null && currentTransform != root;
             currentTransform = currentTransform.parent)
        {
            string objectName = currentTransform.name;
            switch (objectName)
            {
                case "End_S": kind = EndpointVisualKind.InputEnd; break;
                case "End_E": kind = EndpointVisualKind.OutputEnd; break;
                case "Seam_S": kind = EndpointVisualKind.InputSeam; break;
                case "Seam_E": kind = EndpointVisualKind.OutputSeam; break;
                case "End_S_L": kind = EndpointVisualKind.InputEnd; channel = 0; break;
                case "End_S_R": kind = EndpointVisualKind.InputEnd; channel = 1; break;
                case "End_E_L": kind = EndpointVisualKind.OutputEnd; channel = 0; break;
                case "End_E_R": kind = EndpointVisualKind.OutputEnd; channel = 1; break;
                case "Seam_S_L": kind = EndpointVisualKind.InputSeam; channel = 0; break;
                case "Seam_S_R": kind = EndpointVisualKind.InputSeam; channel = 1; break;
                case "Seam_E_L": kind = EndpointVisualKind.OutputSeam; channel = 0; break;
                case "Seam_E_R": kind = EndpointVisualKind.OutputSeam; channel = 1; break;
                default: continue;
            }

            endpointRoot = currentTransform;
            return;
        }
    }

    private static bool IsActiveBelowEndpointRoot(Transform transform, Transform endpointRoot)
    {
        for (Transform currentTransform = transform;
             currentTransform != null && currentTransform != endpointRoot;
             currentTransform = currentTransform.parent)
        {
            if (!currentTransform.gameObject.activeSelf)
            {
                return false;
            }
        }

        return endpointRoot != null;
    }

    private static Transform FindNamedAncestor(
        Transform transform,
        Transform root,
        string firstName,
        string secondName)
    {
        for (Transform currentTransform = transform;
             currentTransform != null && currentTransform != root;
             currentTransform = currentTransform.parent)
        {
            if (currentTransform.name == firstName || currentTransform.name == secondName)
            {
                return currentTransform;
            }
        }

        return null;
    }

    private void AddCoordinateMappings(ConveyorRuntimeRecord record)
    {
        IReadOnlyList<Vector2Int> occupied = record.OccupiedCoordinates;
        for (int i = 0; i < occupied.Count; i++)
        {
            AddCoordinateMapping(occupied[i], record);
        }

        if (record.IsBelt2F)
        {
            for (int y = -2; y <= 2; y++)
            {
                for (int x = -2; x <= 2; x++)
                {
                    Vector2Int coordinate = record.AnchorCoordinate + new Vector2Int(x, y);
                    if (record.Covers(coordinate))
                    {
                        AddCoordinateMapping(coordinate, record);
                    }
                }
            }
        }
    }

    private void CreateSplitterCollider(ConveyorRuntimeRecord record, ConveyorBelt source)
    {
        if (record == null || !record.IsSplitter || source == null)
        {
            return;
        }

        BoxCollider sourceCollider = source.GetComponent<BoxCollider>();
        if (sourceCollider == null || !sourceCollider.enabled)
        {
            return;
        }

        BoxCollider collider = gameObject.AddComponent<BoxCollider>();
        Matrix4x4 worldMatrix = Matrix4x4.TRS(
            record.WorldPosition,
            record.WorldRotation,
            record.WorldScale);
        Matrix4x4 hostWorldToLocal = transform.worldToLocalMatrix;
        collider.center = hostWorldToLocal.MultiplyPoint3x4(
            worldMatrix.MultiplyPoint3x4(sourceCollider.center));
        Vector3 axisX = hostWorldToLocal.MultiplyVector(
            worldMatrix.MultiplyVector(Vector3.right * sourceCollider.size.x));
        Vector3 axisY = hostWorldToLocal.MultiplyVector(
            worldMatrix.MultiplyVector(Vector3.up * sourceCollider.size.y));
        Vector3 axisZ = hostWorldToLocal.MultiplyVector(
            worldMatrix.MultiplyVector(Vector3.forward * sourceCollider.size.z));
        collider.size = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        collider.isTrigger = sourceCollider.isTrigger;
        collider.sharedMaterial = sourceCollider.sharedMaterial;
        splitterCollidersByStorageKey[record.StorageKey] = collider;
    }

    private void RemoveSplitterCollider(Vector2Int storageKey)
    {
        if (!splitterCollidersByStorageKey.TryGetValue(storageKey, out BoxCollider collider))
        {
            return;
        }

        splitterCollidersByStorageKey.Remove(storageKey);
        DestroyRuntimeComponent(collider);
    }

    private static void DestroyRuntimeComponent(Component component)
    {
        if (component == null)
        {
            return;
        }

        if (component is Collider collider)
        {
            collider.enabled = false;
        }

        if (Application.isPlaying)
        {
            Destroy(component);
        }
        else
        {
            DestroyImmediate(component);
        }
    }

    private void AddCoordinateMapping(Vector2Int coordinate, ConveyorRuntimeRecord record)
    {
        if (!recordsByCoordinate.TryGetValue(coordinate, out List<ConveyorRuntimeRecord> records))
        {
            records = new List<ConveyorRuntimeRecord>(2);
            recordsByCoordinate.Add(coordinate, records);
        }

        if (!records.Contains(record))
        {
            records.Add(record);
        }
    }

    private void RemoveCoordinateMappings(ConveyorRuntimeRecord record)
    {
        List<Vector2Int> emptyCoordinates = null;
        foreach (KeyValuePair<Vector2Int, List<ConveyorRuntimeRecord>> pair in recordsByCoordinate)
        {
            pair.Value.Remove(record);
            if (pair.Value.Count == 0)
            {
                emptyCoordinates ??= new List<Vector2Int>();
                emptyCoordinates.Add(pair.Key);
            }
        }

        if (emptyCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < emptyCoordinates.Count; i++)
        {
            recordsByCoordinate.Remove(emptyCoordinates[i]);
        }
    }

    private static bool IsActiveRelativeToRoot(Transform transform, Transform root)
    {
        Transform currentTransform = transform;
        while (currentTransform != null)
        {
            if (!currentTransform.gameObject.activeSelf)
            {
                return false;
            }

            if (currentTransform == root)
            {
                return true;
            }

            currentTransform = currentTransform.parent;
        }

        return false;
    }

    private static Matrix4x4 CalculateLocalToRoot(Transform transform, Transform root)
    {
        Matrix4x4 result = Matrix4x4.identity;
        Transform currentTransform = transform;
        while (currentTransform != null && currentTransform != root)
        {
            result = Matrix4x4.TRS(
                         currentTransform.localPosition,
                         currentTransform.localRotation,
                         currentTransform.localScale)
                     * result;
            currentTransform = currentTransform.parent;
        }

        return result;
    }

    private static bool HasOddNegativeScale(Matrix4x4 matrix)
    {
        Vector3 xAxis = new Vector3(matrix.m00, matrix.m10, matrix.m20);
        Vector3 yAxis = new Vector3(matrix.m01, matrix.m11, matrix.m21);
        Vector3 zAxis = new Vector3(matrix.m02, matrix.m12, matrix.m22);
        return Vector3.Dot(Vector3.Cross(xAxis, yAxis), zAxis) < 0f;
    }

    int IVirtualRenderBatchOwner.BatchEntryCount => batchEntries.Count;

    public void UpdateBatchEntryMatrixIndex(int entryIndex, int matrixIndex)
    {
        if (entryIndex < 0 || entryIndex >= batchEntries.Count)
        {
            return;
        }

        VirtualRenderBatchEntry entry = batchEntries[entryIndex];
        entry.MatrixIndex = matrixIndex;
        batchEntries[entryIndex] = entry;
    }
}
