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
    private bool hasInstallGridBounds;
    private readonly List<MapObject> installPreviewInstances = new List<MapObject>();
    private readonly Dictionary<MapObject, int> installPreviewQuarterTurnsByPreview = new Dictionary<MapObject, int>();
    private readonly Dictionary<MapObject, Vector2Int> installPreviewAnchorCoordinates = new Dictionary<MapObject, Vector2Int>();

    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        ResolveInstallButtons();
        BindInstallButtons();
        SetInstallButtonVisible(false);
    }

    private void OnEnable()
    {
        ResolveInstallButtons();
        RefreshInstallButton();
    }

    private void Update()
    {
        ResolveInstallButtons();
        RefreshInstallButton();
        UpdateInstallGrid(Time.deltaTime);

        if (!IsInstallationModeActive())
        {
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
        ClearInstallPreview();
        SetInstallButtonVisible(false);
    }

    private void OnDestroy()
    {
        UnbindInstallButtons();
        ClearInstallPreview();
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
                }
            }
        }
    }

    private void UpdateInstallButtonVisibility(PlayerBag handBag)
    {
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
    }

    private void UnbindInstallButtons()
    {
        UnbindButton(installButton, HandleInstallButtonClicked);
        UnbindButton(installCancelButton, HandleInstallCancelClicked);
        UnbindButton(installRotationButton, HandleInstallRotationClicked);
        UnbindButton(installCompleteButton, HandleInstallCompleteClicked);
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

    private void HandleInstallButtonClicked()
    {
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
        ClearInstallPreview();
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

            MapObject installedObject = Instantiate(activeInstallDefinition.mapObject, anchorBlock.transform);
            installedObject.transform.rotation = preview.transform.rotation;
            installedObject.transform.position = GetPreviewWorldPosition(anchorBlock, installedObject, quarterTurns, 0f);

            for (int blockIndex = 0; blockIndex < footprintBlocks.Count; blockIndex++)
            {
                footprintBlocks[blockIndex].SetMapObject(installedObject);
            }

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

    private void BeginInstallPreview(ItemDefinition definition)
    {
        ClearInstallPreview();

        activeInstallDefinition = definition;
        activeInstallPreview = null;
        activeInstallBaseRotation = definition.mapObject != null ? definition.mapObject.transform.rotation : Quaternion.identity;
        installPreviewQuarterTurns = 0;
        waitForPointerReleaseAfterPreviewSpawn = true;
        installGridRefreshTimer = 0f;
        hasInstallGridBounds = false;
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
        Vector3 targetPosition = GetPreviewWorldPosition(block, activeInstallPreview, installPreviewQuarterTurns, installPreviewVerticalOffset);
        activeInstallPreview.transform.position = targetPosition;
        activeInstallPreview.transform.rotation = GetInstallPreviewRotation();
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
        if (!IsInstallationModeActive())
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
            hasInstallGridBounds = false;
            installGridMesh.Clear();
            return;
        }

        if (hasInstallGridBounds
            && installGridMinCoordinate == minCoordinate
            && installGridMaxCoordinate == maxCoordinate
            && installGridMesh.vertexCount > 0)
        {
            return;
        }

        hasInstallGridBounds = true;
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
        hasInstallGridBounds = false;
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
    }

    private void EndPreviewPointerTracking(Vector2 pointerPosition, bool wasCanceled)
    {
        if (!isPreviewPointerTracking)
        {
            return;
        }

        if (!wasCanceled && !previewPointerStartedOverUi)
        {
            bool handledByDuplicate = previewPointerDragged && TryDuplicateInstallPreview(pointerPosition);
            if (!handledByDuplicate && !previewPointerDragged)
            {
                HandleInstallPreviewClick(pointerPosition);
            }
        }

        ResetPreviewPointerTracking();
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

        int nextQuarterTurns = (installPreviewQuarterTurns + 1) % 4;
        if (TryGetPreviewAnchorCoordinate(activeInstallPreview, out Vector2Int anchorCoordinate)
            && ResolveInstallPreviewTerrain() != null
            && ResolveInstallPreviewTerrain().TryGetLoadedBlock(anchorCoordinate, out Block anchorBlock)
            && !CanPlacePreviewOnBlock(anchorBlock, activeInstallPreview, nextQuarterTurns))
        {
            return;
        }

        installPreviewQuarterTurns = nextQuarterTurns;
        activeInstallPreview.transform.rotation = GetInstallPreviewRotation();
        installPreviewQuarterTurnsByPreview[activeInstallPreview] = installPreviewQuarterTurns;

        if (TryGetPreviewAnchorCoordinate(activeInstallPreview, out Vector2Int storedAnchor)
            && ResolveInstallPreviewTerrain() != null
            && ResolveInstallPreviewTerrain().TryGetLoadedBlock(storedAnchor, out Block storedAnchorBlock))
        {
            activeInstallPreview.transform.position = GetPreviewWorldPosition(
                storedAnchorBlock,
                activeInstallPreview,
                installPreviewQuarterTurns,
                installPreviewVerticalOffset);
        }
    }

    private Quaternion GetInstallPreviewRotation()
    {
        return activeInstallBaseRotation * Quaternion.Euler(0f, installPreviewQuarterTurns * 90f, 0f);
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
        return GetInstallPreviewCount() < GetCurrentInstallItemCount();
    }

    private int GetCurrentInstallItemCount()
    {
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
        activeInstallPreview.transform.rotation = GetInstallPreviewRotation();
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
        return TryGetFootprintBlocks(block.Coordinate, footprintSource, quarterTurns, previewToIgnore, out _);
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
        int sizeX = 1;
        int sizeY = 1;

        if (footprintSource != null)
        {
            sizeX = Mathf.Max(1, footprintSource.Status.mapSizeX);
            sizeY = Mathf.Max(1, footprintSource.Status.mapSizeY);
        }

        if (Mathf.Abs(quarterTurns) % 2 == 1)
        {
            (sizeX, sizeY) = (sizeY, sizeX);
        }

        return new Vector2Int(sizeX, sizeY);
    }

    private List<Vector2Int> GetFootprintCoordinates(Vector2Int anchorCoordinate, MapObject footprintSource, int quarterTurns)
    {
        Vector2Int footprintSize = GetFootprintSize(footprintSource, quarterTurns);
        List<Vector2Int> coordinates = new List<Vector2Int>(footprintSize.x * footprintSize.y);

        for (int y = 0; y < footprintSize.y; y++)
        {
            for (int x = 0; x < footprintSize.x; x++)
            {
                coordinates.Add(new Vector2Int(anchorCoordinate.x + x, anchorCoordinate.y + y));
            }
        }

        return coordinates;
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
        if (occupyingObject is Resource resource)
        {
            return resource.CanHarvest && (allowedFilter & InstallationMapFilter.Resource) != 0;
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
        Vector2Int footprintSize = GetFootprintSize(footprintSource, quarterTurns);
        Vector3 position = anchorBlock.transform.position;
        position.x += (footprintSize.x - 1) * 0.5f;
        position.z += (footprintSize.y - 1) * 0.5f;
        position.y += verticalOffset;
        return position;
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
        ResetPreviewPointerTracking();
        GameManager.Instance?.SetInstallationPlacementActive(false);
        SetInstallGridVisible(false);
        hasInstallGridBounds = false;
        installGridRefreshTimer = 0f;

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
