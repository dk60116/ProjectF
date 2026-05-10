using System;
using System.Collections;
using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    public const int SlotCount = 10;

    private const string RecentSlotPlayerPrefsKey = "ProjectF.SaveManager.RecentSlot";
    private const string SaveFileExtension = ".pfsave";

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

    public int SelectedSlotIndex
    {
        get => NormalizeSlotIndex(selectedSlotIndex);
        set => selectedSlotIndex = NormalizeSlotIndex(value);
    }

    private IEnumerator Start()
    {
        CaptureDefaultPlayerState();

        yield return null;

        if (loadRecentSlotOnStart)
        {
            LoadRecentSlotOrStartNewMap();
        }
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
            terrain = terrain.CaptureTerrainSaveState(),
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

            ApplySaveData(data);
            SetRecentSlot(slotIndex);
            Debug.Log($"[SaveManager] Slot {slotIndex + 1} 로드 완료: {path}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveManager] Slot {slotIndex + 1} 로드 실패: {exception}");
            return false;
        }
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

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain != null)
        {
            terrain.LoadFromSaveState(data.terrain, data.map);
        }

        if (player != null && data.player != null && data.player.hasPlayer)
        {
            player.ApplyTransformState(data.player);
        }

        if (player != null && data.player != null && data.player.hasPlayer)
        {
            player.ApplyInventoryAndStatState(data.player);
        }
    }

    private void LoadRecentSlotOrStartNewMap()
    {
        int recentSlot = NormalizeSlotIndex(PlayerPrefs.GetInt(RecentSlotPlayerPrefsKey, 0));
        LoadSlot(recentSlot);
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
