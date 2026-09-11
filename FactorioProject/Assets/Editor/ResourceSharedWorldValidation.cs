using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class ResourceSharedWorldValidation
{
    [MenuItem("Tools/ProjectF/Diagnostics/Validate Shared Resources", true)]
    private static bool CanValidate() => Application.isPlaying;

    // Read-only validation of the running world's ownership and render identities.
    [MenuItem("Tools/ProjectF/Diagnostics/Validate Shared Resources")]
    private static void Validate()
    {
        ResourceTypeWorld[] hosts = UnityEngine.Object.FindObjectsByType<ResourceTypeWorld>(FindObjectsSortMode.None);
        HashSet<Vector2Int> coordinates = new HashSet<Vector2Int>();
        HashSet<ResourceHandle> identities = new HashSet<ResourceHandle>();
        int resources = 0, colliders = 0, renderedParts = 0;
        long units = 0;
        foreach (ResourceTypeWorld host in hosts)
        {
            Require(host.transform.childCount == 0, host.name + " has per-resource child GameObjects.");
            Require(host.GetComponents<Resource>().Length == 0, "Shared host contains per-resource MonoBehaviours.");
            foreach (ResourceInstance resource in host.Instances)
            {
                if (!resource.IsRuntimeActive) continue;
                Require(resource.Handle.TryResolve(out ResourceInstance resolved) && ReferenceEquals(resolved, resource), "Resource handle does not resolve its own state.");
                Require(identities.Add(resource.Handle), "Resource identity was reused.");
                Block block = resource.OwningBlock;
                Require(block != null && ReferenceEquals(block.Resource, resource), "Resource/block ownership does not agree.");
                Require(coordinates.Add(block.Coordinate), "Two live resources own " + block.Coordinate);
                Require((resource.WorldPosition - block.WorldPosition).sqrMagnitude < 0.00001f,
                    "Resource position does not match " + block.Coordinate);
                Resource.ResourceSaveState state = resource.CaptureState();
                Require(state.resourceCount == resource.ResourceCount && state.currentGauge == resource.CurrentGauge,
                    "Save state does not match the displayed count/gauge.");
                Require(IsFinite(resource.FocusPoint), "Resource focus point is invalid.");
                int partCount = 0;
                for (int i = 0; i < resource.BatchRenderEntryCount; i++)
                {
                    if (!resource.TryGetBatchRenderData(i, out Mesh mesh, out Material[] materials,
                            out Matrix4x4 matrix, out Vector3 position, out _, out _, out _, out _)) continue;
                    Require(mesh != null && materials.Length > 0 && IsFinite(position), "Resource render part is invalid.");
                    Require(IsFinite(matrix.MultiplyPoint3x4(mesh.bounds.center)), "Resource render matrix is invalid.");
                    partCount++;
                }
                if (resource.ResourceCount > 0 && (!(resource is ProjectF.MapObjects.TreeInstance tree) || tree.Growth > 0))
                    Require(partCount > 0, "Live resource has no render parts at " + block.Coordinate);
                renderedParts += partCount;
                resources++;
                units += resource.ResourceCount;
            }
            foreach (Collider collider in host.GetComponents<Collider>())
            {
                if (!collider.enabled) continue;
                ResourceInstance target = ResourceTypeWorld.ResolveColliderTarget(collider) as ResourceInstance;
                Require(target != null && target.IsRuntimeActive && Contains(host, target),
                    "Collider resolved the wrong resource identity.");
                colliders++;
            }
        }
        Debug.Log($"Shared resources validated: {hosts.Length} GameObjects, {resources} resources, "
                  + $"{units} remaining units, {renderedParts} render parts, {colliders} active colliders.");
    }

    private static bool Contains(ResourceTypeWorld host, ResourceInstance target)
    {
        foreach (ResourceInstance resource in host.Instances) if (ReferenceEquals(resource, target)) return true;
        return false;
    }

    private static bool IsFinite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
