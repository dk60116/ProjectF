using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class GameSceneLoadingScreen : MonoBehaviour
{
    private const string LoadingSceneName = "LoadingScene";
    private const string GameSceneName = "GameScene";
    private const float SceneProgressWeight = 0.15f;

    private static GameSceneLoadingScreen active;

    private RectTransform fillRect;
    private float displayedProgress;
    private bool loadStarted;

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
            || !TryCreate(out GameSceneLoadingScreen screen))
        {
            return false;
        }

        screen.StartCoroutine(screen.LoadSceneRoutine(buildIndex, null));
        return true;
    }

    public static bool TryLoadSceneAsync(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)
            || !Application.CanStreamedLevelBeLoaded(sceneName)
            || !TryCreate(out GameSceneLoadingScreen screen))
        {
            return false;
        }

        screen.StartCoroutine(screen.LoadSceneRoutine(-1, sceneName));
        return true;
    }

    public static bool TryShowUntilWorldReady()
    {
        if (!TryCreate(out GameSceneLoadingScreen screen))
        {
            return false;
        }

        screen.StartCoroutine(screen.WaitForWorldReadyRoutine(0f));
        return true;
    }

    private static bool TryCreate(out GameSceneLoadingScreen screen)
    {
        if (active != null)
        {
            screen = null;
            return false;
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
        if (active == this)
        {
            active = null;
        }
    }

    private IEnumerator LoadSceneRoutine(int buildIndex, string sceneName)
    {
        if (loadStarted)
        {
            yield break;
        }

        loadStarted = true;

        // Give the loading canvas one complete frame before starting scene IO.
        yield return null;

        AsyncOperation operation = buildIndex >= 0
            ? SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single)
            : SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (operation == null)
        {
            Debug.LogError("[LoadingScreen] 씬 로드 요청을 생성하지 못했습니다.");
            Destroy(gameObject);
            yield break;
        }

        while (!operation.isDone)
        {
            float sceneProgress = Mathf.Clamp01(operation.progress / 0.9f);
            SetDisplayedProgress(sceneProgress * SceneProgressWeight);
            yield return null;
        }

        yield return WaitForWorldReadyRoutine(SceneProgressWeight);
    }

    private IEnumerator WaitForWorldReadyRoutine(float completedSceneWeight)
    {
        bool worldReady = false;
        while (!worldReady)
        {
            TryGetGameWorldLoadState(out float worldProgress, out worldReady);
            SetDisplayedProgress(
                completedSceneWeight + ((1f - completedSceneWeight) * worldProgress));
            yield return null;
        }

        SetProgress(1f);
        yield return null;
        Destroy(gameObject);
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
