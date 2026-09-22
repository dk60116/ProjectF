using UnityEngine;
using System.Collections.Generic;

public readonly struct AreaMarkerSpawnRequest
{
    public readonly Vector3 WorldPosition;
    public readonly Sprite Icon;
    public readonly float IconRotationZ;
    public readonly Sprite OverlayIcon;
    public readonly float OverlayIconRotationZ;
    public readonly bool UsesRuntimeFluidIcon;
    public readonly Vector2Int RuntimeFluidCoordinate;
    public readonly Sprite FallbackIcon;
    public readonly int RuntimeFluidItemId;

    public AreaMarkerSpawnRequest(Vector3 worldPosition, Sprite icon, float iconRotationZ = 0f)
        : this(worldPosition, icon, iconRotationZ, null, 0f, false, default, icon, -1)
    {
    }

    private AreaMarkerSpawnRequest(
        Vector3 worldPosition,
        Sprite icon,
        float iconRotationZ,
        Sprite overlayIcon,
        float overlayIconRotationZ,
        bool usesRuntimeFluidIcon,
        Vector2Int runtimeFluidCoordinate,
        Sprite fallbackIcon,
        int runtimeFluidItemId)
    {
        WorldPosition = worldPosition;
        Icon = icon;
        IconRotationZ = iconRotationZ;
        OverlayIcon = overlayIcon;
        OverlayIconRotationZ = overlayIconRotationZ;
        UsesRuntimeFluidIcon = usesRuntimeFluidIcon;
        RuntimeFluidCoordinate = runtimeFluidCoordinate;
        FallbackIcon = fallbackIcon;
        RuntimeFluidItemId = runtimeFluidItemId;
    }

    public static AreaMarkerSpawnRequest CreateRuntimeFluid(
        Vector3 worldPosition,
        Vector2Int fluidCoordinate,
        Sprite fallbackIcon)
    {
        return new AreaMarkerSpawnRequest(
            worldPosition,
            fallbackIcon,
            0f,
            null,
            0f,
            true,
            fluidCoordinate,
            fallbackIcon,
            -1);
    }

    public AreaMarkerSpawnRequest WithRuntimeFluid(int fluidItemId, Sprite icon)
    {
        return new AreaMarkerSpawnRequest(
            WorldPosition,
            icon != null ? icon : FallbackIcon,
            IconRotationZ,
            OverlayIcon,
            OverlayIconRotationZ,
            UsesRuntimeFluidIcon,
            RuntimeFluidCoordinate,
            FallbackIcon,
            fluidItemId);
    }

    public AreaMarkerSpawnRequest WithOverlay(Sprite overlayIcon, float overlayIconRotationZ = 0f)
    {
        return new AreaMarkerSpawnRequest(
            WorldPosition,
            Icon,
            IconRotationZ,
            overlayIcon,
            overlayIconRotationZ,
            UsesRuntimeFluidIcon,
            RuntimeFluidCoordinate,
            FallbackIcon,
            RuntimeFluidItemId);
    }
}

public readonly struct InputOutputModuleItemAreaBinding
{
    public readonly Vector2Int Coordinate;
    public readonly int ItemId;

    public InputOutputModuleItemAreaBinding(Vector2Int coordinate, int itemId)
    {
        Coordinate = coordinate;
        ItemId = itemId;
    }
}

// The prefab is a visual template only. Runtime markers never instantiate it.
public class AreaMarker : MonoBehaviour
{
    [SerializeField] private SpriteRenderer icon;
    public SpriteRenderer Icon => icon;
}

