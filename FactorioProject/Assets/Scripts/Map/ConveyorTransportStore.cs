using System;

namespace ProjectF.Conveyors
{
    // Ordered item coordinates encode the gaps. A lazy translation changes the
    // common displacement without visiting items. A blocked suffix is represented
    // by one packed interval; splitting/merging touches only tree paths.
    internal sealed class ConveyorTransportStore<T>
    {
        private struct Node
        {
            internal int Left, Right, Count, NextFree;
            internal uint Priority;
            internal double Position, Min, Max, Shift, PackedStart;
            internal bool Packed;
            internal T Value;
        }

        private readonly Node[] nodes;
        private int root, free;
        private uint random = 0x9e3779b9;
        internal int Count => Size(root);
        internal long NodeVisits { get; private set; }
        internal long CommonMoves { get; private set; }
        internal double LastPosition => root == 0 ? double.NegativeInfinity : nodes[root].Max;
        internal double FirstPosition => root == 0 ? double.PositiveInfinity : nodes[root].Min;
        internal readonly double End;
        private const double Epsilon = 1e-7;

        internal ConveyorTransportStore(int capacity, double end)
        {
            if (capacity < 1 || double.IsNaN(end) || end < 0) throw new ArgumentOutOfRangeException();
            End = end;
            nodes = new Node[checked(capacity + 1)];
            free = 1;
            for (int i = 1; i < nodes.Length; i++) nodes[i].NextFree = i + 1 < nodes.Length ? i + 1 : 0;
        }

        internal bool TryInsert(double position, T value)
        {
            if (free == 0 || double.IsNaN(position) || position < -1 || position > End + Epsilon) return false;
            int rank = LowerBound(position, out int next);
            if (next != 0 && nodes[next].Position - position < 1 - Epsilon) return false;
            if (rank > 0 && position - PositionAt(rank - 1) < 1 - Epsilon) return false;
            int node = free;
            free = nodes[node].NextFree;
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            nodes[node] = new Node { Count = 1, Position = position, Min = position, Max = position, Value = value, Priority = random };
            Split(root, rank, out int left, out int right);
            root = Merge(Merge(left, node), right);
            return true;
        }

        internal bool TryGetLane(int lane, out T value, out double position)
        {
            value = default; position = 0;
            if (lane < 0 || lane > End) return false;
            int n = root, skipped = 0;
            while (n != 0)
            {
                NodeVisits++; Push(n);
                int rank = skipped + Size(nodes[n].Left);
                int reservedLane = GetReservedLane(rank, nodes[n].Position);
                if (reservedLane == lane) { value = nodes[n].Value; position = nodes[n].Position; return true; }
                if (reservedLane > lane) n = nodes[n].Left;
                else { skipped = rank + 1; n = nodes[n].Right; }
            }
            return false;
        }

        // A moving item reserves its next slot, even at the exact start of the
        // segment. Packed items stay at their occupied slot. This avoids a
        // one-frame entrance stall at every exact spacing/speed boundary and
        // still gives every item a unique legacy query slot without slot writes.
        internal int GetReservedLane(int rank, double position)
        {
            return Math.Max(0, (int)Math.Min(Math.Floor(position + Epsilon) + 1, End - (Count - 1 - rank)));
        }

        internal bool RemoveLane(int lane, out T value)
        {
            value = default;
            if (!TryGetLane(lane, out value, out double position)) return false;
            int rank = LowerBound(position - Epsilon * 0.5, out _);
            Split(root, rank, out int left, out int rest);
            Split(rest, 1, out int removed, out int right);
            root = Merge(left, right);
            nodes[removed] = new Node { NextFree = free };
            free = removed;
            return true;
        }

        internal bool SetLaneValue(int lane, T value)
        {
            if (!TryGetLane(lane, out _, out double position)) return false;
            LowerBound(position - Epsilon * 0.5, out int found);
            nodes[found].Value = value;
            return true;
        }

        internal bool Advance(double distance)
        {
            if (root == 0 || distance <= 0 || double.IsNaN(distance) || double.IsInfinity(distance)) return false;
            // Entirely compressed against the exit: even the tree search sleeps.
            if (nodes[root].Min >= End - Count + 1 - Epsilon) return false;
            CommonMoves++;
            Translate(root, distance);
            if (nodes[root].Max <= End + Epsilon) return true;
            double packedStart = End - Count + 1;
            int firstBlocked = FirstBeyondPacked(root, packedStart);
            Split(root, firstBlocked, out int moving, out int blocked);
            Pack(blocked, packedStart + firstBlocked);
            root = Merge(moving, blocked);
            return true;
        }

