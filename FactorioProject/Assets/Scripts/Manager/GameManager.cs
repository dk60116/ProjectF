using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using ProjectF.Diagnostics;
using TMPro;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private UIManager uiManager;
    private ItemManager itemManager;
    private VirtualObjectWorld virtualObjectWorld;
    private VirtualItemStackRenderer virtualItemStackRenderer;
    private WorldTimeService worldTimeService;

    [SerializeField]
    private Player player;
    [SerializeField]
    private bool debugConveyorInstallGridEnds;
    [SerializeField]
    private bool showConveyorSlotDots;
    [SerializeField]
    private bool showSleepAwake;
    [SerializeField]
    private bool showBeltItemLine;
    [SerializeField, InspectorName("Hide Belt Item")]
    private bool hideBeltItems;
    [SerializeField, InspectorName("Hide Belt")]
    private bool hideBelts;
    [SerializeField]
    private bool showRailLine;
    [FormerlySerializedAs("showBeltDirections")]
    [SerializeField]
    private bool showDirections;
    [SerializeField]
    private bool freeCamera;
    [SerializeField]
    private bool freeTrain;
    [SerializeField, InspectorName("Free Electro Energy")]
    private bool freeElectroEnergy;
    [SerializeField, InspectorName("Free Bucket")]
    private bool freeBucket;
    [SerializeField]
    private bool mapObjectTickProfilingEnabled;
    [SerializeField, Min(1)]
    private int mapObjectTickProfilingMaxRows = 64;
    [SerializeField, Min(1f)]
    private float animalAIActiveRadius = 60f;
    [SerializeField]
    private bool showAnimalHerdAreas;
    [SerializeField]
    private bool runtimeItemGiveServerEnabled = true;
    [SerializeField, Min(1)]
    private int runtimeItemGiveServerPort = RuntimeItemGiveReceiver.DefaultPort;
    private bool conveyorSlotDotRuntimeStateInitialized;
    private bool lastRuntimeShowConveyorSlotDots;
    private bool sleepAwakeRuntimeStateInitialized;
    private bool lastRuntimeShowSleepAwake;
    private bool beltItemLineRuntimeStateInitialized;
    private bool lastRuntimeShowBeltItemLine;
    private bool beltItemRenderingRuntimeStateInitialized;
    private bool lastRuntimeHideBeltItems;
    private bool beltRenderingRuntimeStateInitialized;
    private bool lastRuntimeHideBelts;
    private bool railLineRuntimeStateInitialized;
    private bool lastRuntimeShowRailLine;
    private bool beltDirectionRuntimeStateInitialized;
    private bool lastRuntimeShowBeltDirections;
    private bool freeCameraRuntimeStateInitialized;
    private bool lastRuntimeFreeCamera;
    private bool freeElectroEnergyRuntimeStateInitialized;
    private bool lastRuntimeFreeElectroEnergy;
    private bool mapObjectTickProfilingRuntimeStateInitialized;
    private bool lastRuntimeMapObjectTickProfilingEnabled;
    private RailLineDebugRenderer railLineDebugRenderer;
    private AnimalAIWorld animalAIWorld;
    private AnimalHerdDebugRenderer animalHerdDebugRenderer;

    public bool InstallationPlacementActive { get; private set; }
    public bool MapEditActive { get; private set; }
    public bool PlayerInteractionLocked => InstallationPlacementActive || MapEditActive;
    public static bool TextInputFocused => IsTextInputFocused();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        
        uiManager = GetComponentInChildren<UIManager>();
        itemManager = GetComponentInChildren<ItemManager>();
        virtualObjectWorld = VirtualObjectWorld.EnsureFor(gameObject);
        worldTimeService = WorldTimeService.EnsureFor(gameObject);
        virtualItemStackRenderer = GetComponent<VirtualItemStackRenderer>();
        if (virtualItemStackRenderer == null)
        {
            virtualItemStackRenderer = gameObject.AddComponent<VirtualItemStackRenderer>();
        }

        railLineDebugRenderer = GetComponent<RailLineDebugRenderer>();
        if (railLineDebugRenderer == null)
        {
            railLineDebugRenderer = gameObject.AddComponent<RailLineDebugRenderer>();
        }

        animalAIWorld = AnimalAIWorld.EnsureFor(gameObject);
        animalHerdDebugRenderer = GetComponent<AnimalHerdDebugRenderer>();
        if (animalHerdDebugRenderer == null)
        {
            animalHerdDebugRenderer = gameObject.AddComponent<AnimalHerdDebugRenderer>();
        }

        animalHerdDebugRenderer.SetVisible(showAnimalHerdAreas);
        virtualItemStackRenderer.Configure(virtualObjectWorld, itemManager);
        ConfigureRuntimeItemGiveReceiver();
    }

    private void Update()
    {
        bool textInputFocused = IsTextInputFocused();
        if (!textInputFocused && Input.GetKeyDown(KeyCode.Alpha0))
            Time.timeScale = 0.5f;
        else if (!textInputFocused && Input.GetKeyDown(KeyCode.Alpha1))
            Time.timeScale = 1f;
        else if (!textInputFocused && Input.GetKeyDown(KeyCode.Alpha2))
            Time.timeScale = 2f;

        SyncConveyorSlotDotRuntimeVisibility();
        SyncSleepAwakeRuntimeVisibility();
        SyncBeltItemLineRuntimeVisibility();
        SyncBeltItemRenderingRuntimeVisibility();
        SyncBeltRenderingRuntimeVisibility();
        SyncRailLineRuntimeVisibility();
        SyncBeltDirectionRuntimeVisibility();
        SyncFreeCameraRuntimeState();
        SyncFreeElectroEnergyRuntimeState();
        SyncMapObjectTickProfilingRuntimeState();
    }

    private void OnValidate()
    {
        animalAIActiveRadius = Mathf.Max(1f, animalAIActiveRadius);
        if (Application.isPlaying && Instance == this)
        {
            SyncConveyorSlotDotRuntimeVisibility(true);
            SyncSleepAwakeRuntimeVisibility(true);
            SyncBeltItemLineRuntimeVisibility(true);
            SyncBeltItemRenderingRuntimeVisibility(true);
            SyncBeltRenderingRuntimeVisibility(true);
            SyncRailLineRuntimeVisibility(true);
            SyncBeltDirectionRuntimeVisibility(true);
            SyncFreeCameraRuntimeState(true);
            SyncFreeElectroEnergyRuntimeState(true);
            SyncMapObjectTickProfilingRuntimeState(true);
            animalHerdDebugRenderer?.SetVisible(showAnimalHerdAreas);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SyncBeltItemRenderingRuntimeVisibility(true);
        SyncBeltRenderingRuntimeVisibility(true);
        SyncRailLineRuntimeVisibility(true);
        SyncFreeCameraRuntimeState(true);
        animalHerdDebugRenderer?.SetVisible(showAnimalHerdAreas);
        worldTimeService?.RefreshEnvironmentBindings();
    }

    public UIManager UIManager => uiManager;
    public ItemManager ItemManger => itemManager;
    public VirtualObjectWorld VirtualWorld => virtualObjectWorld;
    public VirtualItemStackRenderer VirtualItemRenderer => virtualItemStackRenderer;
    public WorldTimeService WorldTime => worldTimeService != null
        ? worldTimeService
        : WorldTimeService.Active;

    public Player Player => player;
    public bool DebugConveyorInstallGridEnds => debugConveyorInstallGridEnds;
    public bool ShowConveyorSlotDots => showConveyorSlotDots;
    public bool ShowSleepAwake => showSleepAwake;
    public bool ShowBeltItemLine => showBeltItemLine;
    public bool HideBeltItems => hideBeltItems;
    public bool HideBelts => hideBelts;
    public bool ShowRailLine => showRailLine;
    public bool ShowDirections => showDirections;
    public bool ShowBeltDirections => ShowDirections;
    public bool FreeCamera => freeCamera;
    public bool FreeTrain => freeTrain;
    public bool FreeElectroEnergy => freeElectroEnergy;
    public bool FreeBucket => freeBucket;
    public bool MapObjectTickProfilingEnabled => mapObjectTickProfilingEnabled;
    public int MapObjectTickProfilingMaxRows => Mathf.Max(1, mapObjectTickProfilingMaxRows);
    public float AnimalAIActiveRadius => Mathf.Max(1f, animalAIActiveRadius);
    public bool ShowAnimalHerdAreas => showAnimalHerdAreas;
    public AnimalAIWorld AnimalAIWorld => animalAIWorld;

    public static bool IsTextInputFocused()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
        {
            return false;
        }

        GameObject selectedObject = eventSystem.currentSelectedGameObject;
        TMP_InputField tmpInputField = selectedObject.GetComponent<TMP_InputField>()
                                       ?? selectedObject.GetComponentInParent<TMP_InputField>();
        if (tmpInputField != null && tmpInputField.isActiveAndEnabled)
        {
            return tmpInputField.isFocused || selectedObject.transform.IsChildOf(tmpInputField.transform);
        }

        InputField inputField = selectedObject.GetComponent<InputField>()
                                ?? selectedObject.GetComponentInParent<InputField>();
        return inputField != null
               && inputField.isActiveAndEnabled
               && (inputField.isFocused || selectedObject.transform.IsChildOf(inputField.transform));
    }
    public bool RuntimeItemGiveServerEnabled => runtimeItemGiveServerEnabled;
    public int RuntimeItemGiveServerPort => runtimeItemGiveServerPort;

    public void SetInstallationPlacementActive(bool isActive)
    {
        if (InstallationPlacementActive == isActive)
        {
            return;
        }

        InstallationPlacementActive = isActive;
        HandlePlayerInteractionModeChanged();
        WorkableObject.RefreshAllRangeVisuals();
    }

    public void SetMapEditActive(bool isActive)
    {
        if (MapEditActive == isActive)
        {
            return;
        }

        MapEditActive = isActive;
        HandlePlayerInteractionModeChanged();
        WorkableObject.RefreshAllRangeVisuals();
    }

    private void HandlePlayerInteractionModeChanged()
    {
        if (PlayerInteractionLocked)
        {
            return;
        }

        WorkableObject.SetInstallOrEditWorkableSelectionRangeVisualsRequested(false);
        RobotArm.WakeAllHeldItemTransfers();
    }

    public void SetShowConveyorSlotDots(bool show)
    {
        showConveyorSlotDots = show;
        SyncConveyorSlotDotRuntimeVisibility(true);
    }

    public void SetShowSleepAwake(bool show)
    {
        showSleepAwake = show;
        SyncSleepAwakeRuntimeVisibility(true);
    }

    public void SetShowBeltItemLine(bool show)
    {
        showBeltItemLine = show;
        SyncBeltItemLineRuntimeVisibility(true);
    }

    public void SetHideBeltItems(bool hide)
    {
        hideBeltItems = hide;
        SyncBeltItemRenderingRuntimeVisibility(true);
    }

    public void SetHideBelts(bool hide)
    {
        hideBelts = hide;
        SyncBeltRenderingRuntimeVisibility(true);
    }

    public void SetShowRailLine(bool show)
    {
        showRailLine = show;
        SyncRailLineRuntimeVisibility(true);
    }

    public void SetShowBeltDirections(bool show)
    {
        SetShowDirections(show);
    }

    public void SetShowDirections(bool show)
    {
        showDirections = show;
        SyncBeltDirectionRuntimeVisibility(true);
    }

    public void SetFreeCamera(bool enabled)
    {
        freeCamera = enabled;
        SyncFreeCameraRuntimeState(true);
    }

    public void SetFreeTrain(bool enabled)
    {
        freeTrain = enabled;
    }

    public void SetFreeElectroEnergy(bool enabled)
    {
        if (freeElectroEnergy == enabled)
        {
            return;
        }

        freeElectroEnergy = enabled;
        SyncFreeElectroEnergyRuntimeState(true);
    }

    public void SetFreeBucket(bool enabled)
    {
        freeBucket = enabled;
    }

    public void SetMapObjectTickProfilingEnabled(bool enabled)
    {
        mapObjectTickProfilingEnabled = enabled;
        SyncMapObjectTickProfilingRuntimeState(true);
    }

    public void SetShowAnimalHerdAreas(bool show)
    {
        showAnimalHerdAreas = show;
        animalHerdDebugRenderer?.SetVisible(show);
    }

    public void SetAnimalAIPaused(bool paused)
    {
        animalAIWorld?.SetPaused(paused);
    }

    public void SetWorldTimePaused(bool paused)
    {
        WorldTime?.SetPaused(paused);
    }

    public void SetWorldTimeScale(float scale)
    {
        WorldTime?.SetTimeScale(scale);
    }

    public bool TrySetWorldTime(int hour, int minute)
    {
        WorldTimeService service = WorldTime;
        return service != null && service.TrySetTimeOfDay(hour, minute);
    }

    public void AdvanceWorldTimeToNextSunrise()
    {
        WorldTime?.AdvanceToNextSunrise();
    }

    public void ResetWorldTime()
    {
        WorldTime?.ResetToDefault();
    }

    private void SyncConveyorSlotDotRuntimeVisibility(bool force = false)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!force
            && conveyorSlotDotRuntimeStateInitialized
            && lastRuntimeShowConveyorSlotDots == showConveyorSlotDots)
        {
            return;
        }

        conveyorSlotDotRuntimeStateInitialized = true;
        lastRuntimeShowConveyorSlotDots = showConveyorSlotDots;
        TerrainGenerator.Active?.RefreshConveyorSlotDotRuntimeVisibility();
    }

    private void SyncSleepAwakeRuntimeVisibility(bool force = false)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!force
            && sleepAwakeRuntimeStateInitialized
            && lastRuntimeShowSleepAwake == showSleepAwake)
        {
            return;
        }

        sleepAwakeRuntimeStateInitialized = true;
        lastRuntimeShowSleepAwake = showSleepAwake;
        PortableObject.RefreshAllSleepAwakeVisuals();
        RobotArm.RefreshAllSleepAwakeDebugVisuals();
        TerrainGenerator.Active?.RefreshSleepAwakeRuntimeVisibility();
    }

    private void SyncBeltItemLineRuntimeVisibility(bool force = false)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!force
            && beltItemLineRuntimeStateInitialized
            && lastRuntimeShowBeltItemLine == showBeltItemLine)
        {
            return;
        }

        beltItemLineRuntimeStateInitialized = true;
        lastRuntimeShowBeltItemLine = showBeltItemLine;
        TerrainGenerator activeTerrain = TerrainGenerator.Active;
        if (activeTerrain != null)
        {
            activeTerrain.RefreshBeltItemLineRuntimeVisibility();
        }
        else
        {
            PortableObject.RefreshAllBeltItemLineDebugVisuals();
        }
    }

    private void SyncBeltItemRenderingRuntimeVisibility(bool force = false)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!force
            && beltItemRenderingRuntimeStateInitialized
            && lastRuntimeHideBeltItems == hideBeltItems)
        {
            return;
        }

        beltItemRenderingRuntimeStateInitialized = true;
        lastRuntimeHideBeltItems = hideBeltItems;
        TerrainGenerator.Active?.RefreshBeltItemRenderingVisibility();
    }

    private void SyncBeltRenderingRuntimeVisibility(bool force = false)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!force
            && beltRenderingRuntimeStateInitialized
            && lastRuntimeHideBelts == hideBelts)
        {
            return;
        }

        beltRenderingRuntimeStateInitialized = true;
        lastRuntimeHideBelts = hideBelts;
        TerrainGenerator.Active?.RefreshBeltRenderingVisibility();
    }

    private void SyncRailLineRuntimeVisibility(bool force = false)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!force
            && railLineRuntimeStateInitialized
            && lastRuntimeShowRailLine == showRailLine)
        {
            return;
        }

        railLineRuntimeStateInitialized = true;
        lastRuntimeShowRailLine = showRailLine;
        RailLineDebugRenderer renderer = ResolveRailLineDebugRenderer();
        if (renderer != null)
        {
            renderer.SetVisible(showRailLine);
        }
    }

    private RailLineDebugRenderer ResolveRailLineDebugRenderer()
    {
        if (railLineDebugRenderer != null)
        {
            return railLineDebugRenderer;
        }

        railLineDebugRenderer = GetComponent<RailLineDebugRenderer>();
        if (railLineDebugRenderer == null)
        {
            railLineDebugRenderer = gameObject.AddComponent<RailLineDebugRenderer>();
        }

        return railLineDebugRenderer;
    }

    private void SyncBeltDirectionRuntimeVisibility(bool force = false)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!force
            && beltDirectionRuntimeStateInitialized
            && lastRuntimeShowBeltDirections == showDirections)
        {
            return;
        }

        beltDirectionRuntimeStateInitialized = true;
        lastRuntimeShowBeltDirections = showDirections;
        TerrainGenerator.Active?.RefreshBeltDirectionRuntimeVisibility();
    }

    private void SyncFreeCameraRuntimeState(bool force = false)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!force
            && freeCameraRuntimeStateInitialized
            && lastRuntimeFreeCamera == freeCamera)
        {
            return;
        }

        PlayerCamera playerCamera = FindObjectOfType<PlayerCamera>();
        if (playerCamera == null)
        {
            return;
        }

        freeCameraRuntimeStateInitialized = true;
        lastRuntimeFreeCamera = freeCamera;
        playerCamera.SetFreeCameraEnabled(freeCamera);
    }

    private void SyncFreeElectroEnergyRuntimeState(bool force = false)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!force
            && freeElectroEnergyRuntimeStateInitialized
            && lastRuntimeFreeElectroEnergy == freeElectroEnergy)
        {
            return;
        }

        freeElectroEnergyRuntimeStateInitialized = true;
        lastRuntimeFreeElectroEnergy = freeElectroEnergy;
        UtilityPole.NotifyFreeElectroEnergyChanged();
    }

    private void SyncMapObjectTickProfilingRuntimeState(bool force = false)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!force
            && mapObjectTickProfilingRuntimeStateInitialized
            && lastRuntimeMapObjectTickProfilingEnabled == mapObjectTickProfilingEnabled)
        {
            return;
        }

        mapObjectTickProfilingRuntimeStateInitialized = true;
        lastRuntimeMapObjectTickProfilingEnabled = mapObjectTickProfilingEnabled;
        if (!mapObjectTickProfilingEnabled)
        {
            MapObjectTickProfiler.Reset();
        }
    }

    private void ConfigureRuntimeItemGiveReceiver()
    {
        RuntimeItemGiveReceiver receiver = GetComponent<RuntimeItemGiveReceiver>();
        if (!runtimeItemGiveServerEnabled)
        {
            if (receiver != null)
            {
                receiver.StopServer();
            }

            return;
        }

        if (receiver == null)
        {
            receiver = gameObject.AddComponent<RuntimeItemGiveReceiver>();
        }

        receiver.Configure(runtimeItemGiveServerPort);
    }
}

