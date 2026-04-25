using UnityEngine;
using System.Collections.Generic;

public readonly struct AreaMarkerSpawnRequest
{
    public readonly Vector3 WorldPosition;
    public readonly Sprite Icon;
    public readonly float IconRotationZ;

    public AreaMarkerSpawnRequest(Vector3 worldPosition, Sprite icon, float iconRotationZ = 0f)
    {
        WorldPosition = worldPosition;
        Icon = icon;
        IconRotationZ = iconRotationZ;
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

public class AreaMarker : MonoBehaviour
{
    [SerializeField]
    private SpriteRenderer icon;

    private bool hasCapturedOriginalIconColor;
    private Color originalIconColor;
    private bool hasCapturedOriginalIconLocalRotation;
    private Quaternion originalIconLocalRotation;

    private void Awake()
    {
        CaptureOriginalIconColor();
        CaptureOriginalIconLocalRotation();
    }

    public void SetIcon(Sprite sprite, float iconRotationZ = 0f)
    {
        if (icon == null)
        {
            return;
        }

        CaptureOriginalIconColor();
        CaptureOriginalIconLocalRotation();
        icon.sprite = sprite;
        icon.transform.localRotation = originalIconLocalRotation * Quaternion.Euler(0f, 0f, iconRotationZ);
        PreserveOriginalIconAlpha();
        icon.enabled = sprite != null;
    }

    public void ResetVisuals()
    {
        if (icon == null)
        {
            return;
        }

        CaptureOriginalIconColor();
        CaptureOriginalIconLocalRotation();
        icon.sprite = null;
        icon.transform.localRotation = originalIconLocalRotation;
        PreserveOriginalIconAlpha();
        icon.enabled = false;
    }

    private void CaptureOriginalIconColor()
    {
        if (icon == null || hasCapturedOriginalIconColor)
        {
            return;
        }

        originalIconColor = icon.color;
        hasCapturedOriginalIconColor = true;
    }

    private void CaptureOriginalIconLocalRotation()
    {
        if (icon == null || hasCapturedOriginalIconLocalRotation)
        {
            return;
        }

        originalIconLocalRotation = icon.transform.localRotation;
        hasCapturedOriginalIconLocalRotation = true;
    }

    private void PreserveOriginalIconAlpha()
    {
        if (icon == null || hasCapturedOriginalIconColor == false)
        {
            return;
        }

        Color iconColor = icon.color;
        iconColor.a = originalIconColor.a;
        icon.color = iconColor;
    }
}

public class AreaMarkerPool : MonoBehaviour
{
    private const string DefaultResourcePath = "Prefab/Enviroment/Block/AreaMarker";

    [SerializeField]
    private AreaMarker defaultPrefab;

    private readonly Stack<AreaMarker> pooledMarkers = new Stack<AreaMarker>();
    private Transform poolRoot;

    public void Configure(AreaMarker prefab)
    {
        if (prefab != null && defaultPrefab == null)
        {
            defaultPrefab = prefab;
        }
    }

    public AreaMarker Get(AreaMarker prefabOverride = null)
    {
        AreaMarker prefab = prefabOverride != null ? prefabOverride : ResolveDefaultPrefab();
        if (prefab == null)
        {
            return null;
        }

        if (defaultPrefab == null)
        {
            defaultPrefab = prefab;
        }

        while (pooledMarkers.Count > 0)
        {
            AreaMarker pooled = pooledMarkers.Pop();
            if (pooled == null)
            {
                continue;
            }

            PrepareBorrowedMarker(pooled);
            return pooled;
        }

        AreaMarker created = Instantiate(prefab, GetPoolRoot());
        created.gameObject.SetActive(false);
        PrepareBorrowedMarker(created);
        return created;
    }

    public void Release(AreaMarker marker)
    {
        if (marker == null)
        {
            return;
        }

        marker.ResetVisuals();
        marker.gameObject.SetActive(false);
        marker.transform.SetParent(GetPoolRoot(), false);
        marker.transform.localPosition = Vector3.zero;
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale = Vector3.one;
        pooledMarkers.Push(marker);
    }

    private AreaMarker ResolveDefaultPrefab()
    {
        if (defaultPrefab != null)
        {
            return defaultPrefab;
        }

        defaultPrefab = Resources.Load<AreaMarker>(DefaultResourcePath);
        return defaultPrefab;
    }

    private void PrepareBorrowedMarker(AreaMarker marker)
    {
        marker.ResetVisuals();
        marker.gameObject.SetActive(true);
    }

    private Transform GetPoolRoot()
    {
        if (poolRoot != null)
        {
            return poolRoot;
        }

        GameObject rootObject = new GameObject("AreaMarkerPool");
        rootObject.transform.SetParent(transform, false);
        poolRoot = rootObject.transform;
        return poolRoot;
    }
}

public class InputOutputModuleAreaMarkerController : MonoBehaviour
{
    private readonly List<AreaMarker> activeMarkers = new List<AreaMarker>();

    [SerializeField, Min(0f)]
    private float visibleRange = 5f;

    [SerializeField, Min(0f)]
    private float verticalOffset = 0.03f;

    private AreaMarkerPool areaMarkerPool;
    private bool areMarkersVisible = true;

    public void Configure(AreaMarkerPool pool, IReadOnlyList<AreaMarkerSpawnRequest> markerRequests)
    {
        areaMarkerPool = pool;
        ReleaseMarkers();

        if (areaMarkerPool == null || markerRequests == null || markerRequests.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < markerRequests.Count; i++)
        {
            AreaMarker marker = areaMarkerPool.Get();
            if (marker == null)
            {
                continue;
            }

            AreaMarkerSpawnRequest request = markerRequests[i];
            marker.transform.SetParent(null, true);
            marker.transform.position = request.WorldPosition + Vector3.up * verticalOffset;
            marker.transform.rotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;
            marker.SetIcon(request.Icon, request.IconRotationZ);
            activeMarkers.Add(marker);
        }

        RefreshMarkerVisibility(true);
    }

    public bool ShouldShowLinkedUi()
    {
        return ShouldMarkersBeVisible();
    }

    private void Update()
    {
        RefreshMarkerVisibility(false);
    }

    private void OnDisable()
    {
        ReleaseMarkers();
    }

    private void OnDestroy()
    {
        ReleaseMarkers();
    }

    private void ReleaseMarkers()
    {
        if (activeMarkers.Count <= 0)
        {
            return;
        }

        for (int i = activeMarkers.Count - 1; i >= 0; i--)
        {
            AreaMarker marker = activeMarkers[i];
            if (marker == null)
            {
                continue;
            }

            if (areaMarkerPool != null)
            {
                areaMarkerPool.Release(marker);
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(marker.gameObject);
            }
            else
            {
                DestroyImmediate(marker.gameObject);
            }
        }

        activeMarkers.Clear();
    }

    private void RefreshMarkerVisibility(bool forceRefresh)
    {
        bool shouldBeVisible = ShouldMarkersBeVisible();
        if (!forceRefresh && areMarkersVisible == shouldBeVisible)
        {
            return;
        }

        areMarkersVisible = shouldBeVisible;
        for (int i = 0; i < activeMarkers.Count; i++)
        {
            AreaMarker marker = activeMarkers[i];
            if (marker == null)
            {
                continue;
            }

            if (marker.gameObject.activeSelf != shouldBeVisible)
            {
                marker.gameObject.SetActive(shouldBeVisible);
            }
        }
    }

    private bool ShouldMarkersBeVisible()
    {
        if (activeMarkers.Count <= 0)
        {
            return false;
        }

        if (visibleRange <= 0f)
        {
            return true;
        }

        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null)
        {
            return false;
        }

        Vector3 playerPosition = player.BodyTransform != null ? player.BodyTransform.position : player.transform.position;
        Vector3 mapObjectPosition = transform.position;
        Vector2 playerXZ = new Vector2(playerPosition.x, playerPosition.z);
        Vector2 mapObjectXZ = new Vector2(mapObjectPosition.x, mapObjectPosition.z);
        float visibleRangeSqr = visibleRange * visibleRange;
        return (playerXZ - mapObjectXZ).sqrMagnitude <= visibleRangeSqr;
    }
}

public class InputOutputModuleEnergyAreaController : MonoBehaviour
{
    private static readonly Dictionary<Vector2Int, Dictionary<ItemDefinition.EnergyType, int>> registeredEnergyAreas
        = new Dictionary<Vector2Int, Dictionary<ItemDefinition.EnergyType, int>>();
    private static readonly HashSet<InputOutputModuleEnergyAreaController> activeControllers
        = new HashSet<InputOutputModuleEnergyAreaController>();
    private const int DefaultAreaMaxObjects = 10;

    [SerializeField]
    private ItemDefinition.EnergyType acceptedEnergyType = ItemDefinition.EnergyType.None;

    [SerializeField]
    private List<Vector2Int> inputEnergyCoordinates = new List<Vector2Int>();

    [SerializeField, Min(0.01f)]
    private float depositInterval = 0.1f;

    private float depositTimer;
    private TerrainGenerator cachedTerrain;
    private bool requiresExitBeforeNextDeposit;
    private bool manualPickupRequiresExit;
    private bool wasPlayerInsideArea;
    private bool isRegistered;

    public void Configure(ItemDefinition.EnergyType energyType, IReadOnlyList<Vector2Int> coordinates)
    {
        UnregisterCoordinates();
        acceptedEnergyType = energyType;
        inputEnergyCoordinates.Clear();
        requiresExitBeforeNextDeposit = false;
        manualPickupRequiresExit = false;
        depositTimer = 0f;
        wasPlayerInsideArea = false;

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

    private void OnEnable()
    {
        activeControllers.Add(this);
        RegisterCoordinates();
    }

    private void OnDisable()
    {
        activeControllers.Remove(this);
        UnregisterCoordinates();
    }

    private void OnDestroy()
    {
        activeControllers.Remove(this);
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

    public static void NotifyManualPickupAtCoordinate(Vector2Int coordinate)
    {
        foreach (InputOutputModuleEnergyAreaController controller in activeControllers)
        {
            if (controller == null)
            {
                continue;
            }

            controller.HandleManualPickupAtCoordinate(coordinate);
        }
    }

    private void Update()
    {
        if (acceptedEnergyType == ItemDefinition.EnergyType.None || inputEnergyCoordinates.Count <= 0)
        {
            return;
        }

        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null)
        {
            return;
        }

        bool isInsideArea = IsPlayerInsideInputEnergyArea(player);
        if (!isInsideArea)
        {
            depositTimer = 0f;
            requiresExitBeforeNextDeposit = false;
            manualPickupRequiresExit = false;
            wasPlayerInsideArea = false;
            return;
        }

        if (!wasPlayerInsideArea)
        {
            depositTimer = Mathf.Max(0.01f, depositInterval);
            manualPickupRequiresExit = false;
            wasPlayerInsideArea = true;
        }

        if (manualPickupRequiresExit)
        {
            return;
        }

        if (requiresExitBeforeNextDeposit)
        {
            if (!CanResumeDepositWithoutAreaExit(player))
            {
                return;
            }

            requiresExitBeforeNextDeposit = false;
            depositTimer = Mathf.Max(0.01f, depositInterval);
        }

        depositTimer -= Time.deltaTime;
        if (depositTimer > 0f)
        {
            return;
        }

        depositTimer = Mathf.Max(0.01f, depositInterval);
        if (!TryDepositOneEnergyItem(player))
        {
            requiresExitBeforeNextDeposit = true;
        }
    }

    private void HandleManualPickupAtCoordinate(Vector2Int coordinate)
    {
        for (int i = 0; i < inputEnergyCoordinates.Count; i++)
        {
            if (inputEnergyCoordinates[i] != coordinate)
            {
                continue;
            }

            manualPickupRequiresExit = true;
            requiresExitBeforeNextDeposit = true;
            depositTimer = Mathf.Max(0.01f, depositInterval);
            return;
        }
    }

    private bool IsPlayerInsideInputEnergyArea(Player player)
    {
        if (player == null)
        {
            return false;
        }

        Vector3 playerPosition = player.BodyTransform != null ? player.BodyTransform.position : player.transform.position;
        Vector2Int playerCoordinate = new Vector2Int(
            Mathf.RoundToInt(playerPosition.x),
            Mathf.RoundToInt(playerPosition.z));

        for (int i = 0; i < inputEnergyCoordinates.Count; i++)
        {
            if (inputEnergyCoordinates[i] == playerCoordinate)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryDepositOneEnergyItem(Player player)
    {
        if (player == null)
        {
            return false;
        }

        if (GetInputEnergyAreaObjectCount() >= ResolveAreaMaxObjects())
        {
            return false;
        }

        if (!TryFindMatchingEnergyDeposit(
                player,
                out PlayerBag sourceBag,
                out int slotIndex,
                out int itemId,
                out Vector3 startWorldPosition,
                out Block targetBlock))
        {
            return false;
        }

        if (sourceBag == null || !sourceBag.TryRemoveOneAtSlot(slotIndex, out int removedItemId) || removedItemId != itemId)
        {
            return false;
        }

        if (!targetBlock.TryAddInputAreaCenterObjectAnimated(itemId, startWorldPosition, 0f, out PortableObject droppedObject))
        {
            sourceBag.TryAddObject(slotIndex, itemId, out _);
            return false;
        }

        DroppedItemPickupGate gate = droppedObject != null ? droppedObject.GetComponent<DroppedItemPickupGate>() : null;
        gate?.SetAutoPickupBlocked(true);
        return true;
    }

    private bool CanResumeDepositWithoutAreaExit(Player player)
    {
        if (player == null
            || GetInputEnergyAreaObjectCount() >= ResolveAreaMaxObjects()
            || !HasEmptyInputEnergyBlock()
            || !OwnerModuleHasStoredOperationalEnergy())
        {
            return false;
        }

        return TryFindMatchingEnergyDeposit(
            player,
            out _,
            out _,
            out _,
            out _,
            out _);
    }

    private bool TryFindMatchingEnergyDeposit(
        Player player,
        out PlayerBag sourceBag,
        out int slotIndex,
        out int itemId,
        out Vector3 startWorldPosition,
        out Block targetBlock)
    {
        sourceBag = null;
        slotIndex = -1;
        itemId = -1;
        startWorldPosition = transform.position;
        targetBlock = null;

        if (player == null)
        {
            return false;
        }

        PlayerBag handBag = player.GetHandBag();
        if (TryFindMatchingEnergyItemInBag(handBag, out slotIndex, out itemId, out startWorldPosition, out targetBlock))
        {
            sourceBag = handBag;
            return true;
        }

        PlayerBag bag = player.GetBag();
        if (TryFindMatchingEnergyItemInBag(bag, out slotIndex, out itemId, out startWorldPosition, out targetBlock))
        {
            sourceBag = bag;
            return true;
        }

        return false;
    }

    private bool TryFindMatchingEnergyItemInBag(
        PlayerBag bag,
        out int slotIndex,
        out int itemId,
        out Vector3 startWorldPosition,
        out Block targetBlock)
    {
        slotIndex = -1;
        itemId = -1;
        startWorldPosition = transform.position;
        targetBlock = null;

        if (bag == null)
        {
            return false;
        }

        bag.RefreshExternalStackCounts(false);
        int slotCount = bag.SlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            int candidateItemId = bag.GetSlotItemId(i);
            if (candidateItemId < 0 || bag.GetSlotCount(i) <= 0)
            {
                continue;
            }

            ItemDefinition candidateDefinition = ResolveItemDefinition(candidateItemId);
            if (candidateDefinition == null
                || candidateDefinition.energyType != acceptedEnergyType
                || candidateDefinition.energyAmount <= 0)
            {
                continue;
            }

            if (!TryResolveDepositBlock(candidateItemId, out Block candidateTargetBlock) || candidateTargetBlock == null)
            {
                continue;
            }

            PortableObject topObject = bag.GetTopObject(i);
            if (topObject != null)
            {
                startWorldPosition = topObject.transform.position;
            }
            else if (GameManager.Instance != null && GameManager.Instance.Player != null)
            {
                startWorldPosition = GameManager.Instance.Player.transform.position;
            }

            slotIndex = i;
            itemId = candidateItemId;
            targetBlock = candidateTargetBlock;
            return true;
        }

        return false;
    }

    private int ResolveAreaMaxObjects()
    {
        InputOutputModule inputOutputModule = GetComponent<InputOutputModule>();
        return inputOutputModule != null
            ? inputOutputModule.ResolveRuntimeAreaCapacity(inputEnergyCoordinates)
            : DefaultAreaMaxObjects;
    }

    private int GetInputEnergyAreaObjectCount()
    {
        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return 0;
        }

        int totalCount = 0;
        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < inputEnergyCoordinates.Count; i++)
        {
            Vector2Int coordinate = inputEnergyCoordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            if (!terrain.TryGetLoadedBlock(coordinate, out Block block) || block == null)
            {
                continue;
            }

            totalCount += block.GetInputAreaCenterItemCount();
        }

        return totalCount;
    }

    private bool HasEmptyInputEnergyBlock()
    {
        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return false;
        }

        for (int i = 0; i < inputEnergyCoordinates.Count; i++)
        {
            if (!terrain.TryGetLoadedBlock(inputEnergyCoordinates[i], out Block block) || block == null)
            {
                continue;
            }

            if (block.Type == Block.BlockType.Ground && !block.HasInputAreaCenterObjects())
            {
                return true;
            }
        }

        return false;
    }

    private bool OwnerModuleHasStoredOperationalEnergy()
    {
        InputOutputModule inputOutputModule = GetComponent<InputOutputModule>();
        return inputOutputModule != null && inputOutputModule.HasStoredOperationalEnergy();
    }

    private bool TryResolveDepositBlock(int itemId, out Block targetBlock)
    {
        targetBlock = null;

        if (itemId < 0)
        {
            return false;
        }

        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return false;
        }

        if (GetInputEnergyAreaObjectCount() >= ResolveAreaMaxObjects())
        {
            return false;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireExistingCenterStack = pass == 0;

            for (int i = 0; i < inputEnergyCoordinates.Count; i++)
            {
                if (!terrain.TryGetLoadedBlock(inputEnergyCoordinates[i], out Block block) || block == null)
                {
                    continue;
                }

                if (block.Type != Block.BlockType.Ground || !block.CanAddInputAreaCenterObjects(1, itemId))
                {
                    continue;
                }

                if (requireExistingCenterStack && !block.HasInputAreaCenterItem(itemId))
                {
                    continue;
                }

                targetBlock = block;
                return true;
            }
        }

        return false;
    }

    private TerrainGenerator ResolveTerrain()
    {
        if (cachedTerrain != null)
        {
            return cachedTerrain;
        }

        cachedTerrain = GetComponentInParent<TerrainGenerator>();
        if (cachedTerrain == null)
        {
            cachedTerrain = Object.FindObjectOfType<TerrainGenerator>();
        }

        return cachedTerrain;
    }

    private static ItemDefinition ResolveItemDefinition(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    private void RegisterCoordinates()
    {
        if (isRegistered || acceptedEnergyType == ItemDefinition.EnergyType.None || inputEnergyCoordinates.Count <= 0)
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

            energyCounts.TryGetValue(acceptedEnergyType, out int existingCount);
            energyCounts[acceptedEnergyType] = existingCount + 1;
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

            if (energyCounts.TryGetValue(acceptedEnergyType, out int existingCount))
            {
                if (existingCount <= 1)
                {
                    energyCounts.Remove(acceptedEnergyType);
                }
                else
                {
                    energyCounts[acceptedEnergyType] = existingCount - 1;
                }
            }

            if (energyCounts.Count <= 0)
            {
                registeredEnergyAreas.Remove(coordinate);
            }
        }

        isRegistered = false;
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
    private static readonly HashSet<InputOutputModuleItemAreaController> activeControllers
        = new HashSet<InputOutputModuleItemAreaController>();

    private static readonly Vector2Int InvalidCoordinate = new Vector2Int(int.MinValue, int.MinValue);
    private const int DefaultAreaMaxObjects = 10;

    [SerializeField]
    private List<InputItemAreaEntry> inputItemAreas = new List<InputItemAreaEntry>();

    [SerializeField, Min(0.01f)]
    private float depositInterval = 0.1f;

    private float depositTimer;
    private TerrainGenerator cachedTerrain;
    private bool requiresExitBeforeNextDeposit;
    private bool manualPickupRequiresExit;
    private bool wasPlayerInsideArea;
    private bool isRegistered;
    private Vector2Int activeAreaCoordinate = InvalidCoordinate;

    public void Configure(IReadOnlyList<InputOutputModuleItemAreaBinding> bindings)
    {
        UnregisterCoordinates();
        inputItemAreas.Clear();
        requiresExitBeforeNextDeposit = false;
        manualPickupRequiresExit = false;
        depositTimer = 0f;
        wasPlayerInsideArea = false;
        activeAreaCoordinate = InvalidCoordinate;

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
        activeControllers.Add(this);
        RegisterCoordinates();
    }

    private void OnDisable()
    {
        activeControllers.Remove(this);
        UnregisterCoordinates();
    }

    private void OnDestroy()
    {
        activeControllers.Remove(this);
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

    public static void NotifyManualPickupAtCoordinate(Vector2Int coordinate)
    {
        foreach (InputOutputModuleItemAreaController controller in activeControllers)
        {
            if (controller == null)
            {
                continue;
            }

            controller.HandleManualPickupAtCoordinate(coordinate);
        }
    }

    private void Update()
    {
        if (inputItemAreas.Count <= 0)
        {
            return;
        }

        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null)
        {
            return;
        }

        if (!TryGetPlayerInputItemAreaEntry(player, out InputItemAreaEntry activeEntry))
        {
            depositTimer = 0f;
            requiresExitBeforeNextDeposit = false;
            manualPickupRequiresExit = false;
            wasPlayerInsideArea = false;
            activeAreaCoordinate = InvalidCoordinate;
            return;
        }

        if (!wasPlayerInsideArea || activeEntry.coordinate != activeAreaCoordinate)
        {
            depositTimer = Mathf.Max(0.01f, depositInterval);
            requiresExitBeforeNextDeposit = false;
            manualPickupRequiresExit = false;
            wasPlayerInsideArea = true;
            activeAreaCoordinate = activeEntry.coordinate;
        }

        if (manualPickupRequiresExit)
        {
            return;
        }

        if (requiresExitBeforeNextDeposit)
        {
            if (!CanResumeInputItemDepositWithoutAreaExit(player, activeEntry))
            {
                return;
            }

            requiresExitBeforeNextDeposit = false;
            depositTimer = Mathf.Max(0.01f, depositInterval);
        }

        depositTimer -= Time.deltaTime;
        if (depositTimer > 0f)
        {
            return;
        }

        depositTimer = Mathf.Max(0.01f, depositInterval);
        if (!TryDepositOneInputItem(player, activeEntry.itemId, activeEntry.coordinate))
        {
            requiresExitBeforeNextDeposit = true;
        }
    }

    private void HandleManualPickupAtCoordinate(Vector2Int coordinate)
    {
        for (int i = 0; i < inputItemAreas.Count; i++)
        {
            if (inputItemAreas[i].coordinate != coordinate)
            {
                continue;
            }

            manualPickupRequiresExit = true;
            requiresExitBeforeNextDeposit = true;
            depositTimer = Mathf.Max(0.01f, depositInterval);
            return;
        }
    }

    private bool TryGetPlayerInputItemAreaEntry(Player player, out InputItemAreaEntry activeEntry)
    {
        activeEntry = default;
        if (player == null)
        {
            return false;
        }

        Vector3 playerPosition = player.BodyTransform != null ? player.BodyTransform.position : player.transform.position;
        Vector2Int playerCoordinate = new Vector2Int(
            Mathf.RoundToInt(playerPosition.x),
            Mathf.RoundToInt(playerPosition.z));

        bool foundCoordinateEntry = false;
        InputItemAreaEntry firstCoordinateEntry = default;
        for (int i = 0; i < inputItemAreas.Count; i++)
        {
            InputItemAreaEntry entry = inputItemAreas[i];
            if (entry.coordinate != playerCoordinate)
            {
                continue;
            }

            if (!foundCoordinateEntry)
            {
                firstCoordinateEntry = entry;
                foundCoordinateEntry = true;
            }

            if (CanUseInputItemAreaEntry(player, entry))
            {
                activeEntry = entry;
                return true;
            }
        }

        if (foundCoordinateEntry)
        {
            activeEntry = firstCoordinateEntry;
            return true;
        }

        return false;
    }

    private bool CanUseInputItemAreaEntry(Player player, InputItemAreaEntry entry)
    {
        if (player == null || entry.itemId < 0 || entry.coordinate == InvalidCoordinate)
        {
            return false;
        }

        if (GetInputItemAreaObjectCount() >= ResolveAreaMaxObjects())
        {
            return false;
        }

        if (HasMatchingOutputItem(entry.itemId))
        {
            return TryResolveDepositBlock(entry.coordinate, entry.itemId, out _);
        }

        if (!TryFindMatchingItemSource(
                player,
                entry.itemId,
                out _,
                out _,
                out int itemId,
                out _))
        {
            return false;
        }

        return TryResolveDepositBlock(entry.coordinate, itemId, out _);
    }

    private bool TryDepositOneInputItem(Player player, int acceptedItemId, Vector2Int targetCoordinate)
    {
        if (player == null || acceptedItemId < 0)
        {
            return false;
        }

        if (GetInputItemAreaObjectCount() >= ResolveAreaMaxObjects())
        {
            return false;
        }

        if (TryDepositMatchingOutputItem(acceptedItemId, targetCoordinate))
        {
            return true;
        }

        if (!TryFindMatchingItemSource(
                player,
                acceptedItemId,
                out PlayerBag sourceBag,
                out int slotIndex,
                out int itemId,
                out Vector3 startWorldPosition))
        {
            return false;
        }

        if (!TryResolveDepositBlock(targetCoordinate, itemId, out Block targetBlock) || targetBlock == null)
        {
            return false;
        }

        if (sourceBag == null || !sourceBag.TryRemoveOneAtSlot(slotIndex, out int removedItemId) || removedItemId != itemId)
        {
            return false;
        }

        if (!targetBlock.TryAddInputAreaCenterObjectAnimated(itemId, startWorldPosition, 0f, out PortableObject droppedObject))
        {
            sourceBag.TryAddObject(slotIndex, itemId, out _);
            return false;
        }

        DroppedItemPickupGate gate = droppedObject != null ? droppedObject.GetComponent<DroppedItemPickupGate>() : null;
        gate?.SetAutoPickupBlocked(true);
        return true;
    }

    private bool CanResumeInputItemDepositWithoutAreaExit(Player player, InputItemAreaEntry activeEntry)
    {
        if (player == null
            || activeEntry.itemId < 0
            || activeEntry.coordinate == InvalidCoordinate
            || GetInputItemAreaObjectCount() >= ResolveAreaMaxObjects()
            || !IsInputItemAreaBlockEmpty(activeEntry.coordinate)
            || !OwnerModuleHasActiveOrPendingCraft())
        {
            return false;
        }

        if (HasMatchingOutputItem(activeEntry.itemId))
        {
            return TryResolveDepositBlock(activeEntry.coordinate, activeEntry.itemId, out _);
        }

        return TryFindMatchingItemSource(
            player,
            activeEntry.itemId,
            out _,
            out _,
            out _,
            out _);
    }

    private bool HasMatchingOutputItem(int acceptedItemId)
    {
        InputOutputModule inputOutputModule = GetComponent<InputOutputModule>();
        return inputOutputModule != null && inputOutputModule.HasAvailableOutputItem(acceptedItemId);
    }

    private bool TryDepositMatchingOutputItem(int acceptedItemId, Vector2Int targetCoordinate)
    {
        InputOutputModule inputOutputModule = GetComponent<InputOutputModule>();
        return inputOutputModule != null && inputOutputModule.TryMoveOneOutputItemToInput(acceptedItemId, targetCoordinate);
    }

    private int ResolveAreaMaxObjects()
    {
        InputOutputModule inputOutputModule = GetComponent<InputOutputModule>();
        if (inputOutputModule == null)
        {
            return DefaultAreaMaxObjects;
        }

        List<Vector2Int> coordinates = new List<Vector2Int>(inputItemAreas.Count);
        for (int i = 0; i < inputItemAreas.Count; i++)
        {
            Vector2Int coordinate = inputItemAreas[i].coordinate;
            if (!coordinates.Contains(coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        return inputOutputModule.ResolveRuntimeAreaCapacity(coordinates);
    }

    private int GetInputItemAreaObjectCount()
    {
        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return 0;
        }

        int totalCount = 0;
        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < inputItemAreas.Count; i++)
        {
            Vector2Int coordinate = inputItemAreas[i].coordinate;
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            if (!terrain.TryGetLoadedBlock(coordinate, out Block block) || block == null)
            {
                continue;
            }

            totalCount += block.GetInputAreaCenterItemCount();
        }

        return totalCount;
    }

    private bool TryFindMatchingItemSource(
        Player player,
        int acceptedItemId,
        out PlayerBag sourceBag,
        out int slotIndex,
        out int itemId,
        out Vector3 startWorldPosition)
    {
        sourceBag = null;
        slotIndex = -1;
        itemId = -1;
        startWorldPosition = transform.position;

        if (player == null || acceptedItemId < 0)
        {
            return false;
        }

        PlayerBag handBag = player.GetHandBag();
        if (TryFindMatchingItemInBag(handBag, acceptedItemId, out slotIndex, out itemId, out startWorldPosition))
        {
            sourceBag = handBag;
            return true;
        }

        PlayerBag bag = player.GetBag();
        if (TryFindMatchingItemInBag(bag, acceptedItemId, out slotIndex, out itemId, out startWorldPosition))
        {
            sourceBag = bag;
            return true;
        }

        return false;
    }

    private bool TryFindMatchingItemInBag(
        PlayerBag bag,
        int acceptedItemId,
        out int slotIndex,
        out int itemId,
        out Vector3 startWorldPosition)
    {
        slotIndex = -1;
        itemId = -1;
        startWorldPosition = transform.position;

        if (bag == null || acceptedItemId < 0)
        {
            return false;
        }

        bag.RefreshExternalStackCounts(false);
        int slotCount = bag.SlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            int candidateItemId = bag.GetSlotItemId(i);
            if (candidateItemId != acceptedItemId || bag.GetSlotCount(i) <= 0)
            {
                continue;
            }

            PortableObject topObject = bag.GetTopObject(i);
            if (topObject != null)
            {
                startWorldPosition = topObject.transform.position;
            }
            else if (GameManager.Instance != null && GameManager.Instance.Player != null)
            {
                startWorldPosition = GameManager.Instance.Player.transform.position;
            }

            slotIndex = i;
            itemId = candidateItemId;
            return true;
        }

        return false;
    }

    private bool TryResolveDepositBlock(Vector2Int targetCoordinate, int itemId, out Block targetBlock)
    {
        targetBlock = null;
        if (itemId < 0)
        {
            return false;
        }

        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return false;
        }

        if (GetInputItemAreaObjectCount() >= ResolveAreaMaxObjects())
        {
            return false;
        }

        if (!terrain.TryGetLoadedBlock(targetCoordinate, out Block block) || block == null)
        {
            return false;
        }

        if (block.Type != Block.BlockType.Ground || !block.CanAddInputAreaCenterObjects(1, itemId))
        {
            return false;
        }

        targetBlock = block;
        return true;
    }

    private bool IsInputItemAreaBlockEmpty(Vector2Int targetCoordinate)
    {
        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return false;
        }

        if (!terrain.TryGetLoadedBlock(targetCoordinate, out Block block) || block == null)
        {
            return false;
        }

        return block.Type == Block.BlockType.Ground && !block.HasInputAreaCenterObjects();
    }

    private bool OwnerModuleHasActiveOrPendingCraft()
    {
        InputOutputModule inputOutputModule = GetComponent<InputOutputModule>();
        return inputOutputModule != null && inputOutputModule.HasActiveOrPendingCraft();
    }

    private TerrainGenerator ResolveTerrain()
    {
        if (cachedTerrain != null)
        {
            return cachedTerrain;
        }

        cachedTerrain = GetComponentInParent<TerrainGenerator>();
        if (cachedTerrain == null)
        {
            cachedTerrain = Object.FindObjectOfType<TerrainGenerator>();
        }

        return cachedTerrain;
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
        }

        isRegistered = false;
    }
}

