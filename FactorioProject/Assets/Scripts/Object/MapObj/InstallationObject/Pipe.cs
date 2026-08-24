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
    private static readonly Dictionary<Vector2Int, int> FluidDisplayNetworkItemCache =
        new Dictionary<Vector2Int, int>();
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
    private readonly List<InstallationObject> objectInfoFluidStorageScratch = new List<InstallationObject>(4);

    [SerializeField]
    private MeshRenderer fluidDP;

    private MaterialPropertyBlock fluidDisplayPropertyBlock;
    private int displayedFluidItemId = NoDisplayedFluidItemId;
    private bool displayedFluidVisible;
    private bool fluidDisplayRendererResolved;
    private bool fluidDisplaySuppressedForVariantPreview;
    private float nextFluidDisplayRefreshTime;
    private static float fluidDisplayNetworkCacheExpiresAt;

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
        InvalidateFluidDisplayNetworkCache();
        fluidDisplaySuppressedForVariantPreview = false;
        Fluidtank.RefreshAllPipeVisuals();
        RefreshFluidDisplayImmediately();
    }

    protected override void OnDisable()
    {
        InvalidateFluidDisplayNetworkCache();
        SetFluidDisplayVisible(false, true);
        base.OnDisable();
        Fluidtank.RefreshAllPipeVisuals();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextFluidDisplayRefreshTime)
        {
            return;
        }

        nextFluidDisplayRefreshTime = Time.unscaledTime + FluidDisplayRefreshIntervalSeconds;
        RefreshFluidDisplay(false);
    }

    protected override void OnPlacementRuntimeChanged()
    {
        base.OnPlacementRuntimeChanged();
        InvalidateFluidDisplayNetworkCache();

        // OnEnable runs before a newly placed pipe is bound to its grid coordinate.
        // Refresh again after the runtime placement index is registered so the pipe
        // can resolve adjacent tanks and pipe outputs without requiring a move.
        RefreshFluidDisplayImmediately();
    }

    public void RefreshFluidDisplayImmediately()
    {
        displayedFluidItemId = NoDisplayedFluidItemId;
        displayedFluidVisible = false;
        nextFluidDisplayRefreshTime = Time.unscaledTime + GetFluidDisplayRefreshOffset();
        RefreshFluidDisplay(true);
    }

    public void SetVariantPreviewFluidDisplaySuppressed(bool suppressed)
    {
        if (fluidDisplaySuppressedForVariantPreview == suppressed)
        {
            return;
        }

        fluidDisplaySuppressedForVariantPreview = suppressed;
        if (suppressed)
        {
            SetFluidDisplayVisible(false, true);
            return;
        }

        RefreshFluidDisplayImmediately();
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

        return TrySearchFluidNetwork(
            startCoordinate,
            false,
            false,
            Vector2Int.zero,
            out fluidItemId,
            out temperatureCelsius);
    }

    public bool TryGetConnectedFluidItemIdIgnoringStorageCoordinate(
        Vector2Int ignoredStorageCoordinate,
        out int fluidItemId)
    {
        fluidItemId = -1;
        if (!TryResolveObjectInfoPipeCoordinate(out Vector2Int startCoordinate))
        {
            return false;
        }

        return TrySearchFluidNetwork(
            startCoordinate,
            false,
            true,
            ignoredStorageCoordinate,
            out fluidItemId,
            out _);
    }

    private bool TrySearchFluidNetwork(
        Vector2Int startCoordinate,
        bool cacheDisplayNetwork,
        bool hasIgnoredStorageCoordinate,
        Vector2Int ignoredStorageCoordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        TerrainGenerator terrain = TerrainGenerator.Active;
        objectInfoFluidSearchQueue.Clear();
        objectInfoFluidSearchVisited.Clear();
        EnqueueObjectInfoFluidSearchCoordinate(startCoordinate);

        bool foundFluid = false;
        int searchedNodeCount = 0;
        while (objectInfoFluidSearchQueue.Count > 0
               && searchedNodeCount < MaxObjectInfoFluidSearchNodes)
        {
            Vector2Int coordinate = objectInfoFluidSearchQueue.Dequeue();
            searchedNodeCount++;

            if (!foundFluid
                && (!hasIgnoredStorageCoordinate || coordinate != ignoredStorageCoordinate)
                && TryGetFluidInfoAtPipeNetworkCoordinate(
                    coordinate,
                    out fluidItemId,
                    out temperatureCelsius))
            {
                foundFluid = true;
                if (!cacheDisplayNetwork)
                {
                    return true;
                }
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
                if (!pipe.HasConnectionTowardsAt(coordinate, pipeRotation, direction))
                {
                    continue;
                }

                Vector2Int neighborCoordinate = coordinate + direction;
                if (!foundFluid
                    && (!hasIgnoredStorageCoordinate || neighborCoordinate != ignoredStorageCoordinate)
                    && TryGetFluidInfoAtPipeNetworkCoordinate(
                        neighborCoordinate,
                        out fluidItemId,
                        out temperatureCelsius))
                {
                    foundFluid = true;
                    if (!cacheDisplayNetwork)
                    {
                        return true;
                    }
                }

                if (TryGetPipeAtCoordinate(terrain, neighborCoordinate, out Pipe neighborPipe, out Quaternion neighborRotation)
                    && neighborPipe.HasConnectionTowardsAt(neighborCoordinate, neighborRotation, -direction))
                {
                    EnqueueObjectInfoFluidSearchCoordinate(neighborCoordinate);
                }
            }

            if (pipe.TryGetRemoteConnectionCoordinate(coordinate, out Vector2Int remoteCoordinate))
            {
                EnqueueObjectInfoFluidSearchCoordinate(remoteCoordinate);
            }
        }

        if (cacheDisplayNetwork)
        {
            foreach (Vector2Int coordinate in objectInfoFluidSearchVisited)
            {
                FluidDisplayNetworkItemCache[coordinate] = fluidItemId;
            }
        }

        return foundFluid;
    }

    public virtual bool HasConnectionTowards(Quaternion rotation, Vector2Int direction)
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

    public virtual bool HasConnectionTowardsAt(
        Vector2Int coordinate,
        Quaternion rotation,
        Vector2Int direction)
    {
        return HasConnectionTowards(rotation, direction);
    }

    public virtual bool TryGetRemoteConnectionCoordinate(
        Vector2Int coordinate,
        out Vector2Int remoteCoordinate)
    {
        remoteCoordinate = default;
        return false;
    }

    public int GetConnectionMask(Quaternion rotation)
    {
        int connectionMask = 0;
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            if (HasConnectionTowards(rotation, CardinalDirections[i]))
            {
                connectionMask |= 1 << i;
            }
        }

        return connectionMask;
    }

    public bool TryGetPrimaryConnectionDirection(
        Quaternion rotation,
        out Vector2Int direction)
    {
        return TryResolveDirection(rotation, localStraightDirection, out direction);
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

    private bool TryGetFluidInfoAtPipeNetworkCoordinate(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        if (TryGetStoredFluidInfoAtCoordinate(coordinate, out fluidItemId, out temperatureCelsius))
        {
            return true;
        }

        return TryGetSourceFluidInfoAtCoordinate(coordinate, out fluidItemId, out temperatureCelsius);
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

        if (TryGetRuntimeStoredFluidInfoAtCoordinate(
                coordinate,
                out fluidItemId,
                out temperatureCelsius))
        {
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

    private bool TryGetRuntimeStoredFluidInfoAtCoordinate(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        objectInfoFluidStorageScratch.Clear();
        if (!CollectActiveInstallationsAtRuntimeGridCoordinate(
                coordinate,
                objectInfoFluidStorageScratch))
        {
            return false;
        }

        for (int i = 0; i < objectInfoFluidStorageScratch.Count; i++)
        {
            InstallationObject storage = objectInfoFluidStorageScratch[i];
            if (storage == null
                || storage is Pipe
                || storage.StoredFluidItemId < 0
                || !CanDisplayStoredFluidAtCoordinate(storage, coordinate))
            {
                continue;
            }

            fluidItemId = storage.StoredFluidItemId;
            temperatureCelsius = storage.GetStoredFluidTemperatureCelsius(fluidItemId);
            objectInfoFluidStorageScratch.Clear();
            return true;
        }

        objectInfoFluidStorageScratch.Clear();
        return false;
    }

    private bool TryGetSourceFluidInfoAtCoordinate(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;

        if (TryResolvePumpSourceAtCoordinate(coordinate, out Pump pump)
            && pump != null
            && pump.TryGetObjectInfoOutputRate(out int outputItemId, out float litersPerSecond)
            && outputItemId >= 0
            && litersPerSecond > 0.0001f)
        {
            fluidItemId = outputItemId;
            temperatureCelsius = pump.GetStoredFluidTemperatureCelsius(outputItemId);
            return true;
        }

        return false;
    }

    private static bool TryResolvePumpSourceAtCoordinate(Vector2Int coordinate, out Pump pump)
    {
        if (InputOutputModule.TryGetRuntimePipeSourceAtCoordinate(coordinate, out pump)
            && pump != null
            && pump.gameObject.activeInHierarchy)
        {
            return true;
        }

        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain != null
            && terrain.TryGetLoadedBlock(coordinate, out Block block)
            && block != null
            && block.MapObject is Pump directPump
            && directPump.gameObject.activeInHierarchy)
        {
            pump = directPump;
            return true;
        }

        pump = null;
        return false;
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

        if (fluidDisplaySuppressedForVariantPreview)
        {
            SetFluidDisplayVisible(false, force);
            return;
        }

        if (!TryGetCachedFluidDisplayItemId(out int fluidItemId))
        {
            displayedFluidItemId = NoDisplayedFluidItemId;
            SetFluidDisplayVisible(false, force);
            return;
        }

        if (!force
            && displayedFluidVisible
            && renderer.enabled
            && displayedFluidItemId == fluidItemId)
        {
            return;
        }

        ApplyFluidDisplayColor(renderer, ResolveFluidDisplayColor(fluidItemId));
        displayedFluidItemId = fluidItemId;
        SetFluidDisplayVisible(true, force);
    }

    private bool TryGetCachedFluidDisplayItemId(out int fluidItemId)
    {
        fluidItemId = -1;
        if (!TryResolveObjectInfoPipeCoordinate(out Vector2Int startCoordinate))
        {
            return false;
        }

        RefreshFluidDisplayNetworkCacheWindow();
        if (FluidDisplayNetworkItemCache.TryGetValue(startCoordinate, out fluidItemId))
        {
            return fluidItemId >= 0;
        }

        return SearchAndCacheFluidDisplayNetwork(startCoordinate, out fluidItemId);
    }

    private bool SearchAndCacheFluidDisplayNetwork(Vector2Int startCoordinate, out int fluidItemId)
    {
        return TrySearchFluidNetwork(
            startCoordinate,
            true,
            false,
            Vector2Int.zero,
            out fluidItemId,
            out _);
    }

    private static void RefreshFluidDisplayNetworkCacheWindow()
    {
        float currentTime = Time.unscaledTime;
        if (currentTime < fluidDisplayNetworkCacheExpiresAt)
        {
            return;
        }

        FluidDisplayNetworkItemCache.Clear();
        fluidDisplayNetworkCacheExpiresAt = currentTime + FluidDisplayRefreshIntervalSeconds;
    }

    private static void InvalidateFluidDisplayNetworkCache()
    {
        FluidDisplayNetworkItemCache.Clear();
        fluidDisplayNetworkCacheExpiresAt = 0f;
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

    protected void ApplyFluidDisplayState(Renderer renderer, bool visible, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        if (visible)
        {
            ApplyFluidDisplayColor(renderer, color);
        }

        renderer.enabled = visible;
    }

    protected virtual void OnFluidDisplayStateChanged(bool visible, Color color)
    {
    }

    private void SetFluidDisplayVisible(bool visible, bool force)
    {
        MeshRenderer renderer = ResolveFluidDisplayRenderer();
        if (renderer == null)
        {
            displayedFluidVisible = false;
            OnFluidDisplayStateChanged(false, default);
            return;
        }

        if (force || renderer.enabled != visible)
        {
            renderer.enabled = visible;
        }

        displayedFluidVisible = visible;
        Color color = visible && displayedFluidItemId != NoDisplayedFluidItemId
            ? ResolveFluidDisplayColor(displayedFluidItemId)
            : default;
        OnFluidDisplayStateChanged(visible, color);
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
