using System.Collections.Generic;
using ProjectF.Conveyors;
using UnityEngine;

public partial class TerrainGenerator
{
    private int lastTransportRuns, lastTransportItems, lastTransportLegacyBlocks, lastTransportRoutedBlockWakes;
    private long lastTransportCommonMoves, lastTransportTreeVisits;
    private long lastTransportBoundaryChecks, lastTransportInputAttempts, lastTransportOutputAttempts;
    private int lastTransportSleepingRuns, lastTransportRebuilds;
    private readonly List<Block> conveyorTransportBuildBlocks = new List<Block>();

    private void TickOwnedConveyorRuns()
    {
        lastTransportRuns = lastTransportItems = lastTransportLegacyBlocks = lastTransportRoutedBlockWakes = 0;
        lastTransportCommonMoves = lastTransportTreeVisits = 0;
        lastTransportBoundaryChecks = lastTransportInputAttempts = lastTransportOutputAttempts = 0;
        lastTransportSleepingRuns = lastTransportRebuilds = 0;
        for (int l = 0; l < conveyorLines.Count; l++)
        {
            ConveyorLine line = conveyorLines[l];
            if (!line.simulationCacheValid || line.blockHandles.Count < 4) continue;
            bool proxyChanged = line.transportProxyVersion != loadedBlocks.RuntimeProxyVersion;
            bool rebuild = false;
            if (line.transportRuns != null)
                for (int r = 0; r < line.transportRuns.Count; r++)
                {
                    ConveyorTransportRun run = line.transportRuns[r];
                    rebuild |= !run.Active || (proxyChanged && !AreTransportBindingsLoaded(run));
                }
            // Unrelated proxy creation/destruction must not discard deadlines
            // or export/reimport the line. Validate bindings only on that event.
            line.transportProxyVersion = loadedBlocks.RuntimeProxyVersion;
            if (rebuild) { ReleaseLineTransport(line); line.transportRetryTime = 0; lastTransportRebuilds++; }
            if (line.transportRuns == null && Time.time >= line.transportRetryTime) BuildLineTransport(line);
            if (line.transportRuns == null) continue;
            lastTransportLegacyBlocks += line.transportLegacySlots.Count;
            for (int r = 0; r < line.transportRuns.Count; r++)
            {
                ConveyorTransportRun run = line.transportRuns[r];
                if (!run.Active) continue;
                long moves = run.Items.CommonMoves, visits = run.Items.NodeVisits;
                long checks = run.BoundaryChecks, inputs = run.InputAttempts, outputs = run.OutputAttempts;
                run.TickBoundaries();
                lastTransportRuns++;
                lastTransportItems += run.Items.Count;
                lastTransportCommonMoves += run.Items.CommonMoves - moves;
                lastTransportTreeVisits += run.Items.NodeVisits - visits;
                lastTransportBoundaryChecks += run.BoundaryChecks - checks;
                lastTransportInputAttempts += run.InputAttempts - inputs;
                lastTransportOutputAttempts += run.OutputAttempts - outputs;
                if (double.IsPositiveInfinity(run.NextBoundaryTime)) lastTransportSleepingRuns++;
            }
        }
    }

    private bool AreTransportBindingsLoaded(ConveyorTransportRun run)
    {
        if (!IsLoadedRuntimeBlock(run.Inlet) || !IsLoadedRuntimeBlock(run.Outlet)) return false;
        for (int b = 0; b < run.Blocks.Length; b++)
            if (!IsLoadedRuntimeBlock(run.Blocks[b])) return false;
        return true;
    }

    private void BuildLineTransport(ConveyorLine line)
    {
        int count = line.blockHandles.Count;
        var blocks = conveyorTransportBuildBlocks;
        blocks.Clear();
        for (int i = 0; i < count; i++)
        {
            if (!TryResolveConveyorLineBlock(line, i, out Block block)) { blocks.Clear(); line.transportRetryTime = Time.time + 1; return; }
            blocks.Add(block);
        }
        List<ConveyorTransportRun> runs = null;
        int start = 0;
        while (start < count)
        {
            if (!blocks[start].CanOwnConveyorTransport) { start++; continue; }
            float speed = blocks[start].RuntimeConveyorSpeed;
            float spacing = line.withinPathLengths[start];
            int end = start + 1;
            while (end < count && blocks[end].CanOwnConveyorTransport
                && Mathf.Abs(blocks[end].RuntimeConveyorSpeed - speed) < 0.00001f
                && Mathf.Abs(line.withinPathLengths[end] - spacing) < 0.00001f
                && Mathf.Abs(line.nextPathLengths[end - 1] - spacing) < 0.00001f) end++;
            if (end - start >= 4 && speed > 0 && spacing > 0.00001f)
            {
                int size = end - start - 2;
                var owned = new Block[size]; var front = new int[size]; var back = new int[size];
                for (int i = 0; i < size; i++)
                {
                    owned[i] = blocks[start + i + 1];
                    front[i] = line.frontLaneIndices[start + i + 1];
                    back[i] = line.backLaneIndices[start + i + 1];
                }
                var run = new ConveyorTransportRun(owned, front, back,
                    blocks[start], line.frontLaneIndices[start], blocks[end - 1], line.backLaneIndices[end - 1], speed, spacing);
                if (run.Adopt()) (runs ??= new List<ConveyorTransportRun>()).Add(run);
            }
            start = end;
        }
        line.transportProxyVersion = loadedBlocks.RuntimeProxyVersion;
        line.transportRetryTime = Time.time + 1;
        if (runs == null) { blocks.Clear(); return; }
        line.transportRuns = runs;
        line.transportLegacySlots = new List<int>();
        for (int i = 0; i < count; i++)
            if (!blocks[i].OwnsConveyorTransport) line.transportLegacySlots.Add(i);
        blocks.Clear();
        ClearStraightConveyorLineRetry(line.id);
    }

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
            lastTransportRoutedBlockWakes++;
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
        conveyorTransportBuildBlocks.Clear();
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
