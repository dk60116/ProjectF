using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class CraftingTreeRuntime
{
    public struct IngredientEntry
    {
        public int itemId;
        public int count;

        public IngredientEntry(int itemId, int count)
        {
            this.itemId = itemId;
            this.count = count;
        }
    }

    private const int CraftingTreeFileVersion = 1;
    private static readonly Dictionary<int, List<int>> CraftableByIngredient = new Dictionary<int, List<int>>();
    private static readonly Dictionary<int, List<IngredientEntry>> IngredientsByItem = new Dictionary<int, List<IngredientEntry>>();
    private static bool loadAttempted;
    private static bool loaded;

    public static bool TryGetCraftableItemIds(int ingredientItemId, List<int> results)
    {
        if (results == null)
        {
            return false;
        }

        EnsureLoaded();
        results.Clear();

        if (CraftableByIngredient.TryGetValue(ingredientItemId, out List<int> craftables))
        {
            results.AddRange(craftables);
        }

        return results.Count > 0;
    }

    public static void ForceReload()
    {
        CraftableByIngredient.Clear();
        IngredientsByItem.Clear();
        loadAttempted = false;
        loaded = false;
        EnsureLoaded();
    }

    public static bool TryGetIngredients(int itemId, List<IngredientEntry> results)
    {
        if (results == null)
        {
            return false;
        }

        EnsureLoaded();
        results.Clear();

        if (IngredientsByItem.TryGetValue(itemId, out List<IngredientEntry> ingredients))
        {
            results.AddRange(ingredients);
        }

        return results.Count > 0;
    }

    private static void EnsureLoaded()
    {
        if (loaded || loadAttempted)
        {
            return;
        }

        loadAttempted = true;

        byte[] data = LoadCraftingTreeBytes();
        if (data == null || data.Length == 0)
        {
            loaded = true;
            return;
        }

        try
        {
            using (MemoryStream stream = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int version = reader.ReadInt32();
                if (version != CraftingTreeFileVersion)
                {
                    Debug.LogWarning($"CraftingTreeRuntime: Unsupported version {version}.");
                    loaded = true;
                    return;
                }

                int itemCount = reader.ReadInt32();
                for (int i = 0; i < itemCount; i++)
                {
                    int itemId = reader.ReadInt32();
                    reader.ReadString(); // map object GUID

                    int ingredientCount = reader.ReadInt32();
                    List<IngredientEntry> ingredientList = null;
                    for (int j = 0; j < ingredientCount; j++)
                    {
                        int ingredientId = reader.ReadInt32();
                        int ingredientCountValue = reader.ReadInt32();

                        if (itemId < 0 || ingredientId < 0)
                        {
                            continue;
                        }

                        AddCraftable(ingredientId, itemId);

                        if (ingredientList == null)
                        {
                            ingredientList = new List<IngredientEntry>(ingredientCount);
                        }

                        ingredientList.Add(new IngredientEntry(ingredientId, Mathf.Max(1, ingredientCountValue)));
                    }

                    if (ingredientList != null && ingredientList.Count > 0)
                    {
                        IngredientsByItem[itemId] = ingredientList;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"CraftingTreeRuntime: Failed to load crafting data. {ex.Message}");
        }

        foreach (List<int> list in CraftableByIngredient.Values)
        {
            list.Sort();
        }

        foreach (List<IngredientEntry> list in IngredientsByItem.Values)
        {
            list.Sort((left, right) => left.itemId.CompareTo(right.itemId));
        }

        loaded = true;
    }

    private static byte[] LoadCraftingTreeBytes()
    {
        TextAsset resource = Resources.Load<TextAsset>("Data/CraftingTree/crafting_tree");
        if (resource != null)
        {
            return resource.bytes;
        }

        string path = Path.Combine(Application.dataPath, "Data", "CraftingTree", "crafting_tree.bytes");
        if (!File.Exists(path))
        {
            return null;
        }

        return File.ReadAllBytes(path);
    }

    private static void AddCraftable(int ingredientId, int itemId)
    {
        if (!CraftableByIngredient.TryGetValue(ingredientId, out List<int> list))
        {
            list = new List<int>();
            CraftableByIngredient.Add(ingredientId, list);
        }

        if (!list.Contains(itemId))
        {
            list.Add(itemId);
        }
    }
}
