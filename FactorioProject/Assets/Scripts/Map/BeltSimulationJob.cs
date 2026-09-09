using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace ProjectF.Conveyors
{
    // The simulation clock and all movement/arbitration arithmetic are integers.
    // Floating-point fields below are opaque presentation/pickup payloads, never routing inputs.
    [System.Serializable]
    public struct BeltLaneState
    {
        public int ItemId, Origin, GateBits;
        public long Remaining, Duration;
        public float StartX, StartY, StartZ, DropX, DropY, DropZ, ExitRadius;
        public static BeltLaneState Empty => new BeltLaneState { ItemId = -1, Origin = -1 };
    }

    public struct BeltLaneTopology
    {
        public int Target, Alternate, Splitter, SplitterInput;
        public long Duration, AlternateDuration;
        public int Paused;
    }

    public struct BeltGroupRange
    {
        public int Start, Count, SplitterStart, SplitterCount, MaxWaves;
    }

    public struct BeltGroupState
    {
        public int Sleeping, ChangedCount, Moves, Waves;
    }

    public struct BeltSplitterState
    {
        public int LeftInput, RightInput, LeftOutput, RightOutput;
        public int NextInput, NextOutput, FilterOutput, FilterStart, FilterWords;
        public int WheelMask;
    }

    [BurstCompile]
    public struct BeltSimulationJob : IJobParallelFor
    {
        public const int TickRate = 60;
        public const long TickUnits = 65536;
        public const int SettledGateBit = 8;
        [ReadOnly] public NativeArray<BeltLaneTopology> Topology;
        [ReadOnly] public NativeArray<BeltGroupRange> Groups;
        [ReadOnly] public NativeArray<ulong> FilterBits;

        // Every group owns a disjoint, validated contiguous interval in these arrays.
        // No worker reads or writes another group's slots or splitter state.
        [NativeDisableParallelForRestriction] public NativeArray<BeltLaneState> Lanes;
        [NativeDisableParallelForRestriction] public NativeArray<BeltLaneState> Transfers;
        [NativeDisableParallelForRestriction] public NativeArray<BeltSplitterState> Splitters;
        public NativeArray<BeltGroupState> GroupStates;
        [NativeDisableParallelForRestriction] public NativeArray<int> Targets;
        [NativeDisableParallelForRestriction] public NativeArray<int> Incoming;
        [NativeDisableParallelForRestriction] public NativeArray<int> Resolution;
        [NativeDisableParallelForRestriction] public NativeArray<int> Stack;
        [NativeDisableParallelForRestriction] public NativeArray<int> MergeCursor;
        [NativeDisableParallelForRestriction] public NativeArray<int> Changed;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Touched;

        public void Execute(int index)
        {
            BeltGroupRange group = Groups[index];
            BeltGroupState status = GroupStates[index];
            status.ChangedCount = status.Moves = status.Waves = 0;
            if (status.Sleeping != 0) { GroupStates[index] = status; return; }
            int end = group.Start + group.Count;
            for (int i = group.Start; i < end; i++)
            {
                Touched[i] = 0;
                BeltLaneState item = Lanes[i];
                if (item.ItemId < 0 || (Topology[i].Paused != 0 && item.Origin >= 0)) continue;
                long before = item.Remaining;
                item.Remaining -= TickUnits;
                if (before > 0 && item.Remaining <= 0)
                {
                    item.GateBits |= SettledGateBit;
                    Touch(i, group.Start, ref status);
                }
                Lanes[i] = item;
            }

            for (int wave = 0; wave < group.MaxWaves; wave++)
            {
                status.Waves++;
                for (int i = group.Start; i < end; i++)
                {
                    Targets[i] = Incoming[i] = -1;
                    Resolution[i] = 0;
                }
                for (int i = group.Start; i < end; i++)
                {
                    BeltLaneTopology route = Topology[i];
                    if (!Ready(i) || route.Splitter >= 0) continue;
                    if (route.Target >= 0 && route.Duration > 0) Propose(i, route.Target, group);
                }
                for (int s = group.SplitterStart; s < group.SplitterStart + group.SplitterCount; s++)
                    ProposeSplitter(s, group);

                // One winner per destination. Losing merge inputs wait without banking time.
                for (int i = group.Start; i < end; i++)
                    if (Targets[i] >= 0 && Incoming[Targets[i]] != i) Targets[i] = -1;

                ResolveTransfers(group);
                // A settled output can still be blocked further downstream. Redirect a blocked
                // splitter input to a proven free output before putting the group to sleep.
                // Successful reservations are never displaced, so each repair adds progress.
                for (int repair = 0; repair < group.SplitterCount * 2; repair++)
                {
                    if (!RepairBlockedSplitterOutputs(group)) break;
                    ResolveTransfers(group);
                }

                int moved = 0;
                for (int source = group.Start; source < end; source++)
                {
                    if (Resolution[source] != 2) continue;
                    int target = Targets[source];
                    BeltLaneState item = Lanes[source];
                    BeltLaneTopology route = Topology[source];
                    long duration = target == route.Alternate ? route.AlternateDuration : route.Duration;
                    item.Origin = source;
                    item.GateBits &= ~64;
                    item.Duration = duration;
                    item.Remaining = duration + item.Remaining;
                    Transfers[target] = item;
                    moved++;
                }
                if (moved == 0) break;
                for (int source = group.Start; source < end; source++)
                {
                    if (Resolution[source] != 2) continue;
                    Lanes[source] = BeltLaneState.Empty;
                    Touch(source, group.Start, ref status);
                }
                for (int source = group.Start; source < end; source++)
                {
                    if (Resolution[source] != 2) continue;
                    int target = Targets[source];
                    Lanes[target] = Transfers[target];
                    MergeCursor[target] = source + 1 < end ? source + 1 : group.Start;
                    Touch(target, group.Start, ref status);
                }
                // Commit splitter arbitration in its preferred input order, independent of node order.
                for (int s = group.SplitterStart; s < group.SplitterStart + group.SplitterCount; s++)
                {
                    BeltSplitterState splitter = Splitters[s];
                    int first = splitter.NextInput & 1;
                    for (int j = 0; j < 2; j++)
                    {
                        int input = first ^ j;
                        int source = input == 0 ? splitter.LeftInput : splitter.RightInput;
                        if (source < 0 || Resolution[source] != 2) continue;
                        int output = Targets[source] == splitter.LeftOutput ? 0 : 1;
                        splitter.NextInput = 1 - input;
                        splitter.NextOutput = 1 - output;
                        int bit = 1 << input;
                        splitter.WheelMask = input == output ? splitter.WheelMask | bit : splitter.WheelMask & ~bit;
                    }
                    Splitters[s] = splitter;
                }
                status.Moves += moved;
            }

            status.Sleeping = 1;
            for (int i = group.Start; i < end; i++)
            {
                BeltLaneState item = Lanes[i];
                if (item.ItemId < 0) continue;
                if (item.Remaining < 0) { item.Remaining = 0; Lanes[i] = item; }
                if (item.Remaining > 0 && (Topology[i].Paused == 0 || item.Origin < 0)) status.Sleeping = 0;
            }
            // A move may have vacated an output for another input whose proposal lost this tick.
            if (status.Moves > 0) status.Sleeping = 0;
            GroupStates[index] = status;
        }

        private bool Ready(int i) => i >= 0 && Lanes[i].ItemId >= 0
            && Lanes[i].Remaining <= 0 && Topology[i].Paused == 0;

        private void ResolveTransfers(BeltGroupRange group)
        {
            int end = group.Start + group.Count;
            for (int i = group.Start; i < end; i++) Resolution[i] = 0;
            // Empty-ended chains and full closed cycles move simultaneously, in linear time.
            for (int seed = group.Start; seed < end; seed++)
            {
                if (Resolution[seed] != 0) continue;
                int count = 0, current = seed, result;
                while (true)
                {
                    int resolved = Resolution[current];
                    if (resolved != 0) { result = resolved == 1 ? 2 : resolved; break; }
                    if (Targets[current] < 0) { Resolution[current] = 3; result = 3; break; }
                    Resolution[current] = 1;
                    Stack[group.Start + count++] = current;
                    int target = Targets[current];
                    if (Lanes[target].ItemId < 0) { result = 2; break; }
                    current = target;
                }
                while (count > 0) Resolution[Stack[group.Start + --count]] = result;
            }
        }

        private bool RepairBlockedSplitterOutputs(BeltGroupRange group)
        {
            bool changed = false;
            for (int s = group.SplitterStart; s < group.SplitterStart + group.SplitterCount; s++)
            {
                BeltSplitterState splitter = Splitters[s];
                for (int j = 0; j < 2; j++)
                {
                    int input = (splitter.NextInput & 1) ^ j;
                    int source = input == 0 ? splitter.LeftInput : splitter.RightInput;
                    if (!Ready(source) || Resolution[source] == 2) continue;
                    int allowed = Allowed(splitter, source);
                    for (int k = 0; k < 2; k++)
                    {
                        int output = (splitter.NextOutput & 1) ^ k;
                        int target = output == 0 ? splitter.LeftOutput : splitter.RightOutput;
                        long duration = output == 0 ? Topology[source].Duration : Topology[source].AlternateDuration;
                        if ((allowed & (1 << output)) == 0 || target < 0 || duration <= 0
                            || Incoming[target] >= 0 || (Lanes[target].ItemId >= 0 && Resolution[target] != 2)) continue;
                        int previous = Targets[source];
                        if (previous >= 0 && Incoming[previous] == source) Incoming[previous] = -1;
                        Propose(source, target, group);
                        changed = true;
                        break;
                    }
                }
            }
            return changed;
        }

        private void Touch(int slot, int start, ref BeltGroupState status)
        {
            if (Touched[slot] != 0) return;
            Touched[slot] = 1;
            Changed[start + status.ChangedCount++] = slot;
        }

        private void Propose(int source, int target, BeltGroupRange group)
        {
            // The bake validates edges. Keep malformed/unloaded targets inert as a second boundary.
            if (target < group.Start || target >= group.Start + group.Count) return;
            Targets[source] = target;
            int winner = Incoming[target];
            int cursor = MergeCursor[target];
            int rank = source >= cursor ? source - cursor : source - cursor + group.Count;
            int winnerRank = winner >= cursor ? winner - cursor : winner - cursor + group.Count;
            if (winner < 0 || rank < winnerRank) Incoming[target] = source;
        }

        private int Allowed(BeltSplitterState splitter, int input)
        {
            if (!Ready(input)) return 0;
            if (splitter.FilterOutput == 0) return 3;
            int item = Lanes[input].ItemId;
            int word = item >> 6;
            bool selected = item >= 0 && word < splitter.FilterWords
                && (FilterBits[splitter.FilterStart + word] & (1UL << (item & 63))) != 0;
            int filteredOutput = splitter.FilterOutput - 1;
            return 1 << (selected ? filteredOutput : 1 - filteredOutput);
        }

        private bool OutputAvailable(int target) => target >= 0
            && (Lanes[target].ItemId < 0 || Ready(target));

        private void ProposeSplitter(int index, BeltGroupRange group)
        {
            BeltSplitterState splitter = Splitters[index];
            int available = (OutputAvailable(splitter.LeftOutput) ? 1 : 0)
                | (OutputAvailable(splitter.RightOutput) ? 2 : 0);
            int first = splitter.NextInput & 1, preferredOutput = splitter.NextOutput & 1;
            for (int j = 0; j < 2; j++)
            {
                int input = first ^ j;
                int source = input == 0 ? splitter.LeftInput : splitter.RightInput;
                int allowed = Allowed(splitter, source) & available;
                if (allowed == 0 || Topology[source].Duration <= 0) continue;
                int output = (allowed & (1 << preferredOutput)) != 0 ? preferredOutput : 1 - preferredOutput;
                Propose(source, output == 0 ? splitter.LeftOutput : splitter.RightOutput, group);
                available &= ~(1 << output);
                preferredOutput = 1 - output;
            }
        }
    }
}
