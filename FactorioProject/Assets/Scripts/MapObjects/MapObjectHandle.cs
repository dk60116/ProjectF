using System;

namespace ProjectF.MapObjects
{
    [Serializable]
    public readonly struct MapObjectHandle : IEquatable<MapObjectHandle>
    {
        internal MapObjectHandle(int typeId, int slot, uint generation, long simulationId)
        {
            TypeId = typeId;
            Slot = slot;
            Generation = generation;
            SimulationId = simulationId;
        }

        public int TypeId { get; }
        public int Slot { get; }
        public uint Generation { get; }
        public long SimulationId { get; }
        public bool IsValid => TypeId >= 0 && Slot > 0 && Generation != 0;

        public bool Equals(MapObjectHandle other)
        {
            return TypeId == other.TypeId
                   && Slot == other.Slot
                   && Generation == other.Generation
                   && SimulationId == other.SimulationId;
        }

        public override bool Equals(object obj)
        {
            return obj is MapObjectHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = TypeId;
                hash = (hash * 397) ^ Slot;
                hash = (hash * 397) ^ (int)Generation;
                hash = (hash * 397) ^ SimulationId.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return IsValid
                ? $"MapObject({TypeId}:{Slot}:{Generation}, Simulation={SimulationId})"
                : "MapObject(Invalid)";
        }

        public static bool operator ==(MapObjectHandle left, MapObjectHandle right) => left.Equals(right);
        public static bool operator !=(MapObjectHandle left, MapObjectHandle right) => !left.Equals(right);
    }
}