// A lifecycle/selection bridge on the existing installation, with no per-object Update.
public class InputOutputModuleAreaMarkerController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float visibleRange = 5f;
    [SerializeField, Min(0f)] private float verticalOffset = 0.08f;
    [SerializeField, Min(0.05f)] private float runtimeFluidIconRefreshInterval = 0.2f;
    private readonly List<AreaMarkerSpawnRequest> requests = new List<AreaMarkerSpawnRequest>();
    private AreaMarkerRenderer markerRenderer;
    private InputOutputModule runtimeFluidModule;
    private Transform markerParent;
    private Matrix4x4 configuredParentInverse = Matrix4x4.identity;
    private Matrix4x4 renderedParentDelta = Matrix4x4.identity;
    private bool forceMarkerVisibility;
    private bool selectionVisibilityRequested;
    private bool visible;
    private int sortingOrderOffset;
    private bool renderOnTop;
    private float markerVerticalOffset;
    private float nextRuntimeFluidIconRefreshTime = float.NegativeInfinity;

    internal int MarkerCount => requests.Count;
    internal bool IsVisible => visible;
    internal float VisibleRange => visibleRange;
    internal Vector3 VisibilityWorldPosition => transform.position;
    internal bool RequiresContinuousVisibilityRefresh => UsesMovingBatches
        || visibleRange <= 0f
        || forceMarkerVisibility
        || selectionVisibilityRequested;
    // Retain the batch partition even after Unity destroys the parent, so unregistration
    // invalidates the mesh that actually contains these markers.
    internal bool UsesMovingBatches => !object.ReferenceEquals(markerParent, null);

    public void Configure(
        AreaMarkerRenderer renderer,
        IReadOnlyList<AreaMarkerSpawnRequest> markerRequests,
        bool forceVisible = false,
        int sortingOrderOffset = 0,
        bool renderOnTop = false,
        Transform markerParent = null,
        float? verticalOffsetOverride = null)
    {
        float offset = verticalOffsetOverride.HasValue
            ? Mathf.Max(0f, verticalOffsetOverride.Value) : verticalOffset;
        Matrix4x4 inverse = markerParent != null ? markerParent.worldToLocalMatrix : Matrix4x4.identity;
        int count = markerRequests != null ? markerRequests.Count : 0;
        bool changed = markerRenderer != renderer || this.markerParent != markerParent
            || forceMarkerVisibility != forceVisible || this.sortingOrderOffset != sortingOrderOffset
            || this.renderOnTop != renderOnTop || markerVerticalOffset != offset
            || !configuredParentInverse.Equals(inverse) || requests.Count != count;
        if (!changed)
        {
            for (int i = 0; i < count; i++)
            {
                AreaMarkerSpawnRequest a = requests[i];
                AreaMarkerSpawnRequest b = markerRequests[i];
                if (a.WorldPosition != b.WorldPosition
                    || a.IconRotationZ != b.IconRotationZ
                    || a.OverlayIcon != b.OverlayIcon
                    || a.OverlayIconRotationZ != b.OverlayIconRotationZ
                    || a.UsesRuntimeFluidIcon != b.UsesRuntimeFluidIcon
                    || a.RuntimeFluidCoordinate != b.RuntimeFluidCoordinate
                    || a.FallbackIcon != b.FallbackIcon
                    || (!a.UsesRuntimeFluidIcon && a.Icon != b.Icon))
                {
                    changed = true;
                    break;
                }
            }
        }
        if (!changed) return;

        if (markerRenderer != null) markerRenderer.Unregister(this);
        markerRenderer = renderer;
        this.markerParent = markerParent;
        configuredParentInverse = inverse;
        forceMarkerVisibility = forceVisible;
        this.sortingOrderOffset = sortingOrderOffset;
        this.renderOnTop = renderOnTop;
        markerVerticalOffset = offset;
        requests.Clear();
        for (int i = 0; i < count; i++) requests.Add(markerRequests[i]);
        runtimeFluidModule = null;
        if (HasRuntimeFluidIconRequests())
        {
            runtimeFluidModule = GetComponent<InputOutputModule>();
            if (runtimeFluidModule == null)
            {
                runtimeFluidModule = GetComponentInChildren<InputOutputModule>(true);
            }
        }
        nextRuntimeFluidIconRefreshTime = float.NegativeInfinity;
        visible = false;
        if (isActiveAndEnabled && markerRenderer != null && count > 0) markerRenderer.Register(this);
    }

    public bool ShouldShowLinkedUi()
    {
        AreaMarkerVisibilityContext context = AreaMarkerVisibilityContext.Capture();
        return isActiveAndEnabled && requests.Count > 0 && ShouldBeVisible(context);
    }

    public void SetSelectionVisibilityRequested(bool requested)
    {
        if (selectionVisibilityRequested == requested) return;
        selectionVisibilityRequested = requested;
        markerRenderer?.NotifyVisibilityOverrideChanged(this);
    }

    private void OnEnable()
    {
        if (markerRenderer != null && requests.Count > 0) markerRenderer.Register(this);
    }

    private void OnDisable()
    {
        if (markerRenderer != null) markerRenderer.Unregister(this);
        visible = false;
    }

    private void OnDestroy()
    {
        if (markerRenderer != null) markerRenderer.Unregister(this);
    }

    internal bool RefreshVisibility(in AreaMarkerVisibilityContext context)
    {
        bool nextVisible = isActiveAndEnabled && requests.Count > 0
            && (!UsesMovingBatches || markerParent != null) && ShouldBeVisible(context);
        Matrix4x4 delta = markerParent != null
            ? markerParent.localToWorldMatrix * configuredParentInverse : Matrix4x4.identity;
        bool changed = visible != nextVisible || (nextVisible && !renderedParentDelta.Equals(delta));
        visible = nextVisible;
        renderedParentDelta = delta;
        if (nextVisible && RefreshRuntimeFluidIcons())
        {
            changed = true;
        }
        return changed;
    }

    private bool HasRuntimeFluidIconRequests()
    {
        for (int i = 0; i < requests.Count; i++)
        {
            if (requests[i].UsesRuntimeFluidIcon)
            {
                return true;
            }
        }

        return false;
    }

    private bool RefreshRuntimeFluidIcons()
    {
        if (!Application.isPlaying
            || runtimeFluidModule == null
            || Time.unscaledTime < nextRuntimeFluidIconRefreshTime)
        {
            return false;
        }

        nextRuntimeFluidIconRefreshTime = Time.unscaledTime
            + Mathf.Max(0.05f, runtimeFluidIconRefreshInterval);
        bool changed = false;
        for (int i = 0; i < requests.Count; i++)
        {
            AreaMarkerSpawnRequest request = requests[i];
            if (!request.UsesRuntimeFluidIcon)
            {
                continue;
            }

            int fluidItemId = TryResolveRuntimeFluidItemId(
                request.RuntimeFluidCoordinate,
                out int resolvedFluidItemId)
                ? resolvedFluidItemId
                : -1;
            if (request.RuntimeFluidItemId == fluidItemId)
            {
                continue;
            }

            ItemDefinition fluidDefinition = InputOutputModule.ResolveItemDefinition(fluidItemId);
            Sprite icon = fluidDefinition != null ? fluidDefinition.icon : request.FallbackIcon;
            AreaMarkerSpawnRequest updatedRequest = request.WithRuntimeFluid(fluidItemId, icon);
            requests[i] = updatedRequest;
            changed |= request.Icon != updatedRequest.Icon;
        }

        return changed;
    }

    private bool TryResolveRuntimeFluidItemId(Vector2Int coordinate, out int fluidItemId)
    {
        fluidItemId = -1;
        PipeWorld pipeWorld = PipeWorld.Current;
        if (pipeWorld != null
            && pipeWorld.TryGetAtCoordinate(coordinate, out PipeRuntimeRecord pipeRecord)
            && pipeRecord != null
            && pipeRecord.TryGetObjectInfoFluidInfo(
                coordinate,
                out fluidItemId,
                out _,
                out _,
                false)
            && fluidItemId >= 0)
        {
            return true;
        }

        if (runtimeFluidModule is Pump pump
            && pump.TryGetObjectInfoFluidInfo(out fluidItemId, out _, out _)
            && fluidItemId >= 0)
        {
            return true;
        }

        if (runtimeFluidModule.StoredFluidItemId >= 0)
        {
            fluidItemId = runtimeFluidModule.StoredFluidItemId;
            return true;
        }

        return InputOutputModule.TryGetFluidOutputInfoAtRuntimeGridCoordinate(
                   coordinate,
                   out fluidItemId,
                   out _)
               && fluidItemId >= 0;
    }

    private bool ShouldBeVisible(in AreaMarkerVisibilityContext context)
    {
        return AreaMarkerVisibilityContext.ShouldShow(visibleRange, forceMarkerVisibility,
            selectionVisibilityRequested, context.ShowAll, context.HasPlayer,
            context.PlayerPosition, transform.position);
    }

    internal void AppendMarkers(AreaMarkerRenderer renderer)
    {
        for (int i = 0; i < requests.Count; i++)
        {
            AreaMarkerSpawnRequest request = requests[i];
            Matrix4x4 matrix = renderedParentDelta * Matrix4x4.Translate(
                request.WorldPosition + Vector3.up * markerVerticalOffset);
            renderer.Append(request, matrix, sortingOrderOffset, renderOnTop, UsesMovingBatches);
        }
    }
}

