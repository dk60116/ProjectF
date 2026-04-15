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

    private AreaMarkerPool areaMarkerPool;

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
            marker.transform.position = request.WorldPosition + Vector3.up * 0.001f;
            marker.transform.rotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;
            marker.SetIcon(request.Icon, request.IconRotationZ);
            activeMarkers.Add(marker);
        }
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
}

public class InputOutputModuleEnergyAreaController : MonoBehaviour
{
    private static readonly Dictionary<Vector2Int, Dictionary<ItemDefinition.EnergyType, int>> registeredEnergyAreas
        = new Dictionary<Vector2Int, Dictionary<ItemDefinition.EnergyType, int>>();

    [SerializeField]
    private ItemDefinition.EnergyType acceptedEnergyType = ItemDefinition.EnergyType.None;

    [SerializeField]
    private List<Vector2Int> inputEnergyCoordinates = new List<Vector2Int>();

    [SerializeField, Min(0.01f)]
    private float depositInterval = 0.1f;

    private float depositTimer;
    private TerrainGenerator cachedTerrain;
    private bool requiresExitBeforeNextDeposit;
    private bool wasPlayerInsideArea;
    private bool isRegistered;

    public void Configure(ItemDefinition.EnergyType energyType, IReadOnlyList<Vector2Int> coordinates)
    {
        UnregisterCoordinates();
        acceptedEnergyType = energyType;
        inputEnergyCoordinates.Clear();
        requiresExitBeforeNextDeposit = false;
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
            wasPlayerInsideArea = false;
            return;
        }

        if (!wasPlayerInsideArea)
        {
            depositTimer = Mathf.Max(0.01f, depositInterval);
            wasPlayerInsideArea = true;
        }

        if (requiresExitBeforeNextDeposit)
        {
            return;
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

        if (!TryFindMatchingEnergyItemSource(
                player,
                out PlayerBag sourceBag,
                out int slotIndex,
                out int itemId,
                out Vector3 startWorldPosition))
        {
            return false;
        }

        if (!TryResolveDepositBlock(itemId, out Block targetBlock) || targetBlock == null)
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

    private bool TryFindMatchingEnergyItemSource(
        Player player,
        out PlayerBag sourceBag,
        out int slotIndex,
        out int itemId,
        out Vector3 startWorldPosition)
    {
        sourceBag = null;
        slotIndex = -1;
        itemId = -1;
        startWorldPosition = transform.position;

        if (player == null)
        {
            return false;
        }

        PlayerBag handBag = player.GetHandBag();
        if (TryFindMatchingEnergyItemInBag(handBag, out slotIndex, out itemId, out startWorldPosition))
        {
            sourceBag = handBag;
            return true;
        }

        PlayerBag bag = player.GetBag();
        if (TryFindMatchingEnergyItemInBag(bag, out slotIndex, out itemId, out startWorldPosition))
        {
            sourceBag = bag;
            return true;
        }

        return false;
    }

    private bool TryFindMatchingEnergyItemInBag(PlayerBag bag, out int slotIndex, out int itemId, out Vector3 startWorldPosition)
    {
        slotIndex = -1;
        itemId = -1;
        startWorldPosition = transform.position;

        if (bag == null)
        {
            return false;
        }

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
