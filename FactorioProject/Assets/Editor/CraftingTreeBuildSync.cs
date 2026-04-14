using System.IO;
using UnityEditor;
using UnityEngine;

public static class CraftingTreeBuildSync
{
    [InitializeOnLoadMethod]
    private static void EnsureCraftingTreeInResources()
    {
        string sourcePath = Path.Combine(Application.dataPath, "Data", "CraftingTree", "crafting_tree.bytes");
        if (!File.Exists(sourcePath))
        {
            return;
        }

        string targetPath = Path.Combine(Application.dataPath, "Resources", "Data", "CraftingTree", "crafting_tree.bytes");
        string targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory) && !Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        bool shouldCopy = !File.Exists(targetPath);
        if (!shouldCopy)
        {
            System.DateTime sourceTime = File.GetLastWriteTimeUtc(sourcePath);
            System.DateTime targetTime = File.GetLastWriteTimeUtc(targetPath);
            shouldCopy = sourceTime > targetTime;
        }

        if (!shouldCopy)
        {
            return;
        }

        File.Copy(sourcePath, targetPath, true);
        AssetDatabase.Refresh();
    }
}
