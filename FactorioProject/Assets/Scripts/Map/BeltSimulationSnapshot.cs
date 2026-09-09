using System;
using System.Collections.Generic;
using System.IO;

namespace ProjectF.Conveyors
{
    [Serializable]
    public sealed class BeltSavedLane
    {
        public int X, Y, Lane;
        public BeltLaneState State;
        public int OriginX, OriginY, OriginLane = -1;
        public int CursorX, CursorY, CursorLane = -1;
        public BeltSavedLane Clone() => (BeltSavedLane)MemberwiseClone();
    }

    [Serializable]
    public sealed class BeltSimulationSnapshot
    {
        public long Tick;
        public List<BeltSavedLane> Lanes = new List<BeltSavedLane>();

        public static void Write(BinaryWriter writer, BeltSimulationSnapshot snapshot)
        {
            writer.Write(snapshot != null);
            if (snapshot == null) return;
            writer.Write(snapshot.Tick);
            writer.Write(snapshot.Lanes.Count);
            foreach (BeltSavedLane lane in snapshot.Lanes) WriteLane(writer, lane);
        }

        public static BeltSimulationSnapshot Read(BinaryReader reader)
        {
            if (!reader.ReadBoolean()) return null;
            var snapshot = new BeltSimulationSnapshot { Tick = reader.ReadInt64() };
            int count = reader.ReadInt32();
            if (snapshot.Tick < 0 || count < 0 || count > 4000000) throw new InvalidDataException("Invalid belt snapshot size or tick.");
            for (int i = 0; i < count; i++) snapshot.Lanes.Add(ReadLane(reader));
            return snapshot;
        }

        public static void WriteLane(BinaryWriter writer, BeltSavedLane lane)
        {
            writer.Write(lane != null);
            if (lane == null) return;
            writer.Write(lane.X); writer.Write(lane.Y); writer.Write(lane.Lane);
            writer.Write(lane.OriginX); writer.Write(lane.OriginY); writer.Write(lane.OriginLane);
            writer.Write(lane.CursorX); writer.Write(lane.CursorY); writer.Write(lane.CursorLane);
            BeltLaneState state = lane.State;
            writer.Write(state.ItemId); writer.Write(state.GateBits);
            writer.Write(state.Remaining); writer.Write(state.Duration);
            writer.Write(state.StartX); writer.Write(state.StartY); writer.Write(state.StartZ);
            writer.Write(state.DropX); writer.Write(state.DropY); writer.Write(state.DropZ); writer.Write(state.ExitRadius);
        }

        public static BeltSavedLane ReadLane(BinaryReader reader)
        {
            if (!reader.ReadBoolean()) return null;
            var lane = new BeltSavedLane
            {
                X = reader.ReadInt32(), Y = reader.ReadInt32(), Lane = reader.ReadInt32(),
                OriginX = reader.ReadInt32(), OriginY = reader.ReadInt32(), OriginLane = reader.ReadInt32(),
                CursorX = reader.ReadInt32(), CursorY = reader.ReadInt32(), CursorLane = reader.ReadInt32()
            };
            lane.State = new BeltLaneState
            {
                Origin = -1, ItemId = reader.ReadInt32(), GateBits = reader.ReadInt32(),
                Remaining = reader.ReadInt64(), Duration = reader.ReadInt64(),
                StartX = reader.ReadSingle(), StartY = reader.ReadSingle(), StartZ = reader.ReadSingle(),
                DropX = reader.ReadSingle(), DropY = reader.ReadSingle(), DropZ = reader.ReadSingle(), ExitRadius = reader.ReadSingle()
            };
            if (lane.Lane < 0 || lane.Lane >= 4 || lane.State.ItemId < -1 || lane.State.Remaining < 0 || lane.State.Duration < 0)
                throw new InvalidDataException("Invalid belt lane checkpoint.");
            return lane;
        }
    }
}
