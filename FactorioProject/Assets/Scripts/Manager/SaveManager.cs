using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectF.Persistence;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SaveManager : MonoBehaviour
{
    public const int SlotCount = 10;

    private const string RecentSlotPlayerPrefsKey = "ProjectF.SaveManager.RecentSlot";
    private const string SaveFileExtension = ".pfsave";
    private const int SaveSnapshotBatchSize = 16;
    private const int SaveSnapshotBeltLaneBatchSize = 64;
    private static readonly ProfilerMarker SaveSnapshotSliceMarker = new ProfilerMarker("SaveManager.SnapshotSlice");

    private static SaveGameData pendingRuntimeLoadData;
    private static int pendingRuntimeLoadSlot = -1;
    private static bool pendingRuntimeStartNewMap;
    private static bool publishingRuntimeSceneLoad;
    private static int runtimeSaveInputBlockDepth;

    [Header("Inspector")]
    [SerializeField]
    [Range(0, SlotCount - 1)]
    private int selectedSlotIndex;

    [Header("Startup")]
    [SerializeField]
    private bool loadRecentSlotOnStart = true;
    [SerializeField]
    private bool randomizeEmptySlotMap = true;

    [Header("Save Performance")]
    [SerializeField, Range(0.5f, 8f)]
    private float snapshotFrameBudgetMilliseconds = 4f;

    private PlayerSaveData defaultPlayerState;
    private bool hasDefaultPlayerState;
    private readonly bool[] cachedSaveFileExists = new bool[SlotCount];
    private bool saveFileExistenceCacheInitialized;
    private string cachedSaveDirectory;
    private bool startupLoadCompleted;
    private bool sceneReloadRequested;
    private Coroutine activeSaveCoroutine;
    private SaveSnapshotScheduler activeSaveSnapshot;
    private Task activeSaveWriteTask;
    private Coroutine activeLoadCoroutine;
    private Task<SaveGameData> activeLoadReadTask;
    private bool saveTickPauseActive;
    private bool saveInputBlockActive;

    public bool IsSaving => activeSaveCoroutine != null
                            || (activeSaveWriteTask != null && !activeSaveWriteTask.IsCompleted);
    public static bool GameplayInputBlocked => runtimeSaveInputBlockDepth > 0;
    public bool IsLoading => sceneReloadRequested
                             || (TerrainGenerator.Active != null && TerrainGenerator.Active.IsWorldRestorePending)
                             || activeLoadCoroutine != null
                             || (activeLoadReadTask != null && !activeLoadReadTask.IsCompleted);

    public int SelectedSlotIndex
    {
        get => NormalizeSlotIndex(selectedSlotIndex);
        set => selectedSlotIndex = NormalizeSlotIndex(value);
    }

    public bool WillInitializeTerrainOnStart =>
        isActiveAndEnabled
        && (loadRecentSlotOnStart || pendingRuntimeLoadSlot >= 0);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeLoadState()
    {
        pendingRuntimeLoadData = null;
        pendingRuntimeLoadSlot = -1;
        pendingRuntimeStartNewMap = false;
        publishingRuntimeSceneLoad = false;
        runtimeSaveInputBlockDepth = 0;
        SlotLoadTimingLog.Reset();
        SlotSaveTimingLog.Reset();
    }

    internal static void DiscardPendingRuntimeLoadForSceneReplacement()
    {
        if (!publishingRuntimeSceneLoad)
            SlotLoadTimingLog.CancelActive("scene-load-replaced");
        pendingRuntimeLoadData = null;
        pendingRuntimeLoadSlot = -1;
        pendingRuntimeStartNewMap = false;
    }

    private IEnumerator Start()
    {
        CaptureDefaultPlayerState();

        yield return null;

        if (TryConsumePendingRuntimeLoad(
                out int pendingSlot,
                out SaveGameData pendingData,
                out bool startNewMap))
        {
            if (startNewMap)
            {
                StartNewMap(pendingSlot);
            }
            else
            {
                ApplyLoadedSlotData(pendingSlot, pendingData, GetSlotPath(pendingSlot));
            }

            startupLoadCompleted = true;
            yield break;
        }

        if (loadRecentSlotOnStart)
        {
            int recentSlot = NormalizeSlotIndex(PlayerPrefs.GetInt(RecentSlotPlayerPrefsKey, 0));
            yield return LoadStartupSlotRoutine(recentSlot);
        }

        startupLoadCompleted = true;
    }

    public void SaveSelectedSlot()
    {
        SaveSlot(SelectedSlotIndex);
    }

    public void LoadSelectedSlot()
    {
        LoadSlot(SelectedSlotIndex);
    }

    public bool ResetSelectedSlot()
    {
        return ResetSlot(SelectedSlotIndex);
    }

    public bool SaveSlot(int slotIndex)
    {
        slotIndex = NormalizeSlotIndex(slotIndex);
        SelectedSlotIndex = slotIndex;

        if (IsSaving || IsLoading)
        {
            Debug.LogWarning("[SaveManager] 저장 또는 불러오기가 진행 중이어서 새 저장 요청을 건너뜁니다.");
            return false;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        Player player = ResolvePlayer();
        if (terrain == null)
        {
            Debug.LogWarning("[SaveManager] TerrainGenerator를 찾을 수 없어 저장하지 못했습니다.");
            return false;
        }

        if (!Application.isPlaying)
        {
            SlotSaveTimingLog.Begin(slotIndex, "immediate-save");
            return SaveSlotImmediate(slotIndex, terrain, player);
        }

        if (!terrain.IsWorldReadyForPresentation || terrain.IsChunkStreamingBusy)
        {
            Debug.LogWarning("[SaveManager] 월드 복원 또는 청크 처리가 끝나기 전에는 저장할 수 없습니다.");
            return false;
        }

        SlotSaveTimingLog.Begin(slotIndex, "runtime-save");
        BeginSaveInputBlock();
        try
        {
            activeSaveCoroutine = StartCoroutine(SaveSlotRoutine(slotIndex, terrain, player));
        }
        catch (Exception exception)
        {
            SlotSaveTimingLog.Fail(slotIndex, "save-coroutine-start-failed: " + exception.GetType().Name);
            EndSaveInputBlock();
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 저장 코루틴 시작 실패: {exception}");
            return false;
        }

        if (activeSaveCoroutine == null)
        {
            SlotSaveTimingLog.Fail(slotIndex, "save-coroutine-missing");
            EndSaveInputBlock();
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 저장 코루틴을 생성하지 못했습니다.");
            return false;
        }

        return true;
    }

    private bool SaveSlotImmediate(int slotIndex, TerrainGenerator terrain, Player player)
    {
        string path = GetSlotPath(slotIndex);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            SaveGameData data = CaptureSaveData(terrain, player);
            SlotSaveTimingLog.MarkSnapshotComplete(
                slotIndex, timer.Elapsed.TotalMilliseconds, timer.Elapsed.TotalMilliseconds, 1, 0);
            SaveGameBinarySerializer.WriteToFile(path, data);
            CompleteSuccessfulSave(slotIndex, path);
            return true;
        }
        catch (Exception exception)
        {
            SlotSaveTimingLog.Fail(slotIndex, "immediate-save-failed: " + exception.GetType().Name);
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 저장 실패: {exception}");
            return false;
        }
    }

    private IEnumerator SaveSlotRoutine(int slotIndex, TerrainGenerator terrain, Player player)
    {
        try
        {
            // Let the current frame present the saving state before snapshot work starts.
            yield return null;

            SaveGameData data = null;
            Exception captureException = null;
            var saveTimer = System.Diagnostics.Stopwatch.StartNew();
            BeginSaveTickPause();
            IEnumerator captureRoutine = CaptureSaveDataIncremental(
                terrain,
                player,
                captured => data = captured);
            activeSaveSnapshot = new SaveSnapshotScheduler(
                captureRoutine, Mathf.Clamp(snapshotFrameBudgetMilliseconds, 0.5f, 8f));
            using (SaveSnapshotScheduler scheduler = activeSaveSnapshot)
            {
                while (captureException == null)
                {
                    bool hasNext = false;
                    try
                    {
                        using (SaveSnapshotSliceMarker.Auto()) { hasNext = scheduler.RunSlice(); }
                    }
                    catch (Exception exception)
                    {
                        captureException = exception;
                    }

                    if (!hasNext) break;
                    yield return null;
                }

                if (captureException == null)
                {
                    SlotSaveTimingLog.MarkSnapshotComplete(
                        slotIndex,
                        scheduler.ActiveMilliseconds,
                        scheduler.MaxSliceMilliseconds,
                        scheduler.SliceCount,
                        scheduler.CheckpointCount);
                    Debug.Log($"[SaveManager] Snapshot wallMs={saveTimer.Elapsed.TotalMilliseconds:F1} "
                        + $"activeMs={scheduler.ActiveMilliseconds:F1} maxSliceMs={scheduler.MaxSliceMilliseconds:F1} "
                        + $"frames={scheduler.SliceCount} checkpoints={scheduler.CheckpointCount}");
                }
            }
            activeSaveSnapshot = null;

            if (captureException == null && data == null)
            {
                captureException = new InvalidOperationException("Save snapshot capture did not complete.");
            }

            // The detached snapshot no longer needs the live simulation to stay paused.
            EndSaveTickPause();

            if (captureException != null)
            {
                SlotSaveTimingLog.Fail(slotIndex, "snapshot-failed: " + captureException.GetType().Name);
                Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 상태 수집 실패: {captureException}");
                yield break;
            }

            // The snapshot is detached from live Unity objects. Compression and disk I/O
            // can therefore run off the main thread while frames continue rendering.
            string path = GetSlotPath(slotIndex);
            Exception taskStartException = null;
            saveTimer.Restart();
            try
            {
                activeSaveWriteTask = Task.Factory.StartNew(
                    () => WriteSaveSnapshotOnBackgroundThread(path, data),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
            catch (Exception exception)
            {
                taskStartException = exception;
            }

            if (taskStartException != null)
            {
                SlotSaveTimingLog.Fail(slotIndex, "write-task-start-failed: " + taskStartException.GetType().Name);
                Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 저장 작업 시작 실패: {taskStartException}");
                yield break;
            }

            while (!activeSaveWriteTask.IsCompleted)
            {
                yield return null;
            }

            if (activeSaveWriteTask.IsFaulted)
            {
                Exception writeException = activeSaveWriteTask.Exception?.GetBaseException();
                SlotSaveTimingLog.Fail(slotIndex, "write-failed: " + (writeException?.GetType().Name ?? "Unknown"));
                Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 저장 실패: {writeException}");
            }
            else if (activeSaveWriteTask.IsCanceled)
            {
                SlotSaveTimingLog.Fail(slotIndex, "write-cancelled");
                Debug.LogWarning($"[SaveManager] Slot {slotIndex + 1} 저장이 취소되었습니다.");
            }
            else
            {
                Debug.Log($"[SaveManager] Compress/write wallMs={saveTimer.Elapsed.TotalMilliseconds:F1}");
                CompleteSuccessfulSave(slotIndex, path);
            }
        }
        finally
        {
            FinishSaveOperation();
        }
    }

    private static SaveGameData CaptureSaveData(TerrainGenerator terrain, Player player)
    {
        if (terrain == null || (Application.isPlaying
            && (!terrain.IsWorldReadyForPresentation || terrain.IsChunkStreamingBusy)))
            throw new InvalidOperationException("Cannot capture an incomplete or streaming world.");
        if (!MapObjectTickManager.CanCaptureCheckpoint)
            throw new InvalidOperationException("Cannot save inside a simulation tick or with unapplied commands.");

        // Finish native topology/pending writes before map DTOs read occupied lanes.
        // Capture stays synchronous: no frame callback may mutate live state between sections.
        var beltSnapshot = terrain.CaptureBeltSimulationSnapshot();
        return new SaveGameData
        {
            version = SaveGameData.CurrentVersion,
            savedAtUtcTicks = DateTime.UtcNow.Ticks,
            itemCatalog = SaveGameItemIdRemapper.CaptureItemCatalog(
                GameManager.Instance?.ItemManger?.ItemDefinitions),
            terrain = terrain.CaptureTerrainSaveState(),
            worldTime = GameManager.Instance?.WorldTime?.CaptureSaveState() ?? new WorldTimeSaveData(),
            map = terrain.CaptureMapSaveState(),
            player = player != null ? player.CaptureSaveState() : new PlayerSaveData(),
            beltSimulation = beltSnapshot,
            simulationTick = MapObjectTickManager.CurrentSimulationTick,
            nextInstallationSimulationId = InstallationObject.NextSimulationId
        };
    }

    private static IEnumerator CaptureSaveDataIncremental(
        TerrainGenerator terrain,
        Player player,
        Action<SaveGameData> completed)
    {
        if (terrain == null || !terrain.IsWorldReadyForPresentation || terrain.IsChunkStreamingBusy)
            throw new InvalidOperationException("Cannot capture an incomplete or streaming world.");
        if (!MapObjectTickManager.CanCaptureCheckpoint)
            throw new InvalidOperationException("Cannot save inside a simulation tick or with unapplied commands.");

        ProjectF.Conveyors.BeltSimulationSnapshot beltSnapshot = null;
        IEnumerator beltCapture = SlotSaveTimingLog.TrackStage(
            "belt-snapshot",
            terrain.CaptureBeltSimulationSnapshotIncremental(
                snapshot => beltSnapshot = snapshot,
                SaveSnapshotBeltLaneBatchSize));
        using (beltCapture as IDisposable)
        {
            while (beltCapture.MoveNext()) yield return beltCapture.Current;
        }

        MapSaveData mapSaveData = new MapSaveData();
        IEnumerator mapCapture = terrain.CaptureMapSaveStateIncremental(
            mapSaveData,
            SaveSnapshotBatchSize);
        using (mapCapture as IDisposable)
        {
            while (mapCapture.MoveNext()) yield return mapCapture.Current;
        }

        SaveGameData data = new SaveGameData
        {
            version = SaveGameData.CurrentVersion,
            savedAtUtcTicks = DateTime.UtcNow.Ticks,
            itemCatalog = SaveGameItemIdRemapper.CaptureItemCatalog(
                GameManager.Instance?.ItemManger?.ItemDefinitions),
            terrain = terrain.CaptureTerrainSaveState(),
            worldTime = GameManager.Instance?.WorldTime?.CaptureSaveState() ?? new WorldTimeSaveData(),
            map = mapSaveData,
            player = player != null ? player.CaptureSaveState() : new PlayerSaveData(),
            beltSimulation = beltSnapshot,
            simulationTick = MapObjectTickManager.CurrentSimulationTick,
            nextInstallationSimulationId = InstallationObject.NextSimulationId
        };
        completed?.Invoke(data);
    }

    private void CompleteSuccessfulSave(int slotIndex, string path)
    {
        SetCachedSaveFileExists(slotIndex, true);
        SetRecentSlot(slotIndex);
        SlotSaveTimingLog.Complete(slotIndex);
        Debug.Log($"[SaveManager] Slot {slotIndex + 1} 저장 완료: {path}");
    }

    private static void WriteSaveSnapshotOnBackgroundThread(string path, SaveGameData data)
    {
        Thread thread = Thread.CurrentThread;
        System.Threading.ThreadPriority originalPriority = System.Threading.ThreadPriority.Normal;
        bool priorityChanged = false;
        try
        {
            originalPriority = thread.Priority;
            thread.Priority = System.Threading.ThreadPriority.BelowNormal;
            priorityChanged = true;
        }
        catch (Exception)
        {
            // Some Unity targets do not expose OS thread priorities. Saving remains valid
            // there; only the scheduling preference is unavailable.
        }

        try
        {
            SaveGameBinarySerializer.WriteToFile(path, data);
        }
        finally
        {
            if (priorityChanged)
            {
                try
                {
                    thread.Priority = originalPriority;
                }
                catch (Exception)
                {
                    // The task is already complete, so failure to restore priority is harmless.
                }
            }
        }
    }

    private void BeginSaveTickPause()
    {
        if (saveTickPauseActive)
        {
            return;
        }

        saveTickPauseActive = true;
        MapObjectTickManager.BeginSaveTickPause();
    }

    private void FinishSaveOperation()
    {
        try { activeSaveSnapshot?.Dispose(); }
        finally
        {
            SlotSaveTimingLog.CancelActive("save-operation-ended-without-result");
            activeSaveSnapshot = null;
            EndSaveTickPause();
            EndSaveInputBlock();
            activeSaveWriteTask = null;
            activeSaveCoroutine = null;
        }
    }

    private void BeginSaveInputBlock()
    {
        if (saveInputBlockActive)
        {
            return;
        }

        saveInputBlockActive = true;
        runtimeSaveInputBlockDepth++;
    }

    private void EndSaveInputBlock()
    {
        if (!saveInputBlockActive)
        {
            return;
        }

        saveInputBlockActive = false;
        runtimeSaveInputBlockDepth = Mathf.Max(0, runtimeSaveInputBlockDepth - 1);
    }

    private void EndSaveTickPause()
    {
        if (!saveTickPauseActive)
        {
            return;
        }

        saveTickPauseActive = false;
        MapObjectTickManager.EndSaveTickPause();
    }

    private void OnDestroy()
    {
        // A scene replacement must release a suspended snapshot even when Unity's
        // coroutine driver does not dispose the outer iterator.
        FinishSaveOperation();
    }

    public bool LoadSlot(int slotIndex)
    {
        if (IsSaving || IsLoading)
        {
            Debug.LogWarning("[SaveManager] 저장 또는 불러오기가 끝나기 전에는 슬롯을 불러올 수 없습니다.");
            return false;
        }

        slotIndex = NormalizeSlotIndex(slotIndex);
        SelectedSlotIndex = slotIndex;
        SlotLoadTimingLog.Begin(
            slotIndex,
            Application.isPlaying && startupLoadCompleted ? "runtime-scene-reload" : "direct-load");

        if (Application.isPlaying && startupLoadCompleted)
        {
            return ReloadSceneForSlot(slotIndex);
        }

        return LoadSlotImmediate(slotIndex);
    }

    private bool LoadSlotImmediate(int slotIndex)
    {
        slotIndex = NormalizeSlotIndex(slotIndex);
        SelectedSlotIndex = slotIndex;
        string path = GetSlotPath(slotIndex);

        if (!HasSaveFile(slotIndex))
        {
            StartNewMap(slotIndex);
            return true;
        }

        try
        {
            SaveGameData data = SaveGameBinarySerializer.ReadFromFile(path);
            SlotLoadTimingLog.MarkPayloadReady(slotIndex);
            if (data == null)
            {
                StartNewMap(slotIndex);
                return true;
            }

            return ApplyLoadedSlotData(slotIndex, data, path);
        }
        catch (Exception exception)
        {
            SlotLoadTimingLog.Fail(slotIndex, "read-or-apply-failed: " + exception.GetType().Name);
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 로드 실패: {exception}");
            return false;
        }
    }

    private IEnumerator LoadStartupSlotRoutine(int slotIndex)
    {
        slotIndex = NormalizeSlotIndex(slotIndex);
        SelectedSlotIndex = slotIndex;
        SlotLoadTimingLog.Begin(slotIndex, "startup-recent-slot");
        string path = GetSlotPath(slotIndex);
        if (!HasSaveFile(slotIndex))
        {
            StartNewMap(slotIndex);
            yield break;
        }

        Exception taskStartException = null;
        try
        {
            activeLoadReadTask = Task.Run(() => SaveGameBinarySerializer.ReadFromFile(path));
        }
        catch (Exception exception)
        {
            taskStartException = exception;
        }

        if (taskStartException != null)
        {
            SlotLoadTimingLog.Fail(slotIndex, "read-task-start-failed: " + taskStartException.GetType().Name);
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 로드 작업 시작 실패: {taskStartException}");
            activeLoadReadTask = null;
            yield break;
        }

        while (!activeLoadReadTask.IsCompleted)
        {
            yield return null;
        }

        Task<SaveGameData> completedTask = activeLoadReadTask;
        activeLoadReadTask = null;
        if (completedTask.IsFaulted)
        {
            Exception readException = completedTask.Exception?.GetBaseException();
            SlotLoadTimingLog.Fail(slotIndex, "read-failed: " + (readException?.GetType().Name ?? "Unknown"));
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 로드 실패: {readException}");
            yield break;
        }

        if (completedTask.IsCanceled)
        {
            SlotLoadTimingLog.Fail(slotIndex, "read-cancelled");
            Debug.LogWarning($"[SaveManager] Slot {slotIndex + 1} 로드가 취소되었습니다.");
            yield break;
        }

        SaveGameData data = completedTask.Result;
        SlotLoadTimingLog.MarkPayloadReady(slotIndex);
        if (data == null)
        {
            StartNewMap(slotIndex);
            yield break;
        }

        ApplyLoadedSlotData(slotIndex, data, path);
    }

    private bool ReloadSceneForSlot(int slotIndex)
    {
        if (sceneReloadRequested)
        {
            SlotLoadTimingLog.Fail(slotIndex, "scene-reload-already-requested");
            return false;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            SlotLoadTimingLog.Fail(slotIndex, "active-scene-invalid");
            Debug.LogError("[SaveManager] 활성 씬을 찾을 수 없어 런타임 로드를 시작하지 못했습니다.");
            return false;
        }

        string path = GetSlotPath(slotIndex);
        bool startNewMap = !HasSaveFile(slotIndex);
        sceneReloadRequested = true;
        if (!startNewMap)
        {
            activeLoadCoroutine = StartCoroutine(ReloadSceneForSlotRoutine(
                slotIndex,
                path,
                activeScene.buildIndex,
                activeScene.name));
            return true;
        }

        return StartSceneReloadForSlot(
            slotIndex,
            null,
            true,
            activeScene.buildIndex,
            activeScene.name);
    }

    private IEnumerator ReloadSceneForSlotRoutine(
        int slotIndex,
        string path,
        int sceneBuildIndex,
        string sceneName)
    {
        Exception taskStartException = null;
        try
        {
            activeLoadReadTask = Task.Run(() => SaveGameBinarySerializer.ReadFromFile(path));
        }
        catch (Exception exception)
        {
            taskStartException = exception;
        }

        if (taskStartException != null)
        {
            SlotLoadTimingLog.Fail(slotIndex, "read-task-start-failed: " + taskStartException.GetType().Name);
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 로드 작업 시작 실패: {taskStartException}");
            ResetActiveLoadState();
            yield break;
        }

        // Ensure StartCoroutine returns before this routine clears its tracked handle.
        yield return null;
        while (!activeLoadReadTask.IsCompleted)
        {
            yield return null;
        }

        Task<SaveGameData> completedTask = activeLoadReadTask;
        activeLoadReadTask = null;
        activeLoadCoroutine = null;
        if (completedTask.IsFaulted)
        {
            Exception readException = completedTask.Exception?.GetBaseException();
            SlotLoadTimingLog.Fail(slotIndex, "read-failed: " + (readException?.GetType().Name ?? "Unknown"));
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 로드 실패: {readException}");
            ResetActiveLoadState();
            yield break;
        }

        if (completedTask.IsCanceled)
        {
            SlotLoadTimingLog.Fail(slotIndex, "read-cancelled");
            Debug.LogWarning($"[SaveManager] Slot {slotIndex + 1} 로드가 취소되었습니다.");
            ResetActiveLoadState();
            yield break;
        }

        SaveGameData data = completedTask.Result;
        SlotLoadTimingLog.MarkPayloadReady(slotIndex);
        StartSceneReloadForSlot(
            slotIndex,
            data,
            data == null,
            sceneBuildIndex,
            sceneName);
    }

    private bool StartSceneReloadForSlot(
        int slotIndex,
        SaveGameData data,
        bool startNewMap,
        int sceneBuildIndex,
        string sceneName)
    {
        bool reloadStarted = false;
        publishingRuntimeSceneLoad = true;
        try
        {
            if (sceneBuildIndex >= 0)
                reloadStarted = GameSceneLoadingScreen.TryLoadSceneAsync(sceneBuildIndex);
            else
                reloadStarted = GameSceneLoadingScreen.TryLoadSceneAsync(sceneName);
        }
        finally
        {
            publishingRuntimeSceneLoad = false;
        }

        if (!reloadStarted)
        {
            SlotLoadTimingLog.Fail(slotIndex, "scene-reload-request-failed");
            ResetActiveLoadState();
            Debug.LogError("[SaveManager] 활성 씬 재로드 요청을 생성하지 못했습니다.");
            return false;
        }

        // TryLoadSceneAsync can replace an older active screen and discard its pending
        // slot payload. Publish this request only after the screen accepted it so the new
        // payload cannot be mistaken for the superseded load.
        pendingRuntimeLoadSlot = slotIndex;
        pendingRuntimeLoadData = data;
        pendingRuntimeStartNewMap = startNewMap;
        SetRecentSlot(slotIndex);
        return true;
    }

    private void ResetActiveLoadState()
    {
        pendingRuntimeLoadSlot = -1;
        pendingRuntimeLoadData = null;
        pendingRuntimeStartNewMap = false;
        sceneReloadRequested = false;
        activeLoadCoroutine = null;
        activeLoadReadTask = null;
    }

    private bool ApplyLoadedSlotData(int slotIndex, SaveGameData data, string path)
    {
        if (data == null)
        {
            return false;
        }

        try
        {
            SaveGameItemIdRemapper.RemapToCurrentDefinitions(
                data,
                GameManager.Instance?.ItemManger?.ItemDefinitions);
            ApplySaveData(data, () =>
            {
                SetRecentSlot(slotIndex);
                CompleteLoadedSlotTimingWhenWorldReady(slotIndex);
                Debug.Log($"[SaveManager] Slot {slotIndex + 1} 최종 상태 복원 완료: {path}");
            });
            return true;
        }
        catch (Exception exception)
        {
            SlotLoadTimingLog.Fail(slotIndex, "apply-failed: " + exception.GetType().Name);
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 적용 실패: {exception}");
            return false;
        }
    }

    private static bool TryConsumePendingRuntimeLoad(
        out int slotIndex,
        out SaveGameData data,
        out bool startNewMap)
    {
        if (pendingRuntimeLoadSlot < 0)
        {
            slotIndex = -1;
            data = null;
            startNewMap = false;
            return false;
        }

        slotIndex = pendingRuntimeLoadSlot;
        data = pendingRuntimeLoadData;
        startNewMap = pendingRuntimeStartNewMap;
        pendingRuntimeLoadSlot = -1;
        pendingRuntimeLoadData = null;
        pendingRuntimeStartNewMap = false;
        return true;
    }

    private void CompleteLoadedSlotTimingWhenWorldReady(int slotIndex)
    {
        if (!SlotLoadTimingLog.IsActiveFor(slotIndex)) return;
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (!Application.isPlaying || terrain == null)
        {
            SlotLoadTimingLog.Complete(slotIndex, "saved-world-ready");
            return;
        }
        StartCoroutine(CompleteSavedWorldLoadTimingWhenReady(slotIndex, terrain));
    }

    private static IEnumerator CompleteSavedWorldLoadTimingWhenReady(
        int slotIndex,
        TerrainGenerator terrain)
    {
        while (terrain != null && terrain.IsWorldRestorePending)
            yield return null;

        if (!SlotLoadTimingLog.IsActiveFor(slotIndex)) yield break;
        if (terrain != null && terrain.IsWorldReadyForPresentation)
            SlotLoadTimingLog.Complete(slotIndex, "saved-world-ready");
        else
            SlotLoadTimingLog.Fail(slotIndex, "saved-world-restore-failed");
    }

    public bool ResetSlot(int slotIndex)
    {
        if (IsSaving || IsLoading)
        {
            Debug.LogWarning("[SaveManager] 저장 또는 불러오기가 끝나기 전에는 슬롯을 초기화할 수 없습니다.");
            return false;
        }

        slotIndex = NormalizeSlotIndex(slotIndex);
        SelectedSlotIndex = slotIndex;

        string path = GetSlotPath(slotIndex);
        try
        {
            if (HasSaveFile(slotIndex, true))
            {
                File.Delete(path);
                SetCachedSaveFileExists(slotIndex, false);
                Debug.Log($"[SaveManager] Slot {slotIndex + 1} 저장 파일 삭제 완료: {path}");
            }
            else
            {
                Debug.Log($"[SaveManager] Slot {slotIndex + 1} 저장 파일이 없어 삭제를 건너뜁니다: {path}");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 리셋 실패: {exception}");
            return false;
        }

        if (Application.isPlaying)
        {
            if (startupLoadCompleted)
            {
                return ReloadSceneForSlot(slotIndex);
            }

            StartNewMap(slotIndex);
        }

        return true;
    }

    public void StartNewMap(int slotIndex)
    {
        StartNewMap(slotIndex, randomizeEmptySlotMap);
    }

    public void StartNewMap(int slotIndex, bool randomizeSeed)
    {
        if (IsSaving || IsLoading)
        {
            Debug.LogWarning("[SaveManager] 저장 또는 불러오기가 끝나기 전에는 새 맵을 시작할 수 없습니다.");
            return;
        }

        slotIndex = NormalizeSlotIndex(slotIndex);
        SelectedSlotIndex = slotIndex;
        EnsureDefaultPlayerState();

        Player player = ResolvePlayer();
        if (player != null)
        {
            player.ApplySaveState(defaultPlayerState);
        }

        WorldTimeService worldTime = GameManager.Instance?.WorldTime ?? WorldTimeService.Active;
        worldTime?.ResetToDefault();

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain != null)
        {
            terrain.StartNewGeneratedMap(randomizeSeed);
            if (Application.isPlaying)
            {
                GameSceneLoadingScreen.TryShowUntilWorldReady();
            }

            if (SlotLoadTimingLog.IsActiveFor(slotIndex))
            {
                if (Application.isPlaying)
                    StartCoroutine(CompleteNewMapLoadTimingWhenReady(slotIndex, terrain));
                else if (terrain.IsWorldReadyForPresentation)
                    SlotLoadTimingLog.Complete(slotIndex, "new-map-world-ready");
                else
                    SlotLoadTimingLog.Fail(slotIndex, "new-map-world-not-ready");
            }
        }
        else if (SlotLoadTimingLog.IsActiveFor(slotIndex))
        {
            SlotLoadTimingLog.Fail(slotIndex, "terrain-generator-missing");
        }

        SetRecentSlot(slotIndex);
        Debug.Log($"[SaveManager] Slot {slotIndex + 1}에 새 맵을 시작했습니다. randomSeed={randomizeSeed}");
    }

    private static IEnumerator CompleteNewMapLoadTimingWhenReady(
        int slotIndex,
        TerrainGenerator terrain)
    {
        while (terrain != null && terrain.IsWorldRestorePending)
            yield return null;

        if (!SlotLoadTimingLog.IsActiveFor(slotIndex)) yield break;
        if (terrain != null && terrain.IsWorldReadyForPresentation)
            SlotLoadTimingLog.Complete(slotIndex, "new-map-world-ready");
        else
            SlotLoadTimingLog.Fail(slotIndex, "new-map-world-restore-failed");
    }

    public string[] BuildSlotLabels()
    {
        string[] labels = new string[SlotCount];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = GetSlotLabel(i);
        }

        return labels;
    }

    public string GetSlotLabel(int slotIndex)
    {
        slotIndex = NormalizeSlotIndex(slotIndex);
        string label = $"Slot {slotIndex + 1}";
        if (HasSaveFile(slotIndex))
        {
            label += " *";
        }

        return label;
    }

    public bool HasSaveFile(int slotIndex)
    {
        return HasSaveFile(slotIndex, false);
    }

    public bool HasSaveFile(int slotIndex, bool forceRefresh)
    {
        slotIndex = NormalizeSlotIndex(slotIndex);
        if (forceRefresh || !saveFileExistenceCacheInitialized)
        {
            RefreshSaveFileExistenceCache();
        }

        return cachedSaveFileExists[slotIndex];
    }

    public string GetSaveSlotMask(bool forceRefresh = false)
    {
        if (forceRefresh || !saveFileExistenceCacheInitialized)
        {
            RefreshSaveFileExistenceCache();
        }

        char[] mask = new char[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            mask[i] = cachedSaveFileExists[i] ? '1' : '0';
        }

        return new string(mask);
    }

    public string GetSlotPath(int slotIndex)
    {
        return Path.Combine(GetSaveDirectory(), $"slot_{NormalizeSlotIndex(slotIndex) + 1:00}{SaveFileExtension}");
    }

    private void ApplySaveData(SaveGameData data, Action onRestored)
    {
        if (data == null)
        {
            return;
        }

        Player player = ResolvePlayer();
        MapObjectTickManager.RestoreSimulationTick(data.simulationTick);
        InstallationObject.RestoreNextSimulationId(data.nextInstallationSimulationId);
        if (player != null && data.player != null && data.player.hasPlayer)
        {
            player.ApplyTransformState(data.player);
        }

        WorldTimeService worldTime = GameManager.Instance?.WorldTime ?? WorldTimeService.Active;
        worldTime?.ApplySaveState(data.worldTime);

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain != null)
        {
            if (Application.isPlaying)
            {
                GameSceneLoadingScreen.TryShowUntilWorldReady();
            }

            terrain.LoadFromSaveState(
                data.terrain,
                data.map,
                () =>
                {
                    RecordLoadStage(
                        "checkpoint-belt-snapshot",
                        () => terrain.RestoreBeltSimulationSnapshot(data.beltSimulation));
                    RecordLoadStage(
                        "checkpoint-player",
                        () => CompletePlayerLoad(player, data.player));
                    RecordLoadStage("checkpoint-publish", onRestored);
                });
            return;
        }

        CompletePlayerLoad(player, data.player);
        onRestored?.Invoke();
    }

    private static void RecordLoadStage(string stage, Action action)
    {
        if (action == null)
        {
            return;
        }

        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            action();
        }
        finally
        {
            SlotLoadTimingLog.RecordStageWork(
                stage,
                (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt)
                * (1000d / System.Diagnostics.Stopwatch.Frequency));
        }
    }

    private void CompletePlayerLoad(Player player, PlayerSaveData playerSaveData)
    {
        if (player == null || playerSaveData == null || !playerSaveData.hasPlayer)
        {
            return;
        }

        player.ApplyTransformState(playerSaveData);
        RestorePlayerMountedState(player, playerSaveData);

        player.ApplyInventoryAndStatState(playerSaveData);
        RestorePlayerNooseState(player, playerSaveData);
    }

    private static void RestorePlayerNooseState(
        Player player,
        PlayerSaveData playerSaveData)
    {
        if (player == null
            || playerSaveData == null
            || playerSaveData.nooseLeashedAnimalId == 0L)
        {
            return;
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController == null
            || !playerController.TryRestoreNooseLeashedAnimal(
                playerSaveData.nooseLeashedAnimalId))
        {
            Debug.LogWarning(
                $"[SaveManager] 올가미로 연결된 동물을 복원하지 못했습니다. "
                + $"animalId={playerSaveData.nooseLeashedAnimalId}");
        }
    }

    private void RestorePlayerMountedState(Player player, PlayerSaveData playerSaveData)
    {
        if (player == null || playerSaveData == null)
        {
            return;
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController == null)
        {
            return;
        }

        if (playerSaveData.mountedAnimalId != 0L)
        {
            if (!playerController.TryRestoreMountedAnimal(playerSaveData.mountedAnimalId))
            {
                playerController.ClearInteractionPointSnapForLoad();
            }

            return;
        }

        if (!playerSaveData.mountedOnVehicle)
        {
            playerController.ClearInteractionPointSnapForLoad();
            return;
        }

        Vehicle mountedVehicle = FindVehicleForSavedMount(playerSaveData);
        if (mountedVehicle == null
            || !playerController.TryRestoreMountedVehicle(
                mountedVehicle,
                playerSaveData.mountedVehiclePlayerPointIndex))
        {
            playerController.ClearInteractionPointSnapForLoad();
        }
    }

    private Vehicle FindVehicleForSavedMount(PlayerSaveData playerSaveData)
    {
        if (playerSaveData == null || !playerSaveData.mountedOnVehicle)
        {
            return null;
        }

        Vehicle[] vehicles = FindObjectsOfType<Vehicle>(true);
        Vehicle coordinateFallback = null;
        for (int i = 0; i < vehicles.Length; i++)
        {
            Vehicle vehicle = vehicles[i];
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (playerSaveData.mountedVehiclePlacementSequence > 0
                && vehicle.RuntimePlacementSequence == playerSaveData.mountedVehiclePlacementSequence)
            {
                return vehicle;
            }

            if (coordinateFallback == null
                && vehicle.RuntimeAnchorCoordinate == playerSaveData.mountedVehicleAnchorCoordinate)
            {
                coordinateFallback = vehicle;
            }
        }

        return coordinateFallback;
    }

    private void EnsureDefaultPlayerState()
    {
        if (!hasDefaultPlayerState)
        {
            CaptureDefaultPlayerState();
        }
    }

    private void CaptureDefaultPlayerState()
    {
        Player player = ResolvePlayer();
        defaultPlayerState = player != null ? player.CaptureSaveState() : new PlayerSaveData();
        hasDefaultPlayerState = true;
    }

    private Player ResolvePlayer()
    {
        if (GameManager.Instance != null && GameManager.Instance.Player != null)
        {
            return GameManager.Instance.Player;
        }

        return FindObjectOfType<Player>();
    }

    private void SetRecentSlot(int slotIndex)
    {
        PlayerPrefs.SetInt(RecentSlotPlayerPrefsKey, NormalizeSlotIndex(slotIndex));
        PlayerPrefs.Save();
    }

    private string GetSaveDirectory()
    {
        if (string.IsNullOrEmpty(cachedSaveDirectory))
        {
            cachedSaveDirectory = Path.Combine(Application.persistentDataPath, "Saves");
        }

        return cachedSaveDirectory;
    }

    private void RefreshSaveFileExistenceCache()
    {
        Array.Clear(cachedSaveFileExists, 0, cachedSaveFileExists.Length);

        string saveDirectory = GetSaveDirectory();
        if (Directory.Exists(saveDirectory))
        {
            string[] files = Directory.GetFiles(saveDirectory, $"*{SaveFileExtension}", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                if (TryParseSaveSlotIndex(files[i], out int slotIndex))
                {
                    cachedSaveFileExists[slotIndex] = true;
                }
            }
        }

        saveFileExistenceCacheInitialized = true;
    }

    private void SetCachedSaveFileExists(int slotIndex, bool exists)
    {
        if (!saveFileExistenceCacheInitialized)
        {
            RefreshSaveFileExistenceCache();
        }

        cachedSaveFileExists[NormalizeSlotIndex(slotIndex)] = exists;
    }

    private static bool TryParseSaveSlotIndex(string path, out int slotIndex)
    {
        slotIndex = -1;
        string fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(fileName)
            || !fileName.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(fileName.Substring(5), out int slotNumber))
        {
            return false;
        }

        int normalizedIndex = slotNumber - 1;
        if (normalizedIndex < 0 || normalizedIndex >= SlotCount)
        {
            return false;
        }

        slotIndex = normalizedIndex;
        return true;
    }

    private static int NormalizeSlotIndex(int slotIndex)
    {
        return Mathf.Clamp(slotIndex, 0, SlotCount - 1);
    }
}
