using System;
using System.Collections.Generic;
using ProjectF.Simulation.Monsters;
using UnityEngine;

namespace ProjectF.Monsters
{
    public static class MonsterSpawner
    {
        private static readonly List<AnimalAIController> animals = new List<AnimalAIController>();

        public static bool TrySpawnNearPlayer(MonsterDefinition definition, int variantId,
            out GameObject spawned, out string error)
        {
            spawned = null;
            error = null;
            if (!TryGetSpawnContext(definition, variantId, out var variant, out var terrain, out error)) return false;
            Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null)
            {
                error = "No active player or loaded terrain.";
                return false;
            }
            animals.Clear();
            AnimalAIWorld.Instance?.CopyControllers(animals, true);
            Vector3 origin = player.transform.position;
            Vector3 position = default;
            bool found;
            try
            {
                found = MonsterSpawnSearch.TryFindNear(Mathf.RoundToInt(origin.x), Mathf.RoundToInt(origin.z),
                    (x, z) =>
                    {
                        Vector3 candidate = new Vector3(x, 0f, z);
                        if (!TryGetSpawnBlock(terrain, candidate, out Block block)) return false;
                        position = block.WorldPosition;
                        return true;
                    }, out _, out _);
            }
            finally { animals.Clear(); }
            if (!found)
            {
                error = "No empty ground within 2-8 blocks of the player.";
                return false;
            }
            return InstantiateMonster(definition, variantId, variant, terrain, position, out spawned, out error);
        }

        public static bool TrySpawnAt(MonsterDefinition definition, int variantId, Vector3 position,
            out GameObject spawned, out string error)
        {
            spawned = null;
            if (!TryGetSpawnContext(definition, variantId, out var variant, out var terrain, out error)) return false;
            if (float.IsNaN(position.x) || float.IsInfinity(position.x)
                || float.IsNaN(position.z) || float.IsInfinity(position.z))
            {
                error = "Invalid spawn position.";
                return false;
            }
            animals.Clear();
            AnimalAIWorld.Instance?.CopyControllers(animals, true);
            try
            {
                if (!TryGetSpawnBlock(terrain, position, out Block block))
                {
                    error = "Select empty, loaded ground.";
                    return false;
                }
                position.y = block.WorldPosition.y;
            }
            finally { animals.Clear(); }
            return InstantiateMonster(definition, variantId, variant, terrain, position, out spawned, out error);
        }

        private static bool TryGetSpawnContext(MonsterDefinition definition, int variantId,
            out MonsterVariant variant, out TerrainGenerator terrain, out string error)
        {
            variant = null;
            terrain = null;
            error = null;
            if (!Application.isPlaying || definition == null
                || !definition.TryGetVariant(variantId, out variant) || variant.prefab == null)
            {
                error = "A saved monster variant and Play Mode are required.";
                return false;
            }
            terrain = TerrainGenerator.ResolveActive();
            if (terrain == null || MapObjectTickManager.WaitingForWorldLoad)
            {
                error = "No loaded terrain.";
                return false;
            }
            return true;
        }

        private static bool TryGetSpawnBlock(TerrainGenerator terrain, Vector3 position, out Block block)
        {
            return terrain.TryGetLoadedBlock(TerrainGenerator.GetWorldBlockCoordinate(position), out block)
                && block != null && block.IsRuntimeActive && block.MapObject == null
                && terrain.CanAnimalMoveTo(position, true) && IsUnoccupied(position);
        }

        private static bool InstantiateMonster(MonsterDefinition definition, int variantId, MonsterVariant variant,
            TerrainGenerator terrain, Vector3 position, out GameObject spawned, out string error)
        {
            spawned = null;
            error = null;
            MonsterInstance monster = null;
            try
            {
                spawned = UnityEngine.Object.Instantiate(variant.prefab, position,
                    variant.prefab.transform.rotation, terrain.transform);
                spawned.name = definition.MonsterName + " " + variant.variantName;
                spawned.SetActive(true);
                monster = new MonsterInstance(spawned, definition, variantId);
                MonsterWorld.Register(monster, definition, terrain);
                return true;
            }
            catch (Exception exception)
            {
                monster?.Dispose();
                if (spawned != null) UnityEngine.Object.Destroy(spawned);
                spawned = null;
                error = exception.Message;
                return false;
            }
        }

        private static bool IsUnoccupied(Vector3 position)
        {
            Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player != null)
            {
                Vector3 delta = player.transform.position - position;
                delta.y = 0f;
                if (delta.sqrMagnitude < .75f * .75f) return false;
            }
            if (MonsterWorld.Current != null && MonsterWorld.Current.IsOccupied(position, 1.25f)) return false;
            for (int i = 0; i < animals.Count; i++)
            {
                AnimalAIController animal = animals[i];
                if (animal == null) continue;
                float radius = 0.5f + animal.AvoidanceColliderRadius;
                Vector3 delta = animal.SimulationPosition - position;
                delta.y = 0f;
                if (delta.sqrMagnitude < radius * radius) return false;
            }
            return true;
        }
    }
}
