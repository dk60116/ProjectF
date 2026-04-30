using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
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

public sealed class EditorToolBuildSync : IPostprocessBuildWithReport
{
    public int callbackOrder => 100;

    public void OnPostprocessBuild(BuildReport report)
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string toolProjectPath = Path.Combine(repositoryRoot, "Tools", "ItemGiveTool", "ItemGiveTool.csproj");
        if (!File.Exists(toolProjectPath))
        {
            Debug.LogWarning($"EditorToolBuildSync: tool project not found at '{toolProjectPath}'.");
            return;
        }

        string buildOutputPath = report.summary.outputPath;
        string buildDirectory = Directory.Exists(buildOutputPath)
            ? buildOutputPath
            : Path.GetDirectoryName(buildOutputPath);
        if (string.IsNullOrWhiteSpace(buildDirectory))
        {
            Debug.LogWarning("EditorToolBuildSync: build output directory could not be resolved.");
            return;
        }

        string publishDirectory = Path.Combine(buildDirectory, "Tools", "EditorTool");
        Directory.CreateDirectory(publishDirectory);

        System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{toolProjectPath}\" -c Release -o \"{publishDirectory}\" --nologo",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repositoryRoot
        };

        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Debug.LogWarning($"EditorToolBuildSync: dotnet publish failed.\n{output}\n{error}");
                return;
            }

            ExportItemCatalog(publishDirectory);
            Debug.Log($"EditorToolBuildSync: published EditorTool to '{publishDirectory}'.");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"EditorToolBuildSync: failed to publish EditorTool. {exception.Message}");
        }
    }

    private static void ExportItemCatalog(string publishDirectory)
    {
        string dataDirectory = Path.Combine(publishDirectory, "Data");
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, true);
        }

        string iconDirectory = Path.Combine(dataDirectory, "icons");
        Directory.CreateDirectory(iconDirectory);

        ItemGiveToolCatalog catalog = new ItemGiveToolCatalog();
        string[] definitionGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Data/Items" });
        for (int i = 0; i < definitionGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
            if (definition == null || definition.id < 0)
            {
                continue;
            }

            string displayName = string.IsNullOrWhiteSpace(definition.itemName)
                ? definition.name
                : definition.itemName.Trim();
            ItemGiveToolCatalogEntry entry = new ItemGiveToolCatalogEntry
            {
                id = definition.id,
                name = displayName
            };

            if (definition.icon != null)
            {
                string iconFileName = $"item_{definition.id}_{SanitizeFileName(displayName)}.png";
                string iconPath = Path.Combine(iconDirectory, iconFileName);
                if (TryExportSpritePng(definition.icon, iconPath))
                {
                    entry.icon = $"icons/{iconFileName}";
                }
            }

            catalog.items.Add(entry);
        }

        catalog.items.Sort((left, right) => left.id.CompareTo(right.id));
        string catalogPath = Path.Combine(dataDirectory, "item_catalog.json");
        File.WriteAllText(catalogPath, JsonUtility.ToJson(catalog, true), new UTF8Encoding(false));
    }

    private static bool TryExportSpritePng(Sprite sprite, string targetPath)
    {
        if (sprite == null || sprite.texture == null)
        {
            return false;
        }

        Rect textureRect = sprite.textureRect;
        int width = Mathf.Max(1, Mathf.RoundToInt(textureRect.width));
        int height = Mathf.Max(1, Mathf.RoundToInt(textureRect.height));
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previousRenderTexture = RenderTexture.active;
        Texture2D readableTexture = null;
        bool matrixPushed = false;

        try
        {
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.clear);
            GL.PushMatrix();
            matrixPushed = true;
            GL.LoadPixelMatrix(0f, width, height, 0f);

            Rect sourceRect = new Rect(
                textureRect.x / sprite.texture.width,
                textureRect.y / sprite.texture.height,
                textureRect.width / sprite.texture.width,
                textureRect.height / sprite.texture.height);
            Graphics.DrawTexture(new Rect(0f, 0f, width, height), sprite.texture, sourceRect, 0, 0, 0, 0);

            readableTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            readableTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            readableTexture.Apply();

            File.WriteAllBytes(targetPath, readableTexture.EncodeToPNG());
            return true;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"EditorToolBuildSync: failed to export icon '{sprite.name}'. {exception.Message}");
            return false;
        }
        finally
        {
            if (matrixPushed)
            {
                GL.PopMatrix();
            }

            if (readableTexture != null)
            {
                Object.DestroyImmediate(readableTexture);
            }

            RenderTexture.active = previousRenderTexture;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "item";
        }

        string result = value.Trim();
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalidCharacter, '_');
        }

        return result.Replace(' ', '_');
    }

    [System.Serializable]
    private sealed class ItemGiveToolCatalog
    {
        public List<ItemGiveToolCatalogEntry> items = new List<ItemGiveToolCatalogEntry>();
    }

    [System.Serializable]
    private sealed class ItemGiveToolCatalogEntry
    {
        public int id;
        public string name;
        public string icon;
    }
}