internal readonly struct AreaMarkerVisibilityContext
{
    public readonly bool ShowAll;
    public readonly bool HasPlayer;
    public readonly Vector3 PlayerPosition;

    private AreaMarkerVisibilityContext(bool showAll, bool hasPlayer, Vector3 playerPosition)
    {
        ShowAll = showAll;
        HasPlayer = hasPlayer;
        PlayerPosition = playerPosition;
    }

    public static AreaMarkerVisibilityContext Capture()
    {
        GameManager manager = GameManager.Instance;
        Player player = manager != null ? manager.Player : null;
        return new AreaMarkerVisibilityContext(
            manager != null && (manager.InstallationPlacementActive || manager.MapEditActive),
            player != null,
            player != null ? (player.BodyTransform != null ? player.BodyTransform.position : player.transform.position) : default);
    }

    internal static bool ShouldShow(float range, bool forced, bool selected, bool showAll,
        bool hasPlayer, Vector3 playerPosition, Vector3 ownerPosition)
    {
        if (range <= 0f || forced || selected || showAll) return true;
        if (!hasPlayer) return false;
        float x = playerPosition.x - ownerPosition.x;
        float z = playerPosition.z - ownerPosition.z;
        return x * x + z * z <= range * range;
    }
}

internal sealed class InstallationPlacementAreaRegistry
{
    private readonly Dictionary<Vector2Int, int> coordinateCounts = new Dictionary<Vector2Int, int>();

