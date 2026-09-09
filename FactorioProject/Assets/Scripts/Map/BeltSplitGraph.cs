using System;

namespace ProjectF.Conveyors
{
    // Weak components: a transfer in either direction means shared simulation state.
    // Vertex indices must be in coordinate order for deterministic representatives.
    public sealed class BeltSplitGraph
    {
        private int[] parents = Array.Empty<int>();
        private int[] sizes = Array.Empty<int>();
        private int[] representatives = Array.Empty<int>();
        public int GroupCount { get; private set; }

        public void Reset(int count)
        {
            if (parents.Length < count)
            {
                parents = new int[count];
                sizes = new int[count];
                representatives = new int[count];
            }
            GroupCount = count;
            for (int i = 0; i < count; i++)
            {
                parents[i] = representatives[i] = i;
                sizes[i] = 1;
            }
        }

        public void Connect(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a == b) return;
            if (sizes[a] < sizes[b]) { int swap = a; a = b; b = swap; }
            parents[b] = a;
            sizes[a] += sizes[b];
            representatives[a] = Math.Min(representatives[a], representatives[b]);
            GroupCount--;
        }

        public int Representative(int index) => representatives[Find(index)];

        private int Find(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }
    }
}
