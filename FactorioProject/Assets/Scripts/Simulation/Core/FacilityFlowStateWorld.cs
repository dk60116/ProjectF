using System;

namespace ProjectF.Simulation
{
    public enum FacilityFlowEntityKind : byte
    {
        None = 0,
        Pump = 1,
        Boiler = 2,
        SteamGenerator = 3
    }

    /// <summary>
    /// Stable sparse handle into a type-dense facility state array. The generation
    /// prevents a pooled or destroyed view from accessing a reused entity slot.
    /// </summary>
    public readonly struct FacilityFlowEntityHandle : IEquatable<FacilityFlowEntityHandle>
    {
        public FacilityFlowEntityHandle(FacilityFlowEntityKind kind, int index, uint generation)
        {
            Kind = kind;
            Index = index;
            Generation = generation;
        }

        public FacilityFlowEntityKind Kind { get; }
        public int Index { get; }
        public uint Generation { get; }
        public bool IsValid => Kind != FacilityFlowEntityKind.None && Index >= 0 && Generation != 0;

        public bool Equals(FacilityFlowEntityHandle other)
        {
            return Kind == other.Kind && Index == other.Index && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is FacilityFlowEntityHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ Index;
                return (hash * 397) ^ (int)Generation;
            }
        }
    }

    public struct PumpFlowState
    {
        public long WaterAccumulatorUnits;
        public long AvailableOutputUnits;
        public long OutputBudgetUpdatedTick;
        public bool OutputBlocked;

        public static PumpFlowState CreateDefault()
        {
            return new PumpFlowState { OutputBudgetUpdatedTick = -1L };
        }
    }

    public struct BoilerFlowState
    {
        public float WaterTemperatureCelsius;
        public bool PreserveSteamReadyTemperatureForMakeupWater;
        public long AvailableSteamOutputUnits;
        public long SteamOutputBudgetUpdatedTick;

        public static BoilerFlowState CreateDefault()
        {
            return new BoilerFlowState { SteamOutputBudgetUpdatedTick = -1L };
        }
    }

    public struct SteamGeneratorFlowState
    {
        public bool IsGenerating;
    }

    /// <summary>
    /// Implemented by Unity-facing adapters so their ECS entity can outlive a
    /// temporary disable but is released when the view is pooled or destroyed.
    /// </summary>
    public interface IFacilityFlowStateOwner
    {
        void EnsureFacilityFlowState();
        void ReleaseFacilityFlowState();
    }

    /// <summary>
    /// Persistent calculation state for fluid facilities. Each type is packed in
    /// a dense array; MonoBehaviours retain only a generation-checked sparse handle.
    /// </summary>
    public static class FacilityFlowStateWorld
    {
        private static readonly PackedStateStore<PumpFlowState> pumps =
            new PackedStateStore<PumpFlowState>(FacilityFlowEntityKind.Pump, 64);
        private static readonly PackedStateStore<BoilerFlowState> boilers =
            new PackedStateStore<BoilerFlowState>(FacilityFlowEntityKind.Boiler, 64);
        private static readonly PackedStateStore<SteamGeneratorFlowState> steamGenerators =
            new PackedStateStore<SteamGeneratorFlowState>(FacilityFlowEntityKind.SteamGenerator, 64);

        public static int PumpCount => pumps.Count;
        public static int BoilerCount => boilers.Count;
        public static int SteamGeneratorCount => steamGenerators.Count;

        public static FacilityFlowEntityHandle CreatePump()
        {
            return pumps.Allocate(PumpFlowState.CreateDefault());
        }

        public static bool ContainsPump(FacilityFlowEntityHandle handle)
        {
            return pumps.Contains(handle);
        }

        public static ref PumpFlowState GetPump(FacilityFlowEntityHandle handle)
        {
            return ref pumps.Get(handle);
        }

        public static void ReleasePump(ref FacilityFlowEntityHandle handle)
        {
            pumps.Release(handle);
            handle = default;
        }

        public static FacilityFlowEntityHandle CreateBoiler()
        {
            return boilers.Allocate(BoilerFlowState.CreateDefault());
        }

        public static bool ContainsBoiler(FacilityFlowEntityHandle handle)
        {
            return boilers.Contains(handle);
        }

        public static ref BoilerFlowState GetBoiler(FacilityFlowEntityHandle handle)
        {
            return ref boilers.Get(handle);
        }

        public static void ReleaseBoiler(ref FacilityFlowEntityHandle handle)
        {
            boilers.Release(handle);
            handle = default;
        }

        public static FacilityFlowEntityHandle CreateSteamGenerator()
        {
            return steamGenerators.Allocate(default);
        }

        public static bool ContainsSteamGenerator(FacilityFlowEntityHandle handle)
        {
            return steamGenerators.Contains(handle);
        }

        public static ref SteamGeneratorFlowState GetSteamGenerator(FacilityFlowEntityHandle handle)
        {
            return ref steamGenerators.Get(handle);
        }

        public static void ReleaseSteamGenerator(ref FacilityFlowEntityHandle handle)
        {
            steamGenerators.Release(handle);
            handle = default;
        }

        public static void Clear()
        {
            pumps.Clear();
            boilers.Clear();
            steamGenerators.Clear();
        }

        private sealed class PackedStateStore<T> where T : struct
        {
            private readonly FacilityFlowEntityKind kind;
            private T[] denseStates;
            private int[] denseToSparse;
            private SparseSlot[] sparseSlots;
            private int denseCount;
            private int sparseCount;
            private int freeSparseHead = -1;
            private uint nextGeneration = 1;

            internal PackedStateStore(FacilityFlowEntityKind kind, int initialCapacity)
            {
                this.kind = kind;
                int capacity = Math.Max(4, initialCapacity);
                denseStates = new T[capacity];
                denseToSparse = new int[capacity];
                sparseSlots = new SparseSlot[capacity];
            }

            internal int Count => denseCount;

            internal FacilityFlowEntityHandle Allocate(T initialState)
            {
                EnsureDenseCapacity(denseCount + 1);
                int sparseIndex;
                if (freeSparseHead >= 0)
                {
                    sparseIndex = freeSparseHead;
                    freeSparseHead = sparseSlots[sparseIndex].NextFree;
                }
                else
                {
                    EnsureSparseCapacity(sparseCount + 1);
                    sparseIndex = sparseCount++;
                }

                uint generation = AllocateGeneration();
                sparseSlots[sparseIndex] = new SparseSlot(denseCount, -1, generation, true);
                denseStates[denseCount] = initialState;
                denseToSparse[denseCount] = sparseIndex;
                denseCount++;
                return new FacilityFlowEntityHandle(kind, sparseIndex, generation);
            }

            internal bool Contains(FacilityFlowEntityHandle handle)
            {
                return handle.Kind == kind
                       && handle.Index >= 0
                       && handle.Index < sparseCount
                       && sparseSlots[handle.Index].Allocated
                       && sparseSlots[handle.Index].Generation == handle.Generation;
            }

            internal ref T Get(FacilityFlowEntityHandle handle)
            {
                if (!Contains(handle))
                {
                    throw new InvalidOperationException("Facility flow entity handle is stale or has the wrong type.");
                }

                return ref denseStates[sparseSlots[handle.Index].DenseIndex];
            }

            internal void Release(FacilityFlowEntityHandle handle)
            {
                if (!Contains(handle)) return;

                int sparseIndex = handle.Index;
                int denseIndex = sparseSlots[sparseIndex].DenseIndex;
                int lastDenseIndex = --denseCount;
                if (denseIndex != lastDenseIndex)
                {
                    denseStates[denseIndex] = denseStates[lastDenseIndex];
                    int movedSparseIndex = denseToSparse[lastDenseIndex];
                    denseToSparse[denseIndex] = movedSparseIndex;
                    SparseSlot movedSlot = sparseSlots[movedSparseIndex];
                    movedSlot.DenseIndex = denseIndex;
                    sparseSlots[movedSparseIndex] = movedSlot;
                }

                denseStates[lastDenseIndex] = default;
                denseToSparse[lastDenseIndex] = 0;
                sparseSlots[sparseIndex] = new SparseSlot(-1, freeSparseHead, handle.Generation, false);
                freeSparseHead = sparseIndex;
            }

            internal void Clear()
            {
                Array.Clear(denseStates, 0, denseCount);
                Array.Clear(denseToSparse, 0, denseCount);
                Array.Clear(sparseSlots, 0, sparseCount);
                denseCount = 0;
                sparseCount = 0;
                freeSparseHead = -1;
            }

            private uint AllocateGeneration()
            {
                uint generation = nextGeneration++;
                if (generation != 0) return generation;
                generation = nextGeneration++;
                return generation == 0 ? 1u : generation;
            }

            private void EnsureDenseCapacity(int required)
            {
                if (required <= denseStates.Length) return;
                int capacity = Math.Max(required, denseStates.Length * 2);
                Array.Resize(ref denseStates, capacity);
                Array.Resize(ref denseToSparse, capacity);
            }

            private void EnsureSparseCapacity(int required)
            {
                if (required <= sparseSlots.Length) return;
                Array.Resize(ref sparseSlots, Math.Max(required, sparseSlots.Length * 2));
            }

            private struct SparseSlot
            {
                internal int DenseIndex;
                internal int NextFree;
                internal uint Generation;
                internal bool Allocated;

                internal SparseSlot(int denseIndex, int nextFree, uint generation, bool allocated)
                {
                    DenseIndex = denseIndex;
                    NextFree = nextFree;
                    Generation = generation;
                    Allocated = allocated;
                }
            }
        }
    }
}
