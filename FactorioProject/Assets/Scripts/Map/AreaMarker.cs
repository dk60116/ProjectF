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
    private const string LateRenderShaderName = "Custom/MapFocusOverlay";
    private const string LateRenderMaterialResourcePath = "Materials/AreaMarkerLateRender";
    private const string LateRenderFallbackMaterialName = "AreaMarkerLateRender_Runtime";
    private const int LateRenderQueue = 5000;

    [SerializeField]
    private SpriteRenderer icon;

    private bool hasCapturedOriginalIconColor;
    private Color originalIconColor;
    private bool hasCapturedOriginalIconLocalRotation;
    private Quaternion originalIconLocalRotation;
    private bool hasCapturedOriginalRendererState;
    private SpriteRenderer[] capturedSpriteRenderers;
    private int[] originalSortingOrders;
    private Material[] originalSharedMaterials;
    private Sprite currentIconSprite;
    private float currentIconRotationZ;
    private bool currentIconInitialized;
    private int currentSortingOrderOffset;
    private bool currentSortingOrderInitialized;
    private bool currentRenderOnTop;
    private bool currentRenderOnTopInitialized;
    private static Material lateRenderMaterial;

    private void Awake()
    {
        CaptureOriginalIconColor();
        CaptureOriginalIconLocalRotation();
        CaptureOriginalRendererState();
    }

    public void SetIcon(Sprite sprite, float iconRotationZ = 0f)
    {
        if (icon == null)
        {
            return;
        }

        CaptureOriginalIconColor();
        CaptureOriginalIconLocalRotation();
        if (currentIconInitialized
            && currentIconSprite == sprite
            && Mathf.Approximately(currentIconRotationZ, iconRotationZ))
        {
            return;
        }

        icon.sprite = sprite;
        icon.transform.localRotation = originalIconLocalRotation * Quaternion.Euler(0f, 0f, iconRotationZ);
        PreserveOriginalIconAlpha();
        icon.enabled = sprite != null;
        currentIconSprite = sprite;
        currentIconRotationZ = iconRotationZ;
        currentIconInitialized = true;
    }

    public void SetSortingOrderOffset(int sortingOrderOffset)
    {
        CaptureOriginalRendererState();
        if (capturedSpriteRenderers == null || originalSortingOrders == null)
        {
            return;
        }

        if (currentSortingOrderInitialized && currentSortingOrderOffset == sortingOrderOffset)
        {
            return;
        }

        for (int i = 0; i < capturedSpriteRenderers.Length && i < originalSortingOrders.Length; i++)
        {
            SpriteRenderer spriteRenderer = capturedSpriteRenderers[i];
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = originalSortingOrders[i] + sortingOrderOffset;
            }
        }

        currentSortingOrderOffset = sortingOrderOffset;
        currentSortingOrderInitialized = true;
    }

    public void SetRenderOnTop(bool renderOnTop)
    {
        CaptureOriginalRendererState();
        if (capturedSpriteRenderers == null || originalSharedMaterials == null)
        {
            return;
        }

        if (currentRenderOnTopInitialized && currentRenderOnTop == renderOnTop)
        {
            return;
        }

        Material renderOnTopMaterial = renderOnTop ? ResolveLateRenderMaterial() : null;
        for (int i = 0; i < capturedSpriteRenderers.Length && i < originalSharedMaterials.Length; i++)
        {
            SpriteRenderer spriteRenderer = capturedSpriteRenderers[i];
            if (spriteRenderer == null)
            {
                continue;
            }

            spriteRenderer.sharedMaterial = renderOnTop && renderOnTopMaterial != null
                ? renderOnTopMaterial
                : originalSharedMaterials[i];
        }

        currentRenderOnTop = renderOnTop;
        currentRenderOnTopInitialized = true;
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
        currentIconSprite = null;
        currentIconRotationZ = 0f;
        currentIconInitialized = true;
        currentSortingOrderInitialized = false;
        currentRenderOnTopInitialized = false;
        ResetRendererSorting();
        SetRenderOnTop(false);
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

    private void CaptureOriginalRendererState()
    {
        if (hasCapturedOriginalRendererState)
        {
            return;
        }

        capturedSpriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalSortingOrders = new int[capturedSpriteRenderers.Length];
        originalSharedMaterials = new Material[capturedSpriteRenderers.Length];
        for (int i = 0; i < capturedSpriteRenderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = capturedSpriteRenderers[i];
            originalSortingOrders[i] = spriteRenderer != null
                ? spriteRenderer.sortingOrder
                : 0;
            originalSharedMaterials[i] = spriteRenderer != null
                ? spriteRenderer.sharedMaterial
                : null;
        }

        hasCapturedOriginalRendererState = true;
    }

    private void ResetRendererSorting()
    {
        CaptureOriginalRendererState();
        if (capturedSpriteRenderers == null || originalSortingOrders == null)
        {
            return;
        }

        for (int i = 0; i < capturedSpriteRenderers.Length && i < originalSortingOrders.Length; i++)
        {
            SpriteRenderer spriteRenderer = capturedSpriteRenderers[i];
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = originalSortingOrders[i];
            }
        }
    }

    private static Material ResolveLateRenderMaterial()
    {
        if (lateRenderMaterial != null)
        {
            return lateRenderMaterial;
        }

        lateRenderMaterial = Resources.Load<Material>(LateRenderMaterialResourcePath);
        if (lateRenderMaterial != null)
        {
            return lateRenderMaterial;
        }

        Shader lateRenderShader = Shader.Find(LateRenderShaderName);
        if (lateRenderShader == null)
        {
            return null;
        }

        lateRenderMaterial = new Material(lateRenderShader)
        {
            name = LateRenderFallbackMaterialName,
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = LateRenderQueue
        };
        return lateRenderMaterial;
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
    private bool forceMarkerVisibility;
    private int markerSortingOrderOffset;
    private bool renderMarkersOnTop;
    private float markerVerticalOffset;

    public void Configure(
        AreaMarkerPool pool,
        IReadOnlyList<AreaMarkerSpawnRequest> markerRequests,
        bool forceVisible = false,
        int sortingOrderOffset = 0,
        bool renderOnTop = false,
        Transform markerParent = null,
        float? verticalOffsetOverride = null)
    {
        if (areaMarkerPool != null && areaMarkerPool != pool)
        {
            ReleaseMarkers();
        }

        areaMarkerPool = pool;
        forceMarkerVisibility = forceVisible;
        markerSortingOrderOffset = sortingOrderOffset;
        renderMarkersOnTop = renderOnTop;
        markerVerticalOffset = verticalOffsetOverride.HasValue
            ? Mathf.Max(0f, verticalOffsetOverride.Value)
            : verticalOffset;

        if (areaMarkerPool == null || markerRequests == null || markerRequests.Count <= 0)
        {
            ReleaseMarkers();
            return;
        }

        SyncMarkerCount(markerRequests.Count);
        for (int i = 0; i < markerRequests.Count && i < activeMarkers.Count; i++)
        {
            AreaMarker marker = activeMarkers[i];
            if (marker == null)
            {
                continue;
            }

            AreaMarkerSpawnRequest request = markerRequests[i];
            ConfigureMarker(marker, request, markerParent);
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

    private void SyncMarkerCount(int markerCount)
    {
        int desiredCount = Mathf.Max(0, markerCount);
        for (int i = activeMarkers.Count - 1; i >= desiredCount; i--)
        {
            AreaMarker marker = activeMarkers[i];
            activeMarkers.RemoveAt(i);
            if (marker == null)
            {
                continue;
            }

            if (areaMarkerPool != null)
            {
                areaMarkerPool.Release(marker);
            }
            else if (Application.isPlaying)
            {
                Destroy(marker.gameObject);
            }
            else
            {
                DestroyImmediate(marker.gameObject);
            }
        }

        while (activeMarkers.Count < desiredCount)
        {
            AreaMarker marker = areaMarkerPool != null ? areaMarkerPool.Get() : null;
            if (marker == null)
            {
                break;
            }

            activeMarkers.Add(marker);
        }
    }

    private void ConfigureMarker(
        AreaMarker marker,
        AreaMarkerSpawnRequest request,
        Transform markerParent)
    {
        if (marker == null)
        {
            return;
        }

        Transform markerTransform = marker.transform;
        Vector3 targetPosition = request.WorldPosition + Vector3.up * markerVerticalOffset;
        if ((markerTransform.position - targetPosition).sqrMagnitude > 0.000001f)
        {
            markerTransform.position = targetPosition;
        }

        if (Mathf.Abs(Quaternion.Dot(markerTransform.rotation, Quaternion.identity)) < 0.9999f)
        {
            markerTransform.rotation = Quaternion.identity;
        }

        if (markerTransform.localScale != Vector3.one)
        {
            markerTransform.localScale = Vector3.one;
        }

        if (markerParent != null)
        {
            if (markerTransform.parent != markerParent)
            {
                markerTransform.SetParent(markerParent, true);
            }
        }
        else if (markerTransform.parent != null)
        {
            markerTransform.SetParent(null, true);
        }

        marker.SetIcon(request.Icon, request.IconRotationZ);
        marker.SetSortingOrderOffset(markerSortingOrderOffset);
        marker.SetRenderOnTop(renderMarkersOnTop);
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

        if (forceMarkerVisibility)
        {
            return true;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager != null && (gameManager.InstallationPlacementActive || gameManager.MapEditActive))
        {
            return true;
        }

        Player player = gameManager != null ? gameManager.Player : null;
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

    [SerializeField]
    private ItemDefinition.EnergyType acceptedEnergyType = ItemDefinition.EnergyType.None;

    [SerializeField]
    private List<Vector2Int> inputEnergyCoordinates = new List<Vector2Int>();

    private bool isRegistered;

    public void Configure(ItemDefinition.EnergyType energyType, IReadOnlyList<Vector2Int> coordinates)
    {
        UnregisterCoordinates();
        acceptedEnergyType = energyType;
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

    [SerializeField]
    private List<InputItemAreaEntry> inputItemAreas = new List<InputItemAreaEntry>();

    private bool isRegistered;

    public void Configure(IReadOnlyList<InputOutputModuleItemAreaBinding> bindings)
    {
        UnregisterCoordinates();
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

    private bool isRegistered;

    public void Configure(IReadOnlyList<Vector2Int> coordinates)
    {
        UnregisterCoordinates();
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
