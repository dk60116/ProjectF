using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace ProjectF.Rendering
{
    internal sealed class ConveyorItemTransformJobProcessor : IDisposable
    {
        private NativeArray<TransformInput> inputs;
        private NativeArray<TransformOutput> outputs;
        private JobHandle scheduledHandle;
        private int resultCount;
        private bool hasScheduledJob;

        public bool ScheduleMatrices(
            List<VirtualConveyorItemRenderData> renderItems,
            bool useBurstJobs,
            int minimumJobItemCount,
            float batchCellSize)
        {
            CompleteScheduled();

            int itemCount = renderItems != null ? renderItems.Count : 0;
            resultCount = itemCount;
            if (itemCount <= 0)
            {
                return false;
            }

            float safeCellSize = Mathf.Max(1f, batchCellSize);
            EnsureOutputCapacity(itemCount);
            if (!useBurstJobs || itemCount < Mathf.Max(1, minimumJobItemCount))
            {
                BuildMatricesOnMainThread(renderItems, outputs, safeCellSize);
                return false;
            }

            EnsureInputCapacity(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                VirtualConveyorItemRenderData renderData = renderItems[i];
                inputs[i] = new TransformInput
                {
                    Position = renderData.Position,
                    Rotation = renderData.Rotation
                };
            }

            scheduledHandle = new BuildTransformMatricesJob
            {
                Inputs = inputs,
                Outputs = outputs,
                InverseBatchCellSize = 1f / safeCellSize
            }.Schedule(itemCount, 64);
            hasScheduledJob = true;
            return true;
        }

        public void CompleteScheduled()
        {
            if (!hasScheduledJob)
            {
                return;
            }

            try
            {
                scheduledHandle.Complete();
            }
            finally
            {
                hasScheduledJob = false;
                scheduledHandle = default;
            }
        }

        public bool TryGetResult(
            int index,
            out Matrix4x4 matrix,
            out int batchCellX,
            out int batchCellZ)
        {
            if (hasScheduledJob
                || !outputs.IsCreated
                || (uint)index >= (uint)resultCount)
            {
                matrix = default;
                batchCellX = 0;
                batchCellZ = 0;
                return false;
            }

            TransformOutput output = outputs[index];
            matrix = output.Matrix;
            batchCellX = output.BatchCellX;
            batchCellZ = output.BatchCellZ;
            return true;
        }

        public void Dispose()
        {
            CompleteScheduled();
            resultCount = 0;
            if (inputs.IsCreated)
            {
                inputs.Dispose();
            }

            if (outputs.IsCreated)
            {
                outputs.Dispose();
            }
        }

        private void EnsureInputCapacity(int requiredCapacity)
        {
            if (inputs.IsCreated && inputs.Length >= requiredCapacity)
            {
                return;
            }

            CompleteScheduled();
            if (inputs.IsCreated)
            {
                inputs.Dispose();
            }

            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, requiredCapacity));
            inputs = new NativeArray<TransformInput>(
                capacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private void EnsureOutputCapacity(int requiredCapacity)
        {
            if (outputs.IsCreated && outputs.Length >= requiredCapacity)
            {
                return;
            }

            CompleteScheduled();
            if (outputs.IsCreated)
            {
                outputs.Dispose();
            }

            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, requiredCapacity));
            outputs = new NativeArray<TransformOutput>(
                capacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private static void BuildMatricesOnMainThread(
            List<VirtualConveyorItemRenderData> renderItems,
            NativeArray<TransformOutput> targetOutputs,
            float batchCellSize)
        {
            for (int i = 0; i < renderItems.Count; i++)
            {
                VirtualConveyorItemRenderData renderData = renderItems[i];
                int cellX = Mathf.FloorToInt(renderData.Position.x / batchCellSize);
                int cellZ = Mathf.FloorToInt(renderData.Position.z / batchCellSize);
                targetOutputs[i] = new TransformOutput
                {
                    Matrix = float4x4.TRS(
                        renderData.Position,
                        renderData.Rotation,
                        new float3(1f)),
                    BatchCellX = cellX,
                    BatchCellZ = cellZ
                };
            }
        }

        private struct TransformInput
        {
            public float3 Position;
            public quaternion Rotation;
        }

        private struct TransformOutput
        {
            public float4x4 Matrix;
            public int BatchCellX;
            public int BatchCellZ;
        }

        [BurstCompile(
            FloatMode = FloatMode.Fast,
            FloatPrecision = FloatPrecision.Standard,
            OptimizeFor = OptimizeFor.Performance)]
        private struct BuildTransformMatricesJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<TransformInput> Inputs;

            [WriteOnly]
            public NativeArray<TransformOutput> Outputs;

            public float InverseBatchCellSize;

            public void Execute(int index)
            {
                TransformInput input = Inputs[index];
                Outputs[index] = new TransformOutput
                {
                    Matrix = float4x4.TRS(input.Position, input.Rotation, new float3(1f)),
                    BatchCellX = (int)math.floor(input.Position.x * InverseBatchCellSize),
                    BatchCellZ = (int)math.floor(input.Position.z * InverseBatchCellSize)
                };
            }
        }
    }
}
