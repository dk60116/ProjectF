using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class InstallationPlacementController : MonoBehaviour
{
    private const string InstallGridOverlayShaderName = "Custom/InstallGridOverlay";
    private const int ConveyorStackStateSentinel = -1000000002;
    private const int InstallPreviewAreaMarkerSortingOrderOffset = 6000;

    [SerializeField]
    private Button installButton;
    [SerializeField]
    private Button installCancelButton;
    [SerializeField]
    private Button installRotationButton;
    [SerializeField]
    private Button installCompleteButton;
    [SerializeField]
    private Button mapEditButton;
    [SerializeField]
    private Button mapEditCancelButton;
    [SerializeField]
    private Button mapEditRotationButton;
    [SerializeField]
    private Button mapEditCompleteButton;
    [SerializeField]
    private Button mapEditPackButton;
    [SerializeField]
    private Button mapEditUndoButton;
    [SerializeField]
    private Color installPreviewTint = new Color(0.45f, 0.95f, 1f, 0.85f);
    [SerializeField, Min(0f)]
    private float installPreviewVerticalOffset = 0.05f;
    [SerializeField]
    private Color installGridColor = new Color(1f, 1f, 1f, 0.55f);
    [SerializeField]
    private Color installGridBlockedFillColor = new Color(1f, 0.2f, 0.2f, 0.22f);
    [SerializeField, Min(0f)]
    private float installPlacementPortableLaunchInterval = 0.04f;
    [SerializeField, Min(0.01f)]
    private float installPlacementScaleDuration = 0.2f;
    [SerializeField]
    private Ease installPlacementScaleEase = Ease.OutBack;
    [SerializeField]
    private Color installGridBlockedLineColor = new Color(1f, 0.2f, 0.2f, 0.9f);
    [SerializeField]
    private Color installGridConveyorEndDebugColor = new Color(1f, 0.1f, 0.1f, 0.95f);
    [SerializeField, Min(0f)]
    private float installGridVerticalOffset = 0.075f;
    [SerializeField, Min(0.005f)]
    private float installGridLineWidth = 0.03f;
    [SerializeField, Min(0.05f)]
    private float installGridConveyorArrowInset = 0.33f;
    [SerializeField, Min(0.03f)]
    private float installGridConveyorArrowHeadLength = 0.12f;
    [SerializeField, Min(0.03f)]
    private float installGridConveyorArrowHeadWidth = 0.16f;
    [SerializeField, Min(0.05f)]
    private float installGridRefreshInterval = 0.35f;
    [SerializeField, Min(1f)]
    private float installRotateTapThreshold = 12f;

    private ItemDefinition activeInstallDefinition;
    private MapObject activeInstallPreview;
    private Quaternion activeInstallBaseRotation = Quaternion.identity;
    private Camera installPreviewCamera;
    private TerrainGenerator installPreviewTerrain;
    private AreaMarkerPool areaMarkerPool;
    private MaterialPropertyBlock installPreviewPropertyBlock;
    private bool waitForPointerReleaseAfterPreviewSpawn;
    private bool isPreviewPointerTracking;
    private bool previewPointerDragged;
    private bool previewPointerStartedOverUi;
    private Vector2 previewPointerStartPosition;
    private MapObject previewPointerOriginPreview;
    private int installPreviewQuarterTurns;
    private GameObject installGridObject;
    private MeshFilter installGridMeshFilter;
    private MeshRenderer installGridMeshRenderer;
    private Mesh installGridMesh;
    private Material installGridMaterial;
    private float installGridRefreshTimer;
    private Vector2Int installGridMinCoordinate;
    private Vector2Int installGridMaxCoordinate;
    private readonly List<MapObject> installPreviewInstances = new List<MapObject>();
    private readonly Dictionary<MapObject, int> installPreviewQuarterTurnsByPreview = new Dictionary<MapObject, int>();
    private readonly Dictionary<MapObject, Quaternion> installPreviewBaseRotationsByPreview = new Dictionary<MapObject, Quaternion>();
    private readonly Dictionary<MapObject, Vector2Int> installPreviewAnchorCoordinates = new Dictionary<MapObject, Vector2Int>();
    private readonly Dictionary<MapObject, MapObject> installPreviewSourcePrefabsByPreview = new Dictionary<MapObject, MapObject>();
    private readonly Dictionary<MapObject, long> installPreviewPlacementSequencesByPreview = new Dictionary<MapObject, long>();
    private readonly Dictionary<MapObject, InstallPreviewItemReservation> installPreviewItemReservationsByPreview = new Dictionary<MapObject, InstallPreviewItemReservation>();
    private readonly Dictionary<int, int> lastBlueprintQuarterTurnsByItemId = new Dictionary<int, int>();
    private readonly Dictionary<int, int> lastInstalledQuarterTurnsByItemId = new Dictionary<int, int>();
    private InstallationObject selectedEditableInstallation;
    private Vector2Int selectedEditableAnchorCoordinate;
    private InstallationEditSession activeInstallationEditSession;
    private readonly Stack<PackedInstallationSession> packedInstallationHistory = new Stack<PackedInstallationSession>();
    private bool mapEditModeActive;
    private ConveyorPreviewVariantMode installPreviewConveyorVariantMode = ConveyorPreviewVariantMode.Straight;
    private bool hasLastBlueprintQuarterTurns;
    private int lastBlueprintQuarterTurns;
    private bool hasLastInstalledQuarterTurns;
    private int lastInstalledQuarterTurns;
    private bool preferDifferentConveyorCornerOnNextRefresh;

    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

    private sealed class InstallationEditSession
    {
        public InstallationObject originalInstallation;
        public ItemDefinition definition;
        public Vector2Int originalAnchorCoordinate;
        public int originalQuarterTurns;
        public Quaternion originalRotation = Quaternion.identity;
        public int originalConveyorVariantKind = -1;
        public List<Vector2Int> originalOccupiedCoordinates = new List<Vector2Int>();
        public List<Vector2Int> originalStateCoordinates = new List<Vector2Int>();
        public List<AreaAttachedBoxState> attachedAreaBoxes = new List<AreaAttachedBoxState>();
        public Dictionary<Vector2Int, List<int>> blockStatesByCanonicalOffset = new Dictionary<Vector2Int, List<int>>();
        public InputOutputModule.PersistentState inputOutputState;
        public bool? boxIsOpen;
        public bool itemFilterMaskInitialized;
        public List<ulong> itemFilterMaskWords = new List<ulong>();
    }

    private sealed class AreaAttachedBoxState
    {
        public BoxObject boxObject;
        public ItemDefinition definition;
        public Vector2Int originalAnchorCoordinate;
        public int originalQuarterTurns;
        public Quaternion originalRotation = Quaternion.identity;
        public Vector2Int canonicalAnchorOffset;
        public bool? boxIsOpen;
        public bool itemFilterMaskInitialized;
        public List<ulong> itemFilterMaskWords = new List<ulong>();
    }

    private enum ConveyorPreviewVariantMode
    {
        Straight,
        Corner
    }

    private sealed class PackedInstallationSession
    {
        public InstallationEditSession editSession;
        public Vector2Int anchorCoordinate;
        public int quarterTurns;
        public int conveyorVariantKind = -1;
        public int itemId;
        public Vector2Int dropCoordinate;
        public PortableObject portableObject;
        public Dictionary<Vector2Int, List<int>> pendingDroppedBlockStatesByCanonicalOffset = new Dictionary<Vector2Int, List<int>>();
    }

    private sealed class ConveyorChangeInfo
    {
        public Vector2Int coordinate;
        public List<Vector2Int> occupiedCoordinates = new List<Vector2Int>();
        public Vector2Int inputDirection;
        public Vector2Int outputDirection;
        public Quaternion rotation = Quaternion.identity;
        public bool isCornerVariant;
    }

    private sealed class InstallPreviewPlacementPlan
    {
        public MapObject preview;
        public Block anchorBlock;
        public MapObject sourcePrefab;
        public int quarterTurns;
        public List<Block> footprintBlocks = new List<Block>();
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
    }

    private sealed class InstallPreviewPlacementReservation
    {
        public MapObject sourcePrefab;
        public Vector2Int anchorCoordinate;
        public int quarterTurns;
        public List<Vector2Int> footprintCoordinates = new List<Vector2Int>();
    }

    private sealed class InstallPreviewItemReservation
    {
        public int itemId = -1;
        public bool fromHand;
        public int sourceSlotIndex = -1;
        public PortableObject sourcePortableObject;
    }

    private void Awake()
    {
        ResolveInstallButtons();
        BindInstallButtons();
        SetInstallButtonVisible(false);
        RefreshMapEditButtonState();
    }

    private void OnEnable()
    {
        ResolveInstallButtons();
        BindInstallButtons();
        RefreshInstallButton();
        RefreshMapEditButtonState();
    }

    private void Update()
    {
        ResolveInstallButtons();
        RefreshInstallButton();
        RefreshMapEditButtonState();
        if (TryHandleModeCancelInput())
        {
            return;
        }

        UpdateInstallGrid(Time.deltaTime);

        if (!IsInstallationModeActive())
        {
            if (mapEditModeActive)
            {
                UpdateMapEditSelectionInput();
            }
            return;
        }

        if (waitForPointerReleaseAfterPreviewSpawn)
        {
            if (IsPrimaryPointerHeld())
            {
                return;
            }

            waitForPointerReleaseAfterPreviewSpawn = false;
        }

        UpdateInstallPreviewPointerInput();
    }

    private bool TryHandleModeCancelInput()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
        {
            return false;
        }

        if (!IsEditingInstallation() && !IsInstallationModeActive() && !mapEditModeActive)
        {
            return false;
        }

        HandleInstallCancelClicked();
        return true;
    }

    private void OnDisable()
    {
        ClearEditableInstallationSelection();
        mapEditModeActive = false;
        GameManager.Instance?.SetMapEditActive(false);
        FinalizePackedInstallationHistory();
        if (IsEditingInstallation())
        {
            CancelInstallationEdit();
        }
        else
        {
            ClearInstallPreview();
        }
        SetInstallButtonVisible(false);
        RefreshMapEditButtonState();
    }

    private void OnDestroy()
    {
        UnbindInstallButtons();
        mapEditModeActive = false;
        GameManager.Instance?.SetMapEditActive(false);
        FinalizePackedInstallationHistory();
        if (IsEditingInstallation())
        {
            CancelInstallationEdit();
        }
        else
        {
            ClearInstallPreview();
        }
        ReleaseInstallGridResources();
    }

    public void SetInstallButtons(Button button, Button cancelButton, Button rotationButton, Button completeButton)
    {
        bool isSameBinding = installButton == button
                             && installCancelButton == cancelButton
                             && installRotationButton == rotationButton
                             && installCompleteButton == completeButton
                             && installButton != null;
        if (isSameBinding)
        {
            return;
        }

        UnbindInstallButtons();
        installButton = button;
        installCancelButton = cancelButton;
        installRotationButton = rotationButton;
        installCompleteButton = completeButton;
        ResolveInstallButtons();
        BindInstallButtons();
        RefreshInstallButton();
    }

    public void SetInstallButton(Button button)
    {
        SetInstallButtons(button, installCancelButton, installRotationButton, installCompleteButton);
    }

    public void SetMapEditButtons(Button button, Button cancelButton, Button rotationButton, Button completeButton, Button packButton, Button undoButton)
    {
        bool isSameBinding = mapEditButton == button
                             && mapEditCancelButton == cancelButton
                             && mapEditRotationButton == rotationButton
                             && mapEditCompleteButton == completeButton
                             && mapEditPackButton == packButton
                             && mapEditUndoButton == undoButton
                             && mapEditButton != null;
        if (isSameBinding)
        {
            return;
        }

        UnbindButton(mapEditButton, HandleMapEditButtonClicked);
        UnbindButton(mapEditCancelButton, HandleInstallCancelClicked);
        UnbindButton(mapEditRotationButton, HandleInstallRotationClicked);
        UnbindButton(mapEditCompleteButton, HandleInstallCompleteClicked);
        UnbindButton(mapEditPackButton, HandleMapEditPackClicked);
        UnbindButton(mapEditUndoButton, HandleMapEditUndoClicked);
        mapEditButton = button;
        mapEditCancelButton = cancelButton;
        mapEditRotationButton = rotationButton;
        mapEditCompleteButton = completeButton;
        mapEditPackButton = packButton;
        mapEditUndoButton = undoButton;
        ResolveInstallButtons();
        BindButton(mapEditButton, HandleMapEditButtonClicked);
        BindButton(mapEditCancelButton, HandleInstallCancelClicked);
        BindButton(mapEditRotationButton, HandleInstallRotationClicked);
        BindButton(mapEditCompleteButton, HandleInstallCompleteClicked);
        BindButton(mapEditPackButton, HandleMapEditPackClicked);
        BindButton(mapEditUndoButton, HandleMapEditUndoClicked);
        RefreshMapEditButtonState();
    }

    private void ResolveInstallButtons()
    {
        PlayerHUD playerHud = GetComponent<PlayerHUD>();
        if (installButton == null && playerHud != null && playerHud.InstallButton != null)
        {
            installButton = playerHud.InstallButton;
        }

        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button candidate = buttons[i];
            if (candidate == null)
            {
                continue;
            }

            if (installButton == null && candidate.name == "InstallButton")
            {
                installButton = candidate;
                continue;
            }

            if (installCancelButton == null)
            {
                if ((playerHud != null && playerHud.InstallCancelButton == candidate) || candidate.name == "InstallCancelButton")
                {
                    installCancelButton = candidate;
                    continue;
                }
            }

            if (installRotationButton == null)
            {
                if ((playerHud != null && playerHud.InstallRotationButton == candidate)
                    || candidate.name == "RotationButton"
                    || candidate.name == "InstallRotationButton")
                {
                    installRotationButton = candidate;
                    continue;
                }
            }

            if (installCompleteButton == null)
            {
                if ((playerHud != null && playerHud.InstallCompleteButton == candidate)
                    || candidate.name == "CompleteButton"
                    || candidate.name == "InstallCompleteButton")
                {
                    installCompleteButton = candidate;
                    continue;
                }
            }

            if (mapEditButton == null)
            {
                if ((playerHud != null && playerHud.MapEditButton == candidate) || candidate.name == "MapEditButton")
                {
                    mapEditButton = candidate;
                }
            }

            if (mapEditPackButton == null && (candidate.name == "PackButton" || candidate.name == "Pack"))
            {
                mapEditPackButton = candidate;
            }

            if (mapEditUndoButton == null && (candidate.name == "UnDoButton" || candidate.name == "UndoButton"))
            {
                mapEditUndoButton = candidate;
            }
        }

        if (playerHud != null)
        {
            if (mapEditCancelButton == null)
            {
                mapEditCancelButton = playerHud.MapEditCancelButton;
            }

            if (mapEditRotationButton == null)
            {
                mapEditRotationButton = playerHud.MapEditRotationButton;
            }

            if (mapEditCompleteButton == null)
            {
                mapEditCompleteButton = playerHud.MapEditCompleteButton;
            }
        }
    }

    private void UpdateInstallButtonVisibility(PlayerBag handBag)
    {
        if (mapEditModeActive)
        {
            SetInstallButtonVisible(false);
            return;
        }

        if (IsEditingInstallation())
        {
            SetInstallButtonVisible(false);
            return;
        }

        int handItemId = handBag != null ? handBag.GetSlotItemId(0) : -1;
        SetInstallButtonVisible(IsInstallationItem(handItemId));
    }

    private void RefreshInstallButton()
    {
        PlayerBag handBag = GetPlayerHandBag();
        UpdateInstallButtonVisibility(handBag);

        if (!IsInstallationModeActive())
        {
            activeInstallDefinition = null;
            activeInstallPreview = null;
            return;
        }

        if (IsEditingInstallation())
        {
            CleanupInstallPreviewReferences();
            EnsureValidActiveInstallPreview();
            return;
        }

        int handItemId = handBag != null ? handBag.GetSlotItemId(0) : -1;
        int handItemCount = handBag != null ? handBag.GetSlotCount(0) : 0;
        if (handItemId >= 0 && handItemCount > 0 && !TryGetInstallationDefinition(handItemId, out _))
        {
            ClearInstallPreview();
            return;
        }

        CleanupInstallPreviewReferences();
        EnsureValidActiveInstallPreview();
    }

    private bool IsEditingInstallation()
    {
        return activeInstallationEditSession != null;
    }

    private bool IsInstallationItem(int itemId)
    {
        return TryGetInstallationDefinition(itemId, out _);
    }

    private bool TryGetHandInstallationDefinition(out ItemDefinition definition)
    {
        definition = null;

        PlayerBag handBag = GetPlayerHandBag();
        if (handBag == null)
        {
            return false;
        }

        int handItemId = handBag.GetSlotItemId(0);
        return TryGetInstallationDefinition(handItemId, out definition);
    }

    private bool TryGetInstallationDefinition(int itemId, out ItemDefinition definition)
    {
        definition = null;

        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return false;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return false;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition candidate = definitions[i];
            if (candidate == null || candidate.id != itemId)
            {
                continue;
            }

            if (TryResolveInstallationObject(candidate.mapObject, out _))
            {
                definition = candidate;
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryResolveInstallationObject(MapObject mapObject, out InstallationObject installationObject)
    {
        installationObject = null;
        if (mapObject == null)
        {
            return false;
        }

        if (mapObject.GetComponent<Resource>() != null || mapObject.GetComponentInChildren<Resource>(true) != null)
        {
            return false;
        }

        installationObject = mapObject as InstallationObject;
        if (installationObject != null)
        {
            return true;
        }

        installationObject = mapObject.GetComponent<InstallationObject>();
        if (installationObject == null)
        {
            installationObject = mapObject.GetComponentInChildren<InstallationObject>(true);
        }

        return installationObject != null;
    }

    private PlayerBag GetPlayerHandBag()
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return null;
        }

        return GameManager.Instance.Player.GetHandBag();
    }

    private PlayerBag GetPlayerInventoryBag()
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return null;
        }

        return GameManager.Instance.Player.GetBag();
    }

    private bool TryReserveInstallPreviewItem(ItemDefinition definition, out InstallPreviewItemReservation reservation)
    {
        reservation = null;
        int itemId = definition != null ? definition.id : -1;
        if (itemId < 0)
        {
            return false;
        }

        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        PlayerBag handBag = player != null ? player.GetHandBag() : GetPlayerHandBag();
        if (TryReserveInstallPreviewItemFromHand(player, handBag, itemId, out reservation))
        {
            return true;
        }

        PlayerBag inventoryBag = player != null ? player.GetBag() : GetPlayerInventoryBag();
        return TryReserveInstallPreviewItemFromInventory(inventoryBag, handBag, itemId, out reservation);
    }

    private bool TryReserveInstallPreviewItemFromHand(
        Player player,
        PlayerBag handBag,
        int itemId,
        out InstallPreviewItemReservation reservation)
    {
        reservation = null;
        if (handBag == null)
        {
            return false;
        }

        PortableObject sourcePortableObject = GetFirstPlayerHandPortableSource(itemId);
        if (!TryReserveInstallPreviewItemFromBagSlot(
                handBag,
                itemId,
                0,
                true,
                sourcePortableObject,
                out reservation))
        {
            return false;
        }

        RefreshHandAfterInstallPreviewReservationChange(player, handBag);
        return true;
    }

    private bool TryReserveInstallPreviewItemFromInventory(
        PlayerBag inventoryBag,
        PlayerBag handBag,
        int itemId,
        out InstallPreviewItemReservation reservation)
    {
        reservation = null;
        if (inventoryBag == null || inventoryBag == handBag)
        {
            return false;
        }

        for (int i = 0; i < inventoryBag.SlotCount; i++)
        {
            if (TryReserveInstallPreviewItemFromBagSlot(
                    inventoryBag,
                    itemId,
                    i,
                    false,
                    null,
                    out reservation))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryReserveInstallPreviewItemFromBagSlot(
        PlayerBag bag,
        int itemId,
        int slotIndex,
        bool fromHand,
        PortableObject sourcePortableObject,
        out InstallPreviewItemReservation reservation)
    {
        reservation = null;
        if (bag == null
            || itemId < 0
            || slotIndex < 0
            || bag.GetSlotItemId(slotIndex) != itemId
            || bag.GetSlotCount(slotIndex) <= 0)
        {
            return false;
        }

        if (!bag.TryRemoveItemsAtSlot(
                slotIndex,
                1,
                out int removedItemId,
                out int removedCount,
                out _,
                false,
                true)
            || removedItemId != itemId
            || removedCount <= 0)
        {
            return false;
        }

        reservation = new InstallPreviewItemReservation
        {
            itemId = itemId,
            fromHand = fromHand,
            sourceSlotIndex = slotIndex,
            sourcePortableObject = sourcePortableObject
        };
        return true;
    }

    private PortableObject GetFirstPlayerHandPortableSource(int itemId)
    {
        List<PortableObject> handSources = GetPlayerHandPortableSources(itemId, 1);
        return handSources.Count > 0 ? handSources[0] : null;
    }

    private static int GetReservationSourceSlotIndex(InstallPreviewItemReservation reservation)
    {
        if (reservation == null)
        {
            return -1;
        }

        return reservation.sourceSlotIndex >= 0 ? reservation.sourceSlotIndex : (reservation.fromHand ? 0 : -1);
    }

    private void RefreshHandAfterInstallPreviewReservationChange(Player player, PlayerBag handBag)
    {
        handBag?.RefreshExternalStackCounts(false);
        player?.UpdateCarryState();
    }

    private void RefundInstallPreviewReservation(MapObject preview)
    {
        if (preview == null)
        {
            return;
        }

        if (!installPreviewItemReservationsByPreview.TryGetValue(preview, out InstallPreviewItemReservation reservation))
        {
            return;
        }

        installPreviewItemReservationsByPreview.Remove(preview);
        RefundInstallPreviewReservation(reservation);
    }

    private bool TryConsumeInstallPreviewReservation(MapObject preview, out PortableObject sourcePortableObject)
    {
        sourcePortableObject = null;
        if (preview == null
            || !installPreviewItemReservationsByPreview.TryGetValue(preview, out InstallPreviewItemReservation reservation))
        {
            return false;
        }

        installPreviewItemReservationsByPreview.Remove(preview);
        if (reservation == null)
        {
            return false;
        }

        sourcePortableObject = reservation.sourcePortableObject;
        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        PlayerBag handBag = player != null ? player.GetHandBag() : GetPlayerHandBag();
        PlayerBag inventoryBag = player != null ? player.GetBag() : GetPlayerInventoryBag();
        PlayerBag sourceBag = reservation.fromHand ? handBag : inventoryBag;
        int sourceSlotIndex = GetReservationSourceSlotIndex(reservation);

        if (sourceBag != null
            && sourceSlotIndex >= 0
            && sourceBag.CommitVisualPreservedObjectRemoval(
                sourceSlotIndex,
                reservation.itemId,
                out PortableObject removedPortableObject)
            && sourcePortableObject == null)
        {
            sourcePortableObject = removedPortableObject;
        }

        if (reservation.fromHand)
        {
            RefreshHandAfterInstallPreviewReservationChange(player, handBag);
        }

        return true;
    }

    private int GetReservedInstallPreviewItemCount(int itemId)
    {
        if (itemId < 0 || installPreviewItemReservationsByPreview.Count <= 0)
        {
            return 0;
        }

        int count = 0;
        foreach (InstallPreviewItemReservation reservation in installPreviewItemReservationsByPreview.Values)
        {
            if (reservation != null && reservation.itemId == itemId)
            {
                count++;
            }
        }

        return count;
    }

    private void RefundAllInstallPreviewReservations()
    {
        List<InstallPreviewItemReservation> reservations = new List<InstallPreviewItemReservation>(
            installPreviewItemReservationsByPreview.Values);
        installPreviewItemReservationsByPreview.Clear();
        for (int i = 0; i < reservations.Count; i++)
        {
            RefundInstallPreviewReservation(reservations[i]);
        }
    }

    private void RefundInstallPreviewReservation(InstallPreviewItemReservation reservation)
    {
        if (reservation == null || reservation.itemId < 0)
        {
            return;
        }

        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        PlayerBag handBag = player != null ? player.GetHandBag() : GetPlayerHandBag();
        PlayerBag inventoryBag = player != null ? player.GetBag() : GetPlayerInventoryBag();
        if (reservation.fromHand)
        {
            if (TryRestoreInstallPreviewReservationToSourceSlot(handBag, reservation))
            {
                RefreshHandAfterInstallPreviewReservationChange(player, handBag);
                return;
            }

            if (player != null && player.TryAddToHand(reservation.itemId, out _))
            {
                RefreshHandAfterInstallPreviewReservationChange(player, handBag);
                return;
            }
        }
        else if (inventoryBag != null)
        {
            if (TryRestoreInstallPreviewReservationToSourceSlot(inventoryBag, reservation)
                || inventoryBag.TryAddObjectToSlotOnly(
                    GetReservationSourceSlotIndex(reservation),
                    reservation.itemId,
                    out _))
            {
                return;
            }

            if (player != null && player.TryAddToBag(reservation.itemId, out _))
            {
                return;
            }
        }

        player?.UpdateCarryState();
    }

    private bool TryRestoreInstallPreviewReservationToSourceSlot(
        PlayerBag bag,
        InstallPreviewItemReservation reservation)
    {
        int sourceSlotIndex = GetReservationSourceSlotIndex(reservation);
        return bag != null
               && reservation != null
               && sourceSlotIndex >= 0
               && bag.TryRestoreVisualPreservedObjectToSlotOnly(
                   sourceSlotIndex,
                   reservation.itemId,
                   out _);
    }

    private List<PortableObject> GetPlayerHandPortableSources(int itemId, int requestedCount)
    {
        List<PortableObject> results = new List<PortableObject>();
        if (requestedCount <= 0 || itemId < 0)
        {
            return results;
        }

        PlayerBag handBag = GetPlayerHandBag();
        if (handBag == null)
        {
            return results;
        }

        List<PortableObject> occupiedObjects = new List<PortableObject>();
        if (!handBag.TryGetOccupiedSlotObjects(0, occupiedObjects))
        {
            return results;
        }

        for (int i = occupiedObjects.Count - 1; i >= 0 && results.Count < requestedCount; i--)
        {
            PortableObject portableObject = occupiedObjects[i];
            if (portableObject == null || portableObject.ItemId != itemId)
            {
                continue;
            }

            results.Add(portableObject);
        }

        return results;
    }

    private void BindInstallButtons()
    {
        BindButton(installButton, HandleInstallButtonClicked);
        BindButton(installCancelButton, HandleInstallCancelClicked);
        BindButton(installRotationButton, HandleInstallRotationClicked);
        BindButton(installCompleteButton, HandleInstallCompleteClicked);
        BindButton(mapEditButton, HandleMapEditButtonClicked);
        BindButton(mapEditCancelButton, HandleInstallCancelClicked);
        BindButton(mapEditRotationButton, HandleInstallRotationClicked);
        BindButton(mapEditCompleteButton, HandleInstallCompleteClicked);
        BindButton(mapEditPackButton, HandleMapEditPackClicked);
        BindButton(mapEditUndoButton, HandleMapEditUndoClicked);
    }

    private void UnbindInstallButtons()
    {
        UnbindButton(installButton, HandleInstallButtonClicked);
        UnbindButton(installCancelButton, HandleInstallCancelClicked);
        UnbindButton(installRotationButton, HandleInstallRotationClicked);
        UnbindButton(installCompleteButton, HandleInstallCompleteClicked);
        UnbindButton(mapEditButton, HandleMapEditButtonClicked);
        UnbindButton(mapEditCancelButton, HandleInstallCancelClicked);
        UnbindButton(mapEditRotationButton, HandleInstallRotationClicked);
        UnbindButton(mapEditCompleteButton, HandleInstallCompleteClicked);
        UnbindButton(mapEditPackButton, HandleMapEditPackClicked);
        UnbindButton(mapEditUndoButton, HandleMapEditUndoClicked);
    }

    private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static void UnbindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
    }

    private void RefreshMapEditButtonState()
    {
        if (mapEditButton == null)
        {
            return;
        }

        CleanupSelectedEditableInstallation();
        mapEditButton.interactable = !IsInstallationModeActive();

        bool canPack = mapEditModeActive && CanPackSelectedInstallation();
        if (mapEditPackButton != null)
        {
            mapEditPackButton.interactable = canPack;
        }

        bool canUndo = mapEditModeActive && !IsEditingInstallation() && packedInstallationHistory.Count > 0;
        if (mapEditUndoButton != null)
        {
            mapEditUndoButton.interactable = canUndo;
        }
    }

    private bool CanPackSelectedInstallation()
    {
        if (!mapEditModeActive)
        {
            return false;
        }

        if (IsEditingInstallation())
        {
            CleanupInstallPreviewReferences();
            return activeInstallationEditSession != null
                   && activeInstallPreview != null
                   && TryGetPreviewAnchorCoordinate(activeInstallPreview, out _);
        }

        CleanupSelectedEditableInstallation();
        return selectedEditableInstallation != null
               && selectedEditableInstallation.TryGetPlacementRuntime(out _, out _);
    }

    private void UpdateMapEditSelectionInput()
    {
        if (!TryGetPrimaryPointerDown(out Vector2 pointerPosition) || IsPointerOverBlockingUi(pointerPosition))
        {
            return;
        }

        if (TryGetEditableInstallationAtPointer(pointerPosition, out InstallationObject installationObject, out Vector2Int anchorCoordinate))
        {
            SelectEditableInstallation(installationObject, anchorCoordinate);
            if (!IsInstallationModeActive())
            {
                BeginInstallationEdit(installationObject);
            }
            return;
        }

        ClearEditableInstallationSelection();
    }

    private bool TryGetEditableInstallationAtPointer(Vector2 pointerPosition, out InstallationObject installationObject, out Vector2Int anchorCoordinate)
    {
        installationObject = null;
        anchorCoordinate = Vector2Int.zero;

        Camera targetCamera = ResolveInstallPreviewCamera();
        if (targetCamera == null)
        {
            return false;
        }

        Ray ray = targetCamera.ScreenPointToRay(pointerPosition);
        if (!TryGetBlockFromGroundPlane(ray, out Block clickedBlock) || clickedBlock == null)
        {
            return false;
        }

        installationObject = clickedBlock.MapObject as InstallationObject;

        if (installationObject == null || !installationObject.TryGetPlacementRuntime(out anchorCoordinate, out _))
        {
            installationObject = null;
            return false;
        }

        return true;
    }

    private void SelectEditableInstallation(InstallationObject installationObject, Vector2Int anchorCoordinate)
    {
        selectedEditableInstallation = installationObject;
        selectedEditableAnchorCoordinate = anchorCoordinate;
        RefreshMapEditButtonState();
    }

    private void ClearEditableInstallationSelection()
    {
        selectedEditableInstallation = null;
        selectedEditableAnchorCoordinate = Vector2Int.zero;
        RefreshMapEditButtonState();
    }

    private void CleanupSelectedEditableInstallation()
    {
        if (selectedEditableInstallation == null)
        {
            return;
        }

        if (!selectedEditableInstallation.gameObject.activeInHierarchy
            || !selectedEditableInstallation.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            selectedEditableInstallation = null;
            selectedEditableAnchorCoordinate = Vector2Int.zero;
            return;
        }

        selectedEditableAnchorCoordinate = anchorCoordinate;
    }

    private void HandleMapEditButtonClicked()
    {
        if (IsInstallationModeActive())
        {
            RefreshMapEditButtonState();
            return;
        }

        SetMapEditModeActive(!mapEditModeActive);
    }

    private void HandleMapEditPackClicked()
    {
        TryPackSelectedInstallation();
        RefreshMapEditButtonState();
    }

    private void HandleMapEditUndoClicked()
    {
        TryUndoPackedInstallation();
        RefreshMapEditButtonState();
    }

    private bool TryPackSelectedInstallation()
    {
        if (!mapEditModeActive)
        {
            return false;
        }

        bool wasEditingInstallation = IsEditingInstallation();
        InstallationEditSession editSession;
        Vector2Int targetAnchorCoordinate;
        int targetQuarterTurns;

        if (wasEditingInstallation)
        {
            CleanupInstallPreviewReferences();
            if (activeInstallationEditSession == null
                || activeInstallPreview == null
                || !TryGetPreviewAnchorCoordinate(activeInstallPreview, out targetAnchorCoordinate))
            {
                return false;
            }

            editSession = activeInstallationEditSession;
            targetQuarterTurns = GetPreviewQuarterTurns(activeInstallPreview);
            activeInstallationEditSession = null;
        }
        else
        {
            CleanupSelectedEditableInstallation();
            if (selectedEditableInstallation == null
                || !selectedEditableInstallation.TryGetPlacementRuntime(out targetAnchorCoordinate, out targetQuarterTurns)
                || !TryCreateInstallationEditSession(selectedEditableInstallation, out editSession))
            {
                return false;
            }

            DetachInstallationForEditing(editSession);
        }

        int itemId = editSession?.definition != null
            ? editSession.definition.id
            : editSession?.originalInstallation != null ? editSession.originalInstallation.ResolveItemId() : -1;
        int packedConveyorVariantKind = wasEditingInstallation && activeInstallPreview != null
            ? GetConveyorVariantKind(activeInstallPreview)
            : editSession.originalConveyorVariantKind;
        if (itemId < 0)
        {
            RestoreEditedInstallation(editSession, targetAnchorCoordinate, targetQuarterTurns);
            ClearInstallPreview();
            return false;
        }

        packedInstallationHistory.Push(new PackedInstallationSession
        {
            editSession = editSession,
            anchorCoordinate = targetAnchorCoordinate,
            quarterTurns = ((targetQuarterTurns % 4) + 4) % 4,
            conveyorVariantKind = packedConveyorVariantKind,
            itemId = itemId,
            dropCoordinate = targetAnchorCoordinate,
            portableObject = null,
            pendingDroppedBlockStatesByCanonicalOffset = BuildPackedInstallationDroppedBlockStates(editSession)
        });

        ConveyorChangeInfo removedConveyorChange = null;
        TryCreateOriginalEditConveyorChange(editSession, out removedConveyorChange);

        ClearEditableInstallationSelection();
        ClearInstallPreview();
        if (removedConveyorChange != null)
        {
            NormalizeDisconnectedConveyorCornersAroundChanges(
                new List<ConveyorChangeInfo> { removedConveyorChange },
                false);
        }
        else
        {
            NormalizeDisconnectedConveyorCornersAroundCoordinates(editSession.originalOccupiedCoordinates, false);
        }
        return true;
    }

    private bool TryUndoPackedInstallation()
    {
        if (!mapEditModeActive || IsEditingInstallation() || packedInstallationHistory.Count <= 0)
        {
            return false;
        }

        PackedInstallationSession packedSession = packedInstallationHistory.Peek();
        if (packedSession == null || packedSession.editSession == null)
        {
            packedInstallationHistory.Pop();
            return false;
        }

        if (packedSession.portableObject != null && !TryRemovePackedPortable(packedSession))
        {
            return false;
        }

        packedInstallationHistory.Pop();
        RestoreEditedInstallation(
            packedSession.editSession,
            packedSession.anchorCoordinate,
            packedSession.quarterTurns);
        return true;
    }

    private bool TryDropPackedPortable(int itemId, Vector2Int preferredCoordinate, out PortableObject portableObject, out Vector2Int dropCoordinate)
    {
        portableObject = null;
        dropCoordinate = preferredCoordinate;

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null || itemId < 0)
        {
            return false;
        }

        const int maxSearchRadius = 2;
        for (int radius = 0; radius <= maxSearchRadius; radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (radius > 0 && Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                    {
                        continue;
                    }

                    Vector2Int coordinate = preferredCoordinate + new Vector2Int(offsetX, offsetY);
                    if (!terrain.TryGetLoadedBlock(coordinate, out Block block) || block == null || block.Type != Block.BlockType.Ground)
                    {
                        continue;
                    }

                    if (!block.TryAddFloorObject(itemId, out portableObject))
                    {
                        continue;
                    }

                    dropCoordinate = coordinate;
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryRemovePackedPortable(PackedInstallationSession packedSession)
    {
        if (packedSession == null || packedSession.portableObject == null)
        {
            return false;
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null)
        {
            return false;
        }

        if (terrain.TryGetLoadedBlock(packedSession.dropCoordinate, out Block dropBlock)
            && dropBlock != null
            && dropBlock.TryRemoveFloorObject(packedSession.portableObject))
        {
            return true;
        }

        return false;
    }

    private void FinalizePackedInstallationHistory()
    {
        while (packedInstallationHistory.Count > 0)
        {
            PackedInstallationSession packedSession = packedInstallationHistory.Pop();
            ApplyPackedInstallationDroppedBlockStates(packedSession);
            if (packedSession.itemId >= 0
                && TryDropPackedPortable(
                    packedSession.itemId,
                    packedSession.dropCoordinate,
                    out PortableObject packedPortableObject,
                    out Vector2Int packedDropCoordinate))
            {
                packedSession.portableObject = packedPortableObject;
                packedSession.dropCoordinate = packedDropCoordinate;
            }

            InstallationObject originalInstallation = packedSession?.editSession?.originalInstallation;
            if (originalInstallation == null)
            {
                continue;
            }

            TerrainGenerator terrain = ResolveInstallPreviewTerrain();
            ReleaseInstalledObjectInstance(
                originalInstallation,
                ResolveInstallationSourcePrefab(
                    packedSession.editSession.definition,
                    packedSession.editSession.originalConveyorVariantKind),
                terrain);
        }
    }

    private void ApplyPackedInstallationDroppedBlockStates(PackedInstallationSession packedSession)
    {
        if (packedSession?.editSession == null
            || packedSession.pendingDroppedBlockStatesByCanonicalOffset == null
            || packedSession.pendingDroppedBlockStatesByCanonicalOffset.Count <= 0)
        {
            return;
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null)
        {
            return;
        }

        MapObject footprintSource = packedSession.editSession.definition != null
            ? packedSession.editSession.definition.mapObject
            : null;
        if (footprintSource is ConveyorBelt conveyorPrototype && packedSession.conveyorVariantKind >= 0)
        {
            footprintSource = ResolveConveyorVariantPrefab(conveyorPrototype, packedSession.conveyorVariantKind)
                ?? footprintSource;
        }

        if (footprintSource == null)
        {
            return;
        }

        List<Vector2Int> occupiedCoordinates = GetFootprintCoordinates(
            packedSession.anchorCoordinate,
            footprintSource,
            packedSession.quarterTurns);

        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = occupiedCoordinates[i];
            if (!terrain.TryGetLoadedBlock(coordinate, out Block block) || block == null)
            {
                continue;
            }

            Vector2Int worldOffset = coordinate - packedSession.anchorCoordinate;
            Vector2Int canonicalOffset = RotateFootprintOffset(worldOffset, -packedSession.quarterTurns);
            if (!packedSession.pendingDroppedBlockStatesByCanonicalOffset.TryGetValue(canonicalOffset, out List<int> droppedItemIds)
                || droppedItemIds == null
                || droppedItemIds.Count <= 0)
            {
                continue;
            }

            for (int itemIndex = 0; itemIndex < droppedItemIds.Count; itemIndex++)
            {
                int itemId = droppedItemIds[itemIndex];
                if (itemId < 0)
                {
                    continue;
                }

                TryDropPackedFloorObject(itemId, coordinate, out _, out _);
            }
        }
    }

    private bool TryDropPackedFloorObject(int itemId, Vector2Int preferredCoordinate, out PortableObject portableObject, out Vector2Int dropCoordinate)
    {
        portableObject = null;
        dropCoordinate = preferredCoordinate;

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null || itemId < 0)
        {
            return false;
        }

        const int maxSearchRadius = 2;
        for (int radius = 0; radius <= maxSearchRadius; radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (radius > 0 && Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                    {
                        continue;
                    }

                    Vector2Int coordinate = preferredCoordinate + new Vector2Int(offsetX, offsetY);
                    if (!terrain.TryGetLoadedBlock(coordinate, out Block block) || block == null || block.Type != Block.BlockType.Ground)
                    {
                        continue;
                    }

                    if (!block.TryAddFloorObject(itemId, out portableObject))
                    {
                        continue;
                    }

                    dropCoordinate = coordinate;
                    return true;
                }
            }
        }

        return false;
    }

    private void RestorePackedInstallationHistory()
    {
        while (packedInstallationHistory.Count > 0)
        {
            PackedInstallationSession packedSession = packedInstallationHistory.Pop();
            if (packedSession?.editSession == null)
            {
                continue;
            }

            TryRemovePackedPortable(packedSession);
            RestoreEditedInstallation(
                packedSession.editSession,
                packedSession.anchorCoordinate,
                packedSession.quarterTurns);
        }
    }

    private void HandleInstallButtonClicked()
    {
        SetMapEditModeActive(false);

        if (!TryGetHandInstallationDefinition(out ItemDefinition definition) || definition == null || definition.mapObject == null)
        {
            ClearInstallPreview();
            return;
        }

        if (IsInstallationModeActive() && activeInstallDefinition == definition)
        {
            ClearInstallPreview();
            return;
        }

        BeginInstallPreview(definition);
    }

    private void HandleInstallCancelClicked()
    {
        if (IsEditingInstallation())
        {
            CancelInstallationEdit();
            RestorePackedInstallationHistory();
            SetMapEditModeActive(false);
            return;
        }

        if (mapEditModeActive)
        {
            RestorePackedInstallationHistory();
            SetMapEditModeActive(false);
            return;
        }

        ClearInstallPreview();
    }

    private void BeginInstallationEdit(InstallationObject installationObject)
    {
        if (installationObject == null || !TryCreateInstallationEditSession(installationObject, out InstallationEditSession editSession))
        {
            return;
        }

        ClearInstallPreview();
        activeInstallationEditSession = editSession;
        ClearEditableInstallationSelection();
        DetachInstallationForEditing(editSession);
        BeginInstallationEditPreview(editSession);
    }

    private bool TryCreateInstallationEditSession(InstallationObject installationObject, out InstallationEditSession editSession)
    {
        editSession = null;
        if (installationObject == null || !installationObject.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int runtimeQuarterTurns))
        {
            return false;
        }

        int itemId = installationObject.ResolveItemId();
        if (!TryGetInstallationDefinition(itemId, out ItemDefinition definition) || definition == null || definition.mapObject == null)
        {
            return false;
        }

        int resolvedQuarterTurns = ResolveInstallationEditQuarterTurns(
            installationObject,
            definition,
            anchorCoordinate,
            runtimeQuarterTurns);

        editSession = new InstallationEditSession
        {
            originalInstallation = installationObject,
            definition = definition,
            originalAnchorCoordinate = anchorCoordinate,
            originalQuarterTurns = resolvedQuarterTurns,
            originalRotation = installationObject.transform.rotation,
            originalConveyorVariantKind = GetConveyorVariantKind(installationObject),
            originalOccupiedCoordinates = new List<Vector2Int>(installationObject.RuntimeOccupiedCoordinates)
        };
        editSession.originalStateCoordinates = GetInstallationEditStateCoordinates(
            editSession,
            editSession.originalAnchorCoordinate,
            editSession.originalQuarterTurns);

        if (installationObject is InputOutputModule inputOutputModule)
        {
            editSession.inputOutputState = inputOutputModule.CapturePersistentState();
        }

        if (installationObject is BoxObject boxObject)
        {
            editSession.boxIsOpen = boxObject.IsOpen;
        }

        editSession.itemFilterMaskInitialized = installationObject.IsItemFilterMaskInitialized;
        editSession.itemFilterMaskWords = installationObject.CaptureItemFilterMaskWords();

        CaptureAttachedAreaBoxes(editSession);
        CaptureInstallationBlockStates(editSession);
        return true;
    }

    private List<Vector2Int> GetInstallationEditStateCoordinates(
        InstallationEditSession editSession,
        Vector2Int anchorCoordinate,
        int quarterTurns)
    {
        if (editSession == null)
        {
            return new List<Vector2Int>();
        }

        MapObject footprintSource = editSession.definition != null && editSession.definition.mapObject != null
            ? editSession.definition.mapObject
            : editSession.originalInstallation;
        return GetFootprintCoordinates(anchorCoordinate, footprintSource, quarterTurns);
    }

    private void CaptureAttachedAreaBoxes(InstallationEditSession editSession)
    {
        if (editSession == null)
        {
            return;
        }

        editSession.attachedAreaBoxes.Clear();

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null || editSession.originalStateCoordinates == null)
        {
            return;
        }

        HashSet<Vector2Int> occupiedCoordinates = new HashSet<Vector2Int>(editSession.originalOccupiedCoordinates);
        HashSet<BoxObject> capturedBoxes = new HashSet<BoxObject>();
        for (int i = 0; i < editSession.originalStateCoordinates.Count; i++)
        {
            Vector2Int coordinate = editSession.originalStateCoordinates[i];
            if (occupiedCoordinates.Contains(coordinate)
                || !terrain.TryGetLoadedBlock(coordinate, out Block block)
                || block == null
                || !(block.MapObject is BoxObject boxObject)
                || !capturedBoxes.Add(boxObject))
            {
                continue;
            }

            if (TryCaptureAttachedAreaBox(editSession, boxObject, out AreaAttachedBoxState boxState))
            {
                editSession.attachedAreaBoxes.Add(boxState);
            }
        }
    }

    private bool TryCaptureAttachedAreaBox(
        InstallationEditSession editSession,
        BoxObject boxObject,
        out AreaAttachedBoxState boxState)
    {
        boxState = null;
        if (editSession == null
            || boxObject == null
            || boxObject == editSession.originalInstallation
            || !boxObject.TryGetPlacementRuntime(out Vector2Int boxAnchorCoordinate, out int boxRuntimeQuarterTurns))
        {
            return false;
        }

        ItemDefinition boxDefinition = null;
        int boxItemId = boxObject.ResolveItemId();
        TryGetInstallationDefinition(boxItemId, out boxDefinition);
        int boxQuarterTurns = boxDefinition != null
            ? ResolveInstallationEditQuarterTurns(boxObject, boxDefinition, boxAnchorCoordinate, boxRuntimeQuarterTurns)
            : NormalizePlacementQuarterTurns(boxRuntimeQuarterTurns);

        Vector2Int worldOffset = boxAnchorCoordinate - editSession.originalAnchorCoordinate;
        boxState = new AreaAttachedBoxState
        {
            boxObject = boxObject,
            definition = boxDefinition,
            originalAnchorCoordinate = boxAnchorCoordinate,
            originalQuarterTurns = boxQuarterTurns,
            originalRotation = boxObject.transform.rotation,
            canonicalAnchorOffset = RotateFootprintOffset(worldOffset, -editSession.originalQuarterTurns),
            boxIsOpen = boxObject.IsOpen,
            itemFilterMaskInitialized = boxObject.IsItemFilterMaskInitialized,
            itemFilterMaskWords = boxObject.CaptureItemFilterMaskWords()
        };
        return true;
    }

    private void CaptureInstallationBlockStates(InstallationEditSession editSession)
    {
        editSession.blockStatesByCanonicalOffset.Clear();

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null || editSession.originalStateCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < editSession.originalStateCoordinates.Count; i++)
        {
            Vector2Int stateCoordinate = editSession.originalStateCoordinates[i];
            if (!terrain.TryGetLoadedBlock(stateCoordinate, out Block block) || block == null)
            {
                continue;
            }

            List<int> blockState = block.CaptureFloorObjectState();
            if (blockState == null || blockState.Count <= 0)
            {
                continue;
            }

            Vector2Int worldOffset = stateCoordinate - editSession.originalAnchorCoordinate;
            Vector2Int canonicalOffset = RotateFootprintOffset(worldOffset, -editSession.originalQuarterTurns);
            editSession.blockStatesByCanonicalOffset[canonicalOffset] = new List<int>(blockState);
        }
    }

    private Dictionary<Vector2Int, List<int>> BuildPackedInstallationDroppedBlockStates(InstallationEditSession editSession)
    {
        Dictionary<Vector2Int, List<int>> droppedStatesByCanonicalOffset = new Dictionary<Vector2Int, List<int>>();
        if (editSession == null || editSession.blockStatesByCanonicalOffset == null || editSession.blockStatesByCanonicalOffset.Count <= 0)
        {
            return droppedStatesByCanonicalOffset;
        }

        List<Vector2Int> keys = new List<Vector2Int>(editSession.blockStatesByCanonicalOffset.Keys);
        for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
        {
            Vector2Int key = keys[keyIndex];
            if (!editSession.blockStatesByCanonicalOffset.TryGetValue(key, out List<int> blockState) || blockState == null || blockState.Count <= 0)
            {
                continue;
            }

            List<int> droppedItemIds = new List<int>(blockState.Count);
            for (int i = 0; i < blockState.Count; i++)
            {
                int itemId = blockState[i];
                if (itemId != ConveyorStackStateSentinel)
                {
                    continue;
                }

                if (i + 1 >= blockState.Count)
                {
                    break;
                }

                int laneCount = Mathf.Max(0, blockState[++i]);
                for (int laneIndex = 0; laneIndex < laneCount && i + 1 < blockState.Count; laneIndex++)
                {
                    int laneItemId = blockState[++i];
                    if (laneItemId >= 0)
                    {
                        droppedItemIds.Add(laneItemId);
                    }
                }
            }

            if (droppedItemIds.Count > 0)
            {
                droppedStatesByCanonicalOffset[key] = droppedItemIds;
            }
        }

        return droppedStatesByCanonicalOffset;
    }

    private void DetachInstallationForEditing(InstallationEditSession editSession, bool preserveConveyorItemsOnGround = false)
    {
        if (editSession == null || editSession.originalInstallation == null)
        {
            return;
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        terrain?.RemoveInstallationPersistence(editSession.originalAnchorCoordinate);
        DetachAttachedAreaBoxes(editSession, terrain);

        List<Vector2Int> stateCoordinates = editSession.originalStateCoordinates != null && editSession.originalStateCoordinates.Count > 0
            ? editSession.originalStateCoordinates
            : editSession.originalOccupiedCoordinates;
        for (int i = 0; i < stateCoordinates.Count; i++)
        {
            if (terrain == null || !terrain.TryGetLoadedBlock(stateCoordinates[i], out Block block) || block == null)
            {
                continue;
            }

            IReadOnlyList<int> detachedFloorState = preserveConveyorItemsOnGround
                ? block.CaptureFloorObjectStateWithDroppedConveyorObjects()
                : null;

            if (block.MapObject == editSession.originalInstallation || IsAttachedAreaBox(editSession, block.MapObject))
            {
                block.SetMapObject(null);
            }

            block.ApplyFloorObjectState(detachedFloorState);
        }

        editSession.originalInstallation.gameObject.SetActive(false);
    }

    private void DetachAttachedAreaBoxes(InstallationEditSession editSession, TerrainGenerator terrain)
    {
        if (editSession?.attachedAreaBoxes == null || editSession.attachedAreaBoxes.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < editSession.attachedAreaBoxes.Count; i++)
        {
            AreaAttachedBoxState boxState = editSession.attachedAreaBoxes[i];
            if (boxState?.boxObject == null)
            {
                continue;
            }

            terrain?.RemoveInstallationPersistence(boxState.originalAnchorCoordinate);
            MapObject footprintSource = boxState.definition != null && boxState.definition.mapObject != null
                ? boxState.definition.mapObject
                : boxState.boxObject;
            List<Vector2Int> boxCoordinates = GetFootprintCoordinates(
                boxState.originalAnchorCoordinate,
                footprintSource,
                boxState.originalQuarterTurns);
            for (int coordinateIndex = 0; coordinateIndex < boxCoordinates.Count; coordinateIndex++)
            {
                if (terrain != null
                    && terrain.TryGetLoadedBlock(boxCoordinates[coordinateIndex], out Block block)
                    && block != null
                    && block.MapObject == boxState.boxObject)
                {
                    block.SetMapObject(null);
                }
            }

            boxState.boxObject.gameObject.SetActive(false);
        }
    }

    private bool IsAttachedAreaBox(InstallationEditSession editSession, MapObject mapObject)
    {
        if (editSession?.attachedAreaBoxes == null || !(mapObject is BoxObject boxObject))
        {
            return false;
        }

        for (int i = 0; i < editSession.attachedAreaBoxes.Count; i++)
        {
            AreaAttachedBoxState boxState = editSession.attachedAreaBoxes[i];
            if (boxState != null && boxState.boxObject == boxObject)
            {
                return true;
            }
        }

        return false;
    }

    private void BeginInstallationEditPreview(InstallationEditSession editSession)
    {
        activeInstallDefinition = editSession.definition;
        activeInstallPreview = null;
        MapObject previewSourcePrefab = editSession.definition.mapObject;
        if (editSession.definition.mapObject is ConveyorBelt conveyorPrototype && editSession.originalConveyorVariantKind >= 0)
        {
            previewSourcePrefab = ResolveConveyorVariantPrefab(conveyorPrototype, editSession.originalConveyorVariantKind)
                ?? editSession.definition.mapObject;
        }

        activeInstallBaseRotation = previewSourcePrefab != null
            ? previewSourcePrefab.transform.rotation
            : Quaternion.identity;
        installPreviewQuarterTurns = editSession.originalQuarterTurns;
        installPreviewConveyorVariantMode = editSession.originalConveyorVariantKind > 0
            ? ConveyorPreviewVariantMode.Corner
            : ConveyorPreviewVariantMode.Straight;
        waitForPointerReleaseAfterPreviewSpawn = true;
        installGridRefreshTimer = 0f;
        GameManager.Instance?.SetInstallationPlacementActive(true);

        MapObject preview = CreateInstallPreviewInstance(previewSourcePrefab, editSession.definition != null ? editSession.definition.mapObject : null);
        if (preview == null)
        {
            CancelInstallationEdit();
            return;
        }

        RegisterInstallPreview(preview, editSession.originalQuarterTurns);
        installPreviewAnchorCoordinates[preview] = editSession.originalAnchorCoordinate;
        SelectInstallPreview(preview);
        installPreviewConveyorVariantMode = editSession.originalConveyorVariantKind > 0
            ? ConveyorPreviewVariantMode.Corner
            : ConveyorPreviewVariantMode.Straight;

        if (ResolveInstallPreviewTerrain() != null
            && ResolveInstallPreviewTerrain().TryGetLoadedBlock(editSession.originalAnchorCoordinate, out Block anchorBlock)
            && anchorBlock != null)
        {
            activeInstallPreview.transform.position = GetPreviewWorldPosition(
                anchorBlock,
                activeInstallPreview,
                editSession.originalQuarterTurns,
                installPreviewVerticalOffset);
        }
        else
        {
            activeInstallPreview.transform.position = GetPlacementWorldPositionFromAnchorCoordinate(
                editSession.originalAnchorCoordinate,
                activeInstallPreview,
                editSession.originalQuarterTurns,
                installPreviewVerticalOffset);
        }

        activeInstallPreview.transform.rotation = editSession.originalRotation;
        InvalidateInstallGrid();
    }

    private bool HasInstallationEditPlacementChanged(
        InstallationEditSession editSession,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        int conveyorVariantKind)
    {
        if (editSession == null)
        {
            return false;
        }

        if (anchorCoordinate != editSession.originalAnchorCoordinate)
        {
            return true;
        }

        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        int originalQuarterTurns = ((editSession.originalQuarterTurns % 4) + 4) % 4;
        if (normalizedQuarterTurns != originalQuarterTurns)
        {
            return true;
        }

        if (editSession.definition != null && editSession.definition.mapObject is ConveyorBelt)
        {
            return conveyorVariantKind != editSession.originalConveyorVariantKind;
        }

        return false;
    }

    private int ResolveInstallationEditQuarterTurns(
        InstallationObject installationObject,
        ItemDefinition definition,
        Vector2Int anchorCoordinate,
        int runtimeQuarterTurns)
    {
        int normalizedRuntimeQuarterTurns = ((runtimeQuarterTurns % 4) + 4) % 4;
        if (installationObject == null || definition == null)
        {
            return normalizedRuntimeQuarterTurns;
        }

        MapObject sourcePrefab = definition.mapObject;
        if (installationObject is ConveyorBelt installedConveyor && definition.mapObject is ConveyorBelt conveyorPrototype)
        {
            sourcePrefab = ResolveConveyorVariantPrefab(conveyorPrototype, GetConveyorVariantKind(installedConveyor))
                ?? definition.mapObject;
        }

        if (sourcePrefab == null)
        {
            return normalizedRuntimeQuarterTurns;
        }

        for (int candidateQuarterTurns = 0; candidateQuarterTurns < 4; candidateQuarterTurns++)
        {
            Quaternion candidateRotation = GetPlacementObjectRotation(sourcePrefab, candidateQuarterTurns);
            if (Mathf.Abs(Quaternion.Dot(candidateRotation, installationObject.transform.rotation)) >= 0.9999f)
            {
                return candidateQuarterTurns;
            }
        }

        return normalizedRuntimeQuarterTurns;
    }

    private void CancelInstallationEdit()
    {
        InstallationEditSession editSession = activeInstallationEditSession;
        activeInstallationEditSession = null;

        if (editSession != null)
        {
            RestoreEditedInstallation(
                editSession,
                editSession.originalAnchorCoordinate,
                editSession.originalQuarterTurns,
                editSession.originalConveyorVariantKind,
                editSession.originalRotation);
        }

        ClearInstallPreview();
    }

    private void CompleteInstallationEdit()
    {
        InstallationEditSession editSession = activeInstallationEditSession;
        if (editSession == null)
        {
            ClearInstallPreview();
            SetMapEditModeActive(false);
            return;
        }

        CleanupInstallPreviewReferences();
        if (activeInstallPreview == null || !TryGetPreviewAnchorCoordinate(activeInstallPreview, out Vector2Int anchorCoordinate))
        {
            CancelInstallationEdit();
            return;
        }

        Quaternion previewRotation = activeInstallPreview.transform.rotation;
        Vector3 previewPosition = activeInstallPreview.transform.position;
        int quarterTurns = GetPreviewQuarterTurns(activeInstallPreview);
        int conveyorVariantKind = GetConveyorVariantKind(activeInstallPreview);
        if (!CanRestoreEditedInstallationAt(
                editSession,
                anchorCoordinate,
                quarterTurns,
                conveyorVariantKind,
                activeInstallPreview))
        {
            InvalidateInstallGrid();
            return;
        }

        activeInstallationEditSession = null;
        MapObject restoredObject = RestoreEditedInstallation(
            editSession,
            anchorCoordinate,
            quarterTurns,
            conveyorVariantKind,
            previewRotation,
            previewPosition);
        if (restoredObject != null)
        {
            RememberLastInstalledRotation(editSession.definition, quarterTurns);
        }

        PlayInstallationEditCompleteAnimation(restoredObject, editSession);
        ClearInstallPreview();
        SetMapEditModeActive(false);
    }

    private bool CanRestoreEditedInstallationAt(
        InstallationEditSession editSession,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        int conveyorVariantKind,
        MapObject previewToIgnore)
    {
        if (!TryResolveEditedInstallationFootprintSource(
                editSession,
                anchorCoordinate,
                quarterTurns,
                conveyorVariantKind,
                previewToIgnore,
                out MapObject footprintSource))
        {
            return false;
        }

        if (!TryGetFootprintBlocks(anchorCoordinate, footprintSource, quarterTurns, previewToIgnore, out _))
        {
            return false;
        }

        return CanRestoreAttachedAreaBoxesAt(editSession, anchorCoordinate, quarterTurns);
    }

    private bool CanRestoreAttachedAreaBoxesAt(
        InstallationEditSession editSession,
        Vector2Int anchorCoordinate,
        int quarterTurns)
    {
        if (editSession?.attachedAreaBoxes == null || editSession.attachedAreaBoxes.Count <= 0)
        {
            return true;
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null)
        {
            return false;
        }

        HashSet<MapObject> movingObjects = new HashSet<MapObject>();
        if (editSession.originalInstallation != null)
        {
            movingObjects.Add(editSession.originalInstallation);
        }

        for (int i = 0; i < editSession.attachedAreaBoxes.Count; i++)
        {
            if (editSession.attachedAreaBoxes[i]?.boxObject != null)
            {
                movingObjects.Add(editSession.attachedAreaBoxes[i].boxObject);
            }
        }

        for (int i = 0; i < editSession.attachedAreaBoxes.Count; i++)
        {
            AreaAttachedBoxState boxState = editSession.attachedAreaBoxes[i];
            if (boxState?.boxObject == null)
            {
                continue;
            }

            Vector2Int targetAnchorCoordinate = GetMovedAttachedBoxAnchorCoordinate(
                editSession,
                boxState,
                anchorCoordinate,
                quarterTurns);
            int targetQuarterTurns = GetMovedAttachedBoxQuarterTurns(editSession, boxState, quarterTurns);
            MapObject footprintSource = boxState.definition != null && boxState.definition.mapObject != null
                ? boxState.definition.mapObject
                : boxState.boxObject;
            List<Vector2Int> boxCoordinates = GetFootprintCoordinates(targetAnchorCoordinate, footprintSource, targetQuarterTurns);
            for (int coordinateIndex = 0; coordinateIndex < boxCoordinates.Count; coordinateIndex++)
            {
                if (!terrain.TryGetLoadedBlock(boxCoordinates[coordinateIndex], out Block block) || block == null)
                {
                    return false;
                }

                MapObject occupyingObject = GetBlockingMapObject(block);
                if (occupyingObject != null && !movingObjects.Contains(occupyingObject))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private Vector2Int GetMovedAttachedBoxAnchorCoordinate(
        InstallationEditSession editSession,
        AreaAttachedBoxState boxState,
        Vector2Int newAnchorCoordinate,
        int newQuarterTurns)
    {
        if (editSession == null || boxState == null)
        {
            return newAnchorCoordinate;
        }

        return newAnchorCoordinate + RotateFootprintOffset(boxState.canonicalAnchorOffset, newQuarterTurns);
    }

    private int GetMovedAttachedBoxQuarterTurns(
        InstallationEditSession editSession,
        AreaAttachedBoxState boxState,
        int newQuarterTurns)
    {
        if (editSession == null || boxState == null)
        {
            return NormalizePlacementQuarterTurns(newQuarterTurns);
        }

        int quarterTurnDelta = NormalizePlacementQuarterTurns(newQuarterTurns - editSession.originalQuarterTurns);
        return NormalizePlacementQuarterTurns(boxState.originalQuarterTurns + quarterTurnDelta);
    }

    private bool TryResolveEditedInstallationFootprintSource(
        InstallationEditSession editSession,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        int conveyorVariantKind,
        MapObject fallbackSource,
        out MapObject footprintSource)
    {
        footprintSource = null;
        if (editSession == null)
        {
            return false;
        }

        MapObject desiredSourcePrefab = null;
        if (editSession.definition != null
            && editSession.definition.mapObject is ConveyorBelt conveyorPrototype
            && conveyorVariantKind >= 0)
        {
            desiredSourcePrefab = ResolveConveyorVariantPrefab(conveyorPrototype, conveyorVariantKind);
        }

        if (desiredSourcePrefab == null && editSession.definition != null)
        {
            desiredSourcePrefab = ResolveInstalledObjectSourcePrefab(editSession.definition, anchorCoordinate, quarterTurns);
        }

        footprintSource = desiredSourcePrefab != null
            ? desiredSourcePrefab
            : (editSession.definition != null ? editSession.definition.mapObject : fallbackSource);
        return footprintSource != null;
    }

    public bool MapEditModeActive => mapEditModeActive;

    public bool TryGetActiveBlueprintHudItemId(out int itemId)
    {
        itemId = activeInstallDefinition != null ? activeInstallDefinition.id : -1;
        return itemId >= 0 && IsInstallationModeActive();
    }

    private void SetMapEditModeActive(bool isActive)
    {
        if (mapEditModeActive == isActive)
        {
            RefreshMapEditButtonState();
            return;
        }

        mapEditModeActive = isActive;
        GameManager.Instance?.SetMapEditActive(mapEditModeActive);
        if (!mapEditModeActive)
        {
            selectedEditableInstallation = null;
            selectedEditableAnchorCoordinate = Vector2Int.zero;
            FinalizePackedInstallationHistory();
        }

        if (IsInstallGridModeActive())
        {
            InvalidateInstallGrid();
        }
        else
        {
            SetInstallGridVisible(false);
            installGridRefreshTimer = 0f;
        }

        RefreshMapEditButtonState();
    }

    private MapObject RestoreEditedInstallation(
        InstallationEditSession editSession,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        int conveyorVariantKind = -1,
        Quaternion? previewRotationOverride = null,
        Vector3? previewPositionOverride = null)
    {
        if (editSession == null || editSession.originalInstallation == null || editSession.definition == null)
        {
            return null;
        }

        bool placementChanged = HasInstallationEditPlacementChanged(
            editSession,
            anchorCoordinate,
            quarterTurns,
            conveyorVariantKind);

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        MapObject desiredSourcePrefab = null;
        if (editSession.definition.mapObject is ConveyorBelt conveyorPrototype && conveyorVariantKind >= 0)
        {
            desiredSourcePrefab = ResolveConveyorVariantPrefab(conveyorPrototype, conveyorVariantKind);
        }

        if (desiredSourcePrefab == null)
        {
            desiredSourcePrefab = ResolveInstalledObjectSourcePrefab(editSession.definition, anchorCoordinate, quarterTurns);
        }
        MapObject restoredObject = ResolveRestoredInstallationObject(editSession, desiredSourcePrefab, terrain);
        if (restoredObject == null)
        {
            return null;
        }

        Transform installParent = terrain != null ? terrain.transform : transform;
        Quaternion restoredRotation = previewRotationOverride
            ?? GetInstalledObjectRotation(
                desiredSourcePrefab != null ? desiredSourcePrefab : editSession.definition.mapObject,
                quarterTurns);
        restoredObject.transform.SetParent(installParent, true);
        restoredObject.transform.SetPositionAndRotation(
            previewPositionOverride
                ?? GetInstalledObjectWorldPosition(
                    anchorCoordinate,
                    desiredSourcePrefab != null ? desiredSourcePrefab : editSession.definition.mapObject,
                    quarterTurns,
                    0f),
            restoredRotation);
        restoredObject.gameObject.SetActive(true);

        MapObject footprintSource = desiredSourcePrefab != null ? desiredSourcePrefab : editSession.definition.mapObject;
        List<Vector2Int> occupiedCoordinates = GetFootprintCoordinates(
            anchorCoordinate,
            footprintSource,
            quarterTurns);
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            if (terrain != null && terrain.TryGetLoadedBlock(occupiedCoordinates[i], out Block block) && block != null)
            {
                if (ShouldBindInstalledObjectToBlock(block, anchorCoordinate, footprintSource, quarterTurns))
                {
                    block.SetMapObject(restoredObject);
                    block.ApplyFloorObjectState(null);
                }
            }
        }

        ConfigureInstalledObjectRuntime(restoredObject, anchorCoordinate, quarterTurns, editSession.inputOutputState);
        restoredObject.ApplyItemFilterMask(editSession.itemFilterMaskWords, editSession.itemFilterMaskInitialized);
        if (restoredObject is BoxObject restoredBoxObject && editSession.boxIsOpen.HasValue)
        {
            restoredBoxObject.SetOpenState(editSession.boxIsOpen.Value, false);
        }

        ApplyEditedInstallationBlockStates(editSession, anchorCoordinate, quarterTurns);
        RestoreAttachedAreaBoxes(editSession, anchorCoordinate, quarterTurns, terrain);
        RegisterInstalledObjectPersistence(restoredObject);
        if (placementChanged)
        {
            if (TryCreateOriginalEditConveyorChange(editSession, out ConveyorChangeInfo removedConveyorChange))
            {
                NormalizeDisconnectedConveyorCornersAroundChanges(
                    new List<ConveyorChangeInfo> { removedConveyorChange },
                    false,
                    activeInstallPreview);
            }
            else
            {
                NormalizeDisconnectedConveyorCornersAroundCoordinates(editSession.originalOccupiedCoordinates, false, activeInstallPreview);
            }
            NormalizeDisconnectedConveyorCornersAroundCoordinates(
                occupiedCoordinates,
                false,
                activeInstallPreview,
                new[] { anchorCoordinate });
        }

        if (restoredObject is InstallationObject restoredInstallation)
        {
            SelectEditableInstallation(restoredInstallation, anchorCoordinate);
        }

        return restoredObject;
    }

    private void ApplyEditedInstallationBlockStates(InstallationEditSession editSession, Vector2Int newAnchorCoordinate, int newQuarterTurns)
    {
        if (editSession == null || editSession.originalInstallation == null)
        {
            return;
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null)
        {
            return;
        }

        List<Vector2Int> newStateCoordinates = GetInstallationEditStateCoordinates(editSession, newAnchorCoordinate, newQuarterTurns);
        for (int i = 0; i < newStateCoordinates.Count; i++)
        {
            Vector2Int coordinate = newStateCoordinates[i];
            if (!terrain.TryGetLoadedBlock(coordinate, out Block block) || block == null)
            {
                continue;
            }

            Vector2Int worldOffset = coordinate - newAnchorCoordinate;
            Vector2Int canonicalOffset = RotateFootprintOffset(worldOffset, -newQuarterTurns);
            if (editSession.blockStatesByCanonicalOffset.TryGetValue(canonicalOffset, out List<int> blockState))
            {
                block.ApplyFloorObjectState(blockState);
            }
            else
            {
                block.ApplyFloorObjectState(null);
            }
        }
    }

    private void RestoreAttachedAreaBoxes(
        InstallationEditSession editSession,
        Vector2Int newAnchorCoordinate,
        int newQuarterTurns,
        TerrainGenerator terrain)
    {
        if (editSession?.attachedAreaBoxes == null || editSession.attachedAreaBoxes.Count <= 0)
        {
            return;
        }

        Transform installParent = terrain != null ? terrain.transform : transform;
        for (int i = 0; i < editSession.attachedAreaBoxes.Count; i++)
        {
            AreaAttachedBoxState boxState = editSession.attachedAreaBoxes[i];
            if (boxState?.boxObject == null)
            {
                continue;
            }

            BoxObject boxObject = boxState.boxObject;
            MapObject footprintSource = boxState.definition != null && boxState.definition.mapObject != null
                ? boxState.definition.mapObject
                : boxObject;
            Vector2Int targetAnchorCoordinate = GetMovedAttachedBoxAnchorCoordinate(
                editSession,
                boxState,
                newAnchorCoordinate,
                newQuarterTurns);
            int targetQuarterTurns = GetMovedAttachedBoxQuarterTurns(editSession, boxState, newQuarterTurns);

            boxObject.transform.SetParent(installParent, true);
            boxObject.transform.SetPositionAndRotation(
                GetInstalledObjectWorldPosition(targetAnchorCoordinate, footprintSource, targetQuarterTurns, 0f),
                GetInstalledObjectRotation(footprintSource, targetQuarterTurns));
            boxObject.gameObject.SetActive(true);

            List<Vector2Int> boxCoordinates = GetFootprintCoordinates(targetAnchorCoordinate, footprintSource, targetQuarterTurns);
            for (int coordinateIndex = 0; coordinateIndex < boxCoordinates.Count; coordinateIndex++)
            {
                if (terrain != null
                    && terrain.TryGetLoadedBlock(boxCoordinates[coordinateIndex], out Block block)
                    && block != null
                    && ShouldBindInstalledObjectToBlock(block, targetAnchorCoordinate, footprintSource, targetQuarterTurns))
                {
                    block.SetMapObject(boxObject);
                }
            }

            ConfigureInstalledObjectRuntime(boxObject, targetAnchorCoordinate, targetQuarterTurns);
            boxObject.ApplyItemFilterMask(boxState.itemFilterMaskWords, boxState.itemFilterMaskInitialized);
            if (boxState.boxIsOpen.HasValue)
            {
                boxObject.SetOpenState(boxState.boxIsOpen.Value, false);
            }

            RegisterInstalledObjectPersistence(boxObject);
        }
    }

    private MapObject ResolveRestoredInstallationObject(
        InstallationEditSession editSession,
        MapObject desiredSourcePrefab,
        TerrainGenerator terrain)
    {
        if (editSession?.originalInstallation == null)
        {
            return null;
        }

        MapObject originalObject = editSession.originalInstallation;
        if (!RequiresConveyorPreviewReplacement(originalObject, desiredSourcePrefab))
        {
            return originalObject;
        }

        if (desiredSourcePrefab == null)
        {
            return originalObject;
        }

        Transform installParent = terrain != null ? terrain.transform : transform;
        MapObject replacementObject = CreateInstalledObjectInstance(desiredSourcePrefab, installParent, terrain);
        if (replacementObject == null)
        {
            return originalObject;
        }

        replacementObject.ApplyItemFilterMask(editSession.itemFilterMaskWords, editSession.itemFilterMaskInitialized);

        ReleaseInstalledObjectInstance(
            editSession.originalInstallation,
            ResolveInstallationSourcePrefab(
                editSession.definition,
                editSession.originalConveyorVariantKind),
            terrain);

        editSession.originalInstallation = replacementObject as InstallationObject;
        return replacementObject;
    }

    private void HandleInstallRotationClicked()
    {
        if (!IsInstallationModeActive())
        {
            return;
        }

        EnsureValidActiveInstallPreview();
        RotateInstallPreviewClockwise();
    }

    private void HandleInstallCompleteClicked()
    {
        if (IsEditingInstallation())
        {
            CompleteInstallationEdit();
            return;
        }

        if (mapEditModeActive)
        {
            SetMapEditModeActive(false);
            return;
        }

        if (!IsInstallationModeActive())
        {
            return;
        }

        CleanupInstallPreviewReferences();
        if (installPreviewInstances.Count <= 0 || activeInstallDefinition == null)
        {
            ClearInstallPreview();
            return;
        }

        List<MapObject> previewsToPlace = new List<MapObject>(installPreviewInstances);
        List<InstallPreviewPlacementPlan> placementPlans = new List<InstallPreviewPlacementPlan>(previewsToPlace.Count);
        List<InstallPreviewPlacementReservation> reservedFootprintReservations =
            new List<InstallPreviewPlacementReservation>();
        List<MapObject> placedPreviews = new List<MapObject>(previewsToPlace.Count);
        List<Vector2Int> placedAnchorCoordinates = new List<Vector2Int>(previewsToPlace.Count);
        List<MapObject> placedObjects = new List<MapObject>(previewsToPlace.Count);
        List<PortableObject> placedAnimationSources = new List<PortableObject>(previewsToPlace.Count);
        List<bool> placedUsedReservations = new List<bool>(previewsToPlace.Count);
        int placedCount = 0;
        int unreservedPlacedCount = 0;

        for (int i = 0; i < previewsToPlace.Count; i++)
        {
            if (TryCreateInstallPreviewPlacementPlan(
                    previewsToPlace[i],
                    reservedFootprintReservations,
                    out InstallPreviewPlacementPlan placementPlan))
            {
                placementPlans.Add(placementPlan);
            }
            else if (TryCreateStraightConveyorPlacementPlan(
                         previewsToPlace[i],
                         reservedFootprintReservations,
                         out placementPlan))
            {
                placementPlans.Add(placementPlan);
            }
        }

        if (!IsEditingInstallation())
        {
            int availableInstallItemCount = GetAvailableInstallItemCount();
            if (placementPlans.Count > availableInstallItemCount)
            {
                placementPlans.RemoveRange(availableInstallItemCount, placementPlans.Count - availableInstallItemCount);
            }
        }

        for (int i = 0; i < placementPlans.Count; i++)
        {
            InstallPreviewPlacementPlan placementPlan = placementPlans[i];
            TerrainGenerator terrain = ResolveInstallPreviewTerrain();
            Transform installParent = terrain != null ? terrain.transform : placementPlan.anchorBlock.transform;
            MapObject installedObject = CreateInstalledObjectInstance(placementPlan.sourcePrefab, installParent, terrain);
            if (installedObject == null)
            {
                continue;
            }

            installedObject.transform.SetPositionAndRotation(
                placementPlan.position,
                placementPlan.rotation);

            for (int blockIndex = 0; blockIndex < placementPlan.footprintBlocks.Count; blockIndex++)
            {
                Block footprintBlock = placementPlan.footprintBlocks[blockIndex];
                if (ShouldBindInstalledObjectToBlock(
                        footprintBlock,
                        placementPlan.anchorBlock.Coordinate,
                        placementPlan.sourcePrefab,
                        placementPlan.quarterTurns))
                {
                    footprintBlock.SetMapObject(installedObject);
                }
            }

            ConfigureInstalledObjectRuntime(installedObject, placementPlan.anchorBlock.Coordinate, placementPlan.quarterTurns);
            RegisterInstalledObjectPersistence(installedObject);
            RememberLastInstalledRotation(activeInstallDefinition, placementPlan.quarterTurns);
            placedPreviews.Add(placementPlan.preview);
            placedAnchorCoordinates.Add(placementPlan.anchorBlock.Coordinate);
            placedObjects.Add(installedObject);
            if (TryConsumeInstallPreviewReservation(placementPlan.preview, out PortableObject reservedSourcePortableObject))
            {
                placedAnimationSources.Add(reservedSourcePortableObject);
                placedUsedReservations.Add(true);
            }
            else
            {
                placedAnimationSources.Add(null);
                placedUsedReservations.Add(false);
                unreservedPlacedCount++;
            }

            placedCount++;
        }

        if (placedCount > 0)
        {
            List<PortableObject> handPortableSources = unreservedPlacedCount > 0
                ? GetPlayerHandPortableSources(activeInstallDefinition.id, unreservedPlacedCount)
                : new List<PortableObject>();
            int handPortableSourceIndex = 0;
            for (int i = 0; i < placedObjects.Count; i++)
            {
                PortableObject sourcePortableObject = i < placedAnimationSources.Count
                    ? placedAnimationSources[i]
                    : null;
                if (i < placedUsedReservations.Count
                    && !placedUsedReservations[i])
                {
                    sourcePortableObject = handPortableSourceIndex < handPortableSources.Count
                        ? handPortableSources[handPortableSourceIndex]
                        : null;
                    handPortableSourceIndex++;
                }

                PlayInstallPlacementAnimation(
                    placedObjects[i],
                    sourcePortableObject,
                    activeInstallDefinition.id,
                    i * Mathf.Max(0f, installPlacementPortableLaunchInterval));
            }

            if (unreservedPlacedCount > 0)
            {
                RemoveInstallItemsFromPlayer(activeInstallDefinition.id, unreservedPlacedCount);
            }
        }

        RemovePlacedInstallPreviews(placedPreviews);
        if (placedAnchorCoordinates.Count > 0)
        {
            NormalizeDisconnectedConveyorCornersAroundCoordinates(
                placedAnchorCoordinates,
                false,
                null,
                placedAnchorCoordinates);
        }
    }

    private bool TryCreateInstallPreviewPlacementPlan(
        MapObject preview,
        List<InstallPreviewPlacementReservation> reservedFootprintReservations,
        out InstallPreviewPlacementPlan placementPlan)
    {
        placementPlan = null;
        if (preview == null
            || activeInstallDefinition == null
            || !TryGetBlockForPreview(preview, out Block anchorBlock))
        {
            return false;
        }

        int previewQuarterTurns = GetPreviewQuarterTurns(preview);
        installPreviewSourcePrefabsByPreview.TryGetValue(preview, out MapObject sourcePrefab);

        if (activeInstallDefinition.mapObject is ConveyorBelt conveyorPrototype)
        {
            ConveyorPreviewVariantMode previewVariantMode = preview == activeInstallPreview
                ? installPreviewConveyorVariantMode
                : GetConveyorPreviewVariantMode(preview);

            if (previewVariantMode == ConveyorPreviewVariantMode.Straight)
            {
                sourcePrefab = ResolveConveyorVariantPrefab(conveyorPrototype, 0) ?? sourcePrefab;
            }
            else if (preview is ConveyorBelt previewConveyor)
            {
                sourcePrefab = ResolveConveyorVariantPrefab(conveyorPrototype, GetConveyorVariantKind(previewConveyor)) ?? sourcePrefab;
            }
            else
            {
                sourcePrefab = ResolveInstalledObjectSourcePrefab(
                                   activeInstallDefinition,
                                   anchorBlock.Coordinate,
                                   previewQuarterTurns,
                                   preview,
                                   previewVariantMode)
                               ?? sourcePrefab;
            }
        }

        if (sourcePrefab == null)
        {
            sourcePrefab = ResolveInstalledObjectSourcePrefab(activeInstallDefinition, anchorBlock.Coordinate, previewQuarterTurns, preview);
        }

        if (sourcePrefab == null)
        {
            sourcePrefab = activeInstallDefinition.mapObject;
        }

        if (sourcePrefab == null)
        {
            return false;
        }

        int resolvedQuarterTurns = ResolvePlacementQuarterTurnsFromRotation(
            sourcePrefab,
            preview.transform.rotation,
            previewQuarterTurns);

        if (!TryFindPlaceablePlacementPlanFootprint(
                anchorBlock.Coordinate,
                sourcePrefab,
                preview,
                resolvedQuarterTurns,
                reservedFootprintReservations,
                out resolvedQuarterTurns,
                out List<Block> footprintBlocks,
                out List<Vector2Int> footprintCoordinates))
        {
            return false;
        }

        AddReservedPlacementFootprint(
            reservedFootprintReservations,
            sourcePrefab,
            anchorBlock.Coordinate,
            resolvedQuarterTurns,
            footprintCoordinates);

        placementPlan = new InstallPreviewPlacementPlan
        {
            preview = preview,
            anchorBlock = anchorBlock,
            sourcePrefab = sourcePrefab,
            quarterTurns = resolvedQuarterTurns,
            footprintBlocks = footprintBlocks,
            position = GetPreviewWorldPosition(anchorBlock, sourcePrefab, resolvedQuarterTurns, installPreviewVerticalOffset),
            rotation = GetPlacementObjectRotation(sourcePrefab, resolvedQuarterTurns)
        };
        return true;
    }

    private bool TryCreateStraightConveyorPlacementPlan(
        MapObject preview,
        List<InstallPreviewPlacementReservation> reservedFootprintReservations,
        out InstallPreviewPlacementPlan placementPlan)
    {
        placementPlan = null;
        if (preview == null
            || activeInstallDefinition == null
            || !CanUseStraightConveyorFallback(preview)
            || !TryGetBlockForPreview(preview, out Block anchorBlock)
            || !TryResolveStraightConveyorPlacementSource(out MapObject sourcePrefab))
        {
            return false;
        }

        int previewQuarterTurns = GetPreviewQuarterTurns(preview);
        int resolvedQuarterTurns = ResolvePlacementQuarterTurnsFromRotation(
            sourcePrefab,
            preview.transform.rotation,
            previewQuarterTurns);

        if (!TryFindPlaceablePlacementPlanFootprint(
                anchorBlock.Coordinate,
                sourcePrefab,
                preview,
                resolvedQuarterTurns,
                reservedFootprintReservations,
                out resolvedQuarterTurns,
                out List<Block> footprintBlocks,
                out List<Vector2Int> footprintCoordinates))
        {
            return false;
        }

        AddReservedPlacementFootprint(
            reservedFootprintReservations,
            sourcePrefab,
            anchorBlock.Coordinate,
            resolvedQuarterTurns,
            footprintCoordinates);

        placementPlan = new InstallPreviewPlacementPlan
        {
            preview = preview,
            anchorBlock = anchorBlock,
            sourcePrefab = sourcePrefab,
            quarterTurns = resolvedQuarterTurns,
            footprintBlocks = footprintBlocks,
            position = GetPreviewWorldPosition(anchorBlock, sourcePrefab, resolvedQuarterTurns, installPreviewVerticalOffset),
            rotation = GetPlacementObjectRotation(sourcePrefab, resolvedQuarterTurns)
        };
        return true;
    }

    private bool TryFindPlaceablePlacementPlanFootprint(
        Vector2Int anchorCoordinate,
        MapObject sourcePrefab,
        MapObject previewToIgnore,
        int preferredQuarterTurns,
        List<InstallPreviewPlacementReservation> reservedFootprintReservations,
        out int resolvedQuarterTurns,
        out List<Block> footprintBlocks,
        out List<Vector2Int> footprintCoordinates)
    {
        resolvedQuarterTurns = NormalizePlacementQuarterTurns(preferredQuarterTurns);
        footprintBlocks = null;
        footprintCoordinates = null;

        if (sourcePrefab == null)
        {
            return false;
        }

        for (int offset = 0; offset < 4; offset++)
        {
            int candidateQuarterTurns = NormalizePlacementQuarterTurns(resolvedQuarterTurns + offset);
            if (!TryGetFootprintBlocks(
                    anchorCoordinate,
                    sourcePrefab,
                    candidateQuarterTurns,
                    previewToIgnore,
                    out List<Block> candidateFootprintBlocks,
                    true)
                || candidateFootprintBlocks == null
                || candidateFootprintBlocks.Count <= 0)
            {
                continue;
            }

            List<Vector2Int> candidateFootprintCoordinates = GetFootprintCoordinates(
                anchorCoordinate,
                sourcePrefab,
                candidateQuarterTurns);
            if (CoordinatesOverlapReserved(
                    anchorCoordinate,
                    sourcePrefab,
                    candidateQuarterTurns,
                    candidateFootprintCoordinates,
                    reservedFootprintReservations))
            {
                continue;
            }

            resolvedQuarterTurns = candidateQuarterTurns;
            footprintBlocks = candidateFootprintBlocks;
            footprintCoordinates = candidateFootprintCoordinates;
            return true;
        }

        return false;
    }

    private static void AddReservedPlacementFootprint(
        List<InstallPreviewPlacementReservation> reservations,
        MapObject sourcePrefab,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        IReadOnlyList<Vector2Int> footprintCoordinates)
    {
        if (reservations == null
            || sourcePrefab == null
            || footprintCoordinates == null
            || footprintCoordinates.Count <= 0)
        {
            return;
        }

        reservations.Add(new InstallPreviewPlacementReservation
        {
            sourcePrefab = sourcePrefab,
            anchorCoordinate = anchorCoordinate,
            quarterTurns = NormalizePlacementQuarterTurns(quarterTurns),
            footprintCoordinates = new List<Vector2Int>(footprintCoordinates)
        });
    }

    private bool CoordinatesOverlapReserved(
        Vector2Int anchorCoordinate,
        MapObject sourcePrefab,
        int quarterTurns,
        IReadOnlyList<Vector2Int> coordinates,
        IReadOnlyList<InstallPreviewPlacementReservation> reservedFootprintReservations)
    {
        if (coordinates == null
            || reservedFootprintReservations == null
            || reservedFootprintReservations.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            for (int reservationIndex = 0; reservationIndex < reservedFootprintReservations.Count; reservationIndex++)
            {
                InstallPreviewPlacementReservation reservation = reservedFootprintReservations[reservationIndex];
                if (reservation == null
                    || reservation.footprintCoordinates == null
                    || !reservation.footprintCoordinates.Contains(coordinate))
                {
                    continue;
                }

                if (!CanOverlapCompatiblePlacementItemAreas(
                        coordinate,
                        sourcePrefab,
                        GetRectGridBlockTypeAtCoordinate(
                            anchorCoordinate,
                            sourcePrefab,
                            quarterTurns,
                            coordinate),
                        anchorCoordinate,
                        quarterTurns,
                        reservation.sourcePrefab,
                        GetRectGridBlockTypeAtCoordinate(
                            reservation.anchorCoordinate,
                            reservation.sourcePrefab,
                            reservation.quarterTurns,
                            coordinate),
                        reservation.anchorCoordinate,
                        reservation.quarterTurns))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void ConfigureInstalledInputOutputMarkers(MapObject installedObject, Vector2Int anchorCoordinate, int quarterTurns)
    {
        ConfigureInputOutputMarkers(installedObject, anchorCoordinate, quarterTurns, false);
    }

    private void RefreshInstallPreviewAreaMarkers(MapObject preview)
    {
        if (preview == null)
        {
            return;
        }

        if (!TryGetPreviewAnchorCoordinate(preview, out Vector2Int anchorCoordinate))
        {
            ClearInputOutputMarkers(preview);
            return;
        }

        ConfigureInputOutputMarkers(preview, anchorCoordinate, GetPreviewQuarterTurns(preview), true);
    }

    private static void ClearInputOutputMarkers(MapObject mapObject)
    {
        InputOutputModuleAreaMarkerController markerController =
            mapObject != null ? mapObject.GetComponent<InputOutputModuleAreaMarkerController>() : null;
        if (markerController != null)
        {
            markerController.Configure(null, null);
        }
    }

    private void ConfigureInputOutputMarkers(
        MapObject mapObject,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        bool isInstallPreview)
    {
        if (!TryGetInputOutputModule(mapObject, out _))
        {
            ClearInputOutputMarkers(mapObject);
            return;
        }

        List<AreaMarkerSpawnRequest> markerRequests = new List<AreaMarkerSpawnRequest>();
        List<Vector3> primaryObjectWorldPositions = GetRectGridBlockWorldPositions(
            anchorCoordinate,
            mapObject,
            quarterTurns,
            InputOutputModule.RectGridBlockType.Object);
        Vector3 primaryObjectWorldPosition = primaryObjectWorldPositions.Count > 0
            ? primaryObjectWorldPositions[0]
            : mapObject.transform.position;
        Sprite arrowIcon = ResolveArrowMarkerIcon();

        List<Vector3> inputEnergyWorldPositions = GetRectGridBlockWorldPositions(
            anchorCoordinate,
            mapObject,
            quarterTurns,
            InputOutputModule.RectGridBlockType.InputEnergy);
        AddAreaMarkerRequests(
            markerRequests,
            inputEnergyWorldPositions,
            ResolveInputEnergyMarkerIcon(mapObject));

        List<Vector3> inputItemWorldPositions = GetRectGridBlockWorldPositions(
            anchorCoordinate,
            mapObject,
            quarterTurns,
            InputOutputModule.RectGridBlockType.InputItem);
        AddDirectionalAreaMarkerRequests(
            markerRequests,
            inputItemWorldPositions,
            arrowIcon,
            primaryObjectWorldPosition,
            false);

        List<Vector3> outputWorldPositions = GetRectGridBlockWorldPositions(
            anchorCoordinate,
            mapObject,
            quarterTurns,
            InputOutputModule.RectGridBlockType.Output);
        AddDirectionalAreaMarkerRequests(
            markerRequests,
            outputWorldPositions,
            arrowIcon,
            primaryObjectWorldPosition,
            true);

        if (markerRequests.Count <= 0)
        {
            ClearInputOutputMarkers(mapObject);
            return;
        }

        AreaMarkerPool pool = ResolveAreaMarkerPool();
        if (pool == null)
        {
            ClearInputOutputMarkers(mapObject);
            return;
        }

        InputOutputModuleAreaMarkerController markerController = mapObject.GetComponent<InputOutputModuleAreaMarkerController>();
        if (markerController == null)
        {
            markerController = mapObject.gameObject.AddComponent<InputOutputModuleAreaMarkerController>();
        }

        markerController.enabled = true;
        markerController.Configure(
            pool,
            markerRequests,
            isInstallPreview,
            isInstallPreview ? InstallPreviewAreaMarkerSortingOrderOffset : 0,
            false);
    }

    private void ConfigureInstalledInputOutputEnergyAreas(MapObject installedObject, Vector2Int anchorCoordinate, int quarterTurns)
    {
        if (!TryGetInputOutputModule(installedObject, out _))
        {
            return;
        }

        ItemDefinition installationDefinition = ResolveItemDefinition(installedObject);
        if (installationDefinition == null || installationDefinition.useEnergyType == ItemDefinition.EnergyType.None)
        {
            return;
        }

        if (!TryGetRectGridBlockCoordinates(
                anchorCoordinate,
                installedObject,
                quarterTurns,
                InputOutputModule.RectGridBlockType.InputEnergy,
                out List<Vector2Int> inputEnergyCoordinates)
            || inputEnergyCoordinates == null
            || inputEnergyCoordinates.Count <= 0)
        {
            return;
        }

        InputOutputModuleEnergyAreaController energyAreaController = installedObject.GetComponent<InputOutputModuleEnergyAreaController>();
        if (energyAreaController == null)
        {
            energyAreaController = installedObject.gameObject.AddComponent<InputOutputModuleEnergyAreaController>();
        }

        energyAreaController.Configure(installationDefinition.useEnergyType, inputEnergyCoordinates);
    }

    private void ConfigureInstalledInputOutputItemAreas(MapObject installedObject, Vector2Int anchorCoordinate, int quarterTurns)
    {
        if (!TryGetInputOutputModule(installedObject, out InputOutputModule inputOutputModule))
        {
            return;
        }

        InputOutputModuleItemAreaController itemAreaController = installedObject.GetComponent<InputOutputModuleItemAreaController>();
        if (!TryGetOrderedInputItemAreaBindings(
                anchorCoordinate,
                installedObject,
                quarterTurns,
                inputOutputModule,
                out List<InputOutputModuleItemAreaBinding> itemAreaBindings)
            || itemAreaBindings == null
            || itemAreaBindings.Count <= 0)
        {
            itemAreaController?.Configure(null);
            return;
        }

        if (itemAreaController == null)
        {
            itemAreaController = installedObject.gameObject.AddComponent<InputOutputModuleItemAreaController>();
        }

        itemAreaController.Configure(itemAreaBindings);
    }

    private void ConfigureInstalledInputOutputOutputAreas(MapObject installedObject, Vector2Int anchorCoordinate, int quarterTurns)
    {
        if (!TryGetInputOutputModule(installedObject, out _))
        {
            return;
        }

        InputOutputModuleOutputAreaController outputAreaController = installedObject.GetComponent<InputOutputModuleOutputAreaController>();
        if (!TryGetRectGridBlockCoordinates(
                anchorCoordinate,
                installedObject,
                quarterTurns,
                InputOutputModule.RectGridBlockType.Output,
                out List<Vector2Int> outputCoordinates)
            || outputCoordinates == null
            || outputCoordinates.Count <= 0)
        {
            outputAreaController?.Configure(null);
            return;
        }

        if (outputAreaController == null)
        {
            outputAreaController = installedObject.gameObject.AddComponent<InputOutputModuleOutputAreaController>();
        }

        outputAreaController.Configure(outputCoordinates);
    }

    private void ConfigureInstalledInputOutputRuntimeAreas(MapObject installedObject, Vector2Int anchorCoordinate, int quarterTurns)
    {
        if (!TryGetInputOutputModule(installedObject, out InputOutputModule inputOutputModule))
        {
            return;
        }

        List<Vector2Int> inputEnergyCoordinates = new List<Vector2Int>();
        TryGetRectGridBlockCoordinates(
            anchorCoordinate,
            installedObject,
            quarterTurns,
            InputOutputModule.RectGridBlockType.InputEnergy,
            out inputEnergyCoordinates);

        List<InputOutputModuleItemAreaBinding> itemAreaBindings = new List<InputOutputModuleItemAreaBinding>();
        TryGetOrderedInputItemAreaBindings(
            anchorCoordinate,
            installedObject,
            quarterTurns,
            inputOutputModule,
            out itemAreaBindings);

        List<Vector2Int> outputCoordinates = new List<Vector2Int>();
        TryGetRectGridBlockCoordinates(
            anchorCoordinate,
            installedObject,
            quarterTurns,
            InputOutputModule.RectGridBlockType.Output,
            out outputCoordinates);

        inputOutputModule.ConfigureRuntimeAreas(inputEnergyCoordinates, itemAreaBindings, outputCoordinates);
    }

    private void ConfigureInstalledInputOutputRuntimeGrid(MapObject installedObject, Vector2Int anchorCoordinate, int quarterTurns)
    {
        if (!TryGetInputOutputModule(installedObject, out InputOutputModule inputOutputModule))
        {
            return;
        }

        List<Vector2Int> footprintCoordinates = GetFootprintCoordinates(anchorCoordinate, installedObject, quarterTurns);
        List<Vector2Int> focusCoordinates = GetPlacementVisualCoordinates(anchorCoordinate, installedObject, quarterTurns);
        inputOutputModule.ConfigureRuntimeGridCoordinates(footprintCoordinates);
        inputOutputModule.ConfigureRuntimeFocusCoordinates(focusCoordinates);
    }

    private List<Vector2Int> GetInstalledObjectBlockingCoordinates(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns)
    {
        if (TryGetRectGridBlockCoordinates(
                anchorCoordinate,
                footprintSource,
                quarterTurns,
                InputOutputModule.RectGridBlockType.Object,
                out List<Vector2Int> objectCoordinates)
            && objectCoordinates.Count > 0)
        {
            return objectCoordinates;
        }

        return GetFootprintCoordinates(anchorCoordinate, footprintSource, quarterTurns);
    }

    public void ConfigureInstalledObjectRuntime(
        MapObject installedObject,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        InputOutputModule.PersistentState persistentState = null,
        long placementSequence = 0)
    {
        if (installedObject == null)
        {
            return;
        }

        if (installedObject is InstallationObject installationObject)
        {
            installationObject.ConfigurePlacementRuntime(
                anchorCoordinate,
                quarterTurns,
                GetInstalledObjectBlockingCoordinates(anchorCoordinate, installedObject, quarterTurns),
                placementSequence);
        }

        ConfigureInstalledInputOutputMarkers(installedObject, anchorCoordinate, quarterTurns);
        ConfigureInstalledInputOutputEnergyAreas(installedObject, anchorCoordinate, quarterTurns);
        ConfigureInstalledInputOutputItemAreas(installedObject, anchorCoordinate, quarterTurns);
        ConfigureInstalledInputOutputOutputAreas(installedObject, anchorCoordinate, quarterTurns);
        ConfigureInstalledInputOutputRuntimeAreas(installedObject, anchorCoordinate, quarterTurns);
        ConfigureInstalledInputOutputRuntimeGrid(installedObject, anchorCoordinate, quarterTurns);

        if (persistentState != null && TryGetInputOutputModule(installedObject, out InputOutputModule inputOutputModule))
        {
            inputOutputModule.ApplyPersistentState(persistentState);
            ConfigureInstalledInputOutputRuntimeAreas(installedObject, anchorCoordinate, quarterTurns);
            ConfigureInstalledInputOutputRuntimeGrid(installedObject, anchorCoordinate, quarterTurns);
        }
    }

    public Quaternion GetInstalledObjectRotation(ItemDefinition definition, int quarterTurns)
    {
        return GetInstalledObjectRotation(definition != null ? definition.mapObject : null, quarterTurns);
    }

    public Quaternion GetInstalledObjectRotation(MapObject sourcePrefab, int quarterTurns)
    {
        return GetPlacementObjectRotation(sourcePrefab, quarterTurns);
    }

    private static int GetConveyorVariantKind(MapObject mapObject)
    {
        if (!(mapObject is ConveyorBelt conveyorBelt) || conveyorBelt == null)
        {
            return -1;
        }

        if (conveyorBelt.IsReverseCornerVariant)
        {
            return 2;
        }

        if (conveyorBelt.IsCornerVariant)
        {
            return 1;
        }

        return 0;
    }

    private static ConveyorPreviewVariantMode GetConveyorPreviewVariantMode(MapObject mapObject)
    {
        return GetConveyorVariantKind(mapObject) > 0
            ? ConveyorPreviewVariantMode.Corner
            : ConveyorPreviewVariantMode.Straight;
    }

    private static MapObject ResolveConveyorVariantPrefab(ConveyorBelt conveyorPrototype, int conveyorVariantKind)
    {
        if (conveyorPrototype == null)
        {
            return null;
        }

        return conveyorVariantKind switch
        {
            2 => conveyorPrototype.ReverseCornerVariantPrefab != null
                ? conveyorPrototype.ReverseCornerVariantPrefab
                : conveyorPrototype.CornerVariantPrefab,
            1 => conveyorPrototype.CornerVariantPrefab != null
                ? conveyorPrototype.CornerVariantPrefab
                : conveyorPrototype.StraightVariantPrefab,
            0 => conveyorPrototype.StraightVariantPrefab,
            _ => null
        };
    }

    public Vector3 GetInstalledObjectWorldPosition(
        Vector2Int anchorCoordinate,
        ItemDefinition definition,
        int quarterTurns,
        float verticalOffset = 0f)
    {
        return GetInstalledObjectWorldPosition(
            anchorCoordinate,
            definition != null ? definition.mapObject : null,
            quarterTurns,
            verticalOffset);
    }

    public Vector3 GetInstalledObjectWorldPosition(
        Vector2Int anchorCoordinate,
        MapObject sourcePrefab,
        int quarterTurns,
        float verticalOffset = 0f)
    {
        return GetPlacementWorldPositionFromAnchorCoordinate(
            anchorCoordinate,
            sourcePrefab,
            quarterTurns,
            verticalOffset);
    }

    public MapObject ResolveInstalledObjectSourcePrefab(ItemDefinition definition, Vector2Int anchorCoordinate, int quarterTurns)
    {
        return ResolveInstalledObjectSourcePrefab(
            definition,
            anchorCoordinate,
            quarterTurns,
            null,
            ConveyorPreviewVariantMode.Corner);
    }

    private MapObject ResolveInstalledObjectSourcePrefab(ItemDefinition definition, Vector2Int anchorCoordinate, int quarterTurns, MapObject previewToIgnore)
    {
        return ResolveInstalledObjectSourcePrefab(
            definition,
            anchorCoordinate,
            quarterTurns,
            previewToIgnore,
            installPreviewConveyorVariantMode);
    }

    private MapObject ResolveInstalledObjectSourcePrefab(
        ItemDefinition definition,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        MapObject previewToIgnore,
        ConveyorPreviewVariantMode conveyorVariantMode)
    {
        if (definition == null || definition.mapObject == null)
        {
            return null;
        }

        if (!(definition.mapObject is ConveyorBelt conveyorPrototype))
        {
            return definition.mapObject;
        }

        if (conveyorVariantMode != ConveyorPreviewVariantMode.Corner)
        {
            return conveyorPrototype.StraightVariantPrefab != null
                ? conveyorPrototype.StraightVariantPrefab
                : definition.mapObject;
        }

        if (!TryResolveConveyorCornerPlacementPrefab(
                conveyorPrototype,
                anchorCoordinate,
                quarterTurns,
                previewToIgnore,
                out MapObject resolvedCornerPrefab))
        {
            return conveyorPrototype.StraightVariantPrefab != null
                ? conveyorPrototype.StraightVariantPrefab
                : definition.mapObject;
        }

        return resolvedCornerPrefab;
    }

    public List<Vector2Int> GetInstalledObjectFootprintCoordinates(Vector2Int anchorCoordinate, ItemDefinition definition, int quarterTurns)
    {
        return GetFootprintCoordinates(anchorCoordinate, definition != null ? definition.mapObject : null, quarterTurns);
    }

    public List<Vector2Int> GetInstalledObjectFocusCoordinates(Vector2Int anchorCoordinate, ItemDefinition definition, int quarterTurns)
    {
        return GetPlacementVisualCoordinates(anchorCoordinate, definition != null ? definition.mapObject : null, quarterTurns);
    }

    private Quaternion GetPlacementObjectRotation(MapObject sourcePrefab, int quarterTurns)
    {
        if (sourcePrefab == null)
        {
            return Quaternion.identity;
        }

        return GetPlacementObjectRotation(sourcePrefab.transform.rotation, sourcePrefab, quarterTurns);
    }

    private Quaternion GetPlacementObjectRotation(Quaternion baseRotation, MapObject sourcePrefab, int quarterTurns)
    {
        if (sourcePrefab == null)
        {
            return baseRotation;
        }

        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        int rotationQuarterTurns = normalizedQuarterTurns;
        if (sourcePrefab is ConveyorBelt conveyorBelt)
        {
            rotationQuarterTurns = (rotationQuarterTurns + conveyorBelt.PlacementRotationQuarterTurnOffset) % 4;
        }

        return baseRotation * Quaternion.Euler(0f, rotationQuarterTurns * 90f, 0f);
    }

    private bool TryResolveConveyorCornerPlacementPrefab(
        ConveyorBelt conveyorPrototype,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        MapObject previewToIgnore,
        out MapObject resolvedPrefab)
    {
        return TryResolveConveyorCornerPlacementPrefab(
            conveyorPrototype,
            anchorCoordinate,
            quarterTurns,
            previewToIgnore,
            out resolvedPrefab,
            out _,
            false);
    }

    private bool TryResolveConveyorCornerPlacementPrefab(
        ConveyorBelt conveyorPrototype,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        MapObject previewToIgnore,
        out MapObject resolvedPrefab,
        out int resolvedQuarterTurns,
        bool preferDifferentFromPreview = false)
    {
        resolvedPrefab = null;
        resolvedQuarterTurns = quarterTurns;
        if (conveyorPrototype == null)
        {
            return false;
        }

        if (!TryGetConveyorPlacementOutputDirection(conveyorPrototype, quarterTurns, out Vector2Int desiredOutputDirection))
        {
            return false;
        }

        ConveyorBelt cornerCandidate = conveyorPrototype.CornerVariantPrefab != null
            ? conveyorPrototype.CornerVariantPrefab
            : conveyorPrototype.StraightVariantPrefab;
        ConveyorBelt reverseCornerCandidate = conveyorPrototype.ReverseCornerVariantPrefab != null
            ? conveyorPrototype.ReverseCornerVariantPrefab
            : conveyorPrototype.CornerVariantPrefab;

        int cornerQuarterTurns = quarterTurns;
        bool cornerInputConnected = false;
        bool cornerOutputConnected = false;
        bool cornerMatches = cornerCandidate != null
            && TryGetConveyorPlacementQuarterTurnsForOutput(cornerCandidate, desiredOutputDirection, out cornerQuarterTurns)
            && TryGetConveyorEndpointConnectionsAtCoordinate(
                cornerCandidate,
                anchorCoordinate,
                cornerQuarterTurns,
                previewToIgnore,
                out cornerInputConnected,
                out cornerOutputConnected)
            && (cornerInputConnected || cornerOutputConnected);

        int reverseQuarterTurns = quarterTurns;
        bool reverseInputConnected = false;
        bool reverseOutputConnected = false;
        bool reverseMatches = reverseCornerCandidate != null
            && TryGetConveyorPlacementQuarterTurnsForOutput(reverseCornerCandidate, desiredOutputDirection, out reverseQuarterTurns)
            && TryGetConveyorEndpointConnectionsAtCoordinate(
                reverseCornerCandidate,
                anchorCoordinate,
                reverseQuarterTurns,
                previewToIgnore,
                out reverseInputConnected,
                out reverseOutputConnected)
            && (reverseInputConnected || reverseOutputConnected);

        if (!preferDifferentFromPreview
            && TryPreserveCurrentConveyorCornerState(
                anchorCoordinate,
                previewToIgnore,
                cornerCandidate,
                cornerMatches,
                cornerInputConnected,
                cornerOutputConnected,
                cornerQuarterTurns,
                reverseCornerCandidate,
                reverseMatches,
                reverseInputConnected,
                reverseOutputConnected,
                reverseQuarterTurns,
                out resolvedPrefab,
                out resolvedQuarterTurns))
        {
            return resolvedPrefab != null;
        }

        if (preferDifferentFromPreview
            && TryResolveDifferentCurrentConveyorCornerState(
                previewToIgnore,
                cornerCandidate,
                cornerMatches,
                cornerInputConnected,
                cornerOutputConnected,
                cornerQuarterTurns,
                reverseCornerCandidate,
                reverseMatches,
                reverseInputConnected,
                reverseOutputConnected,
                reverseQuarterTurns,
                out resolvedPrefab,
                out resolvedQuarterTurns))
        {
            return resolvedPrefab != null;
        }

        bool cornerPreferred = cornerMatches && cornerInputConnected;
        bool reversePreferred = reverseMatches && reverseInputConnected;

        if (cornerPreferred && !reversePreferred)
        {
            resolvedPrefab = cornerCandidate;
            resolvedQuarterTurns = cornerQuarterTurns;
            return resolvedPrefab != null;
        }

        if (reversePreferred && !cornerPreferred)
        {
            resolvedPrefab = reverseCornerCandidate;
            resolvedQuarterTurns = reverseQuarterTurns;
            return resolvedPrefab != null;
        }

        if (!cornerMatches && !reverseMatches)
        {
            return false;
        }

        if (cornerMatches && !reverseMatches)
        {
            resolvedPrefab = cornerCandidate;
            resolvedQuarterTurns = cornerQuarterTurns;
            return resolvedPrefab != null;
        }

        if (reverseMatches && !cornerMatches)
        {
            resolvedPrefab = reverseCornerCandidate;
            resolvedQuarterTurns = reverseQuarterTurns;
            return resolvedPrefab != null;
        }

        bool preferReverseVariant = previewToIgnore is ConveyorBelt previewConveyor && previewConveyor.IsReverseCornerVariant;
        resolvedPrefab = preferReverseVariant ? reverseCornerCandidate : cornerCandidate;
        resolvedQuarterTurns = preferReverseVariant ? reverseQuarterTurns : cornerQuarterTurns;
        return resolvedPrefab != null;
    }

    private bool TryPreserveCurrentConveyorCornerState(
        Vector2Int anchorCoordinate,
        MapObject currentPreview,
        ConveyorBelt cornerCandidate,
        bool cornerMatches,
        bool cornerInputConnected,
        bool cornerOutputConnected,
        int cornerQuarterTurns,
        ConveyorBelt reverseCornerCandidate,
        bool reverseMatches,
        bool reverseInputConnected,
        bool reverseOutputConnected,
        int reverseQuarterTurns,
        out MapObject resolvedPrefab,
        out int resolvedQuarterTurns)
    {
        resolvedPrefab = null;
        resolvedQuarterTurns = 0;
        if (!(currentPreview is ConveyorBelt currentConveyor)
            || !currentConveyor.IsCornerVariant
            || !TryGetPreviewAnchorCoordinate(currentPreview, out Vector2Int currentAnchorCoordinate)
            || currentAnchorCoordinate != anchorCoordinate)
        {
            return false;
        }

        if (cornerMatches
            && (cornerInputConnected || cornerOutputConnected)
            && IsSameConveyorPreviewState(currentPreview, cornerCandidate, cornerQuarterTurns))
        {
            resolvedPrefab = cornerCandidate;
            resolvedQuarterTurns = cornerQuarterTurns;
            return true;
        }

        if (reverseMatches
            && (reverseInputConnected || reverseOutputConnected)
            && IsSameConveyorPreviewState(currentPreview, reverseCornerCandidate, reverseQuarterTurns))
        {
            resolvedPrefab = reverseCornerCandidate;
            resolvedQuarterTurns = reverseQuarterTurns;
            return true;
        }

        return false;
    }

    private bool TryResolveDifferentCurrentConveyorCornerState(
        MapObject currentPreview,
        ConveyorBelt cornerCandidate,
        bool cornerMatches,
        bool cornerInputConnected,
        bool cornerOutputConnected,
        int cornerQuarterTurns,
        ConveyorBelt reverseCornerCandidate,
        bool reverseMatches,
        bool reverseInputConnected,
        bool reverseOutputConnected,
        int reverseQuarterTurns,
        out MapObject resolvedPrefab,
        out int resolvedQuarterTurns)
    {
        resolvedPrefab = null;
        resolvedQuarterTurns = 0;
        if (!(currentPreview is ConveyorBelt currentConveyor) || !currentConveyor.IsCornerVariant)
        {
            return false;
        }

        if (cornerMatches
            && (cornerInputConnected || cornerOutputConnected)
            && !IsSameConveyorPreviewState(currentPreview, cornerCandidate, cornerQuarterTurns))
        {
            resolvedPrefab = cornerCandidate;
            resolvedQuarterTurns = cornerQuarterTurns;
            return true;
        }

        if (reverseMatches
            && (reverseInputConnected || reverseOutputConnected)
            && !IsSameConveyorPreviewState(currentPreview, reverseCornerCandidate, reverseQuarterTurns))
        {
            resolvedPrefab = reverseCornerCandidate;
            resolvedQuarterTurns = reverseQuarterTurns;
            return true;
        }

        return false;
    }

    private Vector2Int[] GetValidConveyorIncomingDirections(Vector2Int anchorCoordinate, MapObject previewToIgnore)
    {
        List<Vector2Int> validIncomingDirections = new List<Vector2Int>(2);
        Vector2Int[] sideDirections =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        for (int i = 0; i < sideDirections.Length; i++)
        {
            Vector2Int sideDirection = sideDirections[i];
            if (!TryGetConveyorPlacementInfoAtCoordinate(
                    anchorCoordinate + sideDirection,
                    previewToIgnore,
                    out _,
                    out Vector2Int neighborOutputDirection))
            {
                continue;
            }

            if (neighborOutputDirection != -sideDirection)
            {
                continue;
            }

            validIncomingDirections.Add(sideDirection);
        }

        return validIncomingDirections.ToArray();
    }

    private bool TryConveyorCornerPrefabMatchesIncomingDirection(
        ConveyorBelt candidatePrefab,
        int quarterTurns,
        IReadOnlyList<Vector2Int> validIncomingDirections)
    {
        if (candidatePrefab == null || validIncomingDirections == null || validIncomingDirections.Count <= 0)
        {
            return false;
        }

        if (!candidatePrefab.TryGetInputDirection(
                GetPlacementObjectRotation(candidatePrefab, quarterTurns),
                out Vector2Int candidateInputDirection))
        {
            return false;
        }

        for (int i = 0; i < validIncomingDirections.Count; i++)
        {
            if (validIncomingDirections[i] == candidateInputDirection)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanUseConveyorCornerVariantAtCoordinate(
        ConveyorBelt conveyorPrototype,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        MapObject previewToIgnore)
    {
        if (conveyorPrototype == null)
        {
            return false;
        }

        ConveyorBelt[] candidates =
        {
            conveyorPrototype.CornerVariantPrefab,
            conveyorPrototype.ReverseCornerVariantPrefab
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            if (HasConveyorEndpointConnectionAtCoordinate(candidates[i], anchorCoordinate, quarterTurns, previewToIgnore))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasConveyorEndpointConnectionAtCoordinate(
        ConveyorBelt candidatePrefab,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        MapObject previewToIgnore)
    {
        return TryGetConveyorEndpointConnectionsAtCoordinate(
                   candidatePrefab,
                   anchorCoordinate,
                   quarterTurns,
                   previewToIgnore,
                   out bool inputConnected,
                   out bool outputConnected)
               && (inputConnected || outputConnected);
    }

    private bool TryGetConveyorEndpointConnectionsAtCoordinate(
        ConveyorBelt candidatePrefab,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        MapObject previewToIgnore,
        out bool inputConnected,
        out bool outputConnected)
    {
        inputConnected = false;
        outputConnected = false;
        if (candidatePrefab == null)
        {
            return false;
        }

        Quaternion candidateRotation = GetPlacementObjectRotation(candidatePrefab, quarterTurns);
        if (!candidatePrefab.TryGetInputDirection(candidateRotation, out Vector2Int inputDirection)
            || !candidatePrefab.TryGetOutputDirection(candidateRotation, out Vector2Int outputDirection))
        {
            return false;
        }

        inputConnected = TryGetConveyorPlacementDirectionsAtCoordinate(
                             anchorCoordinate + inputDirection,
                             previewToIgnore,
                             out _,
                             out _,
                             out Vector2Int neighborOutputDirection)
                         && neighborOutputDirection == -inputDirection;

        outputConnected = TryGetConveyorPlacementDirectionsAtCoordinate(
                              anchorCoordinate + outputDirection,
                              previewToIgnore,
                              out _,
                              out Vector2Int neighborInputDirection,
                              out _)
                          && neighborInputDirection == -outputDirection;

        return inputConnected || outputConnected;
    }

    private bool TryGetInstalledConveyorPlacementDirectionsAtCoordinate(
        Vector2Int coordinate,
        out ConveyorBelt conveyorBelt,
        out Vector2Int inputDirection,
        out Vector2Int outputDirection,
        out long placementSequence)
    {
        conveyorBelt = null;
        inputDirection = Vector2Int.zero;
        outputDirection = Vector2Int.zero;
        placementSequence = 0;

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null)
        {
            return false;
        }

        if (terrain.TryGetLoadedBlock(coordinate, out Block block)
            && block != null
            && block.MapObject is ConveyorBelt liveConveyor
            && liveConveyor != null
            && liveConveyor.gameObject.activeInHierarchy)
        {
            conveyorBelt = liveConveyor;
            placementSequence = liveConveyor.RuntimePlacementSequence;
            return liveConveyor.TryGetInputDirection(liveConveyor.transform.rotation, out inputDirection)
                && liveConveyor.TryGetOutputDirection(liveConveyor.transform.rotation, out outputDirection);
        }

        if (!terrain.TryGetInstallationStateAtCoordinate(coordinate, out BlockStateStore.InstallationSaveState savedState)
            || savedState == null
            || !TryGetInstallationDefinition(savedState.itemId, out ItemDefinition savedDefinition)
            || !(savedDefinition.mapObject is ConveyorBelt))
        {
            return false;
        }

        MapObject savedSourcePrefab = null;
        if (savedDefinition.mapObject is ConveyorBelt savedPrototype && savedState.conveyorVariantKind >= 0)
        {
            savedSourcePrefab = ResolveConveyorVariantPrefab(savedPrototype, savedState.conveyorVariantKind);
        }

        if (savedSourcePrefab == null)
        {
            savedSourcePrefab = ResolveInstalledObjectSourcePrefab(
                savedDefinition,
                savedState.anchorCoordinate,
                savedState.quarterTurns,
                null,
                ConveyorPreviewVariantMode.Corner);
        }

        if (!(savedSourcePrefab is ConveyorBelt savedConveyor))
        {
            return false;
        }

        conveyorBelt = savedConveyor;
        placementSequence = savedState.placementSequence;
        Quaternion savedRotation = GetPlacementObjectRotation(savedConveyor, savedState.quarterTurns);
        return savedConveyor.TryGetInputDirection(savedRotation, out inputDirection)
            && savedConveyor.TryGetOutputDirection(savedRotation, out outputDirection);
    }

    private bool TryResolveInitialConveyorPreviewQuarterTurns(
        Vector2Int anchorCoordinate,
        ConveyorBelt conveyorPrototype,
        out int resolvedQuarterTurns)
    {
        resolvedQuarterTurns = 0;
        if (conveyorPrototype == null)
        {
            return false;
        }

        ConveyorBelt straightPrefab = conveyorPrototype.StraightVariantPrefab != null
            ? conveyorPrototype.StraightVariantPrefab
            : conveyorPrototype;
        if (TryResolveConveyorEndpointAlignedQuarterTurnsForSource(
                straightPrefab,
                anchorCoordinate,
                null,
                resolvedQuarterTurns,
                out resolvedQuarterTurns,
                out _))
        {
            return true;
        }

        if (!TryGetLatestAdjacentConveyorOutputDirection(anchorCoordinate, out Vector2Int desiredOutputDirection))
        {
            return false;
        }

        return TryGetConveyorPlacementQuarterTurnsForOutput(straightPrefab, desiredOutputDirection, out resolvedQuarterTurns);
    }

    private bool TryGetLatestAdjacentConveyorOutputDirection(Vector2Int anchorCoordinate, out Vector2Int desiredOutputDirection)
    {
        desiredOutputDirection = Vector2Int.zero;
        bool found = false;
        long bestPlacementSequence = long.MinValue;
        Vector2Int[] directions =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int sideDirection = directions[i];
                if (!TryGetConveyorPlacementSequenceAndOutputDirectionAtCoordinate(
                        anchorCoordinate + sideDirection,
                        out Vector2Int neighborOutputDirection,
                        out long placementSequence))
                {
                    continue;
                }

                bool matchesIncomingConnection = neighborOutputDirection == -sideDirection;
                if ((pass == 0 && !matchesIncomingConnection) || (pass == 1 && matchesIncomingConnection))
                {
                    continue;
                }

                if (found && placementSequence <= bestPlacementSequence)
                {
                    continue;
                }

                bestPlacementSequence = placementSequence;
                desiredOutputDirection = neighborOutputDirection;
                found = true;
            }

            if (found)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetConveyorPlacementSequenceAndOutputDirectionAtCoordinate(
        Vector2Int coordinate,
        out Vector2Int outputDirection,
        out long placementSequence)
    {
        outputDirection = Vector2Int.zero;
        placementSequence = 0;

        if (TryGetInstallPreviewAtCoordinate(coordinate, out MapObject preview)
            && preview != null
            && preview is ConveyorBelt previewConveyor)
        {
            placementSequence = installPreviewPlacementSequencesByPreview.TryGetValue(preview, out long previewSequence)
                ? previewSequence
                : 0;
            return previewConveyor.TryGetOutputDirection(previewConveyor.transform.rotation, out outputDirection);
        }

        return TryGetInstalledConveyorPlacementDirectionsAtCoordinate(
            coordinate,
            out _,
            out _,
            out outputDirection,
            out placementSequence);
    }

    private bool TryGetInstalledConveyorEndpointConnections(
        ConveyorBelt conveyorBelt,
        Vector2Int anchorCoordinate,
        MapObject previewToIgnore,
        out bool inputConnected,
        out bool outputConnected)
    {
        inputConnected = false;
        outputConnected = false;
        if (conveyorBelt == null)
        {
            return false;
        }

        if (!conveyorBelt.TryGetInputDirection(conveyorBelt.transform.rotation, out Vector2Int inputDirection)
            || !conveyorBelt.TryGetOutputDirection(conveyorBelt.transform.rotation, out Vector2Int outputDirection))
        {
            return false;
        }

        inputConnected = TryGetInstalledConveyorPlacementDirectionsAtCoordinate(
                             anchorCoordinate + inputDirection,
                             out _,
                             out _,
                             out Vector2Int neighborOutputDirection,
                             out _)
                         && neighborOutputDirection == -inputDirection;

        if (!inputConnected)
        {
            inputConnected = TryGetConveyorPlacementDirectionsAtCoordinate(
                                 anchorCoordinate + inputDirection,
                                 previewToIgnore,
                                 out _,
                                 out _,
                                 out neighborOutputDirection)
                             && neighborOutputDirection == -inputDirection;
        }

        outputConnected = TryGetInstalledConveyorPlacementDirectionsAtCoordinate(
                              anchorCoordinate + outputDirection,
                              out _,
                              out Vector2Int neighborInputDirection,
                              out _,
                              out _)
                          && neighborInputDirection == -outputDirection;

        if (!outputConnected)
        {
            outputConnected = TryGetConveyorPlacementDirectionsAtCoordinate(
                                  anchorCoordinate + outputDirection,
                                  previewToIgnore,
                                  out _,
                                  out neighborInputDirection,
                                  out _)
                              && neighborInputDirection == -outputDirection;
        }

        return inputConnected || outputConnected;
    }

    private static ConveyorChangeInfo FindConveyorChangeAtCoordinate(
        IReadOnlyList<ConveyorChangeInfo> changes,
        Vector2Int coordinate)
    {
        if (changes == null)
        {
            return null;
        }

        for (int i = 0; i < changes.Count; i++)
        {
            ConveyorChangeInfo change = changes[i];
            if (change == null)
            {
                continue;
            }

            if (change.coordinate == coordinate)
            {
                return change;
            }

            if (change.occupiedCoordinates == null)
            {
                continue;
            }

            for (int occupiedIndex = 0; occupiedIndex < change.occupiedCoordinates.Count; occupiedIndex++)
            {
                if (change.occupiedCoordinates[occupiedIndex] == coordinate)
                {
                    return change;
                }
            }
        }

        return null;
    }

    private static List<ConveyorChangeInfo> BuildCoordinateOnlyConveyorChanges(IReadOnlyList<Vector2Int> coordinates)
    {
        List<ConveyorChangeInfo> changes = new List<ConveyorChangeInfo>();
        if (coordinates == null)
        {
            return changes;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            changes.Add(new ConveyorChangeInfo
            {
                coordinate = coordinates[i],
                occupiedCoordinates = new List<Vector2Int> { coordinates[i] },
                inputDirection = Vector2Int.zero,
                outputDirection = Vector2Int.zero,
                rotation = Quaternion.identity,
                isCornerVariant = false
            });
        }

        return changes;
    }

    private bool TryCreateConveyorChange(
        Vector2Int anchorCoordinate,
        IReadOnlyList<Vector2Int> occupiedCoordinates,
        ConveyorBelt conveyor,
        Quaternion rotation,
        out ConveyorChangeInfo changeInfo)
    {
        changeInfo = null;
        if (conveyor == null
            || !conveyor.TryGetInputDirection(rotation, out Vector2Int inputDirection)
            || !conveyor.TryGetOutputDirection(rotation, out Vector2Int outputDirection))
        {
            return false;
        }

        changeInfo = new ConveyorChangeInfo
        {
            coordinate = anchorCoordinate,
            occupiedCoordinates = occupiedCoordinates != null ? new List<Vector2Int>(occupiedCoordinates) : new List<Vector2Int> { anchorCoordinate },
            inputDirection = inputDirection,
            outputDirection = outputDirection,
            rotation = rotation,
            isCornerVariant = conveyor.IsCornerVariant
        };
        return true;
    }

    private bool TryCreateOriginalEditConveyorChange(
        InstallationEditSession editSession,
        out ConveyorChangeInfo changeInfo)
    {
        changeInfo = null;
        if (editSession == null
            || !(editSession.definition?.mapObject is ConveyorBelt conveyorPrototype))
        {
            return false;
        }

        MapObject sourcePrefab = ResolveConveyorVariantPrefab(conveyorPrototype, editSession.originalConveyorVariantKind)
            ?? editSession.definition.mapObject;
        return TryCreateConveyorChange(
            editSession.originalAnchorCoordinate,
            editSession.originalOccupiedCoordinates,
            sourcePrefab as ConveyorBelt,
            editSession.originalRotation,
            out changeInfo);
    }

    private void RefreshConveyorVariantsAroundActivePreview(
        Vector2Int? previousAnchorCoordinate = null,
        ConveyorChangeInfo previousChange = null)
    {
        if (!(activeInstallPreview is ConveyorBelt activePreviewConveyor))
        {
            return;
        }

        List<ConveyorChangeInfo> changes = new List<ConveyorChangeInfo>(2);
        if (previousChange != null)
        {
            changes.Add(previousChange);
        }

        List<Vector2Int> coordinates = new List<Vector2Int>(2);
        if (previousAnchorCoordinate.HasValue)
        {
            coordinates.Add(previousAnchorCoordinate.Value);
        }

        if (TryGetPreviewAnchorCoordinate(activeInstallPreview, out Vector2Int currentAnchorCoordinate))
        {
            if (!coordinates.Contains(currentAnchorCoordinate))
            {
                coordinates.Add(currentAnchorCoordinate);
            }

            List<Vector2Int> currentOccupiedCoordinates = GetFootprintCoordinates(
                currentAnchorCoordinate,
                activeInstallPreview,
                GetPreviewQuarterTurns(activeInstallPreview));
            if (TryCreateConveyorChange(
                    currentAnchorCoordinate,
                    currentOccupiedCoordinates,
                    activePreviewConveyor,
                    activeInstallPreview.transform.rotation,
                    out ConveyorChangeInfo currentPreviewChange))
            {
                changes.Add(currentPreviewChange);
            }
        }

        if (changes.Count > 0)
        {
            NormalizeDisconnectedConveyorCornersAroundChanges(
                changes,
                false,
                null);
            return;
        }

        NormalizeDisconnectedConveyorCornersAroundCoordinates(coordinates, false, null);
    }

    private void NormalizeDisconnectedConveyorCornersAroundCoordinates(
        IReadOnlyList<Vector2Int> coordinates,
        bool includeSelf = true,
        MapObject previewToIgnore = null,
        IReadOnlyCollection<Vector2Int> protectedAnchorCoordinates = null)
    {
        NormalizeDisconnectedConveyorCornersAroundChanges(
            BuildCoordinateOnlyConveyorChanges(coordinates),
            includeSelf,
            previewToIgnore,
            protectedAnchorCoordinates);
    }

    private void NormalizeDisconnectedConveyorCornersAroundChanges(
        IReadOnlyList<ConveyorChangeInfo> changes,
        bool includeSelf = true,
        MapObject previewToIgnore = null,
        IReadOnlyCollection<Vector2Int> protectedAnchorCoordinates = null)
    {
        if (changes == null || changes.Count <= 0)
        {
            return;
        }

        HashSet<Vector2Int> candidateCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < changes.Count; i++)
        {
            ConveyorChangeInfo change = changes[i];
            if (change == null)
            {
                continue;
            }

            IReadOnlyList<Vector2Int> sourceCoordinates = change.occupiedCoordinates != null
                && change.occupiedCoordinates.Count > 0
                    ? change.occupiedCoordinates
                    : new List<Vector2Int> { change.coordinate };

            for (int sourceIndex = 0; sourceIndex < sourceCoordinates.Count; sourceIndex++)
            {
                Vector2Int sourceCoordinate = sourceCoordinates[sourceIndex];
                if (includeSelf)
                {
                    candidateCoordinates.Add(sourceCoordinate);
                }

                candidateCoordinates.Add(sourceCoordinate + Vector2Int.up);
                candidateCoordinates.Add(sourceCoordinate + Vector2Int.right);
                candidateCoordinates.Add(sourceCoordinate + Vector2Int.down);
                candidateCoordinates.Add(sourceCoordinate + Vector2Int.left);
            }
        }

        if (candidateCoordinates.Count <= 0)
        {
            return;
        }

        List<Vector2Int> candidateList = new List<Vector2Int>(candidateCoordinates);
        int maxPassCount = Mathf.Max(1, candidateList.Count);
        for (int pass = 0; pass < maxPassCount; pass++)
        {
            bool anyChanged = false;
            for (int i = 0; i < candidateList.Count; i++)
            {
                if (TryNormalizeDisconnectedConveyorCornerAtCoordinate(
                        candidateList[i],
                        changes,
                        previewToIgnore,
                        protectedAnchorCoordinates))
                {
                    anyChanged = true;
                }
            }

            if (!anyChanged)
            {
                break;
            }
        }
    }

    private bool TryNormalizeDisconnectedConveyorCornerAtCoordinate(
        Vector2Int coordinate,
        IReadOnlyList<ConveyorChangeInfo> changedConveyors,
        MapObject previewToIgnore,
        IReadOnlyCollection<Vector2Int> protectedAnchorCoordinates)
    {
        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is ConveyorBelt installedConveyor)
            || installedConveyor == null
            || !installedConveyor.IsCornerVariant
            || !installedConveyor.gameObject.activeInHierarchy
            || !installedConveyor.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            return false;
        }

        if (anchorCoordinate != coordinate)
        {
            return false;
        }

        if (protectedAnchorCoordinates != null)
        {
            foreach (Vector2Int protectedAnchorCoordinate in protectedAnchorCoordinates)
            {
                if (protectedAnchorCoordinate == anchorCoordinate)
                {
                    return false;
                }
            }
        }

        if (!installedConveyor.TryGetInputDirection(installedConveyor.transform.rotation, out Vector2Int inputDirection)
            || !installedConveyor.TryGetOutputDirection(installedConveyor.transform.rotation, out Vector2Int outputDirection))
        {
            return false;
        }

        if (!TryGetInstalledConveyorEndpointConnections(
                installedConveyor,
                anchorCoordinate,
                previewToIgnore,
                out bool inputConnected,
                out bool outputConnected))
        {
            inputConnected = false;
            outputConnected = false;
        }

        // Only straighten truly orphaned corners; a one-sided connection still means the corner is intentional.
        if (inputConnected || outputConnected)
        {
            return false;
        }

        ConveyorChangeInfo changedInputSide = FindConveyorChangeAtCoordinate(changedConveyors, anchorCoordinate + inputDirection);
        ConveyorChangeInfo changedOutputSide = FindConveyorChangeAtCoordinate(changedConveyors, anchorCoordinate + outputDirection);
        if (changedInputSide != null || changedOutputSide != null)
        {
            // Removing an adjacent belt should not reinterpret the corner into the opposite side.
            return false;
        }

        ConveyorBelt straightPrefab = installedConveyor.StraightVariantPrefab != null
            ? installedConveyor.StraightVariantPrefab
            : installedConveyor;
        if (straightPrefab == null)
        {
            return false;
        }

        installedConveyor.RefreshInstalledDirectionFromCurrentTransform();
        if (!TryGetInstallationFacingVector(installedConveyor.InstalledDirection, out Vector2Int desiredInputDirection)
            || !TryGetConveyorPlacementQuarterTurnsForInput(
                straightPrefab,
                desiredInputDirection,
                out int straightQuarterTurns))
        {
            return false;
        }

        MapObject desiredPrefab = straightPrefab;
        int desiredQuarterTurns = straightQuarterTurns;

        int currentQuarterTurns = installedConveyor.RuntimeQuarterTurns;
        Quaternion desiredRotation = GetInstalledObjectRotation(desiredPrefab, desiredQuarterTurns);
        bool sameVariant = !RequiresConveyorPreviewReplacement(installedConveyor, desiredPrefab);
        bool sameQuarterTurns = ((currentQuarterTurns % 4) + 4) % 4 == ((desiredQuarterTurns % 4) + 4) % 4;
        bool sameRotation = Mathf.Abs(Quaternion.Dot(installedConveyor.transform.rotation, desiredRotation)) >= 0.9999f;
        if (sameVariant && sameQuarterTurns && sameRotation)
        {
            return false;
        }

        Transform installParent = terrain.transform;
        if (!(desiredPrefab is ConveyorBelt replacementPrefab))
        {
            return false;
        }

        MapObject replacementObject = CreateInstalledObjectInstance(replacementPrefab, installParent, terrain);
        if (!(replacementObject is ConveyorBelt replacementConveyor))
        {
            if (replacementObject is InstallationObject replacementInstallation)
            {
                ReleaseInstalledObjectInstance(replacementInstallation, replacementPrefab, terrain);
            }

            return false;
        }

        replacementConveyor.transform.SetPositionAndRotation(
            GetInstalledObjectWorldPosition(anchorCoordinate, replacementPrefab, desiredQuarterTurns, 0f),
            desiredRotation);
        replacementConveyor.ApplyItemFilterMask(
            installedConveyor.CaptureItemFilterMaskWords(),
            installedConveyor.IsItemFilterMaskInitialized);

        if (terrain.TryGetLoadedBlock(anchorCoordinate, out Block anchorBlock) && anchorBlock != null)
        {
            anchorBlock.SetMapObject(replacementConveyor);
        }

        ConfigureInstalledObjectRuntime(replacementConveyor, anchorCoordinate, desiredQuarterTurns);
        RegisterInstalledObjectPersistence(replacementConveyor);

        TryGetInstallationDefinition(installedConveyor.ResolveItemId(), out ItemDefinition installedDefinition);
        ReleaseInstalledObjectInstance(
            installedConveyor,
            ResolveInstallationSourcePrefab(
                installedDefinition,
                GetConveyorVariantKind(installedConveyor)),
            terrain);

        return true;
    }

    private bool TryGetConveyorPlacementOutputDirection(ConveyorBelt conveyorPrototype, int quarterTurns, out Vector2Int outputDirection)
    {
        outputDirection = Vector2Int.zero;
        if (conveyorPrototype == null)
        {
            return false;
        }

        return conveyorPrototype.TryGetOutputDirection(
            GetPlacementObjectRotation(conveyorPrototype, quarterTurns),
            out outputDirection);
    }

    private bool TryGetConveyorPlacementQuarterTurnsForOutput(MapObject sourcePrefab, Vector2Int desiredOutputDirection, out int resolvedQuarterTurns)
    {
        resolvedQuarterTurns = 0;
        if (!(sourcePrefab is ConveyorBelt conveyorSource) || desiredOutputDirection == Vector2Int.zero)
        {
            return false;
        }

        for (int candidateQuarterTurns = 0; candidateQuarterTurns < 4; candidateQuarterTurns++)
        {
            if (!conveyorSource.TryGetOutputDirection(
                    GetPlacementObjectRotation(conveyorSource, candidateQuarterTurns),
                    out Vector2Int candidateOutputDirection))
            {
                continue;
            }

            if (candidateOutputDirection != desiredOutputDirection)
            {
                continue;
            }

            resolvedQuarterTurns = candidateQuarterTurns;
            return true;
        }

        return false;
    }

    private bool TryGetConveyorPlacementQuarterTurnsForRotation(
        MapObject sourcePrefab,
        Quaternion desiredRotation,
        out int resolvedQuarterTurns)
    {
        resolvedQuarterTurns = 0;
        if (!(sourcePrefab is ConveyorBelt conveyorSource))
        {
            return false;
        }

        for (int candidateQuarterTurns = 0; candidateQuarterTurns < 4; candidateQuarterTurns++)
        {
            Quaternion candidateRotation = GetPlacementObjectRotation(conveyorSource, candidateQuarterTurns);
            if (Mathf.Abs(Quaternion.Dot(candidateRotation, desiredRotation)) < 0.9999f)
            {
                continue;
            }

            resolvedQuarterTurns = candidateQuarterTurns;
            return true;
        }

        return false;
    }

    private bool TryGetConveyorPlacementQuarterTurnsForInput(MapObject sourcePrefab, Vector2Int desiredInputDirection, out int resolvedQuarterTurns)
    {
        resolvedQuarterTurns = 0;
        if (!(sourcePrefab is ConveyorBelt conveyorSource) || desiredInputDirection == Vector2Int.zero)
        {
            return false;
        }

        for (int candidateQuarterTurns = 0; candidateQuarterTurns < 4; candidateQuarterTurns++)
        {
            if (!conveyorSource.TryGetInputDirection(
                    GetPlacementObjectRotation(conveyorSource, candidateQuarterTurns),
                    out Vector2Int candidateInputDirection))
            {
                continue;
            }

            if (candidateInputDirection != desiredInputDirection)
            {
                continue;
            }

            resolvedQuarterTurns = candidateQuarterTurns;
            return true;
        }

        return false;
    }

    private bool TryResolveConveyorEndpointAlignedQuarterTurns(
        Vector2Int anchorCoordinate,
        MapObject previewToIgnore,
        int preferredQuarterTurns,
        out int resolvedQuarterTurns)
    {
        resolvedQuarterTurns = NormalizePlacementQuarterTurns(preferredQuarterTurns);
        if (activeInstallDefinition == null || !(activeInstallDefinition.mapObject is ConveyorBelt conveyorPrototype))
        {
            return false;
        }

        ConveyorPreviewVariantMode previewVariantMode = previewToIgnore == activeInstallPreview
            ? installPreviewConveyorVariantMode
            : GetConveyorPreviewVariantMode(previewToIgnore);
        List<MapObject> candidateSources = new List<MapObject>(3);
        if (previewVariantMode == ConveyorPreviewVariantMode.Straight)
        {
            AddUniqueConveyorCandidateSource(candidateSources, conveyorPrototype.StraightVariantPrefab);
        }
        else
        {
            AddUniqueConveyorCandidateSource(candidateSources, conveyorPrototype.CornerVariantPrefab);
            AddUniqueConveyorCandidateSource(candidateSources, conveyorPrototype.ReverseCornerVariantPrefab);
            AddUniqueConveyorCandidateSource(candidateSources, conveyorPrototype.StraightVariantPrefab);
        }

        bool found = false;
        int bestScore = 0;
        int bestOffset = int.MaxValue;
        for (int i = 0; i < candidateSources.Count; i++)
        {
            if (!TryResolveConveyorEndpointAlignedQuarterTurnsForSource(
                    candidateSources[i],
                    anchorCoordinate,
                    previewToIgnore,
                    preferredQuarterTurns,
                    out int candidateQuarterTurns,
                    out int candidateScore))
            {
                continue;
            }

            int candidateOffset = GetQuarterTurnSearchOffset(preferredQuarterTurns, candidateQuarterTurns);
            if (found
                && (candidateScore < bestScore
                    || (candidateScore == bestScore && candidateOffset >= bestOffset)))
            {
                continue;
            }

            found = true;
            bestScore = candidateScore;
            bestOffset = candidateOffset;
            resolvedQuarterTurns = candidateQuarterTurns;
        }

        return found;
    }

    private bool TryResolveConveyorEndpointAlignedQuarterTurnsForSource(
        MapObject sourcePrefab,
        Vector2Int anchorCoordinate,
        MapObject previewToIgnore,
        int preferredQuarterTurns,
        out int resolvedQuarterTurns,
        out int resolvedScore)
    {
        resolvedQuarterTurns = NormalizePlacementQuarterTurns(preferredQuarterTurns);
        resolvedScore = 0;
        if (!(sourcePrefab is ConveyorBelt conveyorSource))
        {
            return false;
        }

        int normalizedPreferredQuarterTurns = NormalizePlacementQuarterTurns(preferredQuarterTurns);
        for (int offset = 0; offset < 4; offset++)
        {
            int candidateQuarterTurns = (normalizedPreferredQuarterTurns + offset) % 4;
            Quaternion candidateRotation = GetPlacementObjectRotation(conveyorSource, candidateQuarterTurns);
            if (!conveyorSource.TryGetInputDirection(candidateRotation, out Vector2Int inputDirection)
                || !conveyorSource.TryGetOutputDirection(candidateRotation, out Vector2Int outputDirection))
            {
                continue;
            }

            int candidateScore = GetConveyorEndpointConnectionScore(
                anchorCoordinate,
                inputDirection,
                outputDirection,
                previewToIgnore);
            if (candidateScore <= 0)
            {
                continue;
            }

            if (resolvedScore > 0 && candidateScore <= resolvedScore)
            {
                continue;
            }

            resolvedQuarterTurns = candidateQuarterTurns;
            resolvedScore = candidateScore;
            if (candidateScore >= 2)
            {
                return true;
            }
        }

        return resolvedScore > 0;
    }

    private int GetConveyorEndpointConnectionScore(
        Vector2Int anchorCoordinate,
        Vector2Int inputDirection,
        Vector2Int outputDirection,
        MapObject previewToIgnore)
    {
        int score = 0;
        if (inputDirection != Vector2Int.zero
            && TryGetConveyorPlacementDirectionsAtCoordinate(
                anchorCoordinate + inputDirection,
                previewToIgnore,
                out _,
                out _,
                out Vector2Int neighborOutputDirection)
            && neighborOutputDirection == -inputDirection)
        {
            score++;
        }

        if (outputDirection != Vector2Int.zero
            && TryGetConveyorPlacementDirectionsAtCoordinate(
                anchorCoordinate + outputDirection,
                previewToIgnore,
                out _,
                out Vector2Int neighborInputDirection,
                out _)
            && neighborInputDirection == -outputDirection)
        {
            score++;
        }

        return score;
    }

    private static void AddUniqueConveyorCandidateSource(List<MapObject> candidateSources, MapObject candidateSource)
    {
        if (candidateSources == null || candidateSource == null)
        {
            return;
        }

        for (int i = 0; i < candidateSources.Count; i++)
        {
            if (candidateSources[i] == candidateSource)
            {
                return;
            }
        }

        candidateSources.Add(candidateSource);
    }

    private static int GetQuarterTurnSearchOffset(int preferredQuarterTurns, int candidateQuarterTurns)
    {
        int normalizedPreferredQuarterTurns = NormalizePlacementQuarterTurns(preferredQuarterTurns);
        int normalizedCandidateQuarterTurns = NormalizePlacementQuarterTurns(candidateQuarterTurns);
        return (normalizedCandidateQuarterTurns - normalizedPreferredQuarterTurns + 4) % 4;
    }

    private static bool TryGetInstallationFacingVector(InstallationFacingDirection facingDirection, out Vector2Int direction)
    {
        direction = facingDirection switch
        {
            InstallationFacingDirection.PositiveX => Vector2Int.right,
            InstallationFacingDirection.NegativeX => Vector2Int.left,
            InstallationFacingDirection.NegativeZ => Vector2Int.down,
            _ => Vector2Int.up
        };

        return direction != Vector2Int.zero;
    }

    private bool TryGetConveyorPlacementInfoAtCoordinate(
        Vector2Int coordinate,
        MapObject previewToIgnore,
        out ConveyorBelt conveyorBelt,
        out Vector2Int outputDirection)
    {
        return TryGetConveyorPlacementDirectionsAtCoordinate(
            coordinate,
            previewToIgnore,
            out conveyorBelt,
            out _,
            out outputDirection);
    }

    private bool TryGetConveyorPlacementDirectionsAtCoordinate(
        Vector2Int coordinate,
        MapObject previewToIgnore,
        out ConveyorBelt conveyorBelt,
        out Vector2Int inputDirection,
        out Vector2Int outputDirection)
    {
        conveyorBelt = null;
        inputDirection = Vector2Int.zero;
        outputDirection = Vector2Int.zero;

        if (TryGetInstallPreviewAtCoordinate(coordinate, out MapObject preview)
            && preview != null
            && preview != previewToIgnore
            && preview is ConveyorBelt previewConveyor)
        {
            conveyorBelt = previewConveyor;
            return previewConveyor.TryGetInputDirection(previewConveyor.transform.rotation, out inputDirection)
                && previewConveyor.TryGetOutputDirection(previewConveyor.transform.rotation, out outputDirection);
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null)
        {
            return false;
        }

        if (terrain.TryGetLoadedBlock(coordinate, out Block block)
            && block != null
            && block.MapObject is ConveyorBelt liveConveyor
            && liveConveyor != null
            && liveConveyor.gameObject.activeInHierarchy)
        {
            conveyorBelt = liveConveyor;
            return liveConveyor.TryGetInputDirection(liveConveyor.transform.rotation, out inputDirection)
                && liveConveyor.TryGetOutputDirection(liveConveyor.transform.rotation, out outputDirection);
        }

        if (!terrain.TryGetInstallationStateAtCoordinate(coordinate, out BlockStateStore.InstallationSaveState savedState)
            || savedState == null
            || !TryGetInstallationDefinition(savedState.itemId, out ItemDefinition savedDefinition)
            || !(savedDefinition.mapObject is ConveyorBelt))
        {
            return false;
        }

        MapObject savedSourcePrefab = null;
        if (savedDefinition.mapObject is ConveyorBelt savedPrototype && savedState.conveyorVariantKind >= 0)
        {
            savedSourcePrefab = ResolveConveyorVariantPrefab(savedPrototype, savedState.conveyorVariantKind);
        }

        if (savedSourcePrefab == null)
        {
            savedSourcePrefab = ResolveInstalledObjectSourcePrefab(
                savedDefinition,
                savedState.anchorCoordinate,
                savedState.quarterTurns,
                null,
                ConveyorPreviewVariantMode.Corner);
        }
        if (!(savedSourcePrefab is ConveyorBelt savedConveyor))
        {
            return false;
        }

        conveyorBelt = savedConveyor;
        Quaternion savedRotation = GetPlacementObjectRotation(savedConveyor, savedState.quarterTurns);
        return savedConveyor.TryGetInputDirection(savedRotation, out inputDirection)
            && savedConveyor.TryGetOutputDirection(savedRotation, out outputDirection);
    }

    private bool HasAdjacentConveyorAtCoordinate(Vector2Int anchorCoordinate, MapObject previewToIgnore)
    {
        Vector2Int[] directions =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        for (int i = 0; i < directions.Length; i++)
        {
            if (TryGetConveyorPlacementInfoAtCoordinate(anchorCoordinate + directions[i], previewToIgnore, out _, out _))
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshConveyorPreviewVariants(
        IReadOnlyList<ConveyorChangeInfo> changedConveyors = null,
        MapObject previewToIgnore = null)
    {
        if (activeInstallDefinition == null || !(activeInstallDefinition.mapObject is ConveyorBelt))
        {
            return;
        }

        CleanupInstallPreviewReferences();
        if (installPreviewInstances.Count <= 0)
        {
            return;
        }

        HashSet<Vector2Int> affectedPreviewCoordinates = BuildConveyorVariantRefreshCoordinates(changedConveyors, true);
        List<MapObject> previews = new List<MapObject>(installPreviewInstances);
        for (int i = 0; i < previews.Count; i++)
        {
            MapObject preview = previews[i];
            if (preview == null || !TryGetPreviewAnchorCoordinate(preview, out Vector2Int anchorCoordinate))
            {
                continue;
            }

            if (affectedPreviewCoordinates != null && !affectedPreviewCoordinates.Contains(anchorCoordinate))
            {
                continue;
            }

            RefreshSingleConveyorPreviewVariant(preview, anchorCoordinate, changedConveyors, previewToIgnore);
        }
    }

    private static HashSet<Vector2Int> BuildConveyorVariantRefreshCoordinates(
        IReadOnlyList<ConveyorChangeInfo> changedConveyors,
        bool includeSelf)
    {
        if (changedConveyors == null || changedConveyors.Count <= 0)
        {
            return null;
        }

        HashSet<Vector2Int> coordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < changedConveyors.Count; i++)
        {
            ConveyorChangeInfo change = changedConveyors[i];
            if (change == null)
            {
                continue;
            }

            IReadOnlyList<Vector2Int> sourceCoordinates = change.occupiedCoordinates != null
                && change.occupiedCoordinates.Count > 0
                    ? change.occupiedCoordinates
                    : new List<Vector2Int> { change.coordinate };

            for (int sourceIndex = 0; sourceIndex < sourceCoordinates.Count; sourceIndex++)
            {
                Vector2Int sourceCoordinate = sourceCoordinates[sourceIndex];
                if (includeSelf)
                {
                    coordinates.Add(sourceCoordinate);
                }

                coordinates.Add(sourceCoordinate + Vector2Int.up);
                coordinates.Add(sourceCoordinate + Vector2Int.right);
                coordinates.Add(sourceCoordinate + Vector2Int.down);
                coordinates.Add(sourceCoordinate + Vector2Int.left);
            }
        }

        return coordinates.Count > 0 ? coordinates : null;
    }

    private void RefreshActiveConveyorPreviewVariant(
        IReadOnlyList<ConveyorChangeInfo> changedConveyors = null,
        MapObject previewToIgnore = null)
    {
        if (activeInstallDefinition == null || !(activeInstallDefinition.mapObject is ConveyorBelt))
        {
            preferDifferentConveyorCornerOnNextRefresh = false;
            return;
        }

        if (activeInstallPreview == null || !TryGetPreviewAnchorCoordinate(activeInstallPreview, out Vector2Int anchorCoordinate))
        {
            preferDifferentConveyorCornerOnNextRefresh = false;
            return;
        }

        RefreshSingleConveyorPreviewVariant(activeInstallPreview, anchorCoordinate, changedConveyors, previewToIgnore);
    }

    private void RefreshSingleConveyorPreviewVariant(
        MapObject preview,
        Vector2Int anchorCoordinate,
        IReadOnlyList<ConveyorChangeInfo> changedConveyors = null,
        MapObject previewToIgnore = null)
    {
        if (preview == null || activeInstallDefinition == null)
        {
            return;
        }

        int quarterTurns = GetPreviewQuarterTurns(preview);
        MapObject explicitSourcePrefab = null;
        bool hasExplicitConveyorVariant = activeInstallDefinition.mapObject is ConveyorBelt
            && installPreviewSourcePrefabsByPreview.TryGetValue(preview, out explicitSourcePrefab)
            && explicitSourcePrefab is ConveyorBelt;
        ConveyorPreviewVariantMode previewVariantMode = hasExplicitConveyorVariant
            ? GetConveyorPreviewVariantMode(explicitSourcePrefab)
            : preview == activeInstallPreview
                ? installPreviewConveyorVariantMode
                : GetConveyorPreviewVariantMode(preview);
        bool preferDifferentCurrentCorner = !hasExplicitConveyorVariant
            && preview == activeInstallPreview
            && preferDifferentConveyorCornerOnNextRefresh;
        MapObject desiredPrefab = hasExplicitConveyorVariant
            ? explicitSourcePrefab
            : ResolveInstalledObjectSourcePrefab(
                activeInstallDefinition,
                anchorCoordinate,
                quarterTurns,
                preview,
                previewVariantMode);
        int resolvedQuarterTurns = quarterTurns;
        if (!hasExplicitConveyorVariant
            && activeInstallDefinition.mapObject is ConveyorBelt conveyorPrototype
            && previewVariantMode == ConveyorPreviewVariantMode.Corner
            && TryResolveConveyorCornerPlacementPrefab(
                conveyorPrototype,
                anchorCoordinate,
                quarterTurns,
                preview,
                out MapObject resolvedCornerPrefab,
                out int resolvedCornerQuarterTurns,
                preferDifferentCurrentCorner))
        {
            desiredPrefab = resolvedCornerPrefab;
            resolvedQuarterTurns = resolvedCornerQuarterTurns;
        }
        else if (activeInstallDefinition.mapObject is ConveyorBelt straightPrototype
                 && preview is ConveyorBelt previewConveyor
                 && previewConveyor.IsCornerVariant
                 && desiredPrefab is ConveyorBelt desiredConveyor
                 && !desiredConveyor.IsCornerVariant
                 && TryResolveDisconnectedConveyorStraightQuarterTurnsForPreview(
                     preview,
                     previewConveyor,
                     straightPrototype,
                     anchorCoordinate,
                     changedConveyors,
                     previewToIgnore,
                     out int resolvedStraightQuarterTurns))
        {
            resolvedQuarterTurns = resolvedStraightQuarterTurns;
        }

        if (desiredPrefab == null
            || !RequiresConveyorPreviewReplacement(preview, desiredPrefab))
        {
            int normalizedResolvedQuarterTurns = NormalizePlacementQuarterTurns(resolvedQuarterTurns);
            if (desiredPrefab != null && NormalizePlacementQuarterTurns(quarterTurns) != normalizedResolvedQuarterTurns)
            {
                installPreviewQuarterTurnsByPreview[preview] = normalizedResolvedQuarterTurns;
                if (preview == activeInstallPreview)
                {
                    installPreviewQuarterTurns = normalizedResolvedQuarterTurns;
                }

                installPreviewSourcePrefabsByPreview[preview] = desiredPrefab;
            }

            if (preferDifferentCurrentCorner)
            {
                preferDifferentConveyorCornerOnNextRefresh = false;
            }

            return;
        }

        MapObject replacementPreview = Instantiate(desiredPrefab);
        if (replacementPreview == null)
        {
            if (preferDifferentCurrentCorner)
            {
                preferDifferentConveyorCornerOnNextRefresh = false;
            }

            return;
        }

        replacementPreview.name = $"{desiredPrefab.name}_Blueprint";
        ConfigureInstallPreview(replacementPreview);
        ReplaceInstallPreviewInstance(preview, replacementPreview, desiredPrefab, anchorCoordinate, resolvedQuarterTurns);
    }

    private static bool RequiresConveyorPreviewReplacement(MapObject currentPreview, MapObject desiredPrefab)
    {
        if (!(currentPreview is ConveyorBelt currentConveyor) || !(desiredPrefab is ConveyorBelt desiredConveyor))
        {
            return false;
        }

        return currentConveyor.IsCornerVariant != desiredConveyor.IsCornerVariant
               || currentConveyor.IsReverseCornerVariant != desiredConveyor.IsReverseCornerVariant;
    }

    private void ReplaceInstallPreviewInstance(MapObject currentPreview, MapObject replacementPreview, MapObject replacementSourcePrefab, Vector2Int anchorCoordinate, int quarterTurns)
    {
        if (currentPreview == null || replacementPreview == null)
        {
            return;
        }

        int previewIndex = installPreviewInstances.IndexOf(currentPreview);
        Vector3 currentPosition = currentPreview.transform.position;
        int resolvedQuarterTurns = quarterTurns;
        Quaternion replacementBaseRotation = replacementPreview.transform.rotation;
        bool wasActivePreview = activeInstallPreview == currentPreview;
        bool wasPointerOriginPreview = previewPointerOriginPreview == currentPreview;

        if (previewIndex >= 0)
        {
            installPreviewInstances[previewIndex] = replacementPreview;
        }
        else
        {
            installPreviewInstances.Add(replacementPreview);
        }

        installPreviewQuarterTurnsByPreview.Remove(currentPreview);
        installPreviewQuarterTurnsByPreview[replacementPreview] = resolvedQuarterTurns;
        installPreviewBaseRotationsByPreview.Remove(currentPreview);
        installPreviewBaseRotationsByPreview[replacementPreview] = replacementBaseRotation;
        if (installPreviewPlacementSequencesByPreview.TryGetValue(currentPreview, out long previewPlacementSequence))
        {
            installPreviewPlacementSequencesByPreview.Remove(currentPreview);
            installPreviewPlacementSequencesByPreview[replacementPreview] = previewPlacementSequence;
        }
        installPreviewSourcePrefabsByPreview.Remove(currentPreview);
        installPreviewSourcePrefabsByPreview[replacementPreview] = replacementSourcePrefab != null ? replacementSourcePrefab : replacementPreview;
        installPreviewAnchorCoordinates.Remove(currentPreview);
        installPreviewAnchorCoordinates[replacementPreview] = anchorCoordinate;
        if (installPreviewItemReservationsByPreview.TryGetValue(currentPreview, out InstallPreviewItemReservation reservation))
        {
            installPreviewItemReservationsByPreview.Remove(currentPreview);
            installPreviewItemReservationsByPreview[replacementPreview] = reservation;
        }

        replacementPreview.transform.SetPositionAndRotation(
            currentPosition,
            GetPlacementObjectRotation(replacementPreview, resolvedQuarterTurns));
        if (replacementPreview is InstallationObject replacementInstallation)
        {
            replacementInstallation.RefreshInstalledDirectionFromCurrentTransform();
        }

        if (wasActivePreview)
        {
            activeInstallPreview = replacementPreview;
            installPreviewQuarterTurns = resolvedQuarterTurns;
            installPreviewConveyorVariantMode = GetConveyorPreviewVariantMode(replacementPreview);
        }

        if (wasActivePreview)
        {
            preferDifferentConveyorCornerOnNextRefresh = false;
        }

        if (wasPointerOriginPreview)
        {
            previewPointerOriginPreview = replacementPreview;
        }

        ClearInputOutputMarkers(currentPreview);
        if (Application.isPlaying)
        {
            Destroy(currentPreview.gameObject);
        }
        else
        {
            DestroyImmediate(currentPreview.gameObject);
        }
    }

    private bool TryResolveDisconnectedConveyorStraightQuarterTurnsForPreview(
        MapObject preview,
        ConveyorBelt previewConveyor,
        ConveyorBelt conveyorPrototype,
        Vector2Int anchorCoordinate,
        IReadOnlyList<ConveyorChangeInfo> changedConveyors,
        MapObject previewToIgnore,
        out int resolvedQuarterTurns)
    {
        resolvedQuarterTurns = installPreviewQuarterTurns;
        if (preview == null || previewConveyor == null || conveyorPrototype == null)
        {
            return false;
        }

        ConveyorBelt straightPrefab = conveyorPrototype.StraightVariantPrefab != null
            ? conveyorPrototype.StraightVariantPrefab
            : conveyorPrototype;
        if (straightPrefab == null
            || !previewConveyor.TryGetInputDirection(preview.transform.rotation, out Vector2Int inputDirection)
            || !previewConveyor.TryGetOutputDirection(preview.transform.rotation, out Vector2Int outputDirection))
        {
            return false;
        }

        bool inputConnected = TryGetConveyorPlacementDirectionsAtCoordinate(
                                  anchorCoordinate + inputDirection,
                                  previewToIgnore,
                                  out _,
                                  out _,
                                  out Vector2Int neighborOutputDirection)
                              && neighborOutputDirection == -inputDirection;

        bool outputConnected = TryGetConveyorPlacementDirectionsAtCoordinate(
                                   anchorCoordinate + outputDirection,
                                   previewToIgnore,
                                   out _,
                                   out Vector2Int neighborInputDirection,
                                   out _)
                               && neighborInputDirection == -outputDirection;

        ConveyorChangeInfo changedInputSide = FindConveyorChangeAtCoordinate(changedConveyors, anchorCoordinate + inputDirection);
        ConveyorChangeInfo changedOutputSide = FindConveyorChangeAtCoordinate(changedConveyors, anchorCoordinate + outputDirection);

        if ((changedInputSide != null) ^ (changedOutputSide != null))
        {
            ConveyorChangeInfo preferredChange = changedInputSide ?? changedOutputSide;
            if (preferredChange != null
                && !preferredChange.isCornerVariant
                && TryGetConveyorPlacementQuarterTurnsForRotation(
                    straightPrefab,
                    preferredChange.rotation,
                    out resolvedQuarterTurns))
            {
                return true;
            }

            if (preferredChange != null
                && TryGetConveyorPlacementQuarterTurnsForOutput(
                    straightPrefab,
                    preferredChange.outputDirection,
                    out resolvedQuarterTurns))
            {
                return true;
            }

            if (changedInputSide != null)
            {
                return TryGetConveyorPlacementQuarterTurnsForInput(
                    straightPrefab,
                    inputDirection,
                    out resolvedQuarterTurns);
            }

            return TryGetConveyorPlacementQuarterTurnsForOutput(
                straightPrefab,
                outputDirection,
                out resolvedQuarterTurns);
        }

        if (!inputConnected && outputConnected)
        {
            return TryGetConveyorPlacementQuarterTurnsForInput(
                straightPrefab,
                inputDirection,
                out resolvedQuarterTurns);
        }

        if (inputConnected && !outputConnected)
        {
            return TryGetConveyorPlacementQuarterTurnsForOutput(
                straightPrefab,
                outputDirection,
                out resolvedQuarterTurns);
        }

        return TryGetConveyorStraightQuarterTurnsFromPreview(preview, conveyorPrototype, out resolvedQuarterTurns);
    }

    private bool TryGetOrderedInputItemAreaBindings(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns,
        InputOutputModule inputOutputModule,
        out List<InputOutputModuleItemAreaBinding> bindings)
    {
        bindings = new List<InputOutputModuleItemAreaBinding>();
        if (footprintSource == null
            || inputOutputModule == null
            || !TryGetRectGridFootprintSettings(footprintSource, out _, out _, out Vector2Int objectAnchorCell))
        {
            return false;
        }

        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements = inputOutputModule.RectGridPlacements;
        List<InputOutputModule.RectGridBlockPlacement> inputItemPlacements = new List<InputOutputModule.RectGridBlockPlacement>();
        for (int i = 0; i < placements.Count; i++)
        {
            InputOutputModule.RectGridBlockPlacement placement = placements[i];
            if (placement.blockType == InputOutputModule.RectGridBlockType.InputItem)
            {
                inputItemPlacements.Add(placement);
            }
        }

        if (inputItemPlacements.Count <= 0)
        {
            return false;
        }

        inputItemPlacements.Sort(CompareInputItemPlacements);

        IReadOnlyList<InputOutputModule.ItemIoEntry> inputList = inputOutputModule.InputList;
        int recipeCount = inputList != null ? inputList.Count : 0;
        if (recipeCount <= 0)
        {
            return false;
        }

        bool useSharedSingleInputArea = inputItemPlacements.Count == 1 && recipeCount > 1;
        int bindingCount = useSharedSingleInputArea
            ? recipeCount
            : Mathf.Min(inputItemPlacements.Count, recipeCount);

        for (int i = 0; i < bindingCount; i++)
        {
            ItemDefinition itemDefinition = inputList[i].itemDefinition;
            if (itemDefinition == null || itemDefinition.id < 0)
            {
                continue;
            }

            InputOutputModule.RectGridBlockPlacement placement = inputItemPlacements[useSharedSingleInputArea ? 0 : i];
            Vector2Int localOffset = new Vector2Int(placement.x - objectAnchorCell.x, placement.y - objectAnchorCell.y);
            Vector2Int coordinate = anchorCoordinate + RotateFootprintOffset(localOffset, quarterTurns);
            bindings.Add(new InputOutputModuleItemAreaBinding(coordinate, itemDefinition.id));
        }

        return bindings.Count > 0;
    }

    private static int CompareInputItemPlacements(
        InputOutputModule.RectGridBlockPlacement left,
        InputOutputModule.RectGridBlockPlacement right)
    {
        if (left.y != right.y)
        {
            return right.y.CompareTo(left.y);
        }

        return left.x.CompareTo(right.x);
    }

    private static void AddAreaMarkerRequests(List<AreaMarkerSpawnRequest> markerRequests, IReadOnlyList<Vector3> worldPositions, Sprite icon)
    {
        if (markerRequests == null || worldPositions == null || worldPositions.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < worldPositions.Count; i++)
        {
            markerRequests.Add(new AreaMarkerSpawnRequest(worldPositions[i], icon));
        }
    }

    private static void AddDirectionalAreaMarkerRequests(
        List<AreaMarkerSpawnRequest> markerRequests,
        IReadOnlyList<Vector3> worldPositions,
        Sprite icon,
        Vector3 referenceWorldPosition,
        bool pointFromReferenceToMarker)
    {
        if (markerRequests == null || worldPositions == null || worldPositions.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < worldPositions.Count; i++)
        {
            Vector3 markerWorldPosition = worldPositions[i];
            float iconRotationZ = pointFromReferenceToMarker
                ? GetArrowMarkerRotationZ(referenceWorldPosition, markerWorldPosition)
                : GetArrowMarkerRotationZ(markerWorldPosition, referenceWorldPosition);
            markerRequests.Add(new AreaMarkerSpawnRequest(markerWorldPosition, icon, iconRotationZ));
        }
    }

    private AreaMarkerPool ResolveAreaMarkerPool()
    {
        if (areaMarkerPool != null)
        {
            return areaMarkerPool;
        }

        areaMarkerPool = GetComponent<AreaMarkerPool>();
        if (areaMarkerPool == null)
        {
            areaMarkerPool = gameObject.AddComponent<AreaMarkerPool>();
        }

        return areaMarkerPool;
    }

    private Sprite ResolveArrowMarkerIcon()
    {
        return UIManager.Instance != null ? UIManager.Instance.ArrowImage : null;
    }

    private Sprite ResolveInputEnergyMarkerIcon(MapObject installedObject)
    {
        ItemDefinition installationDefinition = ResolveItemDefinition(installedObject);
        if (installationDefinition == null || installationDefinition.useEnergyType == ItemDefinition.EnergyType.None)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance?.ItemManger?.ItemDefinitions;
        if (definitions == null || definitions.Count <= 0)
        {
            return null;
        }

        ItemDefinition fixedCoalDefinition = FindItemDefinitionByName(definitions, "Coar");
        if (fixedCoalDefinition == null)
        {
            fixedCoalDefinition = FindItemDefinitionByName(definitions, "Coal");
        }

        if (fixedCoalDefinition != null && fixedCoalDefinition.icon != null)
        {
            return fixedCoalDefinition.icon;
        }

        ItemDefinition bestDefinition = null;
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition candidate = definitions[i];
            if (candidate == null
                || candidate.energyType != installationDefinition.useEnergyType
                || candidate.energyAmount <= 0)
            {
                continue;
            }

            if (bestDefinition == null || candidate.id < bestDefinition.id)
            {
                bestDefinition = candidate;
            }
        }

        return bestDefinition != null ? bestDefinition.icon : null;
    }

    private static ItemDefinition FindItemDefinitionByName(List<ItemDefinition> definitions, string itemName)
    {
        if (definitions == null || definitions.Count <= 0 || string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition candidate = definitions[i];
            if (candidate == null)
            {
                continue;
            }

            if (string.Equals(candidate.itemName, itemName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.name, itemName, System.StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static float GetArrowMarkerRotationZ(Vector3 fromWorldPosition, Vector3 toWorldPosition)
    {
        Vector2 direction = new Vector2(
            toWorldPosition.x - fromWorldPosition.x,
            toWorldPosition.z - fromWorldPosition.z);
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        return -Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
    }

    private ItemDefinition ResolveItemDefinition(MapObject mapObject)
    {
        int itemId = mapObject != null ? mapObject.ResolveItemId() : -1;
        if (itemId < 0)
        {
            return activeInstallDefinition;
        }

        List<ItemDefinition> definitions = GameManager.Instance?.ItemManger?.ItemDefinitions;
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition != null && definition.id == itemId)
                {
                    return definition;
                }
            }
        }

        return activeInstallDefinition != null && activeInstallDefinition.id == itemId
            ? activeInstallDefinition
            : null;
    }

    private bool TryGetInputOutputModule(MapObject footprintSource, out InputOutputModule inputOutputModule)
    {
        inputOutputModule = footprintSource as InputOutputModule;
        if (inputOutputModule == null && footprintSource != null)
        {
            inputOutputModule = footprintSource.GetComponent<InputOutputModule>();
        }

        if (inputOutputModule == null && footprintSource != null)
        {
            inputOutputModule = footprintSource.GetComponentInChildren<InputOutputModule>(true);
        }

        return inputOutputModule != null;
    }

    private bool TryGetMiningMachine(MapObject footprintSource, out MiningMachine miningMachine)
    {
        miningMachine = footprintSource as MiningMachine;
        if (miningMachine == null && footprintSource != null)
        {
            miningMachine = footprintSource.GetComponent<MiningMachine>();
        }

        if (miningMachine == null && footprintSource != null)
        {
            miningMachine = footprintSource.GetComponentInChildren<MiningMachine>(true);
        }

        return miningMachine != null;
    }

    private List<Vector3> GetRectGridBlockWorldPositions(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns,
        InputOutputModule.RectGridBlockType blockType)
    {
        List<Vector3> worldPositions = new List<Vector3>();
        if (!TryGetRectGridBlockCoordinates(anchorCoordinate, footprintSource, quarterTurns, blockType, out List<Vector2Int> coordinates))
        {
            return worldPositions;
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        float fallbackY = terrain != null ? terrain.transform.position.y : 0f;

        for (int i = 0; i < coordinates.Count; i++)
        {
            if (terrain != null && terrain.TryGetLoadedBlock(coordinates[i], out Block block) && block != null)
            {
                Vector3 markerPosition = block.transform.position;
                if (TryGetMiningResourceMarkerSurfaceY(block, out float markerSurfaceY))
                {
                    markerPosition.y = Mathf.Max(markerPosition.y, markerSurfaceY);
                }

                worldPositions.Add(markerPosition);
                continue;
            }

            worldPositions.Add(new Vector3(coordinates[i].x, fallbackY, coordinates[i].y));
        }

        return worldPositions;
    }

    private static bool TryGetMiningResourceMarkerSurfaceY(Block block, out float surfaceY)
    {
        surfaceY = 0f;
        Resource resource = block != null ? block.Resource : null;
        if (resource == null || resource.ResolvedHarvestMode != Resource.HarvestMode.Mining)
        {
            return false;
        }

        Renderer[] renderers = resource.GetComponentsInChildren<Renderer>(true);
        bool foundRenderer = false;
        Bounds bounds = default;
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rendererComponent = renderers[i];
                if (rendererComponent == null)
                {
                    continue;
                }

                if (!foundRenderer)
                {
                    bounds = rendererComponent.bounds;
                    foundRenderer = true;
                    continue;
                }

                bounds.Encapsulate(rendererComponent.bounds);
            }
        }

        surfaceY = foundRenderer
            ? bounds.max.y
            : Mathf.Max(block.transform.position.y, resource.transform.position.y);
        return true;
    }

    private bool TryGetRectGridBlockCoordinates(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns,
        InputOutputModule.RectGridBlockType blockType,
        out List<Vector2Int> coordinates)
    {
        coordinates = new List<Vector2Int>();
        if (!TryGetRectGridBlockLocalOffsets(footprintSource, quarterTurns, blockType, out List<Vector2Int> localOffsets))
        {
            return false;
        }

        for (int i = 0; i < localOffsets.Count; i++)
        {
            coordinates.Add(anchorCoordinate + localOffsets[i]);
        }

        return coordinates.Count > 0;
    }

    private bool TryGetRectGridBlockLocalOffsets(
        MapObject footprintSource,
        int quarterTurns,
        InputOutputModule.RectGridBlockType blockType,
        out List<Vector2Int> localOffsets)
    {
        localOffsets = new List<Vector2Int>();
        if (!TryGetRectGridFootprintSettings(footprintSource, out _, out _, out Vector2Int objectAnchorCell)
            || !TryGetInputOutputModule(footprintSource, out InputOutputModule inputOutputModule))
        {
            return false;
        }

        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements = inputOutputModule.RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            InputOutputModule.RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != blockType)
            {
                continue;
            }

            Vector2Int localOffset = new Vector2Int(placement.x - objectAnchorCell.x, placement.y - objectAnchorCell.y);
            localOffsets.Add(RotateFootprintOffset(localOffset, quarterTurns));
        }

        return localOffsets.Count > 0;
    }

    private void BeginInstallPreview(ItemDefinition definition)
    {
        ClearInstallPreview();

        activeInstallDefinition = definition;
        activeInstallPreview = null;
        activeInstallBaseRotation = definition.mapObject != null ? definition.mapObject.transform.rotation : Quaternion.identity;
        installPreviewQuarterTurns = GetPreferredInstallPreviewQuarterTurns(definition, null);
        waitForPointerReleaseAfterPreviewSpawn = true;
        installGridRefreshTimer = 0f;
        GameManager.Instance?.SetInstallationPlacementActive(true);
    }

    private MapObject CreateInstallPreviewInstance(ItemDefinition definition)
    {
        if (definition == null || definition.mapObject == null)
        {
            return null;
        }

        MapObject sourcePrefab = definition.mapObject;
        if (definition.mapObject is ConveyorBelt conveyorPrototype)
        {
            sourcePrefab = conveyorPrototype.StraightVariantPrefab != null
                ? conveyorPrototype.StraightVariantPrefab
                : definition.mapObject;
        }

        return CreateInstallPreviewInstance(sourcePrefab, definition.mapObject);
    }

    private MapObject CreateInstallPreviewInstance(MapObject sourcePrefab, MapObject nameSourcePrefab)
    {
        if (sourcePrefab == null)
        {
            return null;
        }

        MapObject preview = Instantiate(sourcePrefab);
        if (preview == null)
        {
            return null;
        }

        string previewName = nameSourcePrefab != null ? nameSourcePrefab.name : sourcePrefab.name;
        preview.name = $"{previewName}_Blueprint";
        ConfigureInstallPreview(preview);
        installPreviewSourcePrefabsByPreview[preview] = sourcePrefab;
        return preview;
    }

    private void ConfigureInstallPreview(MapObject preview)
    {
        if (preview == null)
        {
            return;
        }

        MonoBehaviour[] behaviours = preview.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            behaviour.enabled = false;
        }

        Collider[] colliders = preview.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = false;
            }
        }

        Rigidbody[] rigidbodies = preview.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody body = rigidbodies[i];
            if (body == null)
            {
                continue;
            }

            body.isKinematic = true;
            body.detectCollisions = false;
        }

        if (installPreviewPropertyBlock == null)
        {
            installPreviewPropertyBlock = new MaterialPropertyBlock();
        }

        Renderer[] renderers = preview.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Material sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial == null)
            {
                continue;
            }

            installPreviewPropertyBlock.Clear();
            bool hasColorProperty = false;

            if (sharedMaterial.HasProperty(BaseColorPropertyId))
            {
                installPreviewPropertyBlock.SetColor(BaseColorPropertyId, installPreviewTint);
                hasColorProperty = true;
            }
            else if (sharedMaterial.HasProperty(ColorPropertyId))
            {
                installPreviewPropertyBlock.SetColor(ColorPropertyId, installPreviewTint);
                hasColorProperty = true;
            }

            if (sharedMaterial.HasProperty(EmissionColorPropertyId))
            {
                installPreviewPropertyBlock.SetColor(EmissionColorPropertyId, installPreviewTint * 0.35f);
                hasColorProperty = true;
            }

            if (hasColorProperty)
            {
                renderer.SetPropertyBlock(installPreviewPropertyBlock);
            }
        }
    }

    private void TrySnapInstallPreviewToInitialBlock()
    {
        if (activeInstallPreview == null)
        {
            return;
        }

        if (TryGetPointerBlock(out Block pointerBlock))
        {
            MoveInstallPreviewToBlock(pointerBlock);
            return;
        }

        if (TryGetPlayerBlock(out Block playerBlock))
        {
            MoveInstallPreviewToBlock(playerBlock);
        }
    }

    private void TryMoveInstallPreview(Vector2 pointerPosition)
    {
        if (activeInstallPreview == null)
        {
            return;
        }

        if (TryGetPointerBlock(pointerPosition, out Block block))
        {
            MoveInstallPreviewToBlock(block);
        }
    }

    private bool MoveInstallPreviewToBlock(Block block)
    {
        if (activeInstallPreview == null
            || block == null
            || !TryFindPlaceableInstallPreviewQuarterTurns(
                block,
                activeInstallPreview,
                installPreviewQuarterTurns,
                out int resolvedQuarterTurns))
        {
            return false;
        }

        installPreviewQuarterTurns = resolvedQuarterTurns;
        installPreviewQuarterTurnsByPreview[activeInstallPreview] = resolvedQuarterTurns;

        Vector2Int previousAnchorCoordinate = Vector2Int.zero;
        bool hadPreviousAnchorCoordinate = TryGetPreviewAnchorCoordinate(activeInstallPreview, out previousAnchorCoordinate);
        ConveyorChangeInfo previousConveyorChange = null;
        if (hadPreviousAnchorCoordinate && activeInstallPreview is ConveyorBelt previousPreviewConveyor)
        {
            List<Vector2Int> previousOccupiedCoordinates = GetFootprintCoordinates(
                previousAnchorCoordinate,
                activeInstallPreview,
                GetPreviewQuarterTurns(activeInstallPreview));
            TryCreateConveyorChange(
                previousAnchorCoordinate,
                previousOccupiedCoordinates,
                previousPreviewConveyor,
                activeInstallPreview.transform.rotation,
                out previousConveyorChange);
        }

        installPreviewAnchorCoordinates[activeInstallPreview] = block.Coordinate;
        RefreshActiveConveyorPreviewVariant();
        Vector3 targetPosition = GetPreviewWorldPosition(block, activeInstallPreview, installPreviewQuarterTurns, installPreviewVerticalOffset);
        activeInstallPreview.transform.position = targetPosition;
        activeInstallPreview.transform.rotation = GetInstallPreviewRotation();
        if (activeInstallPreview is InstallationObject movedInstallationPreview)
        {
            movedInstallationPreview.RefreshInstalledDirectionFromCurrentTransform();
        }

        RefreshInstallPreviewAreaMarkers(activeInstallPreview);
        RefreshConveyorVariantsAroundActivePreview(
            hadPreviousAnchorCoordinate ? previousAnchorCoordinate : (Vector2Int?)null,
            previousConveyorChange);
        RememberLastBlueprintRotation(activeInstallDefinition, installPreviewQuarterTurns);
        InvalidateInstallGrid();
        return true;
    }

    private bool TryGetPointerBlock(out Block block)
    {
        block = null;
        return TryGetPrimaryPointerPosition(out Vector2 pointerPosition) && TryGetPointerBlock(pointerPosition, out block);
    }

    private bool TryGetPointerBlock(Vector2 pointerPosition, out Block block)
    {
        block = null;

        Camera targetCamera = ResolveInstallPreviewCamera();
        if (targetCamera == null)
        {
            return false;
        }

        Ray ray = targetCamera.ScreenPointToRay(pointerPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 512f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, CompareRaycastHits);
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                Block hitBlock = hit.collider != null ? hit.collider.GetComponentInParent<Block>() : null;
                if (hitBlock != null)
                {
                    block = hitBlock;
                    return true;
                }
            }
        }

        return TryGetBlockFromGroundPlane(ray, out block);
    }

    private bool TryGetPlayerBlock(out Block block)
    {
        block = null;

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null || GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return false;
        }

        Transform playerTransform = GameManager.Instance.Player.transform;
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(playerTransform.position.x),
            Mathf.RoundToInt(playerTransform.position.z));

        return terrain.TryGetLoadedBlock(coordinate, out block);
    }

    private Camera ResolveInstallPreviewCamera()
    {
        if (installPreviewCamera != null)
        {
            return installPreviewCamera;
        }

        installPreviewCamera = Camera.main;
        if (installPreviewCamera != null)
        {
            return installPreviewCamera;
        }

        PlayerCamera playerCamera = FindObjectOfType<PlayerCamera>();
        if (playerCamera != null)
        {
            installPreviewCamera = playerCamera.GetComponent<Camera>();
        }

        if (installPreviewCamera == null)
        {
            installPreviewCamera = FindObjectOfType<Camera>();
        }

        return installPreviewCamera;
    }

    private TerrainGenerator ResolveInstallPreviewTerrain()
    {
        if (installPreviewTerrain == null)
        {
            installPreviewTerrain = TerrainGenerator.ResolveActive();
        }

        return installPreviewTerrain;
    }

    private void UpdateInstallGrid(float deltaTime)
    {
        bool showFullGrid = IsInstallGridModeActive();
        bool showConveyorDebugOnly = !showFullGrid && GameManager.Instance != null && GameManager.Instance.DebugConveyorInstallGridEnds;
        if (!showFullGrid && !showConveyorDebugOnly)
        {
            SetInstallGridVisible(false);
            installGridRefreshTimer = 0f;
            return;
        }

        EnsureInstallGridResources();
        SetInstallGridVisible(true);

        installGridRefreshTimer -= Mathf.Max(0f, deltaTime);
        if (installGridRefreshTimer > 0f)
        {
            return;
        }

        installGridRefreshTimer = Mathf.Max(0.05f, installGridRefreshInterval);
        RebuildInstallGridMesh();
    }

    private void EnsureInstallGridResources()
    {
        if (installGridObject == null)
        {
            installGridObject = new GameObject("InstallationGridOverlay");
            installGridObject.hideFlags = HideFlags.DontSave;
            installGridObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            installGridMeshFilter = installGridObject.AddComponent<MeshFilter>();
            installGridMeshRenderer = installGridObject.AddComponent<MeshRenderer>();
        }

        if (installGridMesh == null)
        {
            installGridMesh = new Mesh
            {
                name = "InstallationGridOverlayMesh",
                hideFlags = HideFlags.DontSave
            };
            installGridMesh.MarkDynamic();
        }

        if (installGridMeshFilter != null && installGridMeshFilter.sharedMesh != installGridMesh)
        {
            installGridMeshFilter.sharedMesh = installGridMesh;
        }

        if (installGridMaterial == null)
        {
            Shader shader = Shader.Find(InstallGridOverlayShaderName);
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader != null)
            {
                installGridMaterial = new Material(shader)
                {
                    name = "InstallationGridOverlayMaterial",
                    hideFlags = HideFlags.DontSave
                };
            }
        }

        if (installGridMaterial != null)
        {
            ApplyGridMaterialColor(installGridMaterial);
        }

        if (installGridMeshRenderer != null)
        {
            installGridMeshRenderer.sharedMaterial = installGridMaterial;
            installGridMeshRenderer.sortingOrder = 5000;
            installGridMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            installGridMeshRenderer.receiveShadows = false;
            installGridMeshRenderer.lightProbeUsage = LightProbeUsage.Off;
            installGridMeshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    private void RebuildInstallGridMesh()
    {
        EnsureInstallGridResources();

        if (installGridMesh == null)
        {
            return;
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null || !terrain.TryGetLoadedBlockBounds(out Vector2Int minCoordinate, out Vector2Int maxCoordinate))
        {
            installGridMesh.Clear();
            return;
        }

        installGridMinCoordinate = minCoordinate;
        installGridMaxCoordinate = maxCoordinate;

        bool showFullGrid = IsInstallGridModeActive();

        float lineY = terrain.transform.position.y + installGridVerticalOffset;
        float fillY = lineY - 0.002f;
        float minX = minCoordinate.x - 0.5f;
        float maxX = maxCoordinate.x + 0.5f;
        float minZ = minCoordinate.y - 0.5f;
        float maxZ = maxCoordinate.y + 0.5f;

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();

        if (showFullGrid && activeInstallDefinition != null && activeInstallDefinition.mapObject != null)
        {
            for (int z = minCoordinate.y; z <= maxCoordinate.y; z++)
            {
                for (int x = minCoordinate.x; x <= maxCoordinate.x; x++)
                {
                    Vector2Int coordinate = new Vector2Int(x, z);
                    if (!terrain.TryGetLoadedBlock(coordinate, out Block block) || block == null)
                    {
                        continue;
                    }

                    if (CanPlacePreviewOnTargetBlockType(block, activeInstallDefinition.mapObject))
                    {
                        continue;
                    }

                    AddGridCellQuad(
                        vertices,
                        triangles,
                        colors,
                        coordinate,
                        fillY,
                        installGridBlockedFillColor);
                }
            }
        }

        if (showFullGrid)
        {
            AddInstallPreviewFootprintFill(vertices, triangles, colors, fillY);
            AddConveyorInstallPreviewDebugEnds(vertices, triangles, colors, lineY);
        }

        AddInstalledConveyorDebugEnds(vertices, triangles, colors, minCoordinate, maxCoordinate, lineY);

        if (showFullGrid)
        {
            for (int x = minCoordinate.x; x <= maxCoordinate.x + 1; x++)
            {
                float lineX = x - 0.5f;
                AddGridLineQuad(
                    vertices,
                    triangles,
                    colors,
                    new Vector3(lineX, lineY, minZ),
                    new Vector3(lineX, lineY, maxZ),
                    installGridColor);
            }

            for (int z = minCoordinate.y; z <= maxCoordinate.y + 1; z++)
            {
                float lineZ = z - 0.5f;
                AddGridLineQuad(
                    vertices,
                    triangles,
                    colors,
                    new Vector3(minX, lineY, lineZ),
                    new Vector3(maxX, lineY, lineZ),
                    installGridColor);
            }

            AddInstallPreviewFootprintOutline(vertices, triangles, colors, lineY + 0.001f);
        }

        installGridMesh.Clear();
        installGridMesh.SetVertices(vertices);
        installGridMesh.SetTriangles(triangles, 0, true);
        installGridMesh.SetColors(colors);
        installGridMesh.RecalculateBounds();
    }

    private void AddInstallPreviewFootprintFill(List<Vector3> vertices, List<int> triangles, List<Color> colors, float fillY)
    {
        CleanupInstallPreviewReferences();

        for (int i = 0; i < installPreviewInstances.Count; i++)
        {
            MapObject preview = installPreviewInstances[i];
            if (preview == null || !TryGetPreviewAnchorCoordinate(preview, out Vector2Int anchorCoordinate))
            {
                continue;
            }

            int quarterTurns = GetPreviewQuarterTurns(preview);
            bool isBlockedPreview = false;
            if (ResolveInstallPreviewTerrain() != null
                && ResolveInstallPreviewTerrain().TryGetLoadedBlock(anchorCoordinate, out Block anchorBlock)
                && anchorBlock != null)
            {
                isBlockedPreview = !CanPlacePreviewOnBlock(anchorBlock, preview, quarterTurns);
            }

            Color previewFillColor = isBlockedPreview ? installGridBlockedFillColor : installPreviewTint;
            List<Vector2Int> occupiedCoordinates = GetFootprintCoordinates(anchorCoordinate, preview, quarterTurns);
            for (int coordinateIndex = 0; coordinateIndex < occupiedCoordinates.Count; coordinateIndex++)
            {
                AddGridCellQuad(
                    vertices,
                    triangles,
                    colors,
                    occupiedCoordinates[coordinateIndex],
                    fillY,
                    previewFillColor);
            }
        }
    }

    private void AddInstallPreviewFootprintOutline(List<Vector3> vertices, List<int> triangles, List<Color> colors, float lineY)
    {
        CleanupInstallPreviewReferences();

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        for (int i = 0; i < installPreviewInstances.Count; i++)
        {
            MapObject preview = installPreviewInstances[i];
            if (preview == null || !TryGetPreviewAnchorCoordinate(preview, out Vector2Int anchorCoordinate))
            {
                continue;
            }

            int quarterTurns = GetPreviewQuarterTurns(preview);
            bool isBlockedPreview = false;
            if (terrain != null
                && terrain.TryGetLoadedBlock(anchorCoordinate, out Block anchorBlock)
                && anchorBlock != null)
            {
                isBlockedPreview = !CanPlacePreviewOnBlock(anchorBlock, preview, quarterTurns);
            }

            Color previewLineColor = isBlockedPreview ? installGridBlockedLineColor : installGridColor;
            List<Vector2Int> occupiedCoordinates = GetFootprintCoordinates(anchorCoordinate, preview, quarterTurns);
            for (int coordinateIndex = 0; coordinateIndex < occupiedCoordinates.Count; coordinateIndex++)
            {
                AddGridCellOutline(
                    vertices,
                    triangles,
                    colors,
                    occupiedCoordinates[coordinateIndex],
                    lineY,
                    previewLineColor);
            }
        }
    }

    private void AddGridCellOutline(List<Vector3> vertices, List<int> triangles, List<Color> colors, Vector2Int coordinate, float y, Color color)
    {
        AddGridEdgeLine(vertices, triangles, colors, coordinate, Vector2Int.up, y, color);
        AddGridEdgeLine(vertices, triangles, colors, coordinate, Vector2Int.right, y, color);
        AddGridEdgeLine(vertices, triangles, colors, coordinate, Vector2Int.down, y, color);
        AddGridEdgeLine(vertices, triangles, colors, coordinate, Vector2Int.left, y, color);
    }

    private void AddConveyorInstallPreviewDebugEnds(List<Vector3> vertices, List<int> triangles, List<Color> colors, float lineY)
    {
        CleanupInstallPreviewReferences();
        for (int i = 0; i < installPreviewInstances.Count; i++)
        {
            MapObject preview = installPreviewInstances[i];
            if (!(preview is ConveyorBelt conveyorPreview) || !TryGetPreviewAnchorCoordinate(preview, out Vector2Int anchorCoordinate))
            {
                continue;
            }

            Quaternion previewRotation = preview.transform.rotation;
            if (!conveyorPreview.TryGetOutputDirection(previewRotation, out Vector2Int outputDirection))
            {
                continue;
            }

            if (!conveyorPreview.TryGetInputDirection(previewRotation, out Vector2Int inputDirection))
            {
                inputDirection = -outputDirection;
            }

            AddConveyorFlowArrow(vertices, triangles, colors, anchorCoordinate, inputDirection, outputDirection, lineY, installGridConveyorEndDebugColor);
        }
    }

    private void AddInstalledConveyorDebugEnds(List<Vector3> vertices, List<int> triangles, List<Color> colors, Vector2Int minCoordinate, Vector2Int maxCoordinate, float lineY)
    {
        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null)
        {
            return;
        }

        HashSet<Vector2Int> drawnAnchors = new HashSet<Vector2Int>();
        for (int z = minCoordinate.y; z <= maxCoordinate.y; z++)
        {
            for (int x = minCoordinate.x; x <= maxCoordinate.x; x++)
            {
                Vector2Int coordinate = new Vector2Int(x, z);
                if (!terrain.TryGetLoadedBlock(coordinate, out Block block)
                    || block == null
                    || !(block.MapObject is ConveyorBelt conveyor)
                    || conveyor == null
                    || !conveyor.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
                    || !drawnAnchors.Add(anchorCoordinate))
                {
                    continue;
                }

                Quaternion rotation = conveyor.transform.rotation;
                if (!conveyor.TryGetOutputDirection(rotation, out Vector2Int outputDirection))
                {
                    continue;
                }

                if (!conveyor.TryGetInputDirection(rotation, out Vector2Int inputDirection))
                {
                    inputDirection = -outputDirection;
                }

                AddConveyorFlowArrow(vertices, triangles, colors, anchorCoordinate, inputDirection, outputDirection, lineY + 0.001f, installGridConveyorEndDebugColor);
            }
        }
    }

    private void AddConveyorFlowArrow(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector2Int anchorCoordinate,
        Vector2Int inputDirection,
        Vector2Int outputDirection,
        float y,
        Color color)
    {
        if (inputDirection == Vector2Int.zero || outputDirection == Vector2Int.zero)
        {
            return;
        }

        Vector3 center = new Vector3(anchorCoordinate.x, y, anchorCoordinate.y);
        Vector3 inputPoint = center + DirectionToWorld(inputDirection) * installGridConveyorArrowInset;
        Vector3 outputPoint = center + DirectionToWorld(outputDirection) * installGridConveyorArrowInset;

        if (inputDirection == -outputDirection)
        {
            AddGridLineQuad(vertices, triangles, colors, inputPoint, outputPoint, color);
            AddArrowHead(vertices, triangles, colors, Vector3.Lerp(inputPoint, outputPoint, 0.45f), (outputPoint - inputPoint).normalized, color);
            AddArrowHead(vertices, triangles, colors, Vector3.Lerp(inputPoint, outputPoint, 0.75f), (outputPoint - inputPoint).normalized, color);
            return;
        }

        AddGridLineQuad(vertices, triangles, colors, inputPoint, center, color);
        AddGridLineQuad(vertices, triangles, colors, center, outputPoint, color);
        AddArrowHead(vertices, triangles, colors, Vector3.Lerp(inputPoint, center, 0.68f), (center - inputPoint).normalized, color);
        AddArrowHead(vertices, triangles, colors, Vector3.Lerp(center, outputPoint, 0.68f), (outputPoint - center).normalized, color);
    }

    private void AddArrowHead(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 center,
        Vector3 direction,
        Color color)
    {
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector3 normalizedDirection = direction.normalized;
        Vector3 tip = center + normalizedDirection * (installGridConveyorArrowHeadLength * 0.5f);
        Vector3 baseCenter = center - normalizedDirection * (installGridConveyorArrowHeadLength * 0.5f);
        Vector3 perpendicular = Vector3.Cross(Vector3.up, normalizedDirection) * (installGridConveyorArrowHeadWidth * 0.5f);
        int startIndex = vertices.Count;

        vertices.Add(tip);
        vertices.Add(baseCenter + perpendicular);
        vertices.Add(baseCenter - perpendicular);

        colors.Add(color);
        colors.Add(color);
        colors.Add(color);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 1);
        triangles.Add(startIndex + 2);
    }

    private static Vector3 DirectionToWorld(Vector2Int direction)
    {
        return new Vector3(direction.x, 0f, direction.y).normalized;
    }

    private Quaternion wasActivePreviewRotation(MapObject preview)
    {
        if (preview == null)
        {
            return Quaternion.identity;
        }

        if (preview == activeInstallPreview)
        {
            return GetInstallPreviewRotation();
        }

        int quarterTurns = GetPreviewQuarterTurns(preview);
        Quaternion previewBaseRotation = installPreviewBaseRotationsByPreview.TryGetValue(preview, out Quaternion storedBaseRotation)
            ? storedBaseRotation
            : preview.transform.rotation;
        return GetPlacementObjectRotation(previewBaseRotation, preview, quarterTurns);
    }

    private void AddGridEdgeLine(List<Vector3> vertices, List<int> triangles, List<Color> colors, Vector2Int coordinate, Vector2Int direction, float y, Color color)
    {
        if (direction == Vector2Int.zero)
        {
            return;
        }

        float minX = coordinate.x - 0.5f;
        float maxX = coordinate.x + 0.5f;
        float minZ = coordinate.y - 0.5f;
        float maxZ = coordinate.y + 0.5f;

        Vector3 start;
        Vector3 end;

        if (direction == Vector2Int.up)
        {
            start = new Vector3(minX, y, maxZ);
            end = new Vector3(maxX, y, maxZ);
        }
        else if (direction == Vector2Int.down)
        {
            start = new Vector3(minX, y, minZ);
            end = new Vector3(maxX, y, minZ);
        }
        else if (direction == Vector2Int.left)
        {
            start = new Vector3(minX, y, minZ);
            end = new Vector3(minX, y, maxZ);
        }
        else if (direction == Vector2Int.right)
        {
            start = new Vector3(maxX, y, minZ);
            end = new Vector3(maxX, y, maxZ);
        }
        else
        {
            return;
        }

        AddGridLineQuad(vertices, triangles, colors, start, end, color);
    }

    private void AddGridLineQuad(List<Vector3> vertices, List<int> triangles, List<Color> colors, Vector3 start, Vector3 end, Color color)
    {
        Vector3 direction = end - start;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector3 perpendicular = Vector3.Cross(Vector3.up, direction.normalized) * (installGridLineWidth * 0.5f);
        int startIndex = vertices.Count;

        vertices.Add(start - perpendicular);
        vertices.Add(start + perpendicular);
        vertices.Add(end + perpendicular);
        vertices.Add(end - perpendicular);

        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 1);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 3);
    }

    private static void AddGridCellQuad(List<Vector3> vertices, List<int> triangles, List<Color> colors, Vector2Int coordinate, float y, Color color)
    {
        float minX = coordinate.x - 0.5f;
        float maxX = coordinate.x + 0.5f;
        float minZ = coordinate.y - 0.5f;
        float maxZ = coordinate.y + 0.5f;
        int startIndex = vertices.Count;

        vertices.Add(new Vector3(minX, y, minZ));
        vertices.Add(new Vector3(minX, y, maxZ));
        vertices.Add(new Vector3(maxX, y, maxZ));
        vertices.Add(new Vector3(maxX, y, minZ));

        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);

        triangles.Add(startIndex);
        triangles.Add(startIndex + 1);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 3);
    }

    private void ApplyGridMaterialColor(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty(BaseColorPropertyId))
        {
            material.SetColor(BaseColorPropertyId, installGridColor);
        }

        if (material.HasProperty(ColorPropertyId))
        {
            material.SetColor(ColorPropertyId, installGridColor);
        }
    }

    private void SetInstallGridVisible(bool isVisible)
    {
        if (installGridObject == null)
        {
            if (!isVisible)
            {
                return;
            }

            EnsureInstallGridResources();
        }

        if (installGridObject != null && installGridObject.activeSelf != isVisible)
        {
            installGridObject.SetActive(isVisible);
        }
    }

    private void ReleaseInstallGridResources()
    {
        if (Application.isPlaying)
        {
            if (installGridObject != null)
            {
                Destroy(installGridObject);
            }

            if (installGridMesh != null)
            {
                Destroy(installGridMesh);
            }

            if (installGridMaterial != null)
            {
                Destroy(installGridMaterial);
            }
        }
        else
        {
            if (installGridObject != null)
            {
                DestroyImmediate(installGridObject);
            }

            if (installGridMesh != null)
            {
                DestroyImmediate(installGridMesh);
            }

            if (installGridMaterial != null)
            {
                DestroyImmediate(installGridMaterial);
            }
        }

        installGridObject = null;
        installGridMeshFilter = null;
        installGridMeshRenderer = null;
        installGridMesh = null;
        installGridMaterial = null;
    }

    private bool TryGetBlockFromGroundPlane(Ray ray, out Block block)
    {
        block = null;

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null)
        {
            return false;
        }

        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, terrain.transform.position.y, 0f));
        if (!groundPlane.Raycast(ray, out float enter))
        {
            return false;
        }

        Vector3 worldPoint = ray.GetPoint(enter);
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(worldPoint.x),
            Mathf.RoundToInt(worldPoint.z));

        return terrain.TryGetLoadedBlock(coordinate, out block) && block != null;
    }

    private bool TryGetPrimaryPointerDragPosition(out Vector2 pointerPosition)
    {
        pointerPosition = Vector2.zero;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
            {
                pointerPosition = touch.position;
                return true;
            }
        }

        if (Input.GetMouseButton(0))
        {
            pointerPosition = Input.mousePosition;
            return true;
        }

        return false;
    }

    private void UpdateInstallPreviewPointerInput()
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    BeginPreviewPointerTracking(touch.position, IsPointerOverBlockingUi(touch.position));
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    EnsurePreviewPointerTracking(touch.position);
                    ContinuePreviewPointerTracking(touch.position);
                    break;
                case TouchPhase.Ended:
                    EndPreviewPointerTracking(touch.position, false);
                    break;
                case TouchPhase.Canceled:
                    EndPreviewPointerTracking(touch.position, true);
                    break;
            }

            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            BeginPreviewPointerTracking(Input.mousePosition, IsPointerOverBlockingUi(Input.mousePosition));
        }
        else if (Input.GetMouseButton(0))
        {
            EnsurePreviewPointerTracking(Input.mousePosition);
        }

        if (Input.GetMouseButton(0))
        {
            ContinuePreviewPointerTracking(Input.mousePosition);
        }

        if (Input.GetMouseButtonUp(0))
        {
            EndPreviewPointerTracking(Input.mousePosition, false);
        }
    }

    private void BeginPreviewPointerTracking(Vector2 pointerPosition, bool startedOverUi)
    {
        isPreviewPointerTracking = true;
        previewPointerDragged = false;
        previewPointerStartedOverUi = startedOverUi;
        previewPointerStartPosition = pointerPosition;
        previewPointerOriginPreview = null;

        if (startedOverUi)
        {
            return;
        }

        if (!TryGetPointerBlock(pointerPosition, out Block pointerBlock) || pointerBlock == null)
        {
            return;
        }

        if (!TryGetInstallPreviewAtBlock(pointerBlock, out MapObject originPreview) || originPreview == null)
        {
            return;
        }

        previewPointerOriginPreview = originPreview;
        if (originPreview != activeInstallPreview)
        {
            SelectInstallPreview(originPreview);
        }
    }

    private void EnsurePreviewPointerTracking(Vector2 pointerPosition)
    {
        if (isPreviewPointerTracking)
        {
            return;
        }

        BeginPreviewPointerTracking(pointerPosition, IsPointerOverBlockingUi(pointerPosition));
    }

    private void ContinuePreviewPointerTracking(Vector2 pointerPosition)
    {
        if (!isPreviewPointerTracking || previewPointerStartedOverUi)
        {
            return;
        }

        if (!previewPointerDragged)
        {
            float threshold = Mathf.Max(1f, installRotateTapThreshold);
            previewPointerDragged = Vector2.Distance(previewPointerStartPosition, pointerPosition) >= threshold;
        }

        if (previewPointerDragged && (IsEditingInstallation() || previewPointerOriginPreview != null))
        {
            TryMoveInstallPreview(pointerPosition);
        }
    }

    private void EndPreviewPointerTracking(Vector2 pointerPosition, bool wasCanceled)
    {
        if (!isPreviewPointerTracking)
        {
            return;
        }

        if (!wasCanceled && !previewPointerStartedOverUi)
        {
            if (IsEditingInstallation())
            {
                if (previewPointerDragged)
                {
                    TryMoveInstallPreview(pointerPosition);
                }
                else
                {
                    HandleInstallationEditClick(pointerPosition);
                }

                ResetPreviewPointerTracking();
                return;
            }

            if (previewPointerDragged)
            {
                if (previewPointerOriginPreview != null)
                {
                    TryMoveInstallPreview(pointerPosition);
                }
            }
            else
            {
                HandleInstallPreviewClick(pointerPosition);
            }
        }

        ResetPreviewPointerTracking();
    }

    private void HandleInstallationEditClick(Vector2 pointerPosition)
    {
        if (!TryGetEditableInstallationAtPointer(pointerPosition, out InstallationObject installationObject, out Vector2Int anchorCoordinate)
            || installationObject == null)
        {
            return;
        }

        if (activeInstallationEditSession != null
            && installationObject == activeInstallationEditSession.originalInstallation)
        {
            return;
        }

        if (!TryCommitActiveInstallationEditPreview())
        {
            CancelInstallationEdit();
        }
        SelectEditableInstallation(installationObject, anchorCoordinate);
        BeginInstallationEdit(installationObject);
    }

    private bool TryCommitActiveInstallationEditPreview()
    {
        InstallationEditSession editSession = activeInstallationEditSession;
        if (editSession == null)
        {
            return false;
        }

        CleanupInstallPreviewReferences();
        if (activeInstallPreview == null || !TryGetPreviewAnchorCoordinate(activeInstallPreview, out Vector2Int anchorCoordinate))
        {
            return false;
        }

        Quaternion previewRotation = activeInstallPreview.transform.rotation;
        Vector3 previewPosition = activeInstallPreview.transform.position;
        int quarterTurns = GetPreviewQuarterTurns(activeInstallPreview);
        int conveyorVariantKind = GetConveyorVariantKind(activeInstallPreview);
        if (!CanRestoreEditedInstallationAt(
                editSession,
                anchorCoordinate,
                quarterTurns,
                conveyorVariantKind,
                activeInstallPreview))
        {
            InvalidateInstallGrid();
            return false;
        }

        activeInstallationEditSession = null;
        RestoreEditedInstallation(
            editSession,
            anchorCoordinate,
            quarterTurns,
            conveyorVariantKind,
            previewRotation,
            previewPosition);
        ClearInstallPreview();
        return true;
    }

    private void ResetPreviewPointerTracking()
    {
        isPreviewPointerTracking = false;
        previewPointerDragged = false;
        previewPointerStartedOverUi = false;
        previewPointerStartPosition = Vector2.zero;
        previewPointerOriginPreview = null;
    }

    private void RotateInstallPreviewClockwise()
    {
        if (activeInstallPreview == null)
        {
            return;
        }

        Block anchorBlock = null;
        bool hasAnchorBlock = TryGetPreviewAnchorCoordinate(activeInstallPreview, out Vector2Int anchorCoordinate)
                            && ResolveInstallPreviewTerrain() != null
                            && ResolveInstallPreviewTerrain().TryGetLoadedBlock(anchorCoordinate, out anchorBlock);
        ConveyorChangeInfo previousConveyorChange = null;
        if (hasAnchorBlock && activeInstallPreview is ConveyorBelt existingPreviewConveyor)
        {
            List<Vector2Int> previousOccupiedCoordinates = GetFootprintCoordinates(
                anchorCoordinate,
                activeInstallPreview,
                GetPreviewQuarterTurns(activeInstallPreview));
            TryCreateConveyorChange(
                anchorCoordinate,
                previousOccupiedCoordinates,
                existingPreviewConveyor,
                activeInstallPreview.transform.rotation,
                out previousConveyorChange);
        }

        if (activeInstallDefinition != null && activeInstallDefinition.mapObject is ConveyorBelt conveyorBelt)
        {
            int logicalQuarterTurns = GetPreviewQuarterTurns(activeInstallPreview);

            bool useCornerVariant = installPreviewConveyorVariantMode == ConveyorPreviewVariantMode.Corner
                || (activeInstallPreview is ConveyorBelt previewConveyor && previewConveyor.IsCornerVariant);
            if (hasAnchorBlock)
            {
                if (!TryFindNextPlaceableConveyorPreviewRotation(
                        anchorBlock,
                        conveyorBelt,
                        activeInstallPreview,
                        logicalQuarterTurns,
                        useCornerVariant,
                        out logicalQuarterTurns,
                        out ConveyorPreviewVariantMode resolvedVariantMode,
                        out MapObject resolvedVariantPrefab))
                {
                    return;
                }

                installPreviewQuarterTurns = logicalQuarterTurns;
                installPreviewConveyorVariantMode = resolvedVariantMode;
                if (resolvedVariantPrefab != null)
                {
                    installPreviewSourcePrefabsByPreview[activeInstallPreview] = resolvedVariantPrefab;
                }
            }
            else
            {
                conveyorBelt.HandlePlacementRotation(ref logicalQuarterTurns, ref useCornerVariant, false);
                installPreviewQuarterTurns = logicalQuarterTurns;
                installPreviewConveyorVariantMode = useCornerVariant
                    ? ConveyorPreviewVariantMode.Corner
                    : ConveyorPreviewVariantMode.Straight;
            }
        }
        else
        {
            int nextQuarterTurns = (installPreviewQuarterTurns + 1) % 4;
            if (hasAnchorBlock)
            {
                if (!TryFindPlaceableInstallPreviewQuarterTurns(
                    anchorBlock,
                    activeInstallPreview,
                    nextQuarterTurns,
                    out nextQuarterTurns))
                {
                    return;
                }
            }

            installPreviewQuarterTurns = nextQuarterTurns;
        }

        installPreviewQuarterTurnsByPreview[activeInstallPreview] = installPreviewQuarterTurns;
        preferDifferentConveyorCornerOnNextRefresh = activeInstallDefinition != null
            && activeInstallDefinition.mapObject is ConveyorBelt
            && installPreviewConveyorVariantMode == ConveyorPreviewVariantMode.Corner;
        RefreshActiveConveyorPreviewVariant();
        activeInstallPreview.transform.rotation = GetInstallPreviewRotation();
        if (activeInstallPreview is InstallationObject rotatedInstallationPreview)
        {
            rotatedInstallationPreview.RefreshInstalledDirectionFromCurrentTransform();
        }

        if (hasAnchorBlock)
        {
            activeInstallPreview.transform.position = GetPreviewWorldPosition(
                anchorBlock,
                activeInstallPreview,
                installPreviewQuarterTurns,
                installPreviewVerticalOffset);
        }

        RefreshInstallPreviewAreaMarkers(activeInstallPreview);
        RememberLastBlueprintRotation(activeInstallDefinition, installPreviewQuarterTurns);
        RefreshConveyorVariantsAroundActivePreview(
            hasAnchorBlock ? anchorCoordinate : (Vector2Int?)null,
            previousConveyorChange);
        InvalidateInstallGrid();
    }

    private bool TryFindNextPlaceableConveyorPreviewRotation(
        Block anchorBlock,
        ConveyorBelt conveyorPrototype,
        MapObject previewToIgnore,
        int currentStraightQuarterTurns,
        bool currentUsesCornerVariant,
        out int resolvedQuarterTurns,
        out ConveyorPreviewVariantMode resolvedVariantMode,
        out MapObject resolvedVariantPrefab)
    {
        int normalizedCurrentQuarterTurns = NormalizePlacementQuarterTurns(currentStraightQuarterTurns);
        resolvedQuarterTurns = normalizedCurrentQuarterTurns;
        resolvedVariantMode = currentUsesCornerVariant
            ? ConveyorPreviewVariantMode.Corner
            : ConveyorPreviewVariantMode.Straight;
        resolvedVariantPrefab = null;

        if (anchorBlock == null || conveyorPrototype == null)
        {
            return false;
        }

        bool currentIsCorner = currentUsesCornerVariant && GetConveyorVariantKind(previewToIgnore) > 0;
        int currentSequenceIndex = currentIsCorner
            ? (((normalizedCurrentQuarterTurns + 3) % 4) * 2) + 1
            : normalizedCurrentQuarterTurns * 2;
        const int sequenceCount = 8;
        for (int step = 1; step <= sequenceCount; step++)
        {
            int candidateSequenceIndex = (currentSequenceIndex + step) % sequenceCount;
            bool candidateUsesCorner = (candidateSequenceIndex % 2) == 1;
            int candidateQuarterTurns = candidateUsesCorner
                ? ((candidateSequenceIndex / 2) + 1) % 4
                : candidateSequenceIndex / 2;
            if (TryUseConveyorRotationSequenceCandidate(
                    anchorBlock,
                    conveyorPrototype,
                    previewToIgnore,
                    candidateQuarterTurns,
                    candidateUsesCorner,
                    out resolvedQuarterTurns,
                    out resolvedVariantMode,
                    out resolvedVariantPrefab))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryUseConveyorRotationSequenceCandidate(
        Block anchorBlock,
        ConveyorBelt conveyorPrototype,
        MapObject previewToIgnore,
        int candidateQuarterTurns,
        bool candidateUsesCorner,
        out int resolvedQuarterTurns,
        out ConveyorPreviewVariantMode resolvedVariantMode,
        out MapObject resolvedVariantPrefab)
    {
        resolvedQuarterTurns = NormalizePlacementQuarterTurns(candidateQuarterTurns);
        resolvedVariantMode = candidateUsesCorner
            ? ConveyorPreviewVariantMode.Corner
            : ConveyorPreviewVariantMode.Straight;
        resolvedVariantPrefab = null;

        if (!TryResolvePlaceableConveyorRotationCandidate(
                anchorBlock,
                conveyorPrototype,
                previewToIgnore,
                candidateQuarterTurns,
                resolvedVariantMode,
                out resolvedVariantPrefab,
                out resolvedQuarterTurns))
        {
            return false;
        }

        if (IsSameConveyorPreviewState(previewToIgnore, resolvedVariantPrefab, resolvedQuarterTurns))
        {
            return false;
        }

        return true;
    }

    private bool TryResolvePlaceableConveyorRotationCandidate(
        Block anchorBlock,
        ConveyorBelt conveyorPrototype,
        MapObject previewToIgnore,
        int candidateQuarterTurns,
        ConveyorPreviewVariantMode candidateVariantMode,
        out MapObject resolvedPrefab,
        out int resolvedQuarterTurns)
    {
        resolvedPrefab = null;
        resolvedQuarterTurns = NormalizePlacementQuarterTurns(candidateQuarterTurns);
        if (anchorBlock == null || conveyorPrototype == null)
        {
            return false;
        }

        if (candidateVariantMode == ConveyorPreviewVariantMode.Corner)
        {
            if (!TryResolveConveyorCornerPlacementPrefab(
                    conveyorPrototype,
                    anchorBlock.Coordinate,
                    candidateQuarterTurns,
                    previewToIgnore,
                    out resolvedPrefab,
                    out resolvedQuarterTurns,
                    true)
                || !(resolvedPrefab is ConveyorBelt cornerConveyor)
                || !cornerConveyor.IsCornerVariant)
            {
                return false;
            }

            if (!TryGetFootprintBlocks(anchorBlock.Coordinate, resolvedPrefab, resolvedQuarterTurns, previewToIgnore, out _))
            {
                return false;
            }

            return HasConveyorEndpointConnectionAtCoordinate(
                cornerConveyor,
                anchorBlock.Coordinate,
                resolvedQuarterTurns,
                previewToIgnore);
        }

        resolvedPrefab = ResolveConveyorVariantPrefab(conveyorPrototype, 0) ?? conveyorPrototype;
        if (!(resolvedPrefab is ConveyorBelt straightConveyor) || straightConveyor.IsCornerVariant)
        {
            return false;
        }

        return TryGetFootprintBlocks(anchorBlock.Coordinate, resolvedPrefab, resolvedQuarterTurns, previewToIgnore, out _);
    }

    private bool TryResolveCornerPreservingConveyorRotation(
        ConveyorBelt conveyorPrototype,
        Vector2Int anchorCoordinate,
        int currentStraightQuarterTurns,
        MapObject previewToIgnore,
        bool hasAnchorBlock,
        out MapObject resolvedPrefab,
        out int resolvedQuarterTurns)
    {
        resolvedPrefab = null;
        resolvedQuarterTurns = currentStraightQuarterTurns;
        if (!hasAnchorBlock || conveyorPrototype == null)
        {
            return false;
        }

        int normalizedQuarterTurns = ((currentStraightQuarterTurns % 4) + 4) % 4;
        // Do not wrap back to earlier corner rotations here; otherwise corner priority can make straight rotation unreachable.
        for (int candidateQuarterTurns = normalizedQuarterTurns + 1; candidateQuarterTurns < 4; candidateQuarterTurns++)
        {
            if (!TryGetConveyorPlacementOutputDirection(
                    conveyorPrototype,
                    candidateQuarterTurns,
                    out Vector2Int desiredOutputDirection))
            {
                continue;
            }

            if (!TryResolveDifferentCornerPlacementPrefabForOutput(
                    conveyorPrototype,
                    anchorCoordinate,
                    desiredOutputDirection,
                    previewToIgnore,
                    out MapObject resolvedCornerPrefab,
                    out int resolvedCornerQuarterTurns))
            {
                continue;
            }

            resolvedPrefab = resolvedCornerPrefab;
            resolvedQuarterTurns = resolvedCornerQuarterTurns;
            return true;
        }

        return false;
    }

    private bool TryResolveDifferentCornerPlacementPrefabForOutput(
        ConveyorBelt conveyorPrototype,
        Vector2Int anchorCoordinate,
        Vector2Int desiredOutputDirection,
        MapObject previewToIgnore,
        out MapObject resolvedPrefab,
        out int resolvedQuarterTurns)
    {
        resolvedPrefab = null;
        resolvedQuarterTurns = 0;
        if (conveyorPrototype == null || desiredOutputDirection == Vector2Int.zero)
        {
            return false;
        }

        ConveyorBelt cornerCandidate = conveyorPrototype.CornerVariantPrefab;
        ConveyorBelt reverseCornerCandidate = conveyorPrototype.ReverseCornerVariantPrefab;
        bool preferReverseCandidate = !(previewToIgnore is ConveyorBelt previewConveyor) || !previewConveyor.IsReverseCornerVariant;
        ConveyorBelt[] candidates =
        {
            preferReverseCandidate ? reverseCornerCandidate : cornerCandidate,
            preferReverseCandidate ? cornerCandidate : reverseCornerCandidate
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            ConveyorBelt candidate = candidates[i];
            if (candidate == null || !candidate.IsCornerVariant)
            {
                continue;
            }

            bool duplicateCandidate = false;
            for (int previousIndex = 0; previousIndex < i; previousIndex++)
            {
                if (candidate == candidates[previousIndex])
                {
                    duplicateCandidate = true;
                    break;
                }
            }

            if (duplicateCandidate
                || !TryGetConveyorPlacementQuarterTurnsForOutput(candidate, desiredOutputDirection, out int candidateQuarterTurns)
                || IsSameConveyorPreviewState(previewToIgnore, candidate, candidateQuarterTurns))
            {
                continue;
            }

            if (!HasConveyorEndpointConnectionAtCoordinate(
                    candidate,
                    anchorCoordinate,
                    candidateQuarterTurns,
                    previewToIgnore))
            {
                continue;
            }

            resolvedPrefab = candidate;
            resolvedQuarterTurns = candidateQuarterTurns;
            return true;
        }

        return false;
    }

    private bool IsSameConveyorPreviewState(MapObject preview, MapObject candidatePrefab, int candidateQuarterTurns)
    {
        if (!(preview is ConveyorBelt previewConveyor)
            || !(candidatePrefab is ConveyorBelt candidateConveyor))
        {
            return false;
        }

        if (previewConveyor.IsCornerVariant != candidateConveyor.IsCornerVariant
            || previewConveyor.IsReverseCornerVariant != candidateConveyor.IsReverseCornerVariant)
        {
            return false;
        }

        Quaternion candidateRotation = GetPlacementObjectRotation(candidateConveyor, candidateQuarterTurns);
        return Mathf.Abs(Quaternion.Dot(preview.transform.rotation, candidateRotation)) >= 0.9999f;
    }

    private bool TryGetConveyorStraightQuarterTurnsFromPreview(MapObject preview, ConveyorBelt conveyorPrototype, out int resolvedQuarterTurns)
    {
        resolvedQuarterTurns = installPreviewQuarterTurns;
        if (!(preview is ConveyorBelt previewConveyor) || conveyorPrototype == null)
        {
            return false;
        }

        if (!previewConveyor.TryGetOutputDirection(preview.transform.rotation, out Vector2Int outputDirection))
        {
            return false;
        }

        ConveyorBelt straightPrefab = conveyorPrototype.StraightVariantPrefab != null
            ? conveyorPrototype.StraightVariantPrefab
            : conveyorPrototype;
        return TryGetConveyorPlacementQuarterTurnsForOutput(straightPrefab, outputDirection, out resolvedQuarterTurns);
    }

    private Quaternion GetInstallPreviewRotation()
    {
        if (activeInstallPreview != null)
        {
            Quaternion previewBaseRotation = installPreviewBaseRotationsByPreview.TryGetValue(activeInstallPreview, out Quaternion storedBaseRotation)
                ? storedBaseRotation
                : activeInstallPreview.transform.rotation;
            return GetPlacementObjectRotation(previewBaseRotation, activeInstallPreview, installPreviewQuarterTurns);
        }

        if (activeInstallDefinition != null && activeInstallDefinition.mapObject != null)
        {
            return GetPlacementObjectRotation(activeInstallBaseRotation, activeInstallDefinition.mapObject, installPreviewQuarterTurns);
        }

        return Quaternion.identity;
    }

    private void HandleInstallPreviewClick(Vector2 pointerPosition)
    {
        if (!TryGetPointerBlock(pointerPosition, out Block clickedBlock) || clickedBlock == null)
        {
            return;
        }

        if (TryGetInstallPreviewAtBlock(clickedBlock, out MapObject clickedPreview) && clickedPreview != null)
        {
            RemoveInstallPreview(clickedPreview);
            return;
        }

        if (CanCreateAdditionalPreview())
        {
            TryCreateAndPlaceInstallPreview(clickedBlock, null);
        }
    }

    private bool TryDuplicateInstallPreview(Vector2 pointerPosition)
    {
        if (previewPointerOriginPreview == null || activeInstallDefinition == null || !CanCreateAdditionalPreview())
        {
            return false;
        }

        if (!TryGetPointerBlock(pointerPosition, out Block targetBlock) || targetBlock == null)
        {
            return false;
        }

        if (TryGetInstallPreviewAtBlock(targetBlock, out MapObject existingPreview) && existingPreview != null)
        {
            SelectInstallPreview(existingPreview);
            return true;
        }

        return TryCreateAndPlaceInstallPreview(targetBlock, previewPointerOriginPreview);
    }

    private bool IsPreviewOnBlock(Block block)
    {
        return activeInstallPreview != null
               && block != null
               && TryGetInstallPreviewAtBlock(block, out MapObject preview)
               && preview == activeInstallPreview;
    }

    private void SelectInstallPreview(MapObject preview)
    {
        if (preview == null)
        {
            return;
        }

        activeInstallPreview = preview;
        installPreviewQuarterTurns = GetPreviewQuarterTurns(preview);
        installPreviewConveyorVariantMode = GetConveyorPreviewVariantMode(preview);
        activeInstallPreview.transform.rotation = GetInstallPreviewRotation();
        if (activeInstallPreview is InstallationObject selectedInstallationPreview)
        {
            selectedInstallationPreview.RefreshInstalledDirectionFromCurrentTransform();
        }

        RefreshInstallPreviewAreaMarkers(activeInstallPreview);
    }

    private int GetPreviewQuarterTurns(MapObject preview)
    {
        if (preview == null)
        {
            return 0;
        }

        if (installPreviewQuarterTurnsByPreview.TryGetValue(preview, out int storedQuarterTurns))
        {
            return Mathf.Abs(storedQuarterTurns) % 4;
        }

        return 0;
    }

    private static int NormalizePlacementQuarterTurns(int quarterTurns)
    {
        return ((quarterTurns % 4) + 4) % 4;
    }

    private void RememberLastBlueprintRotation(ItemDefinition definition, int quarterTurns)
    {
        int normalizedQuarterTurns = NormalizePlacementQuarterTurns(quarterTurns);
        lastBlueprintQuarterTurns = normalizedQuarterTurns;
        hasLastBlueprintQuarterTurns = true;

        if (definition != null && definition.id >= 0)
        {
            lastBlueprintQuarterTurnsByItemId[definition.id] = normalizedQuarterTurns;
        }
    }

    private void RememberLastInstalledRotation(ItemDefinition definition, int quarterTurns)
    {
        int normalizedQuarterTurns = NormalizePlacementQuarterTurns(quarterTurns);
        lastInstalledQuarterTurns = normalizedQuarterTurns;
        hasLastInstalledQuarterTurns = true;

        if (definition != null && definition.id >= 0)
        {
            lastInstalledQuarterTurnsByItemId[definition.id] = normalizedQuarterTurns;
        }
    }

    private int GetPreferredInstallPreviewQuarterTurns(ItemDefinition definition, MapObject sourcePreview)
    {
        if (sourcePreview != null)
        {
            return GetPreviewQuarterTurns(sourcePreview);
        }

        if (TryGetRememberedBlueprintRotation(definition, out int blueprintQuarterTurns))
        {
            return blueprintQuarterTurns;
        }

        if (activeInstallPreview != null && installPreviewInstances.Contains(activeInstallPreview))
        {
            return GetPreviewQuarterTurns(activeInstallPreview);
        }

        return TryGetRememberedInstallRotation(definition, out int rememberedQuarterTurns)
            ? rememberedQuarterTurns
            : 0;
    }

    private bool HasRememberedInstallRotation(ItemDefinition definition)
    {
        return TryGetRememberedInstallRotation(definition, out _);
    }

    private bool HasRememberedBlueprintRotation(ItemDefinition definition)
    {
        return TryGetRememberedBlueprintRotation(definition, out _);
    }

    private bool TryGetRememberedBlueprintRotation(ItemDefinition definition, out int quarterTurns)
    {
        quarterTurns = 0;
        if (definition != null
            && definition.id >= 0
            && lastBlueprintQuarterTurnsByItemId.TryGetValue(definition.id, out int itemQuarterTurns))
        {
            quarterTurns = NormalizePlacementQuarterTurns(itemQuarterTurns);
            return true;
        }

        if (!hasLastBlueprintQuarterTurns)
        {
            return false;
        }

        quarterTurns = NormalizePlacementQuarterTurns(lastBlueprintQuarterTurns);
        return true;
    }

    private bool TryGetRememberedInstallRotation(ItemDefinition definition, out int quarterTurns)
    {
        quarterTurns = 0;
        if (definition != null
            && definition.id >= 0
            && lastInstalledQuarterTurnsByItemId.TryGetValue(definition.id, out int itemQuarterTurns))
        {
            quarterTurns = NormalizePlacementQuarterTurns(itemQuarterTurns);
            return true;
        }

        if (!hasLastInstalledQuarterTurns)
        {
            return false;
        }

        quarterTurns = NormalizePlacementQuarterTurns(lastInstalledQuarterTurns);
        return true;
    }

    private int ResolvePlacementQuarterTurnsFromRotation(MapObject sourcePrefab, Quaternion worldRotation, int fallbackQuarterTurns)
    {
        if (sourcePrefab == null)
        {
            return ((fallbackQuarterTurns % 4) + 4) % 4;
        }

        for (int candidateQuarterTurns = 0; candidateQuarterTurns < 4; candidateQuarterTurns++)
        {
            Quaternion candidateRotation = GetPlacementObjectRotation(sourcePrefab, candidateQuarterTurns);
            if (Mathf.Abs(Quaternion.Dot(candidateRotation, worldRotation)) >= 0.9999f)
            {
                return candidateQuarterTurns;
            }
        }

        return ((fallbackQuarterTurns % 4) + 4) % 4;
    }

    private bool CanCreateAdditionalPreview()
    {
        if (IsEditingInstallation())
        {
            return false;
        }

        int itemId = activeInstallDefinition != null ? activeInstallDefinition.id : -1;
        int availableItemCount = GetAvailableInstallItemCount(itemId);
        return GetInstallPreviewCount() < availableItemCount;
    }

    private int GetAvailableInstallItemCount(int itemId = -1)
    {
        if (IsEditingInstallation())
        {
            return 1;
        }

        if (itemId < 0 && activeInstallDefinition != null)
        {
            itemId = activeInstallDefinition.id;
        }

        return GetCurrentInstallItemCount() + GetReservedInstallPreviewItemCount(itemId);
    }

    private int GetCurrentInstallItemCount()
    {
        if (IsEditingInstallation())
        {
            return 1;
        }

        int itemId = activeInstallDefinition != null ? activeInstallDefinition.id : -1;
        PlayerBag handBag = GetPlayerHandBag();
        if (itemId < 0 && handBag != null)
        {
            itemId = handBag.GetSlotItemId(0);
        }

        if (itemId < 0)
        {
            return 0;
        }

        int totalCount = 0;
        if (handBag != null && handBag.GetSlotItemId(0) == itemId)
        {
            totalCount += handBag.GetSlotCount(0);
        }

        PlayerBag inventoryBag = GetPlayerInventoryBag();
        if (inventoryBag != null && inventoryBag != handBag)
        {
            totalCount += inventoryBag.GetTotalItemCount(itemId);
        }

        return totalCount;
    }

    private int RemoveInstallItemsFromPlayer(int itemId, int requestedCount)
    {
        if (itemId < 0 || requestedCount <= 0)
        {
            return 0;
        }

        int removedCount = 0;
        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        PlayerBag handBag = player != null ? player.GetHandBag() : GetPlayerHandBag();
        PlayerBag inventoryBag = player != null ? player.GetBag() : GetPlayerInventoryBag();
        if (inventoryBag != null && inventoryBag != handBag)
        {
            removedCount += inventoryBag.RemoveItems(itemId, requestedCount);
        }

        int remainingCount = requestedCount - removedCount;
        if (remainingCount > 0 && handBag != null)
        {
            removedCount += handBag.RemoveItems(itemId, remainingCount);
            handBag.RefreshExternalStackCounts();
        }

        player?.UpdateCarryState();
        return removedCount;
    }

    private int GetInstallPreviewCount()
    {
        CleanupInstallPreviewReferences();
        return installPreviewInstances.Count;
    }

    private void RegisterInstallPreview(MapObject preview, int quarterTurns)
    {
        if (preview == null)
        {
            return;
        }

        CleanupInstallPreviewReferences();
        if (!installPreviewInstances.Contains(preview))
        {
            installPreviewInstances.Add(preview);
        }

        installPreviewQuarterTurnsByPreview[preview] = Mathf.Abs(quarterTurns) % 4;
        installPreviewBaseRotationsByPreview[preview] = preview.transform.rotation;
        if (!installPreviewPlacementSequencesByPreview.ContainsKey(preview))
        {
            installPreviewPlacementSequencesByPreview[preview] = InstallationObject.ClaimNextPlacementSequence();
        }
        installPreviewConveyorVariantMode = GetConveyorPreviewVariantMode(preview);
    }

    private void CleanupInstallPreviewReferences()
    {
        for (int i = installPreviewInstances.Count - 1; i >= 0; i--)
        {
            if (installPreviewInstances[i] != null)
            {
                continue;
            }

            installPreviewInstances.RemoveAt(i);
        }

        List<MapObject> previews = new List<MapObject>(installPreviewQuarterTurnsByPreview.Keys);
        for (int i = 0; i < previews.Count; i++)
        {
            if (previews[i] != null)
            {
                continue;
            }

            installPreviewQuarterTurnsByPreview.Remove(previews[i]);
        }

        previews = new List<MapObject>(installPreviewAnchorCoordinates.Keys);
        for (int i = 0; i < previews.Count; i++)
        {
            if (previews[i] != null)
            {
                continue;
            }

            installPreviewAnchorCoordinates.Remove(previews[i]);
        }

        previews = new List<MapObject>(installPreviewBaseRotationsByPreview.Keys);
        for (int i = 0; i < previews.Count; i++)
        {
            if (previews[i] != null)
            {
                continue;
            }

            installPreviewBaseRotationsByPreview.Remove(previews[i]);
        }

        previews = new List<MapObject>(installPreviewSourcePrefabsByPreview.Keys);
        for (int i = 0; i < previews.Count; i++)
        {
            if (previews[i] != null)
            {
                continue;
            }

            installPreviewSourcePrefabsByPreview.Remove(previews[i]);
        }

        previews = new List<MapObject>(installPreviewPlacementSequencesByPreview.Keys);
        for (int i = 0; i < previews.Count; i++)
        {
            if (previews[i] != null)
            {
                continue;
            }

            installPreviewPlacementSequencesByPreview.Remove(previews[i]);
        }

        previews = new List<MapObject>(installPreviewItemReservationsByPreview.Keys);
        for (int i = 0; i < previews.Count; i++)
        {
            if (previews[i] != null)
            {
                continue;
            }

            if (installPreviewItemReservationsByPreview.TryGetValue(
                    previews[i],
                    out InstallPreviewItemReservation reservation))
            {
                RefundInstallPreviewReservation(reservation);
            }

            installPreviewItemReservationsByPreview.Remove(previews[i]);
        }
    }

    private bool TryCreateAndPlaceInstallPreview(Block block, MapObject sourcePreview)
    {
        if (block == null || activeInstallDefinition == null || !CanCreateAdditionalPreview())
        {
            return false;
        }

        int quarterTurns = GetPreferredInstallPreviewQuarterTurns(activeInstallDefinition, sourcePreview);
        if (sourcePreview == null && activeInstallDefinition.mapObject is ConveyorBelt conveyorPrototype)
        {
            if (!HasRememberedBlueprintRotation(activeInstallDefinition)
                && !HasRememberedInstallRotation(activeInstallDefinition)
                && activeInstallPreview == null)
            {
                TryResolveInitialConveyorPreviewQuarterTurns(block.Coordinate, conveyorPrototype, out quarterTurns);
            }
        }

        if (!TryFindPlaceableInstallPreviewQuarterTurns(block, null, quarterTurns, out quarterTurns))
        {
            return false;
        }

        if (!TryReserveInstallPreviewItem(activeInstallDefinition, out InstallPreviewItemReservation reservation))
        {
            return false;
        }

        MapObject preview = CreateInstallPreviewInstance(activeInstallDefinition);
        if (preview == null)
        {
            RefundInstallPreviewReservation(reservation);
            return false;
        }

        installPreviewItemReservationsByPreview[preview] = reservation;
        RegisterInstallPreview(preview, quarterTurns);
        SelectInstallPreview(preview);
        if (sourcePreview != null)
        {
            installPreviewConveyorVariantMode = GetConveyorPreviewVariantMode(sourcePreview);
            RefreshActiveConveyorPreviewVariant();
        }
        if (MoveInstallPreviewToBlock(block))
        {
            return true;
        }

        RemoveInstallPreview(preview);
        return false;
    }

    private void RemoveInstallPreview(MapObject preview)
    {
        if (preview == null)
        {
            return;
        }

        RefundInstallPreviewReservation(preview);

        ConveyorChangeInfo removedPreviewConveyorChange = null;
        if (preview is ConveyorBelt previewConveyor
            && TryGetPreviewAnchorCoordinate(preview, out Vector2Int previewAnchorCoordinate))
        {
            List<Vector2Int> occupiedCoordinates = GetFootprintCoordinates(
                previewAnchorCoordinate,
                preview,
                GetPreviewQuarterTurns(preview));
            TryCreateConveyorChange(
                previewAnchorCoordinate,
                occupiedCoordinates,
                previewConveyor,
                preview.transform.rotation,
                out removedPreviewConveyorChange);
        }

        installPreviewInstances.Remove(preview);
        installPreviewQuarterTurnsByPreview.Remove(preview);
        installPreviewBaseRotationsByPreview.Remove(preview);
        installPreviewSourcePrefabsByPreview.Remove(preview);
        installPreviewPlacementSequencesByPreview.Remove(preview);
        installPreviewAnchorCoordinates.Remove(preview);

        if (activeInstallPreview == preview)
        {
            activeInstallPreview = null;
            installPreviewQuarterTurns = 0;
        }

        ClearInputOutputMarkers(preview);
        if (Application.isPlaying)
        {
            Destroy(preview.gameObject);
        }
        else
        {
            DestroyImmediate(preview.gameObject);
        }

        EnsureValidActiveInstallPreview();
        if (removedPreviewConveyorChange != null)
        {
            NormalizeDisconnectedConveyorCornersAroundChanges(
                new List<ConveyorChangeInfo> { removedPreviewConveyorChange },
                false,
                preview);

            RefreshConveyorPreviewVariants(
                new List<ConveyorChangeInfo> { removedPreviewConveyorChange },
                preview);
        }
        else
        {
            RefreshActiveConveyorPreviewVariant();
        }
        InvalidateInstallGrid();
    }

    private void RemovePlacedInstallPreviews(IReadOnlyList<MapObject> placedPreviews)
    {
        if (placedPreviews == null || placedPreviews.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < placedPreviews.Count; i++)
        {
            MapObject preview = placedPreviews[i];
            if (preview == null)
            {
                continue;
            }

            installPreviewInstances.Remove(preview);
            installPreviewQuarterTurnsByPreview.Remove(preview);
            installPreviewBaseRotationsByPreview.Remove(preview);
            installPreviewSourcePrefabsByPreview.Remove(preview);
            installPreviewPlacementSequencesByPreview.Remove(preview);
            installPreviewAnchorCoordinates.Remove(preview);
            installPreviewItemReservationsByPreview.Remove(preview);

            if (activeInstallPreview == preview)
            {
                activeInstallPreview = null;
                installPreviewQuarterTurns = 0;
            }

            ClearInputOutputMarkers(preview);
            if (Application.isPlaying)
            {
                Destroy(preview.gameObject);
            }
            else
            {
                DestroyImmediate(preview.gameObject);
            }
        }

        EnsureValidActiveInstallPreview();
        if (installPreviewInstances.Count <= 0)
        {
            ClearInstallPreview();
            return;
        }

        InvalidateInstallGrid();
    }

    private void EnsureValidActiveInstallPreview()
    {
        if (activeInstallPreview != null)
        {
            return;
        }

        CleanupInstallPreviewReferences();
        if (installPreviewInstances.Count <= 0)
        {
            installPreviewQuarterTurns = 0;
            return;
        }

        SelectInstallPreview(installPreviewInstances[installPreviewInstances.Count - 1]);
    }

    private bool IsInstallationModeActive()
    {
        return GameManager.Instance != null
               && GameManager.Instance.InstallationPlacementActive
               && activeInstallDefinition != null;
    }

    private bool IsInstallGridModeActive()
    {
        return IsInstallationModeActive() || mapEditModeActive;
    }

    private void InvalidateInstallGrid()
    {
        installGridRefreshTimer = 0f;
    }

    private bool TryGetInstallPreviewAtBlock(Block block, out MapObject preview)
    {
        preview = null;
        if (block == null)
        {
            return false;
        }

        return TryGetInstallPreviewAtCoordinate(block.Coordinate, out preview);
    }

    private bool TryGetBlockForPreview(MapObject preview, out Block block)
    {
        block = null;
        if (preview == null)
        {
            return false;
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null)
        {
            return false;
        }

        if (!TryGetPreviewAnchorCoordinate(preview, out Vector2Int coordinate))
        {
            return false;
        }

        return terrain.TryGetLoadedBlock(coordinate, out block) && block != null;
    }

    private bool CanPlacePreviewOnBlock(Block block, MapObject previewToIgnore = null, int? quarterTurnsOverride = null)
    {
        if (block == null)
        {
            return false;
        }

        int quarterTurns = quarterTurnsOverride ?? (previewToIgnore != null ? GetPreviewQuarterTurns(previewToIgnore) : installPreviewQuarterTurns);
        MapObject footprintSource = previewToIgnore != null
            ? previewToIgnore
            : (activeInstallDefinition != null ? activeInstallDefinition.mapObject : null);
        if (activeInstallDefinition != null && activeInstallDefinition.mapObject is ConveyorBelt)
        {
            ConveyorPreviewVariantMode previewVariantMode = previewToIgnore == activeInstallPreview
                ? installPreviewConveyorVariantMode
                : GetConveyorPreviewVariantMode(previewToIgnore);
            footprintSource = ResolveInstalledObjectSourcePrefab(
                                  activeInstallDefinition,
                                  block.Coordinate,
                                  quarterTurns,
                                  previewToIgnore,
                                  previewVariantMode)
                              ?? footprintSource;
        }

        if (footprintSource == null)
        {
            return false;
        }

        if (!TryGetFootprintBlocks(block.Coordinate, footprintSource, quarterTurns, previewToIgnore, out _))
        {
            return CanPlaceStraightConveyorPreviewOnBlock(block, previewToIgnore, quarterTurns);
        }

        if (activeInstallDefinition == null || !(activeInstallDefinition.mapObject is ConveyorBelt conveyorPrototype))
        {
            return true;
        }

        ConveyorBelt cornerConveyor = footprintSource is ConveyorBelt footprintConveyor && footprintConveyor.IsCornerVariant
            ? footprintConveyor
            : null;

        if (cornerConveyor == null)
        {
            return conveyorPrototype != null;
        }

        return HasConveyorEndpointConnectionAtCoordinate(
            cornerConveyor,
            block.Coordinate,
            quarterTurns,
            previewToIgnore);
    }

    private bool CanPlaceStraightConveyorPreviewOnBlock(Block block, MapObject previewToIgnore, int quarterTurns)
    {
        if (block == null
            || !CanUseStraightConveyorFallback(previewToIgnore)
            || !TryResolveStraightConveyorPlacementSource(out MapObject sourcePrefab))
        {
            return false;
        }

        return TryGetFootprintBlocks(
            block.Coordinate,
            sourcePrefab,
            NormalizePlacementQuarterTurns(quarterTurns),
            previewToIgnore,
            out _);
    }

    private bool CanUseStraightConveyorFallback(MapObject preview)
    {
        if (activeInstallDefinition == null || !(activeInstallDefinition.mapObject is ConveyorBelt))
        {
            return false;
        }

        if (preview == null)
        {
            return true;
        }

        if (preview == activeInstallPreview && installPreviewConveyorVariantMode == ConveyorPreviewVariantMode.Straight)
        {
            return true;
        }

        return preview is ConveyorBelt conveyorPreview && !conveyorPreview.IsCornerVariant;
    }

    private bool TryResolveStraightConveyorPlacementSource(out MapObject sourcePrefab)
    {
        sourcePrefab = null;
        if (activeInstallDefinition == null || !(activeInstallDefinition.mapObject is ConveyorBelt conveyorPrototype))
        {
            return false;
        }

        sourcePrefab = ResolveConveyorVariantPrefab(conveyorPrototype, 0) ?? conveyorPrototype;
        return sourcePrefab is ConveyorBelt straightConveyor && !straightConveyor.IsCornerVariant;
    }

    private bool TryFindPlaceableInstallPreviewQuarterTurns(
        Block block,
        MapObject previewToIgnore,
        int preferredQuarterTurns,
        out int resolvedQuarterTurns)
    {
        resolvedQuarterTurns = NormalizePlacementQuarterTurns(preferredQuarterTurns);
        if (block == null)
        {
            return false;
        }

        if (IsConveyorInstallPreview(previewToIgnore)
            && TryResolveConveyorEndpointAlignedQuarterTurns(
                block.Coordinate,
                previewToIgnore,
                resolvedQuarterTurns,
                out int endpointAlignedQuarterTurns))
        {
            if (CanPlacePreviewOnBlock(block, previewToIgnore, endpointAlignedQuarterTurns)
                || CanPlaceStraightConveyorPreviewOnBlock(block, previewToIgnore, endpointAlignedQuarterTurns))
            {
                resolvedQuarterTurns = endpointAlignedQuarterTurns;
                return true;
            }
        }

        if (CanPlacePreviewOnBlock(block, previewToIgnore, resolvedQuarterTurns))
        {
            return true;
        }

        if (IsConveyorInstallPreview(previewToIgnore))
        {
            if (CanPlaceStraightConveyorPreviewOnBlock(block, previewToIgnore, resolvedQuarterTurns))
            {
                return true;
            }

            return false;
        }

        for (int offset = 1; offset < 4; offset++)
        {
            int candidateQuarterTurns = (resolvedQuarterTurns + offset) % 4;
            if (!CanPlacePreviewOnBlock(block, previewToIgnore, candidateQuarterTurns))
            {
                continue;
            }

            resolvedQuarterTurns = candidateQuarterTurns;
            return true;
        }

        return false;
    }

    private bool IsConveyorInstallPreview(MapObject previewToIgnore)
    {
        if (previewToIgnore is ConveyorBelt)
        {
            return true;
        }

        return activeInstallDefinition != null && activeInstallDefinition.mapObject is ConveyorBelt;
    }

    private bool TryGetInstallPreviewAtCoordinate(Vector2Int coordinate, out MapObject preview)
    {
        preview = null;
        CleanupInstallPreviewReferences();
        for (int i = 0; i < installPreviewInstances.Count; i++)
        {
            MapObject candidate = installPreviewInstances[i];
            if (candidate == null || !TryGetPreviewAnchorCoordinate(candidate, out Vector2Int anchorCoordinate))
            {
                continue;
            }

            List<Vector2Int> occupiedCoordinates = GetFootprintCoordinates(anchorCoordinate, candidate, GetPreviewQuarterTurns(candidate));
            for (int coordinateIndex = 0; coordinateIndex < occupiedCoordinates.Count; coordinateIndex++)
            {
                if (occupiedCoordinates[coordinateIndex] != coordinate)
                {
                    continue;
                }

                preview = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryGetPreviewAnchorCoordinate(MapObject preview, out Vector2Int anchorCoordinate)
    {
        anchorCoordinate = Vector2Int.zero;
        return preview != null && installPreviewAnchorCoordinates.TryGetValue(preview, out anchorCoordinate);
    }

    private MapObject ResolveInstallPreviewFootprintSource(MapObject preview)
    {
        if (preview != null
            && installPreviewSourcePrefabsByPreview.TryGetValue(preview, out MapObject sourcePrefab)
            && sourcePrefab != null)
        {
            return sourcePrefab;
        }

        return preview;
    }

    private bool CanOverlapCompatibleInstallPreview(
        Vector2Int coordinate,
        MapObject candidateFootprintSource,
        InputOutputModule.RectGridBlockType candidateBlockType,
        Vector2Int candidateAnchorCoordinate,
        int candidateQuarterTurns,
        MapObject existingPreview)
    {
        if (existingPreview == null
            || !TryGetPreviewAnchorCoordinate(existingPreview, out Vector2Int existingAnchorCoordinate))
        {
            return false;
        }

        MapObject existingFootprintSource = ResolveInstallPreviewFootprintSource(existingPreview);
        int existingQuarterTurns = GetPreviewQuarterTurns(existingPreview);
        return CanOverlapCompatiblePlacementItemAreas(
            coordinate,
            candidateFootprintSource,
            candidateBlockType,
            candidateAnchorCoordinate,
            candidateQuarterTurns,
            existingFootprintSource,
            GetRectGridBlockTypeAtCoordinate(
                existingAnchorCoordinate,
                existingFootprintSource,
                existingQuarterTurns,
                coordinate),
            existingAnchorCoordinate,
            existingQuarterTurns);
    }

    private Vector2Int GetFootprintSize(MapObject footprintSource, int quarterTurns)
    {
        List<Vector2Int> offsets = GetFootprintLocalOffsets(footprintSource, quarterTurns);
        if (offsets.Count <= 0)
        {
            return Vector2Int.one;
        }

        int minX = offsets[0].x;
        int maxX = offsets[0].x;
        int minY = offsets[0].y;
        int maxY = offsets[0].y;

        for (int i = 1; i < offsets.Count; i++)
        {
            Vector2Int offset = offsets[i];
            minX = Mathf.Min(minX, offset.x);
            maxX = Mathf.Max(maxX, offset.x);
            minY = Mathf.Min(minY, offset.y);
            maxY = Mathf.Max(maxY, offset.y);
        }

        return new Vector2Int(maxX - minX + 1, maxY - minY + 1);
    }

    private List<Vector2Int> GetFootprintCoordinates(Vector2Int anchorCoordinate, MapObject footprintSource, int quarterTurns)
    {
        List<Vector2Int> offsets = GetFootprintLocalOffsets(footprintSource, quarterTurns);
        List<Vector2Int> coordinates = new List<Vector2Int>(offsets.Count);
        for (int i = 0; i < offsets.Count; i++)
        {
            coordinates.Add(anchorCoordinate + offsets[i]);
        }

        return coordinates;
    }

    private List<Vector2Int> GetFootprintLocalOffsets(MapObject footprintSource, int quarterTurns)
    {
        if (TryGetRectGridFootprintSettings(footprintSource, out int rectGridWidth, out int rectGridHeight, out Vector2Int objectAnchorCell)
            && TryGetInputOutputModule(footprintSource, out InputOutputModule inputOutputModule))
        {
            IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements = inputOutputModule.RectGridPlacements;
            List<Vector2Int> rectGridOffsets = new List<Vector2Int>(placements.Count);
            HashSet<Vector2Int> occupiedOffsets = new HashSet<Vector2Int>();

            for (int i = 0; i < placements.Count; i++)
            {
                InputOutputModule.RectGridBlockPlacement placement = placements[i];
                if (placement.blockType == InputOutputModule.RectGridBlockType.None
                    || placement.x < 0
                    || placement.x >= rectGridWidth
                    || placement.y < 0
                    || placement.y >= rectGridHeight)
                {
                    continue;
                }

                Vector2Int localOffset = new Vector2Int(placement.x - objectAnchorCell.x, placement.y - objectAnchorCell.y);
                Vector2Int rotatedOffset = RotateFootprintOffset(localOffset, quarterTurns);
                if (occupiedOffsets.Add(rotatedOffset))
                {
                    rectGridOffsets.Add(rotatedOffset);
                }
            }

            if (rectGridOffsets.Count > 0)
            {
                return rectGridOffsets;
            }
        }

        int sizeX = 1;
        int sizeY = 1;
        if (footprintSource != null)
        {
            sizeX = Mathf.Max(1, footprintSource.Status.mapSizeX);
            sizeY = Mathf.Max(1, footprintSource.Status.mapSizeY);
        }

        List<Vector2Int> offsets = new List<Vector2Int>(sizeX * sizeY);
        for (int y = 0; y < sizeY; y++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                offsets.Add(RotateFootprintOffset(new Vector2Int(x, y), quarterTurns));
            }
        }

        return offsets;
    }

    private bool TryGetRectGridFootprintSettings(
        MapObject footprintSource,
        out int rectGridWidth,
        out int rectGridHeight,
        out Vector2Int objectAnchorCell)
    {
        rectGridWidth = 0;
        rectGridHeight = 0;
        objectAnchorCell = Vector2Int.zero;

        if (footprintSource == null)
        {
            return false;
        }

        InputOutputModule inputOutputModule = footprintSource as InputOutputModule;
        if (inputOutputModule == null)
        {
            inputOutputModule = footprintSource.GetComponent<InputOutputModule>();
        }

        if (inputOutputModule == null)
        {
            inputOutputModule = footprintSource.GetComponentInChildren<InputOutputModule>(true);
        }

        if (inputOutputModule == null || inputOutputModule.LayoutType != InputOutputModule.SlotLayoutType.RectGrid)
        {
            return false;
        }

        rectGridWidth = Mathf.Max(1, inputOutputModule.RectGridWidth);
        rectGridHeight = Mathf.Max(1, inputOutputModule.RectGridHeight);
        return TryGetRectGridObjectAnchorCell(inputOutputModule, out objectAnchorCell);
    }

    private bool TryGetRectGridObjectAnchorCell(InputOutputModule inputOutputModule, out Vector2Int objectAnchorCell)
    {
        objectAnchorCell = Vector2Int.zero;
        if (inputOutputModule == null)
        {
            return false;
        }

        return inputOutputModule.TryGetPrimaryObjectCell(out objectAnchorCell);
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

    public bool CanPlaceInstalledObjectAt(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns,
        MapObject previewToIgnore = null,
        bool ignoreOtherPreviews = false)
    {
        return TryGetFootprintBlocks(
            anchorCoordinate,
            footprintSource,
            quarterTurns,
            previewToIgnore,
            out _,
            ignoreOtherPreviews);
    }

    private bool TryGetFootprintBlocks(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns,
        MapObject previewToIgnore,
        out List<Block> footprintBlocks,
        bool ignoreOtherPreviews = false)
    {
        footprintBlocks = new List<Block>();

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null)
        {
            return false;
        }

        List<Vector2Int> occupiedCoordinates = GetFootprintCoordinates(anchorCoordinate, footprintSource, quarterTurns);
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = occupiedCoordinates[i];
            if (!terrain.TryGetLoadedBlock(coordinate, out Block footprintBlock) || footprintBlock == null)
            {
                return false;
            }

            TryGetRectGridFootprintBlockType(
                anchorCoordinate,
                footprintSource,
                quarterTurns,
                coordinate,
                out InputOutputModule.RectGridBlockType rectGridBlockType);
            if (!CanPlacePreviewOnTargetBlockType(
                    footprintBlock,
                    footprintSource,
                    rectGridBlockType,
                    anchorCoordinate,
                    quarterTurns))
            {
                return false;
            }

            if (!ignoreOtherPreviews
                && TryGetInstallPreviewAtCoordinate(coordinate, out MapObject existingPreview)
                && existingPreview != null
                && existingPreview != previewToIgnore
                && !CanOverlapCompatibleInstallPreview(
                    coordinate,
                    footprintSource,
                    rectGridBlockType,
                    anchorCoordinate,
                    quarterTurns,
                    existingPreview))
            {
                return false;
            }

            footprintBlocks.Add(footprintBlock);
        }

        return footprintBlocks.Count > 0;
    }

    private bool TryGetRectGridFootprintBlockType(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns,
        Vector2Int coordinate,
        out InputOutputModule.RectGridBlockType blockType)
    {
        blockType = InputOutputModule.RectGridBlockType.None;
        if (!TryGetRectGridFootprintSettings(footprintSource, out int rectGridWidth, out int rectGridHeight, out Vector2Int objectAnchorCell)
            || !TryGetInputOutputModule(footprintSource, out InputOutputModule inputOutputModule))
        {
            return false;
        }

        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements = inputOutputModule.RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            InputOutputModule.RectGridBlockPlacement placement = placements[i];
            if (placement.blockType == InputOutputModule.RectGridBlockType.None
                || placement.x < 0
                || placement.x >= rectGridWidth
                || placement.y < 0
                || placement.y >= rectGridHeight)
            {
                continue;
            }

            Vector2Int localOffset = new Vector2Int(placement.x - objectAnchorCell.x, placement.y - objectAnchorCell.y);
            Vector2Int rotatedCoordinate = anchorCoordinate + RotateFootprintOffset(localOffset, quarterTurns);
            if (rotatedCoordinate != coordinate)
            {
                continue;
            }

            blockType = placement.blockType;
            return true;
        }

        return false;
    }

    private InputOutputModule.RectGridBlockType GetRectGridBlockTypeAtCoordinate(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns,
        Vector2Int coordinate)
    {
        return TryGetRectGridFootprintBlockType(
                anchorCoordinate,
                footprintSource,
                quarterTurns,
                coordinate,
                out InputOutputModule.RectGridBlockType blockType)
            ? blockType
            : InputOutputModule.RectGridBlockType.None;
    }

    private bool ShouldBindInstalledObjectToBlock(
        Block block,
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns)
    {
        if (block == null)
        {
            return false;
        }

        if (!TryGetRectGridFootprintSettings(footprintSource, out _, out _, out _)
            || !TryGetInputOutputModule(footprintSource, out _))
        {
            return true;
        }

        return TryGetRectGridFootprintBlockType(
                anchorCoordinate,
                footprintSource,
                quarterTurns,
                block.Coordinate,
                out InputOutputModule.RectGridBlockType blockType)
            && blockType == InputOutputModule.RectGridBlockType.Object;
    }

    private bool CanPlacePreviewOnTargetBlockType(
        Block block,
        MapObject footprintSource,
        InputOutputModule.RectGridBlockType rectGridBlockType = InputOutputModule.RectGridBlockType.None,
        Vector2Int? anchorCoordinate = null,
        int quarterTurns = 0)
    {
        if (block == null)
        {
            return false;
        }

        MapObject occupyingObject = GetBlockingMapObject(block);

        if (!TryResolveInstallationObject(footprintSource, out InstallationObject installationObject))
        {
            return block.Type == Block.BlockType.Ground && occupyingObject == null;
        }

        InstallationMapFilter allowedFilter = installationObject.MapFilter;
        bool isInputOutputEnergyAreaBlock = InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(block.Coordinate)
            || InputOutputModule.CoordinateIsRuntimeRectGridBlockType(block.Coordinate, InputOutputModule.RectGridBlockType.InputEnergy);
        bool isInputOutputItemAreaBlock = InputOutputModuleItemAreaController.CoordinateIsItemArea(block.Coordinate)
            || InputOutputModule.CoordinateIsRuntimeRectGridBlockType(block.Coordinate, InputOutputModule.RectGridBlockType.InputItem);
        bool isInputOutputOutputAreaBlock = InputOutputModuleOutputAreaController.CoordinateIsOutputArea(block.Coordinate)
            || InputOutputModule.CoordinateIsRuntimeRectGridBlockType(block.Coordinate, InputOutputModule.RectGridBlockType.Output);
        bool isInputOutputAreaBlock = isInputOutputEnergyAreaBlock || isInputOutputItemAreaBlock || isInputOutputOutputAreaBlock;

        if (IsRectGridAreaBlockType(rectGridBlockType))
        {
            return CanPlaceRectGridAreaBlock(
                block,
                occupyingObject,
                footprintSource,
                rectGridBlockType,
                anchorCoordinate ?? block.Coordinate,
                quarterTurns,
                isInputOutputEnergyAreaBlock,
                isInputOutputItemAreaBlock,
                isInputOutputOutputAreaBlock);
        }

        if (isInputOutputAreaBlock)
        {
            return CanPlaceBoxOnInputOutputAreaBlock(
                installationObject,
                block,
                occupyingObject,
                isInputOutputItemAreaBlock);
        }

        if (occupyingObject is Resource resource)
        {
            return IsResourceAllowedByMapFilter(resource, allowedFilter);
        }

        if (occupyingObject != null)
        {
            return false;
        }

        return block.Type switch
        {
            Block.BlockType.Ground => (allowedFilter & InstallationMapFilter.Ground) != 0,
            _ => false
        };
    }

    private bool CanPlaceRectGridAreaBlock(
        Block block,
        MapObject occupyingObject,
        MapObject footprintSource,
        InputOutputModule.RectGridBlockType rectGridBlockType,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        bool hasExistingInputOutputEnergyAreaBlock,
        bool hasExistingInputOutputItemAreaBlock,
        bool hasExistingInputOutputOutputAreaBlock)
    {
        if (block == null || block.Type != Block.BlockType.Ground)
        {
            return false;
        }

        bool hasExistingInputOutputAreaBlock =
            hasExistingInputOutputEnergyAreaBlock
            || hasExistingInputOutputItemAreaBlock
            || hasExistingInputOutputOutputAreaBlock;
        if (hasExistingInputOutputAreaBlock
            && !CanOverlapCompatibleItemArea(
                block.Coordinate,
                footprintSource,
                rectGridBlockType,
                anchorCoordinate,
                quarterTurns,
                hasExistingInputOutputEnergyAreaBlock,
                hasExistingInputOutputItemAreaBlock,
                hasExistingInputOutputOutputAreaBlock))
        {
            return false;
        }

        if (occupyingObject == null || occupyingObject is BoxObject)
        {
            return true;
        }

        return occupyingObject is Resource resource
            && IsResourceAllowedByMapFilter(resource, InstallationMapFilter.Ore);
    }

    private bool CanOverlapCompatibleItemArea(
        Vector2Int coordinate,
        MapObject footprintSource,
        InputOutputModule.RectGridBlockType rectGridBlockType,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        bool hasExistingInputOutputEnergyAreaBlock,
        bool hasExistingInputOutputItemAreaBlock,
        bool hasExistingInputOutputOutputAreaBlock)
    {
        switch (rectGridBlockType)
        {
            case InputOutputModule.RectGridBlockType.InputItem:
                return hasExistingInputOutputOutputAreaBlock
                    && CandidateInputItemsMatchExistingOutput(
                        coordinate,
                        footprintSource,
                        anchorCoordinate,
                        quarterTurns);

            case InputOutputModule.RectGridBlockType.InputEnergy:
                return hasExistingInputOutputOutputAreaBlock
                    && CandidateInputEnergyMatchesExistingOutput(coordinate, footprintSource);

            case InputOutputModule.RectGridBlockType.Output:
                return CandidateOutputMatchesExistingInputAreas(
                    coordinate,
                    footprintSource,
                    anchorCoordinate,
                    quarterTurns,
                    hasExistingInputOutputItemAreaBlock,
                    hasExistingInputOutputEnergyAreaBlock);

            default:
                return false;
        }
    }

    private bool CandidateInputItemsMatchExistingOutput(
        Vector2Int coordinate,
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns)
    {
        HashSet<int> candidateInputItemIds = new HashSet<int>();
        HashSet<int> existingOutputItemIds = new HashSet<int>();
        return TryGetCandidateInputItemIdsAtCoordinate(
                anchorCoordinate,
                footprintSource,
                quarterTurns,
                coordinate,
                candidateInputItemIds)
            && TryGetExistingOutputItemIdsAtCoordinate(coordinate, existingOutputItemIds)
            && HasSharedItemId(candidateInputItemIds, existingOutputItemIds);
    }

    private bool CandidateInputEnergyMatchesExistingOutput(Vector2Int coordinate, MapObject footprintSource)
    {
        HashSet<ItemDefinition.EnergyType> candidateInputEnergyTypes = new HashSet<ItemDefinition.EnergyType>();
        HashSet<int> existingOutputItemIds = new HashSet<int>();
        return TryGetCandidateInputEnergyTypes(footprintSource, candidateInputEnergyTypes)
            && TryGetExistingOutputItemIdsAtCoordinate(coordinate, existingOutputItemIds)
            && HasOutputItemWithEnergyType(existingOutputItemIds, candidateInputEnergyTypes);
    }

    private bool CandidateOutputMatchesExistingInputAreas(
        Vector2Int coordinate,
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        bool hasExistingInputOutputItemAreaBlock,
        bool hasExistingInputOutputEnergyAreaBlock)
    {
        if (!hasExistingInputOutputItemAreaBlock && !hasExistingInputOutputEnergyAreaBlock)
        {
            return false;
        }

        HashSet<int> candidateOutputItemIds = new HashSet<int>();
        if (!TryGetCandidateOutputItemIds(anchorCoordinate, footprintSource, candidateOutputItemIds))
        {
            return false;
        }

        return (hasExistingInputOutputItemAreaBlock
                && ExistingInputItemsMatchOutput(coordinate, candidateOutputItemIds))
            || (hasExistingInputOutputEnergyAreaBlock
                && ExistingInputEnergyMatchesOutput(coordinate, candidateOutputItemIds));
    }

    private static bool ExistingInputItemsMatchOutput(Vector2Int coordinate, ISet<int> outputItemIds)
    {
        HashSet<int> existingInputItemIds = new HashSet<int>();
        return TryGetExistingInputItemIdsAtCoordinate(coordinate, existingInputItemIds)
            && HasSharedItemId(outputItemIds, existingInputItemIds);
    }

    private static bool ExistingInputEnergyMatchesOutput(Vector2Int coordinate, ISet<int> outputItemIds)
    {
        HashSet<ItemDefinition.EnergyType> existingInputEnergyTypes = new HashSet<ItemDefinition.EnergyType>();
        return TryGetExistingInputEnergyTypesAtCoordinate(coordinate, existingInputEnergyTypes)
            && HasOutputItemWithEnergyType(outputItemIds, existingInputEnergyTypes);
    }

    private bool CanOverlapCompatiblePlacementItemAreas(
        Vector2Int coordinate,
        MapObject candidateFootprintSource,
        InputOutputModule.RectGridBlockType candidateBlockType,
        Vector2Int candidateAnchorCoordinate,
        int candidateQuarterTurns,
        MapObject existingFootprintSource,
        InputOutputModule.RectGridBlockType existingBlockType,
        Vector2Int existingAnchorCoordinate,
        int existingQuarterTurns)
    {
        if (candidateBlockType == InputOutputModule.RectGridBlockType.Output)
        {
            HashSet<int> candidateOutputItemIds = new HashSet<int>();
            if (!TryGetCandidateOutputItemIds(
                    candidateAnchorCoordinate,
                    candidateFootprintSource,
                    candidateOutputItemIds))
            {
                return false;
            }

            return PlacementInputAreaMatchesOutput(
                coordinate,
                existingFootprintSource,
                existingBlockType,
                existingAnchorCoordinate,
                existingQuarterTurns,
                candidateOutputItemIds);
        }

        if (existingBlockType != InputOutputModule.RectGridBlockType.Output)
        {
            return false;
        }

        HashSet<int> existingOutputItemIds = new HashSet<int>();
        if (!TryGetCandidateOutputItemIds(
                existingAnchorCoordinate,
                existingFootprintSource,
                existingOutputItemIds))
        {
            return false;
        }

        return PlacementInputAreaMatchesOutput(
            coordinate,
            candidateFootprintSource,
            candidateBlockType,
            candidateAnchorCoordinate,
            candidateQuarterTurns,
            existingOutputItemIds);
    }

    private bool PlacementInputAreaMatchesOutput(
        Vector2Int coordinate,
        MapObject inputFootprintSource,
        InputOutputModule.RectGridBlockType inputBlockType,
        Vector2Int inputAnchorCoordinate,
        int inputQuarterTurns,
        ISet<int> outputItemIds)
    {
        if (inputBlockType == InputOutputModule.RectGridBlockType.InputItem)
        {
            HashSet<int> inputItemIds = new HashSet<int>();
            return TryGetCandidateInputItemIdsAtCoordinate(
                    inputAnchorCoordinate,
                    inputFootprintSource,
                    inputQuarterTurns,
                    coordinate,
                    inputItemIds)
                && HasSharedItemId(outputItemIds, inputItemIds);
        }

        if (inputBlockType == InputOutputModule.RectGridBlockType.InputEnergy)
        {
            HashSet<ItemDefinition.EnergyType> inputEnergyTypes = new HashSet<ItemDefinition.EnergyType>();
            return TryGetCandidateInputEnergyTypes(inputFootprintSource, inputEnergyTypes)
                && HasOutputItemWithEnergyType(outputItemIds, inputEnergyTypes);
        }

        return false;
    }

    private bool TryGetCandidateInputItemIdsAtCoordinate(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns,
        Vector2Int coordinate,
        ISet<int> inputItemIds)
    {
        if (inputItemIds == null
            || !TryGetInputOutputModule(footprintSource, out InputOutputModule inputOutputModule)
            || !TryGetOrderedInputItemAreaBindings(
                anchorCoordinate,
                footprintSource,
                quarterTurns,
                inputOutputModule,
                out List<InputOutputModuleItemAreaBinding> bindings))
        {
            return false;
        }

        bool foundAny = false;
        for (int i = 0; i < bindings.Count; i++)
        {
            InputOutputModuleItemAreaBinding binding = bindings[i];
            if (binding.Coordinate != coordinate || binding.ItemId < 0)
            {
                continue;
            }

            inputItemIds.Add(binding.ItemId);
            foundAny = true;
        }

        return foundAny;
    }

    private bool TryGetCandidateOutputItemIds(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        ISet<int> outputItemIds)
    {
        if (outputItemIds == null || !TryGetInputOutputModule(footprintSource, out InputOutputModule inputOutputModule))
        {
            return false;
        }

        bool foundAny = false;
        IReadOnlyList<InputOutputModule.ItemIoEntry> outputList = inputOutputModule.OutputList;
        if (outputList != null)
        {
            for (int i = 0; i < outputList.Count; i++)
            {
                ItemDefinition itemDefinition = outputList[i].itemDefinition;
                if (itemDefinition == null || itemDefinition.id < 0)
                {
                    continue;
                }

                outputItemIds.Add(itemDefinition.id);
                foundAny = true;
            }
        }

        if (TryGetMiningMachine(footprintSource, out MiningMachine miningMachine))
        {
            foundAny |= miningMachine.TryAppendPlacementOutputItemIds(
                ResolveInstallPreviewTerrain(),
                anchorCoordinate,
                outputItemIds);
        }

        return foundAny;
    }

    private bool TryGetCandidateInputEnergyTypes(
        MapObject footprintSource,
        ISet<ItemDefinition.EnergyType> energyTypes)
    {
        if (energyTypes == null)
        {
            return false;
        }

        ItemDefinition installationDefinition = ResolveItemDefinition(footprintSource);
        if (installationDefinition == null
            || installationDefinition.useEnergyType == ItemDefinition.EnergyType.None)
        {
            return false;
        }

        energyTypes.Add(installationDefinition.useEnergyType);
        return true;
    }

    private static bool TryGetExistingOutputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> outputItemIds)
    {
        return InputOutputModule.TryGetOutputItemIdsAtRuntimeGridCoordinate(coordinate, outputItemIds);
    }

    private static bool TryGetExistingInputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> inputItemIds)
    {
        if (inputItemIds == null)
        {
            return false;
        }

        bool foundAny = InputOutputModuleItemAreaController.TryGetAcceptedItemIds(coordinate, inputItemIds);
        foundAny |= InputOutputModule.TryGetInputItemIdsAtRuntimeGridCoordinate(coordinate, inputItemIds);
        return foundAny;
    }

    private static bool TryGetExistingInputEnergyTypesAtCoordinate(
        Vector2Int coordinate,
        ISet<ItemDefinition.EnergyType> energyTypes)
    {
        if (energyTypes == null)
        {
            return false;
        }

        bool foundAny = InputOutputModuleEnergyAreaController.TryGetAcceptedEnergyTypes(coordinate, energyTypes);
        foundAny |= InputOutputModule.TryGetInputEnergyTypesAtRuntimeGridCoordinate(coordinate, energyTypes);
        return foundAny;
    }

    private static bool HasSharedItemId(ISet<int> leftItemIds, ISet<int> rightItemIds)
    {
        if (leftItemIds == null || rightItemIds == null || leftItemIds.Count <= 0 || rightItemIds.Count <= 0)
        {
            return false;
        }

        foreach (int itemId in leftItemIds)
        {
            if (itemId >= 0 && rightItemIds.Contains(itemId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOutputItemWithEnergyType(
        ISet<int> outputItemIds,
        ISet<ItemDefinition.EnergyType> acceptedEnergyTypes)
    {
        if (outputItemIds == null
            || acceptedEnergyTypes == null
            || outputItemIds.Count <= 0
            || acceptedEnergyTypes.Count <= 0)
        {
            return false;
        }

        foreach (int outputItemId in outputItemIds)
        {
            ItemDefinition outputDefinition = ResolveItemDefinition(outputItemId);
            if (outputDefinition == null
                || outputDefinition.energyType == ItemDefinition.EnergyType.None
                || outputDefinition.energyAmount <= 0)
            {
                continue;
            }

            if (acceptedEnergyTypes.Contains(outputDefinition.energyType))
            {
                return true;
            }
        }

        return false;
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

    private static bool CanPlaceBoxOnInputOutputAreaBlock(
        InstallationObject installationObject,
        Block block,
        MapObject occupyingObject,
        bool isInputOutputItemAreaBlock)
    {
        if (!(installationObject is BoxObject) || block == null || block.Type != Block.BlockType.Ground)
        {
            return false;
        }

        if (occupyingObject == null || occupyingObject is InputOutputModule)
        {
            return true;
        }

        return isInputOutputItemAreaBlock
            && occupyingObject is Resource resource
            && IsResourceAllowedByMapFilter(resource, InstallationMapFilter.Ore);
    }

    private static bool IsResourceAllowedByMapFilter(Resource resource, InstallationMapFilter allowedFilter)
    {
        if (resource == null || !resource.CanHarvest)
        {
            return false;
        }

        InstallationMapFilter resourceFilter = resource.ResolvedHarvestMode == Resource.HarvestMode.Logging
            ? InstallationMapFilter.Tree
            : InstallationMapFilter.Ore;
        return (allowedFilter & resourceFilter) != 0;
    }

    private static bool IsRectGridAreaBlockType(InputOutputModule.RectGridBlockType blockType)
    {
        return blockType == InputOutputModule.RectGridBlockType.InputEnergy
            || blockType == InputOutputModule.RectGridBlockType.InputItem
            || blockType == InputOutputModule.RectGridBlockType.Output;
    }

    private MapObject GetBlockingMapObject(Block block)
    {
        if (block == null)
        {
            return null;
        }

        MapObject occupyingObject = block.MapObject;
        if (occupyingObject == null)
        {
            return null;
        }

        if (!occupyingObject.gameObject.activeInHierarchy)
        {
            block.SetMapObject(null);
            return null;
        }

        if (occupyingObject is Resource resource && !resource.CanHarvest)
        {
            block.SetMapObject(null);
            return null;
        }

        return occupyingObject;
    }

    private Vector3 GetPreviewWorldPosition(Block anchorBlock, MapObject footprintSource, int quarterTurns, float verticalOffset)
    {
        Vector3 position = anchorBlock.transform.position;
        List<Vector2Int> offsets = GetPlacementVisualLocalOffsets(footprintSource, quarterTurns);
        if (offsets.Count > 0)
        {
            Vector2 averageOffset = Vector2.zero;
            for (int i = 0; i < offsets.Count; i++)
            {
                averageOffset += offsets[i];
            }

            averageOffset /= offsets.Count;
            position.x += averageOffset.x;
            position.z += averageOffset.y;
        }

        position.y += verticalOffset;
        return position;
    }

    private Vector3 GetPlacementWorldPositionFromAnchorCoordinate(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns,
        float verticalOffset)
    {
        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        float baseHeight = terrain != null ? terrain.transform.position.y : 0f;
        Vector3 position = new Vector3(anchorCoordinate.x, baseHeight, anchorCoordinate.y);
        if (terrain != null && terrain.TryGetLoadedBlock(anchorCoordinate, out Block anchorBlock) && anchorBlock != null)
        {
            position = anchorBlock.transform.position;
        }

        List<Vector2Int> offsets = GetPlacementVisualLocalOffsets(footprintSource, quarterTurns);
        if (offsets.Count > 0)
        {
            Vector2 averageOffset = Vector2.zero;
            for (int i = 0; i < offsets.Count; i++)
            {
                averageOffset += offsets[i];
            }

            averageOffset /= offsets.Count;
            position.x += averageOffset.x;
            position.z += averageOffset.y;
        }

        position.y += verticalOffset;
        return position;
    }

    private List<Vector2Int> GetPlacementVisualLocalOffsets(MapObject footprintSource, int quarterTurns)
    {
        if (TryGetRectGridObjectLocalOffsets(footprintSource, quarterTurns, out List<Vector2Int> objectOffsets)
            && objectOffsets.Count > 0)
        {
            return objectOffsets;
        }

        return GetFootprintLocalOffsets(footprintSource, quarterTurns);
    }

    private bool TryGetRectGridObjectLocalOffsets(MapObject footprintSource, int quarterTurns, out List<Vector2Int> objectOffsets)
    {
        objectOffsets = null;
        if (!TryGetRectGridFootprintSettings(footprintSource, out _, out _, out Vector2Int objectAnchorCell))
        {
            return false;
        }

        InputOutputModule inputOutputModule = footprintSource as InputOutputModule;
        if (inputOutputModule == null)
        {
            inputOutputModule = footprintSource.GetComponent<InputOutputModule>();
        }

        if (inputOutputModule == null)
        {
            inputOutputModule = footprintSource.GetComponentInChildren<InputOutputModule>(true);
        }

        if (inputOutputModule == null)
        {
            return false;
        }

        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements = inputOutputModule.RectGridPlacements;
        objectOffsets = new List<Vector2Int>();

        for (int i = 0; i < placements.Count; i++)
        {
            InputOutputModule.RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != InputOutputModule.RectGridBlockType.Object)
            {
                continue;
            }

            Vector2Int localOffset = new Vector2Int(placement.x - objectAnchorCell.x, placement.y - objectAnchorCell.y);
            objectOffsets.Add(RotateFootprintOffset(localOffset, quarterTurns));
        }

        return objectOffsets.Count > 0;
    }

    private bool IsPreviewObjectCell(MapObject preview, Block block)
    {
        if (preview == null || block == null || !TryGetPreviewAnchorCoordinate(preview, out Vector2Int anchorCoordinate))
        {
            return false;
        }

        List<Vector2Int> visualCoordinates = GetPlacementVisualCoordinates(anchorCoordinate, preview, GetPreviewQuarterTurns(preview));
        for (int i = 0; i < visualCoordinates.Count; i++)
        {
            if (visualCoordinates[i] == block.Coordinate)
            {
                return true;
            }
        }

        return false;
    }

    private List<Vector2Int> GetPlacementVisualCoordinates(Vector2Int anchorCoordinate, MapObject footprintSource, int quarterTurns)
    {
        List<Vector2Int> offsets = GetPlacementVisualLocalOffsets(footprintSource, quarterTurns);
        List<Vector2Int> coordinates = new List<Vector2Int>(offsets.Count);
        for (int i = 0; i < offsets.Count; i++)
        {
            coordinates.Add(anchorCoordinate + offsets[i]);
        }

        return coordinates;
    }

    private void RegisterInstalledObjectPersistence(MapObject installedObject)
    {
        if (!(installedObject is InstallationObject installationObject))
        {
            return;
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        terrain?.RegisterLiveInstallationObject(installationObject);
    }

    private MapObject CreateInstalledObjectInstance(MapObject sourcePrefab, Transform parent, TerrainGenerator terrain)
    {
        if (sourcePrefab == null)
        {
            return null;
        }

        InstallationObject pooledObject = terrain != null
            ? terrain.CreateInstallationObject(sourcePrefab, parent)
            : null;
        return pooledObject != null ? pooledObject : Instantiate(sourcePrefab, parent);
    }

    private void ReleaseInstalledObjectInstance(InstallationObject installationObject, MapObject sourcePrefab, TerrainGenerator terrain)
    {
        if (installationObject == null)
        {
            return;
        }

        if (terrain != null)
        {
            terrain.ReleaseInstallationObject(installationObject, sourcePrefab);
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(installationObject.gameObject);
        }
        else
        {
            DestroyImmediate(installationObject.gameObject);
        }
    }

    private static MapObject ResolveInstallationSourcePrefab(ItemDefinition definition, int conveyorVariantKind)
    {
        MapObject sourcePrefab = definition != null ? definition.mapObject : null;
        if (sourcePrefab is ConveyorBelt conveyorPrototype && conveyorVariantKind >= 0)
        {
            sourcePrefab = ResolveConveyorVariantPrefab(conveyorPrototype, conveyorVariantKind)
                ?? sourcePrefab;
        }

        return sourcePrefab;
    }

    private void PlayInstallPlacementAnimation(MapObject installedObject, PortableObject sourcePortableObject, int itemId, float delay)
    {
        if (installedObject == null)
        {
            return;
        }

        Transform installedTransform = installedObject.transform;
        Vector3 originalScale = installedTransform.localScale;
        SetConveyorBeltVirtualRendering(installedObject, false);
        installedTransform.DOKill();
        installedTransform.localScale = Vector3.zero;
        SetInstalledObjectVisualVisible(installedObject, false);

        if (sourcePortableObject == null || itemId < 0)
        {
            RevealInstalledObjectAfterPlacement(installedObject, installedTransform, originalScale, delay);
            return;
        }

        PortableObject movingPortableObject = Instantiate(
            sourcePortableObject,
            sourcePortableObject.transform.position,
            sourcePortableObject.transform.rotation);
        if (movingPortableObject == null)
        {
            RevealInstalledObjectAfterPlacement(installedObject, installedTransform, originalScale, delay);
            return;
        }

        movingPortableObject.name = $"{sourcePortableObject.name}_InstallMove";
        movingPortableObject.transform.SetParent(null, true);
        movingPortableObject.transform.position = sourcePortableObject.transform.position;
        movingPortableObject.transform.localScale = sourcePortableObject.transform.lossyScale;
        if (!movingPortableObject.gameObject.activeSelf)
        {
            movingPortableObject.gameObject.SetActive(true);
        }

        if (!movingPortableObject.SetItem(itemId))
        {
            Destroy(movingPortableObject.gameObject);
            RevealInstalledObjectAfterPlacement(installedObject, installedTransform, originalScale, delay);
            return;
        }

        Vector3 targetPosition = installedTransform != null ? installedTransform.position : movingPortableObject.transform.position;
        movingPortableObject.MoveTo(targetPosition, Mathf.Max(0f, delay), () =>
        {
            if (movingPortableObject != null)
            {
                Destroy(movingPortableObject.gameObject);
            }

            if (installedObject != null && installedTransform != null)
            {
                RevealInstalledObjectAfterPlacement(installedObject, installedTransform, originalScale, 0f);
            }
        }, false);
    }

    private void RevealInstalledObjectAfterPlacement(
        MapObject installedObject,
        Transform installedTransform,
        Vector3 originalScale,
        float delay)
    {
        if (installedObject == null || installedTransform == null)
        {
            return;
        }

        SetInstalledObjectVisualVisible(installedObject, true);

        bool restoredVirtualRendering = false;
        TweenCallback restoreVirtualRendering = () =>
        {
            if (restoredVirtualRendering || installedObject == null)
            {
                return;
            }

            restoredVirtualRendering = true;
            SetConveyorBeltVirtualRendering(installedObject, true);
        };

        Tween revealTween = installedTransform
            .DOScale(originalScale, installPlacementScaleDuration)
            .SetDelay(Mathf.Max(0f, delay))
            .SetEase(installPlacementScaleEase)
            .SetLink(installedObject.gameObject);
        revealTween.OnComplete(restoreVirtualRendering);
        revealTween.OnKill(restoreVirtualRendering);
    }

    private static void SetConveyorBeltVirtualRendering(MapObject installedObject, bool isEnabled)
    {
        if (installedObject is ConveyorBelt conveyorBelt)
        {
            conveyorBelt.SetVirtualRuntimeRenderingEnabled(isEnabled);
        }
    }

    private void PlayInstallationEditCompleteAnimation(MapObject restoredObject, InstallationEditSession editSession)
    {
        if (restoredObject == null)
        {
            return;
        }

        int itemId = editSession?.definition != null
            ? editSession.definition.id
            : restoredObject.ResolveItemId();
        PortableObject sourcePortableObject = null;
        List<PortableObject> handPortableSources = GetPlayerHandPortableSources(itemId, 1);
        if (handPortableSources.Count > 0)
        {
            sourcePortableObject = handPortableSources[0];
        }

        PlayInstallPlacementAnimation(restoredObject, sourcePortableObject, itemId, 0f);
    }

    private void SetInstalledObjectVisualVisible(MapObject installedObject, bool isVisible)
    {
        if (installedObject == null)
        {
            return;
        }

        Renderer[] renderers = installedObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = isVisible;
            }
        }
    }

    private bool TryGetPrimaryPointerPosition(out Vector2 pointerPosition)
    {
        pointerPosition = Vector2.zero;

        if (Input.touchCount > 0)
        {
            pointerPosition = Input.GetTouch(0).position;
            return true;
        }

        pointerPosition = Input.mousePosition;
        return true;
    }

    private bool TryGetPrimaryPointerDown(out Vector2 pointerPosition)
    {
        pointerPosition = Vector2.zero;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                pointerPosition = touch.position;
                return true;
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            pointerPosition = Input.mousePosition;
            return true;
        }

        return false;
    }

    private bool IsPrimaryPointerHeld()
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            return touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
        }

        return Input.GetMouseButton(0);
    }

    private bool IsPointerOverBlockingUi(Vector2 pointerPosition)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = pointerPosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        for (int i = 0; i < results.Count; i++)
        {
            GameObject hitObject = results[i].gameObject;
            if (IsBlockingButtonObject(hitObject))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBlockingButtonObject(GameObject targetObject)
    {
        if (targetObject == null)
        {
            return false;
        }

        return targetObject.GetComponentInParent<Button>() != null;
    }

    private static int CompareRaycastHits(RaycastHit left, RaycastHit right)
    {
        return left.distance.CompareTo(right.distance);
    }

    private void ClearInstallPreview()
    {
        RefundAllInstallPreviewReservations();
        activeInstallDefinition = null;
        activeInstallBaseRotation = Quaternion.identity;
        waitForPointerReleaseAfterPreviewSpawn = false;
        installPreviewQuarterTurns = 0;
        installPreviewConveyorVariantMode = ConveyorPreviewVariantMode.Straight;
        ResetPreviewPointerTracking();
        GameManager.Instance?.SetInstallationPlacementActive(false);
        if (IsInstallGridModeActive())
        {
            InvalidateInstallGrid();
            SetInstallGridVisible(true);
        }
        else
        {
            SetInstallGridVisible(false);
            installGridRefreshTimer = 0f;
        }

        CleanupInstallPreviewReferences();
        for (int i = 0; i < installPreviewInstances.Count; i++)
        {
            MapObject preview = installPreviewInstances[i];
            if (preview == null)
            {
                continue;
            }

            ClearInputOutputMarkers(preview);
            if (Application.isPlaying)
            {
                Destroy(preview.gameObject);
            }
            else
            {
                DestroyImmediate(preview.gameObject);
            }
        }

        installPreviewInstances.Clear();
        installPreviewQuarterTurnsByPreview.Clear();
        installPreviewBaseRotationsByPreview.Clear();
        installPreviewAnchorCoordinates.Clear();
        installPreviewSourcePrefabsByPreview.Clear();
        installPreviewPlacementSequencesByPreview.Clear();
        installPreviewItemReservationsByPreview.Clear();
        activeInstallPreview = null;
    }

    private void SetInstallButtonVisible(bool isVisible)
    {
        if (installButton == null)
        {
            return;
        }

        if (installButton.gameObject.activeSelf != isVisible)
        {
            installButton.gameObject.SetActive(isVisible);
        }

        installButton.interactable = isVisible;
    }
}
