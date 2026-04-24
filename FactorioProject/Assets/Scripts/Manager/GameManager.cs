using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

    private readonly Queue<GiveRequest> pendingRequests = new Queue<GiveRequest>();
    private readonly object pendingRequestLock = new object();
    private TcpListener listener;
    private Thread listenerThread;
    private int port = DefaultPort;
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
        while (TryDequeueRequest(out GiveRequest request))
        {
            request.Result = GiveItems(request.ItemId, request.Count);
            request.Completion.Set();
        }
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

            if (!TryParseRequest(line, out int itemId, out int count, out string error))
            {
                writer.WriteLine($"error {error}");
                return;
            }

            GiveRequest request = new GiveRequest(itemId, count);
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

    private static bool TryParseRequest(string line, out int itemId, out int count, out string error)
    {
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
            itemId = 0;
            count = 0;
            return true;
        }

        if (parts.Length < 2 || !string.Equals(parts[0], "give", StringComparison.OrdinalIgnoreCase))
        {
            error = "usage: give <itemId> [count]";
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

    private void EnqueueRequest(GiveRequest request)
    {
        lock (pendingRequestLock)
        {
            pendingRequests.Enqueue(request);
        }
    }

    private bool TryDequeueRequest(out GiveRequest request)
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

    private sealed class GiveRequest
    {
        public GiveRequest(int itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public int ItemId { get; }
        public int Count { get; }
        public ManualResetEventSlim Completion { get; } = new ManualResetEventSlim(false);
        public GiveResult Result { get; set; }
    }

    private readonly struct GiveResult
    {
        private GiveResult(bool success, int itemId, int requested, int given, int bag, int hand, int dropped, string message)
        {
            IsSuccess = success;
            ItemId = itemId;
            Requested = requested;
            Given = given;
            Bag = bag;
            Hand = hand;
            Dropped = dropped;
            Message = message;
        }

        private bool IsSuccess { get; }
        private int ItemId { get; }
        private int Requested { get; }
        private int Given { get; }
        private int Bag { get; }
        private int Hand { get; }
        private int Dropped { get; }
        private string Message { get; }

        public static GiveResult Success(int itemId, int requested, int given, int bag, int hand, int dropped, string message = "ok")
        {
            return new GiveResult(true, itemId, requested, given, bag, hand, dropped, message);
        }

        public static GiveResult Error(int itemId, int requested, string message)
        {
            return new GiveResult(false, itemId, requested, 0, 0, 0, 0, message);
        }

        public string ToProtocolLine()
        {
            string prefix = IsSuccess ? "ok" : "error";
            return $"{prefix} itemId={ItemId} requested={Requested} given={Given} bag={Bag} hand={Hand} dropped={Dropped} message=\"{Message}\"";
        }
    }
}
