using System;
using System.Collections.Generic;
using ProjectF.Simulation.Monsters;
using UnityEngine;

namespace ProjectF.Monsters
{
    public enum MonsterAnimationState { Idle, Move, Attack, Hit, Death }

    [Serializable]
    public sealed class MonsterAnimations
    {
        public AnimationClip idle;
        public AnimationClip move;
        public AnimationClip attack;
        public AnimationClip hit;
        public AnimationClip death;

        public AnimationClip GetClip(MonsterAnimationState state)
        {
            switch (state)
            {
                case MonsterAnimationState.Idle: return idle;
                case MonsterAnimationState.Move: return move;
                case MonsterAnimationState.Attack: return attack;
                case MonsterAnimationState.Hit: return hit;
                case MonsterAnimationState.Death: return death;
                default: return null;
            }
        }
    }

    [Serializable]
    public sealed class MonsterDropEntry
    {
        public ItemDefinition item;
        [Min(1)] public int amount = 1;
        [Range(0f, 1f)] public float chance = 1f;

    }

    [Serializable]
    public sealed class MonsterVariant
    {
        [Min(0)] public int id;
        public string variantName = string.Empty;
        public GameObject prefab;
        public Sprite icon;
        public MonsterStat overrideMask;
        public MonsterStats stats = MonsterStats.Default;
        public bool overrideDrops;
        public List<MonsterDropEntry> drops = new List<MonsterDropEntry>();
    }

    [CreateAssetMenu(menuName = "ProjectF/Monster Definition", fileName = "Monster_")]
    public sealed class MonsterDefinition : ScriptableObject
    {
        [SerializeField, Min(0)] private int id;
        [SerializeField] private string monsterName = string.Empty;
        [SerializeField] private MonsterStats commonStats = MonsterStats.Default;
        [SerializeField] private MonsterAnimations animations = new MonsterAnimations();
        [SerializeField] private List<MonsterDropEntry> commonDrops = new List<MonsterDropEntry>();
        [SerializeField] private List<MonsterVariant> variants = new List<MonsterVariant>();
        [SerializeField, HideInInspector] private int nextVariantId;

        public int Id => id;
        public string MonsterName => monsterName;
        public MonsterStats CommonStats => commonStats;
        public MonsterAnimations Animations => animations;
        public IReadOnlyList<MonsterDropEntry> CommonDrops => commonDrops;
        public IReadOnlyList<MonsterVariant> Variants => variants;

        public bool TryGetVariant(int variantId, out MonsterVariant variant)
        {
            for (int i = 0; variants != null && i < variants.Count; i++)
            {
                variant = variants[i];
                if (variant != null && variant.id == variantId) return true;
            }
            variant = null;
            return false;
        }

        public bool TryGetRuntimeData(int variantId, out MonsterArchetypeData data)
        {
            if (TryGetVariant(variantId, out MonsterVariant variant))
            {
                data = new MonsterArchetypeData(id, variant.id,
                    MonsterStats.Resolve(commonStats, variant.stats, variant.overrideMask));
                return true;
            }
            data = default;
            return false;
        }

        public IReadOnlyList<MonsterDropEntry> GetDrops(int variantId)
        {
            return TryGetVariant(variantId, out MonsterVariant variant)
                ? variant.overrideDrops ? variant.drops : commonDrops
                : Array.Empty<MonsterDropEntry>();
        }

        // Build once when a catalog is loaded; simulation ticks consume these numeric records.
        public MonsterDropData[] CreateDropData(int variantId)
        {
            IReadOnlyList<MonsterDropEntry> source = GetDrops(variantId);
            if (source == null || source.Count == 0) return Array.Empty<MonsterDropData>();
            int count = 0;
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null && source[i].item != null && source[i].item.id >= 0) count++;
            MonsterDropData[] result = new MonsterDropData[count];
            int index = 0;
            for (int i = 0; i < source.Count; i++)
            {
                MonsterDropEntry entry = source[i];
                if (entry == null || entry.item == null || entry.item.id < 0) continue;
                float chance = float.IsNaN(entry.chance) || float.IsInfinity(entry.chance)
                    ? 0f : Mathf.Clamp01(entry.chance);
                result[index++] = new MonsterDropData(entry.item.id,
                    Mathf.Max(1, entry.amount), chance);
            }
            return result;
        }

#if UNITY_EDITOR
        public int NextVariantId => nextVariantId;

        public void ReserveVariantIdsThrough(int exclusiveMaximum)
        {
            nextVariantId = Math.Max(nextVariantId, exclusiveMaximum);
        }

        public int AllocateVariantId()
        {
            for (int i = 0; variants != null && i < variants.Count; i++)
                if (variants[i] != null) nextVariantId = Math.Max(nextVariantId, variants[i].id + 1);
            return nextVariantId++;
        }

        public void SetAuthoringData(int speciesId, string speciesName, MonsterStats stats,
            MonsterAnimations clips, List<MonsterDropEntry> drops, List<MonsterVariant> colors)
        {
            id = speciesId;
            monsterName = speciesName;
            commonStats = stats;
            animations = clips ?? new MonsterAnimations();
            commonDrops = drops ?? new List<MonsterDropEntry>();
            variants = colors ?? new List<MonsterVariant>();
            for (int i = 0; i < variants.Count; i++)
                if (variants[i] != null) nextVariantId = Math.Max(nextVariantId, variants[i].id + 1);
        }
#endif
    }
}