public sealed class RuntimeItemGiveReceiver : MonoBehaviour
{
    public const int DefaultPort = 50877;
    private const int MaxItemsPerRequest = 1000;
    private const int ConveyorLineDefaultCount = 100;
    private const int ConveyorStressDefaultCount = 1000;
    private const int MaxConveyorsPerRequest = 1000;
    private const int ConveyorItemFillDefaultCount = 50;
    private const int MaxConveyorItemsPerRequest = 500;
    private const int AnimalStressDefaultCount = 100;
    private const int MaxAnimalsPerRequest = 2000;
    private const int AnimalThreatDefaultRadius = 20;
    private const int ConveyorLineFillSearchLimit = 4096;
    private const float ConveyorItemFillSearchRadius = 32f;
    private const int RequestTimeoutMilliseconds = 30000;
    private const int MaxRequestsPerFrame = 4;
    private const int MaxRequestsPerFrameDuringChunkStreaming = 1;
    private const float StatusWorldStatsRefreshInterval = 1f;
    private const float StatusSaveSlotRefreshInterval = 5f;
    private const float PlayerSpeedSampleInterval = 0.2f;
    private const float PlayerTeleportDistanceThreshold = 5f;
    private const int RuntimeProfilerRecorderCapacity = 128;
    private const int RuntimeProfilerRecorderRelevantNamesMaxLength = 360;
    private const ProfilerRecorderOptions RuntimeProfilerRecorderBaseOptions =
        ProfilerRecorderOptions.StartImmediately
        | ProfilerRecorderOptions.WrapAroundWhenCapacityReached
        | ProfilerRecorderOptions.SumAllSamplesInFrame;

    private static readonly Vector2Int[] ConveyorLineCardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private readonly Queue<ToolRequest> pendingRequests = new Queue<ToolRequest>();
    private readonly object pendingRequestLock = new object();
    private readonly Dictionary<int, int> installationCountsByItemId = new Dictionary<int, int>();
    private readonly List<KeyValuePair<int, int>> installationCountSortBuffer = new List<KeyValuePair<int, int>>();
    private readonly StringBuilder installationCountTokenBuilder = new StringBuilder(128);
    private readonly FrameTiming[] frameTimingBuffer = new FrameTiming[1];
    private readonly List<RuntimeProfilerRecorder> runtimeProfilerRecorders = new List<RuntimeProfilerRecorder>();
    private readonly List<ProfilerRecorderHandle> runtimeProfilerRecorderHandles = new List<ProfilerRecorderHandle>(256);
    private readonly Dictionary<string, ProfilerRecorderHandle> runtimeProfilerRecorderHandlesByKey = new Dictionary<string, ProfilerRecorderHandle>();
    private readonly StringBuilder runtimeProfilerRecorderTextBuilder = new StringBuilder(512);
    private TcpListener listener;
    private Thread listenerThread;
    private int port = DefaultPort;
    private float fpsSampleElapsed;
    private int fpsSampleFrames;
    private float currentFps;
    private float currentFrameMs;
    private Player trackedSpeedPlayer;
    private PlayerController trackedSpeedPlayerController;
    private Transform trackedPlayerSpeedSource;
    private Vector3 lastPlayerSpeedPosition;
    private float playerSpeedSampleDistance;
    private float playerSpeedSampleElapsed;
    private float currentPlayerSpeed;
    private bool hasPlayerSpeedSample;
    private float cachedStatusWorldStatsTime = float.NegativeInfinity;
    private int cachedInstalledObjectTotal;
    private int cachedConveyorItemTotal;
    private string cachedInstallationTypeCounts = "-";
    private float cachedSaveSlotsStatusTime = float.NegativeInfinity;
    private int cachedSaveSlotsSelectedSlotIndex = -1;
    private string cachedSaveSlotsExtraTokens = string.Empty;
    private SaveManager cachedSaveManager;
    private PlayerCamera cachedPlayerCamera;
    private bool hasGcCollectionSnapshot;
    private int lastGen0CollectionCount;
    private int lastGen1CollectionCount;
    private int lastGen2CollectionCount;
    private bool runtimeProfilerRecordersInitialized;
    private int availableRuntimeProfilerRecorderCount;
    private string availableRuntimeProfilerRecorderRelevantNames = string.Empty;
    private bool runtimeProfilerRecorderRelevantNamesTruncated;
    private volatile bool stopRequested;

    private static readonly RuntimeProfilerRecorderSpec[] RuntimeProfilerRecorderSpecs =
    {
        new RuntimeProfilerRecorderSpec(
            "ProfilerCPU",
            "MainThreadMs",
            RuntimeProfilerRecorderUnit.NanosecondsToMilliseconds,
            RuntimeProfilerRecorderCandidate.Internal("Main Thread")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerCPU",
            "RenderThreadMs",
            RuntimeProfilerRecorderUnit.NanosecondsToMilliseconds,
            RuntimeProfilerRecorderCandidate.Internal("Render Thread")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerCPU",
            "WaitForTargetFpsMs",
            RuntimeProfilerRecorderUnit.NanosecondsToMilliseconds,
            RuntimeProfilerRecorderCandidate.CustomCategory("VSync", "WaitForTargetFPS"),
            RuntimeProfilerRecorderCandidate.Internal("WaitForTargetFPS"),
            RuntimeProfilerRecorderCandidate.Internal("WaitForTargetFPS.FreeTime")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerCPU",
            "GfxWaitForPresentMs",
            RuntimeProfilerRecorderUnit.NanosecondsToMilliseconds,
            RuntimeProfilerRecorderCandidate.Render("Gfx.WaitForPresentOnGfxThread"),
            RuntimeProfilerRecorderCandidate.Internal("Gfx.WaitForPresentOnGfxThread"),
            RuntimeProfilerRecorderCandidate.Render("Gfx.PresentFrame"),
            RuntimeProfilerRecorderCandidate.Internal("Gfx.PresentFrame")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerRender",
            "CameraRenderMs",
            RuntimeProfilerRecorderUnit.NanosecondsToMilliseconds,
            RuntimeProfilerRecorderCandidate.Render("Camera.Render"),
            RuntimeProfilerRecorderCandidate.Internal("Camera.Render")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerRender",
            "RenderLoopDrawMs",
            RuntimeProfilerRecorderUnit.NanosecondsToMilliseconds,
            RuntimeProfilerRecorderCandidate.Render("RenderLoop.Draw"),
            RuntimeProfilerRecorderCandidate.Internal("RenderLoop.Draw")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerGPU",
            "FrameGpuMs",
            RuntimeProfilerRecorderUnit.NanosecondsToMilliseconds,
            RuntimeProfilerRecorderCandidate.Render("FrameTime.GPU")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerRender",
            "DrawCalls",
            RuntimeProfilerRecorderUnit.Count,
            RuntimeProfilerRecorderCandidate.Render("Draw Calls Count")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerRender",
            "Batches",
            RuntimeProfilerRecorderUnit.Count,
            RuntimeProfilerRecorderCandidate.Render("Batches Count")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerRender",
            "SetPassCalls",
            RuntimeProfilerRecorderUnit.Count,
            RuntimeProfilerRecorderCandidate.Render("SetPass Calls Count")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerRender",
            "Triangles",
            RuntimeProfilerRecorderUnit.Count,
            RuntimeProfilerRecorderCandidate.Render("Triangles Count")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerRender",
            "Vertices",
            RuntimeProfilerRecorderUnit.Count,
            RuntimeProfilerRecorderCandidate.Render("Vertices Count")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerRender",
            "ShadowCasters",
            RuntimeProfilerRecorderUnit.Count,
            RuntimeProfilerRecorderCandidate.Render("Shadow Casters Count")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerMemory",
            "GcAllocFrameKB",
            RuntimeProfilerRecorderUnit.BytesToKilobytes,
            RuntimeProfilerRecorderCandidate.Memory("GC Allocated In Frame")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerGC",
            "GcCollectMs",
            RuntimeProfilerRecorderUnit.NanosecondsToMilliseconds,
            RuntimeProfilerRecorderCandidate.CustomCategory("GC", "GC.Collect")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerMemory",
            "GcUsedMB",
            RuntimeProfilerRecorderUnit.BytesToMegabytes,
            RuntimeProfilerRecorderCandidate.Memory("GC Used Memory")),
        new RuntimeProfilerRecorderSpec(
            "ProfilerMemory",
            "TotalUsedMB",
            RuntimeProfilerRecorderUnit.BytesToMegabytes,
            RuntimeProfilerRecorderCandidate.Memory("Total Used Memory"))
    };

    public void Configure(int listenPort)
    {
        int resolvedPort = Mathf.Clamp(listenPort, 1, 65535);
        if (listener != null && port == resolvedPort)
        {
            return;
        }

        port = resolvedPort;
        StartServer();
    }

    public void StopServer()
    {
        stopRequested = true;
        DisposeRuntimeProfilerRecorders();

        try
        {
            listener?.Stop();
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        listener = null;
        if (listenerThread != null && listenerThread.IsAlive)
        {
            listenerThread.Join(100);
        }

        listenerThread = null;
    }

    private void OnDestroy()
    {
        StopServer();
    }

    private void Update()
    {
        UpdateFrameStats();
        UpdatePlayerSpeed();

        int processedCount = 0;
        int maxRequestsThisFrame = IsTerrainChunkStreamingBusy()
            ? MaxRequestsPerFrameDuringChunkStreaming
            : MaxRequestsPerFrame;
        while (processedCount < maxRequestsThisFrame && TryDequeueRequest(out ToolRequest request))
        {
            try
            {
                ProcessRequest(request);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                request.Result = ToolResult.Error(0, 0, "request processing failed");
            }
            finally
            {
                request.Complete();
            }

            processedCount++;
        }
    }

    private void ProcessRequest(ToolRequest request)
    {
        switch (request.Command)
        {
            case ToolCommand.Ping:
                request.Result = ToolResult.Ping();
                break;
            case ToolCommand.Status:
                request.Result = GetStatusResult();
                break;
            case ToolCommand.TimeStatus:
                request.Result = GetWorldTimeStatusResult();
                break;
            case ToolCommand.TimeSet:
                request.Result = SetWorldTime(request.TimeParameters.Hour, request.TimeParameters.Minute);
                break;
            case ToolCommand.TimeScale:
                request.Result = SetWorldTimeScale(request.TimeParameters.Scale);
                break;
            case ToolCommand.TimePause:
                request.Result = SetWorldTimePaused(request.TimeParameters.Paused);
                break;
            case ToolCommand.TimeNextSunrise:
                request.Result = AdvanceWorldTimeToNextSunrise();
                break;
            case ToolCommand.TimeCheck:
                request.Result = CheckWorldTime();
                break;
            case ToolCommand.SetDebugToggle:
                request.Result = SetDebugToggle(request.DebugToggleName, request.DebugToggleValue);
                break;
            case ToolCommand.SetCameraSizeRange:
                request.Result = SetCameraSizeRange(request.CameraMinSize, request.CameraMaxSize);
                break;
            case ToolCommand.SetSeed:
                request.Result = SetTerrainSeed(request.SeedValue);
                break;
            case ToolCommand.CreateConveyorLine:
                request.Result = CreateConveyorLine(request.ItemId, request.Count);
                break;
            case ToolCommand.CreateConveyorStressTest:
                request.Result = CreateConveyorStressTest(request.Count);
                break;
            case ToolCommand.CreateAnimalStressTest:
                request.Result = CreateAnimalStressTest(request.Count);
                break;
            case ToolCommand.CreateAnimalCollisionStressTest:
                request.Result = CreateAnimalCollisionStressTest(request.Count);
                break;
            case ToolCommand.ForceAnimalThreat:
                request.Result = ForceAnimalThreat(request.Count);
                break;
            case ToolCommand.FillConveyorItems:
                request.Result = FillRandomConveyorItems(request.Count);
                break;
            case ToolCommand.CheckConveyors:
                request.Result = CheckConveyors();
                break;
            case ToolCommand.SaveSlot:
                request.Result = SaveSlot(request.SlotIndex);
                break;
            case ToolCommand.LoadSlot:
                request.Result = LoadSlot(request.SlotIndex);
                break;
            case ToolCommand.ResetMap:
                request.Result = ResetMap(request.SlotIndex, request.RandomizeSeed);
                break;
            case ToolCommand.ListSaveSlots:
                request.Result = GetSaveSlotsResult();
                break;
            case ToolCommand.PerfSnapshot:
                request.Result = GetPerfSnapshotResult(request.Count);
                break;
            default:
                request.Result = GiveItems(request.ItemId, request.Count);
                break;
        }
    }

    private void UpdateFrameStats()
    {
        FrameTimingManager.CaptureFrameTimings();

        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        fpsSampleElapsed += deltaTime;
        fpsSampleFrames++;
        if (fpsSampleElapsed < 0.5f)
        {
            return;
        }

        currentFps = fpsSampleFrames / fpsSampleElapsed;
        currentFrameMs = 1000f / Mathf.Max(currentFps, 0.0001f);
        fpsSampleElapsed = 0f;
        fpsSampleFrames = 0;
    }

    private void UpdatePlayerSpeed()
    {
        GameManager gameManager = GameManager.Instance;
        Player currentPlayer = gameManager != null ? gameManager.Player : null;
        if (currentPlayer == null || !currentPlayer.gameObject.activeInHierarchy)
        {
            ResetPlayerSpeedSample(null, null, Vector3.zero);
            return;
        }

        if (trackedSpeedPlayer != currentPlayer)
        {
            trackedSpeedPlayerController = currentPlayer.GetComponent<PlayerController>();
        }

        Transform currentSpeedSource = ResolvePlayerSpeedSource(
            currentPlayer,
            trackedSpeedPlayerController);
        Vector3 currentPosition = currentSpeedSource.position;
        if (trackedSpeedPlayer != currentPlayer
            || trackedPlayerSpeedSource != currentSpeedSource)
        {
            ResetPlayerSpeedSample(currentPlayer, currentSpeedSource, currentPosition);
            return;
        }

        float deltaTime = Time.deltaTime;
        Vector3 movement = currentPosition - lastPlayerSpeedPosition;
        lastPlayerSpeedPosition = currentPosition;
        movement.y = 0f;
        if (deltaTime <= 0f || movement.sqrMagnitude > PlayerTeleportDistanceThreshold * PlayerTeleportDistanceThreshold)
        {
            playerSpeedSampleDistance = 0f;
            playerSpeedSampleElapsed = 0f;
            currentPlayerSpeed = 0f;
            hasPlayerSpeedSample = true;
            return;
        }

        playerSpeedSampleDistance += movement.magnitude;
        playerSpeedSampleElapsed += deltaTime;
        if (playerSpeedSampleElapsed < PlayerSpeedSampleInterval)
        {
            return;
        }

        currentPlayerSpeed = playerSpeedSampleDistance / playerSpeedSampleElapsed;
        playerSpeedSampleDistance = 0f;
        playerSpeedSampleElapsed = 0f;
        hasPlayerSpeedSample = true;
    }

    private static Transform ResolvePlayerSpeedSource(
        Player currentPlayer,
        PlayerController playerController)
    {
        Vehicle mountedVehicle = playerController != null
            ? playerController.MountedVehicle
            : null;
        if (mountedVehicle != null)
        {
            return mountedVehicle.transform;
        }

        Animal mountedAnimal = playerController != null
            ? playerController.MountedAnimal
            : null;
        if (mountedAnimal != null)
        {
            Handcart attachedHandcart = mountedAnimal.AttachedDraftHandcart;
            if (attachedHandcart != null)
            {
                return attachedHandcart.transform;
            }

            Transform movementRoot = mountedAnimal.MovementRoot;
            if (movementRoot != null)
            {
                return movementRoot;
            }
        }

        return currentPlayer.transform;
    }

    private void ResetPlayerSpeedSample(
        Player currentPlayer,
        Transform speedSource,
        Vector3 currentPosition)
    {
        trackedSpeedPlayer = currentPlayer;
        trackedPlayerSpeedSource = speedSource;
        if (currentPlayer == null)
        {
            trackedSpeedPlayerController = null;
        }
        lastPlayerSpeedPosition = currentPosition;
        playerSpeedSampleDistance = 0f;
        playerSpeedSampleElapsed = 0f;
        currentPlayerSpeed = 0f;
        hasPlayerSpeedSample = false;
    }

    private void StartServer()
    {
        StopServer();
        stopRequested = false;

        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch (Exception exception) when (exception is SocketException || exception is InvalidOperationException)
        {
            Debug.LogWarning($"RuntimeItemGiveReceiver: failed to listen on localhost:{port}. {exception.Message}");
            listener = null;
            return;
        }

        EnsureRuntimeProfilerRecorders();
        listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "RuntimeItemGiveReceiver"
        };
        listenerThread.Start();
        Debug.Log($"RuntimeItemGiveReceiver: listening on localhost:{port}");
    }

    private void ListenLoop()
    {
        while (!stopRequested)
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
            catch (SocketException)
            {
                if (!stopRequested)
                {
                    Thread.Sleep(100);
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        {
            client.ReceiveTimeout = RequestTimeoutMilliseconds;
            client.SendTimeout = RequestTimeoutMilliseconds;

            using NetworkStream stream = client.GetStream();
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
            using StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)
            {
                AutoFlush = true
            };

            string line;
            try
            {
                line = reader.ReadLine();
            }
            catch (IOException)
            {
                return;
            }

            if (!TryParseRequest(
                    line,
                    out ToolCommand command,
                    out int itemId,
                    out int count,
                    out int slotIndex,
                    out string debugToggleName,
                    out bool debugToggleValue,
                    out float cameraMinSize,
                    out float cameraMaxSize,
                    out int seedValue,
                    out bool randomizeSeed,
                    out WorldTimeToolParameters timeParameters,
                    out string error))
            {
                writer.WriteLine($"error {error}");
                return;
            }

            ToolRequest request = new ToolRequest(
                command,
                itemId,
                count,
                slotIndex,
                debugToggleName,
                debugToggleValue,
                cameraMinSize,
                cameraMaxSize,
                seedValue,
                randomizeSeed,
                timeParameters);
            EnqueueRequest(request);
            try
            {
                if (!request.WaitForCompletion(RequestTimeoutMilliseconds))
                {
                    writer.WriteLine("error timed out waiting for the Unity main thread");
                    return;
                }

                writer.WriteLine(request.Result.ToProtocolLine());
            }
            finally
            {
                request.ReleaseWaiter();
            }
        }
    }

    private static bool TryParseRequest(
        string line,
        out ToolCommand command,
        out int itemId,
        out int count,
        out int slotIndex,
        out string debugToggleName,
        out bool debugToggleValue,
        out float cameraMinSize,
        out float cameraMaxSize,
        out int seedValue,
        out bool randomizeSeed,
        out WorldTimeToolParameters timeParameters,
        out string error)
    {
        command = ToolCommand.Give;
        itemId = -1;
        count = 1;
        slotIndex = 0;
        debugToggleName = string.Empty;
        debugToggleValue = false;
        cameraMinSize = 0f;
        cameraMaxSize = 0f;
        seedValue = 0;
        randomizeSeed = false;
        timeParameters = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "empty request";
            return false;
        }

        string[] parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && string.Equals(parts[0], "ping", StringComparison.OrdinalIgnoreCase))
        {
            command = ToolCommand.Ping;
            itemId = 0;
            count = 0;
            return true;
        }

        if (parts.Length == 1 && string.Equals(parts[0], "status", StringComparison.OrdinalIgnoreCase))
        {
            command = ToolCommand.Status;
            itemId = 0;
            count = 0;
            return true;
        }

