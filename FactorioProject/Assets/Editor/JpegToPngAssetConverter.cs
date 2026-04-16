using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class JpegToPngAssetConverter
{
    private const string MenuPath = "Assets/Convert To PNG";
    private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg" };

    [MenuItem(MenuPath, false, 2000)]
    private static void ConvertSelectedAssetsToPng()
    {
        string[] selectedAssetPaths = GetSelectedJpegAssetPaths();
        if (selectedAssetPaths.Length <= 0)
        {
            return;
        }

        List<string> createdAssetPaths = new List<string>();
        List<string> skippedAssetPaths = new List<string>();

        try
        {
            AssetDatabase.StartAssetEditing();

            for (int i = 0; i < selectedAssetPaths.Length; i++)
            {
                string assetPath = selectedAssetPaths[i];
                if (!TryConvertAsset(assetPath, out string createdAssetPath))
                {
                    skippedAssetPaths.Add(assetPath);
                    continue;
                }

                createdAssetPaths.Add(createdAssetPath);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();

        if (createdAssetPaths.Count > 0)
        {
            Selection.objects = LoadAssets(createdAssetPaths);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        if (createdAssetPaths.Count <= 0)
        {
            EditorUtility.DisplayDialog(
                "Convert To PNG",
                "No PNG files were created.",
                "OK");
            return;
        }

        string message = $"Created {createdAssetPaths.Count} PNG file(s).";
        if (skippedAssetPaths.Count > 0)
        {
            message += $"\nSkipped {skippedAssetPaths.Count} file(s).";
        }

        EditorUtility.DisplayDialog(
            "Convert To PNG",
            message,
            "OK");
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateConvertSelectedAssetsToPng()
    {
        return GetSelectedJpegAssetPaths().Length > 0;
    }

    private static string[] GetSelectedJpegAssetPaths()
    {
        string[] selectedAssetPaths = Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0
            ? Array.ConvertAll(Selection.assetGUIDs, AssetDatabase.GUIDToAssetPath)
            : Array.Empty<string>();

        List<string> jpegAssetPaths = new List<string>();
        for (int i = 0; i < selectedAssetPaths.Length; i++)
        {
            string assetPath = selectedAssetPaths[i];
            if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                continue;
            }

            if (!IsSupportedJpegPath(assetPath))
            {
                continue;
            }

            jpegAssetPaths.Add(assetPath);
        }

        return jpegAssetPaths.ToArray();
    }

    private static bool TryConvertAsset(string sourceAssetPath, out string createdAssetPath)
    {
        createdAssetPath = null;

        string fullSourcePath = ToFullPath(sourceAssetPath);
        if (!File.Exists(fullSourcePath))
        {
            Debug.LogWarning($"JpegToPngAssetConverter: Source file not found: {sourceAssetPath}");
            return false;
        }

        byte[] sourceBytes = File.ReadAllBytes(fullSourcePath);
        if (sourceBytes == null || sourceBytes.Length <= 0)
        {
            Debug.LogWarning($"JpegToPngAssetConverter: Source file is empty: {sourceAssetPath}");
            return false;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, sourceBytes, false))
            {
                Debug.LogWarning($"JpegToPngAssetConverter: Failed to decode image: {sourceAssetPath}");
                return false;
            }

            byte[] pngBytes = texture.EncodeToPNG();
            if (pngBytes == null || pngBytes.Length <= 0)
            {
                Debug.LogWarning($"JpegToPngAssetConverter: Failed to encode PNG: {sourceAssetPath}");
                return false;
            }

            createdAssetPath = GetUniquePngAssetPath(sourceAssetPath);
            string fullTargetPath = ToFullPath(createdAssetPath);
            File.WriteAllBytes(fullTargetPath, pngBytes);
            AssetDatabase.ImportAsset(createdAssetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static string GetUniquePngAssetPath(string sourceAssetPath)
    {
        string directory = Path.GetDirectoryName(sourceAssetPath)?.Replace("\\", "/") ?? "Assets";
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourceAssetPath);
        string candidatePath = $"{directory}/{fileNameWithoutExtension}.png";
        int suffix = 1;

        while (File.Exists(ToFullPath(candidatePath)))
        {
            candidatePath = $"{directory}/{fileNameWithoutExtension}_{suffix}.png";
            suffix++;
        }

        return candidatePath;
    }

    private static bool IsSupportedJpegPath(string assetPath)
    {
        string extension = Path.GetExtension(assetPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        string normalizedExtension = extension.ToLowerInvariant();
        for (int i = 0; i < SupportedExtensions.Length; i++)
        {
            if (normalizedExtension == SupportedExtensions[i])
            {
                return true;
            }
        }

        return false;
    }

    private static UnityEngine.Object[] LoadAssets(List<string> assetPaths)
    {
        List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
        for (int i = 0; i < assetPaths.Count; i++)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPaths[i]);
            if (asset != null)
            {
                assets.Add(asset);
            }
        }

        return assets.ToArray();
    }

    private static string ToFullPath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }
}
