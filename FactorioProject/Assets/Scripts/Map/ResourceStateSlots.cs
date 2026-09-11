using System;
using System.Collections.Generic;

namespace ProjectF.Runtime
{
    /// <summary>Resource state storage with lifetime checks, independent of Unity's object lifetime.</summary>
    internal sealed class ResourceStateSlots<T> where T : struct
    {
        internal readonly struct Slot
        {
            internal readonly int Index;
            internal readonly uint Generation;
            internal Slot(int index, uint generation) { Index = index; Generation = generation; }
        }

        private T[] states;
        private uint[] generations;
        private bool[] occupied;
        private readonly Stack<int> free = new Stack<int>();
        private int allocatedLength;
        internal int Count { get; private set; }

        internal ResourceStateSlots(int initialCapacity = 64)
        {
            int capacity = Math.Max(1, initialCapacity);
            states = new T[capacity]; generations = new uint[capacity]; occupied = new bool[capacity];
        }

        internal Slot Allocate(T initialState)
        {
            int index = free.Count > 0 ? free.Pop() : allocatedLength++;
            if (index >= states.Length)
            {
                int capacity = checked(states.Length * 2);
                Array.Resize(ref states, capacity); Array.Resize(ref generations, capacity); Array.Resize(ref occupied, capacity);
            }
            if (generations[index] == 0) generations[index] = 1;
            states[index] = initialState;
            occupied[index] = true;
            Count++;
            return new Slot(index, generations[index]);
        }

        internal bool Contains(int index, uint generation) => index >= 0 && index < allocatedLength
            && generation != 0 && occupied[index] && generations[index] == generation;

        internal ref T Get(int index, uint generation)
        {
            if (!Contains(index, generation)) throw new InvalidOperationException("Resource handle has expired.");
            return ref states[index];
        }

        internal bool Release(int index, uint generation)
        {
            if (!Contains(index, generation)) return false;
            occupied[index] = false;
            states[index] = default;
            Count--;
            // Retire the slot at the generation limit; wrapping could revive a stale handle.
            if (generations[index] == uint.MaxValue) return true;
            generations[index]++;
            free.Push(index);
            return true;
        }
    }
}
