using System;
using System.Collections.Generic;

namespace ProjectF.Simulation
{
    // Single-threaded membership; a copied tick snapshot isolates Plan/Apply from wakes.
    public sealed class ActiveTickSet<T> where T : class
    {
        private readonly List<T> active = new List<T>();
        private readonly Dictionary<T, int> indices = new Dictionary<T, int>();
        private readonly Comparison<T> comparison;
        private bool orderDirty;

        public ActiveTickSet(IComparer<T> comparer)
        { comparison = (comparer ?? throw new ArgumentNullException(nameof(comparer))).Compare; }

        public int Count => active.Count;

        public bool Add(T target)
        {
            if (indices.ContainsKey(target)) return false;
            indices.Add(target, active.Count);
            active.Add(target);
            orderDirty = true;
            return true;
        }

        public bool Remove(T target)
        {
            if (!indices.TryGetValue(target, out int index)) return false;
            int last = active.Count - 1;
            if (index != last)
            {
                T moved = active[last];
                active[index] = moved;
                indices[moved] = index;
                orderDirty = true;
            }
            active.RemoveAt(last);
            indices.Remove(target);
            return true;
        }

        public void CopyOrderedTo(List<T> snapshot)
        {
            if (orderDirty)
            {
                active.Sort(comparison);
                for (int i = 0; i < active.Count; i++) indices[active[i]] = i;
                orderDirty = false;
            }
            snapshot.Clear();
            snapshot.AddRange(active);
        }

        public void Clear()
        { active.Clear(); indices.Clear(); orderDirty = false; }
    }
}
