using System.Collections.Generic;
using ProjectF.Conveyors;
using UnityEngine;

public partial class TerrainGenerator
{


    private bool RouteOwnedConveyorLineWake(ConveyorLine line, ConveyorLineWakeRange wakeRange)
    {
        if (line.transportRuns == null) return false;
        for (int r = 0; r < line.transportRuns.Count; r++)
            if (!line.transportRuns[r].Active) { ReleaseLineTransport(line); return false; }
        // A later event may cover another port. Preserve it for the next frame
        // instead of dispatching this line repeatedly or dropping the event.
        if (!conveyorLinesTickedThisFrame.Add(line.id))
        {
            DeferConveyorLineWake(line.id, wakeRange);
            return true;
        }
        ResolveStraightConveyorLineWakeRange(line, ref wakeRange, out int minSlot, out int maxSlot);
        List<int> slots = line.transportLegacySlots;
        int first = slots.BinarySearch(minSlot);
        if (first < 0) first = ~first;
        for (int i = first; i < slots.Count && slots[i] <= maxSlot; i++)
        {
            if (!TryResolveConveyorLineBlock(line, slots[i], out Block block)) continue;
            QueueConveyorDirectWake(block);
        }
        return true;
    }

    private static void ReleaseLineTransport(ConveyorLine line)
    {
        if (line.transportRuns != null)
            for (int i = 0; i < line.transportRuns.Count; i++) line.transportRuns[i].Release();
        line.transportRuns = null;
        line.transportLegacySlots = null;
    }

    private void ReleaseAllConveyorTransport()
    {
        for (int i = 0; i < conveyorLines.Count; i++) ReleaseLineTransport(conveyorLines[i]);
    }

    private int CountOwnedConveyorItems()
    {
        int count = 0;
        for (int l = 0; l < conveyorLines.Count; l++)
        {
            var runs = conveyorLines[l].transportRuns;
            if (runs == null) continue;
            for (int r = 0; r < runs.Count; r++) if (runs[r].Active) count += runs[r].Items.Count;
        }
        return count;
    }
}
