using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    internal static class BookItemAssetGenerator
    {
        private const string BookIconTemplatePath = "Assets/Items/Book/Book/Book_Icon.png";
        private const string BookPortableTextureTemplatePath = "Assets/Items/Book/Book/Book_P_TB.png";
        private const string BookPortableMaterialTemplatePath = "Assets/Items/Book/Book/Book_P.mat";
        private const string BookPortableMeshPath = "Assets/Items/Book/Book_P.mesh";
        private const string PaperIconTemplatePath = PaperPortableMeshGenerator.PaperItemFolder + "/Paper/Paper_Icon.png";
        private const string BookItemFolder = "Assets/Items/Book";
        private const string CdItemFolder = "Assets/Items/CD";
        private const string CdTemplateFolder = CdItemFolder + "/CD";
        private const string CdIconTemplatePath = CdTemplateFolder + "/CD_Icon.png";
        private const string CdPortableTextureTemplatePath = CdTemplateFolder + "/CD_P_TB.png";
        internal const string CdPortableMeshPath = CdTemplateFolder + "/CD_P.asset";
        private const float IconEmblemScaleMultiplier = 1.15f;
        private const float UiIconEmblemCenterX = 0.5078125f;
        private const float UiIconEmblemCenterY = 0.40625f;
        private const float UiIconEmblemSizeRatio = 0.253f * IconEmblemScaleMultiplier;
        private const float BookIconEmblemRotationDegrees = -17.5f;
        private const float PaperIconEmblemRotationDegrees = 20f;
        private const int GeneratedIconMaxTextureSize = 128;
        private const float PortableEmblemSizeRatio = 0.19f;
        private const float PortableFrontFaceCenterY = 0.1675f;
        private const int BackgroundTransparentThreshold = 4;
        private const int BackgroundOpaqueThreshold = 36;
        private const string BookSourceIconGuidUserDataPrefix = "ProjectF.BookSourceIconGuid=";
        private const string PaperSourceIconGuidUserDataPrefix = "ProjectF.PaperSourceIconGuid=";
        private const string CdSourceIconGuidUserDataPrefix = "ProjectF.CdSourceIconGuid=";
        private const string TargetItemNamePrefix = "Manual - ";
        private const string LegacyBookItemNamePrefix = "Book - ";
        private const string LegacyPaperItemNamePrefix = "Paper - ";
        private const string LegacyNoteItemNamePrefix = "Note - ";

        private static readonly string[] DocumentItemNamePrefixes =
        {
            TargetItemNamePrefix,
            LegacyBookItemNamePrefix,
            LegacyPaperItemNamePrefix,
            LegacyNoteItemNamePrefix
        };

        private sealed class GenerationProfile
        {
            internal string DisplayName;
            internal string IconTemplatePath;
            internal string PortableMeshPath;
            internal string OutputFolder;
            internal string SourceIconGuidUserDataPrefix;
            internal float IconEmblemCenterX;
            internal float IconEmblemCenterY;
            internal float IconEmblemSizeRatio;
            internal float IconEmblemRotationDegrees;
            internal float PortableEmblemSizeRatio;
            internal bool UsesPaperSurface;
            internal bool UsesCdSurface;
        }

        private static readonly GenerationProfile BookProfile = new GenerationProfile
        {
            DisplayName = "Book",
            IconTemplatePath = BookIconTemplatePath,
            PortableMeshPath = BookPortableMeshPath,
            OutputFolder = BookItemFolder,
            SourceIconGuidUserDataPrefix = BookSourceIconGuidUserDataPrefix,
            IconEmblemCenterX = UiIconEmblemCenterX,
            IconEmblemCenterY = UiIconEmblemCenterY,
            IconEmblemSizeRatio = UiIconEmblemSizeRatio,
            IconEmblemRotationDegrees = BookIconEmblemRotationDegrees,
            PortableEmblemSizeRatio = PortableEmblemSizeRatio,
            UsesPaperSurface = false
        };

        private static readonly GenerationProfile PaperProfile = new GenerationProfile
        {
            DisplayName = "Paper",
            IconTemplatePath = PaperIconTemplatePath,
            PortableMeshPath = PaperPortableMeshGenerator.PaperPortableMeshPath,
            OutputFolder = PaperPortableMeshGenerator.PaperItemFolder,
            SourceIconGuidUserDataPrefix = PaperSourceIconGuidUserDataPrefix,
            IconEmblemCenterX = 0.5078125f,
            IconEmblemCenterY = 0.515625f,
            IconEmblemSizeRatio = 0.299f * IconEmblemScaleMultiplier,
            IconEmblemRotationDegrees = PaperIconEmblemRotationDegrees,
            PortableEmblemSizeRatio = 0.18f,
            UsesPaperSurface = true
        };

        private static readonly GenerationProfile CdProfile = new GenerationProfile
        {
            DisplayName = "CD",
            IconTemplatePath = CdIconTemplatePath,
            PortableMeshPath = CdPortableMeshPath,
            OutputFolder = CdItemFolder,
            SourceIconGuidUserDataPrefix = CdSourceIconGuidUserDataPrefix,
            IconEmblemCenterX = 0.5f,
            IconEmblemCenterY = 0.5f,
            IconEmblemSizeRatio = 0.45f,
            IconEmblemRotationDegrees = 0f,
            PortableEmblemSizeRatio = 0f,
            UsesCdSurface = true
        };

        internal sealed class Result
        {
            internal ItemDefinition SourceDefinition;
            internal ItemDefinition TargetDefinition;
            internal Sprite Icon;
            internal Texture2D PortableTexture;
            internal Material PortableMaterial;
            internal string TargetItemName;
            internal string IconPath;
            internal string PortableTexturePath;
            internal string PortableMaterialPath;
        }

        private sealed class PixelBuffer
        {
            internal readonly int Width;
            internal readonly int Height;
            internal readonly Color32[] Pixels;

            internal PixelBuffer(int width, int height)
            {
                Width = width;
                Height = height;
                Pixels = new Color32[width * height];
            }

            internal PixelBuffer(int width, int height, Color32[] pixels)
            {
                Width = width;
                Height = height;
                Pixels = pixels;
            }

            internal Color32 GetPixel(int x, int y)
            {
                return Pixels[y * Width + x];
            }

            internal void SetPixel(int x, int y, Color32 color)
            {
                Pixels[y * Width + x] = color;
            }
        }

        internal static bool TryCreate(
            ItemDefinition selectedDefinition,
            IReadOnlyList<ItemDefinition> definitions,
            out Result result,
            out string errorMessage)
        {
            return TryCreate(
                selectedDefinition,
                definitions,
                BookProfile,
                out result,
                out errorMessage);
        }

        internal static bool TryCreatePaper(
            ItemDefinition selectedDefinition,
            IReadOnlyList<ItemDefinition> definitions,
            out Result result,
            out string errorMessage)
        {
            return TryCreate(
                selectedDefinition,
                definitions,
                PaperProfile,
                out result,
                out errorMessage);
        }

        internal static bool TryCreateCd(
            ItemDefinition selectedDefinition,
            IReadOnlyList<ItemDefinition> definitions,
            out Result result,
            out string errorMessage)
        {
            return TryCreate(
                selectedDefinition,
                definitions,
                CdProfile,
                out result,
                out errorMessage);
        }

        [MenuItem("Tools/ProjectF/Items/Regenerate All Manual Icons")]
        public static void RegenerateAllManualIcons()
        {
            List<ItemDefinition> definitions = LoadAllItemDefinitions();
            List<ItemDefinition> targets = new List<ItemDefinition>();
            for (int i = 0; i < definitions.Count; i++)
            {
                if (TryResolveStoredGenerationProfile(definitions[i], out _))
                {
                    targets.Add(definitions[i]);
                }
            }

            int regeneratedCount = 0;
            List<string> failures = new List<string>();
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    ItemDefinition target = targets[i];
                    if (!TryResolveStoredGenerationProfile(target, out GenerationProfile profile))
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        "Regenerate Manual Icons",
                        GetItemName(target),
                        targets.Count > 0 ? (float)i / targets.Count : 1f);

                    if (profile.UsesPaperSurface)
                    {
                        PaperPortableMeshGenerator.EnsureAssetAndBindings();
                    }

                    if (TryCreate(target, definitions, profile, out _, out string errorMessage))
                    {
                        regeneratedCount++;
                    }
                    else
                    {
                        failures.Add($"{GetItemName(target)}: {errorMessage}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            if (failures.Count > 0)
            {
                Debug.LogError(
                    $"Manual 아이콘 일괄 갱신: {regeneratedCount}개 성공, {failures.Count}개 실패\n"
                    + string.Join("\n", failures));
                return;
            }

            Debug.Log($"Manual 아이콘 {regeneratedCount}개를 다시 생성했습니다.");
        }

        private static List<ItemDefinition> LoadAllItemDefinitions()
        {
            string[] definitionGuids = AssetDatabase.FindAssets(
                "t:ItemDefinition",
                new[] { "Assets/Data/Items" });
            List<ItemDefinition> definitions = new List<ItemDefinition>(definitionGuids.Length);
            for (int i = 0; i < definitionGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
                if (definition != null)
                {
                    definitions.Add(definition);
                }
            }

            definitions.Sort((left, right) => string.CompareOrdinal(
                AssetDatabase.GetAssetPath(left),
                AssetDatabase.GetAssetPath(right)));
            return definitions;
        }

        private static bool TryResolveStoredGenerationProfile(
            ItemDefinition definition,
            out GenerationProfile profile)
        {
            profile = null;
            if (definition == null || definition.icon == null)
            {
                return false;
            }

            string iconPath = AssetDatabase.GetAssetPath(definition.icon);
            TextureImporter importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
            string userData = importer != null ? importer.userData : string.Empty;
            if (userData.StartsWith(BookSourceIconGuidUserDataPrefix, StringComparison.Ordinal))
            {
                profile = BookProfile;
                return true;
            }

            if (userData.StartsWith(PaperSourceIconGuidUserDataPrefix, StringComparison.Ordinal))
            {
                profile = PaperProfile;
                return true;
            }

            if (userData.StartsWith(CdSourceIconGuidUserDataPrefix, StringComparison.Ordinal))
            {
                profile = CdProfile;
                return true;
            }

            return false;
        }

        private static bool TryCreate(
            ItemDefinition selectedDefinition,
            IReadOnlyList<ItemDefinition> definitions,
            GenerationProfile profile,
            out Result result,
            out string errorMessage)
        {
            result = null;
            errorMessage = string.Empty;
            if (selectedDefinition == null || profile == null)
            {
                errorMessage = "문서 에셋을 생성할 아이템을 선택하세요.";
                return false;
            }

            if (!TryResolveGenerationContext(
                    selectedDefinition,
                    definitions,
                    profile,
                    out ItemDefinition sourceDefinition,
                    out ItemDefinition targetDefinition,
                    out Sprite sourceIcon,
                    out string targetItemName,
                    out errorMessage))
            {
                return false;
            }

            if (targetDefinition == sourceDefinition)
            {
                errorMessage = $"원본 ItemDefinition은 {profile.DisplayName} 생성 대상으로 사용할 수 없습니다.";
                return false;
            }

            string sourceIconPath = AssetDatabase.GetAssetPath(sourceIcon);
            if (!IsPngAssetPath(sourceIconPath))
            {
                errorMessage = $"아이템 '{GetItemName(sourceDefinition)}'의 원본 PNG 아이콘을 찾을 수 없습니다.";
                return false;
            }

            string sourceIconGuid = AssetDatabase.AssetPathToGUID(sourceIconPath);

            string portableTextureTemplatePath = profile.UsesCdSurface
                ? CdPortableTextureTemplatePath
                : BookPortableTextureTemplatePath;
            if (!AssetExists(profile.IconTemplatePath)
                || !AssetExists(portableTextureTemplatePath))
            {
                errorMessage = $"{profile.DisplayName} 아이콘 또는 P 텍스처 템플릿을 찾을 수 없습니다.";
                return false;
            }

            Texture2D sourceIconTexture = null;
            Texture2D documentIconTemplate = null;
            Texture2D portableTextureTemplate = null;
            try
            {
                sourceIconTexture = LoadPng(sourceIconPath);
                documentIconTemplate = LoadPng(profile.IconTemplatePath);
                portableTextureTemplate = LoadPng(portableTextureTemplatePath);
                if (sourceIconTexture == null || documentIconTemplate == null || portableTextureTemplate == null)
                {
                    errorMessage = $"{profile.DisplayName} 에셋 생성에 필요한 PNG를 읽지 못했습니다.";
                    return false;
                }

                PixelBuffer emblem = ExtractEmblem(ToTopLeftBuffer(sourceIconTexture));
                if (emblem == null)
                {
                    errorMessage = $"원본 아이콘 '{sourceIconPath}'에서 문양 영역을 찾지 못했습니다.";
                    return false;
                }

                string outputFolder = ResolveOutputFolder(profile.OutputFolder, targetItemName);
                EnsureAssetFolder(outputFolder);

                string safeItemName = SanitizeFileName(targetItemName);
                string iconPath = $"{outputFolder}/{safeItemName}_Icon.png";
                string portableTexturePath = $"{outputFolder}/{safeItemName}_P_TB.png";
                string portableMaterialPath = $"{outputFolder}/{safeItemName}_P.mat";

                PixelBuffer icon = ToTopLeftBuffer(documentIconTemplate);
                CompositeCentered(
                    icon,
                    emblem,
                    icon.Width * profile.IconEmblemCenterX,
                    icon.Height * profile.IconEmblemCenterY,
                    icon.Width * profile.IconEmblemSizeRatio,
                    profile.IconEmblemRotationDegrees);

                PixelBuffer portableTexture;
                if (profile.UsesCdSurface)
                {
                    portableTexture = ToTopLeftBuffer(portableTextureTemplate);
                    CompositeCentered(
                        portableTexture,
                        emblem,
                        portableTexture.Width * profile.IconEmblemCenterX,
                        portableTexture.Height * profile.IconEmblemCenterY,
                        portableTexture.Width * profile.IconEmblemSizeRatio,
                        profile.IconEmblemRotationDegrees);
                }
                else
                {
                    portableTexture = profile.UsesPaperSurface
                        ? BuildPaperPortableTexture(
                            ToTopLeftBuffer(documentIconTemplate),
                            portableTextureTemplate.width,
                            portableTextureTemplate.height)
                        : BuildPortableTexture(ToTopLeftBuffer(portableTextureTemplate));
                    PixelBuffer rotatedPortableEmblem = RotateCounterClockwise(emblem);
                    CompositeCentered(
                        portableTexture,
                        rotatedPortableEmblem,
                        portableTexture.Width * 0.25f,
                        portableTexture.Height * PortableFrontFaceCenterY,
                        portableTexture.Width * profile.PortableEmblemSizeRatio);
                }

                WritePng(iconPath, icon, true);
                WritePng(portableTexturePath, portableTexture, false);
                ImportIcon(iconPath, sourceIconGuid, profile.SourceIconGuidUserDataPrefix);
                ImportPortableTexture(portableTexturePath);

                Sprite iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                Texture2D importedPortableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(portableTexturePath);
                if (iconSprite == null || importedPortableTexture == null)
                {
                    errorMessage = "생성된 Book 텍스처를 Unity 에셋으로 불러오지 못했습니다.";
                    return false;
                }

                Material portableMaterial = CreateOrUpdatePortableMaterial(
                    portableMaterialPath,
                    safeItemName,
                    importedPortableTexture);
                Mesh portableMesh = AssetDatabase.LoadAssetAtPath<Mesh>(profile.PortableMeshPath);
                if (portableMaterial == null || portableMesh == null)
                {
                    errorMessage = "Book 휴대 메시 또는 재질을 생성하지 못했습니다.";
                    return false;
                }

                if (targetDefinition != null)
                {
                    Undo.RecordObject(targetDefinition, $"Create {profile.DisplayName} Icon");
                    targetDefinition.itemName = targetItemName;
                    targetDefinition.isManual = true;
                    targetDefinition.manualTargetItem = sourceDefinition;
                    targetDefinition.icon = iconSprite;
                    targetDefinition.portableMesh = portableMesh;
                    targetDefinition.portableMat = portableMaterial;
                    EditorUtility.SetDirty(targetDefinition);
                }

                DeleteLegacyOutputFolders(
                    sourceDefinition,
                    sourceIconGuid,
                    profile.OutputFolder,
                    outputFolder);
                AssetDatabase.SaveAssets();

                result = new Result
                {
                    SourceDefinition = sourceDefinition,
                    TargetDefinition = targetDefinition,
                    Icon = iconSprite,
                    PortableTexture = importedPortableTexture,
                    PortableMaterial = portableMaterial,
                    TargetItemName = targetItemName,
                    IconPath = iconPath,
                    PortableTexturePath = portableTexturePath,
                    PortableMaterialPath = portableMaterialPath
                };
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                errorMessage = $"{profile.DisplayName} 에셋 생성 중 오류가 발생했습니다.\n{exception.Message}";
                return false;
            }
            finally
            {
                DestroyTexture(sourceIconTexture);
                DestroyTexture(documentIconTemplate);
                DestroyTexture(portableTextureTemplate);
            }
        }

        private static bool TryResolveGenerationContext(
            ItemDefinition selectedDefinition,
            IReadOnlyList<ItemDefinition> definitions,
            GenerationProfile profile,
            out ItemDefinition sourceDefinition,
            out ItemDefinition targetDefinition,
            out Sprite sourceIcon,
            out string targetItemName,
            out string errorMessage)
        {
            sourceDefinition = null;
            targetDefinition = null;
            sourceIcon = null;
            targetItemName = string.Empty;
            errorMessage = string.Empty;
            if (selectedDefinition == null)
            {
                errorMessage = $"{profile.DisplayName} 에셋을 생성할 아이템을 선택하세요.";
                return false;
            }

            if (IsDocumentTargetDefinition(selectedDefinition))
            {
                targetDefinition = selectedDefinition;
                if (TryResolveExplicitManualTarget(
                        targetDefinition,
                        out sourceDefinition,
                        out sourceIcon)
                    || TryResolveAnyStoredSourceIcon(
                        targetDefinition.icon,
                        definitions,
                        out sourceDefinition,
                        out sourceIcon)
                    || TryResolveNamedSourceDefinition(
                        targetDefinition,
                        definitions,
                        out sourceDefinition,
                        out sourceIcon)
                    || TryResolveSharedIconSourceDefinition(
                        targetDefinition,
                        definitions,
                        out sourceDefinition,
                        out sourceIcon))
                {
                    targetItemName = BuildTargetItemName(sourceDefinition);
                    return true;
                }

                errorMessage = $"{profile.DisplayName} 아이템 '{GetItemName(targetDefinition)}'에 대응하는 원본 아이템을 찾지 못했습니다.";
                return false;
            }

            sourceDefinition = selectedDefinition;
            sourceIcon = selectedDefinition.icon;
            if (sourceIcon == null)
            {
                errorMessage = "선택한 아이템에 원본으로 사용할 Icon이 설정되어 있지 않습니다.";
                return false;
            }

            targetDefinition = FindTargetDefinition(sourceDefinition, sourceIcon, definitions);
            targetItemName = BuildTargetItemName(sourceDefinition);
            return true;
        }

        private static bool IsDocumentTargetDefinition(ItemDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            string itemName = GetItemName(definition);
            if (definition.icon != null)
            {
                string iconPath = AssetDatabase.GetAssetPath(definition.icon);
                TextureImporter importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
                string userData = importer != null ? importer.userData : string.Empty;
                if (HasGeneratedSourceMarker(userData))
                {
                    return true;
                }
            }

            if (string.Equals(itemName, "Book", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemName, "Paper", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemName, "CD", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (definition.isManual)
            {
                return true;
            }

            string portableMeshPath = definition.portableMesh != null
                ? AssetDatabase.GetAssetPath(definition.portableMesh)
                : string.Empty;
            if (IsDocumentPortableMeshPath(portableMeshPath))
            {
                return true;
            }

            return TryGetNamedSourceItemName(itemName, out _);
        }

        private static bool TryResolveExplicitManualTarget(
            ItemDefinition targetDefinition,
            out ItemDefinition sourceDefinition,
            out Sprite sourceIcon)
        {
            sourceDefinition = targetDefinition != null
                ? targetDefinition.ManualTargetItem
                : null;
            sourceIcon = sourceDefinition != null ? sourceDefinition.icon : null;
            if (sourceDefinition == null
                || sourceDefinition == targetDefinition
                || sourceIcon == null)
            {
                sourceDefinition = null;
                sourceIcon = null;
                return false;
            }

            return true;
        }

        private static bool HasGeneratedSourceMarker(string userData)
        {
            return !string.IsNullOrWhiteSpace(userData)
                && (userData.StartsWith(BookSourceIconGuidUserDataPrefix, StringComparison.Ordinal)
                    || userData.StartsWith(PaperSourceIconGuidUserDataPrefix, StringComparison.Ordinal)
                    || userData.StartsWith(CdSourceIconGuidUserDataPrefix, StringComparison.Ordinal));
        }

        private static bool IsDocumentPortableMeshPath(string assetPath)
        {
            return string.Equals(assetPath, BookPortableMeshPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    assetPath,
                    PaperPortableMeshGenerator.PaperPortableMeshPath,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(assetPath, CdPortableMeshPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveNamedSourceDefinition(
            ItemDefinition targetDefinition,
            IReadOnlyList<ItemDefinition> definitions,
            out ItemDefinition sourceDefinition,
            out Sprite sourceIcon)
        {
            sourceDefinition = null;
            sourceIcon = null;
            if (targetDefinition == null
                || !TryGetNamedSourceItemName(
                    GetItemName(targetDefinition),
                    out string sourceItemName))
            {
                return false;
            }

            for (int i = 0; definitions != null && i < definitions.Count; i++)
            {
                ItemDefinition candidate = definitions[i];
                if (candidate == null || candidate == targetDefinition || candidate.icon == null)
                {
                    continue;
                }

                if (!string.Equals(GetItemName(candidate), sourceItemName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sourceDefinition = candidate;
                sourceIcon = candidate.icon;
                return true;
            }

            return false;
        }

        private static bool TryResolveSharedIconSourceDefinition(
            ItemDefinition targetDefinition,
            IReadOnlyList<ItemDefinition> definitions,
            out ItemDefinition sourceDefinition,
            out Sprite sourceIcon)
        {
            sourceDefinition = null;
            sourceIcon = targetDefinition != null ? targetDefinition.icon : null;
            if (sourceIcon == null)
            {
                return false;
            }

            for (int i = 0; definitions != null && i < definitions.Count; i++)
            {
                ItemDefinition candidate = definitions[i];
                if (candidate == null
                    || candidate == targetDefinition
                    || candidate.icon != sourceIcon
                    || IsDocumentTargetDefinition(candidate))
                {
                    continue;
                }

                sourceDefinition = candidate;
                return true;
            }

            sourceIcon = null;
            return false;
        }

        private static ItemDefinition FindTargetDefinition(
            ItemDefinition sourceDefinition,
            Sprite sourceIcon,
            IReadOnlyList<ItemDefinition> definitions)
        {
            ItemDefinition bestTarget = null;
            int bestScore = int.MinValue;
            string sourceItemName = GetItemName(sourceDefinition);
            for (int i = 0; definitions != null && i < definitions.Count; i++)
            {
                ItemDefinition candidate = definitions[i];
                if (candidate == null
                    || candidate == sourceDefinition
                    || !IsDocumentTargetDefinition(candidate))
                {
                    continue;
                }

                int score = 0;
                if (candidate.ManualTargetItem == sourceDefinition)
                {
                    score += 2000;
                }

                if (TryResolveAnyStoredSourceIcon(
                        candidate.icon,
                        definitions,
                        out _,
                        out Sprite storedSourceIcon)
                    && storedSourceIcon == sourceIcon)
                {
                    score += 1000;
                }

                if (TryGetNamedSourceItemName(
                        GetItemName(candidate),
                        out string namedSourceItemName)
                    && string.Equals(namedSourceItemName, sourceItemName, StringComparison.OrdinalIgnoreCase))
                {
                    score += 500;
                }

                if (candidate.icon == sourceIcon)
                {
                    score += 100;
                }

                if (score > bestScore && score > 0)
                {
                    bestTarget = candidate;
                    bestScore = score;
                }
            }

            return bestTarget;
        }

        private static bool TryGetNamedSourceItemName(
            string itemName,
            out string sourceItemName)
        {
            sourceItemName = string.Empty;
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return false;
            }

            for (int i = 0; i < DocumentItemNamePrefixes.Length; i++)
            {
                string prefix = DocumentItemNamePrefixes[i];
                if (!itemName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sourceItemName = itemName.Substring(prefix.Length).Trim();
                return !string.IsNullOrWhiteSpace(sourceItemName);
            }

            return false;
        }

        private static bool TryResolveAnyStoredSourceIcon(
            Sprite currentIcon,
            IReadOnlyList<ItemDefinition> definitions,
            out ItemDefinition sourceDefinition,
            out Sprite sourceIcon)
        {
            return TryResolveStoredSourceIcon(
                    currentIcon,
                    definitions,
                    BookSourceIconGuidUserDataPrefix,
                    out sourceDefinition,
                    out sourceIcon)
                || TryResolveStoredSourceIcon(
                    currentIcon,
                    definitions,
                    PaperSourceIconGuidUserDataPrefix,
                    out sourceDefinition,
                    out sourceIcon)
                || TryResolveStoredSourceIcon(
                    currentIcon,
                    definitions,
                    CdSourceIconGuidUserDataPrefix,
                    out sourceDefinition,
                    out sourceIcon);
        }

        private static bool TryResolveStoredSourceIcon(
            Sprite currentIcon,
            IReadOnlyList<ItemDefinition> definitions,
            string sourceIconGuidUserDataPrefix,
            out ItemDefinition sourceDefinition,
            out Sprite sourceIcon)
        {
            sourceDefinition = null;
            sourceIcon = null;
            string currentIconPath = AssetDatabase.GetAssetPath(currentIcon);
            TextureImporter importer = AssetImporter.GetAtPath(currentIconPath) as TextureImporter;
            string userData = importer != null ? importer.userData : string.Empty;
            if (string.IsNullOrWhiteSpace(userData)
                || string.IsNullOrWhiteSpace(sourceIconGuidUserDataPrefix)
                || !userData.StartsWith(sourceIconGuidUserDataPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string sourceIconGuid = userData.Substring(sourceIconGuidUserDataPrefix.Length).Trim();
            string sourceIconPath = AssetDatabase.GUIDToAssetPath(sourceIconGuid);
            sourceIcon = AssetDatabase.LoadAssetAtPath<Sprite>(sourceIconPath);
            if (sourceIcon == null || sourceIcon == currentIcon)
            {
                sourceIcon = null;
                return false;
            }

            for (int i = 0; definitions != null && i < definitions.Count; i++)
            {
                ItemDefinition candidate = definitions[i];
                if (candidate != null && candidate.icon == sourceIcon)
                {
                    sourceDefinition = candidate;
                    break;
                }
            }

            return true;
        }

        private static string GetItemName(ItemDefinition definition)
        {
            if (definition == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(definition.itemName)
                ? definition.name
                : definition.itemName.Trim();
        }

        private static string BuildTargetItemName(ItemDefinition sourceDefinition)
        {
            return $"{TargetItemNamePrefix}{GetItemName(sourceDefinition)}";
        }

        private static string ResolveOutputFolder(string rootFolder, string itemName)
        {
            return $"{rootFolder}/{SanitizeFileName(itemName)}";
        }

        private static void DeleteLegacyOutputFolders(
            ItemDefinition sourceDefinition,
            string sourceIconGuid,
            string rootFolder,
            string currentOutputFolder)
        {
            string sourceItemName = GetItemName(sourceDefinition);
            if (string.IsNullOrWhiteSpace(sourceItemName) || string.IsNullOrWhiteSpace(sourceIconGuid))
            {
                return;
            }

            for (int i = 1; i < DocumentItemNamePrefixes.Length; i++)
            {
                string legacyItemName = $"{DocumentItemNamePrefixes[i]}{sourceItemName}";
                string legacyFolder = $"{rootFolder}/{SanitizeFileName(legacyItemName)}";
                if (string.Equals(legacyFolder, currentOutputFolder, StringComparison.OrdinalIgnoreCase)
                    || !AssetDatabase.IsValidFolder(legacyFolder))
                {
                    continue;
                }

                string legacyIconPath = $"{legacyFolder}/{SanitizeFileName(legacyItemName)}_Icon.png";
                TextureImporter importer = AssetImporter.GetAtPath(legacyIconPath) as TextureImporter;
                string userData = importer != null ? importer.userData : string.Empty;
                if (!HasSourceIconGuid(userData, sourceIconGuid))
                {
                    continue;
                }

                AssetDatabase.DeleteAsset(legacyFolder);
            }
        }

        private static bool HasSourceIconGuid(string userData, string sourceIconGuid)
        {
            return string.Equals(
                    userData,
                    $"{BookSourceIconGuidUserDataPrefix}{sourceIconGuid}",
                    StringComparison.Ordinal)
                || string.Equals(
                    userData,
                    $"{PaperSourceIconGuidUserDataPrefix}{sourceIconGuid}",
                    StringComparison.Ordinal)
                || string.Equals(
                    userData,
                    $"{CdSourceIconGuidUserDataPrefix}{sourceIconGuid}",
                    StringComparison.Ordinal);
        }

        private static PixelBuffer BuildPortableTexture(PixelBuffer template)
        {
            if (template == null || template.Width < 2 || template.Height < 3)
            {
                throw new InvalidOperationException("Book P 텍스처 템플릿 크기가 올바르지 않습니다.");
            }

            int halfWidth = template.Width / 2;
            int firstBoundary = FindHorizontalBoundary(
                template,
                0,
                halfWidth,
                Mathf.RoundToInt(template.Height * 0.35f),
                Mathf.RoundToInt(template.Height * 0.55f));
            int secondBoundary = FindHorizontalBoundary(
                template,
                0,
                halfWidth,
                Mathf.RoundToInt(template.Height * 0.6f),
                Mathf.RoundToInt(template.Height * 0.8f));

            int topSourceEnd = Mathf.Clamp(firstBoundary - 3, 1, template.Height - 2);
            int middleSourceStart = Mathf.Clamp(firstBoundary + 1, topSourceEnd, template.Height - 2);
            int middleSourceEnd = Mathf.Clamp(secondBoundary - 1, middleSourceStart + 1, template.Height - 1);
            int bottomSourceStart = Mathf.Clamp(secondBoundary + 3, middleSourceEnd, template.Height - 1);

            int firstTargetBoundary = Mathf.RoundToInt(template.Height / 3f);
            int secondTargetBoundary = Mathf.RoundToInt(template.Height * 2f / 3f);
            PixelBuffer output = new PixelBuffer(template.Width, template.Height);

            RectInt topLeftTarget = new RectInt(0, 0, halfWidth, firstTargetBoundary);
            RectInt topRightTarget = new RectInt(halfWidth, 0, template.Width - halfWidth, firstTargetBoundary);
            RectInt middleLeftTarget = new RectInt(0, firstTargetBoundary, halfWidth, secondTargetBoundary - firstTargetBoundary);
            RectInt middleRightTarget = new RectInt(halfWidth, firstTargetBoundary, template.Width - halfWidth, secondTargetBoundary - firstTargetBoundary);
            RectInt bottomLeftTarget = new RectInt(0, secondTargetBoundary, halfWidth, template.Height - secondTargetBoundary);
            RectInt bottomRightTarget = new RectInt(halfWidth, secondTargetBoundary, template.Width - halfWidth, template.Height - secondTargetBoundary);

            RectInt topLeftSource = new RectInt(0, 0, halfWidth, topSourceEnd);
            RectInt topRightSource = new RectInt(halfWidth, 0, template.Width - halfWidth, topSourceEnd);
            RectInt middleLeftPageSource = new RectInt(0, middleSourceStart, halfWidth, middleSourceEnd - middleSourceStart);
            RectInt middleRightPageSource = new RectInt(halfWidth, middleSourceStart, template.Width - halfWidth, middleSourceEnd - middleSourceStart);
            RectInt bottomLeftSpineSource = new RectInt(0, bottomSourceStart, halfWidth, template.Height - bottomSourceStart);
            RectInt bottomRightPageSource = new RectInt(halfWidth, bottomSourceStart, template.Width - halfWidth, template.Height - bottomSourceStart);

            BlitScaled(template, topLeftSource, output, topLeftTarget);
            BlitScaled(template, topRightSource, output, topRightTarget);
            BlitScaled(template, bottomLeftSpineSource, output, middleLeftTarget);
            BlitScaled(template, middleRightPageSource, output, middleRightTarget);
            BlitScaled(template, middleLeftPageSource, output, bottomLeftTarget);
            BlitScaled(template, bottomRightPageSource, output, bottomRightTarget);
            return output;
        }

        private static PixelBuffer BuildPaperPortableTexture(
            PixelBuffer paperTemplate,
            int outputWidth,
            int outputHeight)
        {
            if (paperTemplate == null
                || paperTemplate.Width < 4
                || paperTemplate.Height < 4
                || outputWidth < 2
                || outputHeight < 3)
            {
                throw new InvalidOperationException("Paper P 텍스처 템플릿 크기가 올바르지 않습니다.");
            }

            int sampleX = Mathf.RoundToInt(paperTemplate.Width * 0.3f);
            int sampleY = Mathf.RoundToInt(paperTemplate.Height * 0.3f);
            int sampleWidth = Mathf.Max(1, Mathf.RoundToInt(paperTemplate.Width * 0.4f));
            int sampleHeight = Mathf.Max(1, Mathf.RoundToInt(paperTemplate.Height * 0.4f));
            RectInt paperSurfaceSource = new RectInt(
                sampleX,
                sampleY,
                Mathf.Min(sampleWidth, paperTemplate.Width - sampleX),
                Mathf.Min(sampleHeight, paperTemplate.Height - sampleY));

            int halfWidth = outputWidth / 2;
            int firstBoundary = Mathf.RoundToInt(outputHeight / 3f);
            int secondBoundary = Mathf.RoundToInt(outputHeight * 2f / 3f);
            PixelBuffer output = new PixelBuffer(outputWidth, outputHeight);
            RectInt[] targetFaces =
            {
                new RectInt(0, 0, halfWidth, firstBoundary),
                new RectInt(halfWidth, 0, outputWidth - halfWidth, firstBoundary),
                new RectInt(0, firstBoundary, halfWidth, secondBoundary - firstBoundary),
                new RectInt(halfWidth, firstBoundary, outputWidth - halfWidth, secondBoundary - firstBoundary),
                new RectInt(0, secondBoundary, halfWidth, outputHeight - secondBoundary),
                new RectInt(halfWidth, secondBoundary, outputWidth - halfWidth, outputHeight - secondBoundary)
            };

            for (int i = 0; i < targetFaces.Length; i++)
            {
                BlitScaled(paperTemplate, paperSurfaceSource, output, targetFaces[i]);
            }

            Darken(output, targetFaces[2], 0.82f);
            Darken(output, targetFaces[3], 0.88f);
            Darken(output, targetFaces[4], 0.76f);
            Darken(output, targetFaces[5], 0.8f);
            return output;
        }

        private static void Darken(PixelBuffer buffer, RectInt area, float multiplier)
        {
            if (buffer == null)
            {
                return;
            }

            float clampedMultiplier = Mathf.Clamp01(multiplier);
            int maxX = Mathf.Min(buffer.Width, area.xMax);
            int maxY = Mathf.Min(buffer.Height, area.yMax);
            for (int y = Mathf.Max(0, area.yMin); y < maxY; y++)
            {
                for (int x = Mathf.Max(0, area.xMin); x < maxX; x++)
                {
                    Color32 color = buffer.GetPixel(x, y);
                    color.r = (byte)Mathf.RoundToInt(color.r * clampedMultiplier);
                    color.g = (byte)Mathf.RoundToInt(color.g * clampedMultiplier);
                    color.b = (byte)Mathf.RoundToInt(color.b * clampedMultiplier);
                    buffer.SetPixel(x, y, color);
                }
            }
        }

        private static int FindHorizontalBoundary(
            PixelBuffer source,
            int x,
            int width,
            int minY,
            int maxY)
        {
            int clampedMinY = Mathf.Clamp(minY, 1, source.Height - 2);
            int clampedMaxY = Mathf.Clamp(maxY, clampedMinY + 1, source.Height - 1);
            int sampleStartX = Mathf.Clamp(x + 32, 0, source.Width - 1);
            int sampleEndX = Mathf.Clamp(x + width - 32, sampleStartX + 1, source.Width);
            float previousLuminance = ComputeRowLuminance(source, sampleStartX, sampleEndX, clampedMinY - 1);
            float strongestDelta = float.MinValue;
            int strongestY = clampedMinY;
            for (int y = clampedMinY; y <= clampedMaxY; y++)
            {
                float luminance = ComputeRowLuminance(source, sampleStartX, sampleEndX, y);
                float delta = Mathf.Abs(luminance - previousLuminance);
                if (delta > strongestDelta)
                {
                    strongestDelta = delta;
                    strongestY = y;
                }

                previousLuminance = luminance;
            }

            return strongestY;
        }

        private static float ComputeRowLuminance(PixelBuffer source, int startX, int endX, int y)
        {
            float luminance = 0f;
            int sampleCount = 0;
            for (int x = startX; x < endX; x += 8)
            {
                Color32 color = source.GetPixel(x, y);
                luminance += color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
                sampleCount++;
            }

            return sampleCount > 0 ? luminance / sampleCount : 0f;
        }

        private static void BlitScaled(
            PixelBuffer source,
            RectInt sourceRect,
            PixelBuffer destination,
            RectInt destinationRect)
        {
            if (sourceRect.width <= 0 || sourceRect.height <= 0
                || destinationRect.width <= 0 || destinationRect.height <= 0)
            {
                return;
            }

            for (int y = 0; y < destinationRect.height; y++)
            {
                float sourceY = sourceRect.y
                    + ((y + 0.5f) / destinationRect.height) * sourceRect.height
                    - 0.5f;
                for (int x = 0; x < destinationRect.width; x++)
                {
                    float sourceX = sourceRect.x
                        + ((x + 0.5f) / destinationRect.width) * sourceRect.width
                        - 0.5f;
                    destination.SetPixel(
                        destinationRect.x + x,
                        destinationRect.y + y,
                        SampleBilinear(source, sourceX, sourceY));
                }
            }
        }

        private static PixelBuffer ExtractEmblem(PixelBuffer source)
        {
            if (source == null)
            {
                return null;
            }

            PixelBuffer working = Clone(source);
            if (!HasTransparentBorder(working))
            {
                RemoveConnectedBackground(working);
            }

            return CropToVisibleBounds(working);
        }

        private static bool HasTransparentBorder(PixelBuffer source)
        {
            int transparentCount = 0;
            int borderCount = 0;
            for (int x = 0; x < source.Width; x++)
            {
                CountTransparent(source.GetPixel(x, 0), ref transparentCount, ref borderCount);
                CountTransparent(source.GetPixel(x, source.Height - 1), ref transparentCount, ref borderCount);
            }

            for (int y = 1; y < source.Height - 1; y++)
            {
                CountTransparent(source.GetPixel(0, y), ref transparentCount, ref borderCount);
                CountTransparent(source.GetPixel(source.Width - 1, y), ref transparentCount, ref borderCount);
            }

            return borderCount > 0 && transparentCount >= Mathf.Max(1, borderCount / 20);
        }

        private static void CountTransparent(Color32 color, ref int transparentCount, ref int totalCount)
        {
            totalCount++;
            if (color.a < 250)
            {
                transparentCount++;
            }
        }

        private static void RemoveConnectedBackground(PixelBuffer source)
        {
            Color32 keyColor = AverageCornerColor(source);
            bool[] visited = new bool[source.Pixels.Length];
            int[] queue = new int[source.Pixels.Length];
            int head = 0;
            int tail = 0;

            for (int x = 0; x < source.Width; x++)
            {
                EnqueueBackgroundPixel(source, x, 0, keyColor, visited, queue, ref tail);
                EnqueueBackgroundPixel(source, x, source.Height - 1, keyColor, visited, queue, ref tail);
            }

            for (int y = 1; y < source.Height - 1; y++)
            {
                EnqueueBackgroundPixel(source, 0, y, keyColor, visited, queue, ref tail);
                EnqueueBackgroundPixel(source, source.Width - 1, y, keyColor, visited, queue, ref tail);
            }

            while (head < tail)
            {
                int index = queue[head++];
                int x = index % source.Width;
                int y = index / source.Width;
                EnqueueBackgroundPixel(source, x - 1, y, keyColor, visited, queue, ref tail);
                EnqueueBackgroundPixel(source, x + 1, y, keyColor, visited, queue, ref tail);
                EnqueueBackgroundPixel(source, x, y - 1, keyColor, visited, queue, ref tail);
                EnqueueBackgroundPixel(source, x, y + 1, keyColor, visited, queue, ref tail);
            }

            for (int i = 0; i < visited.Length; i++)
            {
                if (!visited[i])
                {
                    continue;
                }

                Color32 color = source.Pixels[i];
                int distance = ColorDistance(color, keyColor);
                float alpha = Mathf.InverseLerp(
                    BackgroundTransparentThreshold,
                    BackgroundOpaqueThreshold,
                    distance);
                color.a = (byte)Mathf.RoundToInt(color.a * alpha);
                source.Pixels[i] = color;
            }
        }

        private static void EnqueueBackgroundPixel(
            PixelBuffer source,
            int x,
            int y,
            Color32 keyColor,
            bool[] visited,
            int[] queue,
            ref int tail)
        {
            if (x < 0 || x >= source.Width || y < 0 || y >= source.Height)
            {
                return;
            }

            int index = y * source.Width + x;
            if (visited[index]
                || ColorDistance(source.Pixels[index], keyColor) > BackgroundOpaqueThreshold)
            {
                return;
            }

            visited[index] = true;
            queue[tail++] = index;
        }

        private static int ColorDistance(Color32 left, Color32 right)
        {
            int red = Mathf.Abs(left.r - right.r);
            int green = Mathf.Abs(left.g - right.g);
            int blue = Mathf.Abs(left.b - right.b);
            return Mathf.Max(red, Mathf.Max(green, blue));
        }

        private static Color32 AverageCornerColor(PixelBuffer source)
        {
            Color32 topLeft = source.GetPixel(0, 0);
            Color32 topRight = source.GetPixel(source.Width - 1, 0);
            Color32 bottomLeft = source.GetPixel(0, source.Height - 1);
            Color32 bottomRight = source.GetPixel(source.Width - 1, source.Height - 1);
            return new Color32(
                (byte)((topLeft.r + topRight.r + bottomLeft.r + bottomRight.r) / 4),
                (byte)((topLeft.g + topRight.g + bottomLeft.g + bottomRight.g) / 4),
                (byte)((topLeft.b + topRight.b + bottomLeft.b + bottomRight.b) / 4),
                255);
        }

        private static PixelBuffer CropToVisibleBounds(PixelBuffer source)
        {
            int minX = source.Width;
            int minY = source.Height;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    if (source.GetPixel(x, y).a <= 8)
                    {
                        continue;
                    }

                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return null;
            }

            PixelBuffer cropped = new PixelBuffer(maxX - minX + 1, maxY - minY + 1);
            for (int y = 0; y < cropped.Height; y++)
            {
                Array.Copy(
                    source.Pixels,
                    (minY + y) * source.Width + minX,
                    cropped.Pixels,
                    y * cropped.Width,
                    cropped.Width);
            }

            return cropped;
        }

        private static PixelBuffer RotateCounterClockwise(PixelBuffer source)
        {
            PixelBuffer rotated = new PixelBuffer(source.Height, source.Width);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    rotated.SetPixel(y, source.Width - 1 - x, source.GetPixel(x, y));
                }
            }

            return rotated;
        }

        private static void CompositeCentered(
            PixelBuffer destination,
            PixelBuffer source,
            float centerX,
            float centerY,
            float maximumLongSide,
            float clockwiseRotationDegrees = 0f)
        {
            if (destination == null || source == null || maximumLongSide <= 0f)
            {
                return;
            }

            float normalizedRotation = Mathf.DeltaAngle(0f, clockwiseRotationDegrees);
            if (Mathf.Abs(normalizedRotation) > 0.001f)
            {
                CompositeCenteredRotated(
                    destination,
                    source,
                    centerX,
                    centerY,
                    maximumLongSide,
                    normalizedRotation);
                return;
            }

            float scale = maximumLongSide / Mathf.Max(source.Width, source.Height);
            int outputWidth = Mathf.Max(1, Mathf.RoundToInt(source.Width * scale));
            int outputHeight = Mathf.Max(1, Mathf.RoundToInt(source.Height * scale));
            int startX = Mathf.RoundToInt(centerX - outputWidth * 0.5f);
            int startY = Mathf.RoundToInt(centerY - outputHeight * 0.5f);

            for (int y = 0; y < outputHeight; y++)
            {
                int destinationY = startY + y;
                if (destinationY < 0 || destinationY >= destination.Height)
                {
                    continue;
                }

                float sourceY = ((y + 0.5f) / outputHeight) * source.Height - 0.5f;
                for (int x = 0; x < outputWidth; x++)
                {
                    int destinationX = startX + x;
                    if (destinationX < 0 || destinationX >= destination.Width)
                    {
                        continue;
                    }

                    float sourceX = ((x + 0.5f) / outputWidth) * source.Width - 0.5f;
                    Color32 foreground = SampleBilinear(source, sourceX, sourceY);
                    if (foreground.a == 0)
                    {
                        continue;
                    }

                    Color32 background = destination.GetPixel(destinationX, destinationY);
                    destination.SetPixel(destinationX, destinationY, AlphaBlend(background, foreground));
                }
            }
        }

        private static void CompositeCenteredRotated(
            PixelBuffer destination,
            PixelBuffer source,
            float centerX,
            float centerY,
            float maximumLongSide,
            float clockwiseRotationDegrees)
        {
            float scale = maximumLongSide / Mathf.Max(source.Width, source.Height);
            float scaledWidth = source.Width * scale;
            float scaledHeight = source.Height * scale;
            float radians = clockwiseRotationDegrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            float boundsWidth = Mathf.Abs(scaledWidth * cosine) + Mathf.Abs(scaledHeight * sine);
            float boundsHeight = Mathf.Abs(scaledWidth * sine) + Mathf.Abs(scaledHeight * cosine);
            int startX = Mathf.FloorToInt(centerX - boundsWidth * 0.5f);
            int endX = Mathf.CeilToInt(centerX + boundsWidth * 0.5f);
            int startY = Mathf.FloorToInt(centerY - boundsHeight * 0.5f);
            int endY = Mathf.CeilToInt(centerY + boundsHeight * 0.5f);
            float sourceCenterX = (source.Width - 1) * 0.5f;
            float sourceCenterY = (source.Height - 1) * 0.5f;

            for (int destinationY = startY; destinationY < endY; destinationY++)
            {
                if (destinationY < 0 || destinationY >= destination.Height)
                {
                    continue;
                }

                float offsetY = destinationY + 0.5f - centerY;
                for (int destinationX = startX; destinationX < endX; destinationX++)
                {
                    if (destinationX < 0 || destinationX >= destination.Width)
                    {
                        continue;
                    }

                    float offsetX = destinationX + 0.5f - centerX;
                    float sourceX = (offsetX * cosine + offsetY * sine) / scale + sourceCenterX;
                    float sourceY = (-offsetX * sine + offsetY * cosine) / scale + sourceCenterY;
                    if (sourceX < -0.5f
                        || sourceX > source.Width - 0.5f
                        || sourceY < -0.5f
                        || sourceY > source.Height - 0.5f)
                    {
                        continue;
                    }

                    Color32 foreground = SampleBilinear(source, sourceX, sourceY);
                    if (foreground.a == 0)
                    {
                        continue;
                    }

                    Color32 background = destination.GetPixel(destinationX, destinationY);
                    destination.SetPixel(destinationX, destinationY, AlphaBlend(background, foreground));
                }
            }
        }

        private static Color32 SampleBilinear(PixelBuffer source, float x, float y)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, source.Width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, source.Height - 1);
            int x1 = Mathf.Min(x0 + 1, source.Width - 1);
            int y1 = Mathf.Min(y0 + 1, source.Height - 1);
            float tx = Mathf.Clamp01(x - x0);
            float ty = Mathf.Clamp01(y - y0);
            Color top = Color.Lerp(source.GetPixel(x0, y0), source.GetPixel(x1, y0), tx);
            Color bottom = Color.Lerp(source.GetPixel(x0, y1), source.GetPixel(x1, y1), tx);
            return Color.Lerp(top, bottom, ty);
        }

        private static Color32 AlphaBlend(Color32 background, Color32 foreground)
        {
            float foregroundAlpha = foreground.a / 255f;
            float backgroundAlpha = background.a / 255f;
            float outputAlpha = foregroundAlpha + backgroundAlpha * (1f - foregroundAlpha);
            if (outputAlpha <= 0f)
            {
                return new Color32(0, 0, 0, 0);
            }

            float red = (foreground.r * foregroundAlpha
                + background.r * backgroundAlpha * (1f - foregroundAlpha)) / outputAlpha;
            float green = (foreground.g * foregroundAlpha
                + background.g * backgroundAlpha * (1f - foregroundAlpha)) / outputAlpha;
            float blue = (foreground.b * foregroundAlpha
                + background.b * backgroundAlpha * (1f - foregroundAlpha)) / outputAlpha;
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(red), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(green), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(blue), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(outputAlpha * 255f), 0, 255));
        }

        private static PixelBuffer Clone(PixelBuffer source)
        {
            Color32[] pixels = new Color32[source.Pixels.Length];
            Array.Copy(source.Pixels, pixels, pixels.Length);
            return new PixelBuffer(source.Width, source.Height, pixels);
        }

        private static PixelBuffer ToTopLeftBuffer(Texture2D texture)
        {
            Color32[] bottomLeftPixels = texture.GetPixels32();
            PixelBuffer result = new PixelBuffer(texture.width, texture.height);
            for (int y = 0; y < texture.height; y++)
            {
                Array.Copy(
                    bottomLeftPixels,
                    (texture.height - 1 - y) * texture.width,
                    result.Pixels,
                    y * texture.width,
                    texture.width);
            }

            return result;
        }

        private static Texture2D LoadPng(string assetPath)
        {
            string absolutePath = ToAbsolutePath(assetPath);
            if (!File.Exists(absolutePath))
            {
                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                name = Path.GetFileNameWithoutExtension(assetPath)
            };
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(absolutePath), false))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }

            return texture;
        }

        private static void WritePng(string assetPath, PixelBuffer source, bool includeAlpha)
        {
            TextureFormat format = includeAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24;
            Texture2D texture = new Texture2D(source.Width, source.Height, format, false, false);
            try
            {
                Color32[] bottomLeftPixels = new Color32[source.Pixels.Length];
                for (int y = 0; y < source.Height; y++)
                {
                    Array.Copy(
                        source.Pixels,
                        y * source.Width,
                        bottomLeftPixels,
                        (source.Height - 1 - y) * source.Width,
                        source.Width);
                }

                texture.SetPixels32(bottomLeftPixels);
                texture.Apply(false, false);
                string absolutePath = ToAbsolutePath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
                File.WriteAllBytes(absolutePath, ImageConversion.EncodeToPNG(texture));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void ImportIcon(
            string assetPath,
            string sourceIconGuid,
            string sourceIconGuidUserDataPrefix)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = GeneratedIconMaxTextureSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.userData = sourceIconGuidUserDataPrefix + sourceIconGuid;
            importer.SaveAndReimport();
        }

        private static void ImportPortableTexture(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        private static Material CreateOrUpdatePortableMaterial(
            string materialPath,
            string itemName,
            Texture2D texture)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Material template = AssetDatabase.LoadAssetAtPath<Material>(BookPortableMaterialTemplatePath);
                if (template == null)
                {
                    return null;
                }

                material = new Material(template)
                {
                    name = $"{itemName}_P"
                };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                Undo.RecordObject(material, "Update Book Portable Material");
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string normalizedPath = folderPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalizedPath))
            {
                return;
            }

            string[] segments = normalizedPath.Split('/');
            if (segments.Length == 0 || !string.Equals(segments[0], "Assets", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unity Assets 하위 경로가 아닙니다: {folderPath}");
            }

            string currentPath = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string nextPath = $"{currentPath}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, segments[i]);
                }

                currentPath = nextPath;
            }
        }

        private static string SanitizeFileName(string value)
        {
            string result = string.IsNullOrWhiteSpace(value) ? "Book" : value.Trim();
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidCharacters.Length; i++)
            {
                result = result.Replace(invalidCharacters[i], '_');
            }

            return result;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static bool AssetExists(string assetPath)
        {
            return File.Exists(ToAbsolutePath(assetPath));
        }

        private static bool IsPngAssetPath(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath)
                && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(assetPath), ".png", StringComparison.OrdinalIgnoreCase);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }

    internal static class PaperItemAssetGenerator
    {
        internal static bool TryCreate(
            ItemDefinition selectedDefinition,
            IReadOnlyList<ItemDefinition> definitions,
            out BookItemAssetGenerator.Result result,
            out string errorMessage)
        {
            PaperPortableMeshGenerator.EnsureAssetAndBindings();
            return BookItemAssetGenerator.TryCreatePaper(
                selectedDefinition,
                definitions,
                out result,
                out errorMessage);
        }
    }

    internal static class CdItemAssetGenerator
    {
        internal static bool TryCreate(
            ItemDefinition selectedDefinition,
            IReadOnlyList<ItemDefinition> definitions,
            out BookItemAssetGenerator.Result result,
            out string errorMessage)
        {
            CdPortableMeshGenerator.EnsureAssetAndBindings();
            return BookItemAssetGenerator.TryCreateCd(
                selectedDefinition,
                definitions,
                out result,
                out errorMessage);
        }
    }
}
