using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace ProjectF.Fluids
{
    public struct FluidNetworkRange
    {
        public int PipeStart;
        public int PipeCount;
    }

    public struct FluidPipeCoordinate
    {
        public int X;
        public int Z;
    }

    public struct FluidPipeTopology
    {
        public long SimulationId;
        public int ItemId;
        public int QuarterTurns;
        public int IsUnderground;
        public int CoordinateStart;
        public int CoordinateCount;
        public int EdgeStart;
        public int EdgeCount;
    }

    public struct FluidPipeState
    {
        public int DisplayedFluidItemId;
    }

    /// <summary>
    /// First-stage Burst kernel for the fluid simulation migration. Each iteration owns one
    /// connected pipe network and produces a deterministic checksum of its native mirror.
    /// No Unity object or managed collection crosses the job boundary.
    /// </summary>
    [BurstCompile]
    public struct FluidTopologyShadowJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<FluidNetworkRange> Networks;
        [ReadOnly] public NativeArray<FluidPipeTopology> Pipes;
        [ReadOnly] public NativeArray<FluidPipeState> States;
        [ReadOnly] public NativeArray<FluidPipeCoordinate> Coordinates;
        [ReadOnly] public NativeArray<int> Edges;
        public NativeArray<ulong> Checksums;

        public void Execute(int index)
        {
            FluidNetworkRange network = Networks[index];
            ulong hash = 14695981039346656037UL;
            Mix(ref hash, network.PipeCount);

            int end = network.PipeStart + network.PipeCount;
            for (int pipeIndex = network.PipeStart; pipeIndex < end; pipeIndex++)
            {
                FluidPipeTopology pipe = Pipes[pipeIndex];
                FluidPipeState state = States[pipeIndex];
                Mix(ref hash, pipe.SimulationId);
                Mix(ref hash, pipe.ItemId);
                Mix(ref hash, pipe.QuarterTurns);
                Mix(ref hash, pipe.IsUnderground);
                Mix(ref hash, state.DisplayedFluidItemId);
                Mix(ref hash, pipe.CoordinateCount);

                int coordinateEnd = pipe.CoordinateStart + pipe.CoordinateCount;
                for (int coordinateIndex = pipe.CoordinateStart;
                     coordinateIndex < coordinateEnd;
                     coordinateIndex++)
                {
                    FluidPipeCoordinate coordinate = Coordinates[coordinateIndex];
                    Mix(ref hash, coordinate.X);
                    Mix(ref hash, coordinate.Z);
                }

                Mix(ref hash, pipe.EdgeCount);
                int edgeEnd = pipe.EdgeStart + pipe.EdgeCount;
                for (int edgeIndex = pipe.EdgeStart; edgeIndex < edgeEnd; edgeIndex++)
                {
                    int neighborIndex = Edges[edgeIndex];
                    // Hash the stable identity rather than the native-array index. Adding an
                    // unrelated network therefore cannot change this network's checksum.
                    Mix(ref hash, Pipes[neighborIndex].SimulationId);
                }
            }

            Checksums[index] = hash;
        }

        private static void Mix(ref ulong hash, long value)
        {
            unchecked
            {
                hash = (hash ^ (ulong)value) * 1099511628211UL;
            }
        }
    }
}
