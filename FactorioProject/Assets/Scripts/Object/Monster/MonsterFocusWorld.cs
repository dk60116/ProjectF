using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Monsters
{
    public static class MonsterFocusWorld
    {
        // Selection registry only; AI target acquisition belongs to MonsterWorld.
        private static readonly List<MonsterInstance> instances = new List<MonsterInstance>();
        private static readonly Dictionary<int, MonsterInstance> byRoot = new Dictionary<int, MonsterInstance>();

        internal static void Register(MonsterInstance instance)
        { instances.Add(instance); byRoot[instance.RootInstanceId] = instance; }
        internal static void Unregister(MonsterInstance instance)
        {
            instances.Remove(instance);
            byRoot.Remove(instance.RootInstanceId);
        }

        public static bool TryGetFromCollider(Collider collider, out MonsterInstance monster)
        {
            for (Transform node = collider != null ? collider.transform : null; node != null; node = node.parent)
                if (byRoot.TryGetValue(node.gameObject.GetInstanceID(), out monster)) return true;
            monster = null; return false;
        }

        public static bool TryRaycast(Ray ray, float maxDistance, out MonsterInstance monster, out float distance)
        {
            monster = null;
            distance = maxDistance;
            if (!(maxDistance >= 0f)) return false;
            for (int i = instances.Count - 1; i >= 0; i--)
            {
                MonsterInstance candidate = instances[i];
                if (candidate.Root == null)
                {
                    candidate.Dispose();
                    continue;
                }
                if (!candidate.TryRaycast(ray, distance, out float hit)) continue;
                monster = candidate;
                distance = hit;
            }
            return monster != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            while (instances.Count > 0) instances[instances.Count - 1].Dispose();
            byRoot.Clear();
        }
    }
}
