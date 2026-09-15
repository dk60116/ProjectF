using System;
using System.Collections.Generic;
using Unity.Jobs;

namespace ProjectF.Conveyors
{
    /// <summary>Stable lane identity. Scene/Block instances are never part of simulation identity.</summary>
    public readonly struct BeltLaneId : IEquatable<BeltLaneId>
    {
        public readonly int X, Y, Lane;
        public BeltLaneId(int x, int y, int lane) { X = x; Y = y; Lane = lane; }
        public bool Equals(BeltLaneId other) => X == other.X && Y == other.Y && Lane == other.Lane;
        public override bool Equals(object obj) => obj is BeltLaneId other && Equals(other);
        public override int GetHashCode() { unchecked { return ((X * 397) ^ Y) * 397 ^ Lane; } }
    }

    /// <summary>
    /// Owns authoritative transport buffers and fixed-tick execution. Topology and IO are
    /// supplied as data; a Block adapter may publish mirrors, but is not needed to Step.
    /// </summary>
    internal sealed class BeltSimulationWorld : IDisposable
    {
        private readonly Dictionary<BeltLaneId, int> indices = new Dictionary<BeltLaneId, int>();
        private readonly List<BeltLaneId> laneIds = new List<BeltLaneId>();
        private JobHandle pending;
        private bool scheduled;
        private bool stepOpen;
        internal BeltSimulationBuffers Buffers { get; private set; }
        internal long Tick { get; private set; }
        internal int LaneCount => laneIds.Count;

        internal void Allocate(int lanes, int groups, int splitters, int words)
        {
            if (stepOpen) throw new InvalidOperationException("Cannot replace topology during a transport step.");
            Complete();
            var replacement = new BeltSimulationBuffers(lanes, groups, splitters, words);
            Buffers?.Dispose();
            Buffers = replacement;
            indices.Clear(); laneIds.Clear();
        }

        internal void AddLane(BeltLaneId id)
        {
            if (Buffers == null || laneIds.Count >= Buffers.Lanes.Length)
                throw new InvalidOperationException("Lane count exceeds allocated topology.");
            indices.Add(id, laneIds.Count);
            laneIds.Add(id);
        }

        internal void ValidateTopology()
        {
            if (Buffers == null || laneIds.Count != Buffers.Lanes.Length)
                throw new InvalidOperationException("Incomplete lane identities.");
            int expectedStart = 0, expectedSplitterStart = 0;
            for (int g = 0; g < Buffers.Groups.Length; g++)
            {
                BeltGroupRange group = Buffers.Groups[g];
                int end = checked(group.Start + group.Count);
                if (group.Start != expectedStart || group.Count <= 0 || end > LaneCount || group.MaxWaves < 1)
                    throw new InvalidOperationException("Transport groups must own disjoint contiguous lanes.");
                if (group.SplitterStart != expectedSplitterStart || group.SplitterCount < 0
                    || (long)group.SplitterStart + group.SplitterCount > Buffers.Splitters.Length)
                    throw new InvalidOperationException("Invalid splitter group range.");
                int splitterEnd = group.SplitterStart + group.SplitterCount;
                for (int s = group.SplitterStart; s < splitterEnd; s++)
                {
                    BeltSplitterState splitter = Buffers.Splitters[s];
                    if (!InGroupOrAbsent(splitter.LeftInput, group.Start, end)
                        || !InGroupOrAbsent(splitter.RightInput, group.Start, end)
                        || !InGroupOrAbsent(splitter.LeftOutput, group.Start, end)
                        || !InGroupOrAbsent(splitter.RightOutput, group.Start, end))
                        throw new InvalidOperationException("Splitter endpoint crosses worker ownership.");
                    if (splitter.FilterStart < 0 || splitter.FilterWords < 0
                        || (long)splitter.FilterStart + splitter.FilterWords > Buffers.FilterBits.Length)
                        throw new InvalidOperationException("Invalid splitter filter range.");
                }
                for (int i = group.Start; i < end; i++)
                {
                    BeltLaneTopology route = Buffers.Topology[i];
                    if (!InGroupOrAbsent(route.Target, group.Start, end)
                        || !InGroupOrAbsent(route.Alternate, group.Start, end))
                        throw new InvalidOperationException("Transport edge crosses worker ownership.");
                    if (route.Splitter != -1 && (route.Splitter < group.SplitterStart
                        || route.Splitter >= group.SplitterStart + group.SplitterCount))
                        throw new InvalidOperationException("Splitter crosses worker ownership.");
                }
                expectedStart = end;
                expectedSplitterStart = splitterEnd;
            }
            if (expectedStart != LaneCount) throw new InvalidOperationException("Unowned transport lanes.");
            if (expectedSplitterStart != Buffers.Splitters.Length) throw new InvalidOperationException("Unowned splitters.");
        }

        private static bool InGroupOrAbsent(int index, int start, int end)
            => index == -1 || index >= start && index < end;