        internal bool TryGetAt(int rank, out T value, out double position)
        {
            value = default; position = 0;
            if (rank < 0 || rank >= Count) return false;
            int n = root;
            while (n != 0)
            {
                NodeVisits++; Push(n);
                int left = Size(nodes[n].Left);
                if (rank == left) { value = nodes[n].Value; position = nodes[n].Position; return true; }
                if (rank < left) n = nodes[n].Left;
                else { rank -= left + 1; n = nodes[n].Right; }
            }
            return false;
        }

        private double PositionAt(int rank) { TryGetAt(rank, out _, out double position); return position; }
        private int Size(int n) => n == 0 ? 0 : nodes[n].Count;
        private void Translate(int n, double distance)
        {
            if (n == 0) return;
            nodes[n].Position += distance; nodes[n].Min += distance; nodes[n].Max += distance;
            if (nodes[n].Packed) nodes[n].PackedStart += distance;
            else nodes[n].Shift += distance;
        }
        private void Pack(int n, double start)
        {
            if (n == 0) return;
            nodes[n].Packed = true; nodes[n].PackedStart = start; nodes[n].Shift = 0;
            nodes[n].Position = start + Size(nodes[n].Left);
            nodes[n].Min = start; nodes[n].Max = start + Size(n) - 1;
        }
        private void Push(int n)
        {
            if (nodes[n].Packed)
            {
                Pack(nodes[n].Left, nodes[n].PackedStart);
                Pack(nodes[n].Right, nodes[n].Position + 1);
                nodes[n].Packed = false;
            }
            if (nodes[n].Shift != 0)
            {
                Translate(nodes[n].Left, nodes[n].Shift); Translate(nodes[n].Right, nodes[n].Shift);
                nodes[n].Shift = 0;
            }
        }
        private void Pull(int n)
        {
            nodes[n].Count = Size(nodes[n].Left) + Size(nodes[n].Right) + 1;
            nodes[n].Min = nodes[n].Left == 0 ? nodes[n].Position : nodes[nodes[n].Left].Min;
            nodes[n].Max = nodes[n].Right == 0 ? nodes[n].Position : nodes[nodes[n].Right].Max;
        }
        private int LowerBound(double position, out int found)
        {
            int n = root, skipped = 0, rank = Count;
            found = 0;
            while (n != 0)
            {
                NodeVisits++; Push(n);
                if (nodes[n].Position >= position) { found = n; rank = skipped + Size(nodes[n].Left); n = nodes[n].Left; }
                else { skipped += Size(nodes[n].Left) + 1; n = nodes[n].Right; }
            }
            return rank;
        }
        private int FirstBeyondPacked(int n, double start)
        {
            if (n == 0) return 0;
            NodeVisits++; Push(n);
            int left = Size(nodes[n].Left);
            if (nodes[n].Position > start + left)
                return FirstBeyondPacked(nodes[n].Left, start);
            return left + 1 + FirstBeyondPacked(nodes[n].Right, start + left + 1);
        }
        private void Split(int n, int count, out int left, out int right)
        {
            if (n == 0) { left = right = 0; return; }
            NodeVisits++; Push(n);
            if (Size(nodes[n].Left) >= count)
            {
                Split(nodes[n].Left, count, out left, out int middle);
                nodes[n].Left = middle; Pull(n); right = n;
            }
            else
            {
                Split(nodes[n].Right, count - Size(nodes[n].Left) - 1, out int middle, out right);
                nodes[n].Right = middle; Pull(n); left = n;
            }
        }
        private int Merge(int left, int right)
        {
            if (left == 0) return right;
            if (right == 0) return left;
            NodeVisits++;
            if (nodes[left].Priority < nodes[right].Priority)
            {
                Push(left); nodes[left].Right = Merge(nodes[left].Right, right); Pull(left); return left;
            }
            Push(right); nodes[right].Left = Merge(left, nodes[right].Left); Pull(right); return right;
        }
    }
}
