using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapObject : PropObj
{
    public enum MultiFocusMode
    {
        All = 0,
        NearOne = 1,
        None = 2
    }

    [System.Serializable]
    public struct MapObjectStatus
    {
        public byte mapSizeX;
        public byte mapSizeY;
        public byte centerCellX;
        public byte centerCellY;
    }

    [SerializeField]
    private MapObjectStatus mapStatus = new MapObjectStatus
    {
        mapSizeX = 1,
        mapSizeY = 1
    };

    [SerializeField]
    private MultiFocusMode multiFocusMode = MultiFocusMode.NearOne;
    [SerializeField, HideInInspector]
    private bool itemFilterMaskInitialized;
    [SerializeField, HideInInspector]
    private List<ulong> itemFilterMaskWords = new List<ulong>();

    public MapObjectStatus Status => mapStatus;
    public MultiFocusMode FocusMode => multiFocusMode;
    public bool AllowsFocus => multiFocusMode != MultiFocusMode.None;
    public bool IsItemFilterMaskInitialized => itemFilterMaskInitialized;
    public Vector2Int PlacementCenterCell
    {
        get
        {
            int sizeX = Mathf.Max(1, mapStatus.mapSizeX);
            int sizeY = Mathf.Max(1, mapStatus.mapSizeY);
            return new Vector2Int(
                Mathf.Clamp(mapStatus.centerCellX, 0, sizeX - 1),
                Mathf.Clamp(mapStatus.centerCellY, 0, sizeY - 1));
        }
    }

    public void CopyFocusSettingsFrom(MapObject source)
    {
        if (source == null)
        {
            return;
        }

        multiFocusMode = source.multiFocusMode;
    }

    public bool IsItemFilterEnabled(int itemId, int totalItemCount)
    {
        if (itemId < 0)
        {
            return false;
        }

        if (!itemFilterMaskInitialized)
        {
            return true;
        }

        EnsureItemFilterMaskCapacity(Mathf.Max(totalItemCount, itemId + 1), true);

        int wordIndex = itemId >> 6;
        if (wordIndex < 0 || wordIndex >= itemFilterMaskWords.Count)
        {
            return true;
        }

        ulong bitMask = 1UL << (itemId & 63);
        return (itemFilterMaskWords[wordIndex] & bitMask) != 0UL;
    }

    public void SetItemFilterEnabled(int itemId, int totalItemCount, bool enabled)
    {
        if (itemId < 0)
        {
            return;
        }

        int normalizedItemCount = Mathf.Max(totalItemCount, itemId + 1);
        EnsureItemFilterMaskCapacity(normalizedItemCount, true);
        itemFilterMaskInitialized = true;

        int wordIndex = itemId >> 6;
        ulong bitMask = 1UL << (itemId & 63);
        ulong currentWord = itemFilterMaskWords[wordIndex];
        itemFilterMaskWords[wordIndex] = enabled
            ? (currentWord | bitMask)
            : (currentWord & ~bitMask);
        OnItemFilterMaskChanged();
    }

    public List<ulong> CaptureItemFilterMaskWords()
    {
        return new List<ulong>(itemFilterMaskWords ?? new List<ulong>());
    }

    public void ApplyItemFilterMask(IReadOnlyList<ulong> words, bool initialized)
    {
        itemFilterMaskInitialized = initialized;

        if (itemFilterMaskWords == null)
        {
            itemFilterMaskWords = new List<ulong>();
        }
        else
        {
            itemFilterMaskWords.Clear();
        }

        if (words == null)
        {
            OnItemFilterMaskChanged();
            return;
        }

        for (int i = 0; i < words.Count; i++)
        {
            itemFilterMaskWords.Add(words[i]);
        }

        OnItemFilterMaskChanged();
    }

    protected virtual void OnItemFilterMaskChanged()
    {
    }

    private void EnsureItemFilterMaskCapacity(int itemCount, bool enableNewBitsByDefault)
    {
        if (itemCount <= 0)
        {
            return;
        }

        if (itemFilterMaskWords == null)
        {
            itemFilterMaskWords = new List<ulong>();
        }

        int requiredWordCount = Mathf.Max(1, (itemCount + 63) >> 6);
        ulong defaultWordValue = enableNewBitsByDefault ? ulong.MaxValue : 0UL;
        while (itemFilterMaskWords.Count < requiredWordCount)
        {
            itemFilterMaskWords.Add(defaultWordValue);
        }
    }
}
