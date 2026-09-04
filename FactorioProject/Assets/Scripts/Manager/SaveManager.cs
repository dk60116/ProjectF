using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SaveManager : MonoBehaviour
{
    public const int SlotCount = 10;

    private const string RecentSlotPlayerPrefsKey = "ProjectF.SaveManager.RecentSlot";
    private const string SaveFileExtension = ".pfsave";

    private static SaveGameData pendingRuntimeLoadData;
    private static int pendingRuntimeLoadSlot = -1;
    private static bool pendingRuntimeStartNewMap;

    [Header("Inspector")]
    [SerializeField]
    [Range(0, SlotCount - 1)]
    private int selectedSlotIndex;

    [Header("Startup")]
    [SerializeField]
    private bool loadRecentSlotOnStart = true;
    [SerializeField]
    private bool randomizeEmptySlotMap = true;

    private PlayerSaveData defaultPlayerState;
    private bool hasDefaultPlayerState;
    private readonly bool[] cachedSaveFileExists = new bool[SlotCount];
    private bool saveFileExistenceCacheInitialized;
    private string cachedSaveDirectory;
    private bool startupLoadCompleted;
    private bool sceneReloadRequested;

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
            LoadSlotImmediate(
                NormalizeSlotIndex(PlayerPrefs.GetInt(RecentSlotPlayerPrefsKey, 0)));
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

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        Player player = ResolvePlayer();
        if (terrain == null)
        {
            Debug.LogWarning("[SaveManager] TerrainGenerator를 찾을 수 없어 저장하지 못했습니다.");
            return false;
        }

        SaveGameData data = new SaveGameData
        {
            version = SaveGameData.CurrentVersion,
            savedAtUtcTicks = DateTime.UtcNow.Ticks,
            itemCatalog = SaveGameItemIdRemapper.CaptureItemCatalog(GameManager.Instance?.ItemManger?.ItemDefinitions),
            terrain = terrain.CaptureTerrainSaveState(),
            worldTime = GameManager.Instance?.WorldTime?.CaptureSaveState() ?? new WorldTimeSaveData(),
            map = terrain.CaptureMapSaveState(),
            player = player != null ? player.CaptureSaveState() : new PlayerSaveData()
        };

        string path = GetSlotPath(slotIndex);
        try
        {
            SaveGameBinarySerializer.WriteToFile(path, data);
            SetCachedSaveFileExists(slotIndex, true);
            SetRecentSlot(slotIndex);
            Debug.Log($"[SaveManager] Slot {slotIndex + 1} 저장 완료: {path}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 저장 실패: {exception}");
            return false;
        }
    }

    public bool LoadSlot(int slotIndex)
    {
        slotIndex = NormalizeSlotIndex(slotIndex);
        SelectedSlotIndex = slotIndex;

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
            if (data == null)
            {
                StartNewMap(slotIndex);
                return true;
            }

            return ApplyLoadedSlotData(slotIndex, data, path);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 로드 실패: {exception}");
            return false;
        }
    }

    private bool ReloadSceneForSlot(int slotIndex)
    {
        if (sceneReloadRequested)
        {
            return false;
        }

        string path = GetSlotPath(slotIndex);
        SaveGameData data = null;
        bool startNewMap = !HasSaveFile(slotIndex);
        if (!startNewMap)
        {
            try
            {
                data = SaveGameBinarySerializer.ReadFromFile(path);
                startNewMap = data == null;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 로드 실패: {exception}");
                return false;
            }
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            Debug.LogError("[SaveManager] 활성 씬을 찾을 수 없어 런타임 로드를 시작하지 못했습니다.");
            return false;
        }

        pendingRuntimeLoadSlot = slotIndex;
        pendingRuntimeLoadData = data;
        pendingRuntimeStartNewMap = startNewMap;
        sceneReloadRequested = true;

        AsyncOperation reloadOperation;
        if (activeScene.buildIndex >= 0)
        {
            reloadOperation = SceneManager.LoadSceneAsync(
                activeScene.buildIndex,
                LoadSceneMode.Single);
        }
        else
        {
            reloadOperation = SceneManager.LoadSceneAsync(
                activeScene.name,
                LoadSceneMode.Single);
        }

        if (reloadOperation == null)
        {
            pendingRuntimeLoadSlot = -1;
            pendingRuntimeLoadData = null;
            pendingRuntimeStartNewMap = false;
            sceneReloadRequested = false;
            Debug.LogError("[SaveManager] 활성 씬 재로드 요청을 생성하지 못했습니다.");
            return false;
        }

        SetRecentSlot(slotIndex);
        return true;
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
            ApplySaveData(data);
            SetRecentSlot(slotIndex);
            Debug.Log($"[SaveManager] Slot {slotIndex + 1} 로드 완료: {path}");
            return true;
        }
        catch (Exception exception)
        {
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

    public bool ResetSlot(int slotIndex)
    {
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
        }

        SetRecentSlot(slotIndex);
        Debug.Log($"[SaveManager] Slot {slotIndex + 1}에 새 맵을 시작했습니다. randomSeed={randomizeSeed}");
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

    private void ApplySaveData(SaveGameData data)
    {
        if (data == null)
        {
            return;
        }

        Player player = ResolvePlayer();
        if (player != null && data.player != null && data.player.hasPlayer)
        {
            player.ApplyTransformState(data.player);
        }

        WorldTimeService worldTime = GameManager.Instance?.WorldTime ?? WorldTimeService.Active;
        worldTime?.ApplySaveState(data.worldTime);

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain != null)
        {
            terrain.LoadFromSaveState(data.terrain, data.map);
        }

        if (player != null && data.player != null && data.player.hasPlayer)
        {
            player.ApplyTransformState(data.player);
            RestorePlayerMountedState(player, data.player);
        }

        if (player != null && data.player != null && data.player.hasPlayer)
        {
            player.ApplyInventoryAndStatState(data.player);
            RestorePlayerNooseState(player, data.player);
        }
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
