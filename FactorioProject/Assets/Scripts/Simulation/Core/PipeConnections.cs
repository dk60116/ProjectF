using System;

namespace ProjectF.Simulation
{
    public readonly struct GridCell : IEquatable<GridCell>
    {
        public readonly int X, Y;
        public GridCell(int x, int y) { X = x; Y = y; }
        public bool Equals(GridCell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridCell other && Equals(other);
        public override int GetHashCode() { unchecked { return X * 397 ^ Y; } }
    }

    public readonly struct PipeEndpoint
    {
        public readonly GridCell Cell;
        // Up, right, down, left. Mask is baked from the authored definition once.
        public readonly byte Connections;
        public PipeEndpoint(GridCell cell, byte connections) { Cell = cell; Connections = connections; }
    }

    public sealed class PipeConnections
    {
        private readonly PipeEndpoint[] endpoints;
        private readonly bool underground;
        public PipeConnections(PipeEndpoint[] endpoints, bool underground)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
            if (underground && endpoints.Length != 2) throw new ArgumentException("An underground pipe needs two endpoints.");
            this.endpoints = (PipeEndpoint[])endpoints.Clone();
            this.underground = underground;
        }
        public bool HasConnection(GridCell cell, int dx, int dy)
        {
            int mask = dx == 0 && dy == 1 ? 1 : dx == 1 && dy == 0 ? 2
                : dx == 0 && dy == -1 ? 4 : dx == -1 && dy == 0 ? 8 : 0;
            for (int i = 0; i < endpoints.Length; i++)
                if (endpoints[i].Cell.Equals(cell)) return (endpoints[i].Connections & mask) != 0;
            return false;
        }
        public bool TryGetRemote(GridCell cell, out GridCell remote)
        {
            remote = default;
            if (!underground) return false;
            if (endpoints[0].Cell.Equals(cell)) { remote = endpoints[1].Cell; return true; }
            if (endpoints[1].Cell.Equals(cell)) { remote = endpoints[0].Cell; return true; }
            return false;
        }
    }
}
