using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private UIManager uiManager;
    private ItemManager itemManager;
    private VirtualObjectWorld virtualObjectWorld;
    private VirtualItemStackRenderer virtualItemStackRenderer;

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
    [FormerlySerializedAs("showBeltDirections")]
    [SerializeField]
    private bool showDirections;
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
    private bool beltDirectionRuntimeStateInitialized;
    private bool lastRuntimeShowBeltDirections;

    public bool InstallationPlacementActive { get; private set; }
    public bool MapEditActive { get; private set; }
    public bool PlayerInteractionLocked => InstallationPlacementActive || MapEditActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        ConfigureShadowQuality();
        ApplySceneShadowSettings();
        SceneManager.sceneLoaded += OnSceneLoaded;
        
        uiManager = GetComponentInChildren<UIManager>();
        itemManager = GetComponentInChildren<ItemManager>();
        virtualObjectWorld = VirtualObjectWorld.EnsureFor(gameObject);
        virtualItemStackRenderer = GetComponent<VirtualItemStackRenderer>();
        if (virtualItemStackRenderer == null)
        {
            virtualItemStackRenderer = gameObject.AddComponent<VirtualItemStackRenderer>();
        }

        virtualItemStackRenderer.Configure(virtualObjectWorld, itemManager);
        ConfigureRuntimeItemGiveReceiver();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha0))
            Time.timeScale = 0.5f;
        else if (Input.GetKeyDown(KeyCode.Alpha1))
            Time.timeScale = 1f;
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            Time.timeScale = 2f;

        SyncConveyorSlotDotRuntimeVisibility();
        SyncSleepAwakeRuntimeVisibility();
        SyncBeltItemLineRuntimeVisibility();
        SyncBeltDirectionRuntimeVisibility();
    }

    private void OnValidate()
    {
        if (Application.isPlaying && Instance == this)
        {
            SyncConveyorSlotDotRuntimeVisibility(true);
            SyncSleepAwakeRuntimeVisibility(true);
            SyncBeltItemLineRuntimeVisibility(true);
            SyncBeltDirectionRuntimeVisibility(true);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplySceneShadowSettings();
    }

    private void ConfigureShadowQuality()
    {
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;
        QualitySettings.shadowCascades = 4;

        if (QualitySettings.shadowDistance < 80f)
        {
            QualitySettings.shadowDistance = 80f;
        }
    }

    private void ApplySceneShadowSettings()
    {
        Light[] lights = FindObjectsOfType<Light>(true);

        foreach (Light lightComponent in lights)
        {
            lightComponent.shadows = LightShadows.Soft;
            lightComponent.shadowStrength = 1f;
            lightComponent.shadowBias = 0.05f;
            lightComponent.shadowNormalBias = 0.4f;
            lightComponent.shadowNearPlane = 0.2f;
            lightComponent.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
        }
    }

    public UIManager UIManager => uiManager;
    public ItemManager ItemManger => itemManager;
    public VirtualObjectWorld VirtualWorld => virtualObjectWorld;
    public VirtualItemStackRenderer VirtualItemRenderer => virtualItemStackRenderer;

    public Player Player => player;
    public bool DebugConveyorInstallGridEnds => debugConveyorInstallGridEnds;
    public bool ShowConveyorSlotDots => showConveyorSlotDots;
    public bool ShowSleepAwake => showSleepAwake;
    public bool ShowBeltItemLine => showBeltItemLine;
    public bool ShowDirections => showDirections;
    public bool ShowBeltDirections => ShowDirections;
    public bool RuntimeItemGiveServerEnabled => runtimeItemGiveServerEnabled;
    public int RuntimeItemGiveServerPort => runtimeItemGiveServerPort;

    public void SetInstallationPlacementActive(bool isActive)
    {
        if (InstallationPlacementActive == isActive)
        {
            return;
        }

        InstallationPlacementActive = isActive;
        if (!InstallationPlacementActive && !MapEditActive)
        {
            WorkableObject.SetInstallOrEditWorkableSelectionRangeVisualsRequested(false);
        }

        WorkableObject.RefreshAllRangeVisuals();
    }

    public void SetMapEditActive(bool isActive)
    {
        if (MapEditActive == isActive)
        {
            return;
        }

        MapEditActive = isActive;
        if (!InstallationPlacementActive && !MapEditActive)
        {
            WorkableObject.SetInstallOrEditWorkableSelectionRangeVisualsRequested(false);
        }

        WorkableObject.RefreshAllRangeVisuals();
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

    public void SetShowBeltDirections(bool show)
    {
        SetShowDirections(show);
    }

    public void SetShowDirections(bool show)
    {
        showDirections = show;
        SyncBeltDirectionRuntimeVisibility(true);
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
    private const int MaxConveyorsPerRequest = 100;
    private const int ConveyorLineFillSearchLimit = 4096;
    private const int RequestTimeoutMilliseconds = 5000;
    private const int MaxRequestsPerFrame = 4;
    private const int MaxRequestsPerFrameDuringChunkStreaming = 1;
    private const float StatusWorldStatsRefreshInterval = 1f;
    private const float StatusSaveSlotRefreshInterval = 5f;

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
    private TcpListener listener;
    private Thread listenerThread;
    private int port = DefaultPort;
    private float fpsSampleElapsed;
    private int fpsSampleFrames;
    private float currentFps;
    private float currentFrameMs;
    private float cachedStatusWorldStatsTime = float.NegativeInfinity;
    private int cachedInstalledObjectTotal;
    private int cachedConveyorItemTotal;
    private string cachedInstallationTypeCounts = "-";
    private float cachedSaveSlotsStatusTime = float.NegativeInfinity;
    private int cachedSaveSlotsSelectedSlotIndex = -1;
    private string cachedSaveSlotsExtraTokens = string.Empty;
    private SaveManager cachedSaveManager;
    private PlayerCamera cachedPlayerCamera;
    private volatile bool stopRequested;

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

        int processedCount = 0;
        int maxRequestsThisFrame = IsTerrainChunkStreamingBusy()
            ? MaxRequestsPerFrameDuringChunkStreaming
            : MaxRequestsPerFrame;
        while (processedCount < maxRequestsThisFrame && TryDequeueRequest(out ToolRequest request))
        {
            ProcessRequest(request);
            request.Completion.Set();
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
            default:
                request.Result = GiveItems(request.ItemId, request.Count);
                break;
        }
    }

    private void UpdateFrameStats()
    {
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
                randomizeSeed);
            EnqueueRequest(request);
            if (!request.Completion.Wait(RequestTimeoutMilliseconds))
            {
                writer.WriteLine("error timed out waiting for the Unity main thread");
                request.Completion.Dispose();
                return;
            }

            writer.WriteLine(request.Result.ToProtocolLine());
            request.Completion.Dispose();
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

        if (parts.Length < 2 || !string.Equals(parts[0], "give", StringComparison.OrdinalIgnoreCase))
        {
            error = "usage: give <itemId> [count] | beltline [auto|itemId] [count] | save <slot> | load <slot> | reset [slot] [randomSeed] | seed <int> | saveslots | debug <showConveyorSlotDots|showSleepAwake|showBeltItemLine|showDirections> <true|false> | camera size <minSize> <maxSize> | ping | status";
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
            currentShowBeltDirections,
            extraTokens);
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
            BuildSeedExtraTokens(terrain));
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
            conveyorItemTotal = cachedConveyorItemTotal;
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
        cachedConveyorItemTotal = terrain.GetLoadedConveyorItemCount();
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
            ConveyorPlacementPlan plan = placementPlans[i];
            if (plan == null
                || !terrain.TryGetLoadedBlock(plan.Coordinate, out Block anchorBlock)
                || anchorBlock == null
                || !placementController.CanPlaceInstalledObjectAt(
                    plan.Coordinate,
                    plan.SourcePrefab,
                    plan.QuarterTurns,
                    null,
                    true))
            {
                break;
            }

            InstallationObject installedInstallation = terrain.CreateInstallationObject(plan.SourcePrefab, terrain.transform);
            if (installedInstallation == null)
            {
                break;
            }

            MapObject installedObject = installedInstallation;
            installedObject.transform.SetPositionAndRotation(
                placementController.GetInstalledObjectWorldPosition(plan.Coordinate, plan.SourcePrefab, plan.QuarterTurns),
                placementController.GetInstalledObjectRotation(plan.SourcePrefab, plan.QuarterTurns));
            if (!placementController.BindInstalledObjectToFootprintBlocks(installedObject, plan.Coordinate, plan.QuarterTurns))
            {
                anchorBlock.SetMapObject(installedObject);
            }

            placementController.ConfigureInstalledObjectRuntime(installedObject, plan.Coordinate, plan.QuarterTurns);
            terrain.RegisterLiveInstallationObject(installedInstallation);

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
        AddUniqueConveyorSpiralCoordinate(coordinates, centerCoordinate, coordinate, limit);

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
                AddUniqueConveyorSpiralCoordinate(coordinates, centerCoordinate, coordinate, limit);
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
        Vector2Int centerCoordinate,
        Vector2Int coordinate,
        int limit)
    {
        if (coordinates == null || coordinates.Count >= limit || coordinate == centerCoordinate)
        {
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            if (coordinates[i] == coordinate)
            {
                return;
            }
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

    private enum ToolCommand
    {
        Give,
        Ping,
        Status,
        SetDebugToggle,
        SetCameraSizeRange,
        SetSeed,
        CreateConveyorLine,
        SaveSlot,
        LoadSlot,
        ResetMap,
        ListSaveSlots
    }

    private sealed class ToolRequest
    {
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
            bool randomizeSeed)
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
        public ManualResetEventSlim Completion { get; } = new ManualResetEventSlim(false);
        public ToolResult Result { get; set; }
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
        private bool ShowBeltDirections { get; }
        private string Message { get; }
        private string ExtraTokens { get; }

        public static ToolResult Success(int itemId, int requested, int given, int bag, int hand, int dropped, string message = "ok", string extraTokens = "")
        {
            return new ToolResult(true, itemId, requested, given, bag, hand, dropped, -1f, -1f, 0, 0, "-", false, false, false, false, message, extraTokens);
        }

        public static ToolResult Error(int itemId, int requested, string message, string extraTokens = "")
        {
            return new ToolResult(false, itemId, requested, 0, 0, 0, 0, -1f, -1f, 0, 0, "-", false, false, false, false, message, extraTokens);
        }

        public static ToolResult Ping()
        {
            return new ToolResult(true, 0, 0, 0, 0, 0, 0, -1f, -1f, 0, 0, "-", false, false, false, false, "pong", string.Empty);
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
                    "{0} itemId={1} requested={2} given={3} bag={4} hand={5} dropped={6} fps={7:0.0} frameMs={8:0.0} installTotal={9} beltItems={10} installTypes={11} showConveyorSlotDots={12} showSleepAwake={13} showBeltItemLine={14} showBeltDirections={15}{16} message=\"{17}\"",
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
                    ShowBeltDirections ? 1 : 0,
                    extra,
                    Message);
            }

            return $"{prefix} itemId={ItemId} requested={Requested} given={Given} bag={Bag} hand={Hand} dropped={Dropped}{extra} message=\"{Message}\"";
        }
    }
}
