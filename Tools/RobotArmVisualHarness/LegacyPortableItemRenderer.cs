// Fixed pre-optimization oracle, captured on 2026-09-07.
using UnityEngine;
using UnityEngine.Rendering;
public partial class LegacyPortableItemRenderer
{
    private void RebuildPortableObjectBatches()
    {
        portableObjectBatches.ClearActiveMatrices();
        portableObjectCleanupBuffer.Clear();

        foreach (PortableObject portableObject in registeredPortableObjects)
        {
            if (portableObject == null)
            {
                portableObjectCleanupBuffer.Add(portableObject);
                continue;
            }

            if (!portableObject.TryGetBatchRenderData(
                    out int itemId,
                    out Mesh mesh,
                    out Material material,
                    out Matrix4x4 localToWorldMatrix,
                    out Vector3 worldPosition,
                    out int layer,
                    out ShadowCastingMode shadowCastingMode,
                    out bool receiveShadows,
                    out bool useSleepAwakeDarkTint,
                    out bool useBeltItemLineDebugColor,
                    out Color32 beltItemLineDebugColor))
            {
                continue;
            }

            if (material != null && !material.enableInstancing)
            {
                material.enableInstancing = true;
            }

            int cellX = Mathf.FloorToInt(worldPosition.x / portableObjectBatchCellSize);
            int cellZ = Mathf.FloorToInt(worldPosition.z / portableObjectBatchCellSize);
            VirtualRenderBatchKey key = new VirtualRenderBatchKey(
                mesh,
                material,
                layer,
                0,
                shadowCastingMode,
                receiveShadows,
                false,
                useSleepAwakeDarkTint,
                useBeltItemLineDebugColor,
                beltItemLineDebugColor,
                itemId,
                cellX,
                cellZ);
            portableObjectBatches.AddMatrix(key, localToWorldMatrix);
        }

        for (int i = 0; i < portableObjectCleanupBuffer.Count; i++)
        {
            registeredPortableObjects.Remove(portableObjectCleanupBuffer[i]);
        }
    }
}
