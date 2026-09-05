using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public partial class Spliterbelt : ConveyorBelt
{
    private static readonly Dictionary<Vector2Int, Spliterbelt> Coverage = new Dictionary<Vector2Int, Spliterbelt>();
    private static readonly EndpointConveyorLookup RuntimeEndpointLookup = LookupRuntimeEndpoint;
    private readonly List<Vector2Int> registeredCoordinates = new List<Vector2Int>(2);
    [SerializeField] private Transform splitterBody;
    private int leftWheelChannel;
    private int rightWheelChannel;

    protected new void Awake()
    {
        base.Awake();
        // Imported Wheel_L/Wheel_R names are opposite to the belt channel names.
        // Bind by physical position, independently of the wheel's authored tilt.
        leftWheelChannel = ResolveWheelChannel(LeftWheel);
        rightWheelChannel = ResolveWheelChannel(rightWheel);
        // Only the machine housing blocks walking; the two exposed belts remain walkable.
        MeshFilter bodyMesh = splitterBody != null ? splitterBody.GetComponentInChildren<MeshFilter>(true) : null;
        if (bodyMesh != null && bodyMesh.sharedMesh != null && bodyMesh.GetComponent<Collider>() == null)
        {
            BoxCollider collider = bodyMesh.gameObject.AddComponent<BoxCollider>();
            collider.center = bodyMesh.sharedMesh.bounds.center;
            collider.size = bodyMesh.sharedMesh.bounds.size;
        }
    }

    [SerializeField]
    private Transform LeftWheel, rightWheel;

    [SerializeField]
    private GameObject endStartObject_L, endStartObject_R;
    [SerializeField]
    private GameObject endEndObject_L;
    [SerializeField, FormerlySerializedAs("endEndObject_LR")]
    private GameObject endEndObject_R;
    [SerializeField]
    private GameObject seamStartObject_L, seamStartObject_R;
    [SerializeField]
    private GameObject seamEndObject_L, seamEndObject_R;

    public override bool CanSuspendRuntimeRoot => false;
    protected override bool RequiresNativeRendering(MeshRenderer renderer) => renderer != null;

    protected override bool IsEndpointVisualRoot(GameObject candidate, bool includeSeams)
    {
        // Both channels own their endpoint visibility, including all Seam Top children.
        return base.IsEndpointVisualRoot(candidate, includeSeams)
            || candidate == endStartObject_L || candidate == endStartObject_R
            || candidate == endEndObject_L || candidate == endEndObject_R
            || (includeSeams
                && (candidate == seamStartObject_L || candidate == seamStartObject_R
                    || candidate == seamEndObject_L || candidate == seamEndObject_R));
    }

    public static bool TryFindCoveringBelt(Vector2Int coordinate, out Spliterbelt belt)
    {
        return Coverage.TryGetValue(coordinate, out belt) && belt != null
            && belt.IsRuntimeRootAvailable && belt.registeredCoordinates.Contains(coordinate);
    }

    public static void ClearRuntimeCoverageLookup() => Coverage.Clear();

    public bool TryGetChannelCoordinate(int channel, out Vector2Int coordinate)
    {
        coordinate = default;
        return TryGetPlacementRuntime(out Vector2Int anchor, out _)
            && TryGetChannelCoordinate(channel, anchor, RuntimeOccupiedCoordinates, out coordinate);
    }

    private bool TryGetChannelCoordinate(int channel, Vector2Int anchor, IReadOnlyList<Vector2Int> cells,
        out Vector2Int coordinate)
    {
        coordinate = default;
        if (!TryGetOutputDirection(transform.rotation, out Vector2Int flow))
            return false;
        // Channel 0 follows Belt_L (-local X); rotation preserves the prefab's two channels.
        Vector2Int right = new Vector2Int(-flow.y, flow.x);
        if (cells == null || cells.Count != 2)
            return false;
        Vector2Int first = cells[0] - anchor;
        Vector2Int second = cells[1] - anchor;
        int firstProjection = first.x * right.x + first.y * right.y;
        int secondProjection = second.x * right.x + second.y * right.y;
        coordinate = channel == 0
            ? (firstProjection < secondProjection ? cells[0] : cells[1])
            : (firstProjection < secondProjection ? cells[1] : cells[0]);
        return true;
    }

    public int GetChannel(Vector2Int coordinate)
    {
        return TryGetChannel(coordinate, out int channel) ? channel : 1;
    }

    public bool TryGetChannel(Vector2Int coordinate, out int channel)
    {
        channel = -1;
        if (TryGetChannelCoordinate(0, out Vector2Int left) && left == coordinate)
        {
            channel = 0;
            return true;
        }
        if (TryGetChannelCoordinate(1, out Vector2Int right) && right == coordinate)
        {
            channel = 1;
            return true;
        }
        return false;
    }

    protected override void OnPlacementRuntimeChanged()
    {
        UnregisterCoverage();
        foreach (Vector2Int coordinate in RuntimeOccupiedCoordinates)
        {
            Coverage[coordinate] = this;
            registeredCoordinates.Add(coordinate);
        }
        base.OnPlacementRuntimeChanged();
        RefreshCoveredBlocks();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        UnregisterCoverage();
        base.OnPlacementRuntimeCleared();
    }

    protected override void OnDisable()
    {
        UnregisterCoverage();
        base.OnDisable();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (TryGetPlacementRuntime(out _, out _))
            OnPlacementRuntimeChanged();
    }

    private void UnregisterCoverage()
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        for (int i = 0; i < registeredCoordinates.Count; i++)
        {
            Vector2Int coordinate = registeredCoordinates[i];
            if (Coverage.TryGetValue(coordinate, out Spliterbelt owner) && ReferenceEquals(owner, this))
                Coverage.Remove(coordinate);
            if (terrain != null && terrain.TryGetLoadedBlock(coordinate, out Block block))
                block.InvalidateRuntimeConveyorTopology();
        }
        registeredCoordinates.Clear();
    }

    public void RefreshCoveredBlocks()
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null)
            return;
        for (int channel = 0; channel < 2; channel++)
        {
            if (TryGetChannelCoordinate(channel, out Vector2Int coordinate)
                && terrain.TryGetLoadedBlock(coordinate, out Block block))
                block.InvalidateRuntimeConveyorTopology();
        }
        terrain.MarkConveyorNetworkDirty();
        terrain.MarkConveyorLineCacheDirty();
    }

    public override void RefreshEndpointVisuals()
    {
        base.RefreshEndpointVisuals();
        if (TryGetPlacementRuntime(out Vector2Int anchor, out _))
            RefreshSplitterEndpoints(anchor, RuntimeOccupiedCoordinates, RuntimeEndpointLookup);
    }

    public override void RefreshEndpointVisualsForPreview(Vector2Int anchor,
        IReadOnlyList<Vector2Int> cells, EndpointConveyorLookup lookup)
    {
        RefreshSplitterEndpoints(anchor, cells, lookup);
    }

    public override void ClearEndpointVisualsForPreview()
    {
        SetChannelEndpoints(endStartObject_L, endEndObject_L, seamStartObject_L, seamEndObject_L, false, false);
        SetChannelEndpoints(endStartObject_R, endEndObject_R, seamStartObject_R, seamEndObject_R, false, false);
    }

    private void RefreshSplitterEndpoints(Vector2Int anchor, IReadOnlyList<Vector2Int> cells, EndpointConveyorLookup lookup)
    {
        RefreshChannelEndpoints(0, anchor, cells, lookup, endStartObject_L, endEndObject_L, seamStartObject_L, seamEndObject_L);
        RefreshChannelEndpoints(1, anchor, cells, lookup, endStartObject_R, endEndObject_R, seamStartObject_R, seamEndObject_R);
    }

    private void RefreshChannelEndpoints(int channel, Vector2Int anchor, IReadOnlyList<Vector2Int> cells,
        EndpointConveyorLookup lookup, GameObject inputEnd, GameObject outputEnd, GameObject inputSeam, GameObject outputSeam)
    {
        if (!TryGetChannelCoordinate(channel, anchor, cells, out Vector2Int coordinate)
            || !TryGetOutputDirection(transform.rotation, out Vector2Int flow))
            return;
        bool inputConnected = lookup != null && lookup(coordinate - flow, this, out ConveyorBelt input, out Quaternion inputRotation)
            && input.TryGetOutputDirection(inputRotation, out Vector2Int inputFlow) && inputFlow == flow;
        bool outputConnected = lookup != null && lookup(coordinate + flow, this, out ConveyorBelt output, out Quaternion outputRotation)
            && output.TryGetInputDirection(outputRotation, out Vector2Int outputInput) && outputInput == -flow;
        bool inputPerpendicular = HasPerpendicularBeltAtEndpoint(coordinate - flow, -flow, lookup);
        bool outputPerpendicular = HasPerpendicularBeltAtEndpoint(coordinate + flow, flow, lookup);
        SetChannelEndpoints(inputEnd, outputEnd, inputSeam, outputSeam,
            inputConnected, outputConnected, inputPerpendicular, outputPerpendicular);
    }

    private static bool LookupRuntimeEndpoint(Vector2Int coordinate, ConveyorBelt ignored,
        out ConveyorBelt belt, out Quaternion rotation)
    {
        bool found = TryGetConveyorBlockAtCoordinate(coordinate, out _, out belt) && belt != ignored;
        rotation = found ? belt.transform.rotation : Quaternion.identity;
        return found;
    }

    private static void SetChannelEndpoints(GameObject inputEnd, GameObject outputEnd,
        GameObject inputSeam, GameObject outputSeam, bool inputConnected, bool outputConnected,
        bool inputPerpendicular = false, bool outputPerpendicular = false)
    {
        SetEndpointVisualActive(inputEnd, !inputConnected && !inputPerpendicular);
        SetEndpointVisualActive(outputEnd, !outputConnected && !outputPerpendicular);
        SetEndpointVisualActive(inputSeam, inputPerpendicular);
        SetEndpointVisualActive(outputSeam, outputPerpendicular);
    }

    private int ResolveWheelChannel(Transform wheel)
    {
        return wheel != null && transform.InverseTransformPoint(wheel.position).x >= 0f ? 1 : 0;
    }

    private float WheelAnimationTime => Time.time;

    protected override bool UsesManagedVisualUpdates => true;

    protected override void TickManagedVisuals(float deltaTime)
    {
        if (ConveyorSpeed <= 0f || !TryGetPlacementRuntime(out _, out _))
            return;
        int activeChannels = GetDisplayedWheelRotationMask(WheelAnimationTime);
        if (activeChannels == 0)
            return;
        float angle = ConveyorSpeed * 180f * deltaTime;
        RotateWheel(LeftWheel, leftWheelChannel, activeChannels, angle);
        RotateWheel(rightWheel, rightWheelChannel, activeChannels, angle);
    }

    private void RotateWheel(Transform wheel, int channel, int activeChannels, float angle)
    {
        if (wheel == null || (activeChannels & (1 << channel)) == 0)
            return;
        // CartWheel spins around local X. The prefab mounts it at -90 degrees on Z,
        // which aligns that axle with the splitter's Y axis.
        wheel.Rotate(Vector3.right, channel == 0 ? angle : -angle, Space.Self);
    }
}
