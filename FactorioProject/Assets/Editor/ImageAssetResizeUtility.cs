using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    internal static class ImageAssetResizeUtility
    {
        private const int TargetSize = 256;
        private const int JpegQuality = 95;
        private const string MenuPath = "Assets/ProjectF/이미지를 256x256으로 변경 (덮어쓰기)";

        private static readonly HashSet<string> SupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png",
                ".jpg",
                ".jpeg",
                ".tga"
            };

        [MenuItem(MenuPath, false, 2001)]
        private static void ResizeSelectedImages()
        {
            List<string> assetPaths = GetSelectedImageAssetPaths();
            if (assetPaths.Count <= 0)
            {
                return;
            }

            int resizedCount = 0;
            var failures = new List<string>();
            try
            {
                for (int i = 0; i < assetPaths.Count; i++)
                {
                    string assetPath = assetPaths[i];
                    EditorUtility.DisplayProgressBar(
                        "이미지를 256x256으로 변경",
                        assetPath,
                        (float)i / assetPaths.Count);

                    if (TryResizeAndOverwrite(assetPath, out string error))
                    {
                        resizedCount++;
                    }
                    else
                    {
                        failures.Add($"{assetPath}: {error}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (failures.Count > 0)
            {
                Debug.LogError(
                    $"Image resize completed with failures. Resized: {resizedCount}, Failed: {failures.Count}\n"
                    + string.Join("\n", failures));
            }
            else
            {
                Debug.Log($"Resized and overwrote {resizedCount} image file(s) at {TargetSize}x{TargetSize}.");
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateResizeSelectedImages()
        {
            return GetSelectedImageAssetPaths().Count > 0;
        }

        private static List<string> GetSelectedImageAssetPaths()
        {
            var paths = new List<string>();
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] selectedGuids = Selection.assetGUIDs;
            if (selectedGuids == null)
            {
                return paths;
            }

            for (int i = 0; i < selectedGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(selectedGuids[i]);
                if (string.IsNullOrWhiteSpace(assetPath)
                    || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    || AssetDatabase.IsValidFolder(assetPath)
                    || !SupportedExtensions.Contains(Path.GetExtension(assetPath))
                    || !uniquePaths.Add(assetPath))
                {
                    continue;
                }

                paths.Add(assetPath);
            }

            return paths;
        }

        private static bool TryResizeAndOverwrite(string assetPath, out string error)
        {
            error = null;
            string absolutePath = ToAbsolutePath(assetPath);
            if (!File.Exists(absolutePath))
            {
                error = "파일을 찾을 수 없음";
                return false;
            }

            string extension = Path.GetExtension(assetPath).ToLowerInvariant();
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            bool linear = importer != null && !importer.sRGBTexture;
            Texture2D decodedTexture = null;
            Texture2D resizedTexture = null;
            Texture2D sourceTexture = null;
            FilterMode previousFilterMode = FilterMode.Bilinear;
            bool restoreSourceFilterMode = false;

            try
            {
                decodedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                byte[] sourceBytes = File.ReadAllBytes(absolutePath);
                if (sourceBytes.Length > 0
                    && ImageConversion.LoadImage(decodedTexture, sourceBytes, false))
                {
                    sourceTexture = decodedTexture;
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(decodedTexture);
                    decodedTexture = null;
                    sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                }

                if (sourceTexture == null || sourceTexture.width <= 0 || sourceTexture.height <= 0)
                {
                    error = "이미지를 디코딩할 수 없음";
                    return false;
                }

                previousFilterMode = sourceTexture.filterMode;
                sourceTexture.filterMode = FilterMode.Bilinear;
                restoreSourceFilterMode = sourceTexture != decodedTexture;
                resizedTexture = CreateResizedTexture(sourceTexture, linear);
                if (restoreSourceFilterMode)
                {
                    sourceTexture.filterMode = previousFilterMode;
                    restoreSourceFilterMode = false;
                }

                if (resizedTexture == null)
                {
                    error = "이미지 크기 변경 실패";
                    return false;
                }

                byte[] outputBytes = EncodeTexture(resizedTexture, extension);
                if (outputBytes == null || outputBytes.Length <= 0)
                {
                    error = "이미지 인코딩 실패";
                    return false;
                }

                File.WriteAllBytes(absolutePath, outputBytes);
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (restoreSourceFilterMode && sourceTexture != null)
                {
                    sourceTexture.filterMode = previousFilterMode;
                }

                if (resizedTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(resizedTexture);
                }

                if (decodedTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(decodedTexture);
                }
            }
        }

        private static Texture2D CreateResizedTexture(Texture2D source, bool linear)
        {
            RenderTextureReadWrite readWrite = linear
                ? RenderTextureReadWrite.Linear
                : RenderTextureReadWrite.sRGB;
            RenderTexture renderTexture = RenderTexture.GetTemporary(
                TargetSize,
                TargetSize,
                0,
                RenderTextureFormat.ARGB32,
                readWrite);
            RenderTexture previousActive = RenderTexture.active;
            Texture2D result = null;

            try
            {
                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;
                result = new Texture2D(
                    TargetSize,
                    TargetSize,
                    TextureFormat.RGBA32,
                    false,
                    linear)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                result.ReadPixels(new Rect(0f, 0f, TargetSize, TargetSize), 0, 0);
                result.Apply(false, false);
                return result;
            }
            catch
            {
                if (result != null)
                {
                    UnityEngine.Object.DestroyImmediate(result);
                }

                return null;
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static byte[] EncodeTexture(Texture2D texture, string extension)
        {
            return extension switch
            {
                ".png" => texture.EncodeToPNG(),
                ".jpg" => texture.EncodeToJPG(JpegQuality),
                ".jpeg" => texture.EncodeToJPG(JpegQuality),
                ".tga" => texture.EncodeToTGA(),
                _ => null
            };
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