    public bool Contains(Vector2Int coordinate)
    {
        return coordinateCounts.TryGetValue(coordinate, out int count) && count > 0;
    }

    public void Register(Vector2Int coordinate)
    {
        coordinateCounts.TryGetValue(coordinate, out int count);
        coordinateCounts[coordinate] = count + 1;
    }

    public void Unregister(Vector2Int coordinate)
    {
        if (!coordinateCounts.TryGetValue(coordinate, out int count))
        {
            return;
        }

        if (count <= 1)
        {
            coordinateCounts.Remove(coordinate);
            return;
        }

        coordinateCounts[coordinate] = count - 1;
    }
}

public class InputOutputModuleEnergyAreaController : MonoBehaviour
{
    private static readonly Dictionary<Vector2Int, Dictionary<ItemDefinition.EnergyType, int>> registeredEnergyAreas
        = new Dictionary<Vector2Int, Dictionary<ItemDefinition.EnergyType, int>>();
    private static readonly InstallationPlacementAreaRegistry placementBlockingAreas
        = new InstallationPlacementAreaRegistry();

    [SerializeField]
    private ItemDefinition.EnergyType acceptedEnergyType = ItemDefinition.EnergyType.None;
    [SerializeField]
    private List<ItemDefinition.EnergyType> acceptedEnergyTypes = new List<ItemDefinition.EnergyType>();

