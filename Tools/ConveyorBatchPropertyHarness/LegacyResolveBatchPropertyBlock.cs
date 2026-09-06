// Fixed pre-optimization oracle, captured on 2026-09-07.
using UnityEngine;
public sealed partial class LegacyVirtualRenderBatchCollection
{
    private MaterialPropertyBlock ResolveBatchPropertyBlock(
        VirtualRenderBatchKey key,
        BatchRenderCache batchCache,
        int startIndex,
        int instanceCount)
    {
        if (!key.HasUvScroll
            && !key.HasConveyorMotion
            && !key.UseSleepAwakeDarkTint
            && !key.UseBeltItemLineDebugColor)
        {
            return null;
        }

        if (batchCache.PropertyBlock == null)
        {
            batchCache.PropertyBlock = new MaterialPropertyBlock();
        }

        batchCache.PropertyBlock.Clear();
        if (key.HasUvScroll)
        {
            uvDrawScratch.Clear();
            int endIndex = Mathf.Min(
                batchCache.InstanceUvData != null ? batchCache.InstanceUvData.Count : 0,
                startIndex + instanceCount);
            for (int i = Mathf.Max(0, startIndex); i < endIndex; i++)
            {
                uvDrawScratch.Add(batchCache.InstanceUvData[i]);
            }

            batchCache.PropertyBlock.SetVectorArray(
                ConveyorUvDataShaderId,
                uvDrawScratch);
        }

        if (key.HasConveyorMotion)
        {
            CopyVectorDrawRange(
                batchCache.ConveyorMotionStarts,
                conveyorMotionStartDrawScratch,
                startIndex,
                instanceCount);
            CopyVectorDrawRange(
                batchCache.ConveyorMotionEnds,
                conveyorMotionEndDrawScratch,
                startIndex,
                instanceCount);
            batchCache.PropertyBlock.SetVectorArray(
                ConveyorMotionStartShaderId,
                conveyorMotionStartDrawScratch);
            batchCache.PropertyBlock.SetVectorArray(
                ConveyorMotionEndShaderId,
                conveyorMotionEndDrawScratch);
        }

        if (key.UseBeltItemLineDebugColor)
        {
            Color color = key.BeltItemLineDebugColor;
            if (key.UseSleepAwakeDarkTint)
            {
                color = SleepAwakeDebugVisual.Darken(color);
            }

            BeltItemLineDebugVisual.ApplySolidColor(batchCache.PropertyBlock, color);
        }
        else if (key.UseSleepAwakeDarkTint)
        {
            SleepAwakeDebugVisual.ApplySleepingColor(batchCache.PropertyBlock, key.Material);
        }

        return batchCache.PropertyBlock;
    }
}
