using System;
using System.Collections;
using ProjectF.Conveyors;
using UnityEngine;

public partial class TerrainGenerator
{
    public BeltSimulationSnapshot CaptureBeltSimulationSnapshot()
    {
        BeltSimulationSnapshot result = null;
        IEnumerator capture = CaptureBeltSimulationSnapshotIncremental(
            snapshot => result = snapshot,
            int.MaxValue);
        while (capture.MoveNext()) { }
        return result ?? new BeltSimulationSnapshot { Tick = beltSimulationTick };
    }

    public IEnumerator CaptureBeltSimulationSnapshotIncremental(
        Action<BeltSimulationSnapshot> completed,
        int lanesPerFrame = 1024)
    {
        EnsureBeltJobs();
        FlushBeltJobWrites();
        lanesPerFrame = Mathf.Max(1, lanesPerFrame);
        var snapshot = new BeltSimulationSnapshot { Tick = beltSimulationTick };
        for (int i = 0; i < beltJobNodes.Count; i++)
        {
            // Occupied lanes are already saved with their item checkpoint in map.conveyorItems.
            if (beltSimulation.ReadLane(i).ItemId < 0)
            {
                var node = beltJobNodes[i];
                snapshot.Lanes.Add(beltSimulation.CaptureLane(node));
            }

            if ((i + 1) % lanesPerFrame == 0)
            {
                yield return null;
            }
        }

        completed?.Invoke(snapshot);
    }

    internal BeltSavedLane CaptureBeltJobLane(Block block, int lane)
    {
        if (beltJobBuffers == null || !beltJobIndices.TryGetValue(BeltId(block, lane), out int index)) return null;
        BeltLaneState state = beltSimulation.ReadLane(index);
        if (beltJobPending.TryGetValue(BeltId(block, lane), out BeltPendingWrite pending))
        {
            if (pending.Restore != null) return pending.Restore;
            state = block.CaptureBeltJobInput(
                lane,
                state,
                pending.Replace,
                pending.Hold,
                pending.UpdatePickupGate);
        }
        var saved = new BeltSavedLane { X = block.Coordinate.x, Y = block.Coordinate.y, Lane = lane, State = state };
        if (state.Origin >= 0 && state.Origin < beltJobNodes.Count)
        {
            var origin = beltJobNodes[state.Origin];
            saved.OriginX = origin.X; saved.OriginY = origin.Y; saved.OriginLane = origin.Lane;
        }
        int cursor = beltJobBuffers.MergeCursor[index];
        if (cursor >= 0 && cursor < beltJobNodes.Count)
        {
            var key = beltJobNodes[cursor];
            saved.CursorX = key.X; saved.CursorY = key.Y; saved.CursorLane = key.Lane;
        }
        return saved;
    }

    internal void QueueBeltJobRestore(Block block, int lane, BeltSavedLane checkpoint)
    {
        SetBeltJobPending(
            BeltId(block, lane),
            new BeltPendingWrite { Replace = true, Restore = checkpoint });
    }

    private BeltLaneState RestoreBeltJobLane(int index, BeltSavedLane checkpoint)
    {
        BeltLaneState state = checkpoint.State;
        state.Origin = -1;
        if (checkpoint.OriginLane >= 0 && beltSimulation.TryGetIndex(
            new BeltLaneId(checkpoint.OriginX, checkpoint.OriginY, checkpoint.OriginLane), out int source)) state.Origin = source;
        int group = beltJobGroupIds[index];
        if (checkpoint.CursorLane >= 0 && beltSimulation.TryGetIndex(
            new BeltLaneId(checkpoint.CursorX, checkpoint.CursorY, checkpoint.CursorLane), out int cursorIndex) && beltJobGroupIds[cursorIndex] == group)
            beltJobBuffers.MergeCursor[index] = cursorIndex;
        return state;
    }

    public void RestoreBeltSimulationSnapshot(BeltSimulationSnapshot snapshot)
    {
        if (snapshot == null) return;
        EnsureBeltJobs();
        beltJobPending.Clear();
        beltJobPendingIndices.Clear();
        beltJobUnindexedPending.Clear();
        beltSimulation.RestoreTick(snapshot.Tick);
        beltJobLastAdvancedWorldTick = MapObjectTickManager.CurrentSimulationTick;
        foreach (BeltSavedLane checkpoint in snapshot.Lanes)
        {
            if (checkpoint == null) continue;
            SetBeltJobPending(new BeltLaneId(checkpoint.X, checkpoint.Y, checkpoint.Lane),
                new BeltPendingWrite { Replace = true, Restore = checkpoint });
        }
        FlushBeltJobWrites();
    }
}
