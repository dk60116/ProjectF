using System;
using ProjectF.MapObjects;
using UnityEditor;
using UnityEngine;

namespace ProjectF.Editor.MapObjects
{
    internal static class MapObjectHandleValidation
    {
        [MenuItem("Tools/ProjectF/Diagnostics/Validate Map Object Handles")]
        private static void Validate()
        {
            GameObject host = new GameObject("__MapObjectHandleValidation");
            try
            {
                VirtualObjectWorld world = host.AddComponent<VirtualObjectWorld>();
                BlockStateStore.InstallationSaveState state = CreateState(25, 1001L);

                MapObjectHandle initial = world.UpsertInstallationHandle(state);
                Require(initial.IsValid, "The first installation handle was invalid.");
                Require(world.TryGetRecord(initial, out VirtualObjectRecord initialRecord),
                    "The first installation handle did not resolve.");
                Require(initialRecord.itemId == state.itemId, "The handle resolved the wrong item type.");

                state.quarterTurns = 1;
                MapObjectHandle updated = world.UpsertInstallationHandle(state);
                Require(updated == initial, "A state-only update changed the installation identity.");

                state.placementSequence = 1002L;
                MapObjectHandle replacement = world.UpsertInstallationHandle(state);
                Require(replacement.IsValid && replacement != initial,
                    "Replacing the logical installation did not change its handle.");
                Require(!world.TryGetRecord(initial, out _), "A stale handle resolved after replacement.");
                Require(!world.RemoveInstallation(initial), "A stale handle removed the replacement installation.");
                Require(world.TryGetRecord(replacement, out _), "The replacement handle did not resolve.");

                Require(world.RemoveInstallation(replacement), "The current handle could not remove its installation.");
                Require(!world.TryGetRecord(replacement, out _), "A removed handle still resolved.");

                state.placementSequence = 1003L;
                MapObjectHandle beforeClear = world.UpsertInstallationHandle(state);
                world.Clear();
                state.placementSequence = 1004L;
                MapObjectHandle afterClear = world.UpsertInstallationHandle(state);
                Require(beforeClear.Slot == afterClear.Slot,
                    "The clear validation did not exercise slot reuse.");
                Require(beforeClear.Generation != afterClear.Generation,
                    "A cleared slot reused its previous generation.");
                Require(!world.TryGetRecord(beforeClear, out _), "A pre-clear handle resolved after slot reuse.");
                Require(world.TryGetRecord(afterClear, out _), "The post-clear handle did not resolve.");

                ValidateResourceHandles(world);

                Debug.Log("MapObject handle validation passed: stable updates, replacement invalidation, "
                          + "stale removal protection, clear-time slot reuse, and resource identity are correct.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static BlockStateStore.InstallationSaveState CreateState(int itemId, long sequence)
        {
            var state = new BlockStateStore.InstallationSaveState
            {
                anchorCoordinate = new Vector2Int(3, 7),
                storageKey = new Vector2Int(3, 7),
                hasStorageKey = true,
                itemId = itemId,
                itemName = "Handle Validation",
                placementSequence = sequence,
                quarterTurns = 0
            };
            state.occupiedCoordinates.Add(state.anchorCoordinate);
            return state;
        }

        private static void ValidateResourceHandles(VirtualObjectWorld world)
        {
            Vector2Int coordinate = new Vector2Int(11, 13);
            int installationVersion = world.InstallationVersion;
            var state = new Resource.ResourceSaveState
            {
                resourceCount = 80,
                maxGauge = 10,
                currentGauge = 10,
                initialResourceCount = 100
            };

            world.UpsertResource(coordinate, 30, state);
            Require(world.TryGetResourceHandle(coordinate, out MapObjectHandle initial),
                "The resource handle was not created.");

            state.resourceCount = 70;
            world.UpsertResource(coordinate, 30, state);
            Require(world.TryGetResourceHandle(coordinate, out MapObjectHandle updated) && updated == initial,
                "A resource state update changed the resource identity.");

            world.UpsertResource(coordinate, 31, state);
            Require(world.TryGetResourceHandle(coordinate, out MapObjectHandle replacement)
                    && replacement != initial,
                "Replacing the resource type did not change its handle.");
            Require(!world.IsHandleAlive(initial), "A replaced resource handle remained alive.");
            Require(!world.RemoveResource(initial), "A stale handle removed the replacement resource.");
            Require(world.RemoveResource(replacement), "The current resource handle could not remove its resource.");
            Require(world.InstallationVersion == installationVersion,
                "Resource changes invalidated the installation render version.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
