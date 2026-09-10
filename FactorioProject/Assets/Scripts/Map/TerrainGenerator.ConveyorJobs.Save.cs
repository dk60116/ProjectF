using ProjectF.Conveyors;
using UnityEngine;

public partial class TerrainGenerator
{
    public BeltSimulationSnapshot CaptureBeltSimulationSnapshot()
    {
        EnsureBeltJobs();
        FlushBeltJobWrites();
        var snapshot = new BeltSimulationSnapshot { Tick = beltSimulationTick };
        for (int i = 0; i < beltJobNodes.Count; i++)
        {
            // Occupied lanes are already saved with their item checkpoint in map.conveyorItems.
            if (beltJobBuffers.Lanes[i].ItemId >= 0) continue;
            var node = beltJobNodes[i];
            snapshot.Lanes.Add(CaptureBeltJobLane(node.block, node.lane));
        }
        return snapshot;
    }

    internal BeltSavedLane CaptureBeltJobLane(Block block, int lane)
    {
        if (beltJobBuffers == null || !beltJobIndices.TryGetValue((block, lane), out int index)) return null;
        BeltLaneState state = beltJobBuffers.Lanes[index];
        if (beltJobPending.TryGetValue((block, lane), out BeltPendingWrite pending))
        {
            if (pending.Restore != null) return pending.Restore;
            state = block.CaptureBeltJobInput(lane, state, pending.Replace, pending.Hold);
        }
        var saved = new BeltSavedLane { X = block.Coordinate.x, Y = block.Coordinate.y, Lane = lane, State = state };
        if (state.Origin >= 0 && state.Origin < beltJobNodes.Count)
        {
            var origin = beltJobNodes[state.Origin];
            saved.OriginX = origin.block.Coordinate.x; saved.OriginY = origin.block.Coordinate.y; saved.OriginLane = origin.lane;
        }
        int cursor = beltJobBuffers.MergeCursor[index];
        if (cursor >= 0 && cursor < beltJobNodes.Count)
        {
            var key = beltJobNodes[cursor];
            saved.CursorX = key.block.Coordinate.x; saved.CursorY = key.block.Coordinate.y; saved.CursorLane = key.lane;
        }
        return saved;
    }

    internal void QueueBeltJobRestore(Block block, int lane, BeltSavedLane checkpoint)
    {
        beltJobPending[(block, lane)] = new BeltPendingWrite { Replace = true, Restore = checkpoint };
    }

    private BeltLaneState RestoreBeltJobLane(int index, BeltSavedLane checkpoint)
    {
        BeltLaneState state = checkpoint.State;
        state.Origin = -1;
        if (checkpoint.OriginLane >= 0 && TryGetLoadedBlock(new Vector2Int(checkpoint.OriginX, checkpoint.OriginY), out Block origin)
            && beltJobIndices.TryGetValue((origin, checkpoint.OriginLane), out int source)) state.Origin = source;
        int group = beltJobGroupIds[index];
        if (checkpoint.CursorLane >= 0 && TryGetLoadedBlock(new Vector2Int(checkpoint.CursorX, checkpoint.CursorY), out Block cursor)
            && beltJobIndices.TryGetValue((cursor, checkpoint.CursorLane), out int cursorIndex) && beltJobGroupIds[cursorIndex] == group)
            beltJobBuffers.MergeCursor[index] = cursorIndex;
        return state;
    }

    public void RestoreBeltSimulationSnapshot(BeltSimulationSnapshot snapshot)
    {
        if (snapshot == null) return;
        EnsureBeltJobs();
        beltJobPending.Clear();
        beltSimulationTick = snapshot.Tick;
        foreach (BeltSavedLane checkpoint in snapshot.Lanes)
        {
            if (checkpoint == null || !TryGetLoadedBlock(new Vector2Int(checkpoint.X, checkpoint.Y), out Block block)) continue;
            QueueBeltJobRestore(block, checkpoint.Lane, checkpoint);
        }
        FlushBeltJobWrites();
    }
}