    [SerializeField]
    private List<Vector2Int> inputEnergyCoordinates = new List<Vector2Int>();

    [SerializeField, HideInInspector]
    private bool blocksInstallationPlacement = true;

    private bool isRegistered;

    public void Configure(
        ItemDefinition.EnergyType energyType,
        IReadOnlyList<Vector2Int> coordinates,
        bool blocksPlacement = true)
    {
        UnregisterCoordinates();
        acceptedEnergyType = energyType;
        acceptedEnergyTypes.Clear();
        if (energyType != ItemDefinition.EnergyType.None)
        {
            acceptedEnergyTypes.Add(energyType);
        }
        blocksInstallationPlacement = blocksPlacement;
        inputEnergyCoordinates.Clear();

        if (coordinates == null)
        {
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            if (!inputEnergyCoordinates.Contains(coordinates[i]))
            {
                inputEnergyCoordinates.Add(coordinates[i]);
            }
        }

        RegisterCoordinates();
    }

    public void Configure(
        ItemDefinition definition,
        IReadOnlyList<Vector2Int> coordinates,
        bool blocksPlacement = true)
    {
        UnregisterCoordinates();
        acceptedEnergyType = ItemDefinition.EnergyType.None;
        acceptedEnergyTypes.Clear();
        blocksInstallationPlacement = blocksPlacement;
        inputEnergyCoordinates.Clear();

        if (definition != null)
        {
            int requirementCount = definition.UseEnergyRequirementCount;
            for (int i = 0; i < requirementCount; i++)
            {
                if (definition.TryGetUseEnergyRequirement(
                        i,
                        out ItemDefinition.EnergyUseRequirement requirement)
                    && requirement.energyType != ItemDefinition.EnergyType.None
                    && requirement.useEnergyAmount > 0f
                    && !acceptedEnergyTypes.Contains(requirement.energyType))
                {
                    acceptedEnergyTypes.Add(requirement.energyType);
                }
            }
        }

        if (coordinates != null)
        {
            for (int i = 0; i < coordinates.Count; i++)
            {
                if (!inputEnergyCoordinates.Contains(coordinates[i]))
                {
                    inputEnergyCoordinates.Add(coordinates[i]);
                }
            }
        }

        RegisterCoordinates();
    }

    private void OnEnable()
    {
        RegisterCoordinates();
    }

    private void OnDisable()
    {
        UnregisterCoordinates();
    }

    private void OnDestroy()
    {
        UnregisterCoordinates();
    }

    public static bool CoordinateAcceptsEnergyType(Vector2Int coordinate, ItemDefinition.EnergyType energyType)
    {
        if (energyType == ItemDefinition.EnergyType.None)
        {
            return false;
        }

        if (!registeredEnergyAreas.TryGetValue(coordinate, out Dictionary<ItemDefinition.EnergyType, int> energyCounts)
            || energyCounts == null)
        {
            return false;
        }

        return energyCounts.TryGetValue(energyType, out int count) && count > 0;
    }

    public static bool TryGetAcceptedEnergyTypes(Vector2Int coordinate, ISet<ItemDefinition.EnergyType> acceptedEnergyTypes)
    {
        if (acceptedEnergyTypes == null)
        {
            return false;
        }

        if (!registeredEnergyAreas.TryGetValue(coordinate, out Dictionary<ItemDefinition.EnergyType, int> energyCounts)
            || energyCounts == null
            || energyCounts.Count <= 0)
        {
            return false;
        }

        bool foundAny = false;
        foreach (KeyValuePair<ItemDefinition.EnergyType, int> pair in energyCounts)
        {
            if (pair.Key == ItemDefinition.EnergyType.None || pair.Value <= 0)
            {
                continue;
            }

            acceptedEnergyTypes.Add(pair.Key);
            foundAny = true;
        }

        return foundAny;
    }