        internal bool TryGetIndex(BeltLaneId id, out int index) => indices.TryGetValue(id, out index);
        internal BeltLaneId GetLaneId(int index) => laneIds[index];

        internal void Schedule()
        {
            if (stepOpen) throw new InvalidOperationException("Transport step already scheduled.");
            pending = Buffers != null && Buffers.Groups.Length > 0
                ? Buffers.Job.Schedule(Buffers.Groups.Length, 1) : default;
            scheduled = true;
            stepOpen = true;
        }

        internal void Complete()
        {
            if (!scheduled) return;
            pending.Complete(); scheduled = false;
        }

        internal void CommitStep()
        {
            Complete();
            if (!stepOpen) return;
            stepOpen = false;
            Tick++;
        }
        internal void Step() { Schedule(); CommitStep(); }
        internal void RestoreTick(long tick)
        {
            if (stepOpen) throw new InvalidOperationException("Cannot restore the clock during a transport step.");
            Tick = Math.Max(0, tick);
        }

        internal bool TryRead(BeltLaneId id, out BeltLaneState state)
        {
            Complete();
            state = BeltLaneState.Empty;
            if (Buffers == null || !indices.TryGetValue(id, out int index)) return false;
            state = Buffers.Lanes[index];
            return true;
        }

        internal BeltSavedLane CaptureLane(BeltLaneId id)
        {
            RequireCommittedStep();
            Complete();
            if (!indices.TryGetValue(id, out int index)) return null;
            var saved = new BeltSavedLane { X = id.X, Y = id.Y, Lane = id.Lane, State = Buffers.Lanes[index] };
            if (saved.State.Origin >= 0 && saved.State.Origin < LaneCount)
            {
                BeltLaneId origin = laneIds[saved.State.Origin];
                saved.OriginX = origin.X; saved.OriginY = origin.Y; saved.OriginLane = origin.Lane;
            }
            int cursor = Buffers.MergeCursor[index];
            if (cursor >= 0 && cursor < LaneCount)
            {
                BeltLaneId target = laneIds[cursor];
                saved.CursorX = target.X; saved.CursorY = target.Y; saved.CursorLane = target.Lane;
            }
            return saved;
        }

        internal bool RestoreLane(BeltSavedLane saved)
        {
            RequireCommittedStep();
            Complete();
            if (saved == null || !indices.TryGetValue(new BeltLaneId(saved.X, saved.Y, saved.Lane), out int index)) return false;
            BeltLaneState state = saved.State;
            state.Origin = saved.OriginLane >= 0 && indices.TryGetValue(
                new BeltLaneId(saved.OriginX, saved.OriginY, saved.OriginLane), out int origin) ? origin : -1;
            int groupIndex = FindGroup(index);
            var group = Buffers.Groups[groupIndex];
            if (saved.CursorLane >= 0 && indices.TryGetValue(
                new BeltLaneId(saved.CursorX, saved.CursorY, saved.CursorLane), out int cursor)
                && cursor >= group.Start && cursor < group.Start + group.Count)
                Buffers.MergeCursor[index] = cursor;
            Buffers.Lanes[index] = state;
            Buffers.GroupStates[groupIndex] = default;
            return true;
        }

        // Synchronous data ports reserve the lane immediately, before another producer can use it.
        internal bool TryInsert(BeltLaneId id, int itemId, long holdUnits = 0)
        {
            RequireCommittedStep();
            if (itemId < 0 || !TryRead(id, out var state) || state.ItemId >= 0) return false;
            int index = indices[id];
            long hold = Math.Max(0, holdUnits);
            Buffers.Lanes[index] = new BeltLaneState { ItemId = itemId, Origin = -1,
                Remaining = hold, Duration = hold, GateBits = hold > 0 ? 0 : BeltSimulationJob.SettledGateBit };
            Buffers.GroupStates[FindGroup(index)] = default;
            return true;
        }

        internal bool TryTakeSettled(BeltLaneId id, out int itemId)
        {
            RequireCommittedStep();
            itemId = -1;
            if (!TryRead(id, out var state) || state.ItemId < 0 || state.Remaining > 0) return false;
            int index = indices[id];
            itemId = state.ItemId;
            Buffers.Lanes[index] = BeltLaneState.Empty;
            Buffers.GroupStates[FindGroup(index)] = default;
            return true;
        }

        private void RequireCommittedStep()
        {
            if (stepOpen) throw new InvalidOperationException("Checkpoint/IO requires a committed transport tick.");
        }

        private int FindGroup(int lane)
        {
            int low = 0, high = Buffers.Groups.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                var group = Buffers.Groups[middle];
                if (lane < group.Start) high = middle - 1;
                else if (lane >= group.Start + group.Count) low = middle + 1;
                else return middle;
            }
            throw new InvalidOperationException("Lane has no transport group.");
        }

        public void Dispose()
        {
            Complete(); Buffers?.Dispose(); Buffers = null;
            indices.Clear(); laneIds.Clear(); Tick = 0; stepOpen = false;
        }
    }
}
