using System.Collections;
using ProjectF.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class GameSceneLoadingScreen : MonoBehaviour
{
    private const string LoadingSceneName = "LoadingScene";
    private const string GameSceneName = "GameScene";
    private const float SceneProgressWeight = 0.15f;

    private static GameSceneLoadingScreen active;

    private readonly LatestSceneLoadRequest sceneRequests = new LatestSceneLoadRequest();
    private RectTransform fillRect;
    private float displayedProgress;
    private Coroutine requestRoutine;
    private bool waitForCurrentWorldReady;
    private bool sceneOperationActive;

    public static bool IsSceneOperationActive => active != null && active.sceneOperationActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        active = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnterGameFromLoadingScene()
    {
        if (!Application.isPlaying
            || SceneManager.GetActiveScene().name != LoadingSceneName
            || active != null)
        {
            return;
        }

        if (!TryLoadSceneAsync(GameSceneName))
        {
            Debug.LogError($"[LoadingScreen] {GameSceneName} 씬을 비동기로 불러올 수 없습니다.");
        }
    }

    public static bool TryLoadSceneAsync(int buildIndex)
    {
        if (buildIndex < 0
            || !Application.CanStreamedLevelBeLoaded(buildIndex)
            || !TryGetOrCreate(out GameSceneLoadingScreen screen, out bool reused))
        {
            return false;
        }

        if (reused)
        {
            SaveManager.DiscardPendingRuntimeLoadForSceneReplacement();
        }

        screen.EnqueueSceneLoad(buildIndex, null);
        return true;
    }

    public static bool TryLoadSceneAsync(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)
            || !Application.CanStreamedLevelBeLoaded(sceneName)
            || !TryGetOrCreate(out GameSceneLoadingScreen screen, out bool reused))
        {
            return false;
        }

        if (reused)
        {
            SaveManager.DiscardPendingRuntimeLoadForSceneReplacement();
        }

        screen.EnqueueSceneLoad(-1, sceneName);
        return true;
    }

    public static bool TryShowUntilWorldReady()
    {
        if (!TryGetOrCreate(out GameSceneLoadingScreen screen, out bool reused)
            || reused)
        {
            return false;
        }

        screen.BeginWorldReadyWait();
        return true;
    }

    private static bool TryGetOrCreate(
        out GameSceneLoadingScreen screen,
        out bool reused)
    {
        if (active != null)
        {
            screen = active;
            reused = true;
            return true;
        }

        GameObject root = new GameObject(
            nameof(GameSceneLoadingScreen),
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        DontDestroyOnLoad(root);
        screen = root.AddComponent<GameSceneLoadingScreen>();
        active = screen;
        reused = false;
        return true;
    }

    private void Awake()
    {
        if (active != null && active != this)
        {
            Destroy(gameObject);
            return;
        }

        active = this;
        BuildView();
        SetProgress(0f);
    }

    private void OnDestroy()
    {
        requestRoutine = null;
        sceneOperationActive = false;
        sceneRequests.Clear();
        if (active == this)
        {
            active = null;
        }
    }

    private void EnqueueSceneLoad(int buildIndex, string sceneName)
    {
        if (buildIndex >= 0)
        {
            sceneRequests.Enqueue(buildIndex);
        }
        else
        {
            sceneRequests.Enqueue(sceneName);
        }

        waitForCurrentWorldReady = false;
        EnsureRequestRoutine();
    }

    private void BeginWorldReadyWait()
    {
        waitForCurrentWorldReady = true;
        EnsureRequestRoutine();
    }

    private void EnsureRequestRoutine()
    {
        if (requestRoutine == null)
        {
            requestRoutine = StartCoroutine(ProcessRequestsRoutine());
        }
    }

    private IEnumerator ProcessRequestsRoutine()
    {
        // Give a newly-created loading canvas one complete frame before starting scene IO.
        yield return null;

        while (true)
        {
            if (sceneRequests.TryTake(out SceneLoadRequest request))
            {
                waitForCurrentWorldReady = false;
                SetProgress(0f);
                AsyncOperation operation = request.UsesBuildIndex
                    ? SceneManager.LoadSceneAsync(request.BuildIndex, LoadSceneMode.Single)
                    : SceneManager.LoadSceneAsync(request.SceneName, LoadSceneMode.Single);
                if (operation == null)
                {
                    Debug.LogError("[LoadingScreen] 씬 로드 요청을 생성하지 못했습니다.");
                    if (sceneRequests.HasPending)
                    {
                        continue;
                    }

                    requestRoutine = null;
                    Destroy(gameObject);
                    yield break;
                }

                sceneOperationActive = true;
                // Unity cannot cancel a LoadSceneAsync operation after it starts. Keep the
                // newest replacement queued and run it as soon as this operation completes.
                while (!operation.isDone)
                {
                    float sceneProgress = Mathf.Clamp01(operation.progress / 0.9f);
                    SetDisplayedProgress(sceneProgress * SceneProgressWeight);
                    yield return null;
                }
                sceneOperationActive = false;

                waitForCurrentWorldReady =
                    SceneManager.GetActiveScene().name == GameSceneName;
            }

            if (sceneRequests.HasPending)
            {
                continue;
            }

            if (waitForCurrentWorldReady)
            {
                bool worldReady = false;
                while (!worldReady && !sceneRequests.HasPending)
                {
                    Scene activeScene = SceneManager.GetActiveScene();
                    if (activeScene.IsValid() && activeScene.name != GameSceneName)
                    {
                        // A caller bypassed this helper and loaded another scene directly.
                        // Treat that active scene as the new owner instead of leaving the
                        // persistent loading overlay waiting forever for GameScene.
                        waitForCurrentWorldReady = false;
                        break;
                    }

                    TryGetGameWorldLoadState(out float worldProgress, out worldReady);
                    SetDisplayedProgress(
                        SceneProgressWeight
                        + ((1f - SceneProgressWeight) * worldProgress));
                    yield return null;
                }

                if (sceneRequests.HasPending)
                {
                    continue;
                }
            }

            SetProgress(1f);
            yield return null;
            if (sceneRequests.HasPending)
            {
                SetProgress(0f);
                continue;
            }

            requestRoutine = null;
            Destroy(gameObject);
            yield break;
        }
    }

    private static void TryGetGameWorldLoadState(out float progress, out bool ready)
    {
        progress = 0f;
        ready = false;
        if (SceneManager.GetActiveScene().name != GameSceneName)
        {
            return;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return;
        }

        progress = terrain.WorldLoadingProgress;
        ready = terrain.IsWorldReadyForPresentation;
    }

    private void BuildView()
    {
        Canvas canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rootRect = (RectTransform)transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        Image backdrop = gameObject.AddComponent<Image>();
        backdrop.color = new Color(0.025f, 0.03f, 0.04f, 1f);
        backdrop.raycastTarget = true;

        RectTransform trackRect = CreateImage(
            transform,
            "Loading Bar",
            new Color(0.12f, 0.14f, 0.16f, 1f));
        trackRect.anchorMin = new Vector2(0.12f, 0f);
        trackRect.anchorMax = new Vector2(0.88f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.anchoredPosition = new Vector2(0f, 48f);
        trackRect.sizeDelta = new Vector2(0f, 18f);

        RectTransform innerRect = CreateImage(
            trackRect,
            "Inner",
            new Color(0.045f, 0.055f, 0.065f, 1f));
        Stretch(innerRect, 3f);

        fillRect = CreateImage(
            innerRect,
            "Progress",
            new Color(0.95f, 0.78f, 0.2f, 1f));
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
    }

    private static RectTransform CreateImage(Transform parent, string objectName, Color color)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private void SetDisplayedProgress(float progress)
    {
        SetProgress(Mathf.Max(displayedProgress, progress));
    }

    private void SetProgress(float progress)
    {
        displayedProgress = Mathf.Clamp01(progress);
        if (fillRect != null)
        {
            fillRect.anchorMax = new Vector2(displayedProgress, 1f);
        }
    }
}
