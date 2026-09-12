using System.Collections.Generic;
using ProjectF.Conveyors;
using UnityEngine;

public class ConvayorBelt2F : ConveyorBelt
{
    private const int ObjectInfoSlotCount = 6;
    private const int DefaultFootprintWidth = 1;
    private const int DefaultFootprintLength = 3;
    private const float DefaultPathHalfLength = 1.5f;
    private const float DefaultPathHighHalfLength = 0.5f;
    private const float DefaultPathLowHeight = 0.13f;
    private const float DefaultPathHighHeight = 0.806f;
    private const float DefaultVisualHalfLength = 1.5f;
    private const float DefaultSideBarrierHalfLength = 1.13f;
    private const bool PathUsesLocalX = true;
    private const float SlotLongitudinalOffset = 0.25f;
    private const float PathSlopeItemPitchDegrees = 34.0587f;
    private const float PathSlopeRotationEpsilon = 0.0001f;

    private static readonly List<ConvayorBelt2F> ActiveBelts = new List<ConvayorBelt2F>();
    private static readonly Dictionary<Vector2Int, ConvayorBelt2F> CoverageByCoordinate = new Dictionary<Vector2Int, ConvayorBelt2F>();
    private static bool coverageLookupDirty = true;
    private static readonly Vector2Int[] RefreshNeighborDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!ActiveBelts.Contains(this))
        {
            ActiveBelts.Add(this);
        }

        MarkCoverageDirty();
        if (Application.isPlaying)
        {
            RefreshCoveredConveyorTopology();
        }
    }

    protected override void OnDisable()
    {
        if (!IsRuntimeRootSuspended)
        {
            ActiveBelts.Remove(this);
        }

        MarkCoverageDirty();
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        ActiveBelts.Remove(this);
        MarkCoverageDirty();
        base.PrepareForPool();
    }

    public static void MarkCoverageDirty()
    {
        coverageLookupDirty = true;
    }

    public static void ClearRuntimeCoverageLookup()
    {
        ActiveBelts.Clear();
        CoverageByCoordinate.Clear();
        coverageLookupDirty = true;
    }

    public static bool TryFindCoveringBelt(Vector2Int coordinate, out ConvayorBelt2F belt)
    {
        if (coverageLookupDirty)
        {
            RebuildCoverageLookup();
        }

        if (CoverageByCoordinate.TryGetValue(coordinate, out belt)
            && IsValidRegisteredBelt(belt)
            && belt.CoversCoordinate(coordinate))
        {
            return true;
        }

        if (belt != null)
        {
            MarkCoverageDirty();
            RebuildCoverageLookup();
            return CoverageByCoordinate.TryGetValue(coordinate, out belt)
                   && IsValidRegisteredBelt(belt)
                   && belt.CoversCoordinate(coordinate);
        }

        belt = null;
        return false;
    }

    public void RefreshCoveredConveyorTopology()
    {
        MarkCoverageDirty();
        TerrainGenerator terrain = TerrainGenerator.Active != null
            ? TerrainGenerator.Active
            : GetComponentInParent<TerrainGenerator>();
        if (terrain == null)
        {
            return;
        }

        HashSet<Vector2Int> coordinatesToRefresh = new HashSet<Vector2Int>();
        AddCoverageCoordinates(coordinatesToRefresh);
        if (coordinatesToRefresh.Count == 0)
        {
            return;
        }

        List<Vector2Int> coveredCoordinates = new List<Vector2Int>(coordinatesToRefresh);
        for (int i = 0; i < coveredCoordinates.Count; i++)
        {
            Vector2Int coordinate = coveredCoordinates[i];
            for (int directionIndex = 0; directionIndex < RefreshNeighborDirections.Length; directionIndex++)
            {
                coordinatesToRefresh.Add(coordinate + RefreshNeighborDirections[directionIndex]);
            }
        }

        foreach (Vector2Int coordinate in coordinatesToRefresh)
        {
            if (terrain.TryGetLoadedBlock(coordinate, out Block block) && block != null)
            {
                block.InvalidateRuntimeConveyorTopology();
            }
        }

        terrain.MarkConveyorNetworkDirty();
    }

    private static void RebuildCoverageLookup()
    {
        coverageLookupDirty = false;
        CoverageByCoordinate.Clear();
        for (int i = ActiveBelts.Count - 1; i >= 0; i--)
        {
            ConvayorBelt2F candidate = ActiveBelts[i];
            if (candidate == null)
            {
                ActiveBelts.RemoveAt(i);
                continue;
            }

            if (!IsValidRegisteredBelt(candidate))
            {
                continue;
            }

            candidate.AddCoverageCoordinatesToLookup();
        }
    }

    private static bool IsValidRegisteredBelt(ConvayorBelt2F belt)
    {
        return belt != null
               && belt.gameObject != null
               && (belt.enabled || belt.IsRuntimeRootSuspended)
               && belt.IsRuntimeRootAvailable;
    }

    public bool CoversCoordinate(Vector2Int coordinate)
    {
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        Vector2Int size = GetFootprintSize();
        Vector2Int anchorCell = GetAnchorCell(size);
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                Vector2Int localOffset = new Vector2Int(x - anchorCell.x, y - anchorCell.y);
                if (anchorCoordinate + RotateFootprintOffset(localOffset, quarterTurns) == coordinate)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool IsBridgeCenterCoordinate(Vector2Int coordinate)
    {
        return TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
               && anchorCoordinate == coordinate;
    }

    public bool IsInputEdgeCoordinate(Vector2Int coordinate)
    {
        return TryGetInputDirection(transform.rotation, out Vector2Int inputDirection)
               && IsFootprintEdgeCoordinate(coordinate, inputDirection);
    }

    public bool IsOutputEdgeCoordinate(Vector2Int coordinate)
    {
        return TryGetOutputDirection(transform.rotation, out Vector2Int outputDirection)
               && IsFootprintEdgeCoordinate(coordinate, outputDirection);
    }

    private bool IsFootprintEdgeCoordinate(Vector2Int coordinate, Vector2Int outwardDirection)
    {
        return outwardDirection != Vector2Int.zero
               && CoversCoordinate(coordinate)
               && !CoversCoordinate(coordinate + outwardDirection);
    }

    public Vector3 ApplyPathHeight(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        localPosition = ConveyorBelt2FPath.ConformItemPosition(
            localPosition,
            PathUsesLocalX,
            DefaultPathHalfLength,
            DefaultPathHighHalfLength,
            DefaultPathLowHeight,
            DefaultPathHighHeight);
        return transform.TransformPoint(localPosition);
    }

    public void GetPlayerSideBarrierEndpoints(out Vector3 start, out Vector3 end)
    {
        Vector3 localStart = Vector3.zero;
        Vector3 localEnd = Vector3.zero;
        SetPathCoordinate(ref localStart, -DefaultSideBarrierHalfLength);
        SetPathCoordinate(ref localEnd, DefaultSideBarrierHalfLength);
        start = transform.TransformPoint(localStart);
        end = transform.TransformPoint(localEnd);
    }

    public Quaternion ResolvePathItemRotation(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        float pitchDegrees = ResolvePathItemPitch(GetPathCoordinate(localPosition));
        if (Mathf.Abs(pitchDegrees) <= PathSlopeRotationEpsilon)
        {
            return Quaternion.identity;
        }

        Vector3 localTiltAxis = PathUsesLocalX ? Vector3.forward : Vector3.right;
        float tiltDegrees = PathUsesLocalX ? -pitchDegrees : pitchDegrees;
        Vector3 worldTiltAxis = transform.TransformDirection(localTiltAxis);
        return Quaternion.AngleAxis(tiltDegrees, worldTiltAxis);
    }

    public bool IsUpperPathWorldPosition(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        return localPosition.y >= (DefaultPathLowHeight + DefaultPathHighHeight) * 0.5f;
    }

    public bool TryGetBridgePeakWorldPosition(out Vector3 worldPosition)
    {
        worldPosition = transform.TransformPoint(new Vector3(0f, DefaultPathHighHeight, 0f));
        return true;
    }

    internal float GetVisualSurfaceLength()
    {
        float landingLength = DefaultVisualHalfLength - DefaultPathHalfLength;
        float slopeRun = DefaultPathHalfLength - DefaultPathHighHalfLength;
        float slopeRise = DefaultPathHighHeight - DefaultPathLowHeight;
        float slopeLength = Mathf.Sqrt((slopeRun * slopeRun) + (slopeRise * slopeRise));
        return (landingLength * 2f) + (slopeLength * 2f) + (DefaultPathHighHalfLength * 2f);
    }

    public bool TryGetLaneWorldPosition(
        Vector2Int coordinate,
        int laneIndex,
        Vector3 fallbackWorldPosition,
        out Vector3 worldPosition)
    {
        worldPosition = fallbackWorldPosition;
        if (!CoversCoordinate(coordinate))
        {
            return false;
        }

        Vector3 localPosition = transform.InverseTransformPoint(fallbackWorldPosition);
        if (TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
            && TryGetOutputDirection(transform.rotation, out Vector2Int outputDirection)
            && outputDirection != Vector2Int.zero)
        {
            Vector2Int relativeCoordinate = coordinate - anchorCoordinate;
            int longitudinalStep =
                relativeCoordinate.x * outputDirection.x
                + relativeCoordinate.y * outputDirection.y;
            bool isFrontLane = laneIndex == 0 || laneIndex == 1;
            bool isBackLane = laneIndex == 2 || laneIndex == 3;
            float slotOffset = isBackLane
                ? SlotLongitudinalOffset
                : isFrontLane ? -SlotLongitudinalOffset : 0f;

            SetPathLateralCoordinate(ref localPosition, 0f);
            SetPathCoordinate(
                ref localPosition,
                Mathf.Clamp(
                -longitudinalStep + slotOffset,
                -DefaultPathHalfLength,
                DefaultPathHalfLength));
        }

        localPosition = ConveyorBelt2FPath.ConformItemPosition(
            localPosition,
            PathUsesLocalX,
            DefaultPathHalfLength,
            DefaultPathHighHalfLength,
            DefaultPathLowHeight,
            DefaultPathHighHeight);

        worldPosition = transform.TransformPoint(localPosition);
        return true;
    }

    public override void CopyObjectInfoItemIds(List<int> results, int maxCount)
    {
        if (results == null || maxCount <= 0)
        {
            return;
        }

        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null)
        {
            return;
        }

        List<Vector2Int> coordinates = new List<Vector2Int>(ObjectInfoSlotCount / 2);
        CopyCoverageCoordinates(coordinates);
        for (int i = 0; i < coordinates.Count && results.Count < maxCount; i++)
        {
            if (!terrain.TryGetLoadedBlock(coordinates[i], out Block block) || block == null)
            {
                AppendEmptyObjectInfoSlots(results, maxCount, 2);
                continue;
            }

            IReadOnlyList<int> laneIndices = ShouldUseBridgeObjectInfoLanes(block, coordinates[i])
                ? ObjectInfoBridgeLaneIndices
                : ObjectInfoMainLaneIndices;
            AppendObjectInfoLaneItemIds(results, maxCount, block, laneIndices);
        }

        while (results.Count < Mathf.Min(maxCount, ObjectInfoSlotCount))
        {
            results.Add(-1);
        }
    }

    private bool ShouldUseBridgeObjectInfoLanes(Block block, Vector2Int coordinate)
    {
        return block != null
               && IsBridgeCenterCoordinate(coordinate)
               && block.MapObject is ConveyorBelt mappedConveyor
               && !(mappedConveyor is ConvayorBelt2F);
    }

    private static void AppendEmptyObjectInfoSlots(List<int> results, int maxCount, int count)
    {
        for (int i = 0; i < count && results.Count < maxCount; i++)
        {
            results.Add(-1);
        }
    }

    private float ResolvePathItemPitch(float localPathCoordinate)
    {
        float absoluteCoordinate = Mathf.Abs(localPathCoordinate);
        if (absoluteCoordinate <= DefaultPathHighHalfLength + PathSlopeRotationEpsilon
            || absoluteCoordinate > DefaultPathHalfLength + PathSlopeRotationEpsilon)
        {
            return 0f;
        }

        return localPathCoordinate > 0f ? PathSlopeItemPitchDegrees : -PathSlopeItemPitchDegrees;
    }

    private void AddCoverageCoordinatesToLookup()
    {
        HashSet<Vector2Int> coordinates = new HashSet<Vector2Int>();
        AddCoverageCoordinates(coordinates);
        foreach (Vector2Int coordinate in coordinates)
        {
            if (!CoverageByCoordinate.TryGetValue(coordinate, out ConvayorBelt2F existing)
                || existing == null
                || RuntimePlacementSequence >= existing.RuntimePlacementSequence)
            {
                CoverageByCoordinate[coordinate] = this;
            }
        }
    }

    private void AddCoverageCoordinates(HashSet<Vector2Int> coordinates)
    {
        if (coordinates == null
            || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return;
        }

        Vector2Int size = GetFootprintSize();
        Vector2Int anchorCell = GetAnchorCell(size);
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                Vector2Int localOffset = new Vector2Int(x - anchorCell.x, y - anchorCell.y);
                coordinates.Add(anchorCoordinate + RotateFootprintOffset(localOffset, quarterTurns));
            }
        }
    }

    private void CopyCoverageCoordinates(List<Vector2Int> coordinates)
    {
        if (coordinates == null
            || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return;
        }

        Vector2Int size = GetFootprintSize();
        Vector2Int anchorCell = GetAnchorCell(size);
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                Vector2Int localOffset = new Vector2Int(x - anchorCell.x, y - anchorCell.y);
                Vector2Int coordinate = anchorCoordinate + RotateFootprintOffset(localOffset, quarterTurns);
                if (!coordinates.Contains(coordinate))
                {
                    coordinates.Add(coordinate);
                }
            }
        }
    }

    private Vector2Int GetFootprintSize()
    {
        int sizeX = Mathf.Max(1, Status.mapSizeX);
        int sizeY = Mathf.Max(1, Status.mapSizeY);
        if (sizeX == 1 && sizeY == 1)
        {
            sizeX = DefaultFootprintWidth;
            sizeY = DefaultFootprintLength;
        }

        return new Vector2Int(sizeX, sizeY);
    }

    private float GetPathCoordinate(Vector3 localPosition)
    {
        return PathUsesLocalX ? localPosition.x : localPosition.z;
    }

    private void SetPathCoordinate(ref Vector3 localPosition, float value)
    {
        if (PathUsesLocalX)
        {
            localPosition.x = value;
            return;
        }

        localPosition.z = value;
    }

    private void SetPathLateralCoordinate(ref Vector3 localPosition, float value)
    {
        if (PathUsesLocalX)
        {
            localPosition.z = value;
            return;
        }

        localPosition.x = value;
    }

    private Vector2Int GetAnchorCell(Vector2Int size)
    {
        Vector2Int centerCell = PlacementCenterCell;
        return new Vector2Int(
            Mathf.Clamp(centerCell.x, 0, size.x - 1),
            Mathf.Clamp(centerCell.y, 0, size.y - 1));
    }

    private static Vector2Int RotateFootprintOffset(Vector2Int offset, int quarterTurns)
    {
        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        return normalizedQuarterTurns switch
        {
            1 => new Vector2Int(offset.y, -offset.x),
            2 => new Vector2Int(-offset.x, -offset.y),
            3 => new Vector2Int(-offset.y, offset.x),
            _ => offset
        };
    }

}