    public static bool CoordinateIsEnergyArea(Vector2Int coordinate)
    {
        if (!registeredEnergyAreas.TryGetValue(coordinate, out Dictionary<ItemDefinition.EnergyType, int> energyCounts)
            || energyCounts == null
            || energyCounts.Count <= 0)
        {
            return false;
        }

        foreach (KeyValuePair<ItemDefinition.EnergyType, int> pair in energyCounts)
        {
            if (pair.Value > 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool CoordinateBlocksInstallationPlacement(Vector2Int coordinate)
    {
        return placementBlockingAreas.Contains(coordinate);
    }

    private void RegisterCoordinates()
    {
        EnsureAcceptedEnergyTypeList();
        if (isRegistered || acceptedEnergyTypes.Count <= 0 || inputEnergyCoordinates.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < inputEnergyCoordinates.Count; i++)
        {
            Vector2Int coordinate = inputEnergyCoordinates[i];
            if (!registeredEnergyAreas.TryGetValue(coordinate, out Dictionary<ItemDefinition.EnergyType, int> energyCounts)
                || energyCounts == null)
            {
                energyCounts = new Dictionary<ItemDefinition.EnergyType, int>();
                registeredEnergyAreas[coordinate] = energyCounts;
            }

            for (int typeIndex = 0; typeIndex < acceptedEnergyTypes.Count; typeIndex++)
            {
                ItemDefinition.EnergyType energyType = acceptedEnergyTypes[typeIndex];
                energyCounts.TryGetValue(energyType, out int existingCount);
                energyCounts[energyType] = existingCount + 1;
            }
            if (blocksInstallationPlacement)
            {
                placementBlockingAreas.Register(coordinate);
            }
        }

        isRegistered = true;
    }

    private void UnregisterCoordinates()
    {
        if (!isRegistered)
        {
            return;
        }

        for (int i = 0; i < inputEnergyCoordinates.Count; i++)
        {
            Vector2Int coordinate = inputEnergyCoordinates[i];
            if (!registeredEnergyAreas.TryGetValue(coordinate, out Dictionary<ItemDefinition.EnergyType, int> energyCounts)
                || energyCounts == null)
            {
                continue;
            }

            EnsureAcceptedEnergyTypeList();
            for (int typeIndex = 0; typeIndex < acceptedEnergyTypes.Count; typeIndex++)
            {
                ItemDefinition.EnergyType energyType = acceptedEnergyTypes[typeIndex];
                if (!energyCounts.TryGetValue(energyType, out int existingCount))
                {
                    continue;
                }

                if (existingCount <= 1)
                {
                    energyCounts.Remove(energyType);
                }
                else
                {
                    energyCounts[energyType] = existingCount - 1;
                }
            }

            if (energyCounts.Count <= 0)
            {
                registeredEnergyAreas.Remove(coordinate);
            }

            if (blocksInstallationPlacement)
            {
                placementBlockingAreas.Unregister(coordinate);
            }
        }

        isRegistered = false;
    }

    private void EnsureAcceptedEnergyTypeList()
    {
        if (acceptedEnergyTypes.Count <= 0 && acceptedEnergyType != ItemDefinition.EnergyType.None)
        {
            acceptedEnergyTypes.Add(acceptedEnergyType);
        }
    }
}

public class InputOutputModuleItemAreaController : MonoBehaviour
{
    [System.Serializable]
    private struct InputItemAreaEntry
    {
        public Vector2Int coordinate;
        public int itemId;

        public InputItemAreaEntry(Vector2Int coordinate, int itemId)
        {
            this.coordinate = coordinate;
            this.itemId = itemId;
        }
    }

    private static readonly Dictionary<Vector2Int, Dictionary<int, int>> registeredItemAreas
        = new Dictionary<Vector2Int, Dictionary<int, int>>();
    private static readonly InstallationPlacementAreaRegistry placementBlockingAreas
        = new InstallationPlacementAreaRegistry();

    [SerializeField]
    private List<InputItemAreaEntry> inputItemAreas = new List<InputItemAreaEntry>();

    [SerializeField, HideInInspector]
    private bool blocksInstallationPlacement = true;

    private bool isRegistered;

    public void Configure(
        IReadOnlyList<InputOutputModuleItemAreaBinding> bindings,
        bool blocksPlacement = true)
    {
        UnregisterCoordinates();
        blocksInstallationPlacement = blocksPlacement;
        inputItemAreas.Clear();

        if (bindings == null)
        {
            return;
        }

        for (int i = 0; i < bindings.Count; i++)
        {
            InputOutputModuleItemAreaBinding binding = bindings[i];
            if (binding.ItemId < 0)
            {
                continue;
            }

            bool alreadyAdded = false;
            for (int existingIndex = 0; existingIndex < inputItemAreas.Count; existingIndex++)
            {
                InputItemAreaEntry existingEntry = inputItemAreas[existingIndex];
                if (existingEntry.coordinate == binding.Coordinate && existingEntry.itemId == binding.ItemId)
                {
                    alreadyAdded = true;
                    break;
                }
            }

            if (alreadyAdded)
            {
                continue;
            }

            inputItemAreas.Add(new InputItemAreaEntry(binding.Coordinate, binding.ItemId));
        }

        RegisterCoordinates();
    }

    private void OnEnable()
    {
        RegisterCoordinates();
    }

    private void OnDisable()
    {
        UnregisterCoordinates();
    }

    private void OnDestroy()
    {
        UnregisterCoordinates();
    }

    public static bool CoordinateAcceptsItemId(Vector2Int coordinate, int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

        if (!registeredItemAreas.TryGetValue(coordinate, out Dictionary<int, int> itemCounts)
            || itemCounts == null)
        {
            return false;
        }

        return itemCounts.TryGetValue(itemId, out int count) && count > 0;
    }

    public static bool TryGetAcceptedItemIds(Vector2Int coordinate, ISet<int> acceptedItemIds)
    {
        if (acceptedItemIds == null)
        {
            return false;
        }

        if (!registeredItemAreas.TryGetValue(coordinate, out Dictionary<int, int> itemCounts)
            || itemCounts == null
            || itemCounts.Count <= 0)
        {
            return false;
        }

        bool foundAny = false;
        foreach (KeyValuePair<int, int> pair in itemCounts)
        {
            if (pair.Key < 0 || pair.Value <= 0)
            {
                continue;
            }

            acceptedItemIds.Add(pair.Key);
            foundAny = true;
        }

        return foundAny;
    }

    public static bool CoordinateIsItemArea(Vector2Int coordinate)
    {
        if (!registeredItemAreas.TryGetValue(coordinate, out Dictionary<int, int> itemCounts)
            || itemCounts == null
            || itemCounts.Count <= 0)
        {
            return false;
        }

        foreach (KeyValuePair<int, int> pair in itemCounts)
        {
            if (pair.Value > 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool CoordinateBlocksInstallationPlacement(Vector2Int coordinate)
    {
        return placementBlockingAreas.Contains(coordinate);
    }

    private void RegisterCoordinates()
    {
        if (isRegistered || inputItemAreas.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < inputItemAreas.Count; i++)
        {
            InputItemAreaEntry entry = inputItemAreas[i];
            if (entry.itemId < 0)
            {
                continue;
            }

            if (!registeredItemAreas.TryGetValue(entry.coordinate, out Dictionary<int, int> itemCounts)
                || itemCounts == null)
            {
                itemCounts = new Dictionary<int, int>();
                registeredItemAreas[entry.coordinate] = itemCounts;
            }

            itemCounts.TryGetValue(entry.itemId, out int existingCount);
            itemCounts[entry.itemId] = existingCount + 1;
            if (blocksInstallationPlacement)
            {
                placementBlockingAreas.Register(entry.coordinate);
            }
        }

        isRegistered = true;
    }

    private void UnregisterCoordinates()
    {
        if (!isRegistered)
        {
            return;
        }

        for (int i = 0; i < inputItemAreas.Count; i++)
        {
            InputItemAreaEntry entry = inputItemAreas[i];
            if (entry.itemId < 0)
            {
                continue;
            }

            if (!registeredItemAreas.TryGetValue(entry.coordinate, out Dictionary<int, int> itemCounts)
                || itemCounts == null)
            {
                continue;
            }

            if (itemCounts.TryGetValue(entry.itemId, out int existingCount))
            {
                if (existingCount <= 1)
                {
                    itemCounts.Remove(entry.itemId);
                }
                else
                {
                    itemCounts[entry.itemId] = existingCount - 1;
                }
            }

            if (itemCounts.Count <= 0)
            {
                registeredItemAreas.Remove(entry.coordinate);
            }

            if (blocksInstallationPlacement)
            {
                placementBlockingAreas.Unregister(entry.coordinate);
            }
        }

        isRegistered = false;
    }
}

public class InputOutputModuleOutputAreaController : MonoBehaviour
{
    private static readonly Dictionary<Vector2Int, int> registeredOutputAreas
        = new Dictionary<Vector2Int, int>();
    private static readonly InstallationPlacementAreaRegistry placementBlockingAreas
        = new InstallationPlacementAreaRegistry();

    [SerializeField]
    private List<Vector2Int> outputCoordinates = new List<Vector2Int>();

    [SerializeField, HideInInspector]
    private bool blocksInstallationPlacement = true;

    private bool isRegistered;

    public void Configure(IReadOnlyList<Vector2Int> coordinates, bool blocksPlacement = true)
    {
        UnregisterCoordinates();
        blocksInstallationPlacement = blocksPlacement;
        outputCoordinates.Clear();

        if (coordinates == null)
        {
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!outputCoordinates.Contains(coordinate))
            {
                outputCoordinates.Add(coordinate);
            }
        }

        RegisterCoordinates();
    }

    private void OnEnable()
    {
        RegisterCoordinates();
    }

    private void OnDisable()
    {
        UnregisterCoordinates();
    }

    private void OnDestroy()
    {
        UnregisterCoordinates();
    }

    public static bool CoordinateIsOutputArea(Vector2Int coordinate)
    {
        return registeredOutputAreas.TryGetValue(coordinate, out int count) && count > 0;
    }

    public static bool CoordinateBlocksInstallationPlacement(Vector2Int coordinate)
    {
        return placementBlockingAreas.Contains(coordinate);
    }

    private void RegisterCoordinates()
    {
        if (isRegistered || outputCoordinates.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < outputCoordinates.Count; i++)
        {
            Vector2Int coordinate = outputCoordinates[i];
            registeredOutputAreas.TryGetValue(coordinate, out int existingCount);
            registeredOutputAreas[coordinate] = existingCount + 1;
            if (blocksInstallationPlacement)
            {
                placementBlockingAreas.Register(coordinate);
            }
        }

        isRegistered = true;
    }

    private void UnregisterCoordinates()
    {
        if (!isRegistered)
        {
            return;
        }

        for (int i = 0; i < outputCoordinates.Count; i++)
        {
            Vector2Int coordinate = outputCoordinates[i];
            if (!registeredOutputAreas.TryGetValue(coordinate, out int existingCount))
            {
                continue;
            }

            if (existingCount <= 1)
            {
                registeredOutputAreas.Remove(coordinate);
            }
            else
            {
                registeredOutputAreas[coordinate] = existingCount - 1;
            }

            if (blocksInstallationPlacement)
            {
                placementBlockingAreas.Unregister(coordinate);
            }
        }

        isRegistered = false;
    }
}
