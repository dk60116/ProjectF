using System;
using Unity.Collections;

namespace ProjectF.Fluids
{
    internal sealed class FluidSimulationBuffers : IDisposable
    {
        internal NativeArray<FluidNetworkRange> Networks;
        internal NativeArray<FluidPipeTopology> Pipes;
        internal NativeArray<FluidPipeState> States;
        internal NativeArray<FluidPipeCoordinate> Coordinates;
        internal NativeArray<int> Edges;
        internal NativeArray<ulong> Checksums;

        internal FluidSimulationBuffers(
            int networkCount,
            int pipeCount,
            int coordinateCount,
            int edgeCount)
        {
            try
            {
                Networks = Allocate<FluidNetworkRange>(networkCount);
                Pipes = Allocate<FluidPipeTopology>(pipeCount);
                States = Allocate<FluidPipeState>(pipeCount);
                Coordinates = Allocate<FluidPipeCoordinate>(coordinateCount);
                Edges = Allocate<int>(edgeCount);
                Checksums = Allocate<ulong>(networkCount);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal FluidTopologyShadowJob ShadowJob => new FluidTopologyShadowJob
        {
            Networks = Networks,
            Pipes = Pipes,
            States = States,
            Coordinates = Coordinates,
            Edges = Edges,
            Checksums = Checksums
        };

        public void Dispose()
        {
            Release(ref Networks);
            Release(ref Pipes);
            Release(ref States);
            Release(ref Coordinates);
            Release(ref Edges);
            Release(ref Checksums);
        }

        private static NativeArray<T> Allocate<T>(int count) where T : struct
        {
            return new NativeArray<T>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        private static void Release<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }

            array = default;
        }
    }
}
