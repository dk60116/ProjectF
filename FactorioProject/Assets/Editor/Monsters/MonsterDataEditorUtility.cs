using System;
using System.Collections.Generic;
using System.IO;
using ProjectF.Monsters;
using ProjectF.Simulation.Monsters;
using UnityEditor;
using UnityEngine;

namespace ProjectF.Editor.Monsters
{
    internal readonly struct MonsterValidationIssue
    {
        public readonly MessageType Type;
        public readonly string Message;
        public MonsterValidationIssue(MessageType type, string message)
        {
            Type = type;
            Message = message;
        }
    }

    internal static class MonsterDataEditorUtility
    {
        public const string DefinitionRoot = "Assets/Monsters";
        public const string PackRoot = "Assets/Level 1 Monster Pack";

        public static List<MonsterDefinition> LoadDefinitions()
        {
            List<MonsterDefinition> result = new List<MonsterDefinition>();
            string[] guids = AssetDatabase.FindAssets("t:MonsterDefinition");
            foreach (string guid in guids)
            {
                MonsterDefinition definition = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null) result.Add(definition);
            }
            result.Sort((left, right) => string.Compare(
                AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right),
                StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public static List<MonsterValidationIssue> Validate(IReadOnlyList<MonsterDefinition> catalog)
        {
            List<MonsterValidationIssue> result = new List<MonsterValidationIssue>();
            HashSet<int> ids = new HashSet<int>();
            for (int i = 0; i < catalog.Count; i++)
            {
                MonsterDefinition definition = catalog[i];
                if (definition == null) continue;
                string prefix = $"[{definition.Id}] {definition.MonsterName}: ";
                if (definition.Id < 0 || !ids.Add(definition.Id))
                    Error(result, prefix + "Invalid or duplicate species ID.");
                if (string.IsNullOrWhiteSpace(definition.MonsterName))
                    Error(result, prefix + "Species name is empty.");
                if (!definition.CommonStats.IsValid)
                    Error(result, prefix + "Common stats must be finite and within their valid ranges.");
                ValidateDrops(definition.CommonDrops, prefix, result);
                if (definition.Variants == null || definition.Variants.Count == 0)
                {
                    Error(result, prefix + "No variants.");
                    continue;
                }
                HashSet<int> variantIds = new HashSet<int>();
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<GameObject> prefabs = new HashSet<GameObject>();
                for (int v = 0; v < definition.Variants.Count; v++)
                {
                    MonsterVariant variant = definition.Variants[v];
                    string variantPrefix = prefix + (variant?.variantName ?? "(null)") + ": ";
                    if (variant == null)
                    {
                        Error(result, variantPrefix + "Missing variant.");
                        continue;
                    }
                    if (variant.id < 0 || !variantIds.Add(variant.id))
                        Error(result, variantPrefix + "Invalid or duplicate variant ID.");
                    if (string.IsNullOrWhiteSpace(variant.variantName) || !names.Add(variant.variantName.Trim()))
                        Error(result, variantPrefix + "Empty or duplicate variant name.");
                    if ((variant.overrideMask & ~MonsterStat.All) != 0 || !variant.stats.IsValid)
                        Error(result, variantPrefix + "Invalid stat overrides.");
                    if (variant.prefab == null || !PrefabUtility.IsPartOfPrefabAsset(variant.prefab))
                        Error(result, variantPrefix + "Assign a prefab asset.");
                    else
                    {
                        if (!prefabs.Add(variant.prefab))
                            result.Add(new MonsterValidationIssue(MessageType.Warning,
                                variantPrefix + "Another variant uses the same prefab."));
                        ValidateModel(definition, variant, variantPrefix, result);
                    }
                    ValidateDrops(variant.drops, variantPrefix, result);
                    MonsterStats effective = MonsterStats.Resolve(
                        definition.CommonStats, variant.stats, variant.overrideMask);
                    if (effective.LeashRange < effective.DetectionRange)
                        result.Add(new MonsterValidationIssue(MessageType.Warning,
                            variantPrefix + "Leash range is shorter than detection range."));
                }
                for (int state = 0; state <= (int)MonsterAnimationState.Death; state++)
                {
                    if (definition.Animations?.GetClip((MonsterAnimationState)state) == null)
                        result.Add(new MonsterValidationIssue(MessageType.Warning,
                            prefix + "Missing animation: " + (MonsterAnimationState)state));
                }
            }
            return result;
        }

        private static void ValidateModel(MonsterDefinition definition, MonsterVariant variant,
            string prefix, List<MonsterValidationIssue> result)
        {
            Renderer[] renderers = variant.prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) Error(result, prefix + "Prefab has no renderer.");
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null)
                        Error(result, prefix + "Missing material or shader.");
                    else if (material.shader.name != "Custom/ToonCharacter")
                        result.Add(new MonsterValidationIssue(MessageType.Warning,
                            prefix + material.name + " does not use Custom/ToonCharacter."));
                }
            }
            if (definition.Animations != null
                && variant.prefab.GetComponentInChildren<Animator>(true) == null)
                result.Add(new MonsterValidationIssue(MessageType.Warning,
                    prefix + "No Animator; preview uses clip sampling."));
        }

        private static void ValidateDrops(IReadOnlyList<MonsterDropEntry> drops,
            string prefix, List<MonsterValidationIssue> result)
        {
            for (int i = 0; drops != null && i < drops.Count; i++)
            {
                MonsterDropEntry entry = drops[i];
                if (entry == null || entry.item == null || entry.item.id < 0
                    || entry.amount < 1 || float.IsNaN(entry.chance) || float.IsInfinity(entry.chance)
                    || entry.chance < 0f || entry.chance > 1f)
                    Error(result, prefix + $"Invalid drop entry {i + 1}.");
            }
        }

        private static void Error(List<MonsterValidationIssue> result, string message) =>
            result.Add(new MonsterValidationIssue(MessageType.Error, message));

        public static bool HasErrors(IReadOnlyList<MonsterValidationIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].Type == MessageType.Error) return true;
            return false;
        }

        public static int NextSpeciesId(IReadOnlyList<MonsterDefinition> catalog)
        {
            int result = 0;
            for (int i = 0; i < catalog.Count; i++) result = Math.Max(result, catalog[i].Id + 1);
            return result;
        }

        public static void CopyAuthoring(MonsterDefinition source, MonsterDefinition target)
        {
            // Copy only authored fields, never the draft object's hide flags or name.
            using SerializedObject input = new SerializedObject(source);
            using SerializedObject output = new SerializedObject(target);
            using SerializedProperty property = input.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = false;
                if (!property.name.StartsWith("m_", StringComparison.Ordinal))
                    output.CopyFromSerializedProperty(property);
            }
            output.ApplyModifiedPropertiesWithoutUndo();
        }

        public static int RebuildPack()
        {
            string prefabRoot = PackRoot + "/Prefabs";
            if (!AssetDatabase.IsValidFolder(prefabRoot))
                throw new InvalidOperationException("Level 1 Monster Pack prefab folder is missing.");
            EnsureFolder(DefinitionRoot);
            List<MonsterDefinition> catalog = LoadDefinitions();
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabRoot });
            Array.Sort(prefabGuids, (left, right) => string.Compare(
                AssetDatabase.GUIDToAssetPath(left), AssetDatabase.GUIDToAssetPath(right),
                StringComparison.OrdinalIgnoreCase));
            int added = 0;
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string species = Path.GetFileName(Path.GetDirectoryName(path));
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool exists = false;
                foreach (MonsterDefinition candidate in catalog)
                    foreach (MonsterVariant variant in candidate.Variants)
                        if (variant != null && variant.prefab == prefab) exists = true;
                if (exists) continue;
                MonsterDefinition definition = catalog.Find(candidate =>
                    string.Equals(candidate.MonsterName, species, StringComparison.OrdinalIgnoreCase)
                    || HasPrefabFolder(candidate, Path.GetDirectoryName(path)));
                bool created = definition == null;
                if (created)
                {
                    definition = ScriptableObject.CreateInstance<MonsterDefinition>();
                    definition.SetAuthoringData(NextSpeciesId(catalog), species, MonsterStats.Default,
                        FindAnimations(species), new List<MonsterDropEntry>(), new List<MonsterVariant>());
                    EnsureFolder(DefinitionRoot + "/" + species);
                    AssetDatabase.CreateAsset(definition, AssetDatabase.GenerateUniqueAssetPath(
                        DefinitionRoot + "/" + species + "/Monster_" + species + ".asset"));
                    Undo.RegisterCreatedObjectUndo(definition, "Create Monster Definition");
                    catalog.Add(definition);
                }
                Undo.RecordObject(definition, "Add Monster Variant");
                List<MonsterVariant> variants = new List<MonsterVariant>(definition.Variants);
                string variantName = Path.GetFileNameWithoutExtension(path);
                string prefix = species + "_";
                if (variantName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    variantName = variantName.Substring(prefix.Length);
                if (variants.Exists(variant => variant != null && string.Equals(
                    variant.variantName, variantName, StringComparison.OrdinalIgnoreCase))) continue;
                variants.Add(new MonsterVariant
                {
                    id = definition.AllocateVariantId(), variantName = variantName, prefab = prefab
                });
                definition.SetAuthoringData(definition.Id, definition.MonsterName,
                    definition.CommonStats, definition.Animations,
                    new List<MonsterDropEntry>(definition.CommonDrops), variants);
                EditorUtility.SetDirty(definition);
                added++;
            }
            AssetDatabase.SaveAssets();
            return added;
        }

        private static bool HasPrefabFolder(MonsterDefinition definition, string folder)
        {
            foreach (MonsterVariant variant in definition.Variants)
                if (variant?.prefab != null && string.Equals(
                    Path.GetDirectoryName(AssetDatabase.GetAssetPath(variant.prefab)),
                    folder, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static MonsterAnimations FindAnimations(string species)
        {
            MonsterAnimations result = new MonsterAnimations();
            string modelRoot = PackRoot + "/Models";
            if (!AssetDatabase.IsValidFolder(modelRoot)) return result;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { modelRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileNameWithoutExtension(path).StartsWith(species + "_",
                    StringComparison.OrdinalIgnoreCase)) continue;
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
                    string name = clip.name.ToLowerInvariant();
                    if (name.EndsWith("_idle")) result.idle = clip;
                    else if (name.EndsWith("_move")) result.move = clip;
                    else if (name.EndsWith("_attack")) result.attack = clip;
                    else if (name.EndsWith("_damage")) result.hit = clip;
                    else if (name.EndsWith("_die")) result.death = clip;
                }
            }
            return result;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
