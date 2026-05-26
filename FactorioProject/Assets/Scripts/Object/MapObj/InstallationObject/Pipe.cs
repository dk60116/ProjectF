using System.Collections.Generic;
using UnityEngine;

public enum PipeVariantKind
{
    Straight = 0,
    Corner = 1,
    Tee = 2,
    Cross = 3
}

public class Pipe : InstallationObject
{
    private const int MaxObjectInfoFluidSearchNodes = 256;
    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    [SerializeField]
    private Pipe straightVariantPrefab;
    [SerializeField]
    private Pipe cornerVariantPrefab;
    [SerializeField]
    private Pipe teeVariantPrefab;
    [SerializeField]
    private Pipe crossVariantPrefab;
    [SerializeField]
    private PipeVariantKind variantKind = PipeVariantKind.Straight;
    [SerializeField]
    private InstallationFacingDirection localStraightDirection = InstallationFacingDirection.PositiveZ;
    [SerializeField]
    private InstallationFacingDirection localCornerFirstDirection = InstallationFacingDirection.NegativeX;
    [SerializeField]
    private InstallationFacingDirection localCornerSecondDirection = InstallationFacingDirection.NegativeZ;
    [SerializeField]
    private InstallationFacingDirection localTeeFirstDirection = InstallationFacingDirection.NegativeX;
    [SerializeField]
    private InstallationFacingDirection localTeeSecondDirection = InstallationFacingDirection.PositiveX;
    [SerializeField]
    private InstallationFacingDirection localTeeThirdDirection = InstallationFacingDirection.NegativeZ;

    private readonly Queue<Vector2Int> objectInfoFluidSearchQueue = new Queue<Vector2Int>(32);
    private readonly HashSet<Vector2Int> objectInfoFluidSearchVisited = new HashSet<Vector2Int>();
    private readonly HashSet<int> objectInfoFluidItemIds = new HashSet<int>();

    public Pipe StraightVariantPrefab => straightVariantPrefab != null ? straightVariantPrefab : this;
    public Pipe CornerVariantPrefab => cornerVariantPrefab;
    public Pipe TeeVariantPrefab => teeVariantPrefab;
    public Pipe CrossVariantPrefab => crossVariantPrefab;
    public PipeVariantKind VariantKind => variantKind;
    public int VariantKindId => (int)variantKind;
    public bool IsCornerVariant => variantKind == PipeVariantKind.Corner;
    public bool IsTeeVariant => variantKind == PipeVariantKind.Tee;
    public bool IsCrossVariant => variantKind == PipeVariantKind.Cross;

    public bool TryGetObjectInfoFluidItemId(out int fluidItemId)
    {
        return TryGetObjectInfoFluidInfo(out fluidItemId, out _);
    }

    public bool TryGetObjectInfoFluidInfo(out int fluidItemId, out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        if (!TryResolveObjectInfoPipeCoordinate(out Vector2Int startCoordinate))
        {
            return false;
        }

        TerrainGenerator terrain = TerrainGenerator.Active;
        objectInfoFluidSearchQueue.Clear();
        objectInfoFluidSearchVisited.Clear();
        EnqueueObjectInfoFluidSearchCoordinate(startCoordinate);

        int searchedNodeCount = 0;
        while (objectInfoFluidSearchQueue.Count > 0
               && searchedNodeCount < MaxObjectInfoFluidSearchNodes)
        {
            Vector2Int coordinate = objectInfoFluidSearchQueue.Dequeue();
            searchedNodeCount++;

            if (TryGetFluidInfoAtPipeNetworkCoordinate(coordinate, out fluidItemId, out temperatureCelsius))
            {
                return true;
            }

            Pipe pipe = null;
            Quaternion pipeRotation = Quaternion.identity;
            bool hasPipe = coordinate == startCoordinate
                ? TryResolveObjectInfoPipeAtStartCoordinate(startCoordinate, out pipe, out pipeRotation)
                : TryGetPipeAtCoordinate(terrain, coordinate, out pipe, out pipeRotation);
            if (!hasPipe || pipe == null)
            {
                continue;
            }

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int direction = CardinalDirections[i];
                if (!pipe.HasConnectionTowards(pipeRotation, direction))
                {
                    continue;
                }

                Vector2Int neighborCoordinate = coordinate + direction;
                if (TryGetFluidInfoAtPipeNetworkCoordinate(neighborCoordinate, out fluidItemId, out temperatureCelsius))
                {
                    return true;
                }

                if (TryGetPipeAtCoordinate(terrain, neighborCoordinate, out Pipe neighborPipe, out Quaternion neighborRotation)
                    && neighborPipe.HasConnectionTowards(neighborRotation, -direction))
                {
                    EnqueueObjectInfoFluidSearchCoordinate(neighborCoordinate);
                }
            }
        }