public class InputOutputModuleOutputAreaController : MonoBehaviour
{
    private static readonly Dictionary<Vector2Int, int> registeredOutputAreas
        = new Dictionary<Vector2Int, int>();

    [SerializeField]
    private List<Vector2Int> outputCoordinates = new List<Vector2Int>();

    [SerializeField, Min(0.01f)]
    private float pickupInterval = 0.1f;

    private float pickupTimer;
    private TerrainGenerator cachedTerrain;
    private bool isRegistered;

    public void Configure(IReadOnlyList<Vector2Int> coordinates)
    {
        UnregisterCoordinates();
        outputCoordinates.Clear();
        pickupTimer = 0f;

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

    private void Update()
    {
        if (outputCoordinates.Count <= 0)
        {
            return;
        }

        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null)
        {
            return;
        }

        if (!TryGetPlayerOutputCoordinate(player, out Vector2Int outputCoordinate))
        {
            pickupTimer = 0f;
            return;
        }

        pickupTimer -= Time.deltaTime;
        if (pickupTimer > 0f)
        {
            return;
        }

        pickupTimer = Mathf.Max(0.01f, pickupInterval);
        TryPickupOneOutputItem(player, outputCoordinate);
    }

    private bool TryGetPlayerOutputCoordinate(Player player, out Vector2Int outputCoordinate)
    {
        outputCoordinate = default;
        if (player == null)
        {
            return false;
        }

        Vector3 playerPosition = player.BodyTransform != null ? player.BodyTransform.position : player.transform.position;
        Vector2Int playerCoordinate = new Vector2Int(
            Mathf.RoundToInt(playerPosition.x),
            Mathf.RoundToInt(playerPosition.z));

        for (int i = 0; i < outputCoordinates.Count; i++)
        {
            if (outputCoordinates[i] != playerCoordinate)
            {
                continue;
            }

            outputCoordinate = playerCoordinate;
            return true;
        }

        return false;
    }

    private bool TryPickupOneOutputItem(Player player, Vector2Int outputCoordinate)
    {
        if (player == null)
        {
            return false;
        }

        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null || !terrain.TryGetLoadedBlock(outputCoordinate, out Block block) || block == null)
        {
            return false;
        }

        if (block.Type != Block.BlockType.Ground)
        {
            return false;
        }

        Vector3 anchorPosition = new Vector3(outputCoordinate.x, player.transform.position.y, outputCoordinate.y);
        return block.TryPickupOneInputAreaCenterObjectToBag(player, anchorPosition, 999f, -1);
    }

    private TerrainGenerator ResolveTerrain()
    {
        if (cachedTerrain != null)
        {
            return cachedTerrain;
        }

        cachedTerrain = GetComponentInParent<TerrainGenerator>();
        if (cachedTerrain == null)
        {
            cachedTerrain = Object.FindObjectOfType<TerrainGenerator>();
        }

        return cachedTerrain;
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
        }

        isRegistered = false;
    }
}
