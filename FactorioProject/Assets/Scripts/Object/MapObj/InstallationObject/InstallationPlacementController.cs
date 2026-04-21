using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class InstallationPlacementController : MonoBehaviour
{
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
    private float installGridVerticalOffset = 0.075f;
    [SerializeField, Min(0.005f)]
    private float installGridLineWidth = 0.03f;
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
    private InstallationObject selectedEditableInstallation;
    private Vector2Int selectedEditableAnchorCoordinate;
    private InstallationEditSession activeInstallationEditSession;
    private readonly Stack<PackedInstallationSession> packedInstallationHistory = new Stack<PackedInstallationSession>();
    private bool mapEditModeActive;
    private ConveyorPreviewVariantMode installPreviewConveyorVariantMode = ConveyorPreviewVariantMode.Straight;

    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

    private sealed class InstallationEditSession
    {
        public InstallationObject originalInstallation;
        public ItemDefinition definition;
        public Vector2Int originalAnchorCoordinate;
        public int originalQuarterTurns;
        public int originalConveyorVariantKind = -1;
        public List<Vector2Int> originalOccupiedCoordinates = new List<Vector2Int>();
        public Dictionary<Vector2Int, List<int>> blockStatesByCanonicalOffset = new Dictionary<Vector2Int, List<int>>();
        public InputOutputModule.PersistentState inputOutputState;
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
        public int itemId;
        public Vector2Int dropCoordinate;
        public PortableObject portableObject;
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

        if (!TryGetHandInstallationDefinition(out ItemDefinition currentDefinition) || currentDefinition != activeInstallDefinition)
        {
            ClearInstallPreview();
            return;
        }

        if (GetCurrentInstallItemCount() < GetInstallPreviewCount())
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

        handBag.RefreshExternalStackCounts(false);
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

        if (mapEditModeActive)
        {
            SetMapEditModeActive(false);
            RefreshMapEditButtonState();
            return;
        }

        SetMapEditModeActive(true);
        RefreshMapEditButtonState();
    }

    private void HandleMapEditPackClicked()
    {
        if (!TryPackSelectedInstallation())
        {
            RefreshMapEditButtonState();
            return;
        }

        RefreshMapEditButtonState();
    }

    private void HandleMapEditUndoClicked()
    {
        if (!TryUndoPackedInstallation())
        {
            RefreshMapEditButtonState();
            return;
        }

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
        if (itemId < 0)
        {
            RestoreEditedInstallation(editSession, targetAnchorCoordinate, targetQuarterTurns);
            ClearInstallPreview();
            return false;
        }

        if (!TryDropPackedPortable(itemId, targetAnchorCoordinate, out PortableObject portableObject, out Vector2Int dropCoordinate))
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
            itemId = itemId,
            dropCoordinate = dropCoordinate,
            portableObject = portableObject
        });

        ClearEditableInstallationSelection();
        ClearInstallPreview();
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

        if (!TryRemovePackedPortable(packedSession))
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
            InstallationObject originalInstallation = packedSession?.editSession?.originalInstallation;
            if (originalInstallation == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(originalInstallation.gameObject);
            }
            else
            {
                DestroyImmediate(originalInstallation.gameObject);
            }
        }
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
        if (installationObject == null || !installationObject.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        int itemId = installationObject.ResolveItemId();
        if (!TryGetInstallationDefinition(itemId, out ItemDefinition definition) || definition == null || definition.mapObject == null)
        {
            return false;
        }

        editSession = new InstallationEditSession
        {
            originalInstallation = installationObject,
            definition = definition,
            originalAnchorCoordinate = anchorCoordinate,
            originalQuarterTurns = ((quarterTurns % 4) + 4) % 4,
            originalConveyorVariantKind = GetConveyorVariantKind(installationObject),
            originalOccupiedCoordinates = new List<Vector2Int>(installationObject.RuntimeOccupiedCoordinates)
        };

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

        CaptureInstallationBlockStates(editSession);
        return true;
    }

    private void CaptureInstallationBlockStates(InstallationEditSession editSession)
    {
        editSession.blockStatesByCanonicalOffset.Clear();

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        if (terrain == null || editSession.originalOccupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < editSession.originalOccupiedCoordinates.Count; i++)
        {
            Vector2Int occupiedCoordinate = editSession.originalOccupiedCoordinates[i];
            if (!terrain.TryGetLoadedBlock(occupiedCoordinate, out Block block) || block == null)
            {
                continue;
            }

            List<int> blockState = block.CaptureFloorObjectState();
            if (blockState == null || blockState.Count <= 0)
            {
                continue;
            }

            Vector2Int worldOffset = occupiedCoordinate - editSession.originalAnchorCoordinate;
            Vector2Int canonicalOffset = RotateFootprintOffset(worldOffset, -editSession.originalQuarterTurns);
            editSession.blockStatesByCanonicalOffset[canonicalOffset] = new List<int>(blockState);
        }
    }

    private void DetachInstallationForEditing(InstallationEditSession editSession)
    {
        if (editSession == null || editSession.originalInstallation == null)
        {
            return;
        }

        TerrainGenerator terrain = ResolveInstallPreviewTerrain();
        terrain?.RemoveInstallationPersistence(editSession.originalAnchorCoordinate);

        for (int i = 0; i < editSession.originalOccupiedCoordinates.Count; i++)
        {
            if (terrain == null || !terrain.TryGetLoadedBlock(editSession.originalOccupiedCoordinates[i], out Block block) || block == null)
            {
                continue;
            }

            if (block.MapObject == editSession.originalInstallation)
            {
                block.SetMapObject(null);
            }

            block.ApplyFloorObjectState(null);
        }

        editSession.originalInstallation.gameObject.SetActive(false);
    }

    private void BeginInstallationEditPreview(InstallationEditSession editSession)
    {
        activeInstallDefinition = editSession.definition;
        activeInstallPreview = null;
        activeInstallBaseRotation = editSession.definition.mapObject != null
            ? editSession.definition.mapObject.transform.rotation
            : Quaternion.identity;
        installPreviewQuarterTurns = editSession.originalQuarterTurns;
        installPreviewConveyorVariantMode = editSession.originalConveyorVariantKind > 0
            ? ConveyorPreviewVariantMode.Corner
            : ConveyorPreviewVariantMode.Straight;
        waitForPointerReleaseAfterPreviewSpawn = true;
        installGridRefreshTimer = 0f;
        GameManager.Instance?.SetInstallationPlacementActive(true);

        MapObject preview = CreateInstallPreviewInstance(editSession.definition);
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
        RefreshConveyorPreviewVariants();

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

        activeInstallPreview.transform.rotation = GetInstallPreviewRotation();
        InvalidateInstallGrid();
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
                editSession.originalConveyorVariantKind);
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

        int quarterTurns = GetPreviewQuarterTurns(activeInstallPreview);
        activeInstallationEditSession = null;
        RestoreEditedInstallation(
            editSession,
            anchorCoordinate,
            quarterTurns,
            GetConveyorVariantKind(activeInstallPreview));
        ClearInstallPreview();
        SetMapEditModeActive(false);
    }

    public bool MapEditModeActive => mapEditModeActive;

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

    private void RestoreEditedInstallation(
        InstallationEditSession editSession,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        int conveyorVariantKind = -1)
    {
        if (editSession == null || editSession.originalInstallation == null || editSession.definition == null)
        {
            return;
        }

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
            return;
        }

        Transform installParent = terrain != null ? terrain.transform : transform;
        restoredObject.transform.SetParent(installParent, true);
        restoredObject.transform.SetPositionAndRotation(
            GetInstalledObjectWorldPosition(anchorCoordinate, editSession.definition, quarterTurns, 0f),
            GetInstalledObjectRotation(restoredObject, quarterTurns));
        restoredObject.gameObject.SetActive(true);

        List<Vector2Int> occupiedCoordinates = GetInstalledObjectFootprintCoordinates(anchorCoordinate, editSession.definition, quarterTurns);
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            if (terrain != null && terrain.TryGetLoadedBlock(occupiedCoordinates[i], out Block block) && block != null)
            {
                block.SetMapObject(restoredObject);
                block.ApplyFloorObjectState(null);
            }
        }

        ConfigureInstalledObjectRuntime(restoredObject, anchorCoordinate, quarterTurns, editSession.inputOutputState);
        restoredObject.ApplyItemFilterMask(editSession.itemFilterMaskWords, editSession.itemFilterMaskInitialized);
        if (restoredObject is BoxObject restoredBoxObject && editSession.boxIsOpen.HasValue)
        {
            restoredBoxObject.SetOpenState(editSession.boxIsOpen.Value, false);
        }

        ApplyEditedInstallationBlockStates(editSession, anchorCoordinate, quarterTurns);
        RegisterInstalledObjectPersistence(restoredObject);

        if (restoredObject is InstallationObject restoredInstallation)
        {
            SelectEditableInstallation(restoredInstallation, anchorCoordinate);
        }
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

        List<Vector2Int> newOccupiedCoordinates = GetInstalledObjectFootprintCoordinates(newAnchorCoordinate, editSession.definition, newQuarterTurns);
        for (int i = 0; i < newOccupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = newOccupiedCoordinates[i];
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
        MapObject replacementObject = Instantiate(desiredSourcePrefab, installParent);
        if (replacementObject == null)
        {
            return originalObject;
        }

        replacementObject.ApplyItemFilterMask(editSession.itemFilterMaskWords, editSession.itemFilterMaskInitialized);

        if (Application.isPlaying)
        {
            Destroy(originalObject.gameObject);
        }
        else
        {
            DestroyImmediate(originalObject.gameObject);
        }

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
        int placedCount = 0;

        for (int i = 0; i < previewsToPlace.Count; i++)
        {
            MapObject preview = previewsToPlace[i];
            if (preview == null)
            {
                continue;
            }

            if (!TryGetBlockForPreview(preview, out Block anchorBlock))
            {
                continue;
            }

            int quarterTurns = GetPreviewQuarterTurns(preview);
            if (!TryGetFootprintBlocks(anchorBlock.Coordinate, preview, quarterTurns, preview, out List<Block> footprintBlocks)
                || footprintBlocks.Count <= 0)
            {
                continue;
            }

            TerrainGenerator terrain = ResolveInstallPreviewTerrain();
            Transform installParent = terrain != null ? terrain.transform : anchorBlock.transform;
            MapObject sourcePrefab = ResolveInstalledObjectSourcePrefab(activeInstallDefinition, anchorBlock.Coordinate, quarterTurns, preview);
            if (sourcePrefab == null)
            {
                sourcePrefab = activeInstallDefinition.mapObject;
            }

            MapObject installedObject = Instantiate(sourcePrefab, installParent);
            installedObject.transform.rotation = GetInstalledObjectRotation(installedObject, quarterTurns);
            installedObject.transform.position = GetInstalledObjectWorldPosition(anchorBlock.Coordinate, activeInstallDefinition, quarterTurns, 0f);

            for (int blockIndex = 0; blockIndex < footprintBlocks.Count; blockIndex++)
            {
                footprintBlocks[blockIndex].SetMapObject(installedObject);
            }

            ConfigureInstalledObjectRuntime(installedObject, anchorBlock.Coordinate, quarterTurns);
            RegisterInstalledObjectPersistence(installedObject);

            placedCount++;
        }

        if (placedCount > 0)
        {
            PlayerBag handBag = GetPlayerHandBag();
            handBag?.RemoveItems(activeInstallDefinition.id, placedCount);
            handBag?.RefreshExternalStackCounts();
        }

        ClearInstallPreview();
    }

    private void ConfigureInstalledInputOutputMarkers(MapObject installedObject, Vector2Int anchorCoordinate, int quarterTurns)
    {
        if (!TryGetInputOutputModule(installedObject, out _))
        {
            return;
        }

        List<AreaMarkerSpawnRequest> markerRequests = new List<AreaMarkerSpawnRequest>();
        List<Vector3> primaryObjectWorldPositions = GetRectGridBlockWorldPositions(
            anchorCoordinate,
            installedObject,
            quarterTurns,
            InputOutputModule.RectGridBlockType.Object);
        Vector3 primaryObjectWorldPosition = primaryObjectWorldPositions.Count > 0
            ? primaryObjectWorldPositions[0]
            : installedObject.transform.position;
        Sprite arrowIcon = ResolveArrowMarkerIcon();

        List<Vector3> inputEnergyWorldPositions = GetRectGridBlockWorldPositions(
            anchorCoordinate,
            installedObject,
            quarterTurns,
            InputOutputModule.RectGridBlockType.InputEnergy);
        AddAreaMarkerRequests(
            markerRequests,
            inputEnergyWorldPositions,
            ResolveInputEnergyMarkerIcon(installedObject));

        List<Vector3> inputItemWorldPositions = GetRectGridBlockWorldPositions(
            anchorCoordinate,
            installedObject,
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
            installedObject,
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
            return;
        }

        AreaMarkerPool pool = ResolveAreaMarkerPool();
        if (pool == null)
        {
            return;
        }

        InputOutputModuleAreaMarkerController markerController = installedObject.GetComponent<InputOutputModuleAreaMarkerController>();
        if (markerController == null)
        {
            markerController = installedObject.gameObject.AddComponent<InputOutputModuleAreaMarkerController>();
        }

        markerController.Configure(pool, markerRequests);
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

    public void ConfigureInstalledObjectRuntime(
        MapObject installedObject,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        InputOutputModule.PersistentState persistentState = null)
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
                GetFootprintCoordinates(anchorCoordinate, installedObject, quarterTurns));
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
        return GetPlacementWorldPositionFromAnchorCoordinate(
            anchorCoordinate,
            definition != null ? definition.mapObject : null,
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

        if (!TryResolveConveyorCornerPlacementPrefab(conveyorPrototype, anchorCoordinate, quarterTurns, previewToIgnore, out MapObject resolvedPrefab)
            || resolvedPrefab == null)
        {
            return null;
        }

        return resolvedPrefab;
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
        resolvedPrefab = null;
        if (conveyorPrototype == null
            || !TryGetConveyorPlacementOutputDirection(conveyorPrototype, quarterTurns, out Vector2Int outputDirection))
        {
            return false;
        }

        Vector2Int[] validInputDirections = GetValidConveyorIncomingDirections(anchorCoordinate, previewToIgnore);
        if (validInputDirections.Length <= 0)
        {
            return false;
        }

        ConveyorBelt cornerCandidate = conveyorPrototype.CornerVariantPrefab != null
            ? conveyorPrototype.CornerVariantPrefab
            : conveyorPrototype.StraightVariantPrefab;
        ConveyorBelt reverseCornerCandidate = conveyorPrototype.ReverseCornerVariantPrefab != null
            ? conveyorPrototype.ReverseCornerVariantPrefab
            : conveyorPrototype.CornerVariantPrefab;

        bool cornerMatches = TryConveyorCornerPrefabMatchesIncomingDirection(
            cornerCandidate,
            quarterTurns,
            validInputDirections);
        bool reverseMatches = TryConveyorCornerPrefabMatchesIncomingDirection(
            reverseCornerCandidate,
            quarterTurns,
            validInputDirections);

        if (!cornerMatches && !reverseMatches)
        {
            return false;
        }

        if (cornerMatches && !reverseMatches)
        {
            resolvedPrefab = cornerCandidate;
            return resolvedPrefab != null;
        }

        if (reverseMatches && !cornerMatches)
        {
            resolvedPrefab = reverseCornerCandidate;
            return resolvedPrefab != null;
        }

        bool preferReverseVariant = previewToIgnore is ConveyorBelt previewConveyor && previewConveyor.IsReverseCornerVariant;
        resolvedPrefab = preferReverseVariant ? reverseCornerCandidate : cornerCandidate;
        return resolvedPrefab != null;
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

    private bool TryGetConveyorPlacementOutputDirection(ConveyorBelt conveyorPrototype, int quarterTurns, out Vector2Int outputDirection)
    {
        outputDirection = Vector2Int.zero;
        if (conveyorPrototype == null)
        {
            return false;
        }

        return ConveyorBelt.TryGetFlowDirection(
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
            if (!ConveyorBelt.TryGetFlowDirection(
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

    private bool TryGetConveyorPlacementInfoAtCoordinate(
        Vector2Int coordinate,
        MapObject previewToIgnore,
        out ConveyorBelt conveyorBelt,
        out Vector2Int outputDirection)
    {
        conveyorBelt = null;
        outputDirection = Vector2Int.zero;

        if (TryGetInstallPreviewAtCoordinate(coordinate, out MapObject preview)
            && preview != null
            && preview != previewToIgnore
            && preview is ConveyorBelt previewConveyor)
        {
            conveyorBelt = previewConveyor;
            return ConveyorBelt.TryGetFlowDirection(previewConveyor.transform.rotation, out outputDirection);
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
            return ConveyorBelt.TryGetFlowDirection(liveConveyor.transform.rotation, out outputDirection);
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
        return ConveyorBelt.TryGetFlowDirection(GetPlacementObjectRotation(savedConveyor, savedState.quarterTurns), out outputDirection);
    }

    private void RefreshConveyorPreviewVariants()
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

        List<MapObject> previews = new List<MapObject>(installPreviewInstances);
        for (int i = 0; i < previews.Count; i++)
        {
            MapObject preview = previews[i];
            if (preview == null || !TryGetPreviewAnchorCoordinate(preview, out Vector2Int anchorCoordinate))
            {
                continue;
            }

            RefreshSingleConveyorPreviewVariant(preview, anchorCoordinate);
        }
    }

    private void RefreshSingleConveyorPreviewVariant(MapObject preview, Vector2Int anchorCoordinate)
    {
        if (preview == null || activeInstallDefinition == null)
        {
            return;
        }

        int quarterTurns = GetPreviewQuarterTurns(preview);
        MapObject desiredPrefab = ResolveInstalledObjectSourcePrefab(activeInstallDefinition, anchorCoordinate, quarterTurns, preview);
        if (desiredPrefab == null
            || !RequiresConveyorPreviewReplacement(preview, desiredPrefab))
        {
            return;
        }

        MapObject replacementPreview = Instantiate(desiredPrefab);
        if (replacementPreview == null)
        {
            return;
        }

        replacementPreview.name = $"{desiredPrefab.name}_Blueprint";
        ConfigureInstallPreview(replacementPreview);
        ReplaceInstallPreviewInstance(preview, replacementPreview, anchorCoordinate, quarterTurns);
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

    private void ReplaceInstallPreviewInstance(MapObject currentPreview, MapObject replacementPreview, Vector2Int anchorCoordinate, int quarterTurns)
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
        installPreviewAnchorCoordinates.Remove(currentPreview);
        installPreviewAnchorCoordinates[replacementPreview] = anchorCoordinate;

        replacementPreview.transform.SetPositionAndRotation(
            currentPosition,
            GetPlacementObjectRotation(replacementPreview, resolvedQuarterTurns));

        if (wasActivePreview)
        {
            activeInstallPreview = replacementPreview;
            installPreviewQuarterTurns = resolvedQuarterTurns;
        }

        if (wasPointerOriginPreview)
        {
            previewPointerOriginPreview = replacementPreview;
        }

        if (Application.isPlaying)
        {
            Destroy(currentPreview.gameObject);
        }
        else
        {
            DestroyImmediate(currentPreview.gameObject);
        }
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
                worldPositions.Add(block.transform.position);
                continue;
            }

            worldPositions.Add(new Vector3(coordinates[i].x, fallbackY, coordinates[i].y));
        }

        return worldPositions;
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
        installPreviewQuarterTurns = 0;
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

        MapObject preview = Instantiate(definition.mapObject);
        if (preview == null)
        {
            return null;
        }

        preview.name = $"{definition.mapObject.name}_Blueprint";
        ConfigureInstallPreview(preview);
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
        if (activeInstallPreview == null || block == null || !CanPlacePreviewOnBlock(block, activeInstallPreview, installPreviewQuarterTurns))
        {
            return false;
        }

        installPreviewAnchorCoordinates[activeInstallPreview] = block.Coordinate;
        RefreshConveyorPreviewVariants();
        Vector3 targetPosition = GetPreviewWorldPosition(block, activeInstallPreview, installPreviewQuarterTurns, installPreviewVerticalOffset);
        activeInstallPreview.transform.position = targetPosition;
        activeInstallPreview.transform.rotation = GetInstallPreviewRotation();
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
            installPreviewTerrain = FindObjectOfType<TerrainGenerator>();
        }

        return installPreviewTerrain;
    }

    private void UpdateInstallGrid(float deltaTime)
    {
        if (!IsInstallGridModeActive())
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
            Shader shader = Shader.Find("Sprites/Default");
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

        float lineY = terrain.transform.position.y + installGridVerticalOffset;
        float fillY = lineY - 0.002f;
        float minX = minCoordinate.x - 0.5f;
        float maxX = maxCoordinate.x + 0.5f;
        float minZ = minCoordinate.y - 0.5f;
        float maxZ = maxCoordinate.y + 0.5f;

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();

        if (activeInstallDefinition != null && activeInstallDefinition.mapObject != null)
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

        AddInstallPreviewFootprintFill(vertices, triangles, colors, fillY);

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

            List<Vector2Int> occupiedCoordinates = GetFootprintCoordinates(anchorCoordinate, preview, GetPreviewQuarterTurns(preview));
            for (int coordinateIndex = 0; coordinateIndex < occupiedCoordinates.Count; coordinateIndex++)
            {
                AddGridCellQuad(
                    vertices,
                    triangles,
                    colors,
                    occupiedCoordinates[coordinateIndex],
                    fillY,
                    installPreviewTint);
            }
        }
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

        if (IsEditingInstallation() && previewPointerDragged)
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

            bool handledByDuplicate = previewPointerDragged && TryDuplicateInstallPreview(pointerPosition);
            if (!handledByDuplicate && !previewPointerDragged)
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

        CancelInstallationEdit();
        SelectEditableInstallation(installationObject, anchorCoordinate);
        BeginInstallationEdit(installationObject);
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
        installPreviewQuarterTurns = (installPreviewQuarterTurns + 1) % 4;
        installPreviewQuarterTurnsByPreview[activeInstallPreview] = installPreviewQuarterTurns;
        RefreshConveyorPreviewVariants();
        activeInstallPreview.transform.rotation = GetInstallPreviewRotation();

        if (hasAnchorBlock)
        {
            activeInstallPreview.transform.position = GetPreviewWorldPosition(
                anchorBlock,
                activeInstallPreview,
                installPreviewQuarterTurns,
                installPreviewVerticalOffset);
        }

        InvalidateInstallGrid();
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
            SelectInstallPreview(clickedPreview);
            if (!IsEditingInstallation() && IsPreviewObjectCell(clickedPreview, clickedBlock))
            {
                RemoveInstallPreview(clickedPreview);
                return;
            }

            MoveInstallPreviewToBlock(clickedBlock);
            return;
        }

        if (activeInstallPreview == null)
        {
            TryCreateAndPlaceInstallPreview(clickedBlock, null);
            return;
        }

        MoveInstallPreviewToBlock(clickedBlock);
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

    private bool CanCreateAdditionalPreview()
    {
        if (IsEditingInstallation())
        {
            return false;
        }

        return GetInstallPreviewCount() < GetCurrentInstallItemCount();
    }

    private int GetCurrentInstallItemCount()
    {
        if (IsEditingInstallation())
        {
            return 1;
        }

        PlayerBag handBag = GetPlayerHandBag();
        if (handBag == null)
        {
            return 0;
        }

        handBag.RefreshExternalStackCounts(false);
        return handBag.GetSlotCount(0);
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
    }

    private bool TryCreateAndPlaceInstallPreview(Block block, MapObject sourcePreview)
    {
        if (block == null || activeInstallDefinition == null || !CanCreateAdditionalPreview())
        {
            return false;
        }

        int quarterTurns = sourcePreview != null ? GetPreviewQuarterTurns(sourcePreview) : 0;
        if (!CanPlacePreviewOnBlock(block, null, quarterTurns))
        {
            return false;
        }

        MapObject preview = CreateInstallPreviewInstance(activeInstallDefinition);
        if (preview == null)
        {
            return false;
        }

        RegisterInstallPreview(preview, quarterTurns);
        SelectInstallPreview(preview);
        if (sourcePreview != null)
        {
            installPreviewConveyorVariantMode = GetConveyorPreviewVariantMode(sourcePreview);
            RefreshConveyorPreviewVariants();
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

        installPreviewInstances.Remove(preview);
        installPreviewQuarterTurnsByPreview.Remove(preview);
        installPreviewBaseRotationsByPreview.Remove(preview);
        installPreviewAnchorCoordinates.Remove(preview);

        if (activeInstallPreview == preview)
        {
            activeInstallPreview = null;
            installPreviewQuarterTurns = 0;
        }

        if (Application.isPlaying)
        {
            Destroy(preview.gameObject);
        }
        else
        {
            DestroyImmediate(preview.gameObject);
        }

        EnsureValidActiveInstallPreview();
        RefreshConveyorPreviewVariants();
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

        MapObject footprintSource = previewToIgnore != null
            ? previewToIgnore
            : (activeInstallDefinition != null ? activeInstallDefinition.mapObject : null);
        if (footprintSource == null)
        {
            return false;
        }

        int quarterTurns = quarterTurnsOverride ?? (previewToIgnore != null ? GetPreviewQuarterTurns(previewToIgnore) : installPreviewQuarterTurns);
        if (!TryGetFootprintBlocks(block.Coordinate, footprintSource, quarterTurns, previewToIgnore, out _))
        {
            return false;
        }

        if (activeInstallDefinition == null || !(activeInstallDefinition.mapObject is ConveyorBelt conveyorPrototype))
        {
            return true;
        }

        if (installPreviewConveyorVariantMode != ConveyorPreviewVariantMode.Corner)
        {
            return true;
        }

        return TryResolveConveyorCornerPlacementPrefab(conveyorPrototype, block.Coordinate, quarterTurns, previewToIgnore, out _);
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
        if (TryGetRectGridFootprintSettings(footprintSource, out int rectGridWidth, out int rectGridHeight, out Vector2Int objectAnchorCell))
        {
            List<Vector2Int> rectGridOffsets = new List<Vector2Int>(rectGridWidth * rectGridHeight);
            for (int y = 0; y < rectGridHeight; y++)
            {
                for (int x = 0; x < rectGridWidth; x++)
                {
                    Vector2Int localOffset = new Vector2Int(x - objectAnchorCell.x, y - objectAnchorCell.y);
                    rectGridOffsets.Add(RotateFootprintOffset(localOffset, quarterTurns));
                }
            }

            return rectGridOffsets;
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

    private bool TryGetFootprintBlocks(
        Vector2Int anchorCoordinate,
        MapObject footprintSource,
        int quarterTurns,
        MapObject previewToIgnore,
        out List<Block> footprintBlocks)
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

            if (!CanPlacePreviewOnTargetBlockType(footprintBlock, footprintSource))
            {
                return false;
            }

            if (TryGetInstallPreviewAtCoordinate(coordinate, out MapObject existingPreview)
                && existingPreview != null
                && existingPreview != previewToIgnore)
            {
                return false;
            }

            footprintBlocks.Add(footprintBlock);
        }

        return footprintBlocks.Count > 0;
    }

    private bool CanPlacePreviewOnTargetBlockType(Block block, MapObject footprintSource)
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
        bool isInputOutputAreaBlock
            = InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(block.Coordinate)
            || InputOutputModuleItemAreaController.CoordinateIsItemArea(block.Coordinate)
            || InputOutputModuleOutputAreaController.CoordinateIsOutputArea(block.Coordinate);

        if (occupyingObject is Resource resource)
        {
            return resource.CanHarvest && (allowedFilter & InstallationMapFilter.Resource) != 0;
        }

        if (isInputOutputAreaBlock && (allowedFilter & InstallationMapFilter.ItemArea) != 0)
        {
            return block.Type == Block.BlockType.Ground
                && (occupyingObject == null || occupyingObject is InputOutputModule);
        }

        if (occupyingObject != null)
        {
            return false;
        }

        return block.Type switch
        {
            Block.BlockType.Ground => (allowedFilter & InstallationMapFilter.Ground) != 0,
            Block.BlockType.Water => (allowedFilter & InstallationMapFilter.Water) != 0,
            _ => false
        };
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
