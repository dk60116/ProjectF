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

        if (!File.Exists(path))
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
            if (File.Exists(path))
            {
                File.Delete(path);
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
        return File.Exists(GetSlotPath(slotIndex));
    }

    public string GetSlotPath(int slotIndex)
    {
        string saveDirectory = Path.Combine(Application.persistentDataPath, "Saves");
        return Path.Combine(saveDirectory, $"slot_{NormalizeSlotIndex(slotIndex) + 1:00}{SaveFileExtension}");
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

    private static int NormalizeSlotIndex(int slotIndex)
    {
        return Mathf.Clamp(slotIndex, 0, SlotCount - 1);
    }
}
