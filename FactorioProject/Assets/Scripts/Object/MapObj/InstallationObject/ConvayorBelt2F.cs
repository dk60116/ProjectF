using System.Collections.Generic;
using UnityEngine;

public class ConvayorBelt2F : ConveyorBelt
{
    private const int ObjectInfoSlotCount = 6;
    private const int DefaultFootprintWidth = 1;
    private const int DefaultFootprintLength = 3;
    private const float DefaultPathHalfLength = 1.33f;
    private const float DefaultPathHighHalfLength = 0.5f;
    private const float DefaultPathLowHeight = 0.2f;
    private const float DefaultPathHighHeight = 0.876f;
    private const float PathItemVerticalOffset = 0.2f;
    private const float SlotLongitudinalOffset = 0.25f;
    private const float PathSlopeItemPitchDegrees = 45f;
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

    private bool pathMetricsDirty = true;
    private float pathHalfLength = DefaultPathHalfLength;
    private float pathHighHalfLength = DefaultPathHighHalfLength;
    private float pathLowHeight = DefaultPathLowHeight;
    private float pathHighHeight = DefaultPathHighHeight;
    private bool pathUsesLocalX;

    protected override void OnEnable()
    {
        base.OnEnable();
        pathMetricsDirty = true;
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
        ActiveBelts.Remove(this);
        MarkCoverageDirty();
        base.OnDisable();
    }

    public static void MarkCoverageDirty()
    {
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
        terrain.MarkConveyorLineCacheDirty();
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
               && belt.isActiveAndEnabled
               && belt.gameObject != null
               && belt.gameObject.activeInHierarchy;
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

    public Vector3 ApplyPathHeight(Vector3 worldPosition)
    {
        RefreshPathMetrics();
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        localPosition.y = ResolvePathHeight(GetPathCoordinate(localPosition));
        return transform.TransformPoint(localPosition);
    }

    public Quaternion ResolvePathItemRotation(Vector3 worldPosition)
    {
        RefreshPathMetrics();
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        float pitchDegrees = ResolvePathItemPitch(GetPathCoordinate(localPosition));
        Quaternion localPitchRotation = pathUsesLocalX
            ? Quaternion.Euler(0f, 0f, -pitchDegrees)
            : Quaternion.Euler(pitchDegrees, 0f, 0f);
        return transform.rotation * localPitchRotation;
    }

    public bool IsUpperPathWorldPosition(Vector3 worldPosition)
    {
        RefreshPathMetrics();
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        return localPosition.y >= (pathLowHeight + pathHighHeight) * 0.5f;
    }

    public bool TryGetBridgePeakWorldPosition(out Vector3 worldPosition)
    {
        RefreshPathMetrics();
        worldPosition = transform.TransformPoint(new Vector3(0f, pathHighHeight, 0f));
        return true;
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

        RefreshPathMetrics();
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
                -pathHalfLength,
                pathHalfLength));
        }

        localPosition.y = ResolvePathHeight(GetPathCoordinate(localPosition));

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

    private float ResolvePathHeight(float localZ)
    {
        float absoluteZ = Mathf.Abs(localZ);
        if (absoluteZ <= pathHighHalfLength)
        {
            return pathHighHeight;
        }

        float slope01 = Mathf.InverseLerp(
            pathHighHalfLength,
            Mathf.Max(pathHalfLength, pathHighHalfLength + 0.0001f),
            absoluteZ);
        return Mathf.Lerp(pathHighHeight, pathLowHeight, slope01);
    }

    private float ResolvePathItemPitch(float localZ)
    {
        float absoluteZ = Mathf.Abs(localZ);
        if (absoluteZ <= pathHighHalfLength + PathSlopeRotationEpsilon
            || absoluteZ > pathHalfLength + PathSlopeRotationEpsilon)
        {
            return 0f;
        }

        return localZ > 0f ? PathSlopeItemPitchDegrees : -PathSlopeItemPitchDegrees;
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

    private void RefreshPathMetrics()
    {
        if (!pathMetricsDirty)
        {
            return;
        }

        pathMetricsDirty = false;
        pathHalfLength = DefaultPathHalfLength;
        pathHighHalfLength = DefaultPathHighHalfLength;
        pathLowHeight = DefaultPathLowHeight;
        pathHighHeight = DefaultPathHighHeight;

        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        int lowBodyCount = 0;
        bool foundHighBody = false;
        float lowBodyHeight = 0f;
        float highBodyHeight = 0f;
        float halfLength = 0f;
        float maxBodyAbsX = 0f;
        float maxBodyAbsZ = 0f;

        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform child = childTransforms[i];
            if (child == null || child == transform)
            {
                continue;
            }

            if (child.name == "Body_Start"
                || child.name == "Body_End"
                || child.name == "Body_Up"
                || child.name == "Body_Down"
                || child.name == "Body")
            {
                Vector3 localPosition = child.localPosition;
                maxBodyAbsX = Mathf.Max(maxBodyAbsX, Mathf.Abs(localPosition.x));
                maxBodyAbsZ = Mathf.Max(maxBodyAbsZ, Mathf.Abs(localPosition.z));
            }
        }

        pathUsesLocalX = maxBodyAbsX > maxBodyAbsZ + 0.0001f;
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform child = childTransforms[i];
            if (child == null || child == transform)
            {
                continue;
            }

            if (child.name == "Body_Start" || child.name == "Body_End")
            {
                Vector3 localPosition = child.localPosition;
                lowBodyHeight += localPosition.y;
                halfLength = Mathf.Max(halfLength, Mathf.Abs(GetPathCoordinate(localPosition)));
                lowBodyCount++;
                continue;
            }

            if (child.name == "Body")
            {
                highBodyHeight = child.localPosition.y;
                pathHighHalfLength = Mathf.Max(0.0001f, Mathf.Abs(child.localScale.z) * 0.5f);
                foundHighBody = true;
            }
        }

        if (lowBodyCount > 0)
        {
            lowBodyHeight /= lowBodyCount;
            pathLowHeight = lowBodyHeight + PathItemVerticalOffset;
        }

        if (foundHighBody)
        {
            pathHighHeight = highBodyHeight + PathItemVerticalOffset;
        }

        if (halfLength > 0.0001f)
        {
            pathHalfLength = halfLength;
        }
    }

    private float GetPathCoordinate(Vector3 localPosition)
    {
        return pathUsesLocalX ? localPosition.x : localPosition.z;
    }

    private void SetPathCoordinate(ref Vector3 localPosition, float value)
    {
        if (pathUsesLocalX)
        {
            localPosition.x = value;
            return;
        }

        localPosition.z = value;
    }

    private void SetPathLateralCoordinate(ref Vector3 localPosition, float value)
    {
        if (pathUsesLocalX)
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

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        pathMetricsDirty = true;
    }
#endif
}
