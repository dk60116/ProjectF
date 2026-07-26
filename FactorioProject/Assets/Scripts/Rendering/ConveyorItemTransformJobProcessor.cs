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

        public bool BuildMatrices(
            List<VirtualConveyorItemRenderData> renderItems,
            bool useBurstJobs,
            int minimumJobItemCount,
            float batchCellSize)
        {
            int itemCount = renderItems != null ? renderItems.Count : 0;
            if (itemCount <= 0)
            {
                return false;
            }

            float safeCellSize = Mathf.Max(1f, batchCellSize);
            if (!useBurstJobs || itemCount < Mathf.Max(1, minimumJobItemCount))
            {
                BuildMatricesOnMainThread(renderItems, safeCellSize);
                return false;
            }

            EnsureCapacity(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                VirtualConveyorItemRenderData renderData = renderItems[i];
                inputs[i] = new TransformInput
                {
                    Position = renderData.Position,
                    Rotation = renderData.Rotation
                };
            }

            JobHandle handle = new BuildTransformMatricesJob
            {
                Inputs = inputs,
                Outputs = outputs,
                InverseBatchCellSize = 1f / safeCellSize
            }.Schedule(itemCount, 64);
            handle.Complete();

            for (int i = 0; i < itemCount; i++)
            {
                TransformOutput output = outputs[i];
                renderItems[i] = renderItems[i].WithResolvedTransform(
                    output.Matrix,
                    output.BatchCellX,
                    output.BatchCellZ);
            }

            return true;
        }

        public void Dispose()
        {
            if (inputs.IsCreated)
            {
                inputs.Dispose();
            }

            if (outputs.IsCreated)
            {
                outputs.Dispose();
            }
        }

        private void EnsureCapacity(int requiredCapacity)
        {
            if (inputs.IsCreated && inputs.Length >= requiredCapacity)
            {
                return;
            }

            Dispose();
            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, requiredCapacity));
            inputs = new NativeArray<TransformInput>(
                capacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            outputs = new NativeArray<TransformOutput>(
                capacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private static void BuildMatricesOnMainThread(
            List<VirtualConveyorItemRenderData> renderItems,
            float batchCellSize)
        {
            for (int i = 0; i < renderItems.Count; i++)
            {
                VirtualConveyorItemRenderData renderData = renderItems[i];
                int cellX = Mathf.FloorToInt(renderData.Position.x / batchCellSize);
                int cellZ = Mathf.FloorToInt(renderData.Position.z / batchCellSize);
                renderItems[i] = renderData.WithResolvedTransform(
                    Matrix4x4.TRS(renderData.Position, renderData.Rotation, Vector3.one),
                    cellX,
                    cellZ);
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
