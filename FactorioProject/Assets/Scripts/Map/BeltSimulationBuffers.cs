using System;
using Unity.Collections;

namespace ProjectF.Conveyors
{
    internal sealed class BeltSimulationBuffers : IDisposable
    {
        internal NativeArray<BeltLaneTopology> Topology;
        internal NativeArray<BeltLaneState> Lanes, Transfers;
        internal NativeArray<BeltGroupRange> Groups;
        internal NativeArray<BeltGroupState> GroupStates;
        internal NativeArray<BeltSplitterState> Splitters;
        internal NativeArray<ulong> FilterBits;
        internal NativeArray<int> Targets, Incoming, Resolution, Stack, MergeCursor, Changed;
        internal NativeArray<byte> Touched;

        internal BeltSimulationBuffers(int lanes, int groups, int splitters, int words)
        {
            try
            {
                Topology = Allocate<BeltLaneTopology>(lanes);
                Lanes = Allocate<BeltLaneState>(lanes); Transfers = Allocate<BeltLaneState>(lanes);
                Groups = Allocate<BeltGroupRange>(groups); GroupStates = Allocate<BeltGroupState>(groups);
                Splitters = Allocate<BeltSplitterState>(splitters); FilterBits = Allocate<ulong>(words);
                Targets = Allocate<int>(lanes); Incoming = Allocate<int>(lanes); Resolution = Allocate<int>(lanes);
                Stack = Allocate<int>(lanes); MergeCursor = Allocate<int>(lanes); Changed = Allocate<int>(lanes);
                Touched = Allocate<byte>(lanes);
            }
            catch { Dispose(); throw; }
        }

        private static NativeArray<T> Allocate<T>(int count) where T : struct
            => new NativeArray<T>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        internal BeltSimulationJob Job => new BeltSimulationJob
        {
            Topology = Topology, Lanes = Lanes, Transfers = Transfers, Groups = Groups, GroupStates = GroupStates,
            Splitters = Splitters, FilterBits = FilterBits, Targets = Targets, Incoming = Incoming,
            Resolution = Resolution, Stack = Stack, MergeCursor = MergeCursor, Changed = Changed, Touched = Touched
        };

        public void Dispose()
        {
            Release(ref Topology); Release(ref Lanes); Release(ref Transfers); Release(ref Groups);
            Release(ref GroupStates); Release(ref Splitters); Release(ref FilterBits);
            Release(ref Targets); Release(ref Incoming); Release(ref Resolution); Release(ref Stack);
            Release(ref MergeCursor); Release(ref Changed); Release(ref Touched);
        }

        private static void Release<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated) array.Dispose();
            array = default;
        }
    }
}
