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
    private const float FluidDisplayRefreshIntervalSeconds = 0.2f;
    private const int NoDisplayedFluidItemId = -2;
    private static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorShaderId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorShaderId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color UnknownFluidDisplayColor = Color.white;
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

    [SerializeField]
    private MeshRenderer fluidDP;

    private MaterialPropertyBlock fluidDisplayPropertyBlock;
    private int displayedFluidItemId = NoDisplayedFluidItemId;
    private bool displayedFluidVisible;
    private bool fluidDisplayRendererResolved;
    private float nextFluidDisplayRefreshTime;

    public Pipe StraightVariantPrefab => straightVariantPrefab != null ? straightVariantPrefab : this;
    public Pipe CornerVariantPrefab => cornerVariantPrefab;
    public Pipe TeeVariantPrefab => teeVariantPrefab;
    public Pipe CrossVariantPrefab => crossVariantPrefab;
    public PipeVariantKind VariantKind => variantKind;
    public int VariantKindId => (int)variantKind;
    public bool IsCornerVariant => variantKind == PipeVariantKind.Corner;
    public bool IsTeeVariant => variantKind == PipeVariantKind.Tee;
    public bool IsCrossVariant => variantKind == PipeVariantKind.Cross;

    protected override void OnEnable()
    {
        base.OnEnable();
        displayedFluidItemId = NoDisplayedFluidItemId;
        displayedFluidVisible = false;
        nextFluidDisplayRefreshTime = Time.time + GetFluidDisplayRefreshOffset();
        RefreshFluidDisplay(true);
    }

    protected override void OnDisable()
    {
        SetFluidDisplayVisible(false, true);
        base.OnDisable();
    }

    private void Update()
    {
        if (Time.time < nextFluidDisplayRefreshTime)
        {
            return;
        }

        nextFluidDisplayRefreshTime = Time.time + FluidDisplayRefreshIntervalSeconds;
        RefreshFluidDisplay(false);
    }

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
        return TryGetStoredFluidInfoAtCoordinate(coordinate, out fluidItemId, out temperatureCelsius);
    }

    private bool TryGetStoredFluidItemIdAtCoordinate(Vector2Int coordinate, out int fluidItemId)
    {
        return TryGetStoredFluidInfoAtCoordinate(coordinate, out fluidItemId, out _);
    }

    private bool TryGetStoredFluidInfoAtCoordinate(
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
                storage => CanDisplayStoredFluidAtCoordinate(storage, coordinate),
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
            || bodyStorage.StoredFluidItemId < 0
            || !CanDisplayStoredFluidAtCoordinate(bodyStorage, coordinate))
        {
            return false;
        }

        fluidItemId = bodyStorage.StoredFluidItemId;
        temperatureCelsius = bodyStorage.GetStoredFluidTemperatureCelsius(fluidItemId);
        return true;
    }

    private bool CanDisplayStoredFluidAtCoordinate(InstallationObject storage, Vector2Int coordinate)
    {
        if (storage == null || storage.StoredFluidItemId < 0)
        {
            return false;
        }

        InputOutputModule module = storage as InputOutputModule;
        if (module == null)
        {
            return true;
        }

        return module.CanExposeStoredFluidAtRuntimePipeCoordinate(
            coordinate,
            storage.StoredFluidItemId,
            objectInfoFluidItemIds);
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

    private void RefreshFluidDisplay(bool force)
    {
        MeshRenderer renderer = ResolveFluidDisplayRenderer();
        if (renderer == null)
        {
            return;
        }

        if (!TryGetObjectInfoFluidItemId(out int fluidItemId))
        {
            displayedFluidItemId = NoDisplayedFluidItemId;
            SetFluidDisplayVisible(false, force);
            return;
        }

        if (!force && displayedFluidVisible && displayedFluidItemId == fluidItemId)
        {
            return;
        }

        ApplyFluidDisplayColor(renderer, ResolveFluidDisplayColor(fluidItemId));
        displayedFluidItemId = fluidItemId;
        SetFluidDisplayVisible(true, force);
    }

    private MeshRenderer ResolveFluidDisplayRenderer()
    {
        if (fluidDisplayRendererResolved)
        {
            return fluidDP;
        }

        fluidDisplayRendererResolved = true;
        if (fluidDP != null)
        {
            return fluidDP;
        }

        MeshRenderer[] childRenderers = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < childRenderers.Length; i++)
        {
            MeshRenderer renderer = childRenderers[i];
            if (renderer != null && renderer.name == "Fluid DP")
            {
                fluidDP = renderer;
                return fluidDP;
            }
        }

        for (int i = 0; i < childRenderers.Length; i++)
        {
            MeshRenderer renderer = childRenderers[i];
            if (RendererUsesFluidDisplayMaterial(renderer))
            {
                fluidDP = renderer;
                return fluidDP;
            }
        }

        return null;
    }

    private static bool RendererUsesFluidDisplayMaterial(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        Material sharedMaterial = renderer.sharedMaterial;
        return sharedMaterial != null && sharedMaterial.name == "M_Fluid";
    }

    private void ApplyFluidDisplayColor(Renderer renderer, Color color)
    {
        if (fluidDisplayPropertyBlock == null)
        {
            fluidDisplayPropertyBlock = new MaterialPropertyBlock();
        }

        renderer.GetPropertyBlock(fluidDisplayPropertyBlock);
        fluidDisplayPropertyBlock.SetColor(BaseColorShaderId, color);
        fluidDisplayPropertyBlock.SetColor(ColorShaderId, color);
        fluidDisplayPropertyBlock.SetColor(EmissionColorShaderId, color);
        renderer.SetPropertyBlock(fluidDisplayPropertyBlock);
    }

    private void SetFluidDisplayVisible(bool visible, bool force)
    {
        MeshRenderer renderer = ResolveFluidDisplayRenderer();
        if (renderer == null)
        {
            displayedFluidVisible = false;
            return;
        }

        if (force || renderer.enabled != visible)
        {
            renderer.enabled = visible;
        }

        displayedFluidVisible = visible;
    }

    private static Color ResolveFluidDisplayColor(int fluidItemId)
    {
        ItemDefinition definition = InputOutputModule.ResolveItemDefinition(fluidItemId);
        if (definition != null)
        {
            return definition.fluidDisplayColor;
        }

        return UnknownFluidDisplayColor;
    }

    private float GetFluidDisplayRefreshOffset()
    {
        return (GetInstanceID() & 0x3ff) / 1024f * FluidDisplayRefreshIntervalSeconds;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (variantKind == PipeVariantKind.Straight && straightVariantPrefab == null)
        {
            straightVariantPrefab = this;
        }

        fluidDisplayRendererResolved = false;
        ResolveFluidDisplayRenderer();
        SetFluidDisplayVisible(false, true);
    }
#endif
}
