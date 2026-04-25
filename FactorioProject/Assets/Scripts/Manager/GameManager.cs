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

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private UIManager uiManager;
    private ItemManager itemManager;

    [SerializeField]
    private Player player;
    [SerializeField]
    private bool debugConveyorInstallGridEnds;
    [SerializeField]
    private bool showConveyorSlotDots = true;
    [SerializeField]
    private bool runtimeItemGiveServerEnabled = true;
    [SerializeField, Min(1)]
    private int runtimeItemGiveServerPort = RuntimeItemGiveReceiver.DefaultPort;

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

    public Player Player => player;
    public bool DebugConveyorInstallGridEnds => debugConveyorInstallGridEnds;
    public bool ShowConveyorSlotDots => showConveyorSlotDots;
    public bool RuntimeItemGiveServerEnabled => runtimeItemGiveServerEnabled;
    public int RuntimeItemGiveServerPort => runtimeItemGiveServerPort;

    public void SetInstallationPlacementActive(bool isActive)
    {
        InstallationPlacementActive = isActive;
    }

    public void SetMapEditActive(bool isActive)
    {
        MapEditActive = isActive;
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
    private const int RequestTimeoutMilliseconds = 5000;

    private readonly Queue<ToolRequest> pendingRequests = new Queue<ToolRequest>();
    private readonly object pendingRequestLock = new object();
    private TcpListener listener;
    private Thread listenerThread;
    private int port = DefaultPort;
    private float fpsSampleElapsed;
    private int fpsSampleFrames;
    private float currentFps;
    private float currentFrameMs;
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

        while (TryDequeueRequest(out ToolRequest request))
        {
            request.Result = request.Command == ToolCommand.Status
                ? GetStatusResult()
                : GiveItems(request.ItemId, request.Count);
            request.Completion.Set();
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

            if (!TryParseRequest(line, out ToolCommand command, out int itemId, out int count, out string error))
            {
                writer.WriteLine($"error {error}");
                return;
            }

            ToolRequest request = new ToolRequest(command, itemId, count);
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

    private static bool TryParseRequest(string line, out ToolCommand command, out int itemId, out int count, out string error)
    {
        command = ToolCommand.Give;
        itemId = -1;
        count = 1;
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

        if (parts.Length < 2 || !string.Equals(parts[0], "give", StringComparison.OrdinalIgnoreCase))
        {
            error = "usage: give <itemId> [count] | ping | status";
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

    private GiveResult GetStatusResult()
    {
        float fps = currentFps;
        float frameMs = currentFrameMs;
        if (fps <= 0f && Time.unscaledDeltaTime > 0f)
        {
            fps = 1f / Time.unscaledDeltaTime;
            frameMs = Time.unscaledDeltaTime * 1000f;
        }

        return GiveResult.Status(fps, frameMs);
    }

    private GiveResult GiveItems(int itemId, int count)
    {
        if (count == 0)
        {
            return GiveResult.Success(itemId, 0, 0, 0, 0, 0, "pong");
        }

        GameManager gameManager = GameManager.Instance;
        Player player = gameManager != null ? gameManager.Player : FindObjectOfType<Player>();
        if (player == null)
        {
            return GiveResult.Error(itemId, count, "player not found");
        }

        ItemManager itemManager = gameManager != null ? gameManager.ItemManger : FindObjectOfType<ItemManager>();
        if (itemManager != null && !itemManager.TryGetItemSetById(itemId, out _))
        {
            return GiveResult.Error(itemId, count, $"item {itemId} not found");
        }

        TerrainGenerator terrain = FindObjectOfType<TerrainGenerator>();
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
            return GiveResult.Error(itemId, count, "bag, hand, and nearby ground are full");
        }

        return GiveResult.Success(itemId, count, givenCount, bagCount, handCount, droppedCount);
    }

    private enum ToolCommand
    {
        Give,
        Ping,
        Status
    }

    private sealed class ToolRequest
    {
        public ToolRequest(ToolCommand command, int itemId, int count)
        {
            Command = command;
            ItemId = itemId;
            Count = count;
        }

        public ToolCommand Command { get; }
        public int ItemId { get; }
        public int Count { get; }
        public ManualResetEventSlim Completion { get; } = new ManualResetEventSlim(false);
        public GiveResult Result { get; set; }
    }

    private readonly struct GiveResult
    {
        private GiveResult(bool success, int itemId, int requested, int given, int bag, int hand, int dropped, float fps, float frameMs, string message)
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
            Message = message;
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
        private string Message { get; }

        public static GiveResult Success(int itemId, int requested, int given, int bag, int hand, int dropped, string message = "ok")
        {
            return new GiveResult(true, itemId, requested, given, bag, hand, dropped, -1f, -1f, message);
        }

        public static GiveResult Error(int itemId, int requested, string message)
        {
            return new GiveResult(false, itemId, requested, 0, 0, 0, 0, -1f, -1f, message);
        }

        public static GiveResult Status(float fps, float frameMs)
        {
            return new GiveResult(true, 0, 0, 0, 0, 0, 0, fps, frameMs, "status");
        }

        public string ToProtocolLine()
        {
            string prefix = IsSuccess ? "ok" : "error";
            if (Fps >= 0f)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} itemId={1} requested={2} given={3} bag={4} hand={5} dropped={6} fps={7:0.0} frameMs={8:0.0} message=\"{9}\"",
                    prefix,
                    ItemId,
                    Requested,
                    Given,
                    Bag,
                    Hand,
                    Dropped,
                    Fps,
                    FrameMs,
                    Message);
            }

            return $"{prefix} itemId={ItemId} requested={Requested} given={Given} bag={Bag} hand={Hand} dropped={Dropped} message=\"{Message}\"";
        }
    }
}