        return false;
    }

    public bool HasConnectionTowards(Quaternion rotation, Vector2Int direction)
    {
        if (direction == Vector2Int.zero)
        {
            return false;
        }

        switch (variantKind)
        {
            case PipeVariantKind.Cross:
                return direction == Vector2Int.up
                       || direction == Vector2Int.right
                       || direction == Vector2Int.down
                       || direction == Vector2Int.left;
            case PipeVariantKind.Tee:
                return HasResolvedConnection(rotation, localTeeFirstDirection, direction)
                       || HasResolvedConnection(rotation, localTeeSecondDirection, direction)
                       || HasResolvedConnection(rotation, localTeeThirdDirection, direction);
            case PipeVariantKind.Corner:
                return HasResolvedConnection(rotation, localCornerFirstDirection, direction)
                       || HasResolvedConnection(rotation, localCornerSecondDirection, direction);
            default:
                return HasResolvedConnection(rotation, localStraightDirection, direction)
                       || HasResolvedConnection(rotation, OppositeFacingDirection(localStraightDirection), direction);
        }
    }

    private bool TryResolveObjectInfoPipeCoordinate(out Vector2Int coordinate)
    {
        if (TryGetPlacementRuntime(out coordinate, out _))
        {
            return true;
        }

        Block block = GetComponentInParent<Block>();
        if (block != null)
        {
            coordinate = block.Coordinate;
            return true;
        }

        coordinate = Vector2Int.zero;
        return false;
    }

    private bool TryResolveObjectInfoPipeAtStartCoordinate(
        Vector2Int coordinate,
        out Pipe pipe,
        out Quaternion pipeRotation)
    {
        if (TryGetPipeAtCoordinate(TerrainGenerator.Active, coordinate, out pipe, out pipeRotation))
        {
            return true;
        }

        pipe = this;
        pipeRotation = transform.rotation;
        return true;
    }

    private void EnqueueObjectInfoFluidSearchCoordinate(Vector2Int coordinate)
    {
        if (objectInfoFluidSearchVisited.Add(coordinate))
        {
            objectInfoFluidSearchQueue.Enqueue(coordinate);
        }
    }

    private bool TryGetFluidItemIdAtPipeNetworkCoordinate(Vector2Int coordinate, out int fluidItemId)
    {
        return TryGetFluidInfoAtPipeNetworkCoordinate(coordinate, out fluidItemId, out _);
    }

    private bool TryGetFluidInfoAtPipeNetworkCoordinate(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        return TryGetFluidOutputInfoAtCoordinate(coordinate, out fluidItemId, out temperatureCelsius)
               || TryGetStoredFluidInfoAtCoordinate(coordinate, out fluidItemId, out temperatureCelsius);
    }

    private bool TryGetFluidOutputItemIdAtCoordinate(Vector2Int coordinate, out int fluidItemId)
    {
        return TryGetFluidOutputInfoAtCoordinate(coordinate, out fluidItemId, out _);
    }

    private bool TryGetFluidOutputInfoAtCoordinate(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        if (InputOutputModule.TryGetFluidOutputInfoAtRuntimeGridCoordinate(
                coordinate,
                out fluidItemId,
                out temperatureCelsius))
        {
            return true;
        }

        objectInfoFluidItemIds.Clear();
        if (InputOutputModule.TryGetOutputItemIdsAtRuntimeGridCoordinate(
                coordinate,
                objectInfoFluidItemIds))
        {
            foreach (int itemId in objectInfoFluidItemIds)
            {
                if (!InputOutputModule.IsFluidItemId(itemId))
                {
                    continue;
                }

                fluidItemId = itemId;
                objectInfoFluidItemIds.Clear();
                return true;
            }
        }

        objectInfoFluidItemIds.Clear();
        return false;
    }

    private static bool TryGetStoredFluidItemIdAtCoordinate(Vector2Int coordinate, out int fluidItemId)
    {
        return TryGetStoredFluidInfoAtCoordinate(coordinate, out fluidItemId, out _);
    }

    private static bool TryGetStoredFluidInfoAtCoordinate(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        if (InputOutputModule.TryGetRuntimePipeFluidStorageAtCoordinate(
                coordinate,
                null,
                false,
                out InstallationObject areaStorage)
            && areaStorage != null
            && areaStorage.StoredFluidItemId >= 0)
        {
            fluidItemId = areaStorage.StoredFluidItemId;
            temperatureCelsius = areaStorage.GetStoredFluidTemperatureCelsius(fluidItemId);
            return true;
        }

        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is InstallationObject bodyStorage)
            || bodyStorage is Pipe
            || bodyStorage.StoredFluidItemId < 0)
        {
            return false;
        }

        fluidItemId = bodyStorage.StoredFluidItemId;
        temperatureCelsius = bodyStorage.GetStoredFluidTemperatureCelsius(fluidItemId);
        return true;
    }

    private static bool TryGetPipeAtCoordinate(
        TerrainGenerator terrain,
        Vector2Int coordinate,
        out Pipe pipe,
        out Quaternion pipeRotation)
    {
        pipe = null;
        pipeRotation = Quaternion.identity;
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is Pipe candidatePipe)
            || !candidatePipe.gameObject.activeInHierarchy)
        {
            return false;
        }

        pipe = candidatePipe;
        pipeRotation = candidatePipe.transform.rotation;
        return true;
    }

    private static bool HasResolvedConnection(
        Quaternion rotation,
        InstallationFacingDirection localDirection,
        Vector2Int direction)
    {
        return TryResolveDirection(rotation, localDirection, out Vector2Int resolvedDirection)
               && resolvedDirection == direction;
    }

    private static InstallationFacingDirection OppositeFacingDirection(InstallationFacingDirection direction)
    {
        switch (direction)
        {
            case InstallationFacingDirection.PositiveX:
                return InstallationFacingDirection.NegativeX;
            case InstallationFacingDirection.NegativeX:
                return InstallationFacingDirection.PositiveX;
            case InstallationFacingDirection.NegativeZ:
                return InstallationFacingDirection.PositiveZ;
            default:
                return InstallationFacingDirection.NegativeZ;
        }
    }

    private static bool TryResolveDirection(
        Quaternion rotation,
        InstallationFacingDirection localDirection,
        out Vector2Int resolvedDirection)
    {
        return TryResolveCardinalDirection(rotation * FacingDirectionToVector(localDirection), out resolvedDirection);
    }

    private static bool TryResolveCardinalDirection(Vector3 directionVector, out Vector2Int resolvedDirection)
    {
        resolvedDirection = Vector2Int.zero;
        directionVector.y = 0f;
        if (directionVector.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        directionVector.Normalize();
        if (Mathf.Abs(directionVector.x) >= Mathf.Abs(directionVector.z))
        {
            resolvedDirection = new Vector2Int(directionVector.x >= 0f ? 1 : -1, 0);
        }
        else
        {
            resolvedDirection = new Vector2Int(0, directionVector.z >= 0f ? 1 : -1);
        }

        return true;
    }

    private static Vector3 FacingDirectionToVector(InstallationFacingDirection direction)
    {
        switch (direction)
        {
            case InstallationFacingDirection.PositiveX:
                return Vector3.right;
            case InstallationFacingDirection.NegativeX:
                return Vector3.left;
            case InstallationFacingDirection.NegativeZ:
                return Vector3.back;
            default:
                return Vector3.forward;
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (variantKind == PipeVariantKind.Straight && straightVariantPrefab == null)
        {
            straightVariantPrefab = this;
        }
    }
#endif
}