        if (string.Equals(parts[0], "time", StringComparison.OrdinalIgnoreCase))
        {
            itemId = 0;
            count = 0;
            return TryParseWorldTimeRequest(
                parts,
                out command,
                out timeParameters,
                out error);
        }

        if (parts.Length == 1
            && (string.Equals(parts[0], "beltcheck", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "conveyorcheck", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "beltaudit", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.CheckConveyors;
            itemId = 0;
            count = 0;
            return true;
        }

        if (parts.Length >= 1
            && (string.Equals(parts[0], "perf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "profile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "tickprofile", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.PerfSnapshot;
            itemId = 0;
            count = GameManager.Instance != null ? GameManager.Instance.MapObjectTickProfilingMaxRows : 64;
            if (parts.Length >= 2 && (!int.TryParse(parts[1], out count) || count <= 0))
            {
                error = "usage: perf [maxRows]";
                return false;
            }

            if (parts.Length > 2)
            {
                error = "usage: perf [maxRows]";
                return false;
            }

            return true;
        }

        if (parts.Length == 1
            && (string.Equals(parts[0], "saveslots", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "slots", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.ListSaveSlots;
            itemId = 0;
            count = 0;
            return true;
        }

        if (parts.Length == 2
            && (string.Equals(parts[0], "save", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "load", StringComparison.OrdinalIgnoreCase)))
        {
            command = string.Equals(parts[0], "save", StringComparison.OrdinalIgnoreCase)
                ? ToolCommand.SaveSlot
                : ToolCommand.LoadSlot;
            itemId = 0;
            count = 0;

            if (!int.TryParse(parts[1], out int slotNumber)
                || slotNumber < 1
                || slotNumber > SaveManager.SlotCount)
            {
                error = $"slot must be between 1 and {SaveManager.SlotCount}";
                return false;
            }

            slotIndex = slotNumber - 1;
            return true;
        }

        if (parts.Length >= 1
            && (string.Equals(parts[0], "seed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "setseed", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.SetSeed;
            itemId = 0;
            count = 0;

            if (parts.Length != 2 || !int.TryParse(parts[1], out seedValue))
            {
                error = "usage: seed <int>";
                return false;
            }

            return true;
        }

        if (parts.Length >= 1
            && (string.Equals(parts[0], "reset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "resetmap", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "newmap", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.ResetMap;
            itemId = 0;
            count = 0;
            slotIndex = -1;
            randomizeSeed = true;

            if (parts.Length >= 2)
            {
                if (!int.TryParse(parts[1], out int slotNumber)
                    || slotNumber < 1
                    || slotNumber > SaveManager.SlotCount)
                {
                    error = $"slot must be between 1 and {SaveManager.SlotCount}";
                    return false;
                }

                slotIndex = slotNumber - 1;
            }

            if (parts.Length >= 3 && !TryParseProtocolBool(parts[2], out randomizeSeed))
            {
                error = "reset randomSeed value must be true/false or 1/0";
                return false;
            }

            if (parts.Length > 3)
            {
                error = "usage: reset [slot] [randomSeed]";
                return false;
            }

            return true;
        }

        if (parts.Length == 3 && string.Equals(parts[0], "debug", StringComparison.OrdinalIgnoreCase))
        {
            command = ToolCommand.SetDebugToggle;
            itemId = 0;
            count = 0;
            debugToggleName = parts[1];
            if (!TryParseProtocolBool(parts[2], out debugToggleValue))
            {
                error = "debug value must be true/false or 1/0";
                return false;
            }

            return true;
        }

        if (parts.Length == 4
            && string.Equals(parts[0], "camera", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "size", StringComparison.OrdinalIgnoreCase))
        {
            command = ToolCommand.SetCameraSizeRange;
            itemId = 0;
            count = 0;
            if (!TryParseProtocolFloat(parts[2], out cameraMinSize)
                || !TryParseProtocolFloat(parts[3], out cameraMaxSize)
                || cameraMinSize <= 0f
                || cameraMaxSize < cameraMinSize)
            {
                error = "camera size usage: camera size <minSize> <maxSize>, maxSize must be >= minSize";
                return false;
            }

            return true;
        }

        if (parts.Length == 3
            && (string.Equals(parts[0], "camerasize", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "cameraSize", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.SetCameraSizeRange;
            itemId = 0;
            count = 0;
            if (!TryParseProtocolFloat(parts[1], out cameraMinSize)
                || !TryParseProtocolFloat(parts[2], out cameraMaxSize)
                || cameraMinSize <= 0f
                || cameraMaxSize < cameraMinSize)
            {
                error = "camera size usage: camerasize <minSize> <maxSize>, maxSize must be >= minSize";
                return false;
            }

            return true;
        }

        if (parts.Length >= 1
            && (string.Equals(parts[0], "animalstress", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "animalspawn", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.CreateAnimalStressTest;
            itemId = -1;
            count = AnimalStressDefaultCount;

            if (parts.Length >= 2 && (!int.TryParse(parts[1], out count) || count <= 0))
            {
                error = "count must be a positive integer";
                return false;
            }

            if (parts.Length > 2)
            {
                error = "usage: animalstress [count]";
                return false;
            }

            count = Math.Min(Math.Max(count, 1), MaxAnimalsPerRequest);
            return true;
        }

        if (parts.Length >= 1
            && (string.Equals(parts[0], "animalcollision", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "animaljitter", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.CreateAnimalCollisionStressTest;
            itemId = -1;
            count = 12;

            if (parts.Length >= 2 && (!int.TryParse(parts[1], out count) || count <= 0))
            {
                error = "count must be a positive integer";
                return false;
            }

            if (parts.Length > 2)
            {
                error = "usage: animalcollision [count]";
                return false;
            }

            count = Math.Min(Math.Max(count, 1), MaxAnimalsPerRequest);
            return true;
        }

        if (parts.Length >= 1
            && string.Equals(parts[0], "animalthreat", StringComparison.OrdinalIgnoreCase))
        {
            command = ToolCommand.ForceAnimalThreat;
            itemId = -1;
            count = AnimalThreatDefaultRadius;

            if (parts.Length >= 2 && (!int.TryParse(parts[1], out count) || count <= 0))
            {
                error = "radius must be a positive integer";
                return false;
            }

            if (parts.Length > 2)
            {
                error = "usage: animalthreat [radius]";
                return false;
            }

            return true;
        }

        if (parts.Length >= 1
            && (string.Equals(parts[0], "beltstress", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "conveyorstress", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.CreateConveyorStressTest;
            itemId = -1;
            count = ConveyorStressDefaultCount;

            if (parts.Length >= 2 && (!int.TryParse(parts[1], out count) || count <= 0))
            {
                error = "count must be a positive integer";
                return false;
            }

            if (parts.Length > 2)
            {
                error = "usage: beltstress [count]";
                return false;
            }

            count = Math.Min(Math.Max(count, 1), MaxConveyorsPerRequest);
            return true;
        }

        if (parts.Length >= 1
            && (string.Equals(parts[0], "beltline", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "conveyorline", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.CreateConveyorLine;
            itemId = -1;
            count = ConveyorLineDefaultCount;

            if (parts.Length >= 2
                && !string.Equals(parts[1], "auto", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parts[1], "belt", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parts[1], "conveyor", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(parts[1], out itemId) || itemId < 0)
                {
                    error = "itemId must be a non-negative integer or auto";
                    return false;
                }
            }

            if (parts.Length >= 3 && (!int.TryParse(parts[2], out count) || count <= 0))
            {
                error = "count must be a positive integer";
                return false;
            }

            if (parts.Length > 3)
            {
                error = "usage: beltline [auto|itemId] [count]";
                return false;
            }

            count = Math.Min(Math.Max(count, 1), MaxConveyorsPerRequest);
            return true;
        }

        if (parts.Length >= 1
            && (string.Equals(parts[0], "beltitems", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "conveyoritems", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.FillConveyorItems;
            itemId = -1;
            count = ConveyorItemFillDefaultCount;

            if (parts.Length >= 2 && (!int.TryParse(parts[1], out count) || count <= 0))
            {
                error = "count must be a positive integer";
                return false;
            }

            if (parts.Length > 2)
            {
                error = "usage: beltitems [count]";
                return false;
            }

            count = Math.Min(Math.Max(count, 1), MaxConveyorItemsPerRequest);
            return true;
        }

        if (parts.Length < 2 || !string.Equals(parts[0], "give", StringComparison.OrdinalIgnoreCase))
        {
            error = "usage: give <itemId> [count] | animalstress [count] | animalcollision [count] | animalthreat [radius] | beltstress [count] | beltline [auto|itemId] [count] | beltitems [count] | beltcheck | save <slot> | load <slot> | reset [slot] [randomSeed] | seed <int> | saveslots | time <status|set|scale|pause|next sunrise|check> | debug <showConveyorSlotDots|showSleepAwake|showBeltItemLine|hideBeltItems|hideBelts|showRailLine|showDirections|freeCamera|showAnimalHerdAreas|animalAIPaused|mapObjectTickProfiling> <true|false> | camera size <minSize> <maxSize> | perf [maxRows] | ping | status";
            return false;
        }

        if (!int.TryParse(parts[1], out itemId) || itemId < 0)
        {
            error = "itemId must be a non-negative integer";
            return false;
        }

        if (parts.Length >= 3 && (!int.TryParse(parts[2], out count) || count <= 0))
        {
            error = "count must be a positive integer";
            return false;
        }

        count = Math.Min(Math.Max(count, 1), MaxItemsPerRequest);
        return true;
    }

    private static bool TryParseWorldTimeRequest(
        string[] parts,
        out ToolCommand command,
        out WorldTimeToolParameters parameters,
        out string error)
    {
        command = ToolCommand.TimeStatus;
        parameters = default;
        error = string.Empty;

        if (parts.Length == 2
            && string.Equals(parts[1], "status", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (parts.Length == 2
            && string.Equals(parts[1], "check", StringComparison.OrdinalIgnoreCase))
        {
            command = ToolCommand.TimeCheck;
            return true;
        }

        if ((parts.Length == 2
             && string.Equals(parts[1], "nextsunrise", StringComparison.OrdinalIgnoreCase))
            || (parts.Length == 3
                && string.Equals(parts[1], "next", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[2], "sunrise", StringComparison.OrdinalIgnoreCase)))
        {
            command = ToolCommand.TimeNextSunrise;
            return true;
        }

        if (parts.Length == 3
            && string.Equals(parts[1], "set", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseClockText(parts[2], out int hour, out int minute))
            {
                error = "time set usage: time set <HH:mm>";
                return false;
            }

            command = ToolCommand.TimeSet;
            parameters = WorldTimeToolParameters.ForTime(hour, minute);
            return true;
        }

        if (parts.Length == 3
            && string.Equals(parts[1], "scale", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseProtocolFloat(parts[2], out float scale) || scale <= 0f)
            {
                error = "time scale must be a positive number";
                return false;
            }

            command = ToolCommand.TimeScale;
            parameters = WorldTimeToolParameters.ForScale(scale);
            return true;
        }

        if (parts.Length == 3
            && string.Equals(parts[1], "pause", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseProtocolBool(parts[2], out bool paused))
            {
                error = "time pause value must be true/false or 1/0";
                return false;
            }

            command = ToolCommand.TimePause;
            parameters = WorldTimeToolParameters.ForPause(paused);
            return true;
        }

        error = "usage: time status | time set <HH:mm> | time scale <value> | time pause <true|false> | time next sunrise | time check";
        return false;
    }

    private static bool TryParseClockText(string value, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0
            || separatorIndex >= value.Length - 1
            || !int.TryParse(value.Substring(0, separatorIndex), out hour)
            || !int.TryParse(value.Substring(separatorIndex + 1), out minute))
        {
            return false;
        }

        return hour >= 0
               && hour < WorldTimeService.HoursPerDay
               && minute >= 0
               && minute < WorldTimeService.MinutesPerHour;
    }

    private static bool TryParseProtocolBool(string value, out bool result)
    {
        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryParseProtocolFloat(string value, out float result)
    {
        return float.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private void EnqueueRequest(ToolRequest request)
    {
        lock (pendingRequestLock)
        {
            pendingRequests.Enqueue(request);
        }
    }

    private bool TryDequeueRequest(out ToolRequest request)
    {
        lock (pendingRequestLock)
        {
            if (pendingRequests.Count <= 0)
            {
                request = null;
                return false;
            }

            request = pendingRequests.Dequeue();
            return true;
        }
    }

    private ToolResult GetStatusResult()
    {
        float fps = currentFps;
        float frameMs = currentFrameMs;
        if (fps <= 0f && Time.unscaledDeltaTime > 0f)
        {
            fps = 1f / Time.unscaledDeltaTime;
            frameMs = Time.unscaledDeltaTime * 1000f;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        bool isChunkStreamingBusy = terrain != null && terrain.IsChunkStreamingBusy;
        CaptureWorldStats(
            terrain,
            isChunkStreamingBusy,
            out int installedObjectTotal,
            out int conveyorItemTotal,
            out string installationTypeCounts);
        GameManager gameManager = GameManager.Instance;
        bool currentShowConveyorSlotDots = gameManager != null && gameManager.ShowConveyorSlotDots;
        bool currentShowSleepAwake = gameManager != null && gameManager.ShowSleepAwake;
        bool currentShowBeltItemLine = gameManager != null && gameManager.ShowBeltItemLine;
        bool currentHideBeltItems = gameManager != null && gameManager.HideBeltItems;
        bool currentHideBelts = gameManager != null && gameManager.HideBelts;
        bool currentShowRailLine = gameManager != null && gameManager.ShowRailLine;
        bool currentShowBeltDirections = gameManager != null && gameManager.ShowDirections;
        string extraTokens = BuildStatusExtraTokens(
            ResolveSaveManager(),
            ResolvePlayerCamera(),
            terrain,
            isChunkStreamingBusy);
        return ToolResult.Status(
            fps,
            frameMs,
            installedObjectTotal,
            conveyorItemTotal,
            installationTypeCounts,
            currentShowConveyorSlotDots,
            currentShowSleepAwake,
            currentShowBeltItemLine,
            currentHideBeltItems,
            currentHideBelts,
            currentShowRailLine,
            currentShowBeltDirections,
            extraTokens);
    }

    private ToolResult GetWorldTimeStatusResult()
    {
        WorldTimeService worldTime = ResolveWorldTime();
        return worldTime != null
            ? ToolResult.Success(0, 0, 0, 0, 0, 0, "time status", BuildWorldTimeExtraTokens(worldTime))
            : ToolResult.Error(0, 0, "world time service not found");
    }

    private ToolResult SetWorldTime(int hour, int minute)
    {
        WorldTimeService worldTime = ResolveWorldTime();
        if (worldTime == null)
        {
            return ToolResult.Error(0, 0, "world time service not found");
        }

        if (!worldTime.TrySetTimeOfDay(hour, minute))
        {
            return ToolResult.Error(0, 0, "invalid world time");
        }

        return ToolResult.Success(0, 0, 0, 0, 0, 0, "time set", BuildWorldTimeExtraTokens(worldTime));
    }

    private ToolResult SetWorldTimeScale(float scale)
    {
        WorldTimeService worldTime = ResolveWorldTime();
        if (worldTime == null)
        {
            return ToolResult.Error(0, 0, "world time service not found");
        }

        worldTime.SetTimeScale(scale);
        return ToolResult.Success(0, 0, 0, 0, 0, 0, "time scale", BuildWorldTimeExtraTokens(worldTime));
    }

    private ToolResult SetWorldTimePaused(bool paused)
    {
        WorldTimeService worldTime = ResolveWorldTime();
        if (worldTime == null)
        {
            return ToolResult.Error(0, 0, "world time service not found");
        }

        worldTime.SetPaused(paused);
        return ToolResult.Success(0, 0, 0, 0, 0, 0, "time pause", BuildWorldTimeExtraTokens(worldTime));
    }

    private ToolResult AdvanceWorldTimeToNextSunrise()
    {
        WorldTimeService worldTime = ResolveWorldTime();
        if (worldTime == null)
        {
            return ToolResult.Error(0, 0, "world time service not found");
        }

        worldTime.AdvanceToNextSunrise();
        return ToolResult.Success(0, 0, 0, 0, 0, 0, "next sunrise", BuildWorldTimeExtraTokens(worldTime));
    }

    private ToolResult CheckWorldTime()
    {
        WorldTimeService worldTime = ResolveWorldTime();
        if (worldTime == null)
        {
            return ToolResult.Error(0, 0, "world time service not found", "timeCheckErrors=1");
        }

        if (!WorldTimeService.RunCalculationSelfCheck(out string firstIssue)
            || !worldTime.TryValidateState(out firstIssue)
            || !SaveGameBinarySerializer.RunWorldTimeRoundTripSelfCheck(out firstIssue))
        {
            return ToolResult.Error(
                0,
                0,
                $"time check failed first={firstIssue}",
                BuildExtraTokens("timeCheckErrors=1", BuildWorldTimeExtraTokens(worldTime)));
        }

        return ToolResult.Success(
            0,
            0,
            0,
            0,
            0,
            0,
            "time check healthy",
            BuildExtraTokens("timeCheckErrors=0", BuildWorldTimeExtraTokens(worldTime)));
    }

    private ToolResult GetSaveSlotsResult()
    {
        SaveManager saveManager = ResolveSaveManager();
        if (saveManager == null)
        {
            return ToolResult.Error(0, 0, "save manager not found", BuildEmptySaveSlotsExtraTokens());
        }

        return ToolResult.Success(
            0,
            0,
            0,
            0,
            0,
            0,
            "save slots",
            BuildSaveSlotsExtraTokens(saveManager, true));
    }

    private ToolResult GetPerfSnapshotResult(int maxRows)
    {
        int resolvedMaxRows = Mathf.Max(1, maxRows);
        EnsureRuntimeProfilerRecorders();
        MapObjectTickProfiler.ClearRuntimeCounters();
        AppendFrameRuntimeProfilerCounters();
        TerrainGenerator.Active?.AppendRuntimeProfilerCounters();
        string json = MapObjectTickProfiler.BuildAndResetSnapshotJson(resolvedMaxRows);
        string encodedJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return ToolResult.Success(
            0,
            0,
            0,
            0,
            0,
            0,
            "perf",
            $"perfData={encodedJson}");
    }

    private void AppendFrameRuntimeProfilerCounters()
    {
        float fps = currentFps;
        float frameMs = currentFrameMs;
        if (fps <= 0f && Time.unscaledDeltaTime > 0f)
        {
            fps = 1f / Time.unscaledDeltaTime;
            frameMs = Time.unscaledDeltaTime * 1000f;
        }

        MapObjectTickProfiler.AddRuntimeCounter("Frame", "Fps", fps);
        MapObjectTickProfiler.AddRuntimeCounter("Frame", "FrameMs", frameMs);
        MapObjectTickProfiler.AddRuntimeCounter("Frame", "UnscaledDeltaMs", Time.unscaledDeltaTime * 1000f);
        MapObjectTickProfiler.AddRuntimeCounter("Frame", "SmoothDeltaMs", Time.smoothDeltaTime * 1000f);
        MapObjectTickProfiler.AddRuntimeCounter("Frame", "TimeScale", Time.timeScale);
        MapObjectTickProfiler.AddRuntimeCounter("Frame", "TargetFrameRate", Application.targetFrameRate);
        MapObjectTickProfiler.AddRuntimeCounter("Frame", "VSyncCount", QualitySettings.vSyncCount);
        MapObjectTickProfiler.AddRuntimeCounter("Frame", "ScreenRefreshRate", FormatScreenRefreshRate());
        MapObjectTickProfiler.AddRuntimeCounter("Frame", "IsEditor", Application.isEditor);

        uint frameTimingCount = FrameTimingManager.GetLatestTimings((uint)frameTimingBuffer.Length, frameTimingBuffer);
        MapObjectTickProfiler.AddRuntimeCounter("FrameTiming", "Samples", (int)frameTimingCount);
        if (frameTimingCount > 0)
        {
            FrameTiming timing = frameTimingBuffer[0];
            AddFrameTimingCounter("CpuFrameMs", timing, "cpuFrameTime");
            AddFrameTimingCounter("CpuMainThreadMs", timing, "cpuMainThreadFrameTime");
            AddFrameTimingCounter("CpuRenderThreadMs", timing, "cpuRenderThreadFrameTime");
            AddFrameTimingCounter("CpuPresentWaitMs", timing, "cpuMainThreadPresentWaitTime");
            AddFrameTimingCounter("GpuFrameMs", timing, "gpuFrameTime");
        }
        else
        {
            AddUnavailableFrameTimingCounters();
        }

        AppendMemoryRuntimeProfilerCounters();
        AppendProfilerRecorderRuntimeCounters();
        AppendEditorRenderRuntimeProfilerCounters();
    }

    private void AppendMemoryRuntimeProfilerCounters()
    {
        int gen0CollectionCount = GC.CollectionCount(0);
        int gen1CollectionCount = GC.CollectionCount(1);
        int gen2CollectionCount = GC.CollectionCount(2);
        int gen0Delta = hasGcCollectionSnapshot ? Mathf.Max(0, gen0CollectionCount - lastGen0CollectionCount) : 0;
        int gen1Delta = hasGcCollectionSnapshot ? Mathf.Max(0, gen1CollectionCount - lastGen1CollectionCount) : 0;
        int gen2Delta = hasGcCollectionSnapshot ? Mathf.Max(0, gen2CollectionCount - lastGen2CollectionCount) : 0;
        hasGcCollectionSnapshot = true;
        lastGen0CollectionCount = gen0CollectionCount;
        lastGen1CollectionCount = gen1CollectionCount;
        lastGen2CollectionCount = gen2CollectionCount;

        MapObjectTickProfiler.AddRuntimeCounter("Memory", "ManagedHeapMB", FormatBytesMb(GC.GetTotalMemory(false)));
        MapObjectTickProfiler.AddRuntimeCounter("Memory", "UnityAllocatedMB", FormatBytesMb(Profiler.GetTotalAllocatedMemoryLong()));
        MapObjectTickProfiler.AddRuntimeCounter("Memory", "UnityReservedMB", FormatBytesMb(Profiler.GetTotalReservedMemoryLong()));
        MapObjectTickProfiler.AddRuntimeCounter("Memory", "UnityUnusedReservedMB", FormatBytesMb(Profiler.GetTotalUnusedReservedMemoryLong()));
        MapObjectTickProfiler.AddRuntimeCounter("GC", "Gen0Total", gen0CollectionCount);
        MapObjectTickProfiler.AddRuntimeCounter("GC", "Gen1Total", gen1CollectionCount);
        MapObjectTickProfiler.AddRuntimeCounter("GC", "Gen2Total", gen2CollectionCount);
        MapObjectTickProfiler.AddRuntimeCounter("GC", "Gen0Delta", gen0Delta);
        MapObjectTickProfiler.AddRuntimeCounter("GC", "Gen1Delta", gen1Delta);
        MapObjectTickProfiler.AddRuntimeCounter("GC", "Gen2Delta", gen2Delta);
    }

    private static void AppendEditorRenderRuntimeProfilerCounters()
    {
#if UNITY_EDITOR
        Type unityStatsType = Type.GetType("UnityEditor.UnityStats, UnityEditor");
        AddEditorRenderStat(unityStatsType, "DrawCalls", "drawCalls");
        AddEditorRenderStat(unityStatsType, "Batches", "batches");
        AddEditorRenderStat(unityStatsType, "SetPassCalls", "setPassCalls");
        AddEditorRenderStat(unityStatsType, "Triangles", "triangles");
        AddEditorRenderStat(unityStatsType, "Vertices", "vertices");
        AddEditorRenderStat(unityStatsType, "ShadowCasters", "shadowCasters");
        AddEditorRenderStat(unityStatsType, "RenderTextureChanges", "renderTextureChanges");
        AddEditorRenderStat(unityStatsType, "VisibleSkinnedMeshes", "visibleSkinnedMeshes");
#else
        MapObjectTickProfiler.AddRuntimeCounter("RenderStats", "EditorStats", "unavailable", "UNITY_EDITOR only");
#endif
    }

#if UNITY_EDITOR
    private static void AddEditorRenderStat(Type unityStatsType, string counterName, string memberName)
    {
        if (unityStatsType == null || string.IsNullOrWhiteSpace(memberName))
        {
            MapObjectTickProfiler.AddRuntimeCounter("RenderStats", counterName, "n/a", "UnityStats unavailable");
            return;
        }

        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Static;
        object rawValue = null;
        PropertyInfo property = unityStatsType.GetProperty(memberName, bindingFlags);
        if (property != null)
        {
            rawValue = property.GetValue(null, null);
        }
        else
        {
            FieldInfo field = unityStatsType.GetField(memberName, bindingFlags);
            if (field != null)
            {
                rawValue = field.GetValue(null);
            }
        }

        MapObjectTickProfiler.AddRuntimeCounter(
            "RenderStats",
            counterName,
            rawValue != null ? Convert.ToString(rawValue, CultureInfo.InvariantCulture) : "n/a",
            "Editor");
    }
#endif

    private void EnsureRuntimeProfilerRecorders()
    {
        if (runtimeProfilerRecordersInitialized)
        {
            return;
        }

        runtimeProfilerRecordersInitialized = true;
        RefreshAvailableRuntimeProfilerRecorders();

        for (int i = 0; i < RuntimeProfilerRecorderSpecs.Length; i++)
        {
            if (TryCreateRuntimeProfilerRecorder(RuntimeProfilerRecorderSpecs[i], out RuntimeProfilerRecorder recorder))
            {
                runtimeProfilerRecorders.Add(recorder);
            }
        }
    }

    private void DisposeRuntimeProfilerRecorders()
    {
        for (int i = 0; i < runtimeProfilerRecorders.Count; i++)
        {
            runtimeProfilerRecorders[i].Dispose();
        }

        runtimeProfilerRecorders.Clear();
        runtimeProfilerRecorderHandles.Clear();
        runtimeProfilerRecorderHandlesByKey.Clear();
        availableRuntimeProfilerRecorderCount = 0;
        availableRuntimeProfilerRecorderRelevantNames = string.Empty;
        runtimeProfilerRecorderRelevantNamesTruncated = false;
        runtimeProfilerRecordersInitialized = false;
    }

    private void RefreshAvailableRuntimeProfilerRecorders()
    {
        runtimeProfilerRecorderHandles.Clear();
        runtimeProfilerRecorderHandlesByKey.Clear();
        runtimeProfilerRecorderTextBuilder.Length = 0;
        availableRuntimeProfilerRecorderCount = 0;
        availableRuntimeProfilerRecorderRelevantNames = string.Empty;
        runtimeProfilerRecorderRelevantNamesTruncated = false;

        try
        {
            ProfilerRecorderHandle.GetAvailable(runtimeProfilerRecorderHandles);
        }
        catch (Exception exception) when (exception is InvalidOperationException || exception is NotSupportedException)
        {
            availableRuntimeProfilerRecorderRelevantNames = $"discovery failed: {exception.GetType().Name}";
            return;
        }

        availableRuntimeProfilerRecorderCount = runtimeProfilerRecorderHandles.Count;
        for (int i = 0; i < runtimeProfilerRecorderHandles.Count; i++)
        {
            ProfilerRecorderHandle handle = runtimeProfilerRecorderHandles[i];
            if (!handle.Valid)
            {
                continue;
            }

            ProfilerRecorderDescription description;
            try
            {
                description = ProfilerRecorderHandle.GetDescription(handle);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
            {
                continue;
            }

            string counterName = description.Name;
            string categoryName = description.Category.Name;
            if (string.IsNullOrWhiteSpace(counterName) || string.IsNullOrWhiteSpace(categoryName))
            {
                continue;
            }

            string key = BuildRuntimeProfilerRecorderKey(categoryName, counterName);
            if (!runtimeProfilerRecorderHandlesByKey.ContainsKey(key))
            {
                runtimeProfilerRecorderHandlesByKey.Add(key, handle);
            }

            if (IsRelevantRuntimeProfilerRecorderName(categoryName, counterName))
            {
                AppendRelevantRuntimeProfilerRecorderName(categoryName, counterName);
            }
        }

        if (runtimeProfilerRecorderTextBuilder.Length > 0)
        {
            availableRuntimeProfilerRecorderRelevantNames = runtimeProfilerRecorderTextBuilder.ToString();
        }
    }

    private bool TryCreateRuntimeProfilerRecorder(RuntimeProfilerRecorderSpec spec, out RuntimeProfilerRecorder runtimeRecorder)
    {
        runtimeRecorder = null;
        bool allowDirectFallback = availableRuntimeProfilerRecorderCount <= 0;
        for (int i = 0; i < spec.Candidates.Length; i++)
        {
            RuntimeProfilerRecorderCandidate candidate = spec.Candidates[i];
            string categoryName = candidate.Category.Name;
            string key = BuildRuntimeProfilerRecorderKey(categoryName, candidate.CounterName);
            if (runtimeProfilerRecorderHandlesByKey.TryGetValue(key, out ProfilerRecorderHandle handle)
                && TryStartRuntimeProfilerRecorder(spec, candidate, handle, out runtimeRecorder))
            {
                return true;
            }

            if (allowDirectFallback
                && TryStartRuntimeProfilerRecorder(spec, candidate, out runtimeRecorder))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryStartRuntimeProfilerRecorder(
        RuntimeProfilerRecorderSpec spec,
        RuntimeProfilerRecorderCandidate candidate,
        ProfilerRecorderHandle handle,
        out RuntimeProfilerRecorder runtimeRecorder)
    {
        runtimeRecorder = null;
        ProfilerRecorder recorder = default;
        try
        {
            recorder = new ProfilerRecorder(
                handle,
                RuntimeProfilerRecorderCapacity,
                RuntimeProfilerRecorderBaseOptions | spec.ExtraOptions);
            if (!recorder.Valid)
            {
                recorder.Dispose();
                return false;
            }

            if (!recorder.IsRunning)
            {
                recorder.Start();
            }

            runtimeRecorder = new RuntimeProfilerRecorder(spec, candidate, recorder);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            || exception is InvalidOperationException
            || exception is NotSupportedException)
        {
            recorder.Dispose();
            return false;
        }
    }

    private static bool TryStartRuntimeProfilerRecorder(
        RuntimeProfilerRecorderSpec spec,
        RuntimeProfilerRecorderCandidate candidate,
        out RuntimeProfilerRecorder runtimeRecorder)
    {
        runtimeRecorder = null;
        ProfilerRecorder recorder = default;
        try
        {
            recorder = ProfilerRecorder.StartNew(
                candidate.Category,
                candidate.CounterName,
                RuntimeProfilerRecorderCapacity,
                RuntimeProfilerRecorderBaseOptions | spec.ExtraOptions);
            if (!recorder.Valid)
            {
                recorder.Dispose();
                return false;
            }

            runtimeRecorder = new RuntimeProfilerRecorder(spec, candidate, recorder);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            || exception is InvalidOperationException
            || exception is NotSupportedException)
        {
            recorder.Dispose();
            return false;
        }
    }

    private void AppendProfilerRecorderRuntimeCounters()
    {
        MapObjectTickProfiler.AddRuntimeCounter("ProfilerRecorder", "AvailableCounters", availableRuntimeProfilerRecorderCount);
        MapObjectTickProfiler.AddRuntimeCounter("ProfilerRecorder", "ActiveRecorders", runtimeProfilerRecorders.Count);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ProfilerRecorder",
            "MissingRecorders",
            Math.Max(0, RuntimeProfilerRecorderSpecs.Length - runtimeProfilerRecorders.Count));

        if (!string.IsNullOrWhiteSpace(availableRuntimeProfilerRecorderRelevantNames))
        {
            MapObjectTickProfiler.AddRuntimeCounter(
                "ProfilerRecorder",
                "RelevantAvailable",
                availableRuntimeProfilerRecorderRelevantNames);
        }

        for (int i = 0; i < runtimeProfilerRecorders.Count; i++)
        {
            RuntimeProfilerRecorder runtimeRecorder = runtimeProfilerRecorders[i];
            ProfilerRecorder recorder = runtimeRecorder.Recorder;
            if (!recorder.Valid)
            {
                MapObjectTickProfiler.AddRuntimeCounter(
                    runtimeRecorder.Group,
                    runtimeRecorder.Name,
                    "n/a",
                    $"invalid source={runtimeRecorder.CategoryName}/{runtimeRecorder.CounterName}");
                continue;
            }

            if (!recorder.IsRunning)
            {
                recorder.Start();
            }

            if (!TryCalculateRuntimeProfilerRecorderAverage(recorder, out double averageRawValue, out int sampleCount))
            {
                MapObjectTickProfiler.AddRuntimeCounter(
                    runtimeRecorder.Group,
                    runtimeRecorder.Name,
                    "n/a",
                    $"no samples source={runtimeRecorder.CategoryName}/{runtimeRecorder.CounterName}");
                continue;
            }

            string averageValue = FormatRuntimeProfilerRecorderValue(averageRawValue, runtimeRecorder.Unit);
            string lastValue = FormatRuntimeProfilerRecorderValue(recorder.LastValue, runtimeRecorder.Unit);
            MapObjectTickProfiler.AddRuntimeCounter(
                runtimeRecorder.Group,
                runtimeRecorder.Name,
                averageValue,
                $"last={lastValue} samples={sampleCount} source={runtimeRecorder.CategoryName}/{runtimeRecorder.CounterName}");
        }
    }

    private static bool TryCalculateRuntimeProfilerRecorderAverage(
        ProfilerRecorder recorder,
        out double averageRawValue,
        out int sampleCount)
    {
        averageRawValue = 0d;
        sampleCount = recorder.Count;
        if (sampleCount <= 0)
        {
            return false;
        }

        double total = 0d;
        for (int i = 0; i < sampleCount; i++)
        {
            total += recorder.GetSample(i).Value;
        }

        averageRawValue = total / sampleCount;
        return true;
    }

    private void AppendRelevantRuntimeProfilerRecorderName(string categoryName, string counterName)
    {
        string entry = $"{categoryName}/{counterName}";
        int separatorLength = runtimeProfilerRecorderTextBuilder.Length > 0 ? 2 : 0;
        if (runtimeProfilerRecorderTextBuilder.Length + separatorLength + entry.Length > RuntimeProfilerRecorderRelevantNamesMaxLength)
        {
            if (!runtimeProfilerRecorderRelevantNamesTruncated
                && runtimeProfilerRecorderTextBuilder.Length <= RuntimeProfilerRecorderRelevantNamesMaxLength - 3)
            {
                runtimeProfilerRecorderTextBuilder.Append("...");
            }

            runtimeProfilerRecorderRelevantNamesTruncated = true;
            return;
        }

        if (runtimeProfilerRecorderTextBuilder.Length > 0)
        {
            runtimeProfilerRecorderTextBuilder.Append(", ");
        }

        runtimeProfilerRecorderTextBuilder.Append(entry);
    }

    private static bool IsRelevantRuntimeProfilerRecorderName(string categoryName, string counterName)
    {
        string text = $"{categoryName} {counterName}".ToLowerInvariant();
        return text.Contains("thread")
            || text.Contains("render")
            || text.Contains("draw")
            || text.Contains("batch")
            || text.Contains("pass")
            || text.Contains("triangle")
            || text.Contains("vertex")
            || text.Contains("memory")
            || text.Contains("gc")
            || text.Contains("present")
            || text.Contains("wait")
            || text.Contains("gfx")
            || text.Contains("camera");
    }

    private static string BuildRuntimeProfilerRecorderKey(string categoryName, string counterName)
    {
        return $"{categoryName}\n{counterName}";
    }

    private static string FormatRuntimeProfilerRecorderValue(double rawValue, RuntimeProfilerRecorderUnit unit)
    {
        double value = unit switch
        {
            RuntimeProfilerRecorderUnit.NanosecondsToMilliseconds => rawValue / 1000000d,
            RuntimeProfilerRecorderUnit.BytesToMegabytes => rawValue / (1024d * 1024d),
            RuntimeProfilerRecorderUnit.BytesToKilobytes => rawValue / 1024d,
            _ => rawValue
        };

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void AddFrameTimingCounter(string counterName, FrameTiming timing, string memberName)
    {
        if (TryReadFrameTimingMember(timing, memberName, out float value))
        {
            MapObjectTickProfiler.AddRuntimeCounter("FrameTiming", counterName, value);
            return;
        }

        MapObjectTickProfiler.AddRuntimeCounter("FrameTiming", counterName, "n/a");
    }

    private static void AddUnavailableFrameTimingCounters()
    {
        MapObjectTickProfiler.AddRuntimeCounter("FrameTiming", "CpuFrameMs", "n/a");
        MapObjectTickProfiler.AddRuntimeCounter("FrameTiming", "CpuMainThreadMs", "n/a");
        MapObjectTickProfiler.AddRuntimeCounter("FrameTiming", "CpuRenderThreadMs", "n/a");
        MapObjectTickProfiler.AddRuntimeCounter("FrameTiming", "CpuPresentWaitMs", "n/a");
        MapObjectTickProfiler.AddRuntimeCounter("FrameTiming", "GpuFrameMs", "n/a");
    }

    private static bool TryReadFrameTimingMember(FrameTiming timing, string memberName, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance;
        Type timingType = typeof(FrameTiming);
        object rawValue = null;
        FieldInfo field = timingType.GetField(memberName, bindingFlags);
        if (field != null)
        {
            rawValue = field.GetValue(timing);
        }
        else
        {
            PropertyInfo property = timingType.GetProperty(memberName, bindingFlags);
            if (property != null)
            {
                rawValue = property.GetValue(timing, null);
            }
        }

        if (rawValue == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToSingle(rawValue, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException || exception is InvalidCastException || exception is OverflowException)
        {
            value = 0f;
            return false;
        }
    }

    private static string FormatBytesMb(long bytes)
    {
        float mb = Math.Max(0L, bytes) / (1024f * 1024f);
        return mb.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatScreenRefreshRate()
    {
        Resolution resolution = Screen.currentResolution;
        Type resolutionType = typeof(Resolution);
        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance;
        object refreshRateRatio = null;
        PropertyInfo ratioProperty = resolutionType.GetProperty("refreshRateRatio", bindingFlags);
        if (ratioProperty != null)
        {
            refreshRateRatio = ratioProperty.GetValue(resolution, null);
        }
        else
        {
            FieldInfo ratioField = resolutionType.GetField("refreshRateRatio", bindingFlags);
            if (ratioField != null)
            {
                refreshRateRatio = ratioField.GetValue(resolution);
            }
        }

        if (TryFormatRefreshRateRatio(refreshRateRatio, out string ratioText))
        {
            return ratioText;
        }

        object refreshRate = null;
        PropertyInfo refreshRateProperty = resolutionType.GetProperty("refreshRate", bindingFlags);
        if (refreshRateProperty != null)
        {
            refreshRate = refreshRateProperty.GetValue(resolution, null);
        }
        else
        {
            FieldInfo refreshRateField = resolutionType.GetField("refreshRate", bindingFlags);
            if (refreshRateField != null)
            {
                refreshRate = refreshRateField.GetValue(resolution);
            }
        }

        return refreshRate != null
            ? Convert.ToString(refreshRate, CultureInfo.InvariantCulture)
            : "n/a";
    }

    private static bool TryFormatRefreshRateRatio(object refreshRateRatio, out string text)
    {
        text = null;
        if (refreshRateRatio == null)
        {
            return false;
        }

        Type ratioType = refreshRateRatio.GetType();
        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance;
        object numerator = ReadMemberValue(ratioType, refreshRateRatio, "numerator", bindingFlags);
        object denominator = ReadMemberValue(ratioType, refreshRateRatio, "denominator", bindingFlags);
        if (numerator == null || denominator == null)
        {
            text = Convert.ToString(refreshRateRatio, CultureInfo.InvariantCulture);
            return !string.IsNullOrWhiteSpace(text);
        }

        try
        {
            double numeratorValue = Convert.ToDouble(numerator, CultureInfo.InvariantCulture);
            double denominatorValue = Convert.ToDouble(denominator, CultureInfo.InvariantCulture);
            if (denominatorValue <= 0.0)
            {
                return false;
            }

            text = (numeratorValue / denominatorValue).ToString("0.###", CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException || exception is InvalidCastException || exception is OverflowException)
        {
            text = null;
            return false;
        }
    }

    private static object ReadMemberValue(Type targetType, object target, string memberName, BindingFlags bindingFlags)
    {
        PropertyInfo property = targetType.GetProperty(memberName, bindingFlags);
        if (property != null)
        {
            return property.GetValue(target, null);
        }

        FieldInfo field = targetType.GetField(memberName, bindingFlags);
        return field != null ? field.GetValue(target) : null;
    }

    private ToolResult SaveSlot(int slotIndex)
    {
        SaveManager saveManager = ResolveSaveManager();
        if (saveManager == null)
        {
            return ToolResult.Error(0, 0, "save manager not found", BuildEmptySaveSlotsExtraTokens());
        }

        int normalizedSlotIndex = Mathf.Clamp(slotIndex, 0, SaveManager.SlotCount - 1);
        bool saved = saveManager.SaveSlot(normalizedSlotIndex);
        InvalidateSaveSlotStatusCache();
        string extraTokens = BuildSaveSlotsExtraTokens(saveManager, true);
        return saved
            ? ToolResult.Success(0, 0, 0, 0, 0, 0, $"saved slot {normalizedSlotIndex + 1}", extraTokens)
            : ToolResult.Error(0, 0, $"failed to save slot {normalizedSlotIndex + 1}", extraTokens);
    }

    private ToolResult LoadSlot(int slotIndex)
    {
        SaveManager saveManager = ResolveSaveManager();
        if (saveManager == null)
        {
            return ToolResult.Error(0, 0, "save manager not found", BuildEmptySaveSlotsExtraTokens());
        }

        int normalizedSlotIndex = Mathf.Clamp(slotIndex, 0, SaveManager.SlotCount - 1);
        bool hadSaveFile = saveManager.HasSaveFile(normalizedSlotIndex);
        bool loaded = saveManager.LoadSlot(normalizedSlotIndex);
        InvalidateSaveSlotStatusCache();
        string extraTokens = BuildSaveSlotsExtraTokens(saveManager, true);
        if (!loaded)
        {
            return ToolResult.Error(0, 0, $"failed to load slot {normalizedSlotIndex + 1}", extraTokens);
        }

        string message = hadSaveFile
            ? $"loaded slot {normalizedSlotIndex + 1}"
            : $"started new map for empty slot {normalizedSlotIndex + 1}";
        return ToolResult.Success(0, 0, 0, 0, 0, 0, message, extraTokens);
    }

    private ToolResult ResetMap(int slotIndex, bool randomizeSeed)
    {
        SaveManager saveManager = ResolveSaveManager();
        if (saveManager == null)
        {
            return ToolResult.Error(0, 0, "save manager not found", BuildEmptySaveSlotsExtraTokens());
        }

        int normalizedSlotIndex = slotIndex >= 0
            ? Mathf.Clamp(slotIndex, 0, SaveManager.SlotCount - 1)
            : saveManager.SelectedSlotIndex;
        saveManager.StartNewMap(normalizedSlotIndex, randomizeSeed);
        InvalidateSaveSlotStatusCache();
        string extraTokens = BuildExtraTokens(
            BuildSaveSlotsExtraTokens(saveManager, true),
            BuildSeedExtraTokens(TerrainGenerator.ResolveActive()));
        return ToolResult.Success(
            0,
            0,
            0,
            0,
            0,
            0,
            $"reset slot {normalizedSlotIndex + 1} randomSeed={(randomizeSeed ? 1 : 0)}",
            extraTokens);
    }

    private ToolResult SetTerrainSeed(int seedValue)
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return ToolResult.Error(0, 0, "terrain generator not found");
        }

        terrain.SetSeed(seedValue);
        return ToolResult.Success(
            0,
            0,
            0,
            0,
            0,
            0,
            $"seed {terrain.CurrentSeed}",
            BuildSeedExtraTokens(terrain));
    }

    private string BuildSaveSlotsExtraTokens(SaveManager saveManager, bool forceRefresh = false, bool allowStaleCache = false)
    {
        if (saveManager == null)
        {
            return BuildEmptySaveSlotsExtraTokens();
        }

        int selectedSlotIndex = saveManager.SelectedSlotIndex;
        float now = Time.unscaledTime;
        if (!forceRefresh
            && !string.IsNullOrEmpty(cachedSaveSlotsExtraTokens)
            && cachedSaveSlotsSelectedSlotIndex == selectedSlotIndex
            && (allowStaleCache || now - cachedSaveSlotsStatusTime < StatusSaveSlotRefreshInterval))
        {
            return cachedSaveSlotsExtraTokens;
        }

        if (!forceRefresh && allowStaleCache)
        {
            return $"saveSlots={new string('0', SaveManager.SlotCount)} selectedSlot={selectedSlotIndex + 1}";
        }

        StringBuilder builder = new StringBuilder(SaveManager.SlotCount);
        builder.Append(saveManager.GetSaveSlotMask(forceRefresh));

        cachedSaveSlotsSelectedSlotIndex = selectedSlotIndex;
        cachedSaveSlotsStatusTime = now;
        cachedSaveSlotsExtraTokens = $"saveSlots={builder} selectedSlot={selectedSlotIndex + 1}";
        return cachedSaveSlotsExtraTokens;
    }

    private void InvalidateSaveSlotStatusCache()
    {
        cachedSaveSlotsStatusTime = float.NegativeInfinity;
        cachedSaveSlotsSelectedSlotIndex = -1;
        cachedSaveSlotsExtraTokens = string.Empty;
    }

    private static string BuildEmptySaveSlotsExtraTokens()
    {
        return $"saveSlots={new string('0', SaveManager.SlotCount)} selectedSlot=1";
    }

    private string BuildStatusExtraTokens(
        SaveManager saveManager,
        PlayerCamera playerCamera,
        TerrainGenerator terrain,
        bool allowStaleCache = false)
    {
        return BuildExtraTokens(
            BuildSaveSlotsExtraTokens(saveManager, false, allowStaleCache),
            BuildCameraSizeExtraTokens(playerCamera),
            BuildSeedExtraTokens(terrain),
            BuildFreeCameraExtraTokens(GameManager.Instance),
            BuildFreeTrainExtraTokens(GameManager.Instance),
            BuildFreeElectroEnergyExtraTokens(GameManager.Instance),
            BuildFreeBucketExtraTokens(GameManager.Instance),
            BuildAnimalAIExtraTokens(GameManager.Instance),
            BuildMapObjectTickProfilingExtraTokens(GameManager.Instance),
            BuildPlayerSpeedExtraTokens(),
            BuildWorldTimeExtraTokens(ResolveWorldTime()));
    }

    private string BuildPlayerSpeedExtraTokens()
    {
        float playerSpeed = hasPlayerSpeedSample ? currentPlayerSpeed : -1f;
        return string.Format(
            CultureInfo.InvariantCulture,
            "playerSpeed={0:0.###}",
            playerSpeed);
    }

    private static string BuildCameraSizeExtraTokens(PlayerCamera playerCamera)
    {
        if (playerCamera == null)
        {
            return string.Empty;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "cameraMinSize={0:0.###} cameraMaxSize={1:0.###}",
            playerCamera.MinimumOrthographicSize,
            playerCamera.MaximumOrthographicSize);
    }

    private static string BuildSeedExtraTokens(TerrainGenerator terrain)
    {
        return terrain != null
            ? $"seed={terrain.CurrentSeed.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;
    }

    private static string BuildFreeCameraExtraTokens(GameManager gameManager)
    {
        return gameManager != null
            ? $"freeCamera={(gameManager.FreeCamera ? 1 : 0)}"
            : "freeCamera=0";
    }

    private static string BuildFreeTrainExtraTokens(GameManager gameManager)
    {
        return gameManager != null
            ? $"freeTrain={(gameManager.FreeTrain ? 1 : 0)}"
            : "freeTrain=0";
    }

    private static string BuildFreeElectroEnergyExtraTokens(GameManager gameManager)
    {
        return gameManager != null
            ? $"freeElectroEnergy={(gameManager.FreeElectroEnergy ? 1 : 0)}"
            : "freeElectroEnergy=0";
    }

    private static string BuildFreeBucketExtraTokens(GameManager gameManager)
    {
        return gameManager != null
            ? $"freeBucket={(gameManager.FreeBucket ? 1 : 0)}"
            : "freeBucket=0";
    }

    private static string BuildMapObjectTickProfilingExtraTokens(GameManager gameManager)
    {
        return gameManager != null
            ? $"mapObjectTickProfiling={(gameManager.MapObjectTickProfilingEnabled ? 1 : 0)}"
            : "mapObjectTickProfiling=0";
    }

    private static string BuildWorldTimeExtraTokens(WorldTimeService worldTime)
    {
        if (worldTime == null)
        {
            return "day=0 time=--:-- timeScale=0 timePaused=1 isDay=0";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "day={0} time={1:00}:{2:00} timeScale={3:0.###} timePaused={4} isDay={5} year={6} season={7} dayOfSeason={8} latitude={9:0.###} airTemperature={10:0.###} waterTemperature={11:0.###}",
            worldTime.DayIndex,
            worldTime.Hour,
            worldTime.Minute,
            worldTime.TimeScale,
            worldTime.Paused ? 1 : 0,
            worldTime.IsDay ? 1 : 0,
            worldTime.YearIndex,
            worldTime.SeasonIndex,
            worldTime.DayOfSeason,
            worldTime.LatitudeDegrees,
            MapClimate.CurrentTemperatureCelsius,
            MapClimate.CurrentWaterTemperatureCelsius);
    }

    private static string BuildExtraTokens(params string[] tokens)
    {
        if (tokens == null || tokens.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(token.Trim());
        }

        return builder.ToString();
    }

    private SaveManager ResolveSaveManager()
    {
        if (cachedSaveManager != null)
        {
            return cachedSaveManager;
        }

        cachedSaveManager = FindObjectOfType<SaveManager>();
        return cachedSaveManager;
    }

    private PlayerCamera ResolvePlayerCamera()
    {
        if (cachedPlayerCamera != null)
        {
            return cachedPlayerCamera;
        }

        cachedPlayerCamera = FindObjectOfType<PlayerCamera>();
        return cachedPlayerCamera;
    }

    private static WorldTimeService ResolveWorldTime()
    {
        return GameManager.Instance?.WorldTime ?? WorldTimeService.Active;
    }

    private void CaptureWorldStats(
        TerrainGenerator terrain,
        bool allowStaleCache,
        out int installedObjectTotal,
        out int conveyorItemTotal,
        out string installationTypeCounts)
    {
        float now = Time.unscaledTime;
        if (allowStaleCache
            || (!float.IsNegativeInfinity(cachedStatusWorldStatsTime)
                && now - cachedStatusWorldStatsTime < StatusWorldStatsRefreshInterval))
        {
            installedObjectTotal = cachedInstalledObjectTotal;
            conveyorItemTotal = terrain != null
                ? terrain.GetConveyorItemCount()
                : cachedConveyorItemTotal;
            cachedConveyorItemTotal = conveyorItemTotal;
            installationTypeCounts = cachedInstallationTypeCounts;
            return;
        }

        if (terrain == null)
        {
            installationCountsByItemId.Clear();
            cachedInstalledObjectTotal = 0;
            cachedConveyorItemTotal = 0;
            cachedInstallationTypeCounts = "-";
            cachedStatusWorldStatsTime = now;
            installedObjectTotal = cachedInstalledObjectTotal;
            conveyorItemTotal = cachedConveyorItemTotal;
            installationTypeCounts = cachedInstallationTypeCounts;
            return;
        }

        cachedInstalledObjectTotal = terrain.GetInstallationItemCounts(installationCountsByItemId);
        cachedConveyorItemTotal = terrain.GetConveyorItemCount();
        cachedInstallationTypeCounts = BuildInstallationTypeCountToken(installationCountsByItemId);
        cachedStatusWorldStatsTime = now;

        installedObjectTotal = cachedInstalledObjectTotal;
        conveyorItemTotal = cachedConveyorItemTotal;
        installationTypeCounts = cachedInstallationTypeCounts;
    }

    private static bool IsTerrainChunkStreamingBusy()
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        return terrain != null && terrain.IsChunkStreamingBusy;
    }

    private string BuildInstallationTypeCountToken(Dictionary<int, int> countsByItemId)
    {
        if (countsByItemId == null || countsByItemId.Count <= 0)
        {
            return "-";
        }

        installationCountSortBuffer.Clear();
        foreach (KeyValuePair<int, int> pair in countsByItemId)
        {
            if (pair.Key >= 0 && pair.Value > 0)
            {
                installationCountSortBuffer.Add(pair);
            }
        }

        if (installationCountSortBuffer.Count <= 0)
        {
            return "-";
        }

        installationCountSortBuffer.Sort((left, right) =>
        {
            int countComparison = right.Value.CompareTo(left.Value);
            return countComparison != 0 ? countComparison : left.Key.CompareTo(right.Key);
        });

        installationCountTokenBuilder.Clear();
        for (int i = 0; i < installationCountSortBuffer.Count; i++)
        {
            if (i > 0)
            {
                installationCountTokenBuilder.Append(',');
            }

            KeyValuePair<int, int> pair = installationCountSortBuffer[i];
            installationCountTokenBuilder.Append(pair.Key);
            installationCountTokenBuilder.Append(':');
            installationCountTokenBuilder.Append(pair.Value);
        }

        return installationCountTokenBuilder.ToString();
    }

    private ToolResult GiveItems(int itemId, int count)
    {
        GameManager gameManager = GameManager.Instance;
        Player player = gameManager != null ? gameManager.Player : FindObjectOfType<Player>();
        if (player == null)
        {
            return ToolResult.Error(itemId, count, "player not found");
        }

        ItemManager itemManager = gameManager != null ? gameManager.ItemManger : FindObjectOfType<ItemManager>();
        if (itemManager != null && !itemManager.TryGetItemSetById(itemId, out _))
        {
            return ToolResult.Error(itemId, count, $"item {itemId} not found");
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        Vector3 playerPosition = player.transform.position;
        int bagCount = 0;
        int handCount = 0;
        int droppedCount = 0;

        for (int i = 0; i < count; i++)
        {
            if (player.TryAddToBag(itemId, out _))
            {
                bagCount++;
                continue;
            }

            if (player.TryAddToHand(itemId, out _))
            {
                handCount++;
                continue;
            }

            if (terrain != null
                && (terrain.TryAddDroppedItemAnimated(playerPosition, itemId, playerPosition, out _)
                    || terrain.TryAddDroppedItemAtPlayerBlock(playerPosition, itemId, out _)
                    || terrain.TryAddDroppedItemNear(playerPosition, itemId, out _)))
            {
                droppedCount++;
                continue;
            }

            break;
        }

        int givenCount = bagCount + handCount + droppedCount;
        if (givenCount <= 0)
        {
            return ToolResult.Error(itemId, count, "bag, hand, and nearby ground are full");
        }

        return ToolResult.Success(itemId, count, givenCount, bagCount, handCount, droppedCount);
    }

    private ToolResult CreateConveyorLine(int itemId, int count)
    {
        GameManager gameManager = GameManager.Instance;
        Player player = gameManager != null ? gameManager.Player : FindObjectOfType<Player>();
        if (player == null)
        {
            return ToolResult.Error(itemId, count, "player not found");
        }

        ItemManager itemManager = gameManager != null ? gameManager.ItemManger : FindObjectOfType<ItemManager>();
        if (itemManager == null)
        {
            return ToolResult.Error(itemId, count, "item manager not found");
        }

        if (!TryResolveConveyorDefinition(itemManager, itemId, out ConveyorBelt conveyorPrototype, out int resolvedItemId))
        {
            string itemLabel = itemId < 0 ? "auto" : itemId.ToString(CultureInfo.InvariantCulture);
            return ToolResult.Error(itemId, count, $"item {itemLabel} is not a conveyor belt");
        }

        itemId = resolvedItemId;

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return ToolResult.Error(itemId, count, "terrain not found");
        }

        InstallationPlacementController placementController = FindObjectOfType<InstallationPlacementController>();
        if (placementController == null)
        {
            return ToolResult.Error(itemId, count, "installation placement controller not found");
        }

        MapObject footprintPrefab = conveyorPrototype.StraightVariantPrefab != null
            ? conveyorPrototype.StraightVariantPrefab
            : conveyorPrototype;
        Transform playerFacingTransform = player.BodyTransform != null ? player.BodyTransform : player.transform;
        Vector2Int preferredOutputDirection = ResolveCardinalGridDirection(playerFacingTransform.forward);
        Vector2Int playerCoordinate = new Vector2Int(
            Mathf.RoundToInt(player.transform.position.x),
            Mathf.RoundToInt(player.transform.position.z));
        List<Vector2Int> spiralCoordinates = BuildConveyorLineSpiralCoordinates(
            playerCoordinate,
            preferredOutputDirection,
            ConveyorLineFillSearchLimit);
        Dictionary<Vector2Int, int> spiralRanks = BuildConveyorLineSpiralRanks(spiralCoordinates);

        if (!TryFindConveyorFillStart(
                terrain,
                placementController,
                footprintPrefab,
                playerCoordinate,
                preferredOutputDirection,
                spiralCoordinates,
                out Vector2Int startCoordinate,
                out Vector2Int initialPreviousCoordinate))
        {
            return ToolResult.Error(itemId, count, "beltline has no placeable coordinate near player");
        }

        List<Vector2Int> path = BuildConveyorFillPath(
            terrain,
            placementController,
            footprintPrefab,
            playerCoordinate,
            preferredOutputDirection,
            startCoordinate,
            initialPreviousCoordinate,
            spiralRanks,
            count);
        if (path.Count <= 0)
        {
            return ToolResult.Error(itemId, count, $"beltline blocked at {startCoordinate.x},{startCoordinate.y}");
        }

        if (!TryBuildConveyorPlacementPlan(
                terrain,
                placementController,
                conveyorPrototype,
                footprintPrefab,
                playerCoordinate,
                preferredOutputDirection,
                path,
                initialPreviousCoordinate,
                spiralRanks,
                out List<ConveyorPlacementPlan> placementPlans,
                out string planError))
        {
            return ToolResult.Error(itemId, count, planError);
        }

        int placedCount = 0;

        for (int i = 0; i < placementPlans.Count; i++)
        {
            if (!TryPlaceConveyorPlan(terrain, placementController, placementPlans[i]))
            {
                break;
            }

            placedCount++;
        }

        if (placedCount <= 0)
        {
            return ToolResult.Error(
                itemId,
                count,
                $"beltline blocked at {startCoordinate.x},{startCoordinate.y}");
        }

        return ToolResult.Success(
            itemId,
            count,
            placedCount,
            0,
            0,
            0,
            $"beltline placed={placedCount} start={startCoordinate.x},{startCoordinate.y} mode=fill dir={preferredOutputDirection.x},{preferredOutputDirection.y}");
    }

    private static string BuildAnimalAIExtraTokens(GameManager gameManager)
    {
        AnimalAIWorld world = gameManager != null ? gameManager.AnimalAIWorld : null;
        return string.Format(
            CultureInfo.InvariantCulture,
            "showAnimalHerdAreas={0} animalAIPaused={1} animalTotal={2} animalAIActive={3} animalAIActiveRadius={4:0.###} animalHerdGroups={5} animalSeparationChecks={6} animalCollisionChecks={7} animalCollisionCellChecks={8} animalColliderRadiusMax={9:0.###} animalPhysicsQueries={10} animalPhysicsHits={11} animalAITicks={12} animalAIDue={13} animalAIDeferred={14} animalAIBudget={15} animalAINear={16} animalAIMid={17} animalAIFar={18}",
            gameManager != null && gameManager.ShowAnimalHerdAreas ? 1 : 0,
            world != null && world.Paused ? 1 : 0,
            world != null ? world.ControllerCount : 0,
            world != null ? world.CountActiveControllers() : 0,
            gameManager != null ? gameManager.AnimalAIActiveRadius : 0f,
            world != null ? world.HerdGroupCount : 0,
            world != null ? world.SeparationCandidateChecksLastFrame : 0,
            world != null ? world.AnimalCollisionCandidateChecksLastFrame : 0,
            world != null ? world.AnimalCollisionCellChecksLastFrame : 0,
            world != null ? world.MaximumAnimalColliderRadius : 0f,
            world != null ? world.ObstaclePhysicsQueriesLastFrame : 0,
            world != null ? world.ObstaclePhysicsHitsLastFrame : 0,
            world != null ? world.ActiveSimulationTicksLastFrame : 0,
            world != null ? world.SimulationTickCandidatesLastFrame : 0,
            world != null ? world.DeferredSimulationTicksLastFrame : 0,
            world != null ? world.SimulationTickBudgetLastFrame : 0,
            world != null ? world.NearActiveControllerCount : 0,
            world != null ? world.MidActiveControllerCount : 0,
            world != null ? world.FarActiveControllerCount : 0);
    }

    private ToolResult CreateConveyorStressTest(int count)
    {
        GameManager gameManager = GameManager.Instance;
        Player player = gameManager != null ? gameManager.Player : FindObjectOfType<Player>();
        if (player == null)
        {
            return ToolResult.Error(-1, count, "player not found");
        }

        ItemManager itemManager = gameManager != null ? gameManager.ItemManger : FindObjectOfType<ItemManager>();
        if (itemManager == null)
        {
            return ToolResult.Error(-1, count, "item manager not found");
        }

        if (!TryResolveConveyorDefinition(itemManager, -1, out ConveyorBelt conveyorPrototype, out int itemId))
        {
            return ToolResult.Error(-1, count, "automatic conveyor definition not found");
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return ToolResult.Error(itemId, count, "terrain not found");
        }

        InstallationPlacementController placementController = FindObjectOfType<InstallationPlacementController>();
        if (placementController == null)
        {
            return ToolResult.Error(itemId, count, "installation placement controller not found");
        }

        Transform playerFacingTransform = player.BodyTransform != null ? player.BodyTransform : player.transform;
        Vector2Int outputDirection = ResolveCardinalGridDirection(playerFacingTransform.forward);
        Vector2Int playerCoordinate = new Vector2Int(
            Mathf.RoundToInt(player.transform.position.x),
            Mathf.RoundToInt(player.transform.position.z));
        if (!TryResolveConveyorPlacementVariant(
                placementController,
                conveyorPrototype,
                NegateGridDirection(outputDirection),
                outputDirection,
                out MapObject sourcePrefab,
                out int quarterTurns))
        {
            return ToolResult.Error(itemId, count, "straight conveyor variant not found");
        }

        int searchLimit = ResolveConveyorStressSearchLimit(terrain, playerCoordinate);
        List<Vector2Int> coordinates = BuildConveyorLineSpiralCoordinates(
            playerCoordinate,
            outputDirection,
            searchLimit);
        ConveyorPlacementPlan plan = new ConveyorPlacementPlan
        {
            SourcePrefab = sourcePrefab,
            QuarterTurns = quarterTurns,
            InputDirection = NegateGridDirection(outputDirection),
            OutputDirection = outputDirection
        };

        int placedCount = 0;
        int blockedCount = 0;
        for (int i = 0; i < coordinates.Count && placedCount < count; i++)
        {
            plan.Coordinate = coordinates[i];
            if (TryPlaceConveyorPlan(terrain, placementController, plan))
            {
                placedCount++;
            }
            else
            {
                blockedCount++;
            }
        }

        if (placedCount != count)
        {
            return ToolResult.Error(
                itemId,
                count,
                $"beltstress incomplete placed={placedCount} requested={count} blocked={blockedCount} searched={coordinates.Count}");
        }

        return ToolResult.Success(
            itemId,
            count,
            placedCount,
            0,
            0,
            0,
            $"beltstress placed={placedCount} blocked={blockedCount} searched={coordinates.Count}");
    }

    private static ToolResult CreateAnimalStressTest(int count)
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return ToolResult.Error(-1, count, "terrain not found");
        }

        int created = terrain.CreateAnimalAIStressTest(count);
        if (created <= 0)
        {
            return ToolResult.Error(-1, count, "no animal could be spawned near player");
        }

        string message = created == count
            ? $"animalstress spawned={created}"
            : $"animalstress incomplete spawned={created} requested={count}";
        return ToolResult.Success(-1, count, created, 0, 0, 0, message);
    }

    private static ToolResult CreateAnimalCollisionStressTest(int count)
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return ToolResult.Error(-1, count, "terrain not found");
        }

        int created = terrain.CreateAnimalCollisionStressTest(count);
        if (created <= 0)
        {
            return ToolResult.Error(-1, count, "animal collision harness could not spawn animals");
        }

        return ToolResult.Success(
            -1,
            count,
            created,
            0,
            0,
            0,
            $"animalcollision spawned={created} obstacles=4");
    }

    private static ToolResult ForceAnimalThreat(int radius)
    {
        GameManager gameManager = GameManager.Instance;
        AnimalAIWorld world = gameManager != null ? gameManager.AnimalAIWorld : null;
        Player player = gameManager != null ? gameManager.Player : null;
        if (world == null || player == null)
        {
            return ToolResult.Error(-1, radius, "animal AI world or player not found");
        }

        int notified = world.ForceThreatPulse(player.transform.position, Mathf.Max(1f, radius));
        return ToolResult.Success(
            -1,
            radius,
            notified,
            0,
            0,
            0,
            $"animalthreat notified={notified} radius={radius}");
    }

    private static int ResolveConveyorStressSearchLimit(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate)
    {
        if (terrain == null
            || !terrain.TryGetLoadedBlockBounds(
                out Vector2Int minimumCoordinate,
                out Vector2Int maximumCoordinate))
        {
            return ConveyorLineFillSearchLimit;
        }

        long radius = Math.Max(
            Math.Max(
                Math.Abs((long)minimumCoordinate.x - centerCoordinate.x),
                Math.Abs((long)maximumCoordinate.x - centerCoordinate.x)),
            Math.Max(
                Math.Abs((long)minimumCoordinate.y - centerCoordinate.y),
                Math.Abs((long)maximumCoordinate.y - centerCoordinate.y)));
        long diameter = (radius * 2L) + 1L;
        long coordinateCount = (diameter * diameter) - 1L;
        return (int)Math.Min(
            int.MaxValue,
            Math.Max(ConveyorLineFillSearchLimit, coordinateCount));
    }

    private static bool TryPlaceConveyorPlan(
        TerrainGenerator terrain,
        InstallationPlacementController placementController,
        ConveyorPlacementPlan plan)
    {
        if (terrain == null
            || placementController == null
            || plan == null
            || !terrain.TryGetLoadedBlock(plan.Coordinate, out Block anchorBlock)
            || anchorBlock == null
            || !placementController.CanPlaceInstalledObjectAt(
                plan.Coordinate,
                plan.SourcePrefab,
                plan.QuarterTurns,
                null,
                true))
        {
            return false;
        }

        InstallationObject installedInstallation =
            terrain.CreateInstallationObject(plan.SourcePrefab, terrain.transform);
        if (installedInstallation == null)
        {
            return false;
        }

        MapObject installedObject = installedInstallation;
        installedObject.transform.SetPositionAndRotation(
            placementController.GetInstalledObjectWorldPosition(
                plan.Coordinate,
                plan.SourcePrefab,
                plan.QuarterTurns),
            placementController.GetInstalledObjectRotation(plan.SourcePrefab, plan.QuarterTurns));
        if (!placementController.BindInstalledObjectToFootprintBlocks(
                installedObject,
                plan.Coordinate,
                plan.QuarterTurns))
        {
            anchorBlock.SetMapObject(installedObject);
        }

        placementController.ConfigureInstalledObjectRuntime(
            installedObject,
            plan.Coordinate,
            plan.QuarterTurns);
        terrain.RegisterLiveInstallationObject(installedInstallation);
        return true;
    }

    private ToolResult FillRandomConveyorItems(int count)
    {
        GameManager gameManager = GameManager.Instance;
        Player player = gameManager != null ? gameManager.Player : FindObjectOfType<Player>();
        if (player == null)
        {
            return ToolResult.Error(-1, count, "player not found");
        }

        ItemManager itemManager = gameManager != null ? gameManager.ItemManger : FindObjectOfType<ItemManager>();
        if (itemManager == null)
        {
            return ToolResult.Error(-1, count, "item manager not found");
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return ToolResult.Error(-1, count, "terrain not found");
        }

        List<int> randomItemIds = BuildConveyorItemFillItemIds(itemManager);
        if (randomItemIds.Count <= 0)
        {
            return ToolResult.Error(-1, count, "no portable item definitions found");
        }

        List<Block> candidates = new List<Block>();
        CollectNearbyConveyorItemFillCandidates(terrain, player.transform.position, candidates);
        if (candidates.Count <= 0)
        {
            return ToolResult.Error(-1, count, "no empty conveyor near player");
        }

        int placedCount = 0;
        int attemptBudget = Mathf.Max(count * 8, candidates.Count * 2);
        Vector3 playerPosition = player.transform.position;
        for (int attempt = 0; attempt < attemptBudget && placedCount < count && candidates.Count > 0; attempt++)
        {
            int candidateIndex = UnityEngine.Random.Range(0, candidates.Count);
            Block candidateBlock = candidates[candidateIndex];
            if (candidateBlock == null || !candidateBlock.CanAddConveyorObjects(1))
            {
                candidates.RemoveAt(candidateIndex);
                continue;
            }

            int itemId = randomItemIds[UnityEngine.Random.Range(0, randomItemIds.Count)];
            Vector3 placementReference = BuildRandomConveyorItemPlacementReference(candidateBlock);
            if (candidateBlock.TryAddConveyorObjectAnimatedAtPlacement(
                    itemId,
                    placementReference,
                    playerPosition,
                    0f,
                    out _,
                    null,
                    null,
                    0f,
                    false,
                    0f))
            {
                placedCount++;
            }
            else
            {
                candidates.RemoveAt(candidateIndex);
                continue;
            }

            if (!candidateBlock.CanAddConveyorObjects(1))
            {
                candidates.RemoveAt(candidateIndex);
            }
        }

        if (placedCount <= 0)
        {
            return ToolResult.Error(-1, count, "no conveyor item slot accepted a random item");
        }

        return ToolResult.Success(
            -1,
            count,
            placedCount,
            0,
            0,
            0,
            $"beltitems placed={placedCount} candidates={candidates.Count}");
    }

    private static ToolResult CheckConveyors()
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return ToolResult.Error(0, 0, "terrain not found");
        }

        ConveyorDiagnosticReport report = ConveyorRuntimeDiagnostics.Run(terrain);
        string extraTokens = report.BuildProtocolTokens();
        if (!report.IsHealthy)
        {
            string firstIssue = string.IsNullOrEmpty(report.FirstIssue) ? "unknown" : report.FirstIssue;
            return ToolResult.Error(
                0,
                0,
                $"beltcheck failed first={firstIssue}",
                extraTokens);
        }

        string message = report.WarningCount > 0
            ? $"beltcheck healthy warnings={report.WarningCount} first={report.FirstIssue}"
            : "beltcheck healthy";
        return ToolResult.Success(0, 0, 0, 0, 0, 0, message, extraTokens);
    }

    private static void CollectNearbyConveyorItemFillCandidates(
        TerrainGenerator terrain,
        Vector3 playerPosition,
        List<Block> results)
    {
        results.Clear();
        if (terrain == null)
        {
            return;
        }

        terrain.CopyLoadedBlocks(results);
        float radiusSqr = ConveyorItemFillSearchRadius * ConveyorItemFillSearchRadius;
        for (int i = results.Count - 1; i >= 0; i--)
        {
            Block block = results[i];
            if (block == null || !block.CanAddConveyorObjects(1))
            {
                results.RemoveAt(i);
                continue;
            }

            Vector3 offset = block.WorldPosition - playerPosition;
            offset.y = 0f;
            if (offset.sqrMagnitude > radiusSqr)
            {
                results.RemoveAt(i);
            }
        }
    }

    private static List<int> BuildConveyorItemFillItemIds(ItemManager itemManager)
    {
        List<int> itemIds = new List<int>();
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        if (definitions == null)
        {
            return itemIds;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null
                && definition.id >= 0
                && !definition.storesFluid
                && !InputOutputModule.IsFluidItemId(definition.id)
                && !ItemDefinition.IsElectricityItemDefinition(definition)
                && definition.portableMesh != null
                && definition.portableMat != null)
            {
                itemIds.Add(definition.id);
            }
        }

        if (itemIds.Count > 0)
        {
            return itemIds;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null
                && definition.id >= 0
                && !definition.storesFluid
                && !InputOutputModule.IsFluidItemId(definition.id)
                && !ItemDefinition.IsElectricityItemDefinition(definition))
            {
                itemIds.Add(definition.id);
            }
        }

        return itemIds;
    }

    private static Vector3 BuildRandomConveyorItemPlacementReference(Block block)
    {
        Vector3 position = block != null ? block.WorldPosition : Vector3.zero;
        return position + new Vector3(
            UnityEngine.Random.Range(-0.35f, 0.35f),
            0f,
            UnityEngine.Random.Range(-0.35f, 0.35f));
    }

    private static bool TryFindConveyorFillStart(
        TerrainGenerator terrain,
        InstallationPlacementController placementController,
        MapObject footprintPrefab,
        Vector2Int playerCoordinate,
        Vector2Int preferredOutputDirection,
        IReadOnlyList<Vector2Int> spiralCoordinates,
        out Vector2Int startCoordinate,
        out Vector2Int initialPreviousCoordinate)
    {
        startCoordinate = playerCoordinate + preferredOutputDirection;
        initialPreviousCoordinate = playerCoordinate;
        if (terrain == null
            || placementController == null
            || footprintPrefab == null
            || spiralCoordinates == null)
        {
            return false;
        }

        bool found = false;
        int bestScore = int.MaxValue;
        for (int i = 0; i < spiralCoordinates.Count; i++)
        {
            Vector2Int candidateCoordinate = spiralCoordinates[i];
            if (!IsConveyorCandidateCoordinate(
                    terrain,
                    placementController,
                    footprintPrefab,
                    playerCoordinate,
                    candidateCoordinate,
                    null))
            {
                continue;
            }

            ResolveConveyorStartPreviousCoordinate(
                terrain,
                candidateCoordinate,
                playerCoordinate,
                preferredOutputDirection,
                out Vector2Int candidatePreviousCoordinate,
                out int connectionPriority);
            int candidateScore = (connectionPriority * ConveyorLineFillSearchLimit) + i;
            if (found && candidateScore >= bestScore)
            {
                continue;
            }

            found = true;
            bestScore = candidateScore;
            startCoordinate = candidateCoordinate;
            initialPreviousCoordinate = candidatePreviousCoordinate;
        }

        return found;
    }

    private static List<Vector2Int> BuildConveyorFillPath(
        TerrainGenerator terrain,
        InstallationPlacementController placementController,
        MapObject footprintPrefab,
        Vector2Int playerCoordinate,
        Vector2Int preferredOutputDirection,
        Vector2Int startCoordinate,
        Vector2Int initialPreviousCoordinate,
        Dictionary<Vector2Int, int> spiralRanks,
        int count)
    {
        List<Vector2Int> path = new List<Vector2Int>(Mathf.Max(0, count));
        if (count <= 0
            || !IsConveyorCandidateCoordinate(
                terrain,
                placementController,
                footprintPrefab,
                playerCoordinate,
                startCoordinate,
                null))
        {
            return path;
        }

        HashSet<Vector2Int> plannedCoordinates = new HashSet<Vector2Int>();
        path.Add(startCoordinate);
        plannedCoordinates.Add(startCoordinate);

        Vector2Int previousCoordinate = initialPreviousCoordinate;
        Vector2Int currentCoordinate = startCoordinate;
        while (path.Count < count
               && TrySelectNextConveyorFillCoordinate(
                   terrain,
                   placementController,
                   footprintPrefab,
                   playerCoordinate,
                   preferredOutputDirection,
                   currentCoordinate,
                   previousCoordinate,
                   plannedCoordinates,
                   spiralRanks,
                   count - path.Count,
                   out Vector2Int nextCoordinate))
        {
            path.Add(nextCoordinate);
            plannedCoordinates.Add(nextCoordinate);
            previousCoordinate = currentCoordinate;
            currentCoordinate = nextCoordinate;
        }

        return path;
    }

    private static bool TrySelectNextConveyorFillCoordinate(
        TerrainGenerator terrain,
        InstallationPlacementController placementController,
        MapObject footprintPrefab,
        Vector2Int playerCoordinate,
        Vector2Int preferredOutputDirection,
        Vector2Int currentCoordinate,
        Vector2Int previousCoordinate,
        HashSet<Vector2Int> plannedCoordinates,
        Dictionary<Vector2Int, int> spiralRanks,
        int remainingCount,
        out Vector2Int nextCoordinate)
    {
        nextCoordinate = currentCoordinate;
        Vector2Int currentTravelDirection = NormalizeGridDirection(currentCoordinate - previousCoordinate);
        bool found = false;
        int bestScore = int.MaxValue;

        for (int i = 0; i < ConveyorLineCardinalDirections.Length; i++)
        {
            Vector2Int candidateDirection = ConveyorLineCardinalDirections[i];
            Vector2Int candidateCoordinate = currentCoordinate + candidateDirection;
            if (!IsConveyorCandidateCoordinate(
                    terrain,
                    placementController,
                    footprintPrefab,
                    playerCoordinate,
                    candidateCoordinate,
                    plannedCoordinates))
            {
                continue;
            }

            int futureNeighborCount = CountFutureConveyorFillNeighbors(
                terrain,
                placementController,
                footprintPrefab,
                playerCoordinate,
                candidateCoordinate,
                currentCoordinate,
                plannedCoordinates);
            int deadEndPenalty = remainingCount > 1 && futureNeighborCount <= 0 ? ConveyorLineFillSearchLimit * 8 : 0;
            int reversePenalty = candidateDirection == NegateGridDirection(currentTravelDirection) ? 256 : 0;
            int preferredDirectionPenalty = candidateDirection == preferredOutputDirection ? 4 : 0;
            int futureNeighborPenalty = Mathf.Max(0, 4 - futureNeighborCount);
            int candidateScore =
                deadEndPenalty
                + (GetConveyorLineSpiralRank(spiralRanks, candidateCoordinate, playerCoordinate) * 16)
                + reversePenalty
                + preferredDirectionPenalty
                + futureNeighborPenalty;

            if (found && candidateScore >= bestScore)
            {
                continue;
            }

            found = true;
            bestScore = candidateScore;
            nextCoordinate = candidateCoordinate;
        }

        return found;
    }

    private static int CountFutureConveyorFillNeighbors(
        TerrainGenerator terrain,
        InstallationPlacementController placementController,
        MapObject footprintPrefab,
        Vector2Int playerCoordinate,
        Vector2Int candidateCoordinate,
        Vector2Int currentCoordinate,
        HashSet<Vector2Int> plannedCoordinates)
    {
        int count = 0;
        for (int i = 0; i < ConveyorLineCardinalDirections.Length; i++)
        {
            Vector2Int neighborCoordinate = candidateCoordinate + ConveyorLineCardinalDirections[i];
            if (neighborCoordinate == currentCoordinate)
            {
                continue;
            }

            if (IsConveyorCandidateCoordinate(
                    terrain,
                    placementController,
                    footprintPrefab,
                    playerCoordinate,
                    neighborCoordinate,
                    plannedCoordinates))
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryBuildConveyorPlacementPlan(
        TerrainGenerator terrain,
        InstallationPlacementController placementController,
        ConveyorBelt conveyorPrototype,
        MapObject footprintPrefab,
        Vector2Int playerCoordinate,
        Vector2Int preferredOutputDirection,
        IReadOnlyList<Vector2Int> path,
        Vector2Int initialPreviousCoordinate,
        Dictionary<Vector2Int, int> spiralRanks,
        out List<ConveyorPlacementPlan> placementPlans,
        out string error)
    {
        placementPlans = new List<ConveyorPlacementPlan>();
        error = null;
        if (path == null || path.Count <= 0)
        {
            error = "beltline path is empty";
            return false;
        }

        HashSet<Vector2Int> plannedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < path.Count; i++)
        {
            plannedCoordinates.Add(path[i]);
        }

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int coordinate = path[i];
            Vector2Int previousCoordinate = i > 0 ? path[i - 1] : initialPreviousCoordinate;
            Vector2Int nextCoordinate = i + 1 < path.Count
                ? path[i + 1]
                : coordinate + ResolveConveyorTailOutputDirection(
                    terrain,
                    placementController,
                    footprintPrefab,
                    playerCoordinate,
                    preferredOutputDirection,
                    coordinate,
                    previousCoordinate,
                    plannedCoordinates,
                    spiralRanks);
            Vector2Int inputDirection = NormalizeGridDirection(previousCoordinate - coordinate);
            Vector2Int outputDirection = NormalizeGridDirection(nextCoordinate - coordinate);
            if (outputDirection == Vector2Int.zero)
            {
                outputDirection = inputDirection != Vector2Int.zero
                    ? NegateGridDirection(inputDirection)
                    : preferredOutputDirection;
            }

            if (inputDirection == Vector2Int.zero)
            {
                inputDirection = NegateGridDirection(outputDirection);
            }

            if (!TryResolveConveyorPlacementVariant(
                    placementController,
                    conveyorPrototype,
                    inputDirection,
                    outputDirection,
                    out MapObject sourcePrefab,
                    out int quarterTurns))
            {
                error = $"beltline could not resolve conveyor turn at {coordinate.x},{coordinate.y}";
                return false;
            }

            if (!placementController.CanPlaceInstalledObjectAt(
                    coordinate,
                    sourcePrefab,
                    quarterTurns,
                    null,
                    true))
            {
                error = $"beltline blocked at {coordinate.x},{coordinate.y}";
                return false;
            }

            placementPlans.Add(new ConveyorPlacementPlan
            {
                Coordinate = coordinate,
                SourcePrefab = sourcePrefab,
                QuarterTurns = quarterTurns,
                InputDirection = inputDirection,
                OutputDirection = outputDirection
            });
        }

        return placementPlans.Count > 0;
    }

    private static bool TryResolveConveyorDefinition(
        ItemManager itemManager,
        int itemId,
        out ConveyorBelt conveyorBelt,
        out int resolvedItemId)
    {
        conveyorBelt = null;
        resolvedItemId = itemId;
        if (itemManager == null || itemManager.ItemDefinitions == null)
        {
            return false;
        }

        List<ItemDefinition> definitions = itemManager.ItemDefinitions;
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition candidate = definitions[i];
            if (candidate == null || (itemId >= 0 && candidate.id != itemId))
            {
                continue;
            }

            if (candidate.mapObject is ConveyorBelt directConveyor)
            {
                conveyorBelt = directConveyor;
                resolvedItemId = candidate.id;
                return true;
            }

            conveyorBelt = candidate.mapObject != null
                ? candidate.mapObject.GetComponent<ConveyorBelt>()
                : null;
            if (conveyorBelt != null)
            {
                resolvedItemId = candidate.id;
                return true;
            }

            if (itemId >= 0)
            {
                resolvedItemId = candidate.id;
                return false;
            }
        }

        return false;
    }

    private static Vector2Int ResolveCardinalGridDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return Vector2Int.up;
        }

        direction.Normalize();
        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.z))
        {
            return direction.x >= 0f ? Vector2Int.right : Vector2Int.left;
        }

        return direction.z >= 0f ? Vector2Int.up : Vector2Int.down;
    }

    private static List<Vector2Int> BuildConveyorLineSpiralCoordinates(
        Vector2Int centerCoordinate,
        Vector2Int preferredOutputDirection,
        int limit)
    {
        List<Vector2Int> coordinates = new List<Vector2Int>(Mathf.Max(0, limit));
        if (limit <= 0)
        {
            return coordinates;
        }

        HashSet<Vector2Int> uniqueCoordinates = new HashSet<Vector2Int>();
        Vector2Int forwardDirection = preferredOutputDirection == Vector2Int.zero
            ? Vector2Int.up
            : preferredOutputDirection;
        Vector2Int rightDirection = RotateGridDirectionClockwise(forwardDirection);
        Vector2Int[] spiralDirections =
        {
            rightDirection,
            NegateGridDirection(forwardDirection),
            NegateGridDirection(rightDirection),
            forwardDirection
        };

        Vector2Int coordinate = centerCoordinate + forwardDirection;
        AddUniqueConveyorSpiralCoordinate(
            coordinates,
            uniqueCoordinates,
            centerCoordinate,
            coordinate,
            limit);

        int directionIndex = 0;
        int segmentLength = 1;
        int segmentsAtCurrentLength = 0;
        bool consumedFirstSingleSegment = false;
        while (coordinates.Count < limit)
        {
            Vector2Int stepDirection = spiralDirections[directionIndex];
            for (int step = 0; step < segmentLength && coordinates.Count < limit; step++)
            {
                coordinate += stepDirection;
                AddUniqueConveyorSpiralCoordinate(
                    coordinates,
                    uniqueCoordinates,
                    centerCoordinate,
                    coordinate,
                    limit);
            }

            directionIndex = (directionIndex + 1) % spiralDirections.Length;
            segmentsAtCurrentLength++;
            if (!consumedFirstSingleSegment)
            {
                consumedFirstSingleSegment = true;
                segmentsAtCurrentLength = 0;
                segmentLength++;
            }
            else if (segmentsAtCurrentLength >= 2)
            {
                segmentsAtCurrentLength = 0;
                segmentLength++;
            }
        }

        return coordinates;
    }

    private static void AddUniqueConveyorSpiralCoordinate(
        List<Vector2Int> coordinates,
        HashSet<Vector2Int> uniqueCoordinates,
        Vector2Int centerCoordinate,
        Vector2Int coordinate,
        int limit)
    {
        if (coordinates == null
            || uniqueCoordinates == null
            || coordinates.Count >= limit
            || coordinate == centerCoordinate
            || !uniqueCoordinates.Add(coordinate))
        {
            return;
        }

        coordinates.Add(coordinate);
    }

    private static Dictionary<Vector2Int, int> BuildConveyorLineSpiralRanks(IReadOnlyList<Vector2Int> spiralCoordinates)
    {
        Dictionary<Vector2Int, int> ranks = new Dictionary<Vector2Int, int>();
        if (spiralCoordinates == null)
        {
            return ranks;
        }

        for (int i = 0; i < spiralCoordinates.Count; i++)
        {
            if (!ranks.ContainsKey(spiralCoordinates[i]))
            {
                ranks.Add(spiralCoordinates[i], i);
            }
        }

        return ranks;
    }

    private static void ResolveConveyorStartPreviousCoordinate(
        TerrainGenerator terrain,
        Vector2Int candidateCoordinate,
        Vector2Int playerCoordinate,
        Vector2Int preferredOutputDirection,
        out Vector2Int previousCoordinate,
        out int connectionPriority)
    {
        previousCoordinate = candidateCoordinate - preferredOutputDirection;
        connectionPriority = 4;

        Vector2Int playerDelta = playerCoordinate - candidateCoordinate;
        if (IsCardinalUnit(playerDelta))
        {
            previousCoordinate = playerCoordinate;
            connectionPriority = candidateCoordinate == playerCoordinate + preferredOutputDirection ? 1 : 3;
        }

        for (int i = 0; i < ConveyorLineCardinalDirections.Length; i++)
        {
            Vector2Int neighborCoordinate = candidateCoordinate + ConveyorLineCardinalDirections[i];
            if (!TryGetConveyorDirectionsAtCoordinate(
                    terrain,
                    neighborCoordinate,
                    out Vector2Int neighborInputDirection,
                    out Vector2Int neighborOutputDirection))
            {
                continue;
            }

            Vector2Int neighborToCandidateDirection = NormalizeGridDirection(candidateCoordinate - neighborCoordinate);
            int neighborPriority = neighborOutputDirection == neighborToCandidateDirection
                ? 0
                : neighborInputDirection == neighborToCandidateDirection ? 2 : 3;
            if (neighborPriority >= connectionPriority)
            {
                continue;
            }

            previousCoordinate = neighborCoordinate;
            connectionPriority = neighborPriority;
        }
    }

    private static Vector2Int ResolveConveyorTailOutputDirection(
        TerrainGenerator terrain,
        InstallationPlacementController placementController,
        MapObject footprintPrefab,
        Vector2Int playerCoordinate,
        Vector2Int preferredOutputDirection,
        Vector2Int currentCoordinate,
        Vector2Int previousCoordinate,
        HashSet<Vector2Int> plannedCoordinates,
        Dictionary<Vector2Int, int> spiralRanks)
    {
        Vector2Int travelDirection = NormalizeGridDirection(currentCoordinate - previousCoordinate);
        Vector2Int fallbackDirection = travelDirection == Vector2Int.zero
            ? preferredOutputDirection
            : travelDirection;
        Vector2Int backDirection = NegateGridDirection(travelDirection);
        bool found = false;
        int bestScore = int.MaxValue;
        Vector2Int resolvedDirection = fallbackDirection;

        for (int i = 0; i < ConveyorLineCardinalDirections.Length; i++)
        {
            Vector2Int candidateDirection = ConveyorLineCardinalDirections[i];
            if (candidateDirection == backDirection)
            {
                continue;
            }

            Vector2Int candidateCoordinate = currentCoordinate + candidateDirection;
            if (!IsConveyorCandidateCoordinate(
                    terrain,
                    placementController,
                    footprintPrefab,
                    playerCoordinate,
                    candidateCoordinate,
                    plannedCoordinates))
            {
                continue;
            }

            int candidateScore =
                (GetConveyorLineSpiralRank(spiralRanks, candidateCoordinate, playerCoordinate) * 16)
                + (candidateDirection == preferredOutputDirection ? 4 : 0);
            if (found && candidateScore >= bestScore)
            {
                continue;
            }

            found = true;
            bestScore = candidateScore;
            resolvedDirection = candidateDirection;
        }

        return resolvedDirection == Vector2Int.zero ? Vector2Int.up : resolvedDirection;
    }

    private static bool TryResolveConveyorPlacementVariant(
        InstallationPlacementController placementController,
        ConveyorBelt conveyorPrototype,
        Vector2Int inputDirection,
        Vector2Int outputDirection,
        out MapObject sourcePrefab,
        out int quarterTurns)
    {
        sourcePrefab = null;
        quarterTurns = 0;
        if (placementController == null || conveyorPrototype == null)
        {
            return false;
        }

        inputDirection = inputDirection == Vector2Int.zero ? NegateGridDirection(outputDirection) : inputDirection;
        outputDirection = outputDirection == Vector2Int.zero ? NegateGridDirection(inputDirection) : outputDirection;

        List<MapObject> candidateSources = new List<MapObject>(4);
        bool prefersCorner = ConveyorBelt.IsPerpendicular(inputDirection, outputDirection);
        if (prefersCorner)
        {
            AddUniqueConveyorSource(candidateSources, conveyorPrototype.CornerVariantPrefab);
            AddUniqueConveyorSource(candidateSources, conveyorPrototype.ReverseCornerVariantPrefab);
            AddUniqueConveyorSource(candidateSources, conveyorPrototype.StraightVariantPrefab);
        }
        else
        {
            AddUniqueConveyorSource(candidateSources, conveyorPrototype.StraightVariantPrefab);
            AddUniqueConveyorSource(candidateSources, conveyorPrototype.CornerVariantPrefab);
            AddUniqueConveyorSource(candidateSources, conveyorPrototype.ReverseCornerVariantPrefab);
        }

        AddUniqueConveyorSource(candidateSources, conveyorPrototype);

        return TryResolveConveyorPlacementVariantFromSources(
                   placementController,
                   candidateSources,
                   inputDirection,
                   outputDirection,
                   true,
                   true,
                   out sourcePrefab,
                   out quarterTurns)
               || TryResolveConveyorPlacementVariantFromSources(
                   placementController,
                   candidateSources,
                   inputDirection,
                   outputDirection,
                   false,
                   true,
                   out sourcePrefab,
                   out quarterTurns)
               || TryResolveConveyorPlacementVariantFromSources(
                   placementController,
                   candidateSources,
                   inputDirection,
                   outputDirection,
                   true,
                   false,
                   out sourcePrefab,
                   out quarterTurns);
    }

    private static bool TryResolveConveyorPlacementVariantFromSources(
        InstallationPlacementController placementController,
        IReadOnlyList<MapObject> candidateSources,
        Vector2Int inputDirection,
        Vector2Int outputDirection,
        bool requireInputMatch,
        bool requireOutputMatch,
        out MapObject sourcePrefab,
        out int quarterTurns)
    {
        sourcePrefab = null;
        quarterTurns = 0;
        if (placementController == null || candidateSources == null)
        {
            return false;
        }

        for (int i = 0; i < candidateSources.Count; i++)
        {
            MapObject candidateSource = candidateSources[i];
            if (!(candidateSource is ConveyorBelt candidateConveyor))
            {
                continue;
            }

            for (int candidateQuarterTurns = 0; candidateQuarterTurns < 4; candidateQuarterTurns++)
            {
                Quaternion candidateRotation = placementController.GetInstalledObjectRotation(
                    candidateSource,
                    candidateQuarterTurns);
                if (!candidateConveyor.TryGetInputDirection(candidateRotation, out Vector2Int candidateInputDirection)
                    || !candidateConveyor.TryGetOutputDirection(candidateRotation, out Vector2Int candidateOutputDirection))
                {
                    continue;
                }

                if (requireInputMatch && candidateInputDirection != inputDirection)
                {
                    continue;
                }

                if (requireOutputMatch && candidateOutputDirection != outputDirection)
                {
                    continue;
                }

                sourcePrefab = candidateSource;
                quarterTurns = candidateQuarterTurns;
                return true;
            }
        }

        return false;
    }

    private static void AddUniqueConveyorSource(List<MapObject> candidateSources, MapObject candidateSource)
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

    private static bool IsConveyorCandidateCoordinate(
        TerrainGenerator terrain,
        InstallationPlacementController placementController,
        MapObject footprintPrefab,
        Vector2Int playerCoordinate,
        Vector2Int coordinate,
        HashSet<Vector2Int> plannedCoordinates)
    {
        if (coordinate == playerCoordinate
            || (plannedCoordinates != null && plannedCoordinates.Contains(coordinate)))
        {
            return false;
        }

        return CanPlaceConveyorAtCoordinate(terrain, placementController, footprintPrefab, coordinate);
    }

    private static bool CanPlaceConveyorAtCoordinate(
        TerrainGenerator terrain,
        InstallationPlacementController placementController,
        MapObject footprintPrefab,
        Vector2Int coordinate)
    {
        if (terrain == null
            || placementController == null
            || footprintPrefab == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || terrain.IsWaterBiomeAt(coordinate))
        {
            return false;
        }

        for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
        {
            if (placementController.CanPlaceInstalledObjectAt(
                    coordinate,
                    footprintPrefab,
                    quarterTurns,
                    null,
                    true))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetConveyorDirectionsAtCoordinate(
        TerrainGenerator terrain,
        Vector2Int coordinate,
        out Vector2Int inputDirection,
        out Vector2Int outputDirection)
    {
        inputDirection = Vector2Int.zero;
        outputDirection = Vector2Int.zero;
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is ConveyorBelt conveyor)
            || conveyor == null
            || !conveyor.gameObject.activeInHierarchy)
        {
            return false;
        }

        return conveyor.TryGetInputDirection(conveyor.transform.rotation, out inputDirection)
               && conveyor.TryGetOutputDirection(conveyor.transform.rotation, out outputDirection);
    }

    private static int GetConveyorLineSpiralRank(
        Dictionary<Vector2Int, int> spiralRanks,
        Vector2Int coordinate,
        Vector2Int playerCoordinate)
    {
        if (spiralRanks != null && spiralRanks.TryGetValue(coordinate, out int rank))
        {
            return rank;
        }

        return ConveyorLineFillSearchLimit
               + Mathf.Abs(coordinate.x - playerCoordinate.x)
               + Mathf.Abs(coordinate.y - playerCoordinate.y);
    }

    private static Vector2Int NormalizeGridDirection(Vector2Int direction)
    {
        if (direction == Vector2Int.zero)
        {
            return Vector2Int.zero;
        }

        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
        {
            return direction.x >= 0 ? Vector2Int.right : Vector2Int.left;
        }

        return direction.y >= 0 ? Vector2Int.up : Vector2Int.down;
    }

    private static Vector2Int RotateGridDirectionClockwise(Vector2Int direction)
    {
        return new Vector2Int(direction.y, -direction.x);
    }

    private static Vector2Int NegateGridDirection(Vector2Int direction)
    {
        return new Vector2Int(-direction.x, -direction.y);
    }

    private static bool IsCardinalUnit(Vector2Int direction)
    {
        return (Mathf.Abs(direction.x) == 1 && direction.y == 0)
               || (Mathf.Abs(direction.y) == 1 && direction.x == 0);
    }

    private ToolResult SetDebugToggle(string toggleName, bool value)
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            return ToolResult.Error(0, 0, "game manager not found");
        }

        if (string.Equals(toggleName, "showConveyorSlotDots", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "conveyorSlotDots", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetShowConveyorSlotDots(value);
            return ToolResult.Success(0, 0, 0, 0, 0, 0, $"showConveyorSlotDots={(value ? 1 : 0)}");
        }

        if (string.Equals(toggleName, "showSleepAwake", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "sleepAwake", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetShowSleepAwake(value);
            return ToolResult.Success(0, 0, 0, 0, 0, 0, $"showSleepAwake={(value ? 1 : 0)}");
        }

        if (string.Equals(toggleName, "showBeltItemLine", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "beltItemLine", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetShowBeltItemLine(value);
            return ToolResult.Success(0, 0, 0, 0, 0, 0, $"showBeltItemLine={(value ? 1 : 0)}");
        }

        if (string.Equals(toggleName, "hideBeltItems", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "hideBeltItem", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "hideConveyorItems", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "hideConveyorItem", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetHideBeltItems(value);
            return ToolResult.Success(0, 0, 0, 0, 0, 0, $"hideBeltItems={(value ? 1 : 0)}");
        }

        if (string.Equals(toggleName, "hideBelts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "hideBelt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "hideConveyorBelts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "hideConveyorBelt", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetHideBelts(value);
            return ToolResult.Success(0, 0, 0, 0, 0, 0, $"hideBelts={(value ? 1 : 0)}");
        }

        if (string.Equals(toggleName, "showRailLine", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "railLine", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "showRailloadLine", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "railloadLine", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetShowRailLine(value);
            return ToolResult.Success(0, 0, 0, 0, 0, 0, $"showRailLine={(value ? 1 : 0)}");
        }

        if (string.Equals(toggleName, "showDirections", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "directions", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "showBeltDirections", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "beltDirections", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "showConveyorDirections", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "conveyorDirections", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetShowDirections(value);
            return ToolResult.Success(0, 0, 0, 0, 0, 0, $"showDirections={(value ? 1 : 0)}");
        }

        if (string.Equals(toggleName, "freeCamera", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "freeCam", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "freeCamear", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetFreeCamera(value);
            return ToolResult.Success(
                0,
                0,
                0,
                0,
                0,
                0,
                $"freeCamera={(value ? 1 : 0)}",
                BuildFreeCameraExtraTokens(gameManager));
        }

        if (string.Equals(toggleName, "freeTrain", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "trainFree", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetFreeTrain(value);
            return ToolResult.Success(
                0,
                0,
                0,
                0,
                0,
                0,
                $"freeTrain={(value ? 1 : 0)}",
                BuildFreeTrainExtraTokens(gameManager));
        }

        if (string.Equals(toggleName, "freeElectroEnergy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "freeElectricEnergy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "freeElectricity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "freeEnergy", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetFreeElectroEnergy(value);
            return ToolResult.Success(
                0,
                0,
                0,
                0,
                0,
                0,
                $"freeElectroEnergy={(value ? 1 : 0)}",
                BuildFreeElectroEnergyExtraTokens(gameManager));
        }

        if (string.Equals(toggleName, "freeBucket", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "infiniteBucket", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetFreeBucket(value);
            return ToolResult.Success(
                0,
                0,
                0,
                0,
                0,
                0,
                $"freeBucket={(value ? 1 : 0)}",
                BuildFreeBucketExtraTokens(gameManager));
        }

        if (string.Equals(toggleName, "mapObjectTickProfiling", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "tickProfiling", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "profiling", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetMapObjectTickProfilingEnabled(value);
            return ToolResult.Success(0, 0, 0, 0, 0, 0, $"mapObjectTickProfiling={(value ? 1 : 0)}");
        }

        if (string.Equals(toggleName, "showAnimalHerdAreas", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "animalHerdAreas", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetShowAnimalHerdAreas(value);
            return ToolResult.Success(
                0,
                0,
                0,
                0,
                0,
                0,
                $"showAnimalHerdAreas={(value ? 1 : 0)}");
        }

        if (string.Equals(toggleName, "animalAIPaused", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toggleName, "pauseAnimalAI", StringComparison.OrdinalIgnoreCase))
        {
            gameManager.SetAnimalAIPaused(value);
            return ToolResult.Success(
                0,
                0,
                0,
                0,
                0,
                0,
                $"animalAIPaused={(value ? 1 : 0)}");
        }

        return ToolResult.Error(0, 0, $"unknown debug toggle {toggleName}");
    }

    private ToolResult SetCameraSizeRange(float minimumSize, float maximumSize)
    {
        PlayerCamera playerCamera = ResolvePlayerCamera();
        if (playerCamera == null)
        {
            return ToolResult.Error(0, 0, "player camera not found");
        }

        playerCamera.SetOrthographicSizeRange(minimumSize, maximumSize);
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "camera size {0:0.###}-{1:0.###}",
            playerCamera.MinimumOrthographicSize,
            playerCamera.MaximumOrthographicSize);
        return ToolResult.Success(
            0,
            0,
            0,
            0,
            0,
            0,
            message,
            BuildCameraSizeExtraTokens(playerCamera));
    }

    private sealed class ConveyorPlacementPlan
    {
        public Vector2Int Coordinate;
        public MapObject SourcePrefab;
        public int QuarterTurns;
        public Vector2Int InputDirection;
        public Vector2Int OutputDirection;
    }

    private enum RuntimeProfilerRecorderUnit
    {
        Count,
        NanosecondsToMilliseconds,
        BytesToMegabytes,
        BytesToKilobytes
    }

    private readonly struct RuntimeProfilerRecorderCandidate
    {
        public RuntimeProfilerRecorderCandidate(ProfilerCategory category, string counterName)
        {
            Category = category;
            CounterName = counterName;
        }

        public ProfilerCategory Category { get; }
        public string CounterName { get; }

        public static RuntimeProfilerRecorderCandidate Internal(string counterName)
        {
            return new RuntimeProfilerRecorderCandidate(ProfilerCategory.Internal, counterName);
        }

        public static RuntimeProfilerRecorderCandidate Render(string counterName)
        {
            return new RuntimeProfilerRecorderCandidate(ProfilerCategory.Render, counterName);
        }

        public static RuntimeProfilerRecorderCandidate Memory(string counterName)
        {
            return new RuntimeProfilerRecorderCandidate(ProfilerCategory.Memory, counterName);
        }

        public static RuntimeProfilerRecorderCandidate CustomCategory(string categoryName, string counterName)
        {
            return new RuntimeProfilerRecorderCandidate(new ProfilerCategory(categoryName), counterName);
        }
    }

    private sealed class RuntimeProfilerRecorderSpec
    {
        public RuntimeProfilerRecorderSpec(
            string group,
            string name,
            RuntimeProfilerRecorderUnit unit,
            params RuntimeProfilerRecorderCandidate[] candidates)
            : this(group, name, unit, ProfilerRecorderOptions.Default, candidates)
        {
        }

        public RuntimeProfilerRecorderSpec(
            string group,
            string name,
            RuntimeProfilerRecorderUnit unit,
            ProfilerRecorderOptions extraOptions,
            params RuntimeProfilerRecorderCandidate[] candidates)
        {
            Group = group;
            Name = name;
            Unit = unit;
            ExtraOptions = extraOptions;
            Candidates = candidates ?? Array.Empty<RuntimeProfilerRecorderCandidate>();
        }

        public string Group { get; }
        public string Name { get; }
        public RuntimeProfilerRecorderUnit Unit { get; }
        public ProfilerRecorderOptions ExtraOptions { get; }
        public RuntimeProfilerRecorderCandidate[] Candidates { get; }
    }

    private sealed class RuntimeProfilerRecorder : IDisposable
    {
        public RuntimeProfilerRecorder(
            RuntimeProfilerRecorderSpec spec,
            RuntimeProfilerRecorderCandidate candidate,
            ProfilerRecorder recorder)
        {
            Group = spec.Group;
            Name = spec.Name;
            Unit = spec.Unit;
            CategoryName = candidate.Category.Name;
            CounterName = candidate.CounterName;
            Recorder = recorder;
        }

        public string Group { get; }
        public string Name { get; }
        public RuntimeProfilerRecorderUnit Unit { get; }
        public string CategoryName { get; }
        public string CounterName { get; }
        public ProfilerRecorder Recorder { get; private set; }

        public void Dispose()
        {
            Recorder.Dispose();
            Recorder = default;
        }
    }

    private enum ToolCommand
    {
        Give,
        Ping,
        Status,
        TimeStatus,
        TimeSet,
        TimeScale,
        TimePause,
        TimeNextSunrise,
        TimeCheck,
        SetDebugToggle,
        SetCameraSizeRange,
        SetSeed,
        CreateConveyorLine,
        CreateConveyorStressTest,
        CreateAnimalStressTest,
        CreateAnimalCollisionStressTest,
        ForceAnimalThreat,
        FillConveyorItems,
        CheckConveyors,
        SaveSlot,
        LoadSlot,
        ResetMap,
        ListSaveSlots,
        PerfSnapshot
    }

    private readonly struct WorldTimeToolParameters
    {
        private WorldTimeToolParameters(int hour, int minute, float scale, bool paused)
        {
            Hour = hour;
            Minute = minute;
            Scale = scale;
            Paused = paused;
        }

        public int Hour { get; }
        public int Minute { get; }
        public float Scale { get; }
        public bool Paused { get; }

        public static WorldTimeToolParameters ForTime(int hour, int minute)
        {
            return new WorldTimeToolParameters(hour, minute, 1f, false);
        }

        public static WorldTimeToolParameters ForScale(float scale)
        {
            return new WorldTimeToolParameters(0, 0, scale, false);
        }

        public static WorldTimeToolParameters ForPause(bool paused)
        {
            return new WorldTimeToolParameters(0, 0, 1f, paused);
        }
    }

    private sealed class ToolRequest
    {
        private int processingCompleted;
        private int waiterReleased;
        private int completionDisposed;

        public ToolRequest(
            ToolCommand command,
            int itemId,
            int count,
            int slotIndex,
            string debugToggleName,
            bool debugToggleValue,
            float cameraMinSize,
            float cameraMaxSize,
            int seedValue,
            bool randomizeSeed,
            WorldTimeToolParameters timeParameters)
        {
            Command = command;
            ItemId = itemId;
            Count = count;
            SlotIndex = slotIndex;
            DebugToggleName = debugToggleName;
            DebugToggleValue = debugToggleValue;
            CameraMinSize = cameraMinSize;
            CameraMaxSize = cameraMaxSize;
            SeedValue = seedValue;
            RandomizeSeed = randomizeSeed;
            TimeParameters = timeParameters;
        }

        public ToolCommand Command { get; }
        public int ItemId { get; }
        public int Count { get; }
        public int SlotIndex { get; }
        public string DebugToggleName { get; }
        public bool DebugToggleValue { get; }
        public float CameraMinSize { get; }
        public float CameraMaxSize { get; }
        public int SeedValue { get; }
        public bool RandomizeSeed { get; }
        public WorldTimeToolParameters TimeParameters { get; }
        private ManualResetEventSlim Completion { get; } = new ManualResetEventSlim(false);
        public ToolResult Result { get; set; }

        public bool WaitForCompletion(int timeoutMilliseconds)
        {
            return Completion.Wait(timeoutMilliseconds);
        }

        public void Complete()
        {
            Completion.Set();
            Interlocked.Exchange(ref processingCompleted, 1);
            TryDisposeCompletion();
        }

        public void ReleaseWaiter()
        {
            Interlocked.Exchange(ref waiterReleased, 1);
            TryDisposeCompletion();
        }

        private void TryDisposeCompletion()
        {
            if (Volatile.Read(ref processingCompleted) != 0
                && Volatile.Read(ref waiterReleased) != 0
                && Interlocked.CompareExchange(ref completionDisposed, 1, 0) == 0)
            {
                Completion.Dispose();
            }
        }
    }

    private readonly struct ToolResult
    {
        private ToolResult(
            bool success,
            int itemId,
            int requested,
            int given,
            int bag,
            int hand,
            int dropped,
            float fps,
            float frameMs,
            int installedObjectTotal,
            int conveyorItemTotal,
            string installationTypeCounts,
            bool showConveyorSlotDots,
            bool showSleepAwake,
            bool showBeltItemLine,
            bool hideBeltItems,
            bool hideBelts,
            bool showRailLine,
            bool showBeltDirections,
            string message,
            string extraTokens)
        {
            IsSuccess = success;
            ItemId = itemId;
            Requested = requested;
            Given = given;
            Bag = bag;
            Hand = hand;
            Dropped = dropped;
            Fps = fps;
            FrameMs = frameMs;
            InstalledObjectTotal = installedObjectTotal;
            ConveyorItemTotal = conveyorItemTotal;
            InstallationTypeCounts = string.IsNullOrWhiteSpace(installationTypeCounts) ? "-" : installationTypeCounts;
            ShowConveyorSlotDots = showConveyorSlotDots;
            ShowSleepAwake = showSleepAwake;
            ShowBeltItemLine = showBeltItemLine;
            HideBeltItems = hideBeltItems;
            HideBelts = hideBelts;
            ShowRailLine = showRailLine;
            ShowBeltDirections = showBeltDirections;
            Message = message;
            ExtraTokens = string.IsNullOrWhiteSpace(extraTokens) ? string.Empty : extraTokens.Trim();
        }

        private bool IsSuccess { get; }
        private int ItemId { get; }
        private int Requested { get; }
        private int Given { get; }
        private int Bag { get; }
        private int Hand { get; }
        private int Dropped { get; }
        private float Fps { get; }
        private float FrameMs { get; }
        private int InstalledObjectTotal { get; }
        private int ConveyorItemTotal { get; }
        private string InstallationTypeCounts { get; }
        private bool ShowConveyorSlotDots { get; }
        private bool ShowSleepAwake { get; }
        private bool ShowBeltItemLine { get; }
        private bool HideBeltItems { get; }
        private bool HideBelts { get; }
        private bool ShowRailLine { get; }
        private bool ShowBeltDirections { get; }
        private string Message { get; }
        private string ExtraTokens { get; }

        public static ToolResult Success(int itemId, int requested, int given, int bag, int hand, int dropped, string message = "ok", string extraTokens = "")
        {
            return new ToolResult(true, itemId, requested, given, bag, hand, dropped, -1f, -1f, 0, 0, "-", false, false, false, false, false, false, false, message, extraTokens);
        }

        public static ToolResult Error(int itemId, int requested, string message, string extraTokens = "")
        {
            return new ToolResult(false, itemId, requested, 0, 0, 0, 0, -1f, -1f, 0, 0, "-", false, false, false, false, false, false, false, message, extraTokens);
        }

        public static ToolResult Ping()
        {
            return new ToolResult(true, 0, 0, 0, 0, 0, 0, -1f, -1f, 0, 0, "-", false, false, false, false, false, false, false, "pong", string.Empty);
        }

        public static ToolResult Status(
            float fps,
            float frameMs,
            int installedObjectTotal,
            int conveyorItemTotal,
            string installationTypeCounts,
            bool showConveyorSlotDots,
            bool showSleepAwake,
            bool showBeltItemLine,
            bool hideBeltItems,
            bool hideBelts,
            bool showRailLine,
            bool showBeltDirections,
            string extraTokens = "")
        {
            return new ToolResult(
                true,
                0,
                0,
                0,
                0,
                0,
                0,
                fps,
                frameMs,
                installedObjectTotal,
                conveyorItemTotal,
                installationTypeCounts,
                showConveyorSlotDots,
                showSleepAwake,
                showBeltItemLine,
                hideBeltItems,
                hideBelts,
                showRailLine,
                showBeltDirections,
                "status",
                extraTokens);
        }

        public string ToProtocolLine()
        {
            string prefix = IsSuccess ? "ok" : "error";
            string extra = string.IsNullOrWhiteSpace(ExtraTokens) ? string.Empty : " " + ExtraTokens;
            if (Fps >= 0f)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} itemId={1} requested={2} given={3} bag={4} hand={5} dropped={6} fps={7:0.0} frameMs={8:0.0} installTotal={9} beltItems={10} installTypes={11} showConveyorSlotDots={12} showSleepAwake={13} showBeltItemLine={14} hideBeltItems={15} hideBelts={16} showRailLine={17} showBeltDirections={18}{19} message=\"{20}\"",
                    prefix,
                    ItemId,
                    Requested,
                    Given,
                    Bag,
                    Hand,
                    Dropped,
                    Fps,
                    FrameMs,
                    InstalledObjectTotal,
                    ConveyorItemTotal,
                    InstallationTypeCounts,
                    ShowConveyorSlotDots ? 1 : 0,
                    ShowSleepAwake ? 1 : 0,
                    ShowBeltItemLine ? 1 : 0,
                    HideBeltItems ? 1 : 0,
                    HideBelts ? 1 : 0,
                    ShowRailLine ? 1 : 0,
                    ShowBeltDirections ? 1 : 0,
                    extra,
                    Message);
            }

            return $"{prefix} itemId={ItemId} requested={Requested} given={Given} bag={Bag} hand={Hand} dropped={Dropped}{extra} message=\"{Message}\"";
        }
    }
}
