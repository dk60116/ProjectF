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
        public int NativePathItemCount { get; private set; }

        public bool ScheduleMatrices(
            List<VirtualConveyorItemRenderData> renderItems,
            bool useBurstJobs,
            int minimumJobItemCount,
            float batchCellSize)
        {
            CompleteScheduled();

            int itemCount = renderItems != null ? renderItems.Count : 0;
            resultCount = itemCount;
            NativePathItemCount = 0;
            if (itemCount <= 0)
            {
                return false;
            }

            float safeCellSize = Mathf.Max(1f, batchCellSize);
            EnsureOutputCapacity(itemCount);
            EnsureInputCapacity(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                VirtualConveyorItemRenderData renderData = renderItems[i];
                if (renderData.VisualPath.Kind != 0) NativePathItemCount++;
                inputs[i] = new TransformInput
                {
                    Position = renderData.Position,
                    Rotation = renderData.Rotation,
                    Path = renderData.VisualPath,
                    Progress = renderData.VisualProgress
                };
            }

            var job = new BuildTransformMatricesJob
            {
                Inputs = inputs,
                Outputs = outputs,
                InverseBatchCellSize = 1f / safeCellSize
            };
            if (!useBurstJobs || itemCount < Mathf.Max(1, minimumJobItemCount))
            {
                for (int i = 0; i < itemCount; i++) job.Execute(i);
                return false;
            }
            scheduledHandle = job.Schedule(itemCount, 64);
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
            NativePathItemCount = 0;
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

        private struct TransformInput
        {
            public float3 Position;
            public quaternion Rotation;
            public BeltItemVisualPath Path;
            public float Progress;
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
                float3 position = input.Path.Kind != 0 ? (float3)input.Path.Evaluate(input.Progress) : input.Position;
                Outputs[index] = new TransformOutput
                {
                    Matrix = float4x4.TRS(position, input.Rotation, new float3(1f)),
                    BatchCellX = (int)math.floor(position.x * InverseBatchCellSize),
                    BatchCellZ = (int)math.floor(position.z * InverseBatchCellSize)
                };
            }
        }
    }
}
