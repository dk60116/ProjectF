using ProjectF.Monsters;
using UnityEditor;
using UnityEngine;

namespace ProjectF.Editor.Monsters
{
    [InitializeOnLoad]
    internal static class MonsterEditorSpawner
    {
        static MonsterEditorSpawner()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Clear();
            };
        }

        public static bool TrySpawn(MonsterDefinition definition, int variantId,
            out GameObject spawned, out string error)
        {
            spawned = null;
            if (definition == null || !definition.TryGetVariant(variantId, out var variant)
                || variant.prefab == null || !PrefabUtility.IsPartOfPrefabAsset(variant.prefab))
            {
                error = "Select a saved monster variant with a prefab.";
                return false;
            }
            return MonsterSpawner.TrySpawnNearPlayer(definition, variantId, out spawned, out error);
        }

        private static void Clear()
        {
            MonsterSpawnBrush.Stop();
            MonsterWorld.Current?.Dispose();
        }
    }
}
