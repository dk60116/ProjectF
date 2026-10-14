using System;
using System.Collections.Generic;
using ProjectF.Monsters;
using ProjectF.Simulation.Monsters;
using UnityEditor;
using UnityEngine;

namespace ProjectF.Editor.Monsters
{
    internal static class MonsterDataJson
    {
        [Serializable]
        private sealed class Document
        {
            public string format = "ProjectF.MonsterData";
            public int version = 1;
            public List<Species> monsters = new List<Species>();
        }

        [Serializable]
        private sealed class AssetReference
        {
            public string guid;
            public long localId;

            public static AssetReference From(UnityEngine.Object asset)
            {
                if (asset == null) return null;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id))
                    throw new InvalidOperationException("Reference is not a saved asset: " + asset.name);
                return new AssetReference { guid = guid, localId = id };
            }

            public T Resolve<T>() where T : UnityEngine.Object
            {
                if (string.IsNullOrEmpty(guid)) return null;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    throw new InvalidOperationException($"Missing {typeof(T).Name} asset: {guid}");
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is T typed && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        asset, out string _, out long id) && id == localId) return typed;
                }
                throw new InvalidOperationException($"Missing {typeof(T).Name} reference: {guid}:{localId}");
            }
        }

        [Serializable]
        private sealed class Drop
        {
            public AssetReference item;
            public int amount;
            public float chance;
        }

        [Serializable]
        private sealed class Variant
        {
            public int id;
            public string name;
            public AssetReference prefab;
            public AssetReference icon;
            public MonsterStat overrideMask;
            public MonsterStats stats;
            public bool overrideDrops;
            public List<Drop> drops;
        }

        [Serializable]
        private sealed class Species
        {
            public AssetReference definition;
            public int id;
            public string name;
            public MonsterStats commonStats;
            public AssetReference idle;
            public AssetReference move;
            public AssetReference attack;
            public AssetReference hit;
            public AssetReference death;
            public List<Drop> commonDrops;
            public List<Variant> variants;
            public int nextVariantId;
        }

        public static string Export(IReadOnlyList<MonsterDefinition> sources,
            IReadOnlyList<MonsterDefinition> drafts)
        {
            Document document = new Document();
            for (int i = 0; i < drafts.Count; i++)
            {
                MonsterDefinition draft = drafts[i];
                Species species = new Species
                {
                    definition = AssetReference.From(sources[i]), id = draft.Id,
                    name = draft.MonsterName, commonStats = draft.CommonStats,
                    idle = AssetReference.From(draft.Animations?.idle),
                    move = AssetReference.From(draft.Animations?.move),
                    attack = AssetReference.From(draft.Animations?.attack),
                    hit = AssetReference.From(draft.Animations?.hit),
                    death = AssetReference.From(draft.Animations?.death),
                    commonDrops = ExportDrops(draft.CommonDrops), variants = new List<Variant>(),
                    nextVariantId = draft.NextVariantId
                };
                foreach (MonsterVariant variant in draft.Variants)
                {
                    species.variants.Add(new Variant
                    {
                        id = variant.id, name = variant.variantName,
                        prefab = AssetReference.From(variant.prefab), icon = AssetReference.From(variant.icon),
                        overrideMask = variant.overrideMask, stats = variant.stats,
                        overrideDrops = variant.overrideDrops, drops = ExportDrops(variant.drops)
                    });
                }
                document.monsters.Add(species);
            }
            return JsonUtility.ToJson(document, true);
        }

        private static List<Drop> ExportDrops(IReadOnlyList<MonsterDropEntry> source)
        {
            List<Drop> result = new List<Drop>();
            for (int i = 0; source != null && i < source.Count; i++)
            {
                MonsterDropEntry entry = source[i];
                result.Add(new Drop
                {
                    item = AssetReference.From(entry.item), amount = entry.amount, chance = entry.chance
                });
            }
            return result;
        }

        // Prepare all entries before mutating any draft, including entries not present in the JSON.
        public static List<MonsterDefinition> Import(string json,
            IReadOnlyList<MonsterDefinition> sources, IReadOnlyList<MonsterDefinition> drafts,
            bool validate = true)
        {
            Document document = JsonUtility.FromJson<Document>(json);
            if (document == null || document.format != "ProjectF.MonsterData" || document.version != 1
                || document.monsters == null || document.monsters.Count == 0)
                throw new InvalidOperationException("Unsupported or empty Monster Data JSON.");
            List<MonsterDefinition> result = new List<MonsterDefinition>();
            try
            {
                foreach (MonsterDefinition draft in drafts)
                {
                    MonsterDefinition copy = UnityEngine.Object.Instantiate(draft);
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    result.Add(copy);
                }
                HashSet<int> importedIndices = new HashSet<int>();
                foreach (Species entry in document.monsters)
                {
                    if (entry == null) throw new InvalidOperationException("Missing species entry.");
                    MonsterDefinition referenced = entry.definition?.Resolve<MonsterDefinition>();
                    int index = -1;
                    for (int i = 0; i < sources.Count; i++)
                    {
                        if ((referenced != null && sources[i] == referenced)
                            || (referenced == null && sources[i].Id == entry.id)) { index = i; break; }
                    }
                    if (index < 0 || !importedIndices.Add(index))
                        throw new InvalidOperationException("Unknown or duplicate species: " + entry.name);
                    List<MonsterVariant> variants = new List<MonsterVariant>();
                    if (entry.variants == null) throw new InvalidOperationException("Missing variants: " + entry.name);
                    foreach (Variant variant in entry.variants)
                    {
                        if (variant == null) throw new InvalidOperationException("Missing variant: " + entry.name);
                        variants.Add(new MonsterVariant
                        {
                            id = variant.id, variantName = variant.name,
                            prefab = variant.prefab?.Resolve<GameObject>(), icon = variant.icon?.Resolve<Sprite>(),
                            overrideMask = variant.overrideMask, stats = variant.stats,
                            overrideDrops = variant.overrideDrops, drops = ImportDrops(variant.drops)
                        });
                    }
                    result[index].SetAuthoringData(entry.id, entry.name, entry.commonStats,
                        new MonsterAnimations
                        {
                            idle = entry.idle?.Resolve<AnimationClip>(), move = entry.move?.Resolve<AnimationClip>(),
                            attack = entry.attack?.Resolve<AnimationClip>(), hit = entry.hit?.Resolve<AnimationClip>(),
                            death = entry.death?.Resolve<AnimationClip>()
                        }, ImportDrops(entry.commonDrops), variants);
                    result[index].ReserveVariantIdsThrough(entry.nextVariantId);
                }
                List<MonsterValidationIssue> issues = validate
                    ? MonsterDataEditorUtility.Validate(result) : new List<MonsterValidationIssue>();
                if (MonsterDataEditorUtility.HasErrors(issues))
                {
                    foreach (MonsterValidationIssue issue in issues)
                        if (issue.Type == MessageType.Error) throw new InvalidOperationException(issue.Message);
                }
                return result;
            }
            catch
            {
                foreach (MonsterDefinition copy in result) UnityEngine.Object.DestroyImmediate(copy);
                throw;
            }
        }

        private static List<MonsterDropEntry> ImportDrops(List<Drop> source)
        {
            List<MonsterDropEntry> result = new List<MonsterDropEntry>();
            if (source == null) return result;
            foreach (Drop entry in source)
            {
                if (entry == null) throw new InvalidOperationException("Missing drop entry.");
                result.Add(new MonsterDropEntry
                {
                    item = entry.item?.Resolve<ItemDefinition>(), amount = entry.amount, chance = entry.chance
                });
            }
            return result;
        }
    }
}
