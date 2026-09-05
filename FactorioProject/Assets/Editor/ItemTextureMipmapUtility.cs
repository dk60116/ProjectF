using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    // Editor-only bulk operation. ItemDefinition remains the source of item prefab/material references.
    internal static class ItemTextureMipmapUtility
    {
        internal sealed class Result
        {
            internal int Materials, Textures, Changed, Unchanged, Skipped, Failed;
            internal bool Cancelled;
            internal readonly StringBuilder Details = new StringBuilder();
            internal string Summary => $"SetMipmap: 변경 {Changed}, 유지 {Unchanged}, 제외 {Skipped}, 실패 {Failed}"
                + (Cancelled ? " (취소됨)" : string.Empty);
        }

        private sealed class Candidate
        {
            internal string Path;
            internal bool IsData;
        }

        internal static Result ApplyAll(IReadOnlyList<ItemDefinition> definitions)
        {
            var result = new Result();
            var textures = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
            var materials = new HashSet<Material>();
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("SetMipmap은 편집 모드에서만 실행할 수 있습니다.");

                // Include non-default corner/end/2F variants, even when no ItemDefinition points directly at them.
                if (AssetDatabase.IsValidFolder("Assets/MapObject"))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MapObject" }))
                        roots.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
                for (int i = 0; definitions != null && i < definitions.Count; i++)
                {
                    ItemDefinition definition = definitions[i];
                    if (definition == null)
                        continue;
                    CollectMaterial(definition.portableMat, materials, textures);
                    if (definition.mapObject == null)
                        continue;
                    CollectPrefab(definition.mapObject.gameObject, materials, textures);
                    string path = AssetDatabase.GetAssetPath(definition.mapObject);
                    if (!string.IsNullOrEmpty(path))
                        roots.Add(path);
                }

                string[] rootPaths = new string[roots.Count];
                roots.CopyTo(rootPaths);
                string[] dependencies = rootPaths.Length > 0
                    ? AssetDatabase.GetDependencies(rootPaths, true) : Array.Empty<string>();
                Array.Sort(dependencies, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < dependencies.Length; i++)
                {
                    string path = dependencies[i];
                    if (EditorUtility.DisplayCancelableProgressBar("SetMipmap",
                        "텍스처 수집: " + path, 0.4f * i / Mathf.Max(1, dependencies.Length)))
                    {
                        result.Cancelled = true;
                        return result;
                    }
                    string extension = Path.GetExtension(path);
                    if (string.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase))
                        CollectMaterial(AssetDatabase.LoadAssetAtPath<Material>(path), materials, textures);
                    else if (string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(extension, ".obj", StringComparison.OrdinalIgnoreCase))
                        CollectPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), materials, textures);
                }

                var ordered = new List<Candidate>(textures.Values);
                ordered.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
                for (int i = 0; i < ordered.Count; i++)
                {
                    Candidate candidate = ordered[i];
                    if (EditorUtility.DisplayCancelableProgressBar("SetMipmap",
                        "텍스처 적용: " + candidate.Path, 0.4f + 0.6f * i / Mathf.Max(1, ordered.Count)))
                    {
                        result.Cancelled = true;
                        break;
                    }
                    if (candidate.IsData)
                    {
                        Skip(result, candidate.Path, "PathUV/RemapUV 좌표 데이터");
                        continue;
                    }
                    TextureImporter importer = AssetImporter.GetAtPath(candidate.Path) as TextureImporter;
                    if (importer == null || !candidate.Path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        Skip(result, candidate.Path, "수정 가능한 프로젝트 TextureImporter 없음");
                        continue;
                    }
                    if (importer.mipmapEnabled && importer.filterMode == FilterMode.Trilinear)
                    {
                        result.Unchanged++;
                        continue;
                    }
                    bool previousMipmaps = importer.mipmapEnabled;
                    FilterMode previousFilter = importer.filterMode;
                    try
                    {
                        importer.mipmapEnabled = true;
                        importer.filterMode = FilterMode.Trilinear;
                        importer.SaveAndReimport();
                        result.Changed++;
                    }
                    catch (Exception exception)
                    {
                        result.Failed++;
                        result.Details.AppendLine($"실패: {candidate.Path} — {exception.Message}");
                        // Failed imports must not look complete on the next button click.
                        importer.mipmapEnabled = previousMipmaps;
                        importer.filterMode = previousFilter;
                        try
                        {
                            AssetDatabase.WriteImportSettingsIfDirty(candidate.Path);
                        }
                        catch (Exception restoreException)
                        {
                            result.Details.AppendLine($"설정 복원 실패: {candidate.Path} — {restoreException.Message}");
                        }
                    }
                }
                return result;
            }
            finally
            {
                result.Materials = materials.Count;
                result.Textures = textures.Count;
                EditorUtility.ClearProgressBar();
            }
        }

        private static void CollectPrefab(GameObject prefab, HashSet<Material> materials,
            Dictionary<string, Candidate> textures)
        {
            if (prefab == null)
                return;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                    CollectMaterial(material, materials, textures);
                if (renderer is SpriteRenderer spriteRenderer && spriteRenderer.sprite != null)
                    CollectTexture(spriteRenderer.sprite.texture, string.Empty, textures);
            }
        }

        private static void CollectMaterial(Material material, HashSet<Material> materials,
            Dictionary<string, Candidate> textures)
        {
            if (material == null || !materials.Add(material))
                return;
            foreach (string property in material.GetTexturePropertyNames())
                CollectTexture(material.GetTexture(property), property, textures);
        }

        private static void CollectTexture(Texture texture, string property,
            Dictionary<string, Candidate> textures)
        {
            if (texture == null)
                return;
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
                return; // Built-in/default textures have no editable source asset.
            if (!textures.TryGetValue(path, out Candidate candidate))
            {
                candidate = new Candidate { Path = path };
                textures.Add(path, candidate);
            }
            // Shared use in even one data slot protects the source texture, regardless of traversal order.
            candidate.IsData |= IsCoordinateDataTexture(path, property);
        }

        internal static bool IsCoordinateDataTexture(string path, string property)
        {
            string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            return name.IndexOf("PathUV", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("RemapUV", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(property, "_PathUvMap", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property, "_RemapUvMap", StringComparison.OrdinalIgnoreCase);
        }

        private static void Skip(Result result, string path, string reason)
        {
            result.Skipped++;
            result.Details.AppendLine($"제외: {path} — {reason}");
        }
    }
}
