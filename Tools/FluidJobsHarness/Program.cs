using ProjectF.Fluids;

int checks = 0;
void Require(bool condition, string message)
{
    checks++;
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

FluidSimulationBuffers CreateFixture()
{
    FluidSimulationBuffers buffers = new FluidSimulationBuffers(3, 6, 7, 6);
    buffers.Networks[0] = new FluidNetworkRange { PipeStart = 0, PipeCount = 3 };
    buffers.Networks[1] = new FluidNetworkRange { PipeStart = 3, PipeCount = 2 };
    buffers.Networks[2] = new FluidNetworkRange { PipeStart = 5, PipeCount = 1 };

    long[] ids = { 10, 11, 12, 30, 31, 90 };
    int[] coordinateCounts = { 1, 1, 2, 1, 1, 1 };
    int[] edgeStarts = { 0, 1, 3, 4, 5, 6 };
    int[] edgeCounts = { 1, 2, 1, 1, 1, 0 };
    int coordinateStart = 0;
    for (int i = 0; i < ids.Length; i++)
    {
        buffers.Pipes[i] = new FluidPipeTopology
        {
            SimulationId = ids[i],
            ItemId = 500 + i,
            QuarterTurns = i & 3,
            IsUnderground = coordinateCounts[i] == 2 ? 1 : 0,
            CoordinateStart = coordinateStart,
            CoordinateCount = coordinateCounts[i],
            EdgeStart = edgeStarts[i],
            EdgeCount = edgeCounts[i]
        };
        buffers.States[i] = new FluidPipeState { DisplayedFluidItemId = i % 2 == 0 ? 7 : -1 };
        coordinateStart += coordinateCounts[i];
    }

    FluidPipeCoordinate[] coordinates =
    {
        new() { X = 0, Z = 0 },
        new() { X = 1, Z = 0 },
        new() { X = 2, Z = 0 },
        new() { X = 8, Z = 0 },
        new() { X = 20, Z = 5 },
        new() { X = 21, Z = 5 },
        new() { X = -10, Z = -10 }
    };
    for (int i = 0; i < coordinates.Length; i++)
    {
        buffers.Coordinates[i] = coordinates[i];
    }

    int[] edges = { 1, 0, 2, 1, 4, 3 };
    for (int i = 0; i < edges.Length; i++)
    {
        buffers.Edges[i] = edges[i];
    }

    return buffers;
}

ulong[] Execute(FluidSimulationBuffers buffers, bool parallel, bool reverse)
{
    FluidTopologyShadowJob job = buffers.ShadowJob;
    if (parallel)
    {
        Parallel.For(0, buffers.Networks.Length, job.Execute);
    }
    else
    {
        for (int i = 0; i < buffers.Networks.Length; i++)
        {
            int index = reverse ? buffers.Networks.Length - i - 1 : i;
            job.Execute(index);
        }
    }

    ulong[] result = new ulong[buffers.Checksums.Length];
    for (int i = 0; i < result.Length; i++)
    {
        result[i] = buffers.Checksums[i];
    }

    return result;
}

using (FluidSimulationBuffers serial = CreateFixture())
using (FluidSimulationBuffers parallel = CreateFixture())
using (FluidSimulationBuffers reverse = CreateFixture())
{
    ulong[] serialChecksums = Execute(serial, false, false);
    ulong[] parallelChecksums = Execute(parallel, true, false);
    ulong[] reverseChecksums = Execute(reverse, false, true);
    Require(serialChecksums.SequenceEqual(parallelChecksums), "parallel worker order changed a network checksum");
    Require(serialChecksums.SequenceEqual(reverseChecksums), "reverse network traversal changed a network checksum");

    FluidPipeState changedState = parallel.States[1];
    changedState.DisplayedFluidItemId = 99;
    parallel.States[1] = changedState;
    ulong[] stateChanged = Execute(parallel, true, false);
    Require(stateChanged[0] != serialChecksums[0], "fluid state change was missing from its network checksum");
    Require(stateChanged[1] == serialChecksums[1] && stateChanged[2] == serialChecksums[2],
        "fluid state change leaked into an independent network");

    FluidPipeCoordinate changedCoordinate = reverse.Coordinates[3];
    changedCoordinate.X++;
    reverse.Coordinates[3] = changedCoordinate;
    ulong[] topologyChanged = Execute(reverse, false, false);
    Require(topologyChanged[0] != serialChecksums[0], "underground endpoint change was missing from the checksum");
    Require(topologyChanged[1] == serialChecksums[1] && topologyChanged[2] == serialChecksums[2],
        "topology change leaked into an independent network");
}

FluidSimulationBuffers disposed = CreateFixture();
disposed.Dispose();
Require(!disposed.Networks.IsCreated
        && !disposed.Pipes.IsCreated
        && !disposed.States.IsCreated
        && !disposed.Coordinates.IsCreated
        && !disposed.Edges.IsCreated
        && !disposed.Checksums.IsCreated,
    "persistent native buffers were not fully released");

Console.WriteLine($"Fluid jobs harness passed {checks} checks.");
